using ClosedXML.Excel;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Reports.Dtos;

namespace OISM.Infrastructure.Services;

/// <summary>
/// Cài đặt dịch vụ xuất file Excel (.xlsx) sử dụng thư viện ClosedXML (MIT) (Upgrade 13 / BE-13.3).
/// Tuân thủ quy tắc bảo mật che giấu toàn bộ cột đơn giá, thành tiền, giá trị đối với tài khoản nhân viên (Staff).
/// </summary>
public class ClosedXmlExcelExportService : IExcelExportService
{
    private const string FontName = "Segoe UI";

    /// <inheritdoc />
    public byte[] ExportInventoryPeriodReport(InventoryPeriodReportDto report, bool isOwner)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Ton_Theo_Ky");
        ws.ShowGridLines = true;

        // 1. Tiêu đề báo cáo
        ws.Cell("A1").Value = "BÁO CÁO TỒN KHO THEO KỲ";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 16;
        ws.Cell("A1").Style.Font.FontName = FontName;
        ws.Cell("A1").Style.Font.FontColor = XLColor.FromHtml("#1E3A8A");

        ws.Cell("A2").Value = $"Kỳ báo cáo: Từ {report.FromDate:dd/MM/yyyy} đến {report.ToDate:dd/MM/yyyy}";
        ws.Cell("A2").Style.Font.Italic = true;
        ws.Cell("A2").Style.Font.FontSize = 11;
        ws.Cell("A2").Style.Font.FontName = FontName;

        ws.Cell("A3").Value = $"Chi nhánh: {report.BranchName}";
        ws.Cell("A3").Style.Font.FontSize = 11;
        ws.Cell("A3").Style.Font.FontName = FontName;

        var isAllBranches = !report.BranchId.HasValue || report.BranchId.Value == Guid.Empty;

        // 2. Định nghĩa các cột tiêu đề
        var colIndex = 1;
        var headers = new List<string>
        {
            "STT",
            "Mã SKU",
            "Mã vạch (Barcode)",
            "Tên sản phẩm",
            "Biến thể",
            "Ngành hàng / Danh mục",
            "ĐVT",
            "Tồn đầu kỳ (SL)"
        };

        if (isOwner)
        {
            headers.Add("Tồn đầu kỳ (Giá trị)");
        }

        headers.Add("Tổng nhập (SL)");
        if (isOwner)
        {
            headers.Add("Tổng nhập (Giá trị)");
        }

        headers.Add("Tổng xuất (SL)");
        if (isOwner)
        {
            headers.Add("Tổng xuất (Giá trị)");
        }

        headers.Add("Tồn cuối kỳ (SL)");
        if (isOwner)
        {
            headers.Add("Tồn cuối kỳ (Giá trị)");
        }

        if (isAllBranches)
        {
            headers.Add("Đang chuyển đầu kỳ");
            headers.Add("Đang chuyển cuối kỳ");
        }
        else
        {
            headers.Add("Đang chuyển đến");
        }

        var headerRow = 5;
        for (int i = 0; i < headers.Count; i++)
        {
            var cell = ws.Cell(headerRow, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontName = FontName;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A8A");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#CBD5E1");
        }
        ws.Row(headerRow).Height = 28;

        // 3. Dòng dữ liệu
        var currentRow = headerRow + 1;
        var stt = 1;

