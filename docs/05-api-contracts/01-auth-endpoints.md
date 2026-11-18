# 01. Đặc Tả Hợp Đồng API Xác Thực & Tổ Chức (Auth & Organization Endpoints)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-API-01` |
| **Phiên bản (Version)** | `v1.3.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-14` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-AUTH-01..04`, `NFR-SEC-01` (Trang 1, 4, 5) |
| **Phạm vi áp dụng** | `AuthController`, `BranchController`, `WarehousesController`, `SuppliersController`, `UsersController` |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả danh sách Endpoints, quyền truy cập và mã trạng thái HTTP cho phân hệ Auth & Tổ chức. | `INIT-API-01` |
| `v1.1.0` | `2026-11-06` | Nhóm dự án OISMS | Đối chiếu `[Authorize]` trong controller: sửa quyền kho, nhà cung cấp; thêm `toggle-status`, `GET {id}` và nhóm API Người dùng (`/api/users`). | `OISMS-46` |
| `v1.2.0` | `2026-11-14` | Nhóm dự án OISMS | Siết `POST /api/warehouses`, `PUT /api/warehouses/{id}`, `PUT /api/warehouses/{id}/toggle-status` chỉ còn `Owner`; `Staff` và `Cashier` nhận `403`. | `OISMS-59-G1A` |
| `v1.3.0` | `2026-11-14` | Nhóm dự án OISMS | Bổ sung `branchIds` trên API `/api/users` và lọc `GET /api/branches` theo phân công chi nhánh đối với `Cashier` (`CS-02`). | `OISMS-59-G1B` |

---

## 1. Danh Mục Endpoints Xác Thực (`/api/auth`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `POST` | `/api/auth/register-tenant` | `Public` | `FR-AUTH-01` | Đăng ký cửa hàng mới, chuẩn hóa SĐT `E.164`, tạo Tenant + tài khoản Owner trong 1 transaction (không tạo sẵn chi nhánh/kho). Sau khi commit, xếp một mail chào mừng gửi nền cho Owner (Upgrade 11); thiếu SMTP hoặc gửi lỗi vẫn trả `201`. Rate limit 10 lần/15 phút mỗi IP (`RateLimiting:RegisterTenantPer15Minutes`). Trả về `201 Created`, `400`, `409` hoặc `429 TOO_MANY_REQUESTS`. |
| `POST` | `/api/auth/login` | `Public` | `FR-AUTH-02` | Đăng nhập bằng `{ identifier, password }`: `identifier` có `@` là email, không có `@` thì chuẩn hóa SĐT sang E.164 (`0901234567` → `+84901234567`). Trường cũ `email` vẫn nhận khi `identifier` rỗng. Sai tài khoản và sai mật khẩu trả cùng `401` với cùng thông báo. Rate limit 10 lần/phút mỗi IP (`429 TOO_MANY_REQUESTS`). Kiểm tra `User.IsActive` và `Tenant.IsActive`. Trả về `accessToken` (60m) + `refreshToken`. |
| `POST` | `/api/auth/refresh-token` | `Public` | `FR-AUTH-02` | Làm mới JWT với khóa dòng `FOR UPDATE` chống race condition. Kiểm tra trạng thái hoạt động của cả User và Tenant. |
| `POST` | `/api/auth/logout` | `Authenticated` | `FR-AUTH-02` | Thu hồi (`Revoke`) Refresh Token hiện hành. |
| `GET` | `/api/auth/me` | `Authenticated` | `FR-AUTH-02` | Trả về thông tin chi tiết người dùng hiện tại (`UserId`, `TenantId`, `Role`, `FullName`, `Email`). |
| `GET` | `/api/auth/password-reset/status` | `Public` | Upgrade 10 | `{ enabled }`: tự đặt lại mật khẩu qua email có bật không (đủ SMTP, khóa ký `PasswordReset:SigningKey`, `PasswordReset:AdminBaseUrl`). Frontend dùng để bật hoặc tắt nút Quên mật khẩu. |
| `POST` | `/api/auth/forgot-password` | `Public` | Upgrade 10 | `{ identifier, app? }` với `app` là `admin` (mặc định) hoặc `pos`. Luôn trả `202` với cùng thông báo, không lộ tài khoản có tồn tại hay không. Nếu tài khoản và cửa hàng đang hoạt động và có email: xếp mail chứa link `<AdminBaseUrl hoặc PosBaseUrl>/reset-password#token=...` vào hàng đợi gửi nền. Token tự ký HMAC-SHA256, hạn 30 phút, gắn dấu vân tay `password_hash` nên không lưu DB. Tắt tính năng trả `503 PASSWORD_RESET_DISABLED`. Rate limit 5 lần/15 phút mỗi IP. |
| `POST` | `/api/auth/reset-password` | `Public` | Upgrade 10 | `{ token, newPassword }` (6–100 ký tự). Trong một transaction: khóa dòng `users` (`FOR UPDATE`), đọc lại và so dấu vân tay, ghi hash mới, thu hồi mọi refresh token. Token sai, hết hạn hoặc đã dùng trả `400 RESET_TOKEN_INVALID`; tài khoản hoặc cửa hàng bị khóa trả `403 USER_INACTIVE`/`TENANT_INACTIVE`. Access token cũ còn hiệu lực tối đa 60 phút. |

