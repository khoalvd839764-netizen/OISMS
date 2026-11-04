-- OISMS Upgrade 02 - Hóa đơn bán và truy vết chứng từ xuất (Sales Invoices & Document Trace) incremental upgrade.
-- Idempotent migration script for PostgreSQL schema.
-- Run with psql as the migration/admin user.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL search_path = public, pg_catalog;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '5min';
SELECT pg_advisory_xact_lock(hashtext('oisms_sales_invoice_schema_upgrade'));

-- =========================================================================
-- 1. Bảng sales_invoices (Bảng thứ 40 - Append-only)
-- =========================================================================
CREATE TABLE IF NOT EXISTS public.sales_invoices (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    number varchar(50) NOT NULL,
    order_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    channel varchar(20) NOT NULL,
    branch_name_snapshot varchar(200) NOT NULL,
    branch_address_snapshot text NULL,
    customer_name_snapshot varchar(200) NULL,
    customer_phone_snapshot varchar(32) NULL,
    issued_by uuid NULL,
    issued_by_name_snapshot varchar(150) NULL,
    total_amount numeric(20,2) NOT NULL,
    payment_method_snapshot varchar(20) NULL,
    issued_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NULL,
    CONSTRAINT uq_sales_invoices_1 UNIQUE (tenant_id, id),
    CONSTRAINT uq_sales_invoices_2 UNIQUE (tenant_id, number),
    CONSTRAINT uq_sales_invoices_3 UNIQUE (tenant_id, order_id),
    CONSTRAINT ck_sales_invoices_channel_values CHECK (channel IN ('POS', 'Admin', 'Shopee', 'TikTok', 'Lazada')),
    CONSTRAINT ck_sales_invoices_total_amount_nonnegative CHECK (total_amount >= 0),
    CONSTRAINT ck_sales_invoices_payment_method_values CHECK (payment_method_snapshot IS NULL OR payment_method_snapshot IN ('Cash','BankTransfer'))
);

CREATE INDEX IF NOT EXISTS ix_sales_invoices_time
ON public.sales_invoices (tenant_id, issued_at, id);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sales_invoices_tenant_id') THEN
        ALTER TABLE public.sales_invoices ADD CONSTRAINT fk_sales_invoices_tenant_id
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sales_invoices_order_branch') THEN
        ALTER TABLE public.sales_invoices ADD CONSTRAINT fk_sales_invoices_order_branch
        FOREIGN KEY (tenant_id, order_id, branch_id) REFERENCES public.orders (tenant_id, id, branch_id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sales_invoices_branch_id') THEN
        ALTER TABLE public.sales_invoices ADD CONSTRAINT fk_sales_invoices_branch_id
        FOREIGN KEY (tenant_id, branch_id) REFERENCES public.branches (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_sales_invoices_issued_by') THEN
        ALTER TABLE public.sales_invoices ADD CONSTRAINT fk_sales_invoices_issued_by
        FOREIGN KEY (tenant_id, issued_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
END $$;

-- =========================================================================
-- 2. Thêm cột vào orders và stock_transfers
-- =========================================================================
ALTER TABLE public.orders ADD COLUMN IF NOT EXISTS confirmed_by uuid NULL;
ALTER TABLE public.stock_transfers ADD COLUMN IF NOT EXISTS dispatched_by uuid NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_orders_confirmed_by') THEN
        ALTER TABLE public.orders ADD CONSTRAINT fk_orders_confirmed_by
        FOREIGN KEY (tenant_id, confirmed_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_transfers_dispatched_by') THEN
        ALTER TABLE public.stock_transfers ADD CONSTRAINT fk_stock_transfers_dispatched_by
        FOREIGN KEY (tenant_id, dispatched_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
END $$;

-- =========================================================================
-- 3. Trigger và Functions bảo vệ
-- =========================================================================
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger tg
        JOIN pg_class c ON tg.tgrelid = c.oid
        WHERE c.relname = 'sales_invoices' AND tg.tgname = 'sales_invoice_append_only'
    ) THEN
        CREATE TRIGGER sales_invoice_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON public.sales_invoices
        FOR EACH STATEMENT EXECUTE FUNCTION public.oisms_deny_mutation();
    END IF;
END $$;

CREATE OR REPLACE FUNCTION public.oisms_protect_order() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
BEGIN
  IF (NEW.tenant_id,NEW.branch_id) IS DISTINCT FROM (OLD.tenant_id,OLD.branch_id)
     OR (OLD.confirmed_at IS NOT NULL AND (NEW.confirmed_at, NEW.confirmed_by) IS DISTINCT FROM (OLD.confirmed_at, OLD.confirmed_by)) THEN
    RAISE EXCEPTION 'Order identity and confirmed timestamp are immutable' USING ERRCODE='23514';
  END IF;
  RETURN NEW;
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
            (NEW.cost_price,NEW.quantity,NEW.unit_price,NEW.factor_snapshot,NEW.sku_code_snapshot,NEW.sku_name_snapshot)
              IS DISTINCT FROM (OLD.cost_price,OLD.quantity,OLD.unit_price,OLD.factor_snapshot,OLD.sku_code_snapshot,OLD.sku_name_snapshot)
            OR (OLD.unit_id IS NOT NULL AND (NEW.unit_id,NEW.unit_name_snapshot) IS DISTINCT FROM (OLD.unit_id,OLD.unit_name_snapshot))
          )) THEN
      RAISE EXCEPTION 'Order item identity or confirmed financial snapshot is immutable' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
  END IF;
  RETURN OLD;
END $$;

-- =========================================================================
-- 4. Quyền cho oisms_runtime
-- =========================================================================
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oisms_runtime') THEN
        REVOKE ALL ON TABLE public.sales_invoices FROM oisms_runtime;
        GRANT SELECT, INSERT ON TABLE public.sales_invoices TO oisms_runtime;
        REVOKE UPDATE, DELETE, TRUNCATE ON TABLE public.sales_invoices FROM oisms_runtime;
    END IF;
END $$;

COMMIT;
