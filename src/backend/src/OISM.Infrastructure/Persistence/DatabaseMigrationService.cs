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
        "replenishment_suggestions",
        "shop_integrations",
        "units",
        "sku_units",
        "sku_unit_factor_history",
        "price_lists",
        "price_change_requests",
        "price_change_request_items",
        "sku_unit_prices"
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
        new("order_items", "item_cost_snapshot", "oisms_order_cost_constraint", "public", "AFTER", true, HasInsert: true, HasUpdate: true, IsDeferrable: true, InitiallyDeferred: true),

        // sku_unit_factor_history
        new("goods_receipt_items", "protect_receipt_item", "oisms_protect_receipt_item", "public", "BEFORE", true, HasUpdate: true, HasDelete: true),
        new("sku_unit_factor_history", "factor_history_append_only", "oisms_deny_mutation", "public", "BEFORE", false, HasUpdate: true, HasDelete: true, HasTruncate: true),

        // price_change_requests
        new("price_change_requests", "price_change_request_guard", "oisms_price_change_request_guard", "public", "BEFORE", true, HasUpdate: true),
        new("price_change_requests", "price_change_request_no_delete", "oisms_deny_mutation", "public", "BEFORE", false, HasDelete: true, HasTruncate: true),

        // price_change_request_items
        new("price_change_request_items", "price_change_item_append_only", "oisms_deny_mutation", "public", "BEFORE", false, HasUpdate: true, HasDelete: true, HasTruncate: true),

        // sku_unit_prices
        new("sku_unit_prices", "sku_unit_price_guard", "oisms_sku_unit_price_guard", "public", "BEFORE", true, HasUpdate: true),
        new("sku_unit_prices", "sku_unit_price_no_delete", "oisms_deny_mutation", "public", "BEFORE", false, HasDelete: true, HasTruncate: true)
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
        new("fk_order_items_sku_id", "order_items", ["tenant_id", "sku_id"], "skus", ["tenant_id", "id"]),
        new("fk_stock_counts_generated_adjustment", "stock_counts", ["tenant_id", "generated_adjustment_id", "branch_id", "warehouse_id"], "inventory_adjustments", ["tenant_id", "id", "branch_id", "warehouse_id"]),
        new("fk_shop_integrations_tenant", "shop_integrations", ["tenant_id"], "tenants", ["id"]),
        new("fk_shop_integrations_branch", "shop_integrations", ["tenant_id", "branch_id"], "branches", ["tenant_id", "id"]),

        // Units & Multi-unit Foreign Keys
        new("fk_units_tenant_id", "units", ["tenant_id"], "tenants", ["id"]),
        new("fk_skus_base_unit_id", "skus", ["tenant_id", "base_unit_id"], "units", ["tenant_id", "id"]),
        new("fk_sku_units_tenant_id", "sku_units", ["tenant_id"], "tenants", ["id"]),
        new("fk_sku_units_sku_id", "sku_units", ["tenant_id", "sku_id"], "skus", ["tenant_id", "id"]),
        new("fk_sku_units_unit_id", "sku_units", ["tenant_id", "unit_id"], "units", ["tenant_id", "id"]),
        new("fk_sku_units_updated_by", "sku_units", ["tenant_id", "updated_by"], "users", ["tenant_id", "id"]),
        new("fk_sku_unit_factor_history_tenant_id", "sku_unit_factor_history", ["tenant_id"], "tenants", ["id"]),
        new("fk_sku_unit_factor_history_sku_unit_id", "sku_unit_factor_history", ["tenant_id", "sku_unit_id"], "sku_units", ["tenant_id", "id"]),
        new("fk_sku_unit_factor_history_changed_by", "sku_unit_factor_history", ["tenant_id", "changed_by"], "users", ["tenant_id", "id"]),
        new("fk_goods_receipt_items_unit_id", "goods_receipt_items", ["tenant_id", "unit_id"], "units", ["tenant_id", "id"]),
        new("fk_order_items_unit_id", "order_items", ["tenant_id", "unit_id"], "units", ["tenant_id", "id"]),

        // Price Lists & Sku Unit Prices Foreign Keys
        new("fk_price_lists_tenant_id", "price_lists", ["tenant_id"], "tenants", ["id"]),
        new("fk_price_change_requests_tenant_id", "price_change_requests", ["tenant_id"], "tenants", ["id"]),
        new("fk_price_change_requests_created_by", "price_change_requests", ["tenant_id", "created_by"], "users", ["tenant_id", "id"]),
        new("fk_price_change_requests_reviewed_by", "price_change_requests", ["tenant_id", "reviewed_by"], "users", ["tenant_id", "id"]),
        new("fk_price_change_request_items_tenant_id", "price_change_request_items", ["tenant_id"], "tenants", ["id"]),
        new("fk_price_change_request_items_request_id", "price_change_request_items", ["tenant_id", "request_id"], "price_change_requests", ["tenant_id", "id"]),
        new("fk_price_change_request_items_price_list_id", "price_change_request_items", ["tenant_id", "price_list_id"], "price_lists", ["tenant_id", "id"]),
        new("fk_price_change_request_items_sku_unit", "price_change_request_items", ["tenant_id", "sku_id", "unit_id"], "sku_units", ["tenant_id", "sku_id", "unit_id"]),
        new("fk_sku_unit_prices_tenant_id", "sku_unit_prices", ["tenant_id"], "tenants", ["id"]),
        new("fk_sku_unit_prices_price_list_id", "sku_unit_prices", ["tenant_id", "price_list_id"], "price_lists", ["tenant_id", "id"]),
        new("fk_sku_unit_prices_source_item_id", "sku_unit_prices", ["tenant_id", "source_item_id"], "price_change_request_items", ["tenant_id", "id"]),
        new("fk_sku_unit_prices_sku_unit", "sku_unit_prices", ["tenant_id", "sku_id", "unit_id"], "sku_units", ["tenant_id", "sku_id", "unit_id"]),
        new("fk_sku_unit_prices_cancelled_by", "sku_unit_prices", ["tenant_id", "cancelled_by"], "users", ["tenant_id", "id"]),
        new("fk_order_items_sku_unit_price_id", "order_items", ["tenant_id", "sku_unit_price_id"], "sku_unit_prices", ["tenant_id", "id"])
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
        new("ck_order_items_cost_price_nonnegative", 'c', "order_items", ExpectedNormalizedExpression: "(cost_price >= (0)::numeric)"),
        new("uq_stock_transfers_3", 'u', "stock_transfers", Columns: ["tenant_id", "request_key"]),
        new("uq_stock_counts_3", 'u', "stock_counts", Columns: ["tenant_id", "request_key"]),
        new("uq_stock_counts_4", 'u', "stock_counts", Columns: ["tenant_id", "generated_adjustment_id"]),
        new("uq_inventory_adjustments_3", 'u', "inventory_adjustments", Columns: ["tenant_id", "id", "branch_id", "warehouse_id"]),
        new("uq_shop_integrations_2", 'u', "shop_integrations", Columns: ["tenant_id", "channel", "external_shop_id"]),
        new("uq_shop_integrations_secret_hash", 'u', "shop_integrations", Columns: ["secret_hash"]),
        new("ck_shop_integrations_channel", 'c', "shop_integrations", ExpectedNormalizedExpression: "((channel)::text = ANY ((ARRAY['Shopee'::character varying, 'TikTok'::character varying, 'Lazada'::character varying])::text[]))"),
        new("uq_orders_4", 'u', "orders", Columns: ["tenant_id", "channel", "external_shop_id", "external_order_id"]),

        // units
        new("uq_units_1", 'u', "units", Columns: ["tenant_id", "id"]),
        new("uq_units_2", 'u', "units", Columns: ["tenant_id", "code"]),
        new("ck_units_measure_group", 'c', "units", ExpectedNormalizedExpression: "((measure_group)::text = ANY ((ARRAY['Mass'::character varying, 'Volume'::character varying, 'Length'::character varying, 'Count'::character varying, 'Packaging'::character varying])::text[]))"),
        new("ck_units_decimal_places", 'c', "units", ExpectedNormalizedExpression: "((decimal_places >= 0) AND (decimal_places <= 3))"),
        new("ck_units_code_normalized", 'c', "units", ExpectedNormalizedExpression: "(((code)::text = lower(btrim((code)::text))) AND (length((code)::text) > 0))"),

        // sku_units
        new("uq_sku_units_1", 'u', "sku_units", Columns: ["tenant_id", "id"]),
        new("uq_sku_units_2", 'u', "sku_units", Columns: ["tenant_id", "sku_id", "unit_id"]),
        new("ck_sku_units_factor_positive", 'c', "sku_units", ExpectedNormalizedExpression: "(factor_to_base > (0)::numeric)"),
        new("ck_sku_units_sale_price_nonnegative", 'c', "sku_units", ExpectedNormalizedExpression: "((sale_price IS NULL) OR (sale_price >= (0)::numeric))"),
        new("ck_sku_units_decimal_places", 'c', "sku_units", ExpectedNormalizedExpression: "((quantity_decimal_places >= 0) AND (quantity_decimal_places <= 3))"),

        // sku_unit_factor_history
        new("uq_sku_unit_factor_history_1", 'u', "sku_unit_factor_history", Columns: ["tenant_id", "id"]),
        new("ck_sku_unit_factor_history_new_factor", 'c', "sku_unit_factor_history", ExpectedNormalizedExpression: "(new_factor > (0)::numeric)"),

        // order_items & goods_receipt_items snapshot constraints
        new("ck_order_items_factor_snapshot_positive", 'c', "order_items", ExpectedNormalizedExpression: "(factor_snapshot > (0)::numeric)"),
        new("ck_goods_receipt_items_factor_snapshot_positive", 'c', "goods_receipt_items", ExpectedNormalizedExpression: "(factor_snapshot > (0)::numeric)"),
        new("uq_order_items_2", 'u', "order_items", Columns: ["tenant_id", "order_id", "sku_id", "unit_id"]),

        // price_lists
        new("uq_price_lists_1", 'u', "price_lists", Columns: ["tenant_id", "id"]),
        new("uq_price_lists_2", 'u', "price_lists", Columns: ["tenant_id", "code"]),
        new("ck_price_lists_code", 'c', "price_lists", ExpectedNormalizedExpression: "((code)::text = ANY ((ARRAY['retail'::character varying, 'wholesale'::character varying])::text[]))"),

        // price_change_requests
        new("uq_price_change_requests_1", 'u', "price_change_requests", Columns: ["tenant_id", "id"]),
        new("uq_price_change_requests_2", 'u', "price_change_requests", Columns: ["tenant_id", "request_key"]),
        new("ck_price_change_requests_kind", 'c', "price_change_requests", ExpectedNormalizedExpression: "((kind)::text = ANY ((ARRAY['Opening'::character varying, 'Initial'::character varying, 'Change'::character varying])::text[]))"),
        new("ck_price_change_requests_status", 'c', "price_change_requests", ExpectedNormalizedExpression: "((status)::text = ANY ((ARRAY['Pending'::character varying, 'Approved'::character varying, 'Rejected'::character varying])::text[]))"),
        new("ck_price_change_requests_created_by", 'c', "price_change_requests", ExpectedNormalizedExpression: "(((kind)::text = 'Opening'::text) OR (created_by IS NOT NULL))"),
        new("ck_price_change_requests_pending_reviewed", 'c', "price_change_requests", ExpectedNormalizedExpression: "(((status)::text = 'Pending'::text) = (reviewed_at IS NULL))"),
        new("ck_price_change_requests_reason", 'c', "price_change_requests", ExpectedNormalizedExpression: "(btrim((reason)::text) <> ''::text)"),

        // price_change_request_items
        new("uq_price_change_request_items_1", 'u', "price_change_request_items", Columns: ["tenant_id", "id"]),
        new("uq_price_change_request_items_2", 'u', "price_change_request_items", Columns: ["tenant_id", "request_id", "price_list_id", "sku_id", "unit_id"]),
        new("ck_price_change_request_items_new_price", 'c', "price_change_request_items", ExpectedNormalizedExpression: "(new_price >= (0)::numeric)"),

        // sku_unit_prices
        new("uq_sku_unit_prices_1", 'u', "sku_unit_prices", Columns: ["tenant_id", "id"]),
        new("uq_sku_unit_prices_2", 'u', "sku_unit_prices", Columns: ["tenant_id", "source_item_id"]),
        new("ck_sku_unit_prices_price", 'c', "sku_unit_prices", ExpectedNormalizedExpression: "(price >= (0)::numeric)"),
        new("ck_sku_unit_prices_cancel_state", 'c', "sku_unit_prices", ExpectedNormalizedExpression: "(((cancelled_at IS NULL) AND (cancelled_by IS NULL) AND (cancel_reason IS NULL)) OR ((cancelled_at IS NOT NULL) AND (cancelled_by IS NOT NULL) AND (cancel_reason IS NOT NULL) AND (btrim((cancel_reason)::text) <> ''::text)))"),

        // order_items price source constraints
        new("ck_order_items_price_source", 'c', "order_items", ExpectedNormalizedExpression: "((price_source)::text = ANY ((ARRAY['PriceList'::character varying, 'Marketplace'::character varying, 'Legacy'::character varying])::text[]))"),
        new("ck_order_items_price_source_ref", 'c', "order_items", ExpectedNormalizedExpression: "(((price_source)::text = 'PriceList'::text) = (sku_unit_price_id IS NOT NULL))")
    ];


    public async Task<MigrationExecutionResult> UpgradeDatabaseSchemaAsync(
        string adminConnectionString,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            // Kiểm tra trùng lặp secret_hash nếu bảng shop_integrations đã tồn tại
            const string checkDupSql = @"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables 
        WHERE table_schema = 'public' AND table_name = 'shop_integrations'
    ) THEN
        IF EXISTS (
            SELECT secret_hash FROM shop_integrations GROUP BY secret_hash HAVING count(*) > 1
        ) THEN
            RAISE EXCEPTION 'Phát hiện trùng lặp secret_hash trong shop_integrations. Yêu cầu xử lý thủ công trước khi áp dụng ràng buộc duy nhất uq_shop_integrations_secret_hash.';
        END IF;
    END IF;