        foreach (var item in report.Items)
        {
            colIndex = 1;
            ws.Cell(currentRow, colIndex++).SetValue(stt++);
            ws.Cell(currentRow, colIndex++).SetValue(item.SkuCode);
            ws.Cell(currentRow, colIndex++).SetValue(item.Barcode ?? string.Empty);
            ws.Cell(currentRow, colIndex++).SetValue(item.ProductName);
            ws.Cell(currentRow, colIndex++).SetValue(item.VariantName ?? string.Empty);
            ws.Cell(currentRow, colIndex++).SetValue(item.CategoryName ?? string.Empty);
            ws.Cell(currentRow, colIndex++).SetValue(item.UnitName);

            // Đầu kỳ
            var cOpenQty = ws.Cell(currentRow, colIndex++);
            cOpenQty.SetValue(item.OpeningQuantity);
            cOpenQty.Style.NumberFormat.Format = "#,##0.###";

            if (isOwner)
            {
                var cOpenVal = ws.Cell(currentRow, colIndex++);
                cOpenVal.SetValue(item.OpeningValue ?? 0m);
                cOpenVal.Style.NumberFormat.Format = "#,##0";
            }

            // Nhập
            var cInQty = ws.Cell(currentRow, colIndex++);
            cInQty.SetValue(item.InQuantity);
            cInQty.Style.NumberFormat.Format = "#,##0.###";

            if (isOwner)
            {
                var cInVal = ws.Cell(currentRow, colIndex++);
                cInVal.SetValue(item.InValue ?? 0m);
                cInVal.Style.NumberFormat.Format = "#,##0";
            }

            // Xuất
            var cOutQty = ws.Cell(currentRow, colIndex++);
            cOutQty.SetValue(item.OutQuantity);
            cOutQty.Style.NumberFormat.Format = "#,##0.###";

            if (isOwner)
            {
                var cOutVal = ws.Cell(currentRow, colIndex++);
                cOutVal.SetValue(item.OutValue ?? 0m);
                cOutVal.Style.NumberFormat.Format = "#,##0";
            }

            // Cuối kỳ
            var cCloseQty = ws.Cell(currentRow, colIndex++);
            cCloseQty.SetValue(item.ClosingQuantity);
            cCloseQty.Style.NumberFormat.Format = "#,##0.###";

            if (isOwner)
            {
                var cCloseVal = ws.Cell(currentRow, colIndex++);
                cCloseVal.SetValue(item.ClosingValue ?? 0m);
                cCloseVal.Style.NumberFormat.Format = "#,##0";
            }

            // Đang chuyển
            if (isAllBranches)
            {
                var cInTransitOpen = ws.Cell(currentRow, colIndex++);
                cInTransitOpen.SetValue(item.OpeningInTransitQuantity);
                cInTransitOpen.Style.NumberFormat.Format = "#,##0.###";

                var cInTransitClose = ws.Cell(currentRow, colIndex++);
                cInTransitClose.SetValue(item.ClosingInTransitQuantity);
                cInTransitClose.Style.NumberFormat.Format = "#,##0.###";
            }
            else
            {
                var cInTransitClose = ws.Cell(currentRow, colIndex++);
                cInTransitClose.SetValue(item.ClosingInTransitQuantity);
                cInTransitClose.Style.NumberFormat.Format = "#,##0.###";
            }

            // Viền ô
            for (int c = 1; c < colIndex; c++)
            {
                var dataCell = ws.Cell(currentRow, c);
                dataCell.Style.Font.FontName = FontName;
                dataCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                dataCell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#E2E8F0");
            }

            currentRow++;
        }

        // 4. Dòng tổng cộng (Grand Total)
        colIndex = 1;
        ws.Cell(currentRow, 1).Value = "TỔNG CỘNG";
        ws.Range(currentRow, 1, currentRow, 7).Merge();
        ws.Cell(currentRow, 1).Style.Font.Bold = true;
        ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        colIndex = 8;

        var sumOpenQty = ws.Cell(currentRow, colIndex++);
        sumOpenQty.SetValue(report.Summary.TotalOpeningQuantity);
        sumOpenQty.Style.NumberFormat.Format = "#,##0.###";
        sumOpenQty.Style.Font.Bold = true;

        if (isOwner)
        {
            var sumOpenVal = ws.Cell(currentRow, colIndex++);
            sumOpenVal.SetValue(report.Summary.TotalOpeningValue ?? 0m);
            sumOpenVal.Style.NumberFormat.Format = "#,##0";
            sumOpenVal.Style.Font.Bold = true;
        }

        var sumInQty = ws.Cell(currentRow, colIndex++);
        sumInQty.SetValue(report.Summary.TotalInQuantity);
        sumInQty.Style.NumberFormat.Format = "#,##0.###";
        sumInQty.Style.Font.Bold = true;

