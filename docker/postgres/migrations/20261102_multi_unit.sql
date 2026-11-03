-- OISMS Sprint 3 -> Sprint 4 (Upgrade 05 - Multi-unit Support) incremental upgrade.
-- Idempotent migration script for PostgreSQL schema.
-- Run with psql as the migration/admin user.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL search_path = public, pg_catalog;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '5min';
SELECT pg_advisory_xact_lock(hashtext('oisms_multi_unit_schema_upgrade'));

-- =========================================================================
-- 1. Tạo 3 bảng mới và các cột mới, constraint trên bảng mới
-- =========================================================================

-- 1.1. Bảng units
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

-- 1.2. Bảng sku_units
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

-- 1.3. Bảng sku_unit_factor_history
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

-- 1.4. Thêm cột vào bảng cũ
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

-- =========================================================================
-- 2. Chèn bộ đơn vị mặc định cho mọi tenant và mỗi giá trị lower(btrim(skus.unit))
-- =========================================================================

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

-- =========================================================================
-- 3. Gán skus.base_unit_id; chèn dòng sku_units gốc (hệ số 1, can_purchase, can_sell, default cả 2)
-- =========================================================================

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

-- =========================================================================
-- 4. Backfill unit_id, unit_name_snapshot cho order_items và goods_receipt_items
--    Tuyệt đối không UPDATE inventory_transactions
-- =========================================================================

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

-- =========================================================================
-- 5. SET CONSTRAINTS ALL IMMEDIATE trước mọi ALTER tiếp theo trên order_items
-- =========================================================================
SET CONSTRAINTS ALL IMMEDIATE;

-- =========================================================================
-- 6. Thêm FK trên bảng cũ, đổi uq_order_items_2
-- =========================================================================

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

-- =========================================================================
-- 7. CREATE OR REPLACE hai function, tạo trigger append-only, cấp quyền oisms_runtime
-- =========================================================================

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

COMMIT;
