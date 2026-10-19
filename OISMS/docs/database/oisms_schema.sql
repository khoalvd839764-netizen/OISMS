-- OISMS v0.5: ONE-FILE NEW DATABASE INSTALLATION; run with psql, connected to postgres.
-- Requires CREATEDB and CREATEROLE (or DBA). Never run against an existing deployment.
-- No DROP. If oisms already exists, ON_ERROR_STOP aborts before any schema changes.
-- CREATE DATABASE is outside the schema transaction: a later failure leaves an empty database.
\set ON_ERROR_STOP on
CREATE DATABASE oisms WITH ENCODING 'UTF8' TEMPLATE template0;
\connect oisms

-- OISMS_SCHEMA_BEGIN: embedded SQL tests execute from the following BEGIN onward.
-- OISMS: PostgreSQL 16+ schema v0.5, branch WAC, warehouse stock and flexible SKU attributes agreed with the user.
-- Run once in an EMPTY database/schema. No DROP statements; existing tables cause rollback.
-- Includes append-only and deferred stock consistency guards. Runtime group permissions are included in this transaction.
-- No RLS, API workflows or automated WAC posting routines.
-- WAC scope is branch/SKU by user decision. Cancellation transitions and QR confirmation remain open.
-- Do not connect the application with owner privileges or expose CRUD on balances/ledger.
BEGIN;

-- Doanh nghiệp sử dụng hệ thống.
CREATE TABLE "tenants" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "name" varchar(200) NOT NULL,
  "is_active" boolean DEFAULT true NOT NULL,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz
);

-- Email hoặc SĐT định danh duy nhất toàn hệ thống; mỗi user thuộc một tenant.
CREATE TABLE "users" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "email" varchar(254),
  "phone" varchar(16),
  "password_hash" varchar(255) NOT NULL,
  "display_name" varchar(150) NOT NULL,
  "role" varchar(20) NOT NULL,
  "is_active" boolean DEFAULT true NOT NULL,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_users_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_users_2" UNIQUE ("email"),
  CONSTRAINT "uq_users_3" UNIQUE ("phone"),
  CONSTRAINT "ck_users_role_values" CHECK (role IN ('Owner', 'Staff', 'Cashier')),
  CONSTRAINT "ck_users_login_identity" CHECK (email IS NOT NULL OR phone IS NOT NULL),
  CONSTRAINT "ck_users_email_normalized" CHECK (email IS NULL OR (email = lower(btrim(email)) AND length(email) > 0)),
  CONSTRAINT "ck_users_phone_normalized" CHECK (phone IS NULL OR phone ~ '^\+[1-9][0-9]{7,14}$')
);

-- Lưu hash; logout thu hồi, refresh xoay token trong transaction.
CREATE TABLE "refresh_tokens" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "user_id" uuid NOT NULL,
  "token_hash" varchar(128) NOT NULL,
  "expires_at" timestamptz NOT NULL,
  "revoked_at" timestamptz,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  CONSTRAINT "uq_refresh_tokens_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_refresh_tokens_2" UNIQUE ("token_hash"),
  CONSTRAINT "ck_refresh_tokens_expiry" CHECK (expires_at > created_at)
);

-- Chi nhánh của tenant.
CREATE TABLE "branches" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "name" varchar(200) NOT NULL,
  "address" text,
  "is_active" boolean DEFAULT true NOT NULL,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_branches_1" UNIQUE ("tenant_id", "id")
);

-- Kho thuộc chi nhánh theo FR-AUTH-04; không dùng kho làm giới hạn tồn khả dụng của đơn.
CREATE TABLE "warehouses" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "branch_id" uuid NOT NULL,
  "name" varchar(200) NOT NULL,
  "is_active" boolean DEFAULT true NOT NULL,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_warehouses_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_warehouses_2" UNIQUE ("tenant_id", "id", "branch_id")
);

-- Danh mục cha–con; service kiểm tra chu trình nhiều cấp.
CREATE TABLE "categories" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "parent_id" uuid,
  "name" varchar(200) NOT NULL,
  "is_active" boolean DEFAULT true NOT NULL,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_categories_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "ck_categories_not_self_parent" CHECK (parent_id IS NULL OR parent_id <> id)
);

-- Thương hiệu sản phẩm.
CREATE TABLE "brands" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "name" varchar(200) NOT NULL,
  "is_active" boolean DEFAULT true NOT NULL,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_brands_1" UNIQUE ("tenant_id", "id")
);

-- Thông tin chung; sản phẩm đơn giản vẫn có một SKU.
CREATE TABLE "products" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "category_id" uuid,
  "brand_id" uuid,
  "name" varchar(250) NOT NULL,
  "description" text,
  "is_active" boolean DEFAULT true NOT NULL,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_products_1" UNIQUE ("tenant_id", "id")
);

-- SKU chứa trực tiếp một barcode và giá sỉ/lẻ. Attributes là object JSON cho thuộc tính biến thể tùy ý; service chuẩn hóa tên/giá trị và kiểm tra tổ hợp trùng trong product.
CREATE TABLE "skus" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "product_id" uuid NOT NULL,
  "code" varchar(80) NOT NULL,
  "attributes" jsonb DEFAULT '{}'::jsonb NOT NULL,
  "unit" varchar(30) DEFAULT 'piece' NOT NULL,
  "barcode_type" varchar(20),
  "barcode" varchar(128),
  "retail_price" numeric(20,2) NOT NULL,
  "wholesale_price" numeric(20,2) NOT NULL,
  "is_active" boolean DEFAULT true NOT NULL,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_skus_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_skus_2" UNIQUE ("tenant_id", "code"),
  CONSTRAINT "uq_skus_3" UNIQUE ("tenant_id", "barcode"),
  CONSTRAINT "ck_skus_attributes_object" CHECK (jsonb_typeof(attributes) = 'object'),
  CONSTRAINT "ck_skus_code_normalized" CHECK (code = upper(btrim(code)) AND length(code) > 0),
  CONSTRAINT "ck_skus_barcode_pair" CHECK ((barcode_type IS NULL) = (barcode IS NULL)),
  CONSTRAINT "ck_skus_barcode_type_values" CHECK (barcode_type IN ('EAN13', 'Code128')),
  CONSTRAINT "ck_skus_ean_shape" CHECK (barcode_type <> 'EAN13' OR barcode ~ '^[0-9]{13}$'),
  CONSTRAINT "ck_skus_retail_price_nonnegative" CHECK (retail_price >= 0),
  CONSTRAINT "ck_skus_wholesale_price_nonnegative" CHECK (wholesale_price >= 0)
);

-- Nhà cung cấp của phiếu nhập.
CREATE TABLE "suppliers" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "name" varchar(200) NOT NULL,
  "phone" varchar(32),
  "email" varchar(254),
  "address" text,
  "is_active" boolean DEFAULT true NOT NULL,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_suppliers_1" UNIQUE ("tenant_id", "id")
);

-- Nhập vào kho cụ thể thuộc chi nhánh; cập nhật đồng thời tồn kho, tổng tồn chi nhánh và WAC chi nhánh.
CREATE TABLE "goods_receipts" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "number" varchar(50) NOT NULL,
  "branch_id" uuid NOT NULL,
  "warehouse_id" uuid NOT NULL,
  "supplier_id" uuid NOT NULL,
  "status" varchar(20) DEFAULT 'Draft' NOT NULL,
  "note" text,
  "created_by" uuid NOT NULL,
  "confirmed_at" timestamptz,
  "confirmed_by" uuid,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_goods_receipts_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_goods_receipts_2" UNIQUE ("tenant_id", "number"),
  CONSTRAINT "ck_goods_receipts_status_values" CHECK (status IN ('Draft', 'Confirmed', 'Cancelled')),
  CONSTRAINT "ck_goods_receipts_confirmed" CHECK ((status = 'Confirmed') = (confirmed_at IS NOT NULL) AND (confirmed_at IS NULL) = (confirmed_by IS NULL))
);

