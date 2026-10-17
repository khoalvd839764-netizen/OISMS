using Npgsql;

namespace OISM.Infrastructure.Persistence;

public record SchemaValidationResult(
    bool IsValid,
    List<string> DriftErrors);

public record ExpectedTrigger(
    string TableName,
    string TriggerName,
    string FunctionName,
    string FunctionSchema = "public",
    string Timing = "BEFORE",
    bool IsRowLevel = true,
    bool HasInsert = false,
    bool HasUpdate = false,
    bool HasDelete = false,
    bool HasTruncate = false,
    bool IsDeferrable = false,
    bool InitiallyDeferred = false,
    string? ExpectedWhenClause = null,
    bool DisallowColumnRestrictions = true);

public record ExpectedForeignKey(
    string ConstraintName,
    string SourceTable,
    string[] SourceColumns,
    string TargetTable,
    string[] TargetColumns,
    string SourceSchema = "public",
    string TargetSchema = "public",
    char OnDelete = 'r',
    char OnUpdate = 'a',
    bool IsDeferrable = false,
    bool InitiallyDeferred = false,
    bool IsValidated = true);

public record ExpectedConstraint(
    string ConstraintName,
    char ConstraintType,
    string TableName,
    string[]? Columns = null,
    string? ExpectedNormalizedExpression = null,
    bool IsValidated = true);

/// <summary>
/// Dịch vụ quản lý khởi tạo và tiếp nhận baseline cho PostgreSQL schema v0.5.
/// Hỗ trợ:
/// 1. Cài mới database trống từ file DDL chuẩn oisms_schema.sql (giữ nguyên trigger, index, constraint và quyền runtime).
/// 2. Tiếp nhận (adopt baseline) database v0.5 đã tồn tại (không tạo lại bảng, ghi nhận migration vào __EFMigrationsHistory).
/// 3. Phát hiện và cảnh báo sai lệch cấu trúc (schema drift) nếu database hiện tại thiếu bảng, trigger bị tắt/sai hoặc quyền sai lệch.
/// Tuyệt đối không dùng EnsureCreated() và không xóa database/volume.
/// </summary>
public class DatabaseMigrationService : IDatabaseMigrationService
{
    public const string InitialBaselineMigrationId = "20261017000000_InitialBaseline";
    public const string ProductVersion = "8.0.11";

    public static readonly string[] RequiredTables =
    [
        "tenants",
        "users",
        "refresh_tokens",
        "branches",
        "warehouses",
        "categories",
        "brands",
        "products",
        "skus",
        "suppliers",
        "goods_receipts",
        "goods_receipt_items",
        "inventory_balances",
        "warehouse_inventory_balances",
        "stock_transfers",
        "stock_transfer_items",
        "stock_counts",
        "stock_count_items",
        "inventory_adjustments",
        "inventory_adjustment_items",
        "orders",
        "order_items",
        "reservations",
        "payments",
        "sales_returns",
        "sales_return_items",
        "inventory_transactions",
        "daily_sales_summaries",
        "replenishment_suggestions"
    ];

    public static readonly ExpectedTrigger[] ExpectedTriggers =
    [
        // inventory_balances
        new("inventory_balances", "stock_lock", "oisms_lock_stock", "public", "BEFORE", true, HasInsert: true, HasUpdate: true),
        new("inventory_balances", "stock_consistency", "oisms_stock_constraint", "public", "AFTER", true, HasInsert: true, HasUpdate: true, IsDeferrable: true, InitiallyDeferred: true),
        new("inventory_balances", "stock_no_delete", "oisms_deny_mutation", "public", "BEFORE", false, HasDelete: true, HasTruncate: true),

        // warehouse_inventory_balances
        new("warehouse_inventory_balances", "stock_lock", "oisms_lock_stock", "public", "BEFORE", true, HasInsert: true, HasUpdate: true),
        new("warehouse_inventory_balances", "stock_consistency", "oisms_stock_constraint", "public", "AFTER", true, HasInsert: true, HasUpdate: true, IsDeferrable: true, InitiallyDeferred: true),
        new("warehouse_inventory_balances", "stock_no_delete", "oisms_deny_mutation", "public", "BEFORE", false, HasDelete: true, HasTruncate: true),

        // reservations
        new("reservations", "stock_lock", "oisms_lock_stock", "public", "BEFORE", true, HasInsert: true, HasUpdate: true),
        new("reservations", "stock_consistency", "oisms_stock_constraint", "public", "AFTER", true, HasInsert: true, HasUpdate: true, IsDeferrable: true, InitiallyDeferred: true),
        new("reservations", "stock_no_delete", "oisms_deny_mutation", "public", "BEFORE", false, HasDelete: true, HasTruncate: true),

        // inventory_transactions
        new("inventory_transactions", "stock_lock", "oisms_lock_stock", "public", "BEFORE", true, HasInsert: true, HasUpdate: true),
        new("inventory_transactions", "stock_consistency", "oisms_stock_constraint", "public", "AFTER", true, HasInsert: true, HasUpdate: true, IsDeferrable: true, InitiallyDeferred: true),
        new("inventory_transactions", "ledger_append_only", "oisms_deny_mutation", "public", "BEFORE", false, HasUpdate: true, HasDelete: true, HasTruncate: true),

        // orders
        new("orders", "protect_order", "oisms_protect_order", "public", "BEFORE", true, HasUpdate: true),
        new("orders", "order_cost_snapshot", "oisms_order_cost_constraint", "public", "AFTER", true, HasInsert: true, HasUpdate: true, IsDeferrable: true, InitiallyDeferred: true),

        // order_items
        new("order_items", "protect_order_item", "oisms_protect_order_item", "public", "BEFORE", true, HasUpdate: true, HasDelete: true),
        new("order_items", "no_truncate_order_items", "oisms_deny_mutation", "public", "BEFORE", false, HasTruncate: true),
        new("order_items", "item_cost_snapshot", "oisms_order_cost_constraint", "public", "AFTER", true, HasInsert: true, HasUpdate: true, IsDeferrable: true, InitiallyDeferred: true)
    ];

