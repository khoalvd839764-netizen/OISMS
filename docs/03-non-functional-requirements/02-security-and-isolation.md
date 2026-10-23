# 02. Cô Lập Đa Khách Thuê, Khả Năng Mở Rộng & Bảo Mật (Multi-tenancy, Scalability & Security)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-NFR-02` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `NFR-TENANT-01`, `NFR-TENANT-02`, `NFR-SEC-01` (Trang 4) |
| **Phạm vi áp dụng** | PostgreSQL Schema, EF Core DbContext, JWT Authentication & HTTPS |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả yêu cầu cô lập dữ liệu Multi-tenant, tối ưu hóa chỉ mục hàng triệu bản ghi và mã hóa xác thực. | `INIT-NFR-02` |

---

## 1. Cô Lập Dữ Liệu Đa Khách Thuê & Mở Rộng (`NFR-TENANT-01`, `NFR-TENANT-02`)

| Mã NFR | Tên yêu cầu | Đặc tả kỹ thuật bắt buộc |
| :---: | :--- | :--- |
| **`NFR-TENANT-01`** | **Data Isolation** (Cô lập dữ liệu) | - Áp dụng mẫu **Shared Database, Separate Schema/Tenant ID pattern**.<br/>- Tất cả các bảng dữ liệu vận hành bắt buộc phải có cột `TenantId`.<br/>- Thực thi **EF Core Global Query Filters** tự động trên toàn bộ thực thể `ITenantEntity` để đảm bảo **0% rò rỉ dữ liệu (`Zero data leakage`)** giữa các Tenant. |
| **`NFR-TENANT-02`** | **Scalability** (Khả năng mở rộng) | - Thiết lập Composite Index tối ưu trên `(TenantId, CreatedAt)` và `(TenantId, SKUId)` cho bảng `InventoryLedger` (`inventory_transactions`) và `Orders` (`orders` / `order_items`) nhằm duy trì tốc độ truy vấn ổn định ở quy mô hàng triệu dòng sổ cái. |

---

## 2. Mã Hóa & Bảo Mật Xác Thực (`NFR-SEC-01`)

- **Băm mật khẩu (`Password Hashing`):** Mật khẩu người dùng bắt buộc băm một chiều bằng thuật toán **BCrypt** (hoặc Argon2), tuyệt đối không lưu mật khẩu dạng rõ (`Plaintext`).
- **Bảo mật đường truyền:** Giao tiếp API qua giao thức **HTTPS / TLS 1.3**.
- **Quản lý phiên JWT:** Sử dụng **Access Token ngắn hạn (60 phút)** đi kèm cơ chế **Refresh Token an toàn** (có khóa dòng chống race condition khi xoay vòng token).