END $$;";
            await using (var checkCmd = new NpgsqlCommand(checkDupSql, connection, tx))
            {
                await checkCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            const string upgradeSql = @"
ALTER TABLE stock_transfers ADD COLUMN IF NOT EXISTS request_key uuid;
ALTER TABLE stock_transfers ADD COLUMN IF NOT EXISTS payload_signature varchar(128);
ALTER TABLE stock_counts ADD COLUMN IF NOT EXISTS request_key uuid;
ALTER TABLE stock_counts ADD COLUMN IF NOT EXISTS payload_signature varchar(128);
ALTER TABLE stock_counts ADD COLUMN IF NOT EXISTS generated_adjustment_id uuid;
ALTER TABLE orders ADD COLUMN IF NOT EXISTS webhook_payload_signature varchar(64);
CREATE TABLE IF NOT EXISTS shop_integrations (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    channel varchar(20) NOT NULL,
    external_shop_id varchar(100) NOT NULL,
    branch_id uuid NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    secret_hash varchar(255) NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    updated_at timestamptz,
    CONSTRAINT uq_shop_integrations_1 UNIQUE (tenant_id, id),
    CONSTRAINT uq_shop_integrations_2 UNIQUE (tenant_id, channel, external_shop_id),
    CONSTRAINT uq_shop_integrations_secret_hash UNIQUE (secret_hash),
    CONSTRAINT ck_shop_integrations_channel CHECK (channel IN ('Shopee', 'TikTok', 'Lazada'))
);
DO $$ 
BEGIN 
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'uq_stock_transfers_3'
    ) THEN 
        ALTER TABLE stock_transfers ADD CONSTRAINT uq_stock_transfers_3 UNIQUE (tenant_id, request_key); 
    END IF; 
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'uq_stock_counts_3'
    ) THEN 
        ALTER TABLE stock_counts ADD CONSTRAINT uq_stock_counts_3 UNIQUE (tenant_id, request_key); 
    END IF; 
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'uq_stock_counts_4'
    ) THEN 
        ALTER TABLE stock_counts ADD CONSTRAINT uq_stock_counts_4 UNIQUE (tenant_id, generated_adjustment_id); 
    END IF; 
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'uq_inventory_adjustments_3'
    ) THEN 
        ALTER TABLE inventory_adjustments ADD CONSTRAINT uq_inventory_adjustments_3 UNIQUE (tenant_id, id, branch_id, warehouse_id); 
    END IF; 
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_counts_generated_adjustment'
    ) THEN 
        ALTER TABLE stock_counts ADD CONSTRAINT fk_stock_counts_generated_adjustment 
        FOREIGN KEY (tenant_id, generated_adjustment_id, branch_id, warehouse_id) 
        REFERENCES inventory_adjustments (tenant_id, id, branch_id, warehouse_id) 
        ON DELETE RESTRICT ON UPDATE NO ACTION; 
    END IF; 
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_shop_integrations_tenant'
    ) THEN 
        ALTER TABLE shop_integrations ADD CONSTRAINT fk_shop_integrations_tenant 
        FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT ON UPDATE NO ACTION; 
    END IF; 
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_shop_integrations_branch'
    ) THEN 
        ALTER TABLE shop_integrations ADD CONSTRAINT fk_shop_integrations_branch 
        FOREIGN KEY (tenant_id, branch_id) REFERENCES branches (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION; 
    END IF; 
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'uq_shop_integrations_secret_hash'
    ) THEN 
        ALTER TABLE shop_integrations ADD CONSTRAINT uq_shop_integrations_secret_hash UNIQUE (secret_hash); 
    END IF; 
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oisms_runtime') THEN
        REVOKE ALL ON TABLE shop_integrations FROM oisms_runtime;
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE shop_integrations TO oisms_runtime;
    END IF; 
