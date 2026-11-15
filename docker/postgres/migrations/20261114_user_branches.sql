-- CS-02: Phân công chi nhánh cho nhân viên (user_branches). Run as schema owner with psql.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL search_path = public, pg_catalog;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '5min';
SELECT pg_advisory_xact_lock(hashtext('oisms_user_branches_schema_upgrade'));

CREATE TABLE IF NOT EXISTS public.user_branches (
    tenant_id uuid NOT NULL,
    user_id uuid NOT NULL,
    branch_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_user_branches PRIMARY KEY (tenant_id, user_id, branch_id),
    CONSTRAINT fk_user_branches_tenant FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE RESTRICT,
    CONSTRAINT fk_user_branches_user FOREIGN KEY (tenant_id, user_id) REFERENCES public.users (tenant_id, id) ON DELETE CASCADE,
    CONSTRAINT fk_user_branches_branch FOREIGN KEY (tenant_id, branch_id) REFERENCES public.branches (tenant_id, id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS ix_user_branches_branch
    ON public.user_branches (tenant_id, branch_id);

REVOKE ALL ON public.user_branches FROM PUBLIC, oisms_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON public.user_branches TO oisms_runtime;

COMMIT;
