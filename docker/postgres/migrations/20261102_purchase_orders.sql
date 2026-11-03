-- OISMS Upgrade 01-A - Phiếu mua hàng và nhập kho từ phiếu mua (Purchase Orders) incremental upgrade.
-- Idempotent migration script for PostgreSQL schema.
-- Run with psql as the migration/admin user.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL search_path = public, pg_catalog;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '5min';
SELECT pg_advisory_xact_lock(hashtext('oisms_purchase_order_schema_upgrade'));

-- =========================================================================
-- 1. Bảng purchase_orders
-- =========================================================================
CREATE TABLE IF NOT EXISTS public.purchase_orders (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    number varchar(50) NOT NULL,
    request_key uuid NOT NULL,
    supplier_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    status varchar(20) NOT NULL,
    note varchar(1000) NULL,
    created_by uuid NOT NULL,
    approved_by uuid NULL,
    approved_at timestamptz NULL,
    closed_by uuid NULL,
    closed_at timestamptz NULL,
    close_reason varchar(500) NULL,
    cancelled_by uuid NULL,
    cancelled_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NULL,
    CONSTRAINT uq_purchase_orders_1 UNIQUE (tenant_id, id),
    CONSTRAINT uq_purchase_orders_2 UNIQUE (tenant_id, number),
    CONSTRAINT uq_purchase_orders_3 UNIQUE (tenant_id, request_key),
    CONSTRAINT ck_purchase_orders_status CHECK (status IN ('Draft','Approved','PartiallyReceived','Received','Closed','Cancelled')),
    CONSTRAINT ck_purchase_orders_approved CHECK ((approved_at IS NULL) = (approved_by IS NULL) AND (status NOT IN ('Approved','PartiallyReceived','Received','Closed') OR approved_at IS NOT NULL)),
    CONSTRAINT ck_purchase_orders_closed CHECK ((status = 'Closed') = (closed_at IS NOT NULL) AND (closed_at IS NULL) = (closed_by IS NULL) AND (closed_at IS NULL OR btrim(close_reason) <> '')),
    CONSTRAINT ck_purchase_orders_cancelled CHECK ((status = 'Cancelled') = (cancelled_at IS NOT NULL) AND (cancelled_at IS NULL) = (cancelled_by IS NULL))
);

