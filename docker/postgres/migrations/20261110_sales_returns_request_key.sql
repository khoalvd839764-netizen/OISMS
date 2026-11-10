-- OISMS Upgrade CS-01 - Trả hàng từ khách (Sales Returns Request Key, Total Refund Amount & Note)
-- Idempotent migration script for PostgreSQL schema.
-- Run with psql as the migration/admin user.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL search_path = public, pg_catalog;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '5min';
SELECT pg_advisory_xact_lock(hashtext('oisms_sales_returns_schema_upgrade'));

-- =========================================================================
-- 1. Thêm request_key, total_refund_amount, note vào bảng sales_returns
-- =========================================================================
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'sales_returns' AND column_name = 'request_key'
    ) THEN
        ALTER TABLE public.sales_returns ADD COLUMN request_key uuid NULL;
        UPDATE public.sales_returns SET request_key = gen_random_uuid() WHERE request_key IS NULL;
        ALTER TABLE public.sales_returns ALTER COLUMN request_key SET NOT NULL;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'sales_returns' AND column_name = 'total_refund_amount'
    ) THEN
        ALTER TABLE public.sales_returns ADD COLUMN total_refund_amount numeric(20,2) NOT NULL DEFAULT 0;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'sales_returns' AND column_name = 'note'
    ) THEN
        ALTER TABLE public.sales_returns ADD COLUMN note text NULL;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_sales_returns_4') THEN
        ALTER TABLE public.sales_returns ADD CONSTRAINT uq_sales_returns_4 UNIQUE (tenant_id, request_key);
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_sales_returns_total_refund_amount_nonnegative') THEN
        ALTER TABLE public.sales_returns ADD CONSTRAINT ck_sales_returns_total_refund_amount_nonnegative CHECK (total_refund_amount >= 0);
    END IF;
END $$;

COMMIT;
