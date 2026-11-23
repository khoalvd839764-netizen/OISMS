using System.Text;
using System.Text.Json;
using Npgsql;
using OISM.Application.Features.Ai.Services;

namespace Oism.ForecastEval;

internal static class Program
{
    private const decimal WapeThreshold = 0.35m;
    private const decimal RecallThreshold = 0.80m;

    private sealed record TxRow(
        Guid BranchId,
        string BranchName,
        Guid SkuId,
        string SkuCode,
        string UnitName,
        string Type,
        decimal Quantity,
        decimal BalanceAfter,
        long SequenceNo,
        DateOnly LocalDate);

    private sealed record EvalPoint(
        DateOnly ForecastDate,
        Guid BranchId,
        string BranchName,
        Guid SkuId,
        string SkuCode,
        string Group,
        int ActiveDays30,
        decimal AvailableAtT,
        decimal ReorderPoint,
        decimal ForecastHorizonQty,
        decimal ActualHorizonQty,
        decimal AbsError,
        bool PredictedNeedsReorder,
        bool ActualNeedsReorder,
        bool HasStockoutInHorizon);

    private sealed record GroupSummary(
        string Group,
        int SampleCount,
        decimal TotalActualQty,
        decimal TotalForecastQty,
        decimal TotalAbsError,
        decimal? Wape,
        int ActualNeedCount,
        int TruePositiveNeedCount,
        decimal? Recall,
        int PredictedNeedCount,
        decimal? Precision);

    public static async Task<int> Main(string[] args)
    {
        var tenantName = GetArg(args, "--tenant") ?? "FORECAST-EVAL";
        var leadTimeDays = int.TryParse(GetArg(args, "--lead-time"), out var lt) ? lt : 7;
        var safetyDays = int.TryParse(GetArg(args, "--safety-days"), out var sd) ? sd : 3;
        var stepDays = int.TryParse(GetArg(args, "--step-days"), out var st) ? st : 7;
        var outDir = GetArg(args, "--out-dir") ?? Directory.GetCurrentDirectory();
        var commitHash = GetArg(args, "--commit") ?? "unknown";

        var connString = Environment.GetEnvironmentVariable("OISM_EVAL_CONNECTION")
            ?? BuildDefaultConnectionString();

        var csb = new NpgsqlConnectionStringBuilder(connString);
        var host = csb.Host?.Trim() ?? string.Empty;
        if (!string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"[BLOCKED] Host DB '{host}' không phải localhost/127.0.0.1. Từ chối chạy forecast-eval.");
            return 2;
        }

        await using var conn = new NpgsqlConnection(csb.ConnectionString);
        await conn.OpenAsync();

        // Chế độ chỉ đọc tuyệt đối
        await using (var roCmd = new NpgsqlCommand("SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY;", conn))
        {
            await roCmd.ExecuteNonQueryAsync();
        }

        Guid tenantId;
        await using (var tCmd = new NpgsqlCommand("SELECT id FROM tenants WHERE name = @name LIMIT 1;", conn))
        {
            tCmd.Parameters.AddWithValue("name", tenantName);
            var obj = await tCmd.ExecuteScalarAsync();
            if (obj is not Guid id)
            {
                Console.Error.WriteLine($"Không tìm thấy tenant '{tenantName}' trong database '{csb.Database}'.");
                return 1;
            }
            tenantId = id;
        }

