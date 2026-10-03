-- ==============================================================================
-- DỰ ÁN: OISM - Omnichannel Inventory and Sales Management System
-- SPRINT 1: MOCK SEED DATA SCRIPT (FILE: seed_data.sql)
-- Tác giả: Đăng Khoa (Tech Lead & Database Master)
-- Ngày tạo: 04/10/2026
-- Mô tả: File nạp sẵn dữ liệu mẫu để cả nhóm Backend dùng test ngay:
--        1. Ba vai trò hệ thống chuẩn (Owner, Staff, Cashier)
--        2. Một Cửa hàng mẫu (Shop Thời Trang GenZ)
--        3. Một tài khoản Chủ shop mẫu (email: owner@genz.com / pass: 123456)
--        4. Một tài khoản Thu ngân mẫu (email: cashier@genz.com / pass: 123456)
--        5. Hai Chi nhánh mẫu (Chi nhánh Quận 1, Kho Tổng TP.HCM)
-- ==============================================================================

-- ------------------------------------------------------------------------------
-- 1. TẠO 3 VAI TRÒ HỆ THỐNG CỐ ĐỊNH (roles)
-- Theo yêu cầu FR-AUTH-03 (Role-Based Access Control - RBAC)
-- Lưu ý: Chúng ta dùng UUID cố định để code C# dễ dàng tham chiếu.
-- ------------------------------------------------------------------------------
INSERT INTO roles (id, code, description) VALUES
    ('11111111-1111-1111-1111-111111111111', 'Owner', 'Chủ cửa hàng - Toàn quyền quản trị hệ thống, chi nhánh, nhân sự và báo cáo tài chính'),
    ('22222222-2222-2222-2222-222222222222', 'Staff', 'Nhân viên quản lý kho & đơn hàng - Xử lý nhập/xuất kho và duyệt đơn hàng'),
    ('33333333-3333-3333-3333-333333333333', 'Cashier', 'Thu ngân POS - Thao tác bán hàng nhanh tại quầy và in hóa đơn')
ON CONFLICT (code) DO NOTHING;


-- ------------------------------------------------------------------------------
-- 2. TẠO CỬA HÀNG MẪU (tenants)
-- Phục vụ test cách ly dữ liệu Multi-tenant (TenantId cố định: 'a0000000-0000-0000-0000-000000000001')
-- ------------------------------------------------------------------------------
INSERT INTO tenants (id, name, code, is_active) VALUES
    ('a0000000-0000-0000-0000-000000000001', 'Shop Thời Trang GenZ', 'genz-fashion', TRUE)
ON CONFLICT (code) DO NOTHING;


-- ------------------------------------------------------------------------------
-- 3. TẠO CÁC TÀI KHOẢN NGƯỜI DÙNG MẪU (users)
-- MẬT KHẨU MẶC ĐỊNH CHO TẤT CẢ TÀI KHOẢN LÀ: 123456
-- Chuỗi hash BCrypt chuẩn bên dưới tương ứng với mật khẩu text: 123456
-- ($2a$11$qR6mCwdwz9.w1Z1iVpIehuN3LdY0N7EkeD9b4uY9vB4D5XoP9I6y.)
-- ------------------------------------------------------------------------------

-- 3.1 Tài khoản Chủ cửa hàng (Owner)
INSERT INTO users (id, tenant_id, email, password_hash, full_name, phone_number, is_active) VALUES
    ('b0000000-0000-0000-0000-000000000001', 
     'a0000000-0000-0000-0000-000000000001', 
     'owner@genz.com', 
     '$2a$11$qR6mCwdwz9.w1Z1iVpIehuN3LdY0N7EkeD9b4uY9vB4D5XoP9I6y.', 
     'Lê Võ Đăng Khoa (Chủ Shop)', 
     '0901234567', 
     TRUE)
ON CONFLICT (tenant_id, email) DO NOTHING;

-- 3.2 Tài khoản Thu ngân (Cashier)
INSERT INTO users (id, tenant_id, email, password_hash, full_name, phone_number, is_active) VALUES
    ('b0000000-0000-0000-0000-000000000002', 
     'a0000000-0000-0000-0000-000000000001', 
     'cashier@genz.com', 
     '$2a$11$qR6mCwdwz9.w1Z1iVpIehuN3LdY0N7EkeD9b4uY9vB4D5XoP9I6y.', 
     'Nguyễn Văn Thu Ngân', 
     '0909888999', 
     TRUE)
ON CONFLICT (tenant_id, email) DO NOTHING;


-- ------------------------------------------------------------------------------
-- 4. PHÂN QUYỀN CHO NGƯỜI DÙNG (user_roles)
-- ------------------------------------------------------------------------------

-- Gán quyền Owner cho tài khoản owner@genz.com
INSERT INTO user_roles (user_id, role_id) VALUES
    ('b0000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111')
ON CONFLICT (user_id, role_id) DO NOTHING;

-- Gán quyền Cashier cho tài khoản cashier@genz.com
INSERT INTO user_roles (user_id, role_id) VALUES
    ('b0000000-0000-0000-0000-000000000002', '33333333-3333-3333-3333-333333333333')
ON CONFLICT (user_id, role_id) DO NOTHING;


-- ------------------------------------------------------------------------------
-- 5. TẠO CÁC CHI NHÁNH & KHO HÀNG MẪU (branches)
-- Phục vụ: Yêu cầu FR-AUTH-04
-- ------------------------------------------------------------------------------
INSERT INTO branches (id, tenant_id, name, address, phone, is_active) VALUES
    ('c0000000-0000-0000-0000-000000000001', 
     'a0000000-0000-0000-0000-000000000001', 
     'Chi nhánh 1 - Quận 1 (Showroom POS)', 
     '123 Đường Lê Lợi, Phường Bến Nghé, Quận 1, TP.HCM', 
     '02838222111', 
     TRUE),
    ('c0000000-0000-0000-0000-000000000002', 
     'a0000000-0000-0000-0000-000000000001', 
     'Kho Tổng - TP. Thủ Đức', 
     '456 Xa Lộ Hà Nội, Phường Linh Trung, TP. Thủ Đức, TP.HCM', 
     '02838999888', 
     TRUE)
ON CONFLICT (id) DO NOTHING;

-- ==============================================================================
-- HOÀN TẤT NẠP DỮ LIỆU MẪU (MẬT KHẨU TEST ĐĂNG NHẬP: 123456)
-- ==============================================================================
