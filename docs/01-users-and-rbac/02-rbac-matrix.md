# 02. Ma Trận Phân Quyền Theo Vai Trò (Role-Based Access Control Matrix)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-USR-02` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-AUTH-02`, `FR-AUTH-03`, `FR-AUTH-04`, `NFR-SEC-01` (Trang 1, 4) |
| **Phạm vi áp dụng** | Middleware Ủy quyền Backend (`[Authorize(Roles = ...)]`) & Route Guard trên Web Admin / POS |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Thiết lập ma trận phân quyền chi tiết cấp API và Màn hình cho 3 vai trò `Owner`, `Staff`, `Cashier`. | `INIT-USR-02` |

---

## 1. Cấu Trúc Claims Trong JWT Token (`FR-AUTH-02`, `NFR-SEC-01`)

Khi người dùng đăng nhập thành công qua Email/SĐT + Mật khẩu, hệ thống phát hành cặp Token gồm:
- **Access Token (JWT):** Thời hạn hiệu lực **60 phút**, bắt buộc đính kèm trong header `Authorization: Bearer <token>`.
- **Refresh Token:** Lưu trữ bảo mật trong cơ sở dữ liệu, hỗ trợ cơ chế luân chuyển nguyên tử (`Token Rotation`) và thu hồi (`Revocation`).

Các trường định danh bắt buộc bên trong Payload của JWT:
- `sub` / `UserId`: Định danh duy nhất của người dùng.
- `tenant_id` / `TenantId`: Định danh cửa hàng dùng để kích hoạt **EF Core Global Query Filter** cô lập dữ liệu.
- `role` / `Role`: Một trong 3 giá trị chuẩn: `Owner`, `Staff`, hoặc `Cashier`.

---

## 2. Ma Trận Phân Quyền Chi Tiết Cấp Nghiệp Vụ & API (`FR-AUTH-03`)

**Chú giải ký hiệu:**
- `✅ Cho phép (200/201)`: Được phép thực thi đầy đủ trên cả giao diện và API.
- `👁️ Chỉ xem (Read-Only)`: Chỉ được gọi các endpoint `GET` tra cứu phục vụ công việc tại quầy/kho.
- `❌ Cấm (403 Forbidden)`: Bị chặn ở tầng API Controller bằng `[Authorize(Roles = ...)]` và ẩn/chặn điều hướng trên Frontend.

