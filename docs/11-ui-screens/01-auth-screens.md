# 01. ĐẶC TẢ CÁC MÀN HÌNH XÁC THỰC, PHÂN QUYỀN & TRANG LỖI TRUY CẬP (AUTH & ACCESS SCREENS)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SCR-01` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực** | `2026-10-25` |
| **Đường dẫn URL Routes** | `/login`, `/403`, `/session-expired` |
| **Quyền truy cập (RBAC)** | Mọi người dùng (Public & Authenticated) |
| **Source code Frontend** | [`AuthPage.tsx`](file:///d:/OISMS/src/frontend/admin-dashboard/src/pages/AuthPage.tsx), [`AccessPage.tsx`](file:///d:/OISMS/src/frontend/admin-dashboard/src/pages/AccessPage.tsx), [`AuthContext.tsx`](file:///d:/OISMS/src/frontend/admin-dashboard/src/context/AuthContext.tsx) |
| **Controller Backend** | [`AuthController.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/AuthController.cs) |

---

## 1. Màn Hình Đăng Nhập Hệ Thống (`/login`)

### 1.1. Mục Tiêu & Luồng Nghiệp Vụ
Màn hình xác thực tập trung hỗ trợ mô hình Multi-tenant SaaS cho toàn bộ các đối tượng người dùng (`Owner`, `Staff`, `Cashier`). Cho phép đăng nhập linh hoạt bằng **Email** hoặc **Số điện thoại** kết hợp Mật khẩu, đồng thời tự động phân luồng điều hướng:
* Nếu người dùng là `Cashier` $\rightarrow$ Tự động chuyển hướng sang Quầy bán hàng `/pos`.
* Nếu người dùng là `Owner` hoặc `Staff` $\rightarrow$ Chuyển hướng vào Bàn làm việc quản trị `/admin`.

### 1.2. Bố Cục Giao Diện Wireframe (ASCII Wireframe)

```text
┌────────────────────────────────────────────────────────────────────────┐
│                              OISMS RETAIL                              │
│                   Hệ thống Quản lý Bán lẻ & Tồn kho Đa kênh            │
├────────────────────────────────────────────────────────────────────────┤
│                                                                        │
│                    ┌──────────────────────────────┐                    │
│                    │     ĐĂNG NHẬP HỆ THỐNG       │                    │
│                    │   Chào mừng bạn quay trở lại!│                    │
│                    ├──────────────────────────────┤                    │
│                    │ Mã cửa hàng / Tenant slug    │                    │
│                    │ [ demo-store               ] │                    │
│                    │                              │                    │
│                    │ Email hoặc Số điện thoại     │                    │
│                    │ [ admin@oisms.vn           ] │                    │
│                    │                              │                    │
│                    │ Mật khẩu                     │                    │
│                    │ [ ••••••••••••••••         👁]│                    │
│                    │                              │                    │
│                    │ [✓] Ghi nhớ đăng nhập        │                    │
│                    │                              │                    │
│                    │ [    ĐĂNG NHẬP HỆ THỐNG    ] │                    │
│                    └──────────────────────────────┘                    │
│                                                                        │
└────────────────────────────────────────────────────────────────────────┘
```

### 1.3. Bảng Thành Phần UI & Ràng Buộc Dữ Liệu

| Tên trường UI | Kiểu Component | Ràng buộc nghiệp vụ (Validation) | Phản hồi khi vi phạm |
| :--- | :--- | :--- | :--- |
| `TenantSlug` | `Input` (Text) | Bắt buộc, chuỗi viết thường không dấu hoặc rỗng nếu dùng subdomain. | Viền đỏ `#c93c48` + Text *"Mã cửa hàng không được để trống"* |
| `Username/Email/Phone` | `Input` (Text) | Bắt buộc, chuỗi định dạng Email chuẩn hoặc SĐT chuẩn `E.164`. | Viền đỏ `#c93c48` + Text *"Vui lòng nhập Email hoặc SĐT hợp lệ"* |
| `Password` | `Input` (Password) | Bắt buộc, tối thiểu 6 ký tự, có nút ẩn/hiện mắt xem mật khẩu. | Viền đỏ `#c93c48` + Text *"Mật khẩu tối thiểu 6 ký tự"* |
| `RememberMe` | `Checkbox` | Mặc định `true` (lưu Refresh Token trong 30 ngày). | N/A |
| Nút `Đăng nhập` | `Button (Primary)` | Hiển thị spinner xoay và vô hiệu hóa (`disabled`) khi đang gửi request. | Chống bấm lặp (Debounce) |

### 1.4. Bảng Hàm API Backend & Tài Liệu Tham Chiếu

| Method | Endpoint Backend | Hàm xử lý Controller / Service | Tài liệu Hợp đồng API tham chiếu | Mã lỗi liên quan |
| :---: | :--- | :--- | :--- | :--- |
| `POST` | `/api/auth/login` | `AuthController.Login` | [`DOC-API-01 - 01-auth-endpoints.md`](../05-api-contracts/01-auth-endpoints.md#2-endpoints-xác-thực-người-dùng-fr-auth-02) | `AUTH_INVALID_CREDENTIALS`, `AUTH_TENANT_LOCKED`, `AUTH_USER_LOCKED` (Chi tiết xem [`DOC-ERR-02`](../12-error-and-status-codes/02-error-codes-catalog.md)) |
| `POST` | `/api/auth/refresh-token` | `AuthController.RefreshToken` | [`DOC-API-01 - 01-auth-endpoints.md`](../05-api-contracts/01-auth-endpoints.md#2-endpoints-xác-thực-người-dùng-fr-auth-02) | `AUTH_TOKEN_EXPIRED`, `AUTH_UNAUTHORIZED` |
| `GET` | `/api/auth/me` | `AuthController.GetCurrentUser` | [`DOC-API-01 - 01-auth-endpoints.md`](../05-api-contracts/01-auth-endpoints.md#2-endpoints-xác-thực-người-dùng-fr-auth-02) | `AUTH_UNAUTHORIZED` |

---

## 2. Màn Hình Bị Từ Chối Quyền Truy Cập (`/403` - Forbidden Access)

### 2.1. Mục Tiêu & Luồng Nghiệp Vụ
Hiển thị khi một tài khoản đã đăng nhập hợp lệ nhưng cố tình truy cập vào các màn hình vượt quá thẩm quyền của mình theo ma trận RBAC (Ví dụ: `Cashier` cố tình gõ link `/admin/reports` hoặc `/admin/organization`).

### 2.2. Bố Cục Giao Diện Wireframe
```text
┌────────────────────────────────────────────────────────────────────────┐
│                                                                        │
│                            [ 🚫 Icon Đỏ ]                              │
│                                                                        │
│                      TRUY CẬP BỊ TỪ CHỐI (403)                         │
│   Tài khoản của bạn (Thu ngân) không có quyền xem Báo cáo doanh thu.   │
│   Vui lòng liên hệ Chủ cửa hàng (Owner) để được cấp thêm quyền.        │
│                                                                        │
│               [ Về màn hình POS ]    [ Đăng xuất ]                     │
│                                                                        │
└────────────────────────────────────────────────────────────────────────┘
```

* **Mã màu áp dụng:** Nền icon cảnh báo sử dụng `--danger-soft: #fff0f1`, viền `--danger: #c93c48` (Xem [`DOC-ERR-03`](../12-error-and-status-codes/03-color-system-tokens.md)).
* **Mã lỗi liên quan:** `FORBIDDEN_ACCESS` (Xem [`DOC-ERR-02`](../12-error-and-status-codes/02-error-codes-catalog.md)).

---

## 3. Màn Hình Hết Hạn Phiên Làm Việc (`/session-expired` - Session Timeout)

### 3.1. Mục Tiêu & Luồng Nghiệp Vụ
Kích hoạt tự động khi Access Token và Refresh Token đều đã hết hạn hoặc bị hệ thống thu hồi (Revoke), đảm bảo tính an toàn dữ liệu trên các máy tính dùng chung tại quầy.

### 3.2. Bố Cục Giao Diện Wireframe
```text
┌────────────────────────────────────────────────────────────────────────┐
│                                                                        │
│                            [ ⏳ Icon Cam ]                             │
│                                                                        │
│                    PHIÊN LÀM VIỆC ĐÃ HẾT HẠN                           │
│   Để đảm bảo bảo mật thông tin bán hàng, phiên của bạn đã tự khóa.     │
│   Vui lòng đăng nhập lại để tiếp tục công việc đang dang dở.            │
│                                                                        │
│                         [ ĐĂNG NHẬP LẠI ]                              │
│                                                                        │
└────────────────────────────────────────────────────────────────────────┘
```

* **Mã màu áp dụng:** Icon cảnh báo sử dụng `--warning: #b76e00`, nền `--warning-soft: #fff5dc` (Xem [`DOC-ERR-03`](../12-error-and-status-codes/03-color-system-tokens.md)).
* **Mã lỗi liên quan:** `AUTH_TOKEN_EXPIRED` (Xem [`DOC-ERR-02`](../12-error-and-status-codes/02-error-codes-catalog.md)).
