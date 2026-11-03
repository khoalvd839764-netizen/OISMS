-- OISMS Upgrade 03 - Lịch kiểm kê và duyệt chênh lệch (Stock Count Schedules, Periods & Approvals).
-- Idempotent migration script for PostgreSQL schema.
-- Run with psql as the migration/admin user.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL search_path = public, pg_catalog;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '5min';
SELECT pg_advisory_xact_lock(hashtext('oisms_stock_count_schedule_schema_upgrade'));

-- =========================================================================
-- 1. Bảng stock_count_schedules (Bảng thứ 41)
-- =========================================================================
CREATE TABLE IF NOT EXISTS public.stock_count_schedules (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    warehouse_id uuid NOT NULL,
    period_type varchar(20) NOT NULL,
    day_of_month smallint NULL,
    local_time time NOT NULL,
    due_days smallint NOT NULL,
    assignee_user_id uuid NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_by uuid NOT NULL,
    updated_by uuid NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    updated_at timestamptz NULL,
    CONSTRAINT uq_stock_count_schedules_1 UNIQUE (tenant_id, id),
    CONSTRAINT uq_stock_count_schedules_2 UNIQUE (tenant_id, warehouse_id, period_type),
    CONSTRAINT ck_stock_count_schedules_period_type CHECK (period_type IN ('MidMonth', 'EndOfMonth')),
    CONSTRAINT ck_stock_count_schedules_day CHECK ((period_type = 'MidMonth' AND day_of_month BETWEEN 1 AND 28) OR (period_type = 'EndOfMonth' AND day_of_month IS NULL)),
    CONSTRAINT ck_stock_count_schedules_due_days CHECK (due_days BETWEEN 1 AND 14)
);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_count_schedules_tenant_id') THEN
        ALTER TABLE public.stock_count_schedules ADD CONSTRAINT fk_stock_count_schedules_tenant_id
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_count_schedules_warehouse_branch') THEN
        ALTER TABLE public.stock_count_schedules ADD CONSTRAINT fk_stock_count_schedules_warehouse_branch
        FOREIGN KEY (tenant_id, warehouse_id, branch_id) REFERENCES public.warehouses (tenant_id, id, branch_id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_count_schedules_assignee') THEN
        ALTER TABLE public.stock_count_schedules ADD CONSTRAINT fk_stock_count_schedules_assignee
        FOREIGN KEY (tenant_id, assignee_user_id) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_count_schedules_created_by') THEN
        ALTER TABLE public.stock_count_schedules ADD CONSTRAINT fk_stock_count_schedules_created_by
        FOREIGN KEY (tenant_id, created_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_count_schedules_updated_by') THEN
        ALTER TABLE public.stock_count_schedules ADD CONSTRAINT fk_stock_count_schedules_updated_by
        FOREIGN KEY (tenant_id, updated_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
END $$;

-- =========================================================================
-- 2. Bảng stock_count_periods (Bảng thứ 42)
-- =========================================================================
CREATE TABLE IF NOT EXISTS public.stock_count_periods (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    schedule_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    warehouse_id uuid NOT NULL,
    period_type varchar(20) NOT NULL,
    period_key varchar(20) NOT NULL,
    scheduled_at timestamptz NOT NULL,
    due_at timestamptz NOT NULL,
    assignee_user_id uuid NOT NULL,
    status varchar(20) DEFAULT 'Planned' NOT NULL,
    completed_at timestamptz NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    updated_at timestamptz NULL,
    CONSTRAINT uq_stock_count_periods_1 UNIQUE (tenant_id, id),
    CONSTRAINT uq_stock_count_periods_2 UNIQUE (tenant_id, warehouse_id, period_key),
    CONSTRAINT ck_stock_count_periods_period_type CHECK (period_type IN ('MidMonth', 'EndOfMonth')),
    CONSTRAINT ck_stock_count_periods_status CHECK (status IN ('Planned', 'InProgress', 'Completed', 'Cancelled')),
    CONSTRAINT ck_stock_count_periods_due CHECK (due_at > scheduled_at),
    CONSTRAINT ck_stock_count_periods_completed CHECK ((status = 'Completed') = (completed_at IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS ix_stock_count_periods_status_due
ON public.stock_count_periods (tenant_id, status, due_at, id);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_count_periods_tenant_id') THEN
        ALTER TABLE public.stock_count_periods ADD CONSTRAINT fk_stock_count_periods_tenant_id
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_count_periods_schedule') THEN
        ALTER TABLE public.stock_count_periods ADD CONSTRAINT fk_stock_count_periods_schedule
        FOREIGN KEY (tenant_id, schedule_id) REFERENCES public.stock_count_schedules (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_count_periods_warehouse_branch') THEN
        ALTER TABLE public.stock_count_periods ADD CONSTRAINT fk_stock_count_periods_warehouse_branch
        FOREIGN KEY (tenant_id, warehouse_id, branch_id) REFERENCES public.warehouses (tenant_id, id, branch_id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_count_periods_assignee') THEN
        ALTER TABLE public.stock_count_periods ADD CONSTRAINT fk_stock_count_periods_assignee
        FOREIGN KEY (tenant_id, assignee_user_id) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
END $$;

-- =========================================================================
-- 3. Bảng stock_count_approvals (Bảng thứ 43 - Append-only)
-- =========================================================================
CREATE TABLE IF NOT EXISTS public.stock_count_approvals (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    stock_count_id uuid NOT NULL,
    action varchar(20) NOT NULL,
    actor_user_id uuid NOT NULL,
    reason text NULL,
    differences jsonb NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    updated_at timestamptz NULL,
    CONSTRAINT uq_stock_count_approvals_1 UNIQUE (tenant_id, id),
    CONSTRAINT ck_stock_count_approvals_action CHECK (action IN ('Submitted', 'Approved', 'RecountRequested', 'Recounted', 'Cancelled'))
);

CREATE INDEX IF NOT EXISTS ix_stock_count_approvals_count
ON public.stock_count_approvals (tenant_id, stock_count_id, created_at);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_count_approvals_tenant_id') THEN
        ALTER TABLE public.stock_count_approvals ADD CONSTRAINT fk_stock_count_approvals_tenant_id
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_count_approvals_stock_count') THEN
        ALTER TABLE public.stock_count_approvals ADD CONSTRAINT fk_stock_count_approvals_stock_count
        FOREIGN KEY (tenant_id, stock_count_id) REFERENCES public.stock_counts (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_count_approvals_actor') THEN
        ALTER TABLE public.stock_count_approvals ADD CONSTRAINT fk_stock_count_approvals_actor
        FOREIGN KEY (tenant_id, actor_user_id) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger tg
        JOIN pg_class c ON tg.tgrelid = c.oid
        WHERE c.relname = 'stock_count_approvals' AND tg.tgname = 'stock_count_approval_append_only'
    ) THEN
        CREATE TRIGGER stock_count_approval_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON public.stock_count_approvals
        FOR EACH STATEMENT EXECUTE FUNCTION public.oisms_deny_mutation();
    END IF;
END $$;

-- =========================================================================
-- 4. Thêm cột, constraint, index cho stock_counts và stock_count_items
-- =========================================================================
ALTER TABLE public.stock_counts ADD COLUMN IF NOT EXISTS count_type varchar(20) NOT NULL DEFAULT 'AdHoc';
ALTER TABLE public.stock_counts ADD COLUMN IF NOT EXISTS ad_hoc_reason text NULL;
ALTER TABLE public.stock_counts ADD COLUMN IF NOT EXISTS period_id uuid NULL;
ALTER TABLE public.stock_counts ADD COLUMN IF NOT EXISTS submitted_by uuid NULL;
ALTER TABLE public.stock_counts ADD COLUMN IF NOT EXISTS submitted_at timestamptz NULL;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM pg_constraint 
        WHERE conname = 'ck_stock_counts_status_values' 
          AND pg_get_constraintdef(oid) NOT LIKE '%PendingApproval%'
    ) THEN
        ALTER TABLE public.stock_counts DROP CONSTRAINT ck_stock_counts_status_values;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'ck_stock_counts_status_values'
    ) THEN
        ALTER TABLE public.stock_counts ADD CONSTRAINT ck_stock_counts_status_values 
        CHECK (status IN ('Draft', 'Counting', 'PendingApproval', 'Confirmed', 'Cancelled'));
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_stock_counts_count_type') THEN
        ALTER TABLE public.stock_counts ADD CONSTRAINT ck_stock_counts_count_type
        CHECK (count_type IN ('MidMonth', 'EndOfMonth', 'AdHoc'));
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_stock_counts_type_period') THEN
        ALTER TABLE public.stock_counts ADD CONSTRAINT ck_stock_counts_type_period
        CHECK ((count_type = 'AdHoc') = (period_id IS NULL));
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_counts_period') THEN
        ALTER TABLE public.stock_counts ADD CONSTRAINT fk_stock_counts_period
        FOREIGN KEY (tenant_id, period_id) REFERENCES public.stock_count_periods (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_counts_submitted_by') THEN
        ALTER TABLE public.stock_counts ADD CONSTRAINT fk_stock_counts_submitted_by
        FOREIGN KEY (tenant_id, submitted_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_stock_counts_active_period 
ON public.stock_counts (tenant_id, period_id) 
WHERE period_id IS NOT NULL AND status <> 'Cancelled';

ALTER TABLE public.stock_count_items ADD COLUMN IF NOT EXISTS counted_by uuid NULL;
ALTER TABLE public.stock_count_items ADD COLUMN IF NOT EXISTS counted_at timestamptz NULL;
ALTER TABLE public.stock_count_items ADD COLUMN IF NOT EXISTS snapshot_at timestamptz NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_count_items_counted_by') THEN
        ALTER TABLE public.stock_count_items ADD CONSTRAINT fk_stock_count_items_counted_by
        FOREIGN KEY (tenant_id, counted_by) REFERENCES public.users (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION;
    END IF;
END $$;

UPDATE public.stock_count_items i
SET snapshot_at = c.created_at
FROM public.stock_counts c
WHERE i.tenant_id = c.tenant_id AND i.stock_count_id = c.id AND i.snapshot_at IS NULL;

-- =========================================================================
-- 5. Quyền cho oisms_runtime
-- =========================================================================
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oisms_runtime') THEN
        REVOKE ALL ON TABLE public.stock_count_schedules FROM oisms_runtime;
        GRANT SELECT, INSERT, UPDATE ON TABLE public.stock_count_schedules TO oisms_runtime;
        REVOKE DELETE, TRUNCATE ON TABLE public.stock_count_schedules FROM oisms_runtime;

        REVOKE ALL ON TABLE public.stock_count_periods FROM oisms_runtime;
        GRANT SELECT, INSERT, UPDATE ON TABLE public.stock_count_periods TO oisms_runtime;
        REVOKE DELETE, TRUNCATE ON TABLE public.stock_count_periods FROM oisms_runtime;

        REVOKE ALL ON TABLE public.stock_count_approvals FROM oisms_runtime;
        GRANT SELECT, INSERT ON TABLE public.stock_count_approvals TO oisms_runtime;
        REVOKE UPDATE, DELETE, TRUNCATE ON TABLE public.stock_count_approvals FROM oisms_runtime;
    END IF;
END $$;

COMMIT;
