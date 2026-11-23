-- Migration: U12 Gói 4 Cấu hình giá bán trên đơn mua, sinh giá khi nhập kho, lý do đổi giá và khóa giá
-- Idempotent: có thể chạy lặp lại nhiều lần không gây lỗi.

BEGIN;

SELECT pg_advisory_xact_lock(hashtext('oisms_po_sale_price_configs_upgrade'));

-- 1. Bổ sung reason_type trên bảng price_change_requests
ALTER TABLE public.price_change_requests ADD COLUMN IF NOT EXISTS reason_type varchar(30) NULL;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_trigger
        WHERE tgname = 'price_change_request_guard'
          AND tgrelid = 'public.price_change_requests'::regclass
    ) THEN
        ALTER TABLE public.price_change_requests DISABLE TRIGGER price_change_request_guard;
        UPDATE public.price_change_requests SET reason_type = 'OTHER' WHERE reason_type IS NULL;
        ALTER TABLE public.price_change_requests ENABLE TRIGGER price_change_request_guard;
    ELSE
        UPDATE public.price_change_requests SET reason_type = 'OTHER' WHERE reason_type IS NULL;
    END IF;
END $$;

ALTER TABLE public.price_change_requests ALTER COLUMN reason_type SET DEFAULT 'OTHER';
ALTER TABLE public.price_change_requests ALTER COLUMN reason_type SET NOT NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_price_change_requests_reason_type') THEN
        ALTER TABLE public.price_change_requests ADD CONSTRAINT ck_price_change_requests_reason_type
            CHECK (reason_type IN ('SHORTAGE', 'PROMOTION', 'FROM_PURCHASE_ORDER', 'OTHER'));
    END IF;
END $$;

-- Cập nhật hàm oisms_price_change_request_guard để bảo vệ tính bất biến của reason_type
CREATE OR REPLACE FUNCTION public.oisms_price_change_request_guard() RETURNS trigger
LANGUAGE plpgsql SET search_path = pg_catalog, public AS $$
BEGIN
  IF OLD.status <> 'Pending' OR NEW.status NOT IN ('Approved', 'Rejected') THEN
    RAISE EXCEPTION 'Price change request can only transition from Pending to Approved or Rejected' USING ERRCODE = '23514';
  END IF;

  IF (NEW.tenant_id, NEW.id, NEW.request_key, NEW.kind, NEW.reason, NEW.reason_type, NEW.requested_valid_from, NEW.created_by, NEW.created_at)
     IS DISTINCT FROM
     (OLD.tenant_id, OLD.id, OLD.request_key, OLD.kind, OLD.reason, OLD.reason_type, OLD.requested_valid_from, OLD.created_by, OLD.created_at) THEN
    RAISE EXCEPTION 'Price change request attributes other than review fields are immutable' USING ERRCODE = '23514';
  END IF;

  RETURN NEW;
END $$;

