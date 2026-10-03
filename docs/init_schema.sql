-- ==============================================================================
-- DỰ ÁN: OISM - Omnichannel Inventory and Sales Management System
-- SPRINT 1: DATABASE INITIALIZATION SCRIPT (INIT SCHEMA)
-- Tác giả: Đăng Khoa (Tech Lead & Database Master)
-- Ngày tạo: 04/10/2026
-- Mô tả: File khởi tạo 5 bảng cốt lõi phục vụ Multi-tenancy, Phân quyền RBAC,
--        Xác thực User và Quản lý Chi nhánh/Kho hàng.
-- ==============================================================================

-- ------------------------------------------------------------------------------
-- 0. KÍCH HOẠT EXTENSION HỖ TRỢ SINH KHÓA CHÍNH DẠNG UUID
-- Tác dụng: Cung cấp hàm gen_random_uuid() để tự động sinh chuỗi ID ngẫu nhiên,
--           tránh lộ số thứ tự tăng dần (1, 2, 3...) và hỗ trợ phân tán dữ liệu.
-- ------------------------------------------------------------------------------
CREATE EXTENSION IF NOT EXISTS "pgcrypto";


-- ------------------------------------------------------------------------------
-- 1. BẢNG CỬA HÀNG / DOANH NGHIỆP THUÊ HỆ THỐNG (tenants)
-- Phục vụ: Yêu cầu FR-AUTH-01 (Tenant Registration)
-- Ý nghĩa: Gốc rễ của kiến trúc SaaS Multi-tenant. Mỗi khách hàng doanh nghiệp
--          khi đăng ký mở shop sẽ là một bản ghi độc lập tại bảng này.
-- ------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS tenants (
    -- Khóa chính: Mã định danh duy nhất của Cửa hàng (TenantId)
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Tên hiển thị của cửa hàng/doanh nghiệp (Ví dụ: "Shop Thời Trang GenZ")
    name VARCHAR(255) NOT NULL,

    -- Mã định danh duy nhất (Slug/Code) dùng cho đường dẫn hoặc nhận diện shop (Ví dụ: "genz-fashion")
    code VARCHAR(50) NOT NULL UNIQUE,

    -- Trạng thái hoạt động: true = Đang hoạt động, false = Bị tạm khóa
    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    -- Thời điểm tạo cửa hàng (lưu chuẩn múi giờ UTC)
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,

    -- Thời điểm cập nhật thông tin cửa hàng gần nhất (nếu có)
    updated_at TIMESTAMP WITH TIME ZONE NULL
);

-- Chú thích cấp bảng và cột (Database Comments)
COMMENT ON TABLE tenants IS 'Bảng lưu thông tin các Cửa hàng/Doanh nghiệp sử dụng hệ thống SaaS OISM';
COMMENT ON COLUMN tenants.id IS 'Mã định danh duy nhất của cửa hàng (TenantId)';
COMMENT ON COLUMN tenants.code IS 'Mã code viết liền không dấu, duy nhất trên toàn hệ thống';