    public static readonly ExpectedForeignKey[] ExpectedForeignKeys =
    [
        // Catalog & SKU Foreign Keys (bảo vệ tenant_id ghép)
        new("fk_skus_product_id", "skus", ["tenant_id", "product_id"], "products", ["tenant_id", "id"]),
        new("fk_skus_tenant_id", "skus", ["tenant_id"], "tenants", ["id"]),
        new("fk_products_tenant_id", "products", ["tenant_id"], "tenants", ["id"]),
        new("fk_products_category_id", "products", ["tenant_id", "category_id"], "categories", ["tenant_id", "id"]),
        new("fk_products_brand_id", "products", ["tenant_id", "brand_id"], "brands", ["tenant_id", "id"]),

        // Auth & Tenant Foreign Keys
        new("fk_users_tenant_id", "users", ["tenant_id"], "tenants", ["id"]),
        new("fk_refresh_tokens_tenant_id", "refresh_tokens", ["tenant_id"], "tenants", ["id"]),
        new("fk_refresh_tokens_user_id", "refresh_tokens", ["tenant_id", "user_id"], "users", ["tenant_id", "id"]),

        // Organization & Warehouses
        new("fk_branches_tenant_id", "branches", ["tenant_id"], "tenants", ["id"]),
        new("fk_warehouses_tenant_id", "warehouses", ["tenant_id"], "tenants", ["id"]),
        new("fk_warehouses_branch_id", "warehouses", ["tenant_id", "branch_id"], "branches", ["tenant_id", "id"]),
        new("fk_suppliers_tenant_id", "suppliers", ["tenant_id"], "tenants", ["id"]),

        // Inventory & Warehouse Balances
        new("fk_inventory_balances_tenant_id", "inventory_balances", ["tenant_id"], "tenants", ["id"]),
        new("fk_inventory_balances_branch_id", "inventory_balances", ["tenant_id", "branch_id"], "branches", ["tenant_id", "id"]),
        new("fk_inventory_balances_sku_id", "inventory_balances", ["tenant_id", "sku_id"], "skus", ["tenant_id", "id"]),
        new("fk_warehouse_inventory_balances_tenant_id", "warehouse_inventory_balances", ["tenant_id"], "tenants", ["id"]),
        new("fk_warehouse_inventory_balances_warehouse_branch", "warehouse_inventory_balances", ["tenant_id", "warehouse_id", "branch_id"], "warehouses", ["tenant_id", "id", "branch_id"]),
        new("fk_warehouse_inventory_balances_sku_id", "warehouse_inventory_balances", ["tenant_id", "sku_id"], "skus", ["tenant_id", "id"]),

        // Reservations
        new("fk_reservations_tenant_id", "reservations", ["tenant_id"], "tenants", ["id"]),
        new("fk_reservations_warehouse_branch", "reservations", ["tenant_id", "warehouse_id", "branch_id"], "warehouses", ["tenant_id", "id", "branch_id"]),
        new("fk_reservations_order_item", "reservations", ["tenant_id", "order_item_id", "order_id", "sku_id"], "order_items", ["tenant_id", "id", "order_id", "sku_id"]),
        new("fk_reservations_order_branch", "reservations", ["tenant_id", "order_id", "branch_id"], "orders", ["tenant_id", "id", "branch_id"]),

        // Inventory Transactions (Ledger)
        new("fk_inventory_transactions_tenant_id", "inventory_transactions", ["tenant_id"], "tenants", ["id"]),
        new("fk_inventory_transactions_warehouse_branch", "inventory_transactions", ["tenant_id", "warehouse_id", "branch_id"], "warehouses", ["tenant_id", "id", "branch_id"]),
        new("fk_inventory_transactions_sku_id", "inventory_transactions", ["tenant_id", "sku_id"], "skus", ["tenant_id", "id"]),
        new("fk_inventory_transactions_branch_id", "inventory_transactions", ["tenant_id", "branch_id"], "branches", ["tenant_id", "id"]),

        // Orders & Order Items
        new("fk_orders_tenant_id", "orders", ["tenant_id"], "tenants", ["id"]),
        new("fk_orders_branch_id", "orders", ["tenant_id", "branch_id"], "branches", ["tenant_id", "id"]),
        new("fk_order_items_tenant_id", "order_items", ["tenant_id"], "tenants", ["id"]),
        new("fk_order_items_order_id", "order_items", ["tenant_id", "order_id"], "orders", ["tenant_id", "id"]),
        new("fk_order_items_sku_id", "order_items", ["tenant_id", "sku_id"], "skus", ["tenant_id", "id"])
    ];

