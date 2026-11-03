-- OISMS Upgrade 04 - Bảng giá theo thời gian (Price Lists by Time) incremental upgrade.
-- Idempotent migration script for PostgreSQL schema.
-- Run with psql as the migration/admin user.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL search_path = public, pg_catalog;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '5min';
SELECT pg_advisory_xact_lock(hashtext('oisms_price_list_schema_upgrade'));

-- =========================================================================
-- 1. Kiểm tra trước: có SKU base_unit_id IS NULL, hoặc thiếu dòng sku_units
--    của đơn vị gốc, thì RAISE EXCEPTION kèm danh sách. Không tự vá.
-- =========================================================================
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

-- =========================================================================
-- 2. Ghi nhớ to_regclass('public.price_change_requests') IS NULL vào cờ
--    trước khi tạo bảng.
-- =========================================================================
DO $$
BEGIN
    IF to_regclass('public.price_change_requests') IS NULL THEN
        PERFORM set_config('oisms.run_price_backfill', 'true', true);
    ELSE
        PERFORM set_config('oisms.run_price_backfill', 'false', true);
    END IF;
END $$;

-- =========================================================================
-- 3. Tạo 4 bảng mới, thêm cột bảng cũ, constraint, index, function, trigger
-- =========================================================================

-- 3.1. Bảng price_lists
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

-- 3.2. Bảng price_change_requests
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

-- 3.3. Bảng price_change_request_items
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

-- 3.4. Bảng sku_unit_prices
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

-- 3.5. Cột mới trên order_items
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

-- 3.6. Functions và Triggers
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

-- 3.7. Quyền cho oisms_runtime
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
        REVOKE UPDATE, DELETE, TRUNCATE ON TABLE public.price_change_request_items FROM oisms_runtime;

        REVOKE ALL ON TABLE public.sku_unit_prices FROM oisms_runtime;
        GRANT SELECT, INSERT, UPDATE ON TABLE public.sku_unit_prices TO oisms_runtime;
        REVOKE DELETE, TRUNCATE ON TABLE public.sku_unit_prices FROM oisms_runtime;
    END IF;
END $$;

-- =========================================================================
-- 4. Backfill (chỉ chạy khi cờ oisms.run_price_backfill = 'true')
-- =========================================================================
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
        -- Tạo 2 price_lists
        v_retail_id := gen_random_uuid();
        v_wholesale_id := gen_random_uuid();

        INSERT INTO public.price_lists (id, tenant_id, code, name, is_active, created_at)
        VALUES
            (v_retail_id, v_tenant.id, 'retail', 'Giá lẻ', true, v_now),
            (v_wholesale_id, v_tenant.id, 'wholesale', 'Giá sỉ', true, v_now)
        ON CONFLICT (tenant_id, code) DO NOTHING;

        SELECT id INTO v_retail_id FROM public.price_lists WHERE tenant_id = v_tenant.id AND code = 'retail';
        SELECT id INTO v_wholesale_id FROM public.price_lists WHERE tenant_id = v_tenant.id AND code = 'wholesale';

        -- Tạo phiếu Opening
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

        -- In danh sách SKU có giá lẻ cũ = 0 bằng RAISE NOTICE để rà soát
        FOR v_zero_sku IN
            SELECT id, code FROM public.skus WHERE tenant_id = v_tenant.id AND retail_price = 0
        LOOP
            RAISE NOTICE 'SKU % (%) có giá lẻ cũ = 0', v_zero_sku.id, v_zero_sku.code;
        END LOOP;

        -- Dòng 1: retail_price -> giá lẻ đơn vị gốc
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

        -- Dòng 2: wholesale_price > 0 -> giá sỉ đơn vị gốc
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

        -- Dòng 3: sale_price IS NOT NULL của đơn vị khác gốc -> giá lẻ đơn vị đó
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

COMMIT;