-- 2. Tạo bảng purchase_order_sale_price_configs (bảng thứ 51)
CREATE TABLE IF NOT EXISTS public.purchase_order_sale_price_configs (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    purchase_order_item_id uuid NOT NULL,
    unit_id uuid NOT NULL,
    price_list_id uuid NOT NULL,
    mode varchar(20) DEFAULT 'KEEP' NOT NULL,
    margin_percent numeric(7,4) NULL,
    fixed_price numeric(20,6) NULL,
    price_source varchar(20) NULL,
    apply_mode varchar(20) DEFAULT 'ON_RECEIPT' NOT NULL,
    apply_from timestamptz NULL,
    applied_at timestamptz NULL,
    price_change_request_id uuid NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    updated_at timestamptz NULL
);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_po_sale_price_configs_1') THEN
        ALTER TABLE public.purchase_order_sale_price_configs ADD CONSTRAINT uq_po_sale_price_configs_1
            UNIQUE (tenant_id, id);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_po_sale_price_configs_item_unit_pricelist') THEN
        ALTER TABLE public.purchase_order_sale_price_configs ADD CONSTRAINT uq_po_sale_price_configs_item_unit_pricelist
            UNIQUE (tenant_id, purchase_order_item_id, unit_id, price_list_id);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_po_sale_price_configs_mode') THEN
        ALTER TABLE public.purchase_order_sale_price_configs ADD CONSTRAINT ck_po_sale_price_configs_mode
            CHECK (mode IN ('KEEP', 'WAC_MARGIN', 'FIXED'));
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_po_sale_price_configs_apply_mode') THEN
        ALTER TABLE public.purchase_order_sale_price_configs ADD CONSTRAINT ck_po_sale_price_configs_apply_mode
            CHECK (apply_mode IN ('ON_RECEIPT', 'FROM_DATE'));
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_po_sale_price_configs_price_source') THEN
        ALTER TABLE public.purchase_order_sale_price_configs ADD CONSTRAINT ck_po_sale_price_configs_price_source
            CHECK (price_source IS NULL OR price_source IN ('SUPPLIER', 'OWNER'));
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_po_sale_price_configs_mode_rules') THEN
        ALTER TABLE public.purchase_order_sale_price_configs ADD CONSTRAINT ck_po_sale_price_configs_mode_rules
            CHECK (
                (mode = 'KEEP' AND margin_percent IS NULL AND fixed_price IS NULL AND price_source IS NULL) OR
                (mode = 'WAC_MARGIN' AND margin_percent IS NOT NULL AND margin_percent < 100 AND fixed_price IS NULL AND price_source IS NULL) OR
                (mode = 'FIXED' AND fixed_price IS NOT NULL AND fixed_price >= 0 AND price_source IS NOT NULL AND margin_percent IS NULL)
            );
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_po_sale_price_configs_apply_rules') THEN
        ALTER TABLE public.purchase_order_sale_price_configs ADD CONSTRAINT ck_po_sale_price_configs_apply_rules
            CHECK (
                (apply_mode = 'ON_RECEIPT' AND apply_from IS NULL) OR
                (apply_mode = 'FROM_DATE' AND apply_from IS NOT NULL)
            );
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_po_sale_price_configs_applied') THEN
        ALTER TABLE public.purchase_order_sale_price_configs ADD CONSTRAINT ck_po_sale_price_configs_applied
            CHECK (
                (applied_at IS NULL AND price_change_request_id IS NULL) OR
                (applied_at IS NOT NULL AND price_change_request_id IS NOT NULL)
            );
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_po_sale_price_configs_tenant_id') THEN
        ALTER TABLE public.purchase_order_sale_price_configs ADD CONSTRAINT fk_po_sale_price_configs_tenant_id
            FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE CASCADE ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_po_sale_price_configs_item') THEN
        ALTER TABLE public.purchase_order_sale_price_configs ADD CONSTRAINT fk_po_sale_price_configs_item
            FOREIGN KEY (tenant_id, purchase_order_item_id) REFERENCES public.purchase_order_items (tenant_id, id) ON DELETE CASCADE ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_po_sale_price_configs_unit') THEN
        ALTER TABLE public.purchase_order_sale_price_configs ADD CONSTRAINT fk_po_sale_price_configs_unit
            FOREIGN KEY (tenant_id, unit_id) REFERENCES public.units (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_po_sale_price_configs_price_list') THEN
        ALTER TABLE public.purchase_order_sale_price_configs ADD CONSTRAINT fk_po_sale_price_configs_price_list
            FOREIGN KEY (tenant_id, price_list_id) REFERENCES public.price_lists (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_po_sale_price_configs_pcr') THEN
        ALTER TABLE public.purchase_order_sale_price_configs ADD CONSTRAINT fk_po_sale_price_configs_pcr
            FOREIGN KEY (tenant_id, price_change_request_id) REFERENCES public.price_change_requests (tenant_id, id) ON DELETE SET NULL ON UPDATE NO ACTION;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_po_sale_price_configs_tenant_item
    ON public.purchase_order_sale_price_configs (tenant_id, purchase_order_item_id);
CREATE INDEX IF NOT EXISTS idx_po_sale_price_configs_tenant_unit
    ON public.purchase_order_sale_price_configs (tenant_id, unit_id);
CREATE INDEX IF NOT EXISTS idx_po_sale_price_configs_tenant_price_list
    ON public.purchase_order_sale_price_configs (tenant_id, price_list_id);
CREATE INDEX IF NOT EXISTS idx_po_sale_price_configs_tenant_pcr
    ON public.purchase_order_sale_price_configs (tenant_id, price_change_request_id);

-- 3. Cấp quyền truy cập cho runtime role
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oisms_runtime') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON public.purchase_order_sale_price_configs TO oisms_runtime;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oisms_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON public.purchase_order_sale_price_configs TO oisms_app;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oisms_test_app') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON public.purchase_order_sale_price_configs TO oisms_test_app;
    END IF;
END $$;

COMMIT;
