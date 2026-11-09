-- OISMS FR-10 - Schema riêng cho storage Hangfire (job hết hạn giữ chỗ, sinh kỳ kiểm kê).
-- Idempotent migration script for PostgreSQL schema.
-- Run with psql as the migration/admin user.
-- Không tạo bảng nghiệp vụ: số bảng trong schema public vẫn là 44.
-- Hangfire tự tạo bảng trong schema 'hangfire' khi backend chạy với Hangfire:Enabled=true;
-- các bảng đó thuộc tài khoản runtime, tách khỏi schema public.
\set ON_ERROR_STOP on
BEGIN;
SET LOCAL search_path = public, pg_catalog;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '5min';
SELECT pg_advisory_xact_lock(hashtext('oisms_hangfire_schema_upgrade'));

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'oisms_runtime') THEN
        RAISE EXCEPTION 'Role oisms_runtime chưa tồn tại. Chạy init-oisms.sh hoặc oisms_schema.sql trước.';
    END IF;
END $$;

CREATE SCHEMA IF NOT EXISTS hangfire;

-- Runtime (oisms_app là thành viên oisms_runtime) được dùng và tạo bảng trong schema hangfire,
-- không có quyền CREATE trên database hay schema public.
GRANT USAGE, CREATE ON SCHEMA hangfire TO oisms_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA hangfire TO oisms_runtime;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA hangfire TO oisms_runtime;

COMMIT;