CREATE INDEX IF NOT EXISTS ix_purchase_orders_status_time
ON public.purchase_orders (tenant_id, status, created_at, id);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_purchase_orders_tenant_id') THEN
        ALTER TABLE public.purchase_orders ADD CONSTRAINT fk_purchase_orders_tenant_id
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_purchase_orders_supplier_id') THEN
        ALTER TABLE public.purchase_orders ADD CONSTRAINT fk_purchase_orders_supplier_id
        FOREIGN KEY (tenant_id, supplier_id) REFERENCES public.suppliers (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_purchase_orders_branch_id') THEN
        ALTER TABLE public.purchase_orders ADD CONSTRAINT fk_purchase_orders_branch_id
        FOREIGN KEY (tenant_id, branch_id) REFERENCES public.branches (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_purchase_orders_created_by') THEN
        ALTER TABLE public.purchase_orders ADD CONSTRAINT fk_purchase_orders_created_by
        FOREIGN KEY (tenant_id, created_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_purchase_orders_approved_by') THEN
        ALTER TABLE public.purchase_orders ADD CONSTRAINT fk_purchase_orders_approved_by
        FOREIGN KEY (tenant_id, approved_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_purchase_orders_closed_by') THEN
        ALTER TABLE public.purchase_orders ADD CONSTRAINT fk_purchase_orders_closed_by
        FOREIGN KEY (tenant_id, closed_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_purchase_orders_cancelled_by') THEN
        ALTER TABLE public.purchase_orders ADD CONSTRAINT fk_purchase_orders_cancelled_by
        FOREIGN KEY (tenant_id, cancelled_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
END $$;

-- =========================================================================
-- 2. Bảng purchase_order_items
-- =========================================================================
CREATE TABLE IF NOT EXISTS public.purchase_order_items (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    purchase_order_id uuid NOT NULL,
    sku_id uuid NOT NULL,
    unit_id uuid NULL,
    unit_name_snapshot varchar(100) NOT NULL DEFAULT '',
    factor_snapshot numeric(18,6) NOT NULL DEFAULT 1,
    quantity_decimal_places smallint NOT NULL DEFAULT 3,
    quantity numeric(18,3) NOT NULL,
    ordered_base_quantity numeric(18,3) GENERATED ALWAYS AS (quantity * factor_snapshot) STORED,
    unit_price numeric(20,6) NOT NULL,
    received_base_quantity numeric(18,3) NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NULL,
    CONSTRAINT uq_purchase_order_items_1 UNIQUE (tenant_id, id),
    CONSTRAINT uq_purchase_order_items_2 UNIQUE (tenant_id, purchase_order_id, sku_id),
    CONSTRAINT uq_purchase_order_items_3 UNIQUE (tenant_id, id, sku_id),
    CONSTRAINT ck_purchase_order_items_factor_snapshot CHECK (factor_snapshot > 0),
    CONSTRAINT ck_purchase_order_items_quantity CHECK (quantity > 0),
    CONSTRAINT ck_purchase_order_items_unit_price CHECK (unit_price >= 0),
    CONSTRAINT ck_purchase_order_items_decimal_places CHECK (quantity_decimal_places BETWEEN 0 AND 3),
    CONSTRAINT ck_purchase_order_items_received_range CHECK (received_base_quantity >= 0 AND received_base_quantity <= quantity * factor_snapshot)
);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_purchase_order_items_tenant_id') THEN
        ALTER TABLE public.purchase_order_items ADD CONSTRAINT fk_purchase_order_items_tenant_id
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_purchase_order_items_purchase_order_id') THEN
        ALTER TABLE public.purchase_order_items ADD CONSTRAINT fk_purchase_order_items_purchase_order_id
        FOREIGN KEY (tenant_id, purchase_order_id) REFERENCES public.purchase_orders (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_purchase_order_items_sku_id') THEN
        ALTER TABLE public.purchase_order_items ADD CONSTRAINT fk_purchase_order_items_sku_id
        FOREIGN KEY (tenant_id, sku_id) REFERENCES public.skus (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_purchase_order_items_unit_id') THEN
        ALTER TABLE public.purchase_order_items ADD CONSTRAINT fk_purchase_order_items_unit_id
        FOREIGN KEY (tenant_id, unit_id) REFERENCES public.units (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
END $$;

-- =========================================================================
-- 3. Thêm cột vào goods_receipts và goods_receipt_items
-- =========================================================================
ALTER TABLE public.goods_receipts ADD COLUMN IF NOT EXISTS purchase_order_id uuid NULL;
ALTER TABLE public.goods_receipt_items ADD COLUMN IF NOT EXISTS purchase_order_item_id uuid NULL;

CREATE INDEX IF NOT EXISTS ix_goods_receipts_purchase_order
ON public.goods_receipts (tenant_id, purchase_order_id);

CREATE INDEX IF NOT EXISTS ix_goods_receipt_items_purchase_order_item
ON public.goods_receipt_items (tenant_id, purchase_order_item_id);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_goods_receipts_purchase_order_id') THEN
        ALTER TABLE public.goods_receipts ADD CONSTRAINT fk_goods_receipts_purchase_order_id
        FOREIGN KEY (tenant_id, purchase_order_id) REFERENCES public.purchase_orders (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_goods_receipt_items_po_item') THEN
        ALTER TABLE public.goods_receipt_items ADD CONSTRAINT fk_goods_receipt_items_po_item
        FOREIGN KEY (tenant_id, purchase_order_item_id, sku_id) REFERENCES public.purchase_order_items (tenant_id, id, sku_id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
END $$;

-- =========================================================================
-- 4. Functions và Triggers bảo vệ
-- =========================================================================
CREATE OR REPLACE FUNCTION public.oisms_purchase_order_guard() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
BEGIN
  IF OLD.status = 'Draft' THEN
    IF NEW.status NOT IN ('Draft', 'Approved', 'Cancelled') THEN
      RAISE EXCEPTION 'Invalid purchase order state transition from % to %', OLD.status, NEW.status USING ERRCODE = '23514';
    END IF;
    IF (NEW.tenant_id, NEW.id, NEW.created_at) IS DISTINCT FROM (OLD.tenant_id, OLD.id, OLD.created_at) THEN
      RAISE EXCEPTION 'Purchase order identity is immutable' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
  END IF;

  IF (NEW.tenant_id, NEW.id, NEW.number, NEW.request_key, NEW.supplier_id, NEW.branch_id, NEW.note, NEW.created_by, NEW.created_at, NEW.approved_by, NEW.approved_at)
     IS DISTINCT FROM
     (OLD.tenant_id, OLD.id, OLD.number, OLD.request_key, OLD.supplier_id, OLD.branch_id, OLD.note, OLD.created_by, OLD.created_at, OLD.approved_by, OLD.approved_at) THEN
    RAISE EXCEPTION 'Purchase order header attributes are immutable outside Draft' USING ERRCODE = '23514';
  END IF;

  IF OLD.status = 'Approved' THEN
    IF NEW.status NOT IN ('Approved', 'PartiallyReceived', 'Received', 'Closed', 'Cancelled') THEN
      RAISE EXCEPTION 'Invalid purchase order state transition from % to %', OLD.status, NEW.status USING ERRCODE = '23514';
    END IF;
  ELSIF OLD.status = 'PartiallyReceived' THEN
    IF NEW.status NOT IN ('PartiallyReceived', 'Received', 'Closed') THEN
      RAISE EXCEPTION 'Invalid purchase order state transition from % to %', OLD.status, NEW.status USING ERRCODE = '23514';
    END IF;
  ELSIF OLD.status IN ('Received', 'Closed', 'Cancelled') THEN
    RAISE EXCEPTION 'Terminal purchase order in state % is immutable', OLD.status USING ERRCODE = '23514';
  ELSE
    RAISE EXCEPTION 'Invalid purchase order state %', OLD.status USING ERRCODE = '23514';
  END IF;

  RETURN NEW;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger tg
        JOIN pg_class c ON tg.tgrelid = c.oid
        WHERE c.relname = 'purchase_orders' AND tg.tgname = 'purchase_order_guard'
    ) THEN
        CREATE TRIGGER purchase_order_guard BEFORE UPDATE ON public.purchase_orders
        FOR EACH ROW EXECUTE FUNCTION public.oisms_purchase_order_guard();
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger tg
        JOIN pg_class c ON tg.tgrelid = c.oid
        WHERE c.relname = 'purchase_orders' AND tg.tgname = 'purchase_order_no_delete'
    ) THEN
        CREATE TRIGGER purchase_order_no_delete BEFORE DELETE OR TRUNCATE ON public.purchase_orders
        FOR EACH STATEMENT EXECUTE FUNCTION public.oisms_deny_mutation();
    END IF;
END $$;

CREATE OR REPLACE FUNCTION public.oisms_purchase_order_item_guard() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
DECLARE
  v_po_status varchar(20);
BEGIN
  IF TG_OP = 'INSERT' THEN
    SELECT status INTO v_po_status FROM public.purchase_orders
    WHERE id = NEW.purchase_order_id AND tenant_id = NEW.tenant_id FOR UPDATE;

    IF v_po_status IS DISTINCT FROM 'Draft' THEN
      RAISE EXCEPTION 'Cannot insert purchase order item when purchase order is in state %', v_po_status USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
  END IF;

  SELECT status INTO v_po_status FROM public.purchase_orders
  WHERE id = OLD.purchase_order_id AND tenant_id = OLD.tenant_id FOR UPDATE;

  IF TG_OP = 'DELETE' THEN
    IF v_po_status IS DISTINCT FROM 'Draft' THEN
      RAISE EXCEPTION 'Cannot delete purchase order item when purchase order is in state %', v_po_status USING ERRCODE = '23514';
    END IF;
    RETURN OLD;
  END IF;

  IF TG_OP = 'UPDATE' THEN
    IF v_po_status = 'Draft' THEN
      IF (NEW.tenant_id, NEW.id, NEW.purchase_order_id, NEW.created_at) IS DISTINCT FROM (OLD.tenant_id, OLD.id, OLD.purchase_order_id, OLD.created_at) THEN
        RAISE EXCEPTION 'Purchase order item identity is immutable' USING ERRCODE = '23514';
      END IF;
      RETURN NEW;
    END IF;

    IF (NEW.tenant_id, NEW.id, NEW.purchase_order_id, NEW.sku_id, NEW.unit_id, NEW.unit_name_snapshot, NEW.factor_snapshot, NEW.quantity_decimal_places, NEW.quantity, NEW.unit_price, NEW.created_at)
       IS DISTINCT FROM
       (OLD.tenant_id, OLD.id, OLD.purchase_order_id, OLD.sku_id, OLD.unit_id, OLD.unit_name_snapshot, OLD.factor_snapshot, OLD.quantity_decimal_places, OLD.quantity, OLD.unit_price, OLD.created_at) THEN
      RAISE EXCEPTION 'Purchase order item attributes other than received_base_quantity and updated_at are immutable outside Draft' USING ERRCODE = '23514';
    END IF;

    IF NEW.received_base_quantity < OLD.received_base_quantity THEN
      RAISE EXCEPTION 'received_base_quantity cannot decrease' USING ERRCODE = '23514';
    END IF;

    IF NEW.received_base_quantity > OLD.received_base_quantity AND v_po_status NOT IN ('Approved', 'PartiallyReceived') THEN
      RAISE EXCEPTION 'Cannot increase received_base_quantity when purchase order is in state %', v_po_status USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
  END IF;

  RETURN NULL;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger tg
        JOIN pg_class c ON tg.tgrelid = c.oid
        WHERE c.relname = 'purchase_order_items' AND tg.tgname = 'purchase_order_item_guard'
    ) THEN
        CREATE TRIGGER purchase_order_item_guard BEFORE INSERT OR UPDATE OR DELETE ON public.purchase_order_items
        FOR EACH ROW EXECUTE FUNCTION public.oisms_purchase_order_item_guard();
    END IF;
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
     ((NEW.tenant_id,NEW.receipt_id,NEW.sku_id,NEW.purchase_order_item_id,NEW.quantity,NEW.unit_price,NEW.factor_snapshot) IS DISTINCT FROM
      (OLD.tenant_id,OLD.receipt_id,OLD.sku_id,OLD.purchase_order_item_id,OLD.quantity,OLD.unit_price,OLD.factor_snapshot)
      OR (OLD.unit_id IS NOT NULL AND (NEW.unit_id,NEW.unit_name_snapshot) IS DISTINCT FROM (OLD.unit_id,OLD.unit_name_snapshot))) THEN
    RAISE EXCEPTION 'Confirmed goods receipt item snapshot is immutable' USING ERRCODE='23514';
  END IF;
  IF TG_OP='UPDATE' THEN RETURN NEW; END IF;
  RETURN OLD;
END $$;

-- =========================================================================
-- 5. Quyền cho oisms_runtime
-- =========================================================================
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oisms_runtime') THEN
        REVOKE ALL ON TABLE public.purchase_orders FROM oisms_runtime;
        GRANT SELECT, INSERT, UPDATE ON TABLE public.purchase_orders TO oisms_runtime;
        REVOKE DELETE, TRUNCATE ON TABLE public.purchase_orders FROM oisms_runtime;

        REVOKE ALL ON TABLE public.purchase_order_items FROM oisms_runtime;
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.purchase_order_items TO oisms_runtime;
        REVOKE TRUNCATE ON TABLE public.purchase_order_items FROM oisms_runtime;
    END IF;
END $$;

-- =========================================================================
-- 6. Thống kê số phiếu nhập Draft chưa gắn phiếu mua để admin xử lý
-- =========================================================================
DO $$
DECLARE
    v_orphan_draft_count bigint;
BEGIN
    SELECT count(*) INTO v_orphan_draft_count
    FROM public.goods_receipts
    WHERE status = 'Draft' AND purchase_order_id IS NULL;

    RAISE NOTICE 'Số phiếu nhập Draft chưa có phiếu mua: %', v_orphan_draft_count;
END $$;

COMMIT;