-- Mỗi SKU một dòng/phiếu; không phân bổ phí nhập/thuế mở rộng.
CREATE TABLE "goods_receipt_items" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "receipt_id" uuid NOT NULL,
  "sku_id" uuid NOT NULL,
  "quantity" numeric(18,3) NOT NULL,
  "unit_price" numeric(20,6) NOT NULL,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_goods_receipt_items_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_goods_receipt_items_2" UNIQUE ("tenant_id", "receipt_id", "sku_id"),
  CONSTRAINT "ck_goods_receipt_items_quantity_positive" CHECK (quantity > 0),
  CONSTRAINT "ck_goods_receipt_items_unit_price_nonnegative" CHECK (unit_price >= 0)
);

-- Tổng tồn dẫn xuất từ các kho thuộc chi nhánh; WAC chốt theo chi nhánh/SKU. Cập nhật cùng transaction với số dư kho và ledger.
CREATE TABLE "inventory_balances" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "branch_id" uuid NOT NULL,
  "sku_id" uuid NOT NULL,
  "on_hand" numeric(18,3) DEFAULT 0 NOT NULL,
  "reserved" numeric(18,3) DEFAULT 0 NOT NULL,
  "average_cost" numeric(20,6) DEFAULT 0 NOT NULL,
  "reorder_point" numeric(18,3) DEFAULT 0 NOT NULL,
  "last_sequence" bigint DEFAULT 0 NOT NULL,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_inventory_balances_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_inventory_balances_2" UNIQUE ("tenant_id", "branch_id", "sku_id"),
  CONSTRAINT "ck_inventory_balances_on_hand_nonnegative" CHECK (on_hand >= 0),
  CONSTRAINT "ck_inventory_balances_reserved_nonnegative" CHECK (reserved >= 0),
  CONSTRAINT "ck_inventory_balances_average_cost_nonnegative" CHECK (average_cost >= 0),
  CONSTRAINT "ck_inventory_balances_reorder_point_nonnegative" CHECK (reorder_point >= 0),
  CONSTRAINT "ck_inventory_balances_last_sequence_nonnegative" CHECK (last_sequence >= 0),
  CONSTRAINT "ck_inventory_balances_reservation_limit" CHECK (reserved <= on_hand)
);

-- Tồn vật lý và giữ chỗ riêng mỗi kho/SKU; không lưu WAC riêng kho. LastSequence ghi sequence chi nhánh của lần cập nhật gần nhất.
CREATE TABLE "warehouse_inventory_balances" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "branch_id" uuid NOT NULL,
  "warehouse_id" uuid NOT NULL,
  "sku_id" uuid NOT NULL,
  "on_hand" numeric(18,3) DEFAULT 0 NOT NULL,
  "reserved" numeric(18,3) DEFAULT 0 NOT NULL,
  "last_sequence" bigint DEFAULT 0 NOT NULL,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_warehouse_inventory_balances_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_warehouse_inventory_balances_2" UNIQUE ("tenant_id", "warehouse_id", "sku_id"),
  CONSTRAINT "ck_warehouse_inventory_balances_on_hand_nonnegative" CHECK (on_hand >= 0),
  CONSTRAINT "ck_warehouse_inventory_balances_reserved_nonnegative" CHECK (reserved >= 0),
  CONSTRAINT "ck_warehouse_inventory_balances_last_sequence_nonnegative" CHECK (last_sequence >= 0),
  CONSTRAINT "ck_warehouse_inventory_balances_reservation_limit" CHECK (reserved <= on_hand)
);

-- Chuyển giữa hai kho qua xuất/nhận; có thể khác chi nhánh theo đề hoặc cùng chi nhánh để quản lý vị trí. Cùng chi nhánh không đổi WAC; hàng vận chuyển theo dõi riêng.
CREATE TABLE "stock_transfers" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "number" varchar(50) NOT NULL,
  "from_branch_id" uuid NOT NULL,
  "to_branch_id" uuid NOT NULL,
  "from_warehouse_id" uuid NOT NULL,
  "to_warehouse_id" uuid NOT NULL,
  "status" varchar(20) DEFAULT 'Draft' NOT NULL,
  "created_by" uuid NOT NULL,
  "dispatched_at" timestamptz,
  "received_at" timestamptz,
  "received_by" uuid,
  "note" text,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_stock_transfers_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_stock_transfers_2" UNIQUE ("tenant_id", "number"),
  CONSTRAINT "ck_stock_transfers_status_values" CHECK (status IN ('Draft', 'InTransit', 'Received', 'Cancelled')),
  CONSTRAINT "ck_stock_transfers_different_warehouses" CHECK (from_warehouse_id <> to_warehouse_id),
  CONSTRAINT "ck_stock_transfers_dispatch_time" CHECK ((status IN ('InTransit','Received')) = (dispatched_at IS NOT NULL)),
  CONSTRAINT "ck_stock_transfers_receive_time" CHECK ((status = 'Received') = (received_at IS NOT NULL) AND (received_at IS NULL) = (received_by IS NULL))
);

-- Chụp unit_cost khi xuất; nhận ở đích dùng đúng chi phí này.
CREATE TABLE "stock_transfer_items" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "transfer_id" uuid NOT NULL,
  "sku_id" uuid NOT NULL,
  "quantity" numeric(18,3) NOT NULL,
  "unit_cost" numeric(20,6),
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_stock_transfer_items_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_stock_transfer_items_2" UNIQUE ("tenant_id", "transfer_id", "sku_id"),
  CONSTRAINT "ck_stock_transfer_items_quantity_positive" CHECK (quantity > 0),
  CONSTRAINT "ck_stock_transfer_items_unit_cost_nonnegative" CHECK (unit_cost >= 0)
);

-- Kiểm kê SKU tại kho; đối chiếu số dư và sequence của kho, cập nhật tổng chi nhánh cùng transaction.
CREATE TABLE "stock_counts" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "number" varchar(50) NOT NULL,
  "branch_id" uuid NOT NULL,
  "warehouse_id" uuid NOT NULL,
  "status" varchar(20) DEFAULT 'Draft' NOT NULL,
  "created_by" uuid NOT NULL,
  "confirmed_by" uuid,
  "confirmed_at" timestamptz,
  "note" text,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_stock_counts_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_stock_counts_2" UNIQUE ("tenant_id", "number"),
  CONSTRAINT "ck_stock_counts_status_values" CHECK (status IN ('Draft', 'Counting', 'Confirmed', 'Cancelled')),
  CONSTRAINT "ck_stock_counts_confirmed" CHECK ((status = 'Confirmed') = (confirmed_at IS NOT NULL) AND (confirmed_at IS NULL) = (confirmed_by IS NULL))
);