---

## 2. Danh Mục Endpoints Chi Nhánh, Kho & Nhà Cung Cấp (`FR-AUTH-04`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `GET` | `/api/branches` | `Owner, Staff, Cashier` | `FR-AUTH-04`, `CS-02` | Lấy danh sách chi nhánh thuộc Tenant hiện tại. Riêng `Cashier` có phân công trong `user_branches` chỉ nhận các chi nhánh được gán; `Cashier` chưa được gán chi nhánh nào, `Staff` và `Owner` nhận toàn bộ chi nhánh của tenant. |
| `GET` | `/api/branches/{id}` | `Owner, Staff, Cashier` | `FR-AUTH-04` | Chi tiết chi nhánh. |
| `POST` | `/api/branches` | `Owner` | `FR-AUTH-04` | Tạo chi nhánh mới thuộc Tenant. |
| `PUT` | `/api/branches/{id}` | `Owner` | `FR-AUTH-04` | Cập nhật thông tin chi nhánh. |
| `PUT` | `/api/branches/{id}/toggle-status` | `Owner` | `FR-AUTH-04` | Bật/tắt `IsActive` của chi nhánh. |
| `GET` | `/api/warehouses` | `Owner, Staff, Cashier` | `FR-AUTH-04` | Lấy danh sách kho hàng (hỗ trợ lọc theo `branchId`). |
| `GET` | `/api/warehouses/{id}` | `Owner, Staff, Cashier` | `FR-AUTH-04` | Chi tiết kho. |
| `POST` | `/api/warehouses` | `Owner` | `FR-AUTH-04` | Tạo kho hàng mới gắn với chi nhánh cùng Tenant (`Staff` và `Cashier` → `403`). |
| `PUT` | `/api/warehouses/{id}` | `Owner` | `FR-AUTH-04` | Cập nhật kho; chặn đổi `BranchId` nếu kho đã có lịch sử sổ cái/tồn kho (`409 Conflict`). |
| `PUT` | `/api/warehouses/{id}/toggle-status` | `Owner` | `FR-AUTH-04` | Bật/tắt `IsActive` của kho. |
| `GET` | `/api/suppliers`, `/api/suppliers/{id}` | `Owner, Staff, Cashier` | `FR-INV-02` | Danh sách và chi tiết nhà cung cấp. |
| `POST` | `/api/suppliers` | `Owner, Staff` | `FR-INV-02` | Tạo nhà cung cấp. Chưa có API sửa/xóa. |

---

## 3. Danh Mục Endpoints Người Dùng (`/api/users`, CS-02)

Toàn bộ controller yêu cầu vai trò `Owner`.

| Method | Endpoint | Mô tả & Quy tắc xử lý |
| :---: | :--- | :--- |
| `GET` | `/api/users` | Danh sách người dùng trong tenant, kèm mảng `branchIds` (rỗng `[]` nghĩa là được dùng mọi chi nhánh). |
| `GET` | `/api/users/{id}` | Chi tiết người dùng, kèm mảng `branchIds`. |
| `POST` | `/api/users` | Tạo tài khoản `Staff` hoặc `Cashier`; email/số điện thoại phải duy nhất toàn hệ thống (`409`). Nhận `branchIds` tùy chọn; chi nhánh không tồn tại hoặc ngoài tenant trả `400 BRANCH_NOT_FOUND` (không ghi DB); lưu user và `user_branches` trong cùng giao dịch. |
| `PUT` | `/api/users/{id}` | Cập nhật thông tin, vai trò và phân công chi nhánh (`branchIds` tùy chọn: truyền mảng sẽ thay toàn bộ danh sách gán trong cùng `SaveChanges`, truyền `null`/bỏ qua sẽ giữ nguyên). Riêng vai trò `Owner` bỏ qua `branchIds` và xóa mọi gán cũ. Chi nhánh không hợp lệ trả `400 BRANCH_NOT_FOUND`. |
| `PATCH` | `/api/users/{id}/status` | Khóa/mở khóa tài khoản. |
| `POST` | `/api/users/{id}/reset-password` | Upgrade 10. Owner đặt lại mật khẩu hộ `Staff`/`Cashier` cùng cửa hàng: `{ newPassword }` (6–100 ký tự). Tài khoản `Owner` (kể cả chính mình) trả `403 CANNOT_RESET_OWNER_PASSWORD`; người dùng cửa hàng khác trả `404 USER_NOT_FOUND`. Ghi hash mới và thu hồi mọi refresh token của nhân viên trong cùng một `SaveChanges`. |
