-- OISMS Upgrade 13 - Phiếu xuất kho 02-VT lưu kèm dòng sổ OUT (Goods Issues & Goods Issue Items).
-- Idempotent migration script for PostgreSQL schema.
-- Run with psql as the migration/admin user.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL search_path = public, pg_catalog;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '5min';
SELECT pg_advisory_xact_lock(hashtext('oisms_goods_issues_schema_upgrade'));

-- =========================================================================
-- 1. Kiểm tra dữ liệu cũ (P6, Q10): Dừng và hướng dẫn reset nếu DB đã có dòng OUT bán hàng chưa có phiếu xuất kho
-- =========================================================================
DO $$
BEGIN
    IF to_regclass('public.goods_issue_items') IS NULL THEN
        IF EXISTS (
            SELECT 1 FROM public.inventory_transactions
            WHERE order_item_id IS NOT NULL
        ) THEN
            RAISE EXCEPTION 'Database đã có dòng bút toán xuất bán (inventory_transactions.order_item_id IS NOT NULL) trước khi nâng cấp phiếu xuất kho 02-VT (Upgrade 13 - P6/Q10). Không lập bù dữ liệu cũ; vui lòng reset database từ oisms_schema.sql và seed lại dữ liệu demo.' USING ERRCODE = '23514';
        END IF;
    ELSE
        IF EXISTS (
            SELECT 1
            FROM public.inventory_transactions t
            LEFT JOIN public.goods_issue_items gii
              ON gii.tenant_id = t.tenant_id AND gii.inventory_transaction_id = t.id
            WHERE t.order_item_id IS NOT NULL AND gii.id IS NULL
        ) THEN
            RAISE EXCEPTION 'Phát hiện dòng bút toán xuất bán (inventory_transactions.order_item_id IS NOT NULL) chưa có dòng phiếu xuất kho (goods_issue_items) tương ứng (Upgrade 13 - P6/Q10). Vui lòng reset database từ oisms_schema.sql và seed lại dữ liệu demo.' USING ERRCODE = '23514';
        END IF;
    END IF;
END $$;