        if (isOwner)
        {
            var sumInVal = ws.Cell(currentRow, colIndex++);
            sumInVal.SetValue(report.Summary.TotalInValue ?? 0m);
            sumInVal.Style.NumberFormat.Format = "#,##0";
            sumInVal.Style.Font.Bold = true;
        }

        var sumOutQty = ws.Cell(currentRow, colIndex++);
        sumOutQty.SetValue(report.Summary.TotalOutQuantity);
        sumOutQty.Style.NumberFormat.Format = "#,##0.###";
        sumOutQty.Style.Font.Bold = true;

        if (isOwner)
        {
            var sumOutVal = ws.Cell(currentRow, colIndex++);
            sumOutVal.SetValue(report.Summary.TotalOutValue ?? 0m);
            sumOutVal.Style.NumberFormat.Format = "#,##0";
            sumOutVal.Style.Font.Bold = true;
        }

        var sumCloseQty = ws.Cell(currentRow, colIndex++);
        sumCloseQty.SetValue(report.Summary.TotalClosingQuantity);
        sumCloseQty.Style.NumberFormat.Format = "#,##0.###";
        sumCloseQty.Style.Font.Bold = true;

        if (isOwner)
        {
            var sumCloseVal = ws.Cell(currentRow, colIndex++);
            sumCloseVal.SetValue(report.Summary.TotalClosingValue ?? 0m);
            sumCloseVal.Style.NumberFormat.Format = "#,##0";
            sumCloseVal.Style.Font.Bold = true;
        }

        if (isAllBranches)
        {
            var sumTransitOpen = ws.Cell(currentRow, colIndex++);
            sumTransitOpen.SetValue(report.Summary.TotalOpeningInTransitQuantity);
            sumTransitOpen.Style.NumberFormat.Format = "#,##0.###";
            sumTransitOpen.Style.Font.Bold = true;

            var sumTransitClose = ws.Cell(currentRow, colIndex++);
            sumTransitClose.SetValue(report.Summary.TotalClosingInTransitQuantity);
            sumTransitClose.Style.NumberFormat.Format = "#,##0.###";
            sumTransitClose.Style.Font.Bold = true;
        }
        else
        {
            var sumTransitClose = ws.Cell(currentRow, colIndex++);
            sumTransitClose.SetValue(report.Summary.TotalClosingInTransitQuantity);
            sumTransitClose.Style.NumberFormat.Format = "#,##0.###";
            sumTransitClose.Style.Font.Bold = true;
        }