| Phân hệ | Chức năng / Nghiệp vụ | Mã FR | `Owner` (Chủ cửa hàng) | `Staff` (Quản lý Kho/Đơn) | `Cashier` (Thu ngân POS) |
| :---: | :--- | :---: | :---: | :---: | :---: |
| **Xác thực & Tổ chức** | Đăng ký Tenant mới (`Register Tenant`) | `FR-AUTH-01` | ✅ Khởi tạo | ❌ Cấm | ❌ Cấm |
| **Xác thực & Tổ chức** | Đăng nhập, Đăng xuất, Làm mới Token, Xem hồ sơ (`/me`) | `FR-AUTH-02` | ✅ Cho phép | ✅ Cho phép | ✅ Cho phép |
| **Xác thực & Tổ chức** | Tạo, cập nhật, bật/tắt Chi nhánh (`Branch`) & Kho (`Warehouse`) | `FR-AUTH-04` | ✅ Toàn quyền | 👁️ Chỉ xem danh sách | 👁️ Chỉ xem chi nhánh POS |
| **Danh mục & Sản phẩm** | Thêm, sửa, xóa Danh mục phân cấp (`Category`) & Thương hiệu (`Brand`) | `FR-PROD-01` | ✅ Toàn quyền | ✅ Toàn quyền | 👁️ Chỉ xem |
| **Danh mục & Sản phẩm** | Tạo, cập nhật Sản phẩm (`Product`) & Biến thể (`SKU`) | `FR-PROD-02` | ✅ Toàn quyền | ✅ Toàn quyền | 👁️ Chỉ tra cứu POS |
| **Danh mục & Sản phẩm** | Sinh tự động / Cập nhật Mã vạch (`EAN-13`/`Code128`) | `FR-PROD-03` | ✅ Toàn quyền | ✅ Toàn quyền | 👁️ Chỉ quét mã |
| **Danh mục & Sản phẩm** | Thiết lập / Cập nhật Giá bán lẻ & Giá bán sỉ theo SKU | `FR-PROD-04` | ✅ Toàn quyền | ✅ Toàn quyền | 👁️ Chỉ đọc giá bán |
| **Kho & Sổ cái** | Xem lịch sử Sổ cái kho (`InventoryTransaction`) & Số dư tồn kho | `FR-INV-01` | ✅ Toàn quyền | ✅ Toàn quyền | ❌ Cấm (Chỉ xem `available` tại POS) |
| **Kho & Sổ cái** | Tạo & Xác nhận Phiếu nhập hàng từ Nhà cung cấp (`Goods Receipt`) | `FR-INV-02` | ✅ Toàn quyền | ✅ Toàn quyền | ❌ Cấm (`403 Forbidden`) |
| **Kho & Sổ cái** | Thực hiện Chuyển kho 2 bước (Xuất đi & Xác nhận nhập) | `FR-INV-03` | ✅ Toàn quyền | ✅ Toàn quyền | ❌ Cấm (`403 Forbidden`) |
| **Kho & Sổ cái** | Tạo phiên Kiểm kê vật lý & Sinh bút toán điều chỉnh cân bằng kho | `FR-INV-04` | ✅ Toàn quyền | ✅ Toàn quyền | ❌ Cấm (`403 Forbidden`) |
| **Đơn hàng & Giữ chỗ** | Xem danh sách đơn đa kênh, duyệt đơn (`Confirm`), in phiếu giao, hủy đơn | `FR-ORD-03` | ✅ Toàn quyền | ✅ Toàn quyền | ❌ Cấm quản lý đơn Admin |
| **Đơn hàng & Giữ chỗ** | Gọi API Giữ chỗ tồn kho (`Reserve Stock`) | `FR-RSE-02` | ✅ Cho phép | ✅ Cho phép | ✅ Cho phép (trong luồng POS) |
| **Bán hàng tại quầy** | Tra cứu nhanh SKU, quét Barcode tại giao diện PWA POS | `FR-POS-01` | ✅ Cho phép | ✅ Cho phép | ✅ Toàn quyền |
| **Bán hàng tại quầy** | Thanh toán nhanh nguyên tử tại quầy POS (`Fast Checkout`) & In hóa đơn | `FR-POS-03..04` | ✅ Cho phép | ✅ Cho phép | ✅ Toàn quyền |
| **Báo cáo & AI** | Báo cáo Doanh thu thuần, Tổng giá vốn (`COGS`), Lợi nhuận gộp | `FR-REP-01` | ✅ Toàn quyền | ❌ Cấm xem lãi gộp | ❌ Cấm (`403 Forbidden`) |
| **Báo cáo & AI** | Báo cáo Giá trị tồn kho, Hàng bán chạy/chậm & Cảnh báo tồn tối thiểu | `FR-REP-02..03` | ✅ Toàn quyền | ✅ Xem cảnh báo kho | ❌ Cấm (`403 Forbidden`) |
| **Báo cáo & AI** | Xem dự báo nhu cầu AI & Gợi ý nhập lại hàng | `FR-AI-01` | ✅ Toàn quyền | ✅ Xem gợi ý nhập | ❌ Cấm (`403 Forbidden`) |

---

## 3. Cơ Chế Kiểm Soát Hai Lớp (Dual-Layer Enforcement)

1. **Tầng Giao diện (Frontend Route Guard & Unified Login):**
   - Sau khi xác thực JWT, hệ thống đọc trường `Role`:
     - `Cashier`: Tự động điều hướng thẳng về giao diện quầy bán hàng (`/pos`). Nếu cố tình truy cập đường dẫn `/admin/*`, ứng dụng chuyển hướng ngay sang màn hình `/403` (Forbidden).
     - `Staff` & `Owner`: Điều hướng vào không gian vận hành và quản trị (`/admin`), tự động ẩn các nút chức năng vượt quá thẩm quyền của `Staff`.
2. **Tầng Backend API (Server-side Policy):**
   - Mọi Controller/Action đều gắn thuộc tính `[Authorize(Roles = "...")]`.
   - Ngay cả khi người dùng dùng Postman/cURL gửi trực tiếp JWT của `Cashier` vào các API quản trị sản phẩm hoặc nhập kho, Backend sẽ trả về mã lỗi chuẩn `HTTP 403 Forbidden`.
