-- Migration: U12 Đơn mua PendingApproval, phân bổ chi phí mua hàng và thu hồi quyền ghi hóa đơn NCC
-- Idempotent: có thể chạy lặp lại nhiều lần không gây lỗi.

BEGIN;

SELECT pg_advisory_xact_lock(hashtext('oisms_purchase_order_u12_schema_upgrade'));

-- 1. Mở rộng bảng purchase_orders: supplier_id nullable ở Draft/PendingApproval, thêm order_date, phí/CK/VAT, trả về và version
ALTER TABLE public.purchase_orders ALTER COLUMN supplier_id DROP NOT NULL;

ALTER TABLE public.purchase_orders ADD COLUMN IF NOT EXISTS order_date date NULL;
UPDATE public.purchase_orders
SET order_date = COALESCE(order_date, (created_at AT TIME ZONE 'Asia/Saigon')::date)
WHERE order_date IS NULL;
ALTER TABLE public.purchase_orders ALTER COLUMN order_date SET DEFAULT CURRENT_DATE;
ALTER TABLE public.purchase_orders ALTER COLUMN order_date SET NOT NULL;

ALTER TABLE public.purchase_orders ADD COLUMN IF NOT EXISTS shipping_fee numeric(20,6) NOT NULL DEFAULT 0;
ALTER TABLE public.purchase_orders ADD COLUMN IF NOT EXISTS discount_amount numeric(20,6) NOT NULL DEFAULT 0;
ALTER TABLE public.purchase_orders ADD COLUMN IF NOT EXISTS vat_amount numeric(20,6) NOT NULL DEFAULT 0;
ALTER TABLE public.purchase_orders ADD COLUMN IF NOT EXISTS return_reason varchar(500) NULL;
ALTER TABLE public.purchase_orders ADD COLUMN IF NOT EXISTS returned_by uuid NULL;
ALTER TABLE public.purchase_orders ADD COLUMN IF NOT EXISTS returned_at timestamptz NULL;
ALTER TABLE public.purchase_orders ADD COLUMN IF NOT EXISTS version integer NOT NULL DEFAULT 1;

-- 2. Mở rộng bảng purchase_order_items: unit_price nullable ở Draft/PendingApproval, thêm allocated_unit_cost
ALTER TABLE public.purchase_order_items ALTER COLUMN unit_price DROP NOT NULL;
ALTER TABLE public.purchase_order_items ADD COLUMN IF NOT EXISTS allocated_unit_cost numeric(20,6) NULL;

-- Backfill allocated_unit_cost cho các đơn mua đã duyệt trước đây (chưa có phí/chiết khấu riêng)
UPDATE public.purchase_order_items poi
SET allocated_unit_cost = poi.unit_price
FROM public.purchase_orders po
WHERE po.tenant_id = poi.tenant_id
  AND po.id = poi.purchase_order_id
  AND po.status IN ('Approved', 'PartiallyReceived', 'Received', 'Closed')
  AND poi.allocated_unit_cost IS NULL
  AND poi.unit_price IS NOT NULL;