        for (int c = 1; c < colIndex; c++)
        {
            var totalCell = ws.Cell(currentRow, c);
            totalCell.Style.Font.FontName = FontName;
            totalCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
            totalCell.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            totalCell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#94A3B8");
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    /// <inheritdoc />
    public byte[] ExportStockCardReport(StockCardReportDto report, bool isOwner)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("The_Kho");
        ws.ShowGridLines = true;

        // 1. Tiêu đề mẫu chứng từ (S10-DN cho Owner / S12-DN cho Staff)
        ws.Cell("A1").Value = report.ReportTitle.ToUpperInvariant();
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 16;
        ws.Cell("A1").Style.Font.FontName = FontName;
        ws.Cell("A1").Style.Font.FontColor = XLColor.FromHtml("#1E3A8A");

        ws.Cell("A2").Value = $"Kỳ: Từ {report.FromDate:dd/MM/yyyy} đến {report.ToDate:dd/MM/yyyy}";
        ws.Cell("A2").Style.Font.Italic = true;
        ws.Cell("A2").Style.Font.FontSize = 11;
        ws.Cell("A2").Style.Font.FontName = FontName;

        ws.Cell("A3").Value = $"Chi nhánh: {report.BranchName}";
        ws.Cell("A3").Style.Font.FontSize = 11;
        ws.Cell("A3").Style.Font.FontName = FontName;

        ws.Cell("A4").Value = $"Sản phẩm: {report.ProductName} {(string.IsNullOrWhiteSpace(report.VariantName) ? "" : $"({report.VariantName})")} | Mã SKU: {report.SkuCode} | Mã vạch: {report.Barcode ?? "—"} | ĐVT: {report.UnitName}";
        ws.Cell("A4").Style.Font.FontSize = 11;
        ws.Cell("A4").Style.Font.Bold = true;
        ws.Cell("A4").Style.Font.FontName = FontName;

        // 2. Tiêu đề bảng
        var headers = new List<string>
        {
            "STT",
            "Ngày ghi sổ",
            "Số chứng từ",
            "Diễn giải nghiệp vụ",
            "Nhập (SL)"
        };

        if (isOwner)
        {
            headers.Add("Nhập (Đơn giá)");
            headers.Add("Nhập (Thành tiền)");
        }

        headers.Add("Xuất (SL)");
        if (isOwner)
        {
            headers.Add("Xuất (Đơn giá)");
            headers.Add("Xuất (Thành tiền)");
        }

        headers.Add("Tồn sau (SL)");
        if (isOwner)
        {
            headers.Add("Tồn sau (Giá trị)");
        }

        var headerRow = 6;
        for (int i = 0; i < headers.Count; i++)
        {
            var cell = ws.Cell(headerRow, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontName = FontName;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A8A");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#CBD5E1");
        }
        ws.Row(headerRow).Height = 28;

        var currentRow = headerRow + 1;

        // 3. Dòng Số dư đầu kỳ
        ws.Cell(currentRow, 1).Value = "—";
        ws.Cell(currentRow, 2).Value = $"00:00 {report.FromDate:dd/MM/yyyy}";
        ws.Cell(currentRow, 3).Value = "—";
        ws.Cell(currentRow, 4).Value = "SỐ DƯ ĐẦU KỲ";
        ws.Cell(currentRow, 4).Style.Font.Bold = true;

        var cCol = 5;
        // Nhập trống
        ws.Cell(currentRow, cCol++).Value = "—";
        if (isOwner)
        {
            ws.Cell(currentRow, cCol++).Value = "—";
            ws.Cell(currentRow, cCol++).Value = "—";
        }
        // Xuất trống
        ws.Cell(currentRow, cCol++).Value = "—";
        if (isOwner)
        {
            ws.Cell(currentRow, cCol++).Value = "—";
            ws.Cell(currentRow, cCol++).Value = "—";
        }
        // Tồn sau
        var cInitQty = ws.Cell(currentRow, cCol++);
        cInitQty.SetValue(report.OpeningQuantity);
        cInitQty.Style.NumberFormat.Format = "#,##0.###";
        cInitQty.Style.Font.Bold = true;

        if (isOwner)
        {
            var cInitVal = ws.Cell(currentRow, cCol++);
            cInitVal.SetValue(report.OpeningValue ?? 0m);
            cInitVal.Style.NumberFormat.Format = "#,##0";
            cInitVal.Style.Font.Bold = true;
        }

        for (int c = 1; c < cCol; c++)
        {
            var rowCell = ws.Cell(currentRow, c);
            rowCell.Style.Font.FontName = FontName;
            rowCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
            rowCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            rowCell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#CBD5E1");
        }
        currentRow++;

        // 4. Các dòng phát sinh trong kỳ
        var stt = 1;
        foreach (var entry in report.Entries)
        {
            cCol = 1;
            ws.Cell(currentRow, cCol++).SetValue(stt++);
            ws.Cell(currentRow, cCol++).SetValue(entry.CreatedAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm"));
            ws.Cell(currentRow, cCol++).SetValue(entry.DocumentNumber);
            ws.Cell(currentRow, cCol++).SetValue(entry.SourceType);

            // Nhập
            if (entry.Type == "IN")
            {
                var cInQ = ws.Cell(currentRow, cCol++);
                cInQ.SetValue(entry.InQuantity);
                cInQ.Style.NumberFormat.Format = "#,##0.###";

                if (isOwner)
                {
                    var cInPrice = ws.Cell(currentRow, cCol++);
                    cInPrice.SetValue(entry.InUnitPrice ?? 0m);
                    cInPrice.Style.NumberFormat.Format = "#,##0";

                    var cInV = ws.Cell(currentRow, cCol++);
                    cInV.SetValue(entry.InValue ?? 0m);
                    cInV.Style.NumberFormat.Format = "#,##0";
                }
            }
            else
            {
                ws.Cell(currentRow, cCol++).Value = "—";
                if (isOwner)
                {
                    ws.Cell(currentRow, cCol++).Value = "—";
                    ws.Cell(currentRow, cCol++).Value = "—";
                }
            }

            // Xuất
            if (entry.Type == "OUT")
            {
                var cOutQ = ws.Cell(currentRow, cCol++);
                cOutQ.SetValue(entry.OutQuantity);
                cOutQ.Style.NumberFormat.Format = "#,##0.###";

                if (isOwner)
                {
                    var cOutPrice = ws.Cell(currentRow, cCol++);
                    cOutPrice.SetValue(entry.OutUnitPrice ?? 0m);
                    cOutPrice.Style.NumberFormat.Format = "#,##0";

                    var cOutV = ws.Cell(currentRow, cCol++);
                    cOutV.SetValue(entry.OutValue ?? 0m);
                    cOutV.Style.NumberFormat.Format = "#,##0";
                }
            }
            else
            {
                ws.Cell(currentRow, cCol++).Value = "—";
                if (isOwner)
                {
                    ws.Cell(currentRow, cCol++).Value = "—";
                    ws.Cell(currentRow, cCol++).Value = "—";
                }
            }

            // Tồn sau
            var cBalQ = ws.Cell(currentRow, cCol++);
            cBalQ.SetValue(entry.BalanceAfterQuantity);
            cBalQ.Style.NumberFormat.Format = "#,##0.###";

            if (isOwner)
            {
                var cBalV = ws.Cell(currentRow, cCol++);
                cBalV.SetValue(entry.BalanceAfterValue ?? 0m);
                cBalV.Style.NumberFormat.Format = "#,##0";
            }

            for (int c = 1; c < cCol; c++)
            {
                var rowCell = ws.Cell(currentRow, c);
                rowCell.Style.Font.FontName = FontName;
                rowCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                rowCell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#E2E8F0");
            }

            currentRow++;
        }

        // 5. Dòng Cộng phát sinh trong kỳ
        cCol = 1;
        ws.Cell(currentRow, 1).Value = "CỘNG PHÁT SINH TRONG KỲ";
        ws.Range(currentRow, 1, currentRow, 4).Merge();
        ws.Cell(currentRow, 1).Style.Font.Bold = true;
        ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        cCol = 5;

        var sumInQ = ws.Cell(currentRow, cCol++);
        sumInQ.SetValue(report.TotalInQuantity);
        sumInQ.Style.NumberFormat.Format = "#,##0.###";
        sumInQ.Style.Font.Bold = true;

        if (isOwner)
        {
            ws.Cell(currentRow, cCol++).Value = "—";
            var sumInV = ws.Cell(currentRow, cCol++);
            sumInV.SetValue(report.TotalInValue ?? 0m);
            sumInV.Style.NumberFormat.Format = "#,##0";
            sumInV.Style.Font.Bold = true;
        }

        var sumOutQ = ws.Cell(currentRow, cCol++);
        sumOutQ.SetValue(report.TotalOutQuantity);
        sumOutQ.Style.NumberFormat.Format = "#,##0.###";
        sumOutQ.Style.Font.Bold = true;

        if (isOwner)
        {
            ws.Cell(currentRow, cCol++).Value = "—";
            var sumOutV = ws.Cell(currentRow, cCol++);
            sumOutV.SetValue(report.TotalOutValue ?? 0m);
            sumOutV.Style.NumberFormat.Format = "#,##0";
            sumOutV.Style.Font.Bold = true;
        }

        ws.Cell(currentRow, cCol++).Value = "—";
        if (isOwner)
        {
            ws.Cell(currentRow, cCol++).Value = "—";
        }

        for (int c = 1; c < cCol; c++)
        {
            var rowCell = ws.Cell(currentRow, c);
            rowCell.Style.Font.FontName = FontName;
            rowCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
            rowCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            rowCell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#94A3B8");
        }
        currentRow++;

        // 6. Dòng Số dư cuối kỳ
        cCol = 1;
        ws.Cell(currentRow, 1).Value = "SỐ DƯ CUỐI KỲ";
        ws.Range(currentRow, 1, currentRow, 4).Merge();
        ws.Cell(currentRow, 1).Style.Font.Bold = true;
        ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        cCol = 5;

        // Nhập trống
        ws.Cell(currentRow, cCol++).Value = "—";
        if (isOwner)
        {
            ws.Cell(currentRow, cCol++).Value = "—";
            ws.Cell(currentRow, cCol++).Value = "—";
        }
        // Xuất trống
        ws.Cell(currentRow, cCol++).Value = "—";
        if (isOwner)
        {
            ws.Cell(currentRow, cCol++).Value = "—";
            ws.Cell(currentRow, cCol++).Value = "—";
        }
        // Tồn sau
        var cFinalQty = ws.Cell(currentRow, cCol++);
        cFinalQty.SetValue(report.ClosingQuantity);
        cFinalQty.Style.NumberFormat.Format = "#,##0.###";
        cFinalQty.Style.Font.Bold = true;

        if (isOwner)
        {
            var cFinalVal = ws.Cell(currentRow, cCol++);
            cFinalVal.SetValue(report.ClosingValue ?? 0m);
            cFinalVal.Style.NumberFormat.Format = "#,##0";
            cFinalVal.Style.Font.Bold = true;
        }

        for (int c = 1; c < cCol; c++)
        {
            var rowCell = ws.Cell(currentRow, c);
            rowCell.Style.Font.FontName = FontName;
            rowCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E2E8F0");
            rowCell.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            rowCell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#64748B");
        }
        currentRow++;

        // 7. Chênh lệch làm tròn nếu có (Q14)
        if (isOwner && report.HasRoundingDifference)
        {
            cCol = 1;
            ws.Cell(currentRow, 1).Value = $"Chênh lệch làm tròn so với WAC hiện tại ({report.CurrentOnHand:G29} × {report.CurrentWac:#,##0} = {report.CurrentTheoreticalValue:#,##0})";
            ws.Range(currentRow, 1, currentRow, cCol + (isOwner ? 9 : 5)).Merge();
            ws.Cell(currentRow, 1).Style.Font.Italic = true;
            ws.Cell(currentRow, 1).Style.Font.FontColor = XLColor.FromHtml("#B45309");

            var cDiff = ws.Cell(currentRow, cCol + (isOwner ? 10 : 6));
            cDiff.SetValue(report.RoundingDifference ?? 0m);
            cDiff.Style.NumberFormat.Format = "#,##0";
            cDiff.Style.Font.Bold = true;
            cDiff.Style.Font.FontColor = XLColor.FromHtml("#B45309");
            currentRow++;
        }

        // 8. Khu vực chữ ký
        currentRow += 2;
        ws.Cell(currentRow, 2).Value = "Người lập biểu";
        ws.Cell(currentRow, 2).Style.Font.Bold = true;
        ws.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(currentRow, 4).Value = "Thủ kho";
        ws.Cell(currentRow, 4).Style.Font.Bold = true;
        ws.Cell(currentRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var lastCol = headers.Count;
        ws.Cell(currentRow, lastCol - 1).Value = "Giám đốc / Chủ shop";
        ws.Cell(currentRow, lastCol - 1).Style.Font.Bold = true;
        ws.Cell(currentRow, lastCol - 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        currentRow++;
        ws.Cell(currentRow, 2).Value = "(Ký, họ tên)";
        ws.Cell(currentRow, 2).Style.Font.Italic = true;
        ws.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(currentRow, 4).Value = "(Ký, họ tên)";
        ws.Cell(currentRow, 4).Style.Font.Italic = true;
        ws.Cell(currentRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(currentRow, lastCol - 1).Value = "(Ký, đóng dấu)";
        ws.Cell(currentRow, lastCol - 1).Style.Font.Italic = true;
        ws.Cell(currentRow, lastCol - 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }
}