-- Phát hiện giao dịch xen giữa bằng sequence; giá bổ sung cần cho hàng thừa chưa có WAC.
CREATE TABLE "stock_count_items" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "stock_count_id" uuid NOT NULL,
  "sku_id" uuid NOT NULL,
  "expected_quantity" numeric(18,3) NOT NULL,
  "counted_quantity" numeric(18,3),
  "balance_sequence_at_snapshot" bigint NOT NULL,
  "adjustment_unit_cost" numeric(20,6),
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_stock_count_items_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_stock_count_items_2" UNIQUE ("tenant_id", "stock_count_id", "sku_id"),
  CONSTRAINT "ck_stock_count_items_expected_quantity_nonnegative" CHECK (expected_quantity >= 0),
  CONSTRAINT "ck_stock_count_items_counted_quantity_nonnegative" CHECK (counted_quantity >= 0),
  CONSTRAINT "ck_stock_count_items_balance_sequence_at_snapshot_nonnegative" CHECK (balance_sequence_at_snapshot >= 0),
  CONSTRAINT "ck_stock_count_items_adjustment_unit_cost_nonnegative" CHECK (adjustment_unit_cost >= 0)
);

-- Chứng từ tối thiểu cho bút toán bù/số dư đầu kỳ chi nhánh theo NFR-SEC-03.
CREATE TABLE "inventory_adjustments" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "number" varchar(50) NOT NULL,
  "branch_id" uuid NOT NULL,
  "warehouse_id" uuid NOT NULL,
  "reason" text NOT NULL,
  "status" varchar(20) DEFAULT 'Draft' NOT NULL,
  "created_by" uuid NOT NULL,
  "confirmed_by" uuid,
  "confirmed_at" timestamptz,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_inventory_adjustments_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_inventory_adjustments_2" UNIQUE ("tenant_id", "number"),
  CONSTRAINT "ck_inventory_adjustments_status_values" CHECK (status IN ('Draft', 'Confirmed', 'Cancelled')),
  CONSTRAINT "ck_inventory_adjustments_reason_required" CHECK (length(btrim(reason)) > 0),
  CONSTRAINT "ck_inventory_adjustments_confirmed" CHECK ((status = 'Confirmed') = (confirmed_at IS NOT NULL) AND (confirmed_at IS NULL) = (confirmed_by IS NULL))
);

-- Tăng/giảm có lý do, có thể liên kết giao dịch cần bù; không sửa ledger cũ.
CREATE TABLE "inventory_adjustment_items" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "adjustment_id" uuid NOT NULL,
  "sku_id" uuid NOT NULL,
  "quantity_delta" numeric(18,3) NOT NULL,
  "unit_cost" numeric(20,6),
  "original_transaction_id" uuid,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_inventory_adjustment_items_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_inventory_adjustment_items_2" UNIQUE ("tenant_id", "adjustment_id", "sku_id"),
  CONSTRAINT "ck_inventory_adjustment_items_nonzero_delta" CHECK (quantity_delta <> 0),
  CONSTRAINT "ck_inventory_adjustment_items_unit_cost_nonnegative" CHECK (unit_cost >= 0),
  CONSTRAINT "ck_inventory_adjustment_items_incoming_cost" CHECK (quantity_delta < 0 OR unit_cost IS NOT NULL)
);

-- Đơn POS/Admin/sàn dùng tồn chi nhánh; không ràng buộc một kho cấp hàng. RequestKey giữ ổn định khi retry.
CREATE TABLE "orders" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "number" varchar(50) NOT NULL,
  "branch_id" uuid NOT NULL,
  "channel" varchar(20) NOT NULL,
  "external_shop_id" varchar(100),
  "external_order_id" varchar(100),
  "status" varchar(20) DEFAULT 'Draft' NOT NULL,
  "customer_name" varchar(200),
  "customer_phone" varchar(32),
  "shipping_address" text,
  "total_amount" numeric(20,2) DEFAULT 0 NOT NULL,
  "request_key" uuid NOT NULL,
  "created_by" uuid,
  "confirmed_at" timestamptz,
  "completed_at" timestamptz,
  "cancelled_at" timestamptz,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_orders_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_orders_2" UNIQUE ("tenant_id", "number"),
  CONSTRAINT "uq_orders_3" UNIQUE ("tenant_id", "request_key"),
  CONSTRAINT "uq_orders_4" UNIQUE ("tenant_id", "channel", "external_shop_id", "external_order_id"),
  CONSTRAINT "uq_orders_5" UNIQUE ("tenant_id", "id", "branch_id"),
  CONSTRAINT "ck_orders_channel_values" CHECK (channel IN ('POS', 'Admin', 'Shopee', 'TikTok', 'Lazada')),
  CONSTRAINT "ck_orders_status_values" CHECK (status IN ('Draft', 'Reserved', 'Confirmed', 'Completed', 'Cancelled')),
  CONSTRAINT "ck_orders_total_amount_nonnegative" CHECK (total_amount >= 0),
  CONSTRAINT "ck_orders_external_identity" CHECK ((channel IN ('Shopee','TikTok','Lazada') AND external_shop_id IS NOT NULL AND external_order_id IS NOT NULL) OR (channel IN ('POS','Admin') AND external_shop_id IS NULL AND external_order_id IS NULL)),
  CONSTRAINT "ck_orders_confirmation_time" CHECK (status NOT IN ('Confirmed','Completed') OR confirmed_at IS NOT NULL),
  CONSTRAINT "ck_orders_completion_time" CHECK ((status <> 'Completed' OR completed_at IS NOT NULL) AND (completed_at IS NULL OR confirmed_at IS NOT NULL)),
  CONSTRAINT "ck_orders_cancellation_time" CHECK ((status = 'Cancelled') = (cancelled_at IS NOT NULL))
);

-- Giữ tên/mã/giá bán lịch sử để in hóa đơn; CostPrice bắt buộc chụp khi xác nhận.
CREATE TABLE "order_items" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "order_id" uuid NOT NULL,
  "sku_id" uuid NOT NULL,
  "sku_code_snapshot" varchar(80) NOT NULL,
  "sku_name_snapshot" varchar(250) NOT NULL,
  "quantity" numeric(18,3) NOT NULL,
  "unit_price" numeric(20,2) NOT NULL,
  "cost_price" numeric(20,6),
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_order_items_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_order_items_2" UNIQUE ("tenant_id", "order_id", "sku_id"),
  CONSTRAINT "uq_order_items_3" UNIQUE ("tenant_id", "id", "order_id", "sku_id"),
  CONSTRAINT "ck_order_items_quantity_positive" CHECK (quantity > 0),
  CONSTRAINT "ck_order_items_unit_price_nonnegative" CHECK (unit_price >= 0),
  CONSTRAINT "ck_order_items_cost_price_nonnegative" CHECK (cost_price >= 0)
);

-- Phân bổ giữ chỗ xuống kho thuộc chi nhánh đơn; một dòng đơn có thể phân bổ nhiều kho. Tổng lượng phân bổ phải khớp dòng đơn dưới khóa.
CREATE TABLE "reservations" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "order_id" uuid NOT NULL,
  "order_item_id" uuid NOT NULL,
  "branch_id" uuid NOT NULL,
  "warehouse_id" uuid NOT NULL,
  "sku_id" uuid NOT NULL,
  "quantity" numeric(18,3) NOT NULL,
  "status" varchar(20) DEFAULT 'Active' NOT NULL,
  "expires_at" timestamptz NOT NULL,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_reservations_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_reservations_2" UNIQUE ("tenant_id", "order_item_id", "warehouse_id"),
  CONSTRAINT "ck_reservations_quantity_positive" CHECK (quantity > 0),
  CONSTRAINT "ck_reservations_status_values" CHECK (status IN ('Active', 'Consumed', 'Released', 'Expired')),
  CONSTRAINT "ck_reservations_expiry" CHECK (expires_at > created_at)
);