    public static readonly ExpectedConstraint[] ExpectedCriticalConstraints =
    [
        // inventory_balances
        new("ck_inventory_balances_on_hand_nonnegative", 'c', "inventory_balances", ExpectedNormalizedExpression: "(on_hand >= (0)::numeric)"),
        new("ck_inventory_balances_reserved_nonnegative", 'c', "inventory_balances", ExpectedNormalizedExpression: "(reserved >= (0)::numeric)"),
        new("ck_inventory_balances_reservation_limit", 'c', "inventory_balances", ExpectedNormalizedExpression: "(reserved <= on_hand)"),
        new("ck_inventory_balances_last_sequence_nonnegative", 'c', "inventory_balances", ExpectedNormalizedExpression: "(last_sequence >= 0)"),

        // warehouse_inventory_balances
        new("ck_warehouse_inventory_balances_on_hand_nonnegative", 'c', "warehouse_inventory_balances", ExpectedNormalizedExpression: "(on_hand >= (0)::numeric)"),
        new("ck_warehouse_inventory_balances_reserved_nonnegative", 'c', "warehouse_inventory_balances", ExpectedNormalizedExpression: "(reserved >= (0)::numeric)"),
        new("ck_warehouse_inventory_balances_reservation_limit", 'c', "warehouse_inventory_balances", ExpectedNormalizedExpression: "(reserved <= on_hand)"),
        new("ck_warehouse_inventory_balances_last_sequence_nonnegative", 'c', "warehouse_inventory_balances", ExpectedNormalizedExpression: "(last_sequence >= 0)"),
        new("uq_warehouse_inventory_balances_2", 'u', "warehouse_inventory_balances", Columns: ["tenant_id", "warehouse_id", "sku_id"]),

        // reservations
        new("ck_reservations_quantity_positive", 'c', "reservations", ExpectedNormalizedExpression: "(quantity > (0)::numeric)"),
        new("ck_reservations_status_values", 'c', "reservations", ExpectedNormalizedExpression: "((status)::text = ANY ((ARRAY['Active'::character varying, 'Consumed'::character varying, 'Released'::character varying, 'Expired'::character varying])::text[]))"),
        new("uq_reservations_2", 'u', "reservations", Columns: ["tenant_id", "order_item_id", "warehouse_id"]),

        // inventory_transactions
        new("ck_inventory_transactions_type_values", 'c', "inventory_transactions", ExpectedNormalizedExpression: "((type)::text = ANY ((ARRAY['IN'::character varying, 'OUT'::character varying])::text[]))"),
        new("ck_inventory_transactions_quantity_positive", 'c', "inventory_transactions", ExpectedNormalizedExpression: "(quantity > (0)::numeric)"),
        new("ck_inventory_transactions_sequence_no_positive", 'c', "inventory_transactions", ExpectedNormalizedExpression: "(sequence_no > 0)"),
        new("ck_inventory_transactions_balance_after_nonnegative", 'c', "inventory_transactions", ExpectedNormalizedExpression: "(balance_after >= (0)::numeric)"),
        new("ck_inventory_transactions_one_source", 'c', "inventory_transactions", ExpectedNormalizedExpression: "(num_nonnulls(receipt_item_id, order_item_id, transfer_item_id, stock_count_item_id, sales_return_item_id, adjustment_item_id) = 1)"),
        new("uq_inventory_transactions_2", 'u', "inventory_transactions", Columns: ["tenant_id", "branch_id", "sku_id", "sequence_no"]),

        // skus
        new("ck_skus_retail_price_nonnegative", 'c', "skus", ExpectedNormalizedExpression: "(retail_price >= (0)::numeric)"),
        new("ck_skus_wholesale_price_nonnegative", 'c', "skus", ExpectedNormalizedExpression: "(wholesale_price >= (0)::numeric)"),
        new("ck_skus_attributes_object", 'c', "skus", ExpectedNormalizedExpression: "(jsonb_typeof(attributes) = 'object'::text)"),
        new("ck_skus_code_normalized", 'c', "skus", ExpectedNormalizedExpression: "(((code)::text = upper(btrim((code)::text))) AND (length((code)::text) > 0))"),
        new("uq_skus_2", 'u', "skus", Columns: ["tenant_id", "code"]),

        // users
        new("ck_users_role_values", 'c', "users", ExpectedNormalizedExpression: "((role)::text = ANY ((ARRAY['Owner'::character varying, 'Staff'::character varying, 'Cashier'::character varying])::text[]))"),
        new("uq_users_1", 'u', "users", Columns: ["tenant_id", "id"]),
        new("uq_users_2", 'u', "users", Columns: ["email"]),

        // orders & order_items
        new("ck_orders_total_amount_nonnegative", 'c', "orders", ExpectedNormalizedExpression: "(total_amount >= (0)::numeric)"),
        new("ck_order_items_quantity_positive", 'c', "order_items", ExpectedNormalizedExpression: "(quantity > (0)::numeric)"),
        new("ck_order_items_unit_price_nonnegative", 'c', "order_items", ExpectedNormalizedExpression: "(unit_price >= (0)::numeric)"),
        new("ck_order_items_cost_price_nonnegative", 'c', "order_items", ExpectedNormalizedExpression: "(cost_price >= (0)::numeric)")
    ];


    public async Task<MigrationExecutionResult> InitializeOrBaselineDatabaseAsync(
        string adminConnectionString,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);

        // 1. Kiểm tra các bảng hiện có trong schema public
        var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string checkTablesSql = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE';";
        await using (var cmd = new NpgsqlCommand(checkTablesSql, connection))
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                existingTables.Add(reader.GetString(0));
            }
        }

        // Bỏ qua bảng __EFMigrationsHistory và các bảng nội bộ/marker kiểm thử khi đếm số bảng nghiệp vụ
        var businessTables = existingTables.Where(t => 
            !t.Equals("__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase) &&
            !t.StartsWith("_", StringComparison.OrdinalIgnoreCase)).ToList();

        // 2. Phân nhánh xử lý: Cài mới vs Tiếp nhận Baseline
        if (businessTables.Count == 0)
        {
            // === SCENARIO A: CÀI MỚI HOÀN TOÀN ===
            var ddlScript = FindAndLoadSchemaDdl();
            if (string.IsNullOrWhiteSpace(ddlScript))
            {
                return new MigrationExecutionResult(
                    Success: false,
                    IsFreshInstall: false,
                    IsBaselineAdopted: false,
                    TableCount: 0,
                    Message: "Không tìm thấy file DDL oisms_schema.sql để thực hiện cài mới.",
                    Errors: new[] { "Missing schema DDL file oisms_schema.sql" });
            }

            var executableDdl = CleanDdlForConnectedDatabase(ddlScript);

            // Quản lý Transaction ở một nơi: C# quản lý toàn bộ khối cài đặt DDL + ghi nhận migration
            await using var tx = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await using (var ddlCmd = new NpgsqlCommand(executableDdl, connection, tx))
                {
                    ddlCmd.CommandTimeout = 180;
                    await ddlCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                await EnsureMigrationHistoryRecordedAsync(connection, tx, cancellationToken);
                await tx.CommitAsync(cancellationToken);

                return new MigrationExecutionResult(
                    Success: true,
                    IsFreshInstall: true,
                    IsBaselineAdopted: false,
                    TableCount: RequiredTables.Length,
                    Message: $"Cài mới thành công 29 bảng schema v0.5 và ghi nhận baseline '{InitialBaselineMigrationId}'.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(cancellationToken);
                return new MigrationExecutionResult(
                    Success: false,
                    IsFreshInstall: false,
                    IsBaselineAdopted: false,
                    TableCount: 0,
                    Message: $"Lỗi khi thực thi DDL cài mới: {ex.Message}",
                    Errors: new[] { ex.ToString() });
            }
        }
        else
        {
            // === SCENARIO B: TIẾP NHẬN DATABASE HIỆN HỮU (BASELINE ADOPTION) ===
            // Xác minh toàn diện cấu trúc: bảng, cột, trigger, function, và quyền runtime
            var validationResult = await ValidateSchemaComprehensiveAsync(connection, null, cancellationToken);

            if (!validationResult.IsValid)
            {
                return new MigrationExecutionResult(
                    Success: false,
                    IsFreshInstall: false,
                    IsBaselineAdopted: false,
                    TableCount: businessTables.Count,
                    Message: "Phát hiện sai lệch cấu trúc (Schema drift). Database hiện hữu không tương thích hoàn toàn với schema v0.5.",
                    Errors: validationResult.DriftErrors);
            }

            // Schema hoàn toàn khớp -> Ghi nhận baseline vào __EFMigrationsHistory
            await using var tx = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await EnsureMigrationHistoryRecordedAsync(connection, tx, cancellationToken);
                await tx.CommitAsync(cancellationToken);

                return new MigrationExecutionResult(
                    Success: true,
                    IsFreshInstall: false,
                    IsBaselineAdopted: true,
                    TableCount: businessTables.Count,
                    Message: $"Xác minh thành công 29 bảng, trigger, function và quyền runtime schema v0.5. Đã tiếp nhận baseline '{InitialBaselineMigrationId}'.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(cancellationToken);
                return new MigrationExecutionResult(
                    Success: false,
                    IsFreshInstall: false,
                    IsBaselineAdopted: false,
                    TableCount: businessTables.Count,
                    Message: $"Lỗi khi ghi nhận migration history: {ex.Message}",
                    Errors: new[] { ex.ToString() });
            }
        }
    }

    public static async Task<SchemaValidationResult> ValidateSchemaComprehensiveAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? tx,
        CancellationToken ct)
    {
        var errors = new List<string>();

        // 1. Kiểm tra 29 bảng bắt buộc
        var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string checkTablesSql = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE';";
        await using (var cmd = new NpgsqlCommand(checkTablesSql, connection, tx))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                existingTables.Add(reader.GetString(0));
            }
        }

        var missingTables = RequiredTables.Where(t => !existingTables.Contains(t)).ToList();
        if (missingTables.Count > 0)
        {
            errors.Add($"Thiếu {missingTables.Count} bảng so với schema v0.5: {string.Join(", ", missingTables)}");
        }

        // 2. Kiểm tra đầy đủ 17 trigger bảo vệ quan trọng và trạng thái ENABLED
        const string checkTriggersSql = @"