-- =========================================================================
-- 2. Bảng goods_issues (Bảng thứ 49 - Append-only)
-- =========================================================================
CREATE TABLE IF NOT EXISTS public.goods_issues (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    number varchar(64) NOT NULL,
    order_id uuid NOT NULL,
    sales_invoice_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    warehouse_id uuid NOT NULL,
    channel varchar(20) NOT NULL,
    order_number_snapshot varchar(50) NOT NULL,
    sales_invoice_number_snapshot varchar(50) NOT NULL,
    branch_name_snapshot varchar(200) NOT NULL,
    warehouse_name_snapshot varchar(200) NOT NULL,
    customer_name_snapshot varchar(200) NULL,
    issued_by uuid NULL,
    issued_by_name_snapshot varchar(150) NULL,
    issued_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NULL,
    CONSTRAINT uq_goods_issues_1 UNIQUE (tenant_id, id),
    CONSTRAINT uq_goods_issues_2 UNIQUE (tenant_id, number),
    CONSTRAINT uq_goods_issues_3 UNIQUE (tenant_id, order_id, warehouse_id),
    CONSTRAINT ck_goods_issues_channel_values CHECK (channel IN ('POS', 'Admin', 'Shopee', 'TikTok', 'Lazada')),
    CONSTRAINT fk_goods_issues_tenant_id FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT ON UPDATE NO ACTION,
    CONSTRAINT fk_goods_issues_order_branch FOREIGN KEY (tenant_id, order_id, branch_id) REFERENCES public.orders (tenant_id, id, branch_id) ON DELETE RESTRICT ON UPDATE NO ACTION,
    CONSTRAINT fk_goods_issues_sales_invoice FOREIGN KEY (tenant_id, sales_invoice_id) REFERENCES public.sales_invoices (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION,
    CONSTRAINT fk_goods_issues_branch_id FOREIGN KEY (tenant_id, branch_id) REFERENCES public.branches (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION,
    CONSTRAINT fk_goods_issues_warehouse_branch FOREIGN KEY (tenant_id, warehouse_id, branch_id) REFERENCES public.warehouses (tenant_id, id, branch_id) ON DELETE RESTRICT ON UPDATE NO ACTION,
    CONSTRAINT fk_goods_issues_issued_by FOREIGN KEY (tenant_id, issued_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION
);

CREATE INDEX IF NOT EXISTS ix_goods_issues_order
    ON public.goods_issues (tenant_id, order_id, warehouse_id);
CREATE INDEX IF NOT EXISTS ix_goods_issues_sales_invoice
    ON public.goods_issues (tenant_id, sales_invoice_id);
CREATE INDEX IF NOT EXISTS ix_goods_issues_branch_time
    ON public.goods_issues (tenant_id, branch_id, issued_at, id);

-- =========================================================================
-- 3. Bảng goods_issue_items (Bảng thứ 50 - Append-only)
-- =========================================================================
CREATE TABLE IF NOT EXISTS public.goods_issue_items (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    goods_issue_id uuid NOT NULL,
    inventory_transaction_id uuid NOT NULL,
    order_item_id uuid NOT NULL,
    sku_id uuid NOT NULL,
    sku_code_snapshot varchar(80) NOT NULL,
    sku_name_snapshot varchar(250) NOT NULL,
    unit_id uuid NULL,
    unit_name_snapshot varchar(100) NOT NULL DEFAULT '',
    factor_snapshot numeric(18,6) NOT NULL DEFAULT 1,
    quantity numeric(18,3) NOT NULL,
    base_quantity numeric(18,3) NOT NULL,
    unit_cost numeric(20,6) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NULL,
    CONSTRAINT uq_goods_issue_items_1 UNIQUE (tenant_id, id),
    CONSTRAINT uq_goods_issue_items_2 UNIQUE (tenant_id, inventory_transaction_id),
    CONSTRAINT ck_goods_issue_items_quantity_positive CHECK (quantity > 0),
    CONSTRAINT ck_goods_issue_items_base_quantity_positive CHECK (base_quantity > 0),
    CONSTRAINT ck_goods_issue_items_factor_snapshot_positive CHECK (factor_snapshot > 0),
    CONSTRAINT ck_goods_issue_items_unit_cost_nonnegative CHECK (unit_cost >= 0),
    CONSTRAINT fk_goods_issue_items_tenant_id FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT ON UPDATE NO ACTION,
    CONSTRAINT fk_goods_issue_items_goods_issue_id FOREIGN KEY (tenant_id, goods_issue_id) REFERENCES public.goods_issues (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION,
    CONSTRAINT fk_goods_issue_items_inventory_tx FOREIGN KEY (tenant_id, inventory_transaction_id) REFERENCES public.inventory_transactions (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION,
    CONSTRAINT fk_goods_issue_items_order_item FOREIGN KEY (tenant_id, order_item_id) REFERENCES public.order_items (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION,
    CONSTRAINT fk_goods_issue_items_sku_id FOREIGN KEY (tenant_id, sku_id) REFERENCES public.skus (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION,
    CONSTRAINT fk_goods_issue_items_unit_id FOREIGN KEY (tenant_id, unit_id) REFERENCES public.units (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION
);

CREATE INDEX IF NOT EXISTS ix_goods_issue_items_goods_issue
    ON public.goods_issue_items (tenant_id, goods_issue_id);
CREATE INDEX IF NOT EXISTS ix_goods_issue_items_order_item
    ON public.goods_issue_items (tenant_id, order_item_id);

-- =========================================================================
-- 4. Trigger bảo vệ Append-only và phân quyền runtime
-- =========================================================================
CREATE OR REPLACE TRIGGER goods_issue_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON public.goods_issues
    FOR EACH STATEMENT EXECUTE FUNCTION public.oisms_deny_mutation();
CREATE OR REPLACE TRIGGER goods_issue_item_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON public.goods_issue_items
    FOR EACH STATEMENT EXECUTE FUNCTION public.oisms_deny_mutation();

REVOKE ALL ON public.goods_issues, public.goods_issue_items FROM PUBLIC, oisms_runtime;
GRANT SELECT, INSERT ON public.goods_issues, public.goods_issue_items TO oisms_runtime;
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oisms_app') THEN
        REVOKE UPDATE, DELETE, TRUNCATE ON public.goods_issues, public.goods_issue_items FROM oisms_app;
        GRANT SELECT, INSERT ON public.goods_issues, public.goods_issue_items TO oisms_app;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oisms_test_app') THEN
        REVOKE UPDATE, DELETE, TRUNCATE ON public.goods_issues, public.goods_issue_items FROM oisms_test_app;
        GRANT SELECT, INSERT ON public.goods_issues, public.goods_issue_items TO oisms_test_app;
    END IF;
END $$;

COMMIT;