END $$;
";
            await using (var cmd = new NpgsqlCommand(upgradeSql, connection, tx))
            {
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            const string multiUnitUpgradeSql = @"
SELECT pg_advisory_xact_lock(hashtext('oisms_multi_unit_schema_upgrade'));

-- 1. Tạo 3 bảng mới và các cột mới
CREATE TABLE IF NOT EXISTS public.units (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    code varchar(30) NOT NULL,
    name varchar(100) NOT NULL,
    symbol varchar(20) NOT NULL,
    measure_group varchar(20) NOT NULL,
    decimal_places smallint DEFAULT 3 NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    updated_at timestamptz,
    CONSTRAINT uq_units_1 UNIQUE (tenant_id, id),
    CONSTRAINT uq_units_2 UNIQUE (tenant_id, code),
    CONSTRAINT ck_units_measure_group CHECK (measure_group IN ('Mass', 'Volume', 'Length', 'Count', 'Packaging')),
    CONSTRAINT ck_units_decimal_places CHECK (decimal_places >= 0 AND decimal_places <= 3),
    CONSTRAINT ck_units_code_normalized CHECK (code = lower(btrim(code)) AND length(code) > 0)
);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_units_tenant_id') THEN
        ALTER TABLE public.units ADD CONSTRAINT fk_units_tenant_id
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS public.sku_units (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    sku_id uuid NOT NULL,
    unit_id uuid NOT NULL,
    factor_to_base numeric(18,6) NOT NULL,
    can_purchase boolean DEFAULT true NOT NULL,
    can_sell boolean DEFAULT true NOT NULL,
    is_default_purchase boolean DEFAULT false NOT NULL,
    is_default_sale boolean DEFAULT false NOT NULL,
    barcode varchar(128),
    quantity_decimal_places smallint DEFAULT 3 NOT NULL,
    sale_price numeric(20,2),
    version integer DEFAULT 1 NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    updated_by uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    updated_at timestamptz,
    CONSTRAINT uq_sku_units_1 UNIQUE (tenant_id, id),
    CONSTRAINT uq_sku_units_2 UNIQUE (tenant_id, sku_id, unit_id),
    CONSTRAINT ck_sku_units_factor_positive CHECK (factor_to_base > 0),
    CONSTRAINT ck_sku_units_sale_price_nonnegative CHECK (sale_price IS NULL OR sale_price >= 0),
    CONSTRAINT ck_sku_units_decimal_places CHECK (quantity_decimal_places >= 0 AND quantity_decimal_places <= 3)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_sku_units_default_sale ON public.sku_units (tenant_id, sku_id) WHERE is_default_sale AND is_active;
CREATE UNIQUE INDEX IF NOT EXISTS ux_sku_units_default_purchase ON public.sku_units (tenant_id, sku_id) WHERE is_default_purchase AND is_active;
CREATE UNIQUE INDEX IF NOT EXISTS ux_sku_units_barcode ON public.sku_units (tenant_id, barcode) WHERE barcode IS NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sku_units_tenant_id') THEN
        ALTER TABLE public.sku_units ADD CONSTRAINT fk_sku_units_tenant_id
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sku_units_sku_id') THEN
        ALTER TABLE public.sku_units ADD CONSTRAINT fk_sku_units_sku_id
        FOREIGN KEY (tenant_id, sku_id) REFERENCES public.skus (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sku_units_unit_id') THEN
        ALTER TABLE public.sku_units ADD CONSTRAINT fk_sku_units_unit_id
        FOREIGN KEY (tenant_id, unit_id) REFERENCES public.units (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sku_units_updated_by') THEN
        ALTER TABLE public.sku_units ADD CONSTRAINT fk_sku_units_updated_by
        FOREIGN KEY (tenant_id, updated_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS public.sku_unit_factor_history (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    sku_unit_id uuid NOT NULL,
    old_factor numeric(18,6),
    new_factor numeric(18,6) NOT NULL,
    old_version integer,
    new_version integer NOT NULL,
    changed_by uuid,
    changed_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT uq_sku_unit_factor_history_1 UNIQUE (tenant_id, id),
    CONSTRAINT ck_sku_unit_factor_history_new_factor CHECK (new_factor > 0)
);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sku_unit_factor_history_tenant_id') THEN
        ALTER TABLE public.sku_unit_factor_history ADD CONSTRAINT fk_sku_unit_factor_history_tenant_id
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sku_unit_factor_history_sku_unit_id') THEN
        ALTER TABLE public.sku_unit_factor_history ADD CONSTRAINT fk_sku_unit_factor_history_sku_unit_id
        FOREIGN KEY (tenant_id, sku_unit_id) REFERENCES public.sku_units (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sku_unit_factor_history_changed_by') THEN
        ALTER TABLE public.sku_unit_factor_history ADD CONSTRAINT fk_sku_unit_factor_history_changed_by
        FOREIGN KEY (tenant_id, changed_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
END $$;

ALTER TABLE public.skus ADD COLUMN IF NOT EXISTS base_unit_id uuid;

ALTER TABLE public.order_items ADD COLUMN IF NOT EXISTS unit_id uuid;
ALTER TABLE public.order_items ADD COLUMN IF NOT EXISTS unit_name_snapshot varchar(100) NOT NULL DEFAULT '';
ALTER TABLE public.order_items ADD COLUMN IF NOT EXISTS factor_snapshot numeric(18,6) NOT NULL DEFAULT 1;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_order_items_factor_snapshot_positive') THEN
        ALTER TABLE public.order_items ADD CONSTRAINT ck_order_items_factor_snapshot_positive CHECK (factor_snapshot > 0);
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_schema = 'public' AND table_name = 'order_items' AND column_name = 'base_quantity'
    ) THEN
        ALTER TABLE public.order_items ADD COLUMN base_quantity numeric(18,3) GENERATED ALWAYS AS (quantity * factor_snapshot) STORED;
    END IF;
END $$;

ALTER TABLE public.goods_receipt_items ADD COLUMN IF NOT EXISTS unit_id uuid;
ALTER TABLE public.goods_receipt_items ADD COLUMN IF NOT EXISTS unit_name_snapshot varchar(100) NOT NULL DEFAULT '';
ALTER TABLE public.goods_receipt_items ADD COLUMN IF NOT EXISTS factor_snapshot numeric(18,6) NOT NULL DEFAULT 1;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_goods_receipt_items_factor_snapshot_positive') THEN
        ALTER TABLE public.goods_receipt_items ADD CONSTRAINT ck_goods_receipt_items_factor_snapshot_positive CHECK (factor_snapshot > 0);
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns 
        WHERE table_schema = 'public' AND table_name = 'goods_receipt_items' AND column_name = 'base_quantity'
    ) THEN
        ALTER TABLE public.goods_receipt_items ADD COLUMN base_quantity numeric(18,3) GENERATED ALWAYS AS (quantity * factor_snapshot) STORED;
    END IF;
END $$;

-- 2. Chèn bộ đơn vị mặc định
INSERT INTO public.units (id, tenant_id, code, name, symbol, measure_group, decimal_places, is_active)
SELECT 
    gen_random_uuid(), t.id, u.code, u.name, u.symbol, u.measure_group, u.decimal_places, true
FROM public.tenants t
CROSS JOIN (
    VALUES
    ('kg', 'Kilôgam', 'kg', 'Mass', 3::smallint),
    ('g', 'Gam', 'g', 'Mass', 3::smallint),
    ('tan', 'Tấn', 'tấn', 'Mass', 3::smallint),
    ('lit', 'Lít', 'l', 'Volume', 3::smallint),
    ('ml', 'Mililít', 'ml', 'Volume', 3::smallint),
    ('m', 'Mét', 'm', 'Length', 3::smallint),
    ('piece', 'Cái', 'cái', 'Count', 3::smallint),
    ('bao', 'Bao', 'bao', 'Packaging', 0::smallint),
    ('thung', 'Thùng', 'thùng', 'Packaging', 0::smallint),
    ('hop', 'Hộp', 'hộp', 'Packaging', 0::smallint),
    ('chai', 'Chai', 'chai', 'Packaging', 0::smallint)
) AS u(code, name, symbol, measure_group, decimal_places)
ON CONFLICT (tenant_id, code) DO NOTHING;

INSERT INTO public.units (id, tenant_id, code, name, symbol, measure_group, decimal_places, is_active)
SELECT 
    gen_random_uuid(), s.tenant_id, lower(btrim(s.unit)), lower(btrim(s.unit)), lower(btrim(s.unit)), 'Count', 3::smallint, true
FROM public.skus s
WHERE s.unit IS NOT NULL AND btrim(s.unit) <> ''
GROUP BY s.tenant_id, lower(btrim(s.unit))
ON CONFLICT (tenant_id, code) DO NOTHING;

-- 3. Gán skus.base_unit_id và sku_units gốc
UPDATE public.skus s
SET base_unit_id = u.id
FROM public.units u
WHERE s.tenant_id = u.tenant_id
  AND lower(btrim(s.unit)) = u.code
  AND s.base_unit_id IS NULL;

INSERT INTO public.sku_units (
    id, tenant_id, sku_id, unit_id, factor_to_base,
    can_purchase, can_sell, is_default_purchase, is_default_sale,
    barcode, quantity_decimal_places, sale_price, version, is_active
)
SELECT 
    gen_random_uuid(), s.tenant_id, s.id, s.base_unit_id, 1,
    true, true, true, true,
    NULL, 3::smallint, NULL, 1, true
FROM public.skus s
WHERE s.base_unit_id IS NOT NULL
ON CONFLICT (tenant_id, sku_id, unit_id) DO NOTHING;

-- 4. Backfill unit_id, unit_name_snapshot (không UPDATE inventory_transactions)
UPDATE public.order_items oi
SET unit_id = s.base_unit_id,
    unit_name_snapshot = COALESCE(u.name, s.unit, 'piece'),
    factor_snapshot = 1
FROM public.skus s
LEFT JOIN public.units u ON u.tenant_id = s.tenant_id AND u.id = s.base_unit_id
WHERE oi.tenant_id = s.tenant_id
  AND oi.sku_id = s.id
  AND oi.unit_id IS NULL;

UPDATE public.goods_receipt_items gri
SET unit_id = s.base_unit_id,
    unit_name_snapshot = COALESCE(u.name, s.unit, 'piece'),
    factor_snapshot = 1
FROM public.skus s
LEFT JOIN public.units u ON u.tenant_id = s.tenant_id AND u.id = s.base_unit_id
WHERE gri.tenant_id = s.tenant_id
  AND gri.sku_id = s.id
  AND gri.unit_id IS NULL;

-- 5. SET CONSTRAINTS ALL IMMEDIATE
SET CONSTRAINTS ALL IMMEDIATE;

-- 6. Thêm FK và đổi uq_order_items_2
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_skus_base_unit_id') THEN
        ALTER TABLE public.skus ADD CONSTRAINT fk_skus_base_unit_id
        FOREIGN KEY (tenant_id, base_unit_id) REFERENCES public.units (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_order_items_unit_id') THEN
        ALTER TABLE public.order_items ADD CONSTRAINT fk_order_items_unit_id
        FOREIGN KEY (tenant_id, unit_id) REFERENCES public.units (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_goods_receipt_items_unit_id') THEN
        ALTER TABLE public.goods_receipt_items ADD CONSTRAINT fk_goods_receipt_items_unit_id
        FOREIGN KEY (tenant_id, unit_id) REFERENCES public.units (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.table_constraints tc
        JOIN information_schema.key_column_usage kcu
          ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema
        WHERE tc.table_name = 'order_items' 
          AND tc.constraint_name = 'uq_order_items_2'
          AND tc.constraint_type = 'UNIQUE'
        GROUP BY tc.constraint_name
        HAVING count(*) = 3
    ) THEN
        ALTER TABLE public.order_items DROP CONSTRAINT uq_order_items_2;
        ALTER TABLE public.order_items ADD CONSTRAINT uq_order_items_2 UNIQUE NULLS NOT DISTINCT (tenant_id, order_id, sku_id, unit_id);
    ELSIF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'uq_order_items_2'
    ) THEN
        ALTER TABLE public.order_items ADD CONSTRAINT uq_order_items_2 UNIQUE NULLS NOT DISTINCT (tenant_id, order_id, sku_id, unit_id);
    END IF;
END $$;

-- 7. Functions, triggers, permissions
CREATE OR REPLACE FUNCTION public.oisms_check_stock(p_tenant uuid,p_branch uuid,p_sku uuid) RETURNS void
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
DECLARE b public.inventory_balances%ROWTYPE; physical numeric; held numeric; seq bigint;
BEGIN
  SELECT * INTO b FROM public.inventory_balances
    WHERE tenant_id=p_tenant AND branch_id=p_branch AND sku_id=p_sku;
  SELECT coalesce(sum(on_hand),0),coalesce(sum(reserved),0) INTO physical,held
    FROM public.warehouse_inventory_balances WHERE tenant_id=p_tenant AND branch_id=p_branch AND sku_id=p_sku;
  SELECT coalesce(max(sequence_no),0) INTO seq FROM public.inventory_transactions
    WHERE tenant_id=p_tenant AND branch_id=p_branch AND sku_id=p_sku;
  IF (coalesce(b.on_hand,0),coalesce(b.reserved,0),coalesce(b.last_sequence,0)) IS DISTINCT FROM (physical,held,seq) THEN
    RAISE EXCEPTION 'Branch balance differs from warehouse totals or ledger sequence' USING ERRCODE='23514';
  END IF;
  IF EXISTS (
    WITH w AS (
      SELECT warehouse_id,on_hand,reserved,last_sequence FROM public.warehouse_inventory_balances
      WHERE tenant_id=p_tenant AND branch_id=p_branch AND sku_id=p_sku
    ), l AS (
      SELECT warehouse_id,sum(CASE WHEN type='IN' THEN quantity ELSE -quantity END) qty,max(sequence_no) seq
      FROM public.inventory_transactions WHERE tenant_id=p_tenant AND branch_id=p_branch AND sku_id=p_sku
      GROUP BY warehouse_id
    ), r AS (
      SELECT warehouse_id,sum(quantity) qty FROM public.reservations
      WHERE tenant_id=p_tenant AND branch_id=p_branch AND sku_id=p_sku AND status='Active'
      GROUP BY warehouse_id
    ) SELECT 1 FROM w FULL JOIN l USING(warehouse_id) FULL JOIN r USING(warehouse_id)
      WHERE coalesce(w.on_hand,0)<>coalesce(l.qty,0) OR coalesce(w.reserved,0)<>coalesce(r.qty,0)
         OR coalesce(w.last_sequence,0)<>coalesce(l.seq,0)
  ) THEN
    RAISE EXCEPTION 'Warehouse balance differs from ledger or active reservations' USING ERRCODE='23514';
  END IF;
  IF EXISTS (
    SELECT 1 FROM (
      SELECT sequence_no,balance_after,
        row_number() OVER(ORDER BY sequence_no) expected_seq,
        sum(CASE WHEN type='IN' THEN quantity ELSE -quantity END) OVER(ORDER BY sequence_no) expected_balance
      FROM public.inventory_transactions WHERE tenant_id=p_tenant AND branch_id=p_branch AND sku_id=p_sku
    ) h WHERE sequence_no<>expected_seq OR balance_after<>expected_balance
  ) THEN
    RAISE EXCEPTION 'Invalid ledger sequence or BalanceAfter' USING ERRCODE='23514';
  END IF;
  IF EXISTS (
    SELECT 1 FROM public.reservations r JOIN public.order_items i ON i.id=r.order_item_id AND i.tenant_id=r.tenant_id
    WHERE r.tenant_id=p_tenant AND r.branch_id=p_branch AND r.sku_id=p_sku AND r.status IN ('Active','Consumed')
    GROUP BY i.id,i.base_quantity HAVING sum(r.quantity)>i.base_quantity
  ) THEN
    RAISE EXCEPTION 'Reservation allocation exceeds order quantity' USING ERRCODE='23514';
  END IF;
END $$;

CREATE OR REPLACE FUNCTION public.oisms_protect_order_item() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
DECLARE confirmed timestamptz;
BEGIN
  SELECT confirmed_at INTO confirmed FROM public.orders WHERE id=OLD.order_id AND tenant_id=OLD.tenant_id FOR UPDATE;
  IF TG_OP='DELETE' AND confirmed IS NOT NULL THEN
    RAISE EXCEPTION 'Confirmed order items cannot be deleted' USING ERRCODE='23514';
  END IF;
  IF TG_OP='UPDATE' THEN
    IF (NEW.tenant_id,NEW.order_id,NEW.sku_id) IS DISTINCT FROM (OLD.tenant_id,OLD.order_id,OLD.sku_id)
      OR (confirmed IS NOT NULL AND OLD.cost_price IS NOT NULL AND
          (
            (NEW.cost_price,NEW.quantity,NEW.unit_price,NEW.factor_snapshot) IS DISTINCT FROM (OLD.cost_price,OLD.quantity,OLD.unit_price,OLD.factor_snapshot)
            OR (OLD.unit_id IS NOT NULL AND (NEW.unit_id,NEW.unit_name_snapshot) IS DISTINCT FROM (OLD.unit_id,OLD.unit_name_snapshot))
          )) THEN
      RAISE EXCEPTION 'Order item identity or confirmed financial snapshot is immutable' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
  END IF;
  RETURN OLD;
END $$;

CREATE OR REPLACE FUNCTION public.oisms_protect_receipt_item() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
DECLARE confirmed timestamptz;
BEGIN
  SELECT confirmed_at INTO confirmed FROM public.goods_receipts WHERE id=OLD.receipt_id AND tenant_id=OLD.tenant_id FOR UPDATE;
  IF TG_OP='DELETE' AND confirmed IS NOT NULL THEN
    RAISE EXCEPTION 'Confirmed goods receipt items cannot be deleted' USING ERRCODE='23514';
  END IF;
  IF TG_OP='UPDATE' AND confirmed IS NOT NULL AND
     ((NEW.tenant_id,NEW.receipt_id,NEW.sku_id,NEW.quantity,NEW.unit_price,NEW.factor_snapshot) IS DISTINCT FROM
      (OLD.tenant_id,OLD.receipt_id,OLD.sku_id,OLD.quantity,OLD.unit_price,OLD.factor_snapshot)
      OR (OLD.unit_id IS NOT NULL AND (NEW.unit_id,NEW.unit_name_snapshot) IS DISTINCT FROM (OLD.unit_id,OLD.unit_name_snapshot))) THEN
    RAISE EXCEPTION 'Confirmed goods receipt item snapshot is immutable' USING ERRCODE='23514';
  END IF;
  IF TG_OP='UPDATE' THEN RETURN NEW; END IF;
  RETURN OLD;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger tg
        JOIN pg_class c ON tg.tgrelid = c.oid
        WHERE c.relname = 'goods_receipt_items' AND tg.tgname = 'protect_receipt_item'
    ) THEN
        CREATE TRIGGER protect_receipt_item BEFORE UPDATE OR DELETE ON public.goods_receipt_items
        FOR EACH ROW EXECUTE FUNCTION public.oisms_protect_receipt_item();
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger tg
        JOIN pg_class c ON tg.tgrelid = c.oid
        WHERE c.relname = 'sku_unit_factor_history' AND tg.tgname = 'factor_history_append_only'
    ) THEN
        CREATE TRIGGER factor_history_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON public.sku_unit_factor_history
        FOR EACH STATEMENT EXECUTE FUNCTION public.oisms_deny_mutation();
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oisms_runtime') THEN
        REVOKE ALL ON TABLE public.units FROM oisms_runtime;
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.units TO oisms_runtime;

        REVOKE ALL ON TABLE public.sku_units FROM oisms_runtime;
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.sku_units TO oisms_runtime;

        REVOKE ALL ON TABLE public.sku_unit_factor_history FROM oisms_runtime;
        GRANT SELECT, INSERT ON TABLE public.sku_unit_factor_history TO oisms_runtime;
        REVOKE UPDATE, DELETE, TRUNCATE ON public.sku_unit_factor_history FROM oisms_runtime;
    END IF;
END $$;
";
            await using (var multiUnitCmd = new NpgsqlCommand(multiUnitUpgradeSql, connection, tx))
            {
                await multiUnitCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            const string priceListUpgradeSql = @"
SELECT pg_advisory_xact_lock(hashtext('oisms_price_list_schema_upgrade'));

-- 1. Kiểm tra trước: có SKU base_unit_id IS NULL, hoặc thiếu dòng sku_units của đơn vị gốc, thì RAISE EXCEPTION kèm danh sách.
DO $$
DECLARE
    v_missing_skus text;
BEGIN
    SELECT string_agg(s.id::text || ' (' || s.code || ')', ', ')
    INTO v_missing_skus
    FROM public.skus s
    LEFT JOIN public.sku_units su ON su.tenant_id = s.tenant_id AND su.sku_id = s.id AND su.unit_id = s.base_unit_id
    WHERE s.base_unit_id IS NULL OR su.id IS NULL;

    IF v_missing_skus IS NOT NULL THEN
        RAISE EXCEPTION 'SKU thiếu đơn vị gốc hoặc thiếu dòng sku_units của đơn vị gốc: %', v_missing_skus;
    END IF;
END $$;

-- 2. Ghi nhớ to_regclass('public.price_change_requests') IS NULL vào cờ trước khi tạo bảng
DO $$
BEGIN
    IF to_regclass('public.price_change_requests') IS NULL THEN
        PERFORM set_config('oisms.run_price_backfill', 'true', true);
    ELSE
        PERFORM set_config('oisms.run_price_backfill', 'false', true);
    END IF;
END $$;

-- 3. Tạo 4 bảng mới, thêm cột bảng cũ, constraint, index, function, trigger
CREATE TABLE IF NOT EXISTS public.price_lists (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    code varchar(30) NOT NULL,
    name varchar(100) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    updated_at timestamptz,
    CONSTRAINT uq_price_lists_1 UNIQUE (tenant_id, id),
    CONSTRAINT uq_price_lists_2 UNIQUE (tenant_id, code),
    CONSTRAINT ck_price_lists_code CHECK (code IN ('retail', 'wholesale'))
);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_price_lists_tenant_id') THEN
        ALTER TABLE public.price_lists ADD CONSTRAINT fk_price_lists_tenant_id
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS public.price_change_requests (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    request_key uuid NOT NULL,
    kind varchar(20) NOT NULL,
    status varchar(20) NOT NULL,
    reason varchar(500) NOT NULL,
    requested_valid_from timestamptz,
    created_by uuid,
    created_at timestamptz DEFAULT now() NOT NULL,
    reviewed_by uuid,
    reviewed_at timestamptz,
    review_note varchar(500),
    CONSTRAINT uq_price_change_requests_1 UNIQUE (tenant_id, id),
    CONSTRAINT uq_price_change_requests_2 UNIQUE (tenant_id, request_key),
    CONSTRAINT ck_price_change_requests_kind CHECK (kind IN ('Opening', 'Initial', 'Change')),
    CONSTRAINT ck_price_change_requests_status CHECK (status IN ('Pending', 'Approved', 'Rejected')),
    CONSTRAINT ck_price_change_requests_created_by CHECK (kind = 'Opening' OR created_by IS NOT NULL),
    CONSTRAINT ck_price_change_requests_pending_reviewed CHECK ((status = 'Pending') = (reviewed_at IS NULL)),
    CONSTRAINT ck_price_change_requests_reason CHECK (btrim(reason) <> '')
);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_price_change_requests_tenant_id') THEN
        ALTER TABLE public.price_change_requests ADD CONSTRAINT fk_price_change_requests_tenant_id
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_price_change_requests_created_by') THEN
        ALTER TABLE public.price_change_requests ADD CONSTRAINT fk_price_change_requests_created_by
        FOREIGN KEY (tenant_id, created_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_price_change_requests_reviewed_by') THEN
        ALTER TABLE public.price_change_requests ADD CONSTRAINT fk_price_change_requests_reviewed_by
        FOREIGN KEY (tenant_id, reviewed_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS public.price_change_request_items (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    request_id uuid NOT NULL,
    price_list_id uuid NOT NULL,
    sku_id uuid NOT NULL,
    unit_id uuid NOT NULL,
    new_price numeric(20,2) NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT uq_price_change_request_items_1 UNIQUE (tenant_id, id),
    CONSTRAINT uq_price_change_request_items_2 UNIQUE (tenant_id, request_id, price_list_id, sku_id, unit_id),
    CONSTRAINT ck_price_change_request_items_new_price CHECK (new_price >= 0)
);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_price_change_request_items_tenant_id') THEN
        ALTER TABLE public.price_change_request_items ADD CONSTRAINT fk_price_change_request_items_tenant_id
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_price_change_request_items_request_id') THEN
        ALTER TABLE public.price_change_request_items ADD CONSTRAINT fk_price_change_request_items_request_id
        FOREIGN KEY (tenant_id, request_id) REFERENCES public.price_change_requests (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_price_change_request_items_price_list_id') THEN
        ALTER TABLE public.price_change_request_items ADD CONSTRAINT fk_price_change_request_items_price_list_id
        FOREIGN KEY (tenant_id, price_list_id) REFERENCES public.price_lists (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_price_change_request_items_sku_unit') THEN
        ALTER TABLE public.price_change_request_items ADD CONSTRAINT fk_price_change_request_items_sku_unit
        FOREIGN KEY (tenant_id, sku_id, unit_id) REFERENCES public.sku_units (tenant_id, sku_id, unit_id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS public.sku_unit_prices (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    price_list_id uuid NOT NULL,
    sku_id uuid NOT NULL,
    unit_id uuid NOT NULL,
    price numeric(20,2) NOT NULL,
    valid_from timestamptz NOT NULL,
    source_item_id uuid NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    cancelled_at timestamptz,
    cancelled_by uuid,
    cancel_reason varchar(500),
    CONSTRAINT uq_sku_unit_prices_1 UNIQUE (tenant_id, id),
    CONSTRAINT uq_sku_unit_prices_2 UNIQUE (tenant_id, source_item_id),
    CONSTRAINT ck_sku_unit_prices_price CHECK (price >= 0),
    CONSTRAINT ck_sku_unit_prices_cancel_state CHECK (
        (cancelled_at IS NULL AND cancelled_by IS NULL AND cancel_reason IS NULL)
        OR (cancelled_at IS NOT NULL AND cancelled_by IS NOT NULL AND cancel_reason IS NOT NULL AND btrim(cancel_reason) <> '')
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_sku_unit_prices_active_key
ON public.sku_unit_prices (tenant_id, price_list_id, sku_id, unit_id, valid_from)
WHERE cancelled_at IS NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sku_unit_prices_tenant_id') THEN
        ALTER TABLE public.sku_unit_prices ADD CONSTRAINT fk_sku_unit_prices_tenant_id
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sku_unit_prices_price_list_id') THEN
        ALTER TABLE public.sku_unit_prices ADD CONSTRAINT fk_sku_unit_prices_price_list_id
        FOREIGN KEY (tenant_id, price_list_id) REFERENCES public.price_lists (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sku_unit_prices_source_item_id') THEN
        ALTER TABLE public.sku_unit_prices ADD CONSTRAINT fk_sku_unit_prices_source_item_id
        FOREIGN KEY (tenant_id, source_item_id) REFERENCES public.price_change_request_items (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sku_unit_prices_sku_unit') THEN
        ALTER TABLE public.sku_unit_prices ADD CONSTRAINT fk_sku_unit_prices_sku_unit
        FOREIGN KEY (tenant_id, sku_id, unit_id) REFERENCES public.sku_units (tenant_id, sku_id, unit_id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sku_unit_prices_cancelled_by') THEN
        ALTER TABLE public.sku_unit_prices ADD CONSTRAINT fk_sku_unit_prices_cancelled_by
        FOREIGN KEY (tenant_id, cancelled_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
END $$;

ALTER TABLE public.order_items ADD COLUMN IF NOT EXISTS sku_unit_price_id uuid;
ALTER TABLE public.order_items ADD COLUMN IF NOT EXISTS price_source varchar(20) NOT NULL DEFAULT 'Legacy';

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_order_items_price_source') THEN
        ALTER TABLE public.order_items ADD CONSTRAINT ck_order_items_price_source
        CHECK (price_source IN ('PriceList', 'Marketplace', 'Legacy'));
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_order_items_price_source_ref') THEN
        ALTER TABLE public.order_items ADD CONSTRAINT ck_order_items_price_source_ref
        CHECK ((price_source = 'PriceList') = (sku_unit_price_id IS NOT NULL));
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_order_items_sku_unit_price_id') THEN
        ALTER TABLE public.order_items ADD CONSTRAINT fk_order_items_sku_unit_price_id
        FOREIGN KEY (tenant_id, sku_unit_price_id) REFERENCES public.sku_unit_prices (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
END $$;

CREATE OR REPLACE FUNCTION public.oisms_price_change_request_guard() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
BEGIN
  IF OLD.status <> 'Pending' OR NEW.status NOT IN ('Approved', 'Rejected') THEN
    RAISE EXCEPTION 'Price change request can only transition from Pending to Approved or Rejected' USING ERRCODE = '23514';
  END IF;

  IF (NEW.tenant_id, NEW.id, NEW.request_key, NEW.kind, NEW.reason, NEW.requested_valid_from, NEW.created_by, NEW.created_at)
     IS DISTINCT FROM
     (OLD.tenant_id, OLD.id, OLD.request_key, OLD.kind, OLD.reason, OLD.requested_valid_from, OLD.created_by, OLD.created_at) THEN
    RAISE EXCEPTION 'Price change request attributes other than review fields are immutable' USING ERRCODE = '23514';
  END IF;

  RETURN NEW;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger tg
        JOIN pg_class c ON tg.tgrelid = c.oid
        WHERE c.relname = 'price_change_requests' AND tg.tgname = 'price_change_request_guard'
    ) THEN
        CREATE TRIGGER price_change_request_guard BEFORE UPDATE ON public.price_change_requests
        FOR EACH ROW EXECUTE FUNCTION public.oisms_price_change_request_guard();
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger tg
        JOIN pg_class c ON tg.tgrelid = c.oid
        WHERE c.relname = 'price_change_requests' AND tg.tgname = 'price_change_request_no_delete'
    ) THEN
        CREATE TRIGGER price_change_request_no_delete BEFORE DELETE OR TRUNCATE ON public.price_change_requests
        FOR EACH STATEMENT EXECUTE FUNCTION public.oisms_deny_mutation();
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger tg
        JOIN pg_class c ON tg.tgrelid = c.oid
        WHERE c.relname = 'price_change_request_items' AND tg.tgname = 'price_change_item_append_only'
    ) THEN
        CREATE TRIGGER price_change_item_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON public.price_change_request_items
        FOR EACH STATEMENT EXECUTE FUNCTION public.oisms_deny_mutation();
    END IF;
END $$;

CREATE OR REPLACE FUNCTION public.oisms_sku_unit_price_guard() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
BEGIN
  IF OLD.cancelled_at IS NOT NULL OR NEW.cancelled_at IS NULL THEN
    RAISE EXCEPTION 'Sku unit price can only transition from active to cancelled' USING ERRCODE = '23514';
  END IF;

  IF (NEW.tenant_id, NEW.id, NEW.price_list_id, NEW.sku_id, NEW.unit_id, NEW.price, NEW.valid_from, NEW.source_item_id, NEW.created_at)
     IS DISTINCT FROM
     (OLD.tenant_id, OLD.id, OLD.price_list_id, OLD.sku_id, OLD.unit_id, OLD.price, OLD.valid_from, OLD.source_item_id, OLD.created_at) THEN
    RAISE EXCEPTION 'Sku unit price attributes other than cancellation fields are immutable' USING ERRCODE = '23514';
  END IF;

  RETURN NEW;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger tg
        JOIN pg_class c ON tg.tgrelid = c.oid
        WHERE c.relname = 'sku_unit_prices' AND tg.tgname = 'sku_unit_price_guard'
    ) THEN
        CREATE TRIGGER sku_unit_price_guard BEFORE UPDATE ON public.sku_unit_prices
        FOR EACH ROW EXECUTE FUNCTION public.oisms_sku_unit_price_guard();
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger tg
        JOIN pg_class c ON tg.tgrelid = c.oid
        WHERE c.relname = 'sku_unit_prices' AND tg.tgname = 'sku_unit_price_no_delete'
    ) THEN
        CREATE TRIGGER sku_unit_price_no_delete BEFORE DELETE OR TRUNCATE ON public.sku_unit_prices
        FOR EACH STATEMENT EXECUTE FUNCTION public.oisms_deny_mutation();
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oisms_runtime') THEN
        REVOKE ALL ON TABLE public.price_lists FROM oisms_runtime;
        GRANT SELECT, INSERT, UPDATE ON TABLE public.price_lists TO oisms_runtime;
        REVOKE DELETE, TRUNCATE ON TABLE public.price_lists FROM oisms_runtime;

        REVOKE ALL ON TABLE public.price_change_requests FROM oisms_runtime;
        GRANT SELECT, INSERT, UPDATE ON TABLE public.price_change_requests TO oisms_runtime;
        REVOKE DELETE, TRUNCATE ON TABLE public.price_change_requests FROM oisms_runtime;

        REVOKE ALL ON TABLE public.price_change_request_items FROM oisms_runtime;
        GRANT SELECT, INSERT ON TABLE public.price_change_request_items TO oisms_runtime;
        REVOKE UPDATE, DELETE, TRUNCATE ON public.price_change_request_items FROM oisms_runtime;

        REVOKE ALL ON TABLE public.sku_unit_prices FROM oisms_runtime;
        GRANT SELECT, INSERT, UPDATE ON TABLE public.sku_unit_prices TO oisms_runtime;
        REVOKE DELETE, TRUNCATE ON TABLE public.sku_unit_prices FROM oisms_runtime;
    END IF;
END $$;

-- 4. Backfill (chỉ chạy khi cờ oisms.run_price_backfill = 'true')
DO $$
DECLARE
    v_run_backfill boolean := COALESCE(current_setting('oisms.run_price_backfill', true) = 'true', false);
    v_tenant record;
    v_retail_id uuid;
    v_wholesale_id uuid;
    v_req_id uuid;
    v_now timestamptz := now();
    v_zero_sku record;
BEGIN
    IF NOT v_run_backfill THEN
        RETURN;
    END IF;

    FOR v_tenant IN SELECT id FROM public.tenants LOOP
        v_retail_id := gen_random_uuid();
        v_wholesale_id := gen_random_uuid();

        INSERT INTO public.price_lists (id, tenant_id, code, name, is_active, created_at)
        VALUES
            (v_retail_id, v_tenant.id, 'retail', 'Giá lẻ', true, v_now),
            (v_wholesale_id, v_tenant.id, 'wholesale', 'Giá sỉ', true, v_now)
        ON CONFLICT (tenant_id, code) DO NOTHING;

        SELECT id INTO v_retail_id FROM public.price_lists WHERE tenant_id = v_tenant.id AND code = 'retail';
        SELECT id INTO v_wholesale_id FROM public.price_lists WHERE tenant_id = v_tenant.id AND code = 'wholesale';

        v_req_id := gen_random_uuid();
        INSERT INTO public.price_change_requests (
            id, tenant_id, request_key, kind, status, reason,
            requested_valid_from, created_by, created_at, reviewed_by, reviewed_at, review_note
        )
        VALUES (
            v_req_id, v_tenant.id, md5(v_tenant.id::text || '/opening')::uuid,
            'Opening', 'Approved', 'Giá đầu kỳ',
            NULL, NULL, v_now, NULL, v_now, NULL
        )
        ON CONFLICT (tenant_id, request_key) DO NOTHING;

        SELECT id INTO v_req_id FROM public.price_change_requests
        WHERE tenant_id = v_tenant.id AND request_key = md5(v_tenant.id::text || '/opening')::uuid;

        FOR v_zero_sku IN
            SELECT id, code FROM public.skus WHERE tenant_id = v_tenant.id AND retail_price = 0
        LOOP
            RAISE NOTICE 'SKU % (%) có giá lẻ cũ = 0', v_zero_sku.id, v_zero_sku.code;
        END LOOP;

        WITH ins_retail AS (
            INSERT INTO public.price_change_request_items (
                id, tenant_id, request_id, price_list_id, sku_id, unit_id, new_price, created_at
            )
            SELECT
                gen_random_uuid(), s.tenant_id, v_req_id, v_retail_id, s.id, s.base_unit_id, s.retail_price, v_now
            FROM public.skus s
            WHERE s.tenant_id = v_tenant.id
              AND s.base_unit_id IS NOT NULL
            ON CONFLICT (tenant_id, request_id, price_list_id, sku_id, unit_id) DO NOTHING
            RETURNING id, tenant_id, price_list_id, sku_id, unit_id, new_price, created_at
        )
        INSERT INTO public.sku_unit_prices (
            id, tenant_id, price_list_id, sku_id, unit_id, price, valid_from, source_item_id, created_at
        )
        SELECT
            gen_random_uuid(), tenant_id, price_list_id, sku_id, unit_id, new_price, v_now, id, created_at
        FROM ins_retail
        ON CONFLICT (tenant_id, source_item_id) DO NOTHING;

        WITH ins_wholesale AS (
            INSERT INTO public.price_change_request_items (
                id, tenant_id, request_id, price_list_id, sku_id, unit_id, new_price, created_at
            )
            SELECT
                gen_random_uuid(), s.tenant_id, v_req_id, v_wholesale_id, s.id, s.base_unit_id, s.wholesale_price, v_now
            FROM public.skus s
            WHERE s.tenant_id = v_tenant.id
              AND s.base_unit_id IS NOT NULL
              AND s.wholesale_price > 0
            ON CONFLICT (tenant_id, request_id, price_list_id, sku_id, unit_id) DO NOTHING
            RETURNING id, tenant_id, price_list_id, sku_id, unit_id, new_price, created_at
        )
        INSERT INTO public.sku_unit_prices (
            id, tenant_id, price_list_id, sku_id, unit_id, price, valid_from, source_item_id, created_at
        )
        SELECT
            gen_random_uuid(), tenant_id, price_list_id, sku_id, unit_id, new_price, v_now, id, created_at
        FROM ins_wholesale
        ON CONFLICT (tenant_id, source_item_id) DO NOTHING;

        WITH ins_unit_retail AS (
            INSERT INTO public.price_change_request_items (
                id, tenant_id, request_id, price_list_id, sku_id, unit_id, new_price, created_at
            )
            SELECT
                gen_random_uuid(), su.tenant_id, v_req_id, v_retail_id, su.sku_id, su.unit_id, su.sale_price, v_now
            FROM public.sku_units su
            JOIN public.skus s ON s.tenant_id = su.tenant_id AND s.id = su.sku_id
            WHERE su.tenant_id = v_tenant.id
              AND su.unit_id <> s.base_unit_id
              AND su.sale_price IS NOT NULL
            ON CONFLICT (tenant_id, request_id, price_list_id, sku_id, unit_id) DO NOTHING
            RETURNING id, tenant_id, price_list_id, sku_id, unit_id, new_price, created_at
        )
        INSERT INTO public.sku_unit_prices (
            id, tenant_id, price_list_id, sku_id, unit_id, price, valid_from, source_item_id, created_at
        )
        SELECT
            gen_random_uuid(), tenant_id, price_list_id, sku_id, unit_id, new_price, v_now, id, created_at
        FROM ins_unit_retail
        ON CONFLICT (tenant_id, source_item_id) DO NOTHING;

    END LOOP;
END $$;
";
            await using (var priceListCmd = new NpgsqlCommand(priceListUpgradeSql, connection, tx))
            {
                await priceListCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            return new MigrationExecutionResult(
                Success: true,
                IsFreshInstall: false,
                IsBaselineAdopted: false,
                TableCount: RequiredTables.Length,
                Message: "Nâng cấp schema v0.5 stock_transfers, shop_integrations, multi_unit và price_lists thành công.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return new MigrationExecutionResult(
                Success: false,
                IsFreshInstall: false,
                IsBaselineAdopted: false,
                TableCount: 0,
                Message: $"Lỗi khi nâng cấp schema: {ex.Message}",
                Errors: new[] { ex.ToString() });
        }
    }

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
                    Message: $"Cài mới thành công {RequiredTables.Length} bảng schema v0.5 và ghi nhận baseline '{InitialBaselineMigrationId}'.");
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
                    Message: $"Xác minh thành công {RequiredTables.Length} bảng, trigger, function và quyền runtime schema v0.5. Đã tiếp nhận baseline '{InitialBaselineMigrationId}'.");
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

        string[] requiredFunctions = [
            "oisms_deny_mutation", "oisms_lock_stock", "oisms_stock_constraint", "oisms_check_stock",
            "oisms_price_change_request_guard", "oisms_sku_unit_price_guard"
        ];
        foreach (var fn in requiredFunctions)
        {
            if (!existingProcs.Contains(fn))
            {
                errors.Add($"Thiếu hàm bảo vệ schema v0.5 '{fn}'.");
            }
        }

        // 6. Kiểm tra các cột trọng yếu của schema v0.5
        const string checkColumnsSql = @"
SELECT table_name, column_name, data_type, COALESCE(character_maximum_length, 0), is_nullable
FROM information_schema.columns 
WHERE table_schema = 'public';";

        var columnMap = new Dictionary<string, (string DataType, int MaxLength, bool IsNullable)>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new NpgsqlCommand(checkColumnsSql, connection, tx))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var key = $"{reader.GetString(0)}.{reader.GetString(1)}";
                var isNullable = string.Equals(reader.GetString(4), "YES", StringComparison.OrdinalIgnoreCase);
                columnMap[key] = (reader.GetString(2), reader.GetInt32(3), isNullable);
            }
        }

        string[] crucialColumns = [
            "users.role", "users.tenant_id", "users.display_name", "users.phone",
            "skus.code", "skus.attributes", "skus.retail_price", "skus.wholesale_price",
            "inventory_transactions.sequence_no", "inventory_transactions.balance_after", "inventory_transactions.type",
            "inventory_balances.on_hand", "inventory_balances.reserved", "inventory_balances.average_cost", "inventory_balances.last_sequence",
            "stock_transfers.request_key", "stock_transfers.payload_signature",
            "stock_counts.request_key", "stock_counts.payload_signature", "stock_counts.generated_adjustment_id",
            "orders.webhook_payload_signature",
            "shop_integrations.channel", "shop_integrations.external_shop_id", "shop_integrations.branch_id",
            "shop_integrations.is_active", "shop_integrations.secret_hash",
            "skus.base_unit_id",
            "order_items.unit_id", "order_items.unit_name_snapshot", "order_items.factor_snapshot", "order_items.base_quantity",
            "order_items.sku_unit_price_id", "order_items.price_source",
            "goods_receipt_items.unit_id", "goods_receipt_items.unit_name_snapshot", "goods_receipt_items.factor_snapshot", "goods_receipt_items.base_quantity",
            "units.code", "units.name", "units.measure_group", "units.decimal_places",
            "sku_units.factor_to_base", "sku_units.quantity_decimal_places",
            "sku_unit_factor_history.new_factor", "sku_unit_factor_history.new_version",
            "price_lists.code", "price_change_requests.status", "price_change_requests.request_key",
            "price_change_request_items.new_price",
            "sku_unit_prices.price", "sku_unit_prices.valid_from"
        ];
        foreach (var col in crucialColumns)
        {
            if (!columnMap.ContainsKey(col))
            {
                errors.Add($"Thiếu cột trọng yếu '{col}' của schema.");
            }
        }

        if (columnMap.TryGetValue("stock_transfers.request_key", out var reqKeyCol))
        {
            if (!string.Equals(reqKeyCol.DataType, "uuid", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Cột 'stock_transfers.request_key' sai kiểu dữ liệu (thực tế='{reqKeyCol.DataType}', kỳ vọng='uuid').");
            }
        }

        if (columnMap.TryGetValue("stock_transfers.payload_signature", out var sigCol))
        {
            if (!string.Equals(sigCol.DataType, "character varying", StringComparison.OrdinalIgnoreCase) || sigCol.MaxLength < 64)
            {
                errors.Add($"Cột 'stock_transfers.payload_signature' sai kiểu dữ liệu hoặc độ dài (thực tế='{sigCol.DataType}({sigCol.MaxLength})', kỳ vọng='character varying(128)').");
            }
        }

        if (columnMap.TryGetValue("stock_counts.request_key", out var scReqKeyCol))
        {
            if (!string.Equals(scReqKeyCol.DataType, "uuid", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Cột 'stock_counts.request_key' sai kiểu dữ liệu (thực tế='{scReqKeyCol.DataType}', kỳ vọng='uuid').");
            }
        }

        if (columnMap.TryGetValue("stock_counts.payload_signature", out var scSigCol))
        {
            if (!string.Equals(scSigCol.DataType, "character varying", StringComparison.OrdinalIgnoreCase) || scSigCol.MaxLength < 64)
            {
                errors.Add($"Cột 'stock_counts.payload_signature' sai kiểu dữ liệu hoặc độ dài (thực tế='{scSigCol.DataType}({scSigCol.MaxLength})', kỳ vọng='character varying(128)').");
            }
        }

        if (columnMap.TryGetValue("stock_counts.generated_adjustment_id", out var scAdjCol))
        {
            if (!string.Equals(scAdjCol.DataType, "uuid", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Cột 'stock_counts.generated_adjustment_id' sai kiểu dữ liệu (thực tế='{scAdjCol.DataType}', kỳ vọng='uuid').");
            }
        }

        if (columnMap.TryGetValue("orders.webhook_payload_signature", out var whSigCol))
        {
            if (!string.Equals(whSigCol.DataType, "character varying", StringComparison.OrdinalIgnoreCase) || whSigCol.MaxLength < 64)
            {
                errors.Add($"Cột 'orders.webhook_payload_signature' sai kiểu dữ liệu hoặc độ dài (thực tế='{whSigCol.DataType}({whSigCol.MaxLength})', kỳ vọng='character varying(64)').");
            }
            if (!whSigCol.IsNullable)
            {
                errors.Add($"Cột 'orders.webhook_payload_signature' sai nullability (thực tế='NOT NULL', kỳ vọng='NULL / nullable cho legacy/POS/Admin').");
            }
        }

        if (columnMap.TryGetValue("shop_integrations.secret_hash", out var siSecretCol))
        {
            if (!string.Equals(siSecretCol.DataType, "character varying", StringComparison.OrdinalIgnoreCase) || siSecretCol.MaxLength < 255)
            {
                errors.Add($"Cột 'shop_integrations.secret_hash' sai kiểu dữ liệu hoặc độ dài (thực tế='{siSecretCol.DataType}({siSecretCol.MaxLength})', kỳ vọng='character varying(255)').");
            }
            if (siSecretCol.IsNullable)
            {
                errors.Add($"Cột 'shop_integrations.secret_hash' sai nullability (thực tế='NULL', kỳ vọng='NOT NULL').");
            }
        }

        if (columnMap.TryGetValue("shop_integrations.channel", out var siChannelCol))
        {
            if (!string.Equals(siChannelCol.DataType, "character varying", StringComparison.OrdinalIgnoreCase) || siChannelCol.MaxLength < 20)
            {
                errors.Add($"Cột 'shop_integrations.channel' sai kiểu dữ liệu hoặc độ dài (thực tế='{siChannelCol.DataType}({siChannelCol.MaxLength})', kỳ vọng='character varying(20)').");
            }
            if (siChannelCol.IsNullable)
            {
                errors.Add($"Cột 'shop_integrations.channel' sai nullability (thực tế='NULL', kỳ vọng='NOT NULL').");
            }
        }

        if (columnMap.TryGetValue("shop_integrations.external_shop_id", out var siShopCol))
        {
            if (!string.Equals(siShopCol.DataType, "character varying", StringComparison.OrdinalIgnoreCase) || siShopCol.MaxLength < 100)
            {
                errors.Add($"Cột 'shop_integrations.external_shop_id' sai kiểu dữ liệu hoặc độ dài (thực tế='{siShopCol.DataType}({siShopCol.MaxLength})', kỳ vọng='character varying(100)').");
            }
            if (siShopCol.IsNullable)
            {
                errors.Add($"Cột 'shop_integrations.external_shop_id' sai nullability (thực tế='NULL', kỳ vọng='NOT NULL').");
            }
        }

        if (columnMap.TryGetValue("shop_integrations.branch_id", out var siBranchCol))
        {
            if (!string.Equals(siBranchCol.DataType, "uuid", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Cột 'shop_integrations.branch_id' sai kiểu dữ liệu (thực tế='{siBranchCol.DataType}', kỳ vọng='uuid').");
            }
            if (siBranchCol.IsNullable)
            {
                errors.Add($"Cột 'shop_integrations.branch_id' sai nullability (thực tế='NULL', kỳ vọng='NOT NULL').");
            }
        }

        if (columnMap.TryGetValue("shop_integrations.is_active", out var siActiveCol))
        {
            if (!string.Equals(siActiveCol.DataType, "boolean", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Cột 'shop_integrations.is_active' sai kiểu dữ liệu (thực tế='{siActiveCol.DataType}', kỳ vọng='boolean').");
            }
            if (siActiveCol.IsNullable)
            {
                errors.Add($"Cột 'shop_integrations.is_active' sai nullability (thực tế='NULL', kỳ vọng='NOT NULL').");
            }
        }

        if (columnMap.TryGetValue("shop_integrations.tenant_id", out var siTenantCol))
        {
            if (!string.Equals(siTenantCol.DataType, "uuid", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Cột 'shop_integrations.tenant_id' sai kiểu dữ liệu (thực tế='{siTenantCol.DataType}', kỳ vọng='uuid').");
            }
            if (siTenantCol.IsNullable)
            {
                errors.Add($"Cột 'shop_integrations.tenant_id' sai nullability (thực tế='NULL', kỳ vọng='NOT NULL').");
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

        // Kiểm tra oisms_runtime KHÔNG có quyền UPDATE hoặc DELETE hoặc TRUNCATE trên sku_unit_factor_history
        const string checkHistoryPrivilegesSql = @"
SELECT privilege_type 
FROM information_schema.role_table_grants 
WHERE grantee = 'oisms_runtime' 
  AND table_schema = 'public' 
  AND table_name = 'sku_unit_factor_history' 
  AND privilege_type IN ('UPDATE', 'DELETE', 'TRUNCATE');";

        await using (var cmdHist = new NpgsqlCommand(checkHistoryPrivilegesSql, connection, tx))
        await using (var reader = await cmdHist.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                errors.Add($"Vi phạm an ninh quyền runtime: 'oisms_runtime' bị cấp quyền '{reader.GetString(0)}' trên bảng append-only 'sku_unit_factor_history'.");
            }
        }

        // Kiểm tra oisms_runtime KHÔNG có quyền UPDATE hoặc DELETE hoặc TRUNCATE trên price_change_request_items
        const string checkPriceItemsPrivilegesSql = @"
SELECT privilege_type 
FROM information_schema.role_table_grants 
WHERE grantee = 'oisms_runtime' 
  AND table_schema = 'public' 
  AND table_name = 'price_change_request_items' 
  AND privilege_type IN ('UPDATE', 'DELETE', 'TRUNCATE');";

        await using (var cmdPriceItems = new NpgsqlCommand(checkPriceItemsPrivilegesSql, connection, tx))
        await using (var reader = await cmdPriceItems.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                errors.Add($"Vi phạm an ninh quyền runtime: 'oisms_runtime' bị cấp quyền '{reader.GetString(0)}' trên bảng append-only 'price_change_request_items'.");
            }
        }

        // Kiểm tra oisms_runtime KHÔNG có quyền DELETE trên price_lists, price_change_requests, sku_unit_prices
        const string checkPriceNoDeletePrivilegesSql = @"
SELECT table_name, privilege_type 
FROM information_schema.role_table_grants 
WHERE grantee = 'oisms_runtime' 
  AND table_schema = 'public' 
  AND table_name IN ('price_lists', 'price_change_requests', 'sku_unit_prices') 
  AND privilege_type IN ('DELETE', 'TRUNCATE');";

        await using (var cmdPriceNoDel = new NpgsqlCommand(checkPriceNoDeletePrivilegesSql, connection, tx))
        await using (var reader = await cmdPriceNoDel.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                errors.Add($"Vi phạm an ninh quyền runtime: 'oisms_runtime' bị cấp quyền '{reader.GetString(1)}' trên bảng '{reader.GetString(0)}'.");
            }
        }

        // Kiểm tra oisms_runtime CÓ quyền SELECT trên shop_integrations
        const string checkShopIntegrationsGrantSql = @"
SELECT 1 
FROM information_schema.role_table_grants 
WHERE grantee = 'oisms_runtime' 
  AND table_schema = 'public' 
  AND table_name = 'shop_integrations' 
  AND privilege_type = 'SELECT';";

        await using (var cmdGrant = new NpgsqlCommand(checkShopIntegrationsGrantSql, connection, tx))
        {
            var hasSelect = await cmdGrant.ExecuteScalarAsync(ct);
            if (hasSelect == null)
            {
                errors.Add("Role 'oisms_runtime' thiếu quyền SELECT trên bảng 'shop_integrations'.");
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