SELECT 
    c.relname AS table_name,
    t.tgname AS trigger_name,
    t.tgenabled AS status,
    pn.nspname AS function_schema,
    p.proname AS function_name,
    t.tgdeferrable AS is_deferrable,
    t.tginitdeferred AS is_initially_deferred,
    (t.tgtype & 1) <> 0 AS is_row_level,
    CASE 
        WHEN (t.tgtype & 2) <> 0 THEN 'BEFORE'
        WHEN (t.tgtype & 64) <> 0 THEN 'INSTEAD OF'
        ELSE 'AFTER'
    END AS timing,
    (t.tgtype & 4) <> 0 AS has_insert,
    (t.tgtype & 8) <> 0 AS has_delete,
    (t.tgtype & 16) <> 0 AS has_update,
    (t.tgtype & 32) <> 0 AS has_truncate,
    pg_get_expr(t.tgqual, t.tgrelid) AS when_clause,
    (SELECT count(*) FROM unnest(t.tgattr)) AS column_restriction_count
FROM pg_trigger t
JOIN pg_class c ON c.oid = t.tgrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
JOIN pg_proc p ON p.oid = t.tgfoid
JOIN pg_namespace pn ON pn.oid = p.pronamespace
WHERE n.nspname = 'public' AND NOT t.tgisinternal;";

        var triggerMap = new Dictionary<string, (string Table, char Status, string FunctionSchema, string Function, bool IsDeferrable, bool InitiallyDeferred, bool IsRowLevel, string Timing, bool HasInsert, bool HasDelete, bool HasUpdate, bool HasTruncate, string? WhenClause, int ColumnRestrictionCount)>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new NpgsqlCommand(checkTriggersSql, connection, tx))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var tbl = reader.GetString(0);
                var tg = reader.GetString(1);
                var status = reader.GetChar(2);
                var fnSchema = reader.GetString(3);
                var fn = reader.GetString(4);
                var isDef = reader.GetBoolean(5);
                var initDef = reader.GetBoolean(6);
                var isRow = reader.GetBoolean(7);
                var timing = reader.GetString(8);
                var ins = reader.GetBoolean(9);
                var del = reader.GetBoolean(10);
                var upd = reader.GetBoolean(11);
                var trunc = reader.GetBoolean(12);
                var whenClause = reader.IsDBNull(13) ? null : reader.GetString(13);
                var colRestrCount = Convert.ToInt32(reader.GetValue(14));
                triggerMap[$"{tbl}.{tg}"] = (tbl, status, fnSchema, fn, isDef, initDef, isRow, timing, ins, del, upd, trunc, whenClause, colRestrCount);
            }
        }

        foreach (var expectedTrigger in ExpectedTriggers)
        {
            var key = $"{expectedTrigger.TableName}.{expectedTrigger.TriggerName}";
            if (!triggerMap.TryGetValue(key, out var actual))
            {
                errors.Add($"Thiếu trigger bảo vệ '{expectedTrigger.TriggerName}' trên bảng '{expectedTrigger.TableName}'.");
            }
            else
            {
                if (actual.Status != 'O' && actual.Status != 'A')
                {
                    errors.Add($"Trigger '{expectedTrigger.TriggerName}' trên bảng '{expectedTrigger.TableName}' đang bị vô hiệu hóa (status='{actual.Status}').");
                }
                if (!string.Equals(actual.FunctionSchema, expectedTrigger.FunctionSchema, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Trigger '{expectedTrigger.TriggerName}' trên bảng '{expectedTrigger.TableName}' thuộc schema hàm '{actual.FunctionSchema}', kỳ vọng '{expectedTrigger.FunctionSchema}'.");
                }
                if (!string.Equals(actual.Function, expectedTrigger.FunctionName, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Trigger '{expectedTrigger.TriggerName}' trên bảng '{expectedTrigger.TableName}' gọi sai hàm '{actual.Function}', kỳ vọng '{expectedTrigger.FunctionName}'.");
                }
                if (!string.Equals(actual.Timing, expectedTrigger.Timing, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Trigger '{expectedTrigger.TriggerName}' trên bảng '{expectedTrigger.TableName}' sai timing '{actual.Timing}', kỳ vọng '{expectedTrigger.Timing}'.");
                }
                if (actual.IsRowLevel != expectedTrigger.IsRowLevel)
                {
                    errors.Add($"Trigger '{expectedTrigger.TriggerName}' trên bảng '{expectedTrigger.TableName}' sai mức thực thi (thực tế={(actual.IsRowLevel ? "ROW" : "STATEMENT")}, kỳ vọng={(expectedTrigger.IsRowLevel ? "ROW" : "STATEMENT")}).");
                }
                if (actual.HasInsert != expectedTrigger.HasInsert)
                {
                    errors.Add($"Trigger '{expectedTrigger.TriggerName}' trên bảng '{expectedTrigger.TableName}' sai sự kiện INSERT (thực tế={actual.HasInsert}, kỳ vọng={expectedTrigger.HasInsert}).");
                }
                if (actual.HasUpdate != expectedTrigger.HasUpdate)
                {
                    errors.Add($"Trigger '{expectedTrigger.TriggerName}' trên bảng '{expectedTrigger.TableName}' sai sự kiện UPDATE (thực tế={actual.HasUpdate}, kỳ vọng={expectedTrigger.HasUpdate}).");
                }
                if (actual.HasDelete != expectedTrigger.HasDelete)
                {
                    errors.Add($"Trigger '{expectedTrigger.TriggerName}' trên bảng '{expectedTrigger.TableName}' sai sự kiện DELETE (thực tế={actual.HasDelete}, kỳ vọng={expectedTrigger.HasDelete}).");
                }
                if (actual.HasTruncate != expectedTrigger.HasTruncate)
                {
                    errors.Add($"Trigger '{expectedTrigger.TriggerName}' trên bảng '{expectedTrigger.TableName}' sai sự kiện TRUNCATE (thực tế={actual.HasTruncate}, kỳ vọng={expectedTrigger.HasTruncate}).");
                }
                if (actual.IsDeferrable != expectedTrigger.IsDeferrable)
                {
                    errors.Add($"Trigger '{expectedTrigger.TriggerName}' trên bảng '{expectedTrigger.TableName}' sai đặc tính DEFERRABLE (thực tế={actual.IsDeferrable}, kỳ vọng={expectedTrigger.IsDeferrable}).");
                }
                if (actual.InitiallyDeferred != expectedTrigger.InitiallyDeferred)
                {
                    errors.Add($"Trigger '{expectedTrigger.TriggerName}' trên bảng '{expectedTrigger.TableName}' sai đặc tính INITIALLY DEFERRED (thực tế={actual.InitiallyDeferred}, kỳ vọng={expectedTrigger.InitiallyDeferred}).");
                }
                if (expectedTrigger.ExpectedWhenClause == null)
                {
                    if (!string.IsNullOrWhiteSpace(actual.WhenClause))
                    {
                        errors.Add($"Trigger '{expectedTrigger.TriggerName}' trên bảng '{expectedTrigger.TableName}' chứa điều kiện WHEN không hợp lệ ('{actual.WhenClause}'), kỳ vọng không có điều kiện (unconditional).");
                    }
                }
                else
                {
                    var actualNorm = NormalizeExpression(actual.WhenClause);
                    var expectedNorm = NormalizeExpression(expectedTrigger.ExpectedWhenClause);
                    if (!string.Equals(actualNorm, expectedNorm, StringComparison.Ordinal))
                    {
                        errors.Add($"Trigger '{expectedTrigger.TriggerName}' trên bảng '{expectedTrigger.TableName}' sai điều kiện WHEN (thực tế='{actual.WhenClause}', kỳ vọng='{expectedTrigger.ExpectedWhenClause}').");
                    }
                }
                if (expectedTrigger.DisallowColumnRestrictions && actual.ColumnRestrictionCount > 0)
                {
                    errors.Add($"Trigger '{expectedTrigger.TriggerName}' trên bảng '{expectedTrigger.TableName}' bị giới hạn theo danh sách cột (column_count={actual.ColumnRestrictionCount}), kỳ vọng áp dụng toàn bộ bảng.");
                }
            }
        }

        // 3. Kiểm tra các Foreign Key quan trọng bảo vệ tính cô lập Tenant và Catalog/Kho
        const string checkFkSql = @"
SELECT
    c.conname AS constraint_name,
    src_ns.nspname AS source_schema,
    src_tbl.relname AS source_table,
    ARRAY(
        SELECT a.attname
        FROM unnest(c.conkey) WITH ORDINALITY AS k(attnum, ord)
        JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.attnum
        ORDER BY k.ord
    ) AS source_columns,
    tgt_ns.nspname AS target_schema,
    tgt_tbl.relname AS target_table,
    ARRAY(
        SELECT a.attname
        FROM unnest(c.confkey) WITH ORDINALITY AS k(attnum, ord)
        JOIN pg_attribute a ON a.attrelid = c.confrelid AND a.attnum = k.attnum
        ORDER BY k.ord
    ) AS target_columns,
    c.confdeltype AS on_delete,
    c.confupdtype AS on_update,
    c.condeferrable AS is_deferrable,
    c.condeferred AS is_initially_deferred,
    c.convalidated AS is_validated
FROM pg_constraint c
JOIN pg_class src_tbl ON src_tbl.oid = c.conrelid
JOIN pg_namespace src_ns ON src_ns.oid = c.connamespace
JOIN pg_class tgt_tbl ON tgt_tbl.oid = c.confrelid
JOIN pg_namespace tgt_ns ON tgt_ns.oid = tgt_tbl.relnamespace
WHERE src_ns.nspname = 'public' AND c.contype = 'f';";

        var fkMap = new Dictionary<string, (string SourceSchema, string SourceTable, string[] SourceColumns, string TargetSchema, string TargetTable, string[] TargetColumns, char OnDelete, char OnUpdate, bool IsDeferrable, bool InitiallyDeferred, bool IsValidated)>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new NpgsqlCommand(checkFkSql, connection, tx))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var cname = reader.GetString(0);
                var srcSchema = reader.GetString(1);
                var srcTbl = reader.GetString(2);
                var srcCols = (string[])reader.GetValue(3);
                var tgtSchema = reader.GetString(4);
                var tgtTbl = reader.GetString(5);
                var tgtCols = (string[])reader.GetValue(6);
                var onDel = reader.GetChar(7);
                var onUpd = reader.GetChar(8);
                var isDef = reader.GetBoolean(9);
                var initDef = reader.GetBoolean(10);
                var isValidated = reader.GetBoolean(11);
                fkMap[cname] = (srcSchema, srcTbl, srcCols, tgtSchema, tgtTbl, tgtCols, onDel, onUpd, isDef, initDef, isValidated);
            }
        }

        foreach (var expectedFk in ExpectedForeignKeys)
        {
            if (!fkMap.TryGetValue(expectedFk.ConstraintName, out var actual))
            {
                errors.Add($"Thiếu khóa ngoại '{expectedFk.ConstraintName}' trên bảng '{expectedFk.SourceTable}'.");
            }
            else
            {
                if (!string.Equals(actual.SourceSchema, expectedFk.SourceSchema, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Khóa ngoại '{expectedFk.ConstraintName}' thuộc schema nguồn '{actual.SourceSchema}', kỳ vọng '{expectedFk.SourceSchema}'.");
                }
                if (!string.Equals(actual.SourceTable, expectedFk.SourceTable, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Khóa ngoại '{expectedFk.ConstraintName}' gắn sai bảng nguồn '{actual.SourceTable}', kỳ vọng '{expectedFk.SourceTable}'.");
                }
                if (!actual.SourceColumns.SequenceEqual(expectedFk.SourceColumns, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"Khóa ngoại '{expectedFk.ConstraintName}' trên bảng '{expectedFk.SourceTable}' định nghĩa sai cột nguồn [{string.Join(", ", actual.SourceColumns)}], kỳ vọng [{string.Join(", ", expectedFk.SourceColumns)}].");
                }
                if (!string.Equals(actual.TargetSchema, expectedFk.TargetSchema, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Khóa ngoại '{expectedFk.ConstraintName}' trỏ schema đích '{actual.TargetSchema}', kỳ vọng '{expectedFk.TargetSchema}'.");
                }
                if (!string.Equals(actual.TargetTable, expectedFk.TargetTable, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Khóa ngoại '{expectedFk.ConstraintName}' trỏ sai bảng đích '{actual.TargetTable}', kỳ vọng '{expectedFk.TargetTable}'.");
                }
                if (!actual.TargetColumns.SequenceEqual(expectedFk.TargetColumns, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"Khóa ngoại '{expectedFk.ConstraintName}' trỏ sai cột đích [{string.Join(", ", actual.TargetColumns)}], kỳ vọng [{string.Join(", ", expectedFk.TargetColumns)}].");
                }
                if (actual.OnDelete != expectedFk.OnDelete)
                {
                    errors.Add($"Khóa ngoại '{expectedFk.ConstraintName}' trên bảng '{expectedFk.SourceTable}' sai hành vi ON DELETE (thực tế='{actual.OnDelete}', kỳ vọng='{expectedFk.OnDelete}').");
                }
                if (actual.OnUpdate != expectedFk.OnUpdate)
                {
                    errors.Add($"Khóa ngoại '{expectedFk.ConstraintName}' trên bảng '{expectedFk.SourceTable}' sai hành vi ON UPDATE (thực tế='{actual.OnUpdate}', kỳ vọng='{expectedFk.OnUpdate}').");
                }
                if (actual.IsDeferrable != expectedFk.IsDeferrable)
                {
                    errors.Add($"Khóa ngoại '{expectedFk.ConstraintName}' trên bảng '{expectedFk.SourceTable}' sai đặc tính DEFERRABLE (thực tế={actual.IsDeferrable}, kỳ vọng={expectedFk.IsDeferrable}).");
                }
                if (actual.InitiallyDeferred != expectedFk.InitiallyDeferred)
                {
                    errors.Add($"Khóa ngoại '{expectedFk.ConstraintName}' trên bảng '{expectedFk.SourceTable}' sai đặc tính INITIALLY DEFERRED (thực tế={actual.InitiallyDeferred}, kỳ vọng={expectedFk.InitiallyDeferred}).");
                }
                if (actual.IsValidated != expectedFk.IsValidated)
                {
                    errors.Add($"Khóa ngoại '{expectedFk.ConstraintName}' trên bảng '{expectedFk.SourceTable}' chưa được validate (convalidated={actual.IsValidated}).");
                }
            }
        }

        // 4. Kiểm tra các ràng buộc CHECK và UNIQUE cốt lõi bảo vệ tồn kho, số lượng, giá và ledger sequence
        const string checkConstraintsSql = @"
SELECT
    c.conname AS constraint_name,
    c.contype AS constraint_type,
    src_tbl.relname AS source_table,
    ARRAY(
        SELECT a.attname
        FROM unnest(c.conkey) WITH ORDINALITY AS k(attnum, ord)
        JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.attnum
        ORDER BY k.ord
    ) AS source_columns,
    c.convalidated AS is_validated,
    CASE 
        WHEN c.contype = 'c' THEN pg_get_expr(c.conbin, c.conrelid)
        ELSE NULL
    END AS expr
FROM pg_constraint c
JOIN pg_class src_tbl ON src_tbl.oid = c.conrelid
JOIN pg_namespace n ON n.oid = c.connamespace
WHERE n.nspname = 'public' AND c.contype IN ('c', 'u');";

        var constraintMap = new Dictionary<string, (char ConstraintType, string TableName, string[] Columns, bool IsValidated, string? Expression)>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new NpgsqlCommand(checkConstraintsSql, connection, tx))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var cname = reader.GetString(0);
                var ctype = reader.GetChar(1);
                var tbl = reader.GetString(2);
                var cols = (string[])reader.GetValue(3);
                var isValidated = reader.GetBoolean(4);
                var expr = reader.IsDBNull(5) ? null : reader.GetString(5);
                constraintMap[cname] = (ctype, tbl, cols, isValidated, expr);
            }
        }

        foreach (var expectedCk in ExpectedCriticalConstraints)
        {
            if (!constraintMap.TryGetValue(expectedCk.ConstraintName, out var actual))
            {
                errors.Add($"Thiếu ràng buộc bảo vệ '{expectedCk.ConstraintName}' (loại {(expectedCk.ConstraintType == 'c' ? "CHECK" : "UNIQUE")}) trên bảng '{expectedCk.TableName}'.");
            }
            else
            {
                if (actual.ConstraintType != expectedCk.ConstraintType)
                {
                    errors.Add($"Ràng buộc '{expectedCk.ConstraintName}' sai loại (thực tế='{actual.ConstraintType}', kỳ vọng='{expectedCk.ConstraintType}').");
                }
                if (!string.Equals(actual.TableName, expectedCk.TableName, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Ràng buộc '{expectedCk.ConstraintName}' gắn sai bảng '{actual.TableName}', kỳ vọng '{expectedCk.TableName}'.");
                }
                if (actual.IsValidated != expectedCk.IsValidated)
                {
                    errors.Add($"Ràng buộc '{expectedCk.ConstraintName}' trên bảng '{expectedCk.TableName}' chưa được validate (convalidated={actual.IsValidated}).");
                }
                if (expectedCk.Columns != null && !actual.Columns.SequenceEqual(expectedCk.Columns, StringComparer.OrdinalIgnoreCase))
                {
                    errors.Add($"Ràng buộc '{expectedCk.ConstraintName}' trên bảng '{expectedCk.TableName}' định nghĩa sai thứ tự hoặc danh sách cột [{string.Join(", ", actual.Columns)}], kỳ vọng [{string.Join(", ", expectedCk.Columns)}].");
                }
                if (expectedCk.ConstraintType == 'c' && !string.IsNullOrWhiteSpace(expectedCk.ExpectedNormalizedExpression))
                {
                    var actualNorm = NormalizeExpression(actual.Expression);
                    var expectedNorm = NormalizeExpression(expectedCk.ExpectedNormalizedExpression);
                    if (!string.Equals(actualNorm, expectedNorm, StringComparison.Ordinal))
                    {
                        errors.Add($"Ràng buộc CHECK '{expectedCk.ConstraintName}' trên bảng '{expectedCk.TableName}' sai biểu thức kiểm tra (thực tế='{actual.Expression}', kỳ vọng='{expectedCk.ExpectedNormalizedExpression}').");
                    }
                }
            }
        }

        // 5. Kiểm tra các hàm bảo vệ schema v0.5 trong pg_proc
        const string checkProcsSql = @"
SELECT proname FROM pg_proc p
JOIN pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname = 'public';";

        var existingProcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new NpgsqlCommand(checkProcsSql, connection, tx))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                existingProcs.Add(reader.GetString(0));
            }
        }

        string[] requiredFunctions = ["oisms_deny_mutation", "oisms_lock_stock", "oisms_stock_constraint", "oisms_check_stock"];
        foreach (var fn in requiredFunctions)
        {
            if (!existingProcs.Contains(fn))
            {
                errors.Add($"Thiếu hàm bảo vệ schema v0.5 '{fn}'.");
            }
        }

        // 6. Kiểm tra các cột trọng yếu của schema v0.5
        const string checkColumnsSql = @"