-- Một khoản thanh toán/đơn; retry cập nhật cùng bản ghi, không tạo lần thu mới. Tiền thừa tính từ cash_received - amount.
CREATE TABLE "payments" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "order_id" uuid NOT NULL,
  "method" varchar(20) NOT NULL,
  "status" varchar(20) DEFAULT 'Pending' NOT NULL,
  "amount" numeric(20,2) NOT NULL,
  "cash_received" numeric(20,2),
  "paid_at" timestamptz,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_payments_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_payments_2" UNIQUE ("tenant_id", "order_id"),
  CONSTRAINT "ck_payments_method_values" CHECK (method IN ('Cash', 'BankTransfer')),
  CONSTRAINT "ck_payments_status_values" CHECK (status IN ('Pending', 'Succeeded', 'Failed')),
  CONSTRAINT "ck_payments_amount_nonnegative" CHECK (amount >= 0),
  CONSTRAINT "ck_payments_cash_received_nonnegative" CHECK (cash_received >= 0),
  CONSTRAINT "ck_payments_paid_time" CHECK ((status = 'Succeeded') = (paid_at IS NOT NULL)),
  CONSTRAINT "ck_payments_cash_received" CHECK ((method = 'Cash' AND (status <> 'Succeeded' OR cash_received IS NOT NULL) AND (cash_received IS NULL OR cash_received >= amount)) OR (method = 'BankTransfer' AND cash_received IS NULL))
);

-- Nguồn giao dịch trả hàng cho ledger theo FR-INV-01; chi tiết quy trình hoàn tiền là thiết kế hỗ trợ, không phải yêu cầu nguyên văn.
CREATE TABLE "sales_returns" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "number" varchar(50) NOT NULL,
  "order_id" uuid NOT NULL,
  "branch_id" uuid NOT NULL,
  "warehouse_id" uuid NOT NULL,
  "status" varchar(20) DEFAULT 'Draft' NOT NULL,
  "reason" text NOT NULL,
  "created_by" uuid NOT NULL,
  "confirmed_by" uuid,
  "confirmed_at" timestamptz,
  "refund_method" varchar(20),
  "refunded_at" timestamptz,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_sales_returns_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_sales_returns_2" UNIQUE ("tenant_id", "number"),
  CONSTRAINT "uq_sales_returns_3" UNIQUE ("tenant_id", "id", "order_id"),
  CONSTRAINT "ck_sales_returns_status_values" CHECK (status IN ('Draft', 'Confirmed', 'Cancelled')),
  CONSTRAINT "ck_sales_returns_refund_method_values" CHECK (refund_method IN ('Cash', 'BankTransfer')),
  CONSTRAINT "ck_sales_returns_confirmed" CHECK ((status = 'Confirmed') = (confirmed_at IS NOT NULL) AND (confirmed_at IS NULL) = (confirmed_by IS NULL)),
  CONSTRAINT "ck_sales_returns_refund" CHECK ((refund_method IS NULL) = (refunded_at IS NULL) AND (refunded_at IS NULL OR status = 'Confirmed'))
);

-- Dòng trả phải thuộc đúng đơn/SKU. Giữ giá vốn gốc và số tiền hoàn để tính doanh thu thuần.
CREATE TABLE "sales_return_items" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "sales_return_id" uuid NOT NULL,
  "order_id" uuid NOT NULL,
  "order_item_id" uuid NOT NULL,
  "sku_id" uuid NOT NULL,
  "quantity" numeric(18,3) NOT NULL,
  "refund_amount" numeric(20,2) NOT NULL,
  "original_unit_cost" numeric(20,6) NOT NULL,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_sales_return_items_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_sales_return_items_2" UNIQUE ("tenant_id", "sales_return_id", "order_item_id"),
  CONSTRAINT "ck_sales_return_items_quantity_positive" CHECK (quantity > 0),
  CONSTRAINT "ck_sales_return_items_refund_amount_nonnegative" CHECK (refund_amount >= 0),
  CONSTRAINT "ck_sales_return_items_original_unit_cost_nonnegative" CHECK (original_unit_cost >= 0)
);

-- Ledger có trường đề yêu cầu và WarehouseId. BalanceAfter và SequenceNo theo chi nhánh/SKU; số dư kho được đối chiếu bằng tổng IN/OUT tại kho.
CREATE TABLE "inventory_transactions" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "branch_id" uuid NOT NULL,
  "warehouse_id" uuid NOT NULL,
  "sku_id" uuid NOT NULL,
  "type" varchar(3) NOT NULL,
  "quantity" numeric(18,3) NOT NULL,
  "unit_cost" numeric(20,6) NOT NULL,
  "balance_after" numeric(18,3) NOT NULL,
  "sequence_no" bigint NOT NULL,
  "receipt_item_id" uuid,
  "order_item_id" uuid,
  "transfer_item_id" uuid,
  "stock_count_item_id" uuid,
  "sales_return_item_id" uuid,
  "adjustment_item_id" uuid,
  "reference_id" uuid GENERATED ALWAYS AS (coalesce(receipt_item_id, order_item_id, transfer_item_id, stock_count_item_id, sales_return_item_id, adjustment_item_id)) STORED NOT NULL,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  CONSTRAINT "uq_inventory_transactions_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_inventory_transactions_2" UNIQUE ("tenant_id", "branch_id", "sku_id", "sequence_no"),
  CONSTRAINT "uq_inventory_transactions_3" UNIQUE ("tenant_id", "receipt_item_id"),
  CONSTRAINT "uq_inventory_transactions_4" UNIQUE ("tenant_id", "order_item_id", "warehouse_id"),
  CONSTRAINT "uq_inventory_transactions_5" UNIQUE ("tenant_id", "transfer_item_id", "type"),
  CONSTRAINT "uq_inventory_transactions_6" UNIQUE ("tenant_id", "stock_count_item_id"),
  CONSTRAINT "uq_inventory_transactions_7" UNIQUE ("tenant_id", "sales_return_item_id"),
  CONSTRAINT "uq_inventory_transactions_8" UNIQUE ("tenant_id", "adjustment_item_id"),
  CONSTRAINT "ck_inventory_transactions_type_values" CHECK (type IN ('IN', 'OUT')),
  CONSTRAINT "ck_inventory_transactions_quantity_positive" CHECK (quantity > 0),
  CONSTRAINT "ck_inventory_transactions_sequence_no_positive" CHECK (sequence_no > 0),
  CONSTRAINT "ck_inventory_transactions_unit_cost_nonnegative" CHECK (unit_cost >= 0),
  CONSTRAINT "ck_inventory_transactions_balance_after_nonnegative" CHECK (balance_after >= 0),
  CONSTRAINT "ck_inventory_transactions_one_source" CHECK (num_nonnulls(receipt_item_id, order_item_id, transfer_item_id, stock_count_item_id, sales_return_item_id, adjustment_item_id) = 1),
  CONSTRAINT "ck_inventory_transactions_source_direction" CHECK ((receipt_item_id IS NULL OR type = 'IN') AND (order_item_id IS NULL OR type = 'OUT') AND (sales_return_item_id IS NULL OR type = 'IN'))
);