-- 3. Cập nhật constraints và khóa ngoại
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_purchase_orders_status') THEN
        ALTER TABLE public.purchase_orders DROP CONSTRAINT ck_purchase_orders_status;
    END IF;
    ALTER TABLE public.purchase_orders ADD CONSTRAINT ck_purchase_orders_status
        CHECK (status IN ('Draft','PendingApproval','Approved','PartiallyReceived','Received','Closed','Cancelled'));

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_purchase_orders_supplier_required') THEN
        ALTER TABLE public.purchase_orders ADD CONSTRAINT ck_purchase_orders_supplier_required
            CHECK (status IN ('Draft','PendingApproval','Cancelled') OR supplier_id IS NOT NULL);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_purchase_orders_fees_nonnegative') THEN
        ALTER TABLE public.purchase_orders ADD CONSTRAINT ck_purchase_orders_fees_nonnegative
            CHECK (shipping_fee >= 0 AND discount_amount >= 0 AND vat_amount >= 0);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_purchase_orders_returned') THEN
        ALTER TABLE public.purchase_orders ADD CONSTRAINT ck_purchase_orders_returned
            CHECK ((returned_at IS NULL) = (returned_by IS NULL) AND (returned_at IS NULL) = (return_reason IS NULL) AND (return_reason IS NULL OR btrim(return_reason) <> ''));
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_purchase_orders_version_positive') THEN
        ALTER TABLE public.purchase_orders ADD CONSTRAINT ck_purchase_orders_version_positive
            CHECK (version >= 1);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_purchase_orders_returned_by') THEN
        ALTER TABLE public.purchase_orders ADD CONSTRAINT fk_purchase_orders_returned_by
            FOREIGN KEY (tenant_id, returned_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_purchase_order_items_unit_price') THEN
        ALTER TABLE public.purchase_order_items DROP CONSTRAINT ck_purchase_order_items_unit_price;
    END IF;
    ALTER TABLE public.purchase_order_items ADD CONSTRAINT ck_purchase_order_items_unit_price
        CHECK (unit_price IS NULL OR unit_price >= 0);

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_purchase_order_items_allocated_unit_cost') THEN
        ALTER TABLE public.purchase_order_items ADD CONSTRAINT ck_purchase_order_items_allocated_unit_cost
            CHECK (allocated_unit_cost IS NULL OR allocated_unit_cost >= 0);
    END IF;
END $$;

-- 4. Cập nhật hàm bảo vệ trạng thái purchase_orders và purchase_order_items
CREATE OR REPLACE FUNCTION public.oisms_purchase_order_guard() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
BEGIN
  IF OLD.status = 'Draft' THEN
    IF NEW.status NOT IN ('Draft', 'PendingApproval', 'Cancelled') THEN
      RAISE EXCEPTION 'Invalid purchase order state transition from % to %', OLD.status, NEW.status USING ERRCODE = '23514';
    END IF;
    IF (NEW.tenant_id, NEW.id, NEW.number, NEW.request_key, NEW.created_by, NEW.created_at)
       IS DISTINCT FROM
       (OLD.tenant_id, OLD.id, OLD.number, OLD.request_key, OLD.created_by, OLD.created_at) THEN
      RAISE EXCEPTION 'Purchase order identity is immutable' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
  END IF;

  IF OLD.status = 'PendingApproval' THEN
    IF NEW.status NOT IN ('PendingApproval', 'Draft', 'Approved', 'Cancelled') THEN
      RAISE EXCEPTION 'Invalid purchase order state transition from % to %', OLD.status, NEW.status USING ERRCODE = '23514';
    END IF;
    IF (NEW.tenant_id, NEW.id, NEW.number, NEW.request_key, NEW.created_by, NEW.created_at)
       IS DISTINCT FROM
       (OLD.tenant_id, OLD.id, OLD.number, OLD.request_key, OLD.created_by, OLD.created_at) THEN
      RAISE EXCEPTION 'Purchase order identity is immutable' USING ERRCODE = '23514';
    END IF;
    IF NEW.status = 'Draft' AND (NEW.returned_at IS NULL OR NEW.returned_by IS NULL OR NEW.return_reason IS NULL OR btrim(NEW.return_reason) = '') THEN
      RAISE EXCEPTION 'Returning purchase order to Draft requires returned_by, returned_at, and return_reason' USING ERRCODE = '23514';
    END IF;
    IF NEW.status = 'Approved' THEN
      IF NEW.supplier_id IS NULL THEN
        RAISE EXCEPTION 'Approved purchase order requires supplier_id' USING ERRCODE = '23514';
      END IF;
      IF NOT EXISTS (
        SELECT 1 FROM public.purchase_order_items poi
        WHERE poi.tenant_id = NEW.tenant_id AND poi.purchase_order_id = NEW.id
      ) THEN
        RAISE EXCEPTION 'Approved purchase order requires at least one item' USING ERRCODE = '23514';
      END IF;
      IF EXISTS (
        SELECT 1 FROM public.purchase_order_items poi
        WHERE poi.tenant_id = NEW.tenant_id AND poi.purchase_order_id = NEW.id
          AND (poi.unit_price IS NULL OR poi.allocated_unit_cost IS NULL)
      ) THEN
        RAISE EXCEPTION 'Approved purchase order requires unit_price and allocated_unit_cost on all items' USING ERRCODE = '23514';
      END IF;
    END IF;
    RETURN NEW;
  END IF;

  IF (NEW.tenant_id, NEW.id, NEW.number, NEW.request_key, NEW.supplier_id, NEW.branch_id, NEW.order_date, NEW.shipping_fee, NEW.discount_amount, NEW.vat_amount, NEW.note, NEW.created_by, NEW.created_at, NEW.approved_by, NEW.approved_at, NEW.returned_by, NEW.returned_at, NEW.return_reason)
     IS DISTINCT FROM
     (OLD.tenant_id, OLD.id, OLD.number, OLD.request_key, OLD.supplier_id, OLD.branch_id, OLD.order_date, OLD.shipping_fee, OLD.discount_amount, OLD.vat_amount, OLD.note, OLD.created_by, OLD.created_at, OLD.approved_by, OLD.approved_at, OLD.returned_by, OLD.returned_at, OLD.return_reason) THEN
    RAISE EXCEPTION 'Purchase order header attributes are immutable outside Draft and PendingApproval' USING ERRCODE = '23514';
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

CREATE OR REPLACE FUNCTION public.oisms_purchase_order_item_guard() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
DECLARE
  v_po_status varchar(20);
BEGIN
  IF TG_OP = 'INSERT' THEN
    SELECT status INTO v_po_status FROM public.purchase_orders
    WHERE id = NEW.purchase_order_id AND tenant_id = NEW.tenant_id FOR UPDATE;

    IF v_po_status IS NULL OR v_po_status NOT IN ('Draft', 'PendingApproval') THEN
      RAISE EXCEPTION 'Cannot insert purchase order item when purchase order is in state %', v_po_status USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
  END IF;

  SELECT status INTO v_po_status FROM public.purchase_orders
  WHERE id = OLD.purchase_order_id AND tenant_id = OLD.tenant_id FOR UPDATE;

  IF TG_OP = 'DELETE' THEN
    IF v_po_status IS NULL OR v_po_status NOT IN ('Draft', 'PendingApproval') THEN
      RAISE EXCEPTION 'Cannot delete purchase order item when purchase order is in state %', v_po_status USING ERRCODE = '23514';
    END IF;
    RETURN OLD;
  END IF;

  IF TG_OP = 'UPDATE' THEN
    IF v_po_status IN ('Draft', 'PendingApproval') THEN
      IF (NEW.tenant_id, NEW.id, NEW.purchase_order_id, NEW.created_at) IS DISTINCT FROM (OLD.tenant_id, OLD.id, OLD.purchase_order_id, OLD.created_at) THEN
        RAISE EXCEPTION 'Purchase order item identity is immutable' USING ERRCODE = '23514';
      END IF;
      RETURN NEW;
    END IF;

    IF (NEW.tenant_id, NEW.id, NEW.purchase_order_id, NEW.sku_id, NEW.unit_id, NEW.unit_name_snapshot, NEW.factor_snapshot, NEW.quantity_decimal_places, NEW.quantity, NEW.unit_price, NEW.allocated_unit_cost, NEW.created_at)
       IS DISTINCT FROM
       (OLD.tenant_id, OLD.id, OLD.purchase_order_id, OLD.sku_id, OLD.unit_id, OLD.unit_name_snapshot, OLD.factor_snapshot, OLD.quantity_decimal_places, OLD.quantity, OLD.unit_price, OLD.allocated_unit_cost, OLD.created_at) THEN
      RAISE EXCEPTION 'Purchase order item attributes other than received_base_quantity and updated_at are immutable outside Draft and PendingApproval' USING ERRCODE = '23514';
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

-- 5. Chuyển các đơn Draft cũ đã có đủ NCC và giá dòng sang PendingApproval
UPDATE public.purchase_orders po
SET status = 'PendingApproval',
    updated_at = COALESCE(po.updated_at, now())
WHERE po.status = 'Draft'
  AND po.supplier_id IS NOT NULL
  AND EXISTS (
      SELECT 1
      FROM public.purchase_order_items poi
      WHERE poi.tenant_id = po.tenant_id
        AND poi.purchase_order_id = po.id
  )
  AND NOT EXISTS (
      SELECT 1
      FROM public.purchase_order_items poi
      WHERE poi.tenant_id = po.tenant_id
        AND poi.purchase_order_id = po.id
        AND poi.unit_price IS NULL
  );

-- 6. Thu hồi quyền ghi trên các bảng hóa đơn NCC khỏi runtime role (chỉ giữ SELECT đọc lịch sử)
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'supplier_invoices') THEN
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oisms_runtime') THEN
            REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON public.supplier_invoices, public.supplier_invoice_items, public.purchase_variances FROM oisms_runtime;
            GRANT SELECT ON public.supplier_invoices, public.supplier_invoice_items, public.purchase_variances TO oisms_runtime;
        END IF;
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oisms_app') THEN
            REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON public.supplier_invoices, public.supplier_invoice_items, public.purchase_variances FROM oisms_app;
            GRANT SELECT ON public.supplier_invoices, public.supplier_invoice_items, public.purchase_variances TO oisms_app;
        END IF;
        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oisms_test_app') THEN
            REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON public.supplier_invoices, public.supplier_invoice_items, public.purchase_variances FROM oisms_test_app;
            GRANT SELECT ON public.supplier_invoices, public.supplier_invoice_items, public.purchase_variances TO oisms_test_app;
        END IF;
    END IF;
END $$;

COMMIT;