SELECT table_name, column_name 
FROM information_schema.columns 
WHERE table_schema = 'public';";

        var columnMap = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new NpgsqlCommand(checkColumnsSql, connection, tx))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                columnMap.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
            }
        }

        string[] crucialColumns = [
            "users.role", "users.tenant_id", "users.display_name", "users.phone",
            "skus.code", "skus.attributes", "skus.retail_price", "skus.wholesale_price",
            "inventory_transactions.sequence_no", "inventory_transactions.balance_after", "inventory_transactions.type",
            "inventory_balances.on_hand", "inventory_balances.reserved", "inventory_balances.average_cost", "inventory_balances.last_sequence"
        ];
        foreach (var col in crucialColumns)
        {
            if (!columnMap.Contains(col))
            {
                errors.Add($"Thiếu cột trọng yếu '{col}' của schema v0.5.");
            }
        }

        // 7. Kiểm tra quyền role runtime oisms_runtime
        const string checkRuntimeRoleSql = "SELECT 1 FROM pg_roles WHERE rolname = 'oisms_runtime';";
        await using (var cmd = new NpgsqlCommand(checkRuntimeRoleSql, connection, tx))
        {
            var exists = await cmd.ExecuteScalarAsync(ct);
            if (exists == null)
            {
                errors.Add("Thiếu role nhóm quyền 'oisms_runtime' trong cụm PostgreSQL.");
            }
        }

        // Kiểm tra oisms_runtime KHÔNG có quyền UPDATE hoặc DELETE hoặc TRUNCATE trên inventory_transactions
        const string checkPrivilegesSql = @"
