-- =====================================================================
-- OISMS - HANGFIRE SCHEMA PROVISIONING SCRIPT
-- Sprint 3 / BE1 Task 6: S3-BE1-T06-RESERVATION-EXPIRY
-- 
-- Chức năng: Khởi tạo schema riêng biệt 'hangfire' và cấp quyền tối thiểu
-- cho runtime role để lưu trữ dữ liệu hàng đợi Hangfire.
-- Đảm bảo hoàn toàn tách biệt khỏi schema 'public' (30 bảng nghiệp vụ OISMS).
-- =====================================================================

-- 1. Tạo schema 'hangfire' nếu chưa tồn tại
CREATE SCHEMA IF NOT EXISTS hangfire;

-- 2. Cấp quyền sử dụng và tạo bảng cho role nghiệp vụ (oisms_runtime, oisms_app, oisms_test_app)
DO $$
BEGIN
    IF EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'oisms_runtime') THEN
        EXECUTE 'GRANT USAGE, CREATE ON SCHEMA hangfire TO oisms_runtime';
        EXECUTE 'GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA hangfire TO oisms_runtime';
        EXECUTE 'GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA hangfire TO oisms_runtime';
        EXECUTE 'ALTER DEFAULT PRIVILEGES IN SCHEMA hangfire GRANT ALL ON TABLES TO oisms_runtime';
        EXECUTE 'ALTER DEFAULT PRIVILEGES IN SCHEMA hangfire GRANT ALL ON SEQUENCES TO oisms_runtime';
    END IF;

    IF EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'oisms_app') THEN
        EXECUTE 'GRANT USAGE, CREATE ON SCHEMA hangfire TO oisms_app';
        EXECUTE 'GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA hangfire TO oisms_app';
        EXECUTE 'GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA hangfire TO oisms_app';
        EXECUTE 'ALTER DEFAULT PRIVILEGES IN SCHEMA hangfire GRANT ALL ON TABLES TO oisms_app';
        EXECUTE 'ALTER DEFAULT PRIVILEGES IN SCHEMA hangfire GRANT ALL ON SEQUENCES TO oisms_app';
    END IF;

    IF EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'oisms_test_app') THEN
        EXECUTE 'GRANT USAGE, CREATE ON SCHEMA hangfire TO oisms_test_app';
        EXECUTE 'GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA hangfire TO oisms_test_app';
        EXECUTE 'GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA hangfire TO oisms_test_app';
        EXECUTE 'ALTER DEFAULT PRIVILEGES IN SCHEMA hangfire GRANT ALL ON TABLES TO oisms_test_app';
        EXECUTE 'ALTER DEFAULT PRIVILEGES IN SCHEMA hangfire GRANT ALL ON SEQUENCES TO oisms_test_app';
    END IF;
END $$;