        var reorderPoints = new Dictionary<(Guid BranchId, Guid SkuId), decimal>();
        await using (var bCmd = new NpgsqlCommand(
            "SELECT branch_id, sku_id, reorder_point FROM inventory_balances WHERE tenant_id = @tid;", conn))
        {
            bCmd.Parameters.AddWithValue("tid", tenantId);
            await using var r = await bCmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                reorderPoints[(r.GetGuid(0), r.GetGuid(1))] = r.GetDecimal(2);
            }
        }

        var txRows = new List<TxRow>();
        const string txSql = @"
            SELECT it.branch_id, b.name, it.sku_id, s.code, s.unit, it.type, it.quantity, it.balance_after, it.sequence_no,
                   (it.created_at AT TIME ZONE 'Asia/Ho_Chi_Minh')::date AS local_date
            FROM inventory_transactions it
            JOIN branches b ON b.tenant_id = it.tenant_id AND b.id = it.branch_id
            JOIN skus s ON s.tenant_id = it.tenant_id AND s.id = it.sku_id
            WHERE it.tenant_id = @tid
              AND s.is_active = TRUE
              AND (it.type <> 'OUT' OR it.transfer_item_id IS NULL)
            ORDER BY it.branch_id, it.sku_id, it.sequence_no ASC;";

        await using (var txCmd = new NpgsqlCommand(txSql, conn))
        {
            txCmd.Parameters.AddWithValue("tid", tenantId);
            await using var r = await txCmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                txRows.Add(new TxRow(
                    r.GetGuid(0),
                    r.GetString(1),
                    r.GetGuid(2),
                    r.GetString(3),
                    r.GetString(4),
                    r.GetString(5),
                    r.GetDecimal(6),
                    r.GetDecimal(7),
                    r.GetInt64(8),
                    DateOnly.FromDateTime(r.GetDateTime(9))));
            }
        }

        if (txRows.Count == 0)
        {
            Console.Error.WriteLine($"Tenant '{tenantName}' chưa có dữ liệu giao dịch kho.");
            return 1;
        }

        var minDate = txRows.Min(x => x.LocalDate);
        var maxDate = txRows.Max(x => x.LocalDate);
        var horizonDays = leadTimeDays + safetyDays;

        // Mốc t đầu tiên sau 30 ngày lịch sử, cách nhau stepDays (7 ngày), đảm bảo [t, t+H) nằm trong dữ liệu
        var forecastDates = new List<DateOnly>();
        for (var d = minDate.AddDays(ReorderForecastCalculator.VelocityWindowDays); d.AddDays(horizonDays - 1) <= maxDate; d = d.AddDays(stepDays))
        {
            forecastDates.Add(d);
        }

        var evalPoints = new List<EvalPoint>();
        var groupedSeries = txRows.GroupBy(x => (x.BranchId, x.BranchName, x.SkuId, x.SkuCode, x.UnitName));

        foreach (var series in groupedSeries)
        {
            var ordered = series.OrderBy(x => x.SequenceNo).ToList();
            var rop = reorderPoints.TryGetValue((series.Key.BranchId, series.Key.SkuId), out var val) ? val : 0m;

            foreach (var t in forecastDates)
            {
                // Chỉ dùng giao dịch có ngày < t
                var historyBeforeT = ordered.Where(x => x.LocalDate < t).ToList();
                var availableAtT = historyBeforeT.Count > 0 ? historyBeforeT[^1].BalanceAfter : 0m;

                var exportPoints = historyBeforeT
                    .Where(x => x.Type == "OUT")
                    .Select(x => new ReorderExportPoint(x.LocalDate, x.Quantity))
                    .ToList();

                var asOf = t.AddDays(-1);
                var forecast = ReorderForecastCalculator.Calculate(
                    exportPoints,
                    asOf,
                    availableAtT,
                    rop,
                    leadTimeDays,
                    safetyDays,
                    series.Key.UnitName);

                var horizonEndExclusive = t.AddDays(horizonDays);
                var horizonTx = ordered.Where(x => x.LocalDate >= t && x.LocalDate < horizonEndExclusive).ToList();
                var actualHorizonQty = horizonTx.Where(x => x.Type == "OUT").Sum(x => x.Quantity);
                var hasStockout = availableAtT <= 0m || horizonTx.Any(x => x.BalanceAfter <= 0m);

                string groupName = hasStockout
                    ? "Stockout"
                    : forecast.ActiveDays >= 10
                        ? "Eligible (>=10d/30d)"
                        : forecast.ActiveDays >= 1
                            ? "Intermittent (1-9d/30d)"
                            : "NoSales (0d/30d)";

                var forecastHorizonQty = Math.Round(forecast.DailyVelocity * horizonDays, 2);
                var absError = Math.Abs(forecastHorizonQty - actualHorizonQty);
                var actualNeedsReorder = availableAtT <= rop || (actualHorizonQty > 0m && availableAtT <= actualHorizonQty);

                evalPoints.Add(new EvalPoint(
                    t,
                    series.Key.BranchId,
                    series.Key.BranchName,
                    series.Key.SkuId,
                    series.Key.SkuCode,
                    groupName,
                    forecast.ActiveDays,
                    availableAtT,
                    rop,
                    forecastHorizonQty,
                    actualHorizonQty,
                    absError,
                    forecast.NeedsReorder,
                    actualNeedsReorder,
                    hasStockout));
            }
        }

        var groupOrder = new[]
        {
            "Eligible (>=10d/30d)",
            "Intermittent (1-9d/30d)",
            "NoSales (0d/30d)",
            "Stockout"
        };

        var summaries = groupOrder
            .Select(g => SummarizeGroup(g, evalPoints.Where(p => p.Group == g).ToList()))
            .Where(s => s.SampleCount > 0)
            .ToList();

        var eligible = summaries.FirstOrDefault(s => s.Group == "Eligible (>=10d/30d)");
        var wapePass = eligible?.Wape is <= WapeThreshold;
        // FIN-C-fix1: recall không đo được (không có ca cần nhập thực tế) thì KHÔNG coi là đạt
        var recallPass = eligible?.Recall is not null && eligible.Recall >= RecallThreshold;
        var overallVerdict = (wapePass && recallPass)
            ? "ĐẠT"
            : eligible?.Recall is null ? "CHƯA ĐẠT (chưa đo được recall: nhóm Eligible không có ca cần nhập)" : "CHƯA ĐẠT";

        var top10Errors = evalPoints
            .OrderByDescending(p => p.AbsError)
            .ThenBy(p => p.SkuCode, StringComparer.Ordinal)
            .ThenBy(p => p.ForecastDate)
            .Take(10)
            .ToList();

        Directory.CreateDirectory(outDir);
        var jsonPath = Path.Combine(outDir, "results.json");
        var reportPath = Path.Combine(outDir, "report.md");

        var payload = new
        {
            formulaVersion = ReorderForecastCalculator.FormulaVersion,
            commit = commitHash,
            tenant = tenantName,
            database = csb.Database,
            dateRange = new { from = minDate.ToString("yyyy-MM-dd"), to = maxDate.ToString("yyyy-MM-dd") },
            parameters = new { leadTimeDays, safetyStockDays = safetyDays, horizonDays, stepDays, wapeThreshold = WapeThreshold, recallThreshold = RecallThreshold },
            overallVerdict,
            groupSummaries = summaries,
            top10Errors,
            points = evalPoints
        };

        await File.WriteAllTextAsync(
            jsonPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);

        var md = new StringBuilder();
        md.AppendLine("# Báo cáo Backtest Dự báo Nhập hàng (`Oism.ForecastEval`)");
        md.AppendLine();
        md.AppendLine($"- **FormulaVersion:** `{ReorderForecastCalculator.FormulaVersion}`");
        md.AppendLine($"- **Commit:** `{commitHash}`");
        md.AppendLine($"- **Tập dữ liệu:** Tenant `{tenantName}` (DB `{csb.Database}`), khoảng `{minDate:yyyy-MM-dd}` → `{maxDate:yyyy-MM-dd}`");
        md.AppendLine($"- **Tham số:** `LeadTimeDays = {leadTimeDays}`, `SafetyStockDays = {safetyDays}`, `H = {horizonDays}`, bước `t` mỗi `{stepDays}` ngày ({forecastDates.Count} mốc)");
        md.AppendLine($"- **Ngưỡng Q6 (chỉ tính trên nhóm `Eligible >=10d/30d`, không có ngày hết hàng):** `WAPE <= {WapeThreshold:P0}`, `Recall >= {RecallThreshold:P0}`");
        md.AppendLine($"- **Kết luận tổng thể:** **{overallVerdict}**");
        md.AppendLine();
        md.AppendLine("## Kết quả theo nhóm SKU");
        md.AppendLine();
        md.AppendLine("| Nhóm | Số mẫu | Tổng thực tế | Tổng dự báo | Sai số tuyệt đối | WAPE | Cần nhập thực tế | Đúng (TP) | Recall | Dự báo cần nhập | Precision |");
        md.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var s in summaries)
        {
            var wapeStr = s.Wape.HasValue ? $"{s.Wape.Value:P2}" : "N/A";
            var recallStr = s.Recall.HasValue ? $"{s.Recall.Value:P2}" : "N/A";
            md.AppendLine($"| {s.Group} | {s.SampleCount} | {s.TotalActualQty:0.##} | {s.TotalForecastQty:0.##} | {s.TotalAbsError:0.##} | {wapeStr} | {s.ActualNeedCount} | {s.TruePositiveNeedCount} | {recallStr} | {s.PredictedNeedCount} | {(s.Precision.HasValue ? $"{s.Precision.Value:P2}" : "N/A")} |");
        }
        md.AppendLine();
        md.AppendLine("## Top 10 sai số lớn nhất");
        md.AppendLine();
        md.AppendLine("| Mốc `t` | SKU | Nhóm | Tồn tại `t` | Dự báo `v×H` | Thực tế `[t, t+H)` | Sai số `|err|` |");
        md.AppendLine("| --- | --- | --- | ---: | ---: | ---: | ---: |");
        foreach (var e in top10Errors)
        {
            md.AppendLine($"| {e.ForecastDate:yyyy-MM-dd} | {e.SkuCode} | {e.Group} | {e.AvailableAtT:0.##} | {e.ForecastHorizonQty:0.##} | {e.ActualHorizonQty:0.##} | {e.AbsError:0.##} |");
        }

        await File.WriteAllTextAsync(reportPath, md.ToString(), Encoding.UTF8);

        Console.WriteLine($"[DONE] Verdict={overallVerdict}, WAPE={eligible?.Wape:P2}, Recall={eligible?.Recall:P2}, Precision={eligible?.Precision:P2}");
        Console.WriteLine($"Written: {jsonPath}");
        Console.WriteLine($"Written: {reportPath}");
        return 0;
    }

    private static GroupSummary SummarizeGroup(string group, List<EvalPoint> items)
    {
        var totalActual = items.Sum(x => x.ActualHorizonQty);
        var totalForecast = items.Sum(x => x.ForecastHorizonQty);
        var totalErr = items.Sum(x => x.AbsError);
        decimal? wape = totalActual > 0m ? Math.Round(totalErr / totalActual, 4) : null;

        var actualNeed = items.Where(x => x.ActualNeedsReorder).ToList();
        var tp = actualNeed.Count(x => x.PredictedNeedsReorder);
        decimal? recall = actualNeed.Count > 0 ? Math.Round((decimal)tp / actualNeed.Count, 4) : null;

        var predictedNeed = items.Count(x => x.PredictedNeedsReorder);
        decimal? precision = predictedNeed > 0 ? Math.Round((decimal)tp / predictedNeed, 4) : null;

        return new GroupSummary(group, items.Count, totalActual, totalForecast, totalErr, wape, actualNeed.Count, tp, recall, predictedNeed, precision);
    }

    private static string BuildDefaultConnectionString()
    {
        var host = Environment.GetEnvironmentVariable("OISM_TEST_HOST") ?? "localhost";
        var port = int.TryParse(Environment.GetEnvironmentVariable("OISM_TEST_PORT"), out var p) ? p : 5432;
        var db = Environment.GetEnvironmentVariable("OISM_EVAL_DATABASE") ?? "oisms_forecast_eval";
        var user = Environment.GetEnvironmentVariable("OISM_TEST_ADMIN_USER") ?? "postgres";
        var pass = Environment.GetEnvironmentVariable("OISM_TEST_ADMIN_PASSWORD") ?? "admin123";
        return new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = db,
            Username = user,
            Password = pass
        }.ConnectionString;
    }

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