-- ------------------------------------------------------------------------------
-- 2. BẢNG VAI TRÒ HỆ THỐNG (roles)
-- Phục vụ: Yêu cầu FR-AUTH-03 (Role-Based Access Control - RBAC)
-- Ý nghĩa: Lưu các chức danh cố định của hệ thống. Bảng này dùng chung cho toàn bộ
--          các Tenant (không có cột tenant_id) để chuẩn hóa quyền hạn.
-- ------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS roles (
    -- Khóa chính của vai trò
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Mã vai trò chuẩn: 'Owner' (Chủ shop), 'Staff' (Quản lý kho/đơn), 'Cashier' (Thu ngân POS)
    code VARCHAR(50) NOT NULL UNIQUE,

    -- Mô tả chi tiết quyền hạn của vai trò
    description VARCHAR(255) NULL,

    -- Thời điểm khởi tạo vai trò trong hệ thống
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

COMMENT ON TABLE roles IS 'Bảng danh mục các vai trò/quyền hạn cố định trong hệ thống OISM';
COMMENT ON COLUMN roles.code IS 'Mã vai trò chuẩn (Owner, Staff, Cashier)';


-- ------------------------------------------------------------------------------
-- 3. BẢNG TÀI KHOẢN NGƯỜI DÙNG / NHÂN VIÊN (users)
-- Phục vụ: Yêu cầu FR-AUTH-02 (User Authentication)
-- Ý nghĩa: Lưu trữ tài khoản đăng nhập của Chủ shop và Nhân viên. BẮT BUỘC có cột
--          tenant_id để cách ly dữ liệu giữa các cửa hàng.
-- ------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS users (
    -- Khóa chính định danh người dùng
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Khóa ngoại: Người dùng này thuộc về Cửa hàng (Tenant) nào
    tenant_id UUID NOT NULL,

    -- Email đăng nhập của người dùng
    email VARCHAR(255) NOT NULL,

    -- Mật khẩu đã được mã hóa băm một chiều bằng thuật toán BCrypt (Tuyệt đối không lưu text thường)
    password_hash VARCHAR(255) NOT NULL,

    -- Họ và tên đầy đủ của người dùng
    full_name VARCHAR(100) NOT NULL,

    -- Số điện thoại liên hệ (hỗ trợ đăng nhập hoặc nhận SMS cảnh báo)
    phone_number VARCHAR(20) NULL,

    -- Trạng thái tài khoản: true = Đang hoạt động, false = Bị vô hiệu hóa
    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    -- Thời điểm tạo tài khoản
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,

    -- Thời điểm cập nhật thông tin tài khoản lần cuối
    updated_at TIMESTAMP WITH TIME ZONE NULL,

    -- RÀNG BUỘC TOÀN VẸN: Trong cùng một Cửa hàng (tenant_id), không được phép trùng Email
    CONSTRAINT uq_users_tenant_email UNIQUE (tenant_id, email)
);

COMMENT ON TABLE users IS 'Bảng lưu thông tin tài khoản người dùng của từng Cửa hàng';
COMMENT ON COLUMN users.tenant_id IS 'Khóa ngoại liên kết tới bảng tenants để cô lập dữ liệu Multi-tenant';
COMMENT ON COLUMN users.password_hash IS 'Chuỗi hash mật khẩu bảo mật BCrypt/Argon2 theo chuẩn NFR-SEC-01';


-- ------------------------------------------------------------------------------
-- 4. BẢNG PHÂN QUYỀN NGƯỜI DÙNG (user_roles)
-- Phục vụ: Yêu cầu FR-AUTH-03 (Phân quyền RBAC)
-- Ý nghĩa: Bảng trung gian liên kết nhiều-nhiều (N-N) giữa Users và Roles.
--          Một người dùng có thể giữ một hoặc nhiều vai trò trong cửa hàng.
-- ------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS user_roles (
    -- Khóa ngoại trỏ đến tài khoản người dùng
    user_id UUID NOT NULL,

    -- Khóa ngoại trỏ đến vai trò được cấp
    role_id UUID NOT NULL,

    -- Thời điểm gán vai trò này cho người dùng
    assigned_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,

    -- Khóa chính kết hợp: Đảm bảo 1 user không bị gán trùng 1 vai trò 2 lần
    PRIMARY KEY (user_id, role_id)
);

COMMENT ON TABLE user_roles IS 'Bảng trung gian liên kết phân quyền giữa Users và Roles';


-- ------------------------------------------------------------------------------
-- 5. BẢNG CHI NHÁNH & KHO HÀNG (branches)
-- Phục vụ: Yêu cầu FR-AUTH-04 (Branch Management)
-- Ý nghĩa: Cho phép một Cửa hàng (Tenant) sở hữu nhiều Chi nhánh bán lẻ (POS)
--          hoặc nhiều Kho chứa hàng khác nhau để quản lý luồng xuất nhập tồn.
-- ------------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS branches (
    -- Khóa chính của chi nhánh / kho hàng
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Khóa ngoại: Chi nhánh này thuộc quyền sở hữu của Cửa hàng nào
    tenant_id UUID NOT NULL,

    -- Tên chi nhánh hoặc kho (Ví dụ: "Chi nhánh Quận 1", "Kho Tổng TP.HCM")
    name VARCHAR(255) NOT NULL,

    -- Địa chỉ thực tế của chi nhánh/kho
    address TEXT NULL,

    -- Số điện thoại liên hệ của chi nhánh
    phone VARCHAR(20) NULL,

    -- Trạng thái hoạt động: true = Đang mở cửa bán hàng, false = Tạm đóng cửa
    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    -- Thời điểm tạo chi nhánh
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,

    -- Thời điểm cập nhật thông tin chi nhánh gần nhất
    updated_at TIMESTAMP WITH TIME ZONE NULL
);

COMMENT ON TABLE branches IS 'Bảng lưu danh sách các chi nhánh bán hàng và kho hàng của từng Cửa hàng';
COMMENT ON COLUMN branches.tenant_id IS 'Khóa ngoại liên kết tới bảng tenants';


-- ------------------------------------------------------------------------------
-- 6. THIẾT LẬP RÀNG BUỘC KHÓA NGOẠI (FOREIGN KEYS)
-- Tác dụng: Đảm bảo tính toàn vẹn dữ liệu (Referential Integrity).
--           Quy tắc ON DELETE RESTRICT ngăn chặn việc xóa nhầm Cửa hàng cha
--           khi vẫn còn dữ liệu con (Users, Branches) bên trong.
-- ------------------------------------------------------------------------------

-- Ràng buộc: User phải thuộc về một Tenant có thật trong hệ thống
ALTER TABLE users
    ADD CONSTRAINT fk_users_tenant 
    FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE RESTRICT;

-- Ràng buộc: Khi xóa User thì tự động xóa liên kết quyền tương ứng trong user_roles
ALTER TABLE user_roles
    ADD CONSTRAINT fk_user_roles_user 
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE;

-- Ràng buộc: Không được xóa Role nếu đang có User giữ Role đó
ALTER TABLE user_roles
    ADD CONSTRAINT fk_user_roles_role 
    FOREIGN KEY (role_id) REFERENCES roles(id) ON DELETE RESTRICT;

-- Ràng buộc: Chi nhánh phải thuộc về một Tenant có thật trong hệ thống
ALTER TABLE branches
    ADD CONSTRAINT fk_branches_tenant 
    FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE RESTRICT;


-- ------------------------------------------------------------------------------
-- 7. THIẾT LẬP CHỈ MỤC TỐI ƯU TRUY VẤN (COMPOSITE INDEXES)
-- Phục vụ: Yêu cầu phi chức năng NFR-TENANT-02 (Tối ưu hóa Index theo TenantId)
-- Tác dụng: Giúp Database tìm kiếm cực nhanh khi dữ liệu phình to lên hàng triệu dòng,
--           đáp ứng chuẩn thời gian phản hồi NFR-PERF-01 (< 200ms).
-- ------------------------------------------------------------------------------

-- Tối ưu câu lệnh: Tìm kiếm danh sách nhân viên của Shop theo ngày tạo gần nhất
CREATE INDEX IF NOT EXISTS idx_users_tenant_created 
    ON users(tenant_id, created_at DESC);

-- Tối ưu câu lệnh: Tìm kiếm nhanh nhân viên theo Email trong phạm vi 1 Cửa hàng
CREATE INDEX IF NOT EXISTS idx_users_tenant_email 
    ON users(tenant_id, email);

-- Tối ưu câu lệnh: Lấy danh sách chi nhánh của một Cửa hàng
CREATE INDEX IF NOT EXISTS idx_branches_tenant_created 
    ON branches(tenant_id, created_at DESC);

-- ==============================================================================
-- HOÀN TẤT KHỞI TẠO SCHEMA GIAI ĐOẠN 1
-- ==============================================================================