-- FR-REP: tổng hợp theo ngày, chi nhánh, kênh và SKU.
CREATE TABLE "daily_sales_summaries" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "business_date" date NOT NULL,
  "branch_id" uuid NOT NULL,
  "channel" varchar(20) NOT NULL,
  "sku_id" uuid NOT NULL,
  "sold_quantity" numeric(18,3) NOT NULL,
  "returned_quantity" numeric(18,3) NOT NULL,
  "revenue" numeric(20,2) NOT NULL,
  "return_amount" numeric(20,2) NOT NULL,
  "cost_of_goods_sold" numeric(20,2) NOT NULL,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_daily_sales_summaries_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_daily_sales_summaries_2" UNIQUE ("tenant_id", "business_date", "branch_id", "channel", "sku_id"),
  CONSTRAINT "ck_daily_sales_summaries_channel_values" CHECK (channel IN ('POS', 'Admin', 'Shopee', 'TikTok', 'Lazada')),
  CONSTRAINT "ck_daily_sales_summaries_sold_quantity_nonnegative" CHECK (sold_quantity >= 0),
  CONSTRAINT "ck_daily_sales_summaries_returned_quantity_nonnegative" CHECK (returned_quantity >= 0),
  CONSTRAINT "ck_daily_sales_summaries_revenue_nonnegative" CHECK (revenue >= 0),
  CONSTRAINT "ck_daily_sales_summaries_return_amount_nonnegative" CHECK (return_amount >= 0)
);

-- Đề trang 6 yêu cầu AI gợi ý nhập; cấu trúc đầu ra này là phương án lưu tối thiểu, thuật toán chưa được đề chốt.
CREATE TABLE "replenishment_suggestions" (
  "id" uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
  "tenant_id" uuid NOT NULL,
  "branch_id" uuid NOT NULL,
  "sku_id" uuid NOT NULL,
  "forecast_date" date NOT NULL,
  "horizon_days" integer NOT NULL,
  "predicted_quantity" numeric(18,3) NOT NULL,
  "suggested_quantity" numeric(18,3) NOT NULL,
  "created_at" timestamptz DEFAULT now() NOT NULL,
  "updated_at" timestamptz,
  CONSTRAINT "uq_replenishment_suggestions_1" UNIQUE ("tenant_id", "id"),
  CONSTRAINT "uq_replenishment_suggestions_2" UNIQUE ("tenant_id", "branch_id", "sku_id", "forecast_date", "horizon_days"),
  CONSTRAINT "ck_replenishment_suggestions_horizon_days_positive" CHECK (horizon_days > 0),
  CONSTRAINT "ck_replenishment_suggestions_predicted_quantity_nonnegative" CHECK (predicted_quantity >= 0),
  CONSTRAINT "ck_replenishment_suggestions_suggested_quantity_nonnegative" CHECK (suggested_quantity >= 0)
);

