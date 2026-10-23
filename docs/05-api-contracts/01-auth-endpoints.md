# 01. Đặc Tả Hợp Đồng API Xác Thực & Tổ Chức (Auth & Organization Endpoints)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-API-01` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-AUTH-01..04`, `NFR-SEC-01` (Trang 1, 4, 5) |
| **Phạm vi áp dụng** | `AuthController`, `BranchesController`, `WarehousesController`, `SuppliersController` |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả danh sách Endpoints, quyền truy cập và mã trạng thái HTTP cho phân hệ Auth & Tổ chức. | `INIT-API-01` |

---

## 1. Danh Mục Endpoints Xác Thực (`/api/auth`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `POST` | `/api/auth/register-tenant` | `Public` | `FR-AUTH-01` | Đăng ký cửa hàng mới, chuẩn hóa SĐT `E.164`, tạo Tenant + Main Branch + Main Warehouse + Owner trong 1 transaction. Trả về `201 Created`, `400` hoặc `409`. |
| `POST` | `/api/auth/login` | `Public` | `FR-AUTH-02` | Đăng nhập bằng Email/SĐT + Password. Kiểm tra `User.IsActive` và `Tenant.IsActive`. Trả về `accessToken` (60m) + `refreshToken`. |
| `POST` | `/api/auth/refresh-token` | `Public` | `FR-AUTH-02` | Làm mới JWT với khóa dòng `FOR UPDATE` chống race condition. Kiểm tra trạng thái hoạt động của cả User và Tenant. |
| `POST` | `/api/auth/logout` | `Authenticated` | `FR-AUTH-02` | Thu hồi (`Revoke`) Refresh Token hiện hành. |
| `GET` | `/api/auth/me` | `Authenticated` | `FR-AUTH-02` | Trả về thông tin chi tiết người dùng hiện tại (`UserId`, `TenantId`, `Role`, `FullName`, `Email`). |

---

## 2. Danh Mục Endpoints Chi Nhánh, Kho & Nhà Cung Cấp (`FR-AUTH-04`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `GET` | `/api/branches` | `Owner, Staff, Cashier` | `FR-AUTH-04` | Lấy danh sách chi nhánh thuộc Tenant hiện tại. |
| `POST` | `/api/branches` | `Owner` | `FR-AUTH-04` | Tạo chi nhánh mới thuộc Tenant. |
| `PUT` | `/api/branches/{id}` | `Owner` | `FR-AUTH-04` | Cập nhật thông tin hoặc bật/tắt `IsActive` của chi nhánh. |
| `GET` | `/api/warehouses` | `Owner, Staff` | `FR-AUTH-04` | Lấy danh sách kho hàng (hỗ trợ lọc theo `branchId`). |
| `POST` | `/api/warehouses` | `Owner` | `FR-AUTH-04` | Tạo kho hàng mới gắn với chi nhánh cùng Tenant. |
| `PUT` | `/api/warehouses/{id}` | `Owner` | `FR-AUTH-04` | Cập nhật kho; chặn đổi `BranchId` nếu kho đã có lịch sử sổ cái/tồn kho (`409 Conflict`). |
| `GET/POST/PUT` | `/api/suppliers` | `Owner, Staff` | `FR-INV-02` | Quản lý danh sách Nhà cung cấp phục vụ lập phiếu nhập kho. |