SELECT privilege_type 
FROM information_schema.role_table_grants 
WHERE grantee = 'oisms_runtime' 
  AND table_schema = 'public' 
  AND table_name = 'inventory_transactions' 
  AND privilege_type IN ('UPDATE', 'DELETE', 'TRUNCATE');";

        await using (var cmd = new NpgsqlCommand(checkPrivilegesSql, connection, tx))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                errors.Add($"Vi phạm an ninh quyền runtime: 'oisms_runtime' bị cấp quyền '{reader.GetString(0)}' trên bảng append-only 'inventory_transactions'.");
            }
        }

        return new SchemaValidationResult(errors.Count == 0, errors);
    }

    private static async Task EnsureMigrationHistoryRecordedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        CancellationToken ct)
    {
        const string createHistoryTableSql = @"
CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (
    ""MigrationId"" character varying(150) NOT NULL,
    ""ProductVersion"" character varying(32) NOT NULL,
    CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY (""MigrationId"")
);";
        await using (var cmd = new NpgsqlCommand(createHistoryTableSql, connection, tx))
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }

        const string recordBaselineSql = @"
INSERT INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"")
VALUES (@migrationId, @productVersion)
ON CONFLICT (""MigrationId"") DO NOTHING;";

        await using (var cmd = new NpgsqlCommand(recordBaselineSql, connection, tx))
        {
            cmd.Parameters.AddWithValue("migrationId", InitialBaselineMigrationId);
            cmd.Parameters.AddWithValue("productVersion", ProductVersion);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public static string? FindAndLoadSchemaDdl()
    {
        // 1. Kiểm tra biến môi trường nếu có
        var envPath = Environment.GetEnvironmentVariable("OISM_SCHEMA_SQL_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return File.ReadAllText(envPath);
        }

        // 2. Duyệt ngược cây thư mục từ BaseDirectory và CurrentDirectory
        var startDirs = new[]
        {
            AppDomain.CurrentDomain.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var startDir in startDirs)
        {
            var dir = new DirectoryInfo(startDir);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                var candidate1 = Path.Combine(dir.FullName, "docs", "database", "oisms_schema.sql");
                if (File.Exists(candidate1)) return File.ReadAllText(candidate1);

                var candidate2 = Path.Combine(dir.FullName, "database", "oisms_schema.sql");
                if (File.Exists(candidate2)) return File.ReadAllText(candidate2);

                var candidate3 = Path.Combine(dir.FullName, "oisms_schema.sql");
                if (File.Exists(candidate3)) return File.ReadAllText(candidate3);

                dir = dir.Parent;
            }
        }

        return null;
    }

    public static string CleanDdlForConnectedDatabase(string ddl)
    {
        var lines = ddl.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var resultLines = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // 1. Loại bỏ các lệnh psql meta-commands bắt đầu bằng dấu '\' (VD: \set, \connect, \getenv, \q...)
            if (trimmed.StartsWith('\\'))
            {
                continue;
            }

            // 2. Loại bỏ câu lệnh CREATE DATABASE vì connection đã kết nối trực tiếp vào target database
            if (trimmed.StartsWith("CREATE DATABASE", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 3. Quản lý transaction ở một nơi: Loại bỏ BEGIN; và COMMIT; độc lập ở cấp cao nhất
            // để NpgsqlTransaction trong C# bao bọc toàn bộ DDL + ghi nhận baseline một cách nguyên tử (ACID).
            // LƯU Ý: Không loại bỏ "BEGIN" không có dấu chấm phẩy vì đó là từ khóa bắt đầu khối lệnh của hàm PL/pgSQL!
            if (trimmed.Equals("BEGIN;", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("BEGIN TRANSACTION;", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (trimmed.Equals("COMMIT;", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("COMMIT TRANSACTION;", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            resultLines.Add(line);
        }

        return string.Join("\n", resultLines);
    }

    public static string NormalizeExpression(string? expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) return string.Empty;

        var sb = new System.Text.StringBuilder(expr.Length);
        int i = 0;
        int len = expr.Length;
        bool lastWasWord = false;

        while (i < len)
        {
            char c = expr[i];

            // 1. Quoted String Literal: '...' (với '' để biểu thị ký tự nháy đơn escape)
            if (c == '\'')
            {
                sb.Append('\'');
                i++;
                while (i < len)
                {
                    if (expr[i] == '\'')
                    {
                        sb.Append('\'');
                        i++;
                        if (i < len && expr[i] == '\'')
                        {
                            sb.Append('\'');
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(expr[i]);
                        i++;
                    }
                }
                lastWasWord = false;
                continue;
            }

            // 2. Quoted Identifier: "..." (với "" để biểu thị nháy kép escape)
            if (c == '"')
            {
                sb.Append('"');
                i++;
                while (i < len)
                {
                    if (expr[i] == '"')
                    {
                        sb.Append('"');
                        i++;
                        if (i < len && expr[i] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(expr[i]);
                        i++;
                    }
                }
                lastWasWord = false;
                continue;
            }

            // 3. Khoảng trắng bên ngoài chuỗi: bỏ qua nhưng đánh dấu ranh giới giữa các từ
            if (char.IsWhiteSpace(c))
            {
                while (i < len && char.IsWhiteSpace(expr[i]))
                {
                    i++;
                }
                continue;
            }

            // 4. Ký tự chữ/số/gạch dưới (keywords, types, unquoted identifiers, số): lowercase, thêm space nếu từ đứng liền kề từ
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                if (lastWasWord)
                {
                    sb.Append(' ');
                }
                while (i < len && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_'))
                {
                    sb.Append(char.ToLowerInvariant(expr[i]));
                    i++;
                }
                lastWasWord = true;
                continue;
            }

            // 5. Ký tự phân cách / toán tử / dấu câu (::, >=, <=, <>, !=, (, ), [, ], ,, =, v.v.)
            lastWasWord = false;

            if (c == ':' && i + 1 < len && expr[i + 1] == ':')
            {
                sb.Append("::");
                i += 2;
                continue;
            }
            if ((c == '<' || c == '>' || c == '!') && i + 1 < len && (expr[i + 1] == '=' || expr[i + 1] == '>'))
            {
                sb.Append(c);
                sb.Append(expr[i + 1]);
                i += 2;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return StripOuterParentheses(sb.ToString());
    }

    private static string StripOuterParentheses(string s)
    {
        while (s.Length >= 2 && s.StartsWith('(') && s.EndsWith(')'))
        {
            int depth = 0;
            bool wrapsEntire = true;
            for (int i = 0; i < s.Length - 1; i++)
            {
                if (s[i] == '\'')
                {
                    i++;
                    while (i < s.Length - 1)
                    {
                        if (s[i] == '\'')
                        {
                            if (i + 1 < s.Length - 1 && s[i + 1] == '\'')
                            {
                                i += 2;
                            }
                            else
                            {
                                break;
                            }
                        }
                        else
                        {
                            i++;
                        }
                    }
                    continue;
                }

                if (s[i] == '"')
                {
                    i++;
                    while (i < s.Length - 1)
                    {
                        if (s[i] == '"')
                        {
                            if (i + 1 < s.Length - 1 && s[i + 1] == '"')
                            {
                                i += 2;
                            }
                            else
                            {
                                break;
                            }
                        }
                        else
                        {
                            i++;
                        }
                    }
                    continue;
                }

                if (s[i] == '(') depth++;
                else if (s[i] == ')') depth--;
                if (depth == 0)
                {
                    wrapsEntire = false;
                    break;
                }
            }
            if (wrapsEntire)
            {
                s = s.Substring(1, s.Length - 2);
            }
            else
            {
                break;
            }
        }
        return s;
    }
}

