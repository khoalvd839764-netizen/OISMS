-- OISMS Sprint 2 -> Sprint 3 incremental upgrade.
-- Exported from DatabaseMigrationService.UpgradeDatabaseSchemaAsync at bfc83c0.
-- Existing schema v0.5 only. Run with psql as the migration/admin user.
-- No rows are deleted and no existing table is recreated.
-- Existing incompatible data/constraints cause failure; do not ignore errors.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL search_path = public, pg_catalog;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '5min';
SELECT pg_advisory_xact_lock(hashtext('oisms_sprint3_schema_upgrade'));

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.tables 
        WHERE table_schema = 'public' AND table_name = 'shop_integrations'
    ) THEN
        IF EXISTS (
            SELECT secret_hash FROM shop_integrations GROUP BY secret_hash HAVING count(*) > 1
        ) THEN
            RAISE EXCEPTION 'Phát hiện trùng lặp secret_hash trong shop_integrations. Yêu cầu xử lý thủ công trước khi áp dụng ràng buộc duy nhất uq_shop_integrations_secret_hash.';
        END IF;
    END IF;
END $$;

ALTER TABLE stock_transfers ADD COLUMN IF NOT EXISTS request_key uuid;
ALTER TABLE stock_transfers ADD COLUMN IF NOT EXISTS payload_signature varchar(128);
ALTER TABLE stock_counts ADD COLUMN IF NOT EXISTS request_key uuid;
ALTER TABLE stock_counts ADD COLUMN IF NOT EXISTS payload_signature varchar(128);
ALTER TABLE stock_counts ADD COLUMN IF NOT EXISTS generated_adjustment_id uuid;
ALTER TABLE orders ADD COLUMN IF NOT EXISTS webhook_payload_signature varchar(64);
CREATE TABLE IF NOT EXISTS shop_integrations (
    id uuid DEFAULT gen_random_uuid() NOT NULL PRIMARY KEY,
    tenant_id uuid NOT NULL,
    channel varchar(20) NOT NULL,
    external_shop_id varchar(100) NOT NULL,
    branch_id uuid NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    secret_hash varchar(255) NOT NULL,
    created_at timestamptz DEFAULT now() NOT NULL,
    updated_at timestamptz,
    CONSTRAINT uq_shop_integrations_1 UNIQUE (tenant_id, id),
    CONSTRAINT uq_shop_integrations_2 UNIQUE (tenant_id, channel, external_shop_id),
    CONSTRAINT uq_shop_integrations_secret_hash UNIQUE (secret_hash),
    CONSTRAINT ck_shop_integrations_channel CHECK (channel IN ('Shopee', 'TikTok', 'Lazada'))
);
DO $$ 
BEGIN 
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'uq_stock_transfers_3'
    ) THEN 
        ALTER TABLE stock_transfers ADD CONSTRAINT uq_stock_transfers_3 UNIQUE (tenant_id, request_key); 
    END IF; 
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'uq_stock_counts_3'
    ) THEN 
        ALTER TABLE stock_counts ADD CONSTRAINT uq_stock_counts_3 UNIQUE (tenant_id, request_key); 
    END IF; 
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'uq_stock_counts_4'
    ) THEN 
        ALTER TABLE stock_counts ADD CONSTRAINT uq_stock_counts_4 UNIQUE (tenant_id, generated_adjustment_id); 
    END IF; 
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'uq_inventory_adjustments_3'
    ) THEN 
        ALTER TABLE inventory_adjustments ADD CONSTRAINT uq_inventory_adjustments_3 UNIQUE (tenant_id, id, branch_id, warehouse_id); 
    END IF; 
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_stock_counts_generated_adjustment'
    ) THEN 
        ALTER TABLE stock_counts ADD CONSTRAINT fk_stock_counts_generated_adjustment 
        FOREIGN KEY (tenant_id, generated_adjustment_id, branch_id, warehouse_id) 
        REFERENCES inventory_adjustments (tenant_id, id, branch_id, warehouse_id) 
        ON DELETE RESTRICT ON UPDATE NO ACTION; 
    END IF; 
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_shop_integrations_tenant'
    ) THEN 
        ALTER TABLE shop_integrations ADD CONSTRAINT fk_shop_integrations_tenant 
        FOREIGN KEY (tenant_id) REFERENCES tenants (id) ON DELETE RESTRICT ON UPDATE NO ACTION; 
    END IF; 
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_shop_integrations_branch'
    ) THEN 
        ALTER TABLE shop_integrations ADD CONSTRAINT fk_shop_integrations_branch 
        FOREIGN KEY (tenant_id, branch_id) REFERENCES branches (tenant_id, id) ON DELETE RESTRICT ON UPDATE NO ACTION; 
    END IF; 
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'uq_shop_integrations_secret_hash'
    ) THEN 
        ALTER TABLE shop_integrations ADD CONSTRAINT uq_shop_integrations_secret_hash UNIQUE (secret_hash); 
    END IF; 
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oisms_runtime') THEN
        REVOKE ALL ON TABLE shop_integrations FROM oisms_runtime;
        GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE shop_integrations TO oisms_runtime;
    END IF; 
END $$;

COMMIT;
