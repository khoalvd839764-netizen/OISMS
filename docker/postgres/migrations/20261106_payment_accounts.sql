-- OISMS Upgrade CS-05 - Quản lý tài khoản ngân hàng thụ hưởng và thanh toán VietQR (Payment Accounts & VietQR).
-- Idempotent migration script for PostgreSQL schema.
-- Run with psql as the migration/admin user.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL search_path = public, pg_catalog;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '5min';
SELECT pg_advisory_xact_lock(hashtext('oisms_payment_accounts_schema_upgrade'));

-- =========================================================================
-- 1. Bảng payment_accounts (Bảng thứ 44 - Cấu hình tài khoản nhận tiền theo tenant)
-- =========================================================================
CREATE TABLE IF NOT EXISTS public.payment_accounts (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    bank_bin varchar(6) NOT NULL,
    bank_name varchar(255) NOT NULL,
    bank_short_name varchar(50) NOT NULL,
    account_number varchar(20) NOT NULL,
    account_name varchar(255) NOT NULL,
    is_default boolean NOT NULL DEFAULT false,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NULL,
    updated_by_user_id uuid NULL,
    CONSTRAINT uq_payment_accounts_1 UNIQUE (tenant_id, id),
    CONSTRAINT uq_payment_accounts_2 UNIQUE (tenant_id, bank_bin, account_number),
    CONSTRAINT ck_payment_accounts_bank_bin CHECK (bank_bin ~ '^[0-9]{6}$'),
    CONSTRAINT ck_payment_accounts_account_number CHECK (account_number ~ '^[A-Za-z0-9]{6,19}$')
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_payment_accounts_tenant_default_active
ON public.payment_accounts (tenant_id)
WHERE is_default AND is_active;

CREATE INDEX IF NOT EXISTS ix_payment_accounts_tenant_active
ON public.payment_accounts (tenant_id, is_active);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_payment_accounts_tenant_id') THEN
        ALTER TABLE public.payment_accounts ADD CONSTRAINT fk_payment_accounts_tenant_id
        FOREIGN KEY (tenant_id) REFERENCES public.tenants (id) ON DELETE CASCADE ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_payment_accounts_updated_by') THEN
        ALTER TABLE public.payment_accounts ADD CONSTRAINT fk_payment_accounts_updated_by
        FOREIGN KEY (tenant_id, updated_by_user_id) REFERENCES public.users (tenant_id, id) ON DELETE SET NULL ON UPDATE NO ACTION;
    END IF;
END $$;

-- =========================================================================
-- 2. Thêm các cột snapshot tài khoản nhận và nội dung CK vào bảng payments
-- =========================================================================
ALTER TABLE public.payments ADD COLUMN IF NOT EXISTS transfer_reference varchar(100) NULL;
ALTER TABLE public.payments ADD COLUMN IF NOT EXISTS receiver_bank_bin varchar(6) NULL;
ALTER TABLE public.payments ADD COLUMN IF NOT EXISTS receiver_account_number varchar(20) NULL;
ALTER TABLE public.payments ADD COLUMN IF NOT EXISTS receiver_account_name varchar(255) NULL;
ALTER TABLE public.payments ADD COLUMN IF NOT EXISTS payment_account_id uuid NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_payments_payment_account_id') THEN
        ALTER TABLE public.payments ADD CONSTRAINT fk_payments_payment_account_id
        FOREIGN KEY (tenant_id, payment_account_id) REFERENCES public.payment_accounts (tenant_id, id) ON DELETE SET NULL ON UPDATE NO ACTION;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_payments_bank_transfer_snapshot') THEN
        ALTER TABLE public.payments ADD CONSTRAINT ck_payments_bank_transfer_snapshot
        CHECK (method = 'BankTransfer' OR (
            transfer_reference IS NULL AND
            receiver_bank_bin IS NULL AND
            receiver_account_number IS NULL AND
            receiver_account_name IS NULL AND
            payment_account_id IS NULL
        ));
    END IF;
END $$;

-- =========================================================================
-- 3. Phân quyền truy cập runtime role
-- =========================================================================
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oisms_runtime') THEN
        GRANT SELECT, INSERT, UPDATE, DELETE ON public.payment_accounts TO oisms_runtime;
    END IF;
END $$;

COMMIT;
