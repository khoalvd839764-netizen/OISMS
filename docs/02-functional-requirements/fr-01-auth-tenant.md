# FR-01: Quản Lý Đa Khách Thuê, Xác Thực & Chi Nhánh (Multi-tenant & Authentication)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-01` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-AUTH-01..04`, `NFR-TENANT-01`, `NFR-SEC-01` (Trang 1, 4) |
| **Gói công việc (Task Package)** | Task Package 1 — Core Platform & Infrastructure Setup |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả chi tiết nhóm yêu cầu chức năng `FR-AUTH-01` đến `FR-AUTH-04`. | `INIT-FR-01` |

---

## 1. Bảng Đặc Tả Chi Tiết Yêu Cầu (`FR-AUTH`)

| Mã yêu cầu | Tên yêu cầu | Mô tả nghiệp vụ & Quy tắc kỹ thuật | Tiêu chí nghiệm thu (Acceptance Criteria) |
| :---: | :--- | :--- | :--- |
| **`FR-AUTH-01`** | **Tenant Registration** (Đăng ký cửa hàng) | Cho phép tạo mới các Store Tenants với không gian dữ liệu cô lập hoàn toàn được gán một `TenantId` duy nhất. Chuẩn hóa số điện thoại theo chuẩn quốc tế `E.164` (`+84...`). | - Tạo thành công Tenant, Chi nhánh mặc định, Kho mặc định và tài khoản `Owner` trong 1 transaction nguyên tử.<br/>- Từ chối Email/SĐT trùng hoặc sai định dạng với mã `400`/`409` (không phát sinh lỗi `500`). |
| **`FR-AUTH-02`** | **User Authentication** (Xác thực người dùng) | Cung cấp chức năng Đăng nhập (`Login`), Làm mới phiên (`Refresh Token`) và Đăng xuất (`Logout`) bằng Email hoặc Số điện thoại + Mật khẩu. Trả về JWT chứa `TenantId`, `UserId`, `Role`. | - Mật khẩu băm bằng `BCrypt`.<br/>- Access Token có hạn 60 phút.<br/>- Refresh Token xoay vòng nguyên tử (`SELECT ... FOR UPDATE`), chặn dùng lại token cũ (`401`). |
| **`FR-AUTH-03`** | **Role-Based Access Control (RBAC)** | Cấu hình và kiểm soát quyền truy cập API và màn hình theo 3 vai trò: `Owner` (Toàn quyền), `Staff` (Quản lý kho/đơn hàng), `Cashier` (Chỉ POS). | - `Cashier` gọi API quản trị hoặc mở URL `/admin` bị chặn `403 Forbidden`.<br/>- Tự động điều hướng sau đăng nhập theo đúng `Role` trong JWT. |
| **`FR-AUTH-04`** | **Branch & Warehouse Management** | Cho phép `Owner` tạo mới, cập nhật thông tin hoặc bật/tắt trạng thái hiển thị (`IsActive`) của các chi nhánh (`Branch`) và kho hàng (`Warehouse`) thuộc sở hữu của mình. | - Chỉ `Owner` tạo/sửa được chi nhánh và kho.<br/>- Chặn đổi chi nhánh của kho đã phát sinh lịch sử chứng từ/sổ cái (`409 Conflict`). |

---

## 2. Ràng Buộc Bảo Mật & Cô Lập Đa Khách Thuê

1. **EF Core Global Query Filters (`NFR-TENANT-01`):** Mọi truy vấn đọc/ghi dữ liệu trên các bảng nghiệp vụ đều tự động đính kèm điều kiện `WHERE tenant_id = @CurrentTenantId`.
2. **Kiểm tra trạng thái khóa Tenant:** Khi `Tenant.IsActive == false` hoặc `User.IsActive == false`, hệ thống từ chối ngay cả bước `Login` lẫn `RefreshToken`.