-- Add foreign keys after all tables exist (including intentional circular references).
ALTER TABLE "users" ADD CONSTRAINT "fk_users_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "refresh_tokens" ADD CONSTRAINT "fk_refresh_tokens_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "refresh_tokens" ADD CONSTRAINT "fk_refresh_tokens_user_id" FOREIGN KEY ("tenant_id", "user_id") REFERENCES "users" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "branches" ADD CONSTRAINT "fk_branches_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "warehouses" ADD CONSTRAINT "fk_warehouses_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "warehouses" ADD CONSTRAINT "fk_warehouses_branch_id" FOREIGN KEY ("tenant_id", "branch_id") REFERENCES "branches" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "categories" ADD CONSTRAINT "fk_categories_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "categories" ADD CONSTRAINT "fk_categories_parent_id" FOREIGN KEY ("tenant_id", "parent_id") REFERENCES "categories" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "brands" ADD CONSTRAINT "fk_brands_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "products" ADD CONSTRAINT "fk_products_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "products" ADD CONSTRAINT "fk_products_category_id" FOREIGN KEY ("tenant_id", "category_id") REFERENCES "categories" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "products" ADD CONSTRAINT "fk_products_brand_id" FOREIGN KEY ("tenant_id", "brand_id") REFERENCES "brands" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "skus" ADD CONSTRAINT "fk_skus_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "skus" ADD CONSTRAINT "fk_skus_product_id" FOREIGN KEY ("tenant_id", "product_id") REFERENCES "products" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "suppliers" ADD CONSTRAINT "fk_suppliers_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "goods_receipts" ADD CONSTRAINT "fk_goods_receipts_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "goods_receipts" ADD CONSTRAINT "fk_goods_receipts_branch_id" FOREIGN KEY ("tenant_id", "branch_id") REFERENCES "branches" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "goods_receipts" ADD CONSTRAINT "fk_goods_receipts_warehouse_branch" FOREIGN KEY ("tenant_id", "warehouse_id", "branch_id") REFERENCES "warehouses" ("tenant_id", "id", "branch_id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "goods_receipts" ADD CONSTRAINT "fk_goods_receipts_supplier_id" FOREIGN KEY ("tenant_id", "supplier_id") REFERENCES "suppliers" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "goods_receipts" ADD CONSTRAINT "fk_goods_receipts_created_by" FOREIGN KEY ("tenant_id", "created_by") REFERENCES "users" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "goods_receipts" ADD CONSTRAINT "fk_goods_receipts_confirmed_by" FOREIGN KEY ("tenant_id", "confirmed_by") REFERENCES "users" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "goods_receipt_items" ADD CONSTRAINT "fk_goods_receipt_items_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "goods_receipt_items" ADD CONSTRAINT "fk_goods_receipt_items_receipt_id" FOREIGN KEY ("tenant_id", "receipt_id") REFERENCES "goods_receipts" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "goods_receipt_items" ADD CONSTRAINT "fk_goods_receipt_items_sku_id" FOREIGN KEY ("tenant_id", "sku_id") REFERENCES "skus" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_balances" ADD CONSTRAINT "fk_inventory_balances_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_balances" ADD CONSTRAINT "fk_inventory_balances_branch_id" FOREIGN KEY ("tenant_id", "branch_id") REFERENCES "branches" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_balances" ADD CONSTRAINT "fk_inventory_balances_sku_id" FOREIGN KEY ("tenant_id", "sku_id") REFERENCES "skus" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "warehouse_inventory_balances" ADD CONSTRAINT "fk_warehouse_inventory_balances_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "warehouse_inventory_balances" ADD CONSTRAINT "fk_warehouse_inventory_balances_warehouse_branch" FOREIGN KEY ("tenant_id", "warehouse_id", "branch_id") REFERENCES "warehouses" ("tenant_id", "id", "branch_id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "warehouse_inventory_balances" ADD CONSTRAINT "fk_warehouse_inventory_balances_sku_id" FOREIGN KEY ("tenant_id", "sku_id") REFERENCES "skus" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "stock_transfers" ADD CONSTRAINT "fk_stock_transfers_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "stock_transfers" ADD CONSTRAINT "fk_stock_transfers_from_branch_id" FOREIGN KEY ("tenant_id", "from_branch_id") REFERENCES "branches" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "stock_transfers" ADD CONSTRAINT "fk_stock_transfers_to_branch_id" FOREIGN KEY ("tenant_id", "to_branch_id") REFERENCES "branches" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "stock_transfers" ADD CONSTRAINT "fk_stock_transfers_created_by" FOREIGN KEY ("tenant_id", "created_by") REFERENCES "users" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "stock_transfers" ADD CONSTRAINT "fk_stock_transfers_received_by" FOREIGN KEY ("tenant_id", "received_by") REFERENCES "users" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "stock_transfers" ADD CONSTRAINT "fk_stock_transfers_source_warehouse" FOREIGN KEY ("tenant_id", "from_warehouse_id", "from_branch_id") REFERENCES "warehouses" ("tenant_id", "id", "branch_id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "stock_transfers" ADD CONSTRAINT "fk_stock_transfers_destination_warehouse" FOREIGN KEY ("tenant_id", "to_warehouse_id", "to_branch_id") REFERENCES "warehouses" ("tenant_id", "id", "branch_id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "stock_transfer_items" ADD CONSTRAINT "fk_stock_transfer_items_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "stock_transfer_items" ADD CONSTRAINT "fk_stock_transfer_items_transfer_id" FOREIGN KEY ("tenant_id", "transfer_id") REFERENCES "stock_transfers" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "stock_transfer_items" ADD CONSTRAINT "fk_stock_transfer_items_sku_id" FOREIGN KEY ("tenant_id", "sku_id") REFERENCES "skus" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "stock_counts" ADD CONSTRAINT "fk_stock_counts_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "stock_counts" ADD CONSTRAINT "fk_stock_counts_branch_id" FOREIGN KEY ("tenant_id", "branch_id") REFERENCES "branches" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "stock_counts" ADD CONSTRAINT "fk_stock_counts_warehouse_branch" FOREIGN KEY ("tenant_id", "warehouse_id", "branch_id") REFERENCES "warehouses" ("tenant_id", "id", "branch_id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "stock_counts" ADD CONSTRAINT "fk_stock_counts_created_by" FOREIGN KEY ("tenant_id", "created_by") REFERENCES "users" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "stock_counts" ADD CONSTRAINT "fk_stock_counts_confirmed_by" FOREIGN KEY ("tenant_id", "confirmed_by") REFERENCES "users" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "stock_count_items" ADD CONSTRAINT "fk_stock_count_items_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "stock_count_items" ADD CONSTRAINT "fk_stock_count_items_stock_count_id" FOREIGN KEY ("tenant_id", "stock_count_id") REFERENCES "stock_counts" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "stock_count_items" ADD CONSTRAINT "fk_stock_count_items_sku_id" FOREIGN KEY ("tenant_id", "sku_id") REFERENCES "skus" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_adjustments" ADD CONSTRAINT "fk_inventory_adjustments_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_adjustments" ADD CONSTRAINT "fk_inventory_adjustments_branch_id" FOREIGN KEY ("tenant_id", "branch_id") REFERENCES "branches" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_adjustments" ADD CONSTRAINT "fk_inventory_adjustments_warehouse_branch" FOREIGN KEY ("tenant_id", "warehouse_id", "branch_id") REFERENCES "warehouses" ("tenant_id", "id", "branch_id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_adjustments" ADD CONSTRAINT "fk_inventory_adjustments_created_by" FOREIGN KEY ("tenant_id", "created_by") REFERENCES "users" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_adjustments" ADD CONSTRAINT "fk_inventory_adjustments_confirmed_by" FOREIGN KEY ("tenant_id", "confirmed_by") REFERENCES "users" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_adjustment_items" ADD CONSTRAINT "fk_inventory_adjustment_items_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_adjustment_items" ADD CONSTRAINT "fk_inventory_adjustment_items_adjustment_id" FOREIGN KEY ("tenant_id", "adjustment_id") REFERENCES "inventory_adjustments" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_adjustment_items" ADD CONSTRAINT "fk_inventory_adjustment_items_sku_id" FOREIGN KEY ("tenant_id", "sku_id") REFERENCES "skus" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_adjustment_items" ADD CONSTRAINT "fk_inventory_adjustment_items_original_transaction_id" FOREIGN KEY ("tenant_id", "original_transaction_id") REFERENCES "inventory_transactions" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "orders" ADD CONSTRAINT "fk_orders_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "orders" ADD CONSTRAINT "fk_orders_branch_id" FOREIGN KEY ("tenant_id", "branch_id") REFERENCES "branches" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "orders" ADD CONSTRAINT "fk_orders_created_by" FOREIGN KEY ("tenant_id", "created_by") REFERENCES "users" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "order_items" ADD CONSTRAINT "fk_order_items_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "order_items" ADD CONSTRAINT "fk_order_items_order_id" FOREIGN KEY ("tenant_id", "order_id") REFERENCES "orders" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "order_items" ADD CONSTRAINT "fk_order_items_sku_id" FOREIGN KEY ("tenant_id", "sku_id") REFERENCES "skus" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "reservations" ADD CONSTRAINT "fk_reservations_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "reservations" ADD CONSTRAINT "fk_reservations_warehouse_branch" FOREIGN KEY ("tenant_id", "warehouse_id", "branch_id") REFERENCES "warehouses" ("tenant_id", "id", "branch_id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "reservations" ADD CONSTRAINT "fk_reservations_order_item" FOREIGN KEY ("tenant_id", "order_item_id", "order_id", "sku_id") REFERENCES "order_items" ("tenant_id", "id", "order_id", "sku_id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "reservations" ADD CONSTRAINT "fk_reservations_order_branch" FOREIGN KEY ("tenant_id", "order_id", "branch_id") REFERENCES "orders" ("tenant_id", "id", "branch_id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "payments" ADD CONSTRAINT "fk_payments_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "payments" ADD CONSTRAINT "fk_payments_order_id" FOREIGN KEY ("tenant_id", "order_id") REFERENCES "orders" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "sales_returns" ADD CONSTRAINT "fk_sales_returns_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "sales_returns" ADD CONSTRAINT "fk_sales_returns_order_id" FOREIGN KEY ("tenant_id", "order_id") REFERENCES "orders" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "sales_returns" ADD CONSTRAINT "fk_sales_returns_branch_id" FOREIGN KEY ("tenant_id", "branch_id") REFERENCES "branches" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "sales_returns" ADD CONSTRAINT "fk_sales_returns_warehouse_branch" FOREIGN KEY ("tenant_id", "warehouse_id", "branch_id") REFERENCES "warehouses" ("tenant_id", "id", "branch_id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "sales_returns" ADD CONSTRAINT "fk_sales_returns_created_by" FOREIGN KEY ("tenant_id", "created_by") REFERENCES "users" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "sales_returns" ADD CONSTRAINT "fk_sales_returns_confirmed_by" FOREIGN KEY ("tenant_id", "confirmed_by") REFERENCES "users" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "sales_return_items" ADD CONSTRAINT "fk_sales_return_items_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "sales_return_items" ADD CONSTRAINT "fk_sales_return_items_return_order" FOREIGN KEY ("tenant_id", "sales_return_id", "order_id") REFERENCES "sales_returns" ("tenant_id", "id", "order_id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "sales_return_items" ADD CONSTRAINT "fk_sales_return_items_original_item" FOREIGN KEY ("tenant_id", "order_item_id", "order_id", "sku_id") REFERENCES "order_items" ("tenant_id", "id", "order_id", "sku_id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_transactions" ADD CONSTRAINT "fk_inventory_transactions_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_transactions" ADD CONSTRAINT "fk_inventory_transactions_warehouse_branch" FOREIGN KEY ("tenant_id", "warehouse_id", "branch_id") REFERENCES "warehouses" ("tenant_id", "id", "branch_id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_transactions" ADD CONSTRAINT "fk_inventory_transactions_sku_id" FOREIGN KEY ("tenant_id", "sku_id") REFERENCES "skus" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_transactions" ADD CONSTRAINT "fk_inventory_transactions_branch_id" FOREIGN KEY ("tenant_id", "branch_id") REFERENCES "branches" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_transactions" ADD CONSTRAINT "fk_inventory_transactions_receipt_item_id" FOREIGN KEY ("tenant_id", "receipt_item_id") REFERENCES "goods_receipt_items" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_transactions" ADD CONSTRAINT "fk_inventory_transactions_order_item_id" FOREIGN KEY ("tenant_id", "order_item_id") REFERENCES "order_items" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_transactions" ADD CONSTRAINT "fk_inventory_transactions_transfer_item_id" FOREIGN KEY ("tenant_id", "transfer_item_id") REFERENCES "stock_transfer_items" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_transactions" ADD CONSTRAINT "fk_inventory_transactions_stock_count_item_id" FOREIGN KEY ("tenant_id", "stock_count_item_id") REFERENCES "stock_count_items" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_transactions" ADD CONSTRAINT "fk_inventory_transactions_sales_return_item_id" FOREIGN KEY ("tenant_id", "sales_return_item_id") REFERENCES "sales_return_items" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "inventory_transactions" ADD CONSTRAINT "fk_inventory_transactions_adjustment_item_id" FOREIGN KEY ("tenant_id", "adjustment_item_id") REFERENCES "inventory_adjustment_items" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "daily_sales_summaries" ADD CONSTRAINT "fk_daily_sales_summaries_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "daily_sales_summaries" ADD CONSTRAINT "fk_daily_sales_summaries_branch_id" FOREIGN KEY ("tenant_id", "branch_id") REFERENCES "branches" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "daily_sales_summaries" ADD CONSTRAINT "fk_daily_sales_summaries_sku_id" FOREIGN KEY ("tenant_id", "sku_id") REFERENCES "skus" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "replenishment_suggestions" ADD CONSTRAINT "fk_replenishment_suggestions_tenant_id" FOREIGN KEY ("tenant_id") REFERENCES "tenants" ("id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "replenishment_suggestions" ADD CONSTRAINT "fk_replenishment_suggestions_branch_id" FOREIGN KEY ("tenant_id", "branch_id") REFERENCES "branches" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;
ALTER TABLE "replenishment_suggestions" ADD CONSTRAINT "fk_replenishment_suggestions_sku_id" FOREIGN KEY ("tenant_id", "sku_id") REFERENCES "skus" ("tenant_id", "id") ON DELETE RESTRICT ON UPDATE NO ACTION;

-- Query and partial unique indexes.
CREATE INDEX "ix_skus_product" ON "skus" ("tenant_id", "product_id");
CREATE INDEX "ix_inventory_balances_sku_branches" ON "inventory_balances" ("tenant_id", "sku_id", "branch_id");
CREATE INDEX "ix_inventory_transactions_sku_history" ON "inventory_transactions" ("tenant_id", "branch_id", "sku_id", "created_at", "id");
CREATE INDEX "ix_inventory_transactions_tenant_history" ON "inventory_transactions" ("tenant_id", "created_at", "id");
CREATE INDEX "ix_inventory_transactions_sku" ON "inventory_transactions" ("tenant_id", "sku_id");
CREATE INDEX "ix_orders_branch_history" ON "orders" ("tenant_id", "branch_id", "created_at", "id");
CREATE INDEX "ix_orders_status_history" ON "orders" ("tenant_id", "status", "created_at", "id");
CREATE INDEX "ix_order_items_sku_orders" ON "order_items" ("tenant_id", "sku_id", "order_id");
CREATE INDEX "ix_reservations_expiry" ON "reservations" ("status", "expires_at", "tenant_id");
CREATE INDEX "ix_refresh_tokens_user" ON "refresh_tokens" ("tenant_id", "user_id");
CREATE INDEX "ix_goods_receipts_status_time" ON "goods_receipts" ("tenant_id", "status", "created_at", "id");
CREATE INDEX "ix_stock_transfers_status_time" ON "stock_transfers" ("tenant_id", "status", "created_at", "id");
CREATE INDEX "ix_stock_counts_status_time" ON "stock_counts" ("tenant_id", "status", "created_at", "id");
CREATE INDEX "ix_sales_returns_status_time" ON "sales_returns" ("tenant_id", "status", "created_at", "id");
CREATE INDEX "ix_orders_tenant_created_at" ON "orders" ("tenant_id", "created_at");
CREATE INDEX "ix_warehouse_inventory_balances_branch_sku" ON "warehouse_inventory_balances" ("tenant_id", "branch_id", "sku_id");
CREATE INDEX "ix_reservations_branch_sku_status" ON "reservations" ("tenant_id", "branch_id", "sku_id", "status");
CREATE INDEX "ix_inventory_transactions_warehouse_sku" ON "inventory_transactions" ("tenant_id", "warehouse_id", "sku_id", "sequence_no");

-- Database guards v0.5. Applied by the generator inside the installation transaction.
-- Keep schema ownership with a migration account; application logins must not own tables.
CREATE FUNCTION public.oisms_deny_mutation() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
BEGIN
  RAISE EXCEPTION 'Operation % on % is prohibited', TG_OP, TG_TABLE_NAME USING ERRCODE='23514';
END $$;

CREATE TRIGGER ledger_append_only BEFORE UPDATE OR DELETE OR TRUNCATE
ON public.inventory_transactions FOR EACH STATEMENT EXECUTE FUNCTION public.oisms_deny_mutation();

-- Acquire before changing any of the four projections/sources. All keys in a multi-SKU
-- operation should also be prelocked by the caller in sorted order to avoid deadlocks.
CREATE FUNCTION public.oisms_lock_stock() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
BEGIN
  IF current_setting('transaction_isolation') <> 'read committed' THEN
    RAISE EXCEPTION 'Stock writes require READ COMMITTED isolation' USING ERRCODE='25000';
  END IF;
  IF TG_OP='UPDATE' AND
    (NEW.tenant_id,NEW.branch_id,NEW.sku_id) IS DISTINCT FROM (OLD.tenant_id,OLD.branch_id,OLD.sku_id) THEN
    RAISE EXCEPTION 'Stock identity is immutable' USING ERRCODE='23514';
  END IF;
  IF TG_OP='UPDATE' AND TG_TABLE_NAME IN ('warehouse_inventory_balances','reservations') THEN
    IF NEW.warehouse_id IS DISTINCT FROM OLD.warehouse_id THEN
      RAISE EXCEPTION 'Warehouse identity is immutable' USING ERRCODE='23514';
    END IF;
  END IF;
  IF TG_OP='UPDATE' AND TG_TABLE_NAME='reservations' THEN
    IF (NEW.order_id,NEW.order_item_id) IS DISTINCT FROM (OLD.order_id,OLD.order_item_id) THEN
      RAISE EXCEPTION 'Reservation order identity is immutable' USING ERRCODE='23514';
    END IF;
  END IF;
  PERFORM pg_advisory_xact_lock(hashtextextended(NEW.tenant_id::text||'/'||NEW.branch_id::text||'/'||NEW.sku_id::text,0));
  RETURN NEW;
END $$;

CREATE FUNCTION public.oisms_check_stock(p_tenant uuid,p_branch uuid,p_sku uuid) RETURNS void
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
  -- Full outer joins also detect missing warehouse balances.
  IF EXISTS (
    WITH w AS (
      SELECT warehouse_id,on_hand,reserved,last_sequence FROM public.warehouse_inventory_balances
      WHERE tenant_id=p_tenant AND branch_id=p_branch AND sku_id=p_sku
    ), l AS (
      SELECT warehouse_id,sum(CASE WHEN type='IN' THEN quantity ELSE -quantity END) qty,max(sequence_no) seq
      FROM public.inventory_transactions WHERE tenant_id=p_tenant AND branch_id=p_branch AND sku_id=p_sku GROUP BY warehouse_id
    ), r AS (
      SELECT warehouse_id,sum(quantity) qty FROM public.reservations
      WHERE tenant_id=p_tenant AND branch_id=p_branch AND sku_id=p_sku AND status='Active' GROUP BY warehouse_id
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
    GROUP BY i.id,i.quantity HAVING sum(r.quantity)>i.quantity
  ) THEN
    RAISE EXCEPTION 'Reservation allocation exceeds order quantity' USING ERRCODE='23514';
  END IF;
END $$;

CREATE FUNCTION public.oisms_stock_constraint() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
BEGIN
  PERFORM public.oisms_check_stock(NEW.tenant_id,NEW.branch_id,NEW.sku_id);
  RETURN NULL;
END $$;

DO $$ DECLARE t text; BEGIN
  FOREACH t IN ARRAY ARRAY['inventory_balances','warehouse_inventory_balances','reservations','inventory_transactions'] LOOP
    EXECUTE format('CREATE TRIGGER stock_lock BEFORE INSERT OR UPDATE ON public.%I FOR EACH ROW EXECUTE FUNCTION public.oisms_lock_stock()',t);
    EXECUTE format('CREATE CONSTRAINT TRIGGER stock_consistency AFTER INSERT OR UPDATE ON public.%I DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.oisms_stock_constraint()',t);
    IF t<>'inventory_transactions' THEN
      EXECUTE format('CREATE TRIGGER stock_no_delete BEFORE DELETE OR TRUNCATE ON public.%I FOR EACH STATEMENT EXECUTE FUNCTION public.oisms_deny_mutation()',t);
    END IF;
  END LOOP;
END $$;

-- A confirmed order must retain its cost snapshot, including later status changes.
CREATE FUNCTION public.oisms_order_cost_constraint() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
DECLARE oid uuid; tid uuid;
BEGIN
  IF TG_TABLE_NAME='orders' THEN oid:=NEW.id; tid:=NEW.tenant_id;
  ELSE oid:=NEW.order_id; tid:=NEW.tenant_id; END IF;
  IF EXISTS(SELECT 1 FROM public.orders WHERE id=oid AND tenant_id=tid AND confirmed_at IS NOT NULL) THEN
    IF NOT EXISTS(SELECT 1 FROM public.order_items WHERE order_id=oid AND tenant_id=tid)
       OR EXISTS(SELECT 1 FROM public.order_items WHERE order_id=oid AND tenant_id=tid AND cost_price IS NULL) THEN
      RAISE EXCEPTION 'Confirmed orders require items with CostPrice snapshots' USING ERRCODE='23514';
    END IF;
  END IF;
  RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER order_cost_snapshot AFTER INSERT OR UPDATE ON public.orders
DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.oisms_order_cost_constraint();
CREATE CONSTRAINT TRIGGER item_cost_snapshot AFTER INSERT OR UPDATE ON public.order_items
DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.oisms_order_cost_constraint();

CREATE FUNCTION public.oisms_protect_order_item() RETURNS trigger
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
          (NEW.cost_price,NEW.quantity,NEW.unit_price) IS DISTINCT FROM (OLD.cost_price,OLD.quantity,OLD.unit_price)) THEN
      RAISE EXCEPTION 'Order item identity or confirmed financial snapshot is immutable' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
  END IF;
  RETURN OLD;
END $$;
CREATE TRIGGER protect_order_item BEFORE UPDATE OR DELETE ON public.order_items
FOR EACH ROW EXECUTE FUNCTION public.oisms_protect_order_item();
CREATE TRIGGER no_truncate_order_items BEFORE TRUNCATE ON public.order_items
FOR EACH STATEMENT EXECUTE FUNCTION public.oisms_deny_mutation();

CREATE FUNCTION public.oisms_protect_order() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
BEGIN
  IF (NEW.tenant_id,NEW.branch_id) IS DISTINCT FROM (OLD.tenant_id,OLD.branch_id)
     OR (OLD.confirmed_at IS NOT NULL AND NEW.confirmed_at IS DISTINCT FROM OLD.confirmed_at) THEN
    RAISE EXCEPTION 'Order identity and confirmed timestamp are immutable' USING ERRCODE='23514';
  END IF;
  RETURN NEW;
END $$;
CREATE TRIGGER protect_order BEFORE UPDATE ON public.orders
FOR EACH ROW EXECUTE FUNCTION public.oisms_protect_order();

-- Validate existing data before enabling this migration on v0.4.
DO $$ DECLARE k record; BEGIN
  FOR k IN
    SELECT tenant_id,branch_id,sku_id FROM public.inventory_balances UNION
    SELECT tenant_id,branch_id,sku_id FROM public.warehouse_inventory_balances UNION
    SELECT tenant_id,branch_id,sku_id FROM public.inventory_transactions UNION
    SELECT tenant_id,branch_id,sku_id FROM public.reservations
  LOOP PERFORM public.oisms_check_stock(k.tenant_id,k.branch_id,k.sku_id); END LOOP;
  IF EXISTS(SELECT 1 FROM public.orders o WHERE confirmed_at IS NOT NULL AND
    (NOT EXISTS(SELECT 1 FROM public.order_items i WHERE i.order_id=o.id AND i.tenant_id=o.tenant_id)
     OR EXISTS(SELECT 1 FROM public.order_items i WHERE i.order_id=o.id AND i.tenant_id=o.tenant_id AND cost_price IS NULL))) THEN
    RAISE EXCEPTION 'Existing confirmed orders lack cost snapshots' USING ERRCODE='23514';
  END IF;
END $$;


-- Runtime group permissions (no login or password is provisioned).
DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='oisms_runtime') THEN CREATE ROLE oisms_runtime NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS; END IF; END $$;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO oisms_runtime;
REVOKE ALL ON public."tenants", public."users", public."refresh_tokens", public."branches", public."warehouses", public."categories", public."brands", public."products", public."skus", public."suppliers", public."goods_receipts", public."goods_receipt_items", public."inventory_balances", public."warehouse_inventory_balances", public."stock_transfers", public."stock_transfer_items", public."stock_counts", public."stock_count_items", public."inventory_adjustments", public."inventory_adjustment_items", public."orders", public."order_items", public."reservations", public."payments", public."sales_returns", public."sales_return_items", public."inventory_transactions", public."daily_sales_summaries", public."replenishment_suggestions" FROM PUBLIC, oisms_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON public."tenants", public."users", public."refresh_tokens", public."branches", public."warehouses", public."categories", public."brands", public."products", public."skus", public."suppliers", public."goods_receipts", public."goods_receipt_items", public."inventory_balances", public."warehouse_inventory_balances", public."stock_transfers", public."stock_transfer_items", public."stock_counts", public."stock_count_items", public."inventory_adjustments", public."inventory_adjustment_items", public."orders", public."order_items", public."reservations", public."payments", public."sales_returns", public."sales_return_items", public."inventory_transactions", public."daily_sales_summaries", public."replenishment_suggestions" TO oisms_runtime;
REVOKE UPDATE, DELETE, TRUNCATE ON public.inventory_transactions FROM oisms_runtime;
REVOKE DELETE, TRUNCATE ON public.inventory_balances, public.warehouse_inventory_balances, public.reservations FROM oisms_runtime;
-- Balance writes are allowed only when deferred checks reconcile ledger/reservations at COMMIT.
-- No TRUNCATE, TRIGGER or schema ownership is granted. Guards execute as invoker.

COMMIT;
