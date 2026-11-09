# 02. Ma Trận Phân Quyền Theo Vai Trò (Role-Based Access Control Matrix)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-USR-02` |
| **Phiên bản (Version)** | `v1.2.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-06` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-AUTH-02`, `FR-AUTH-03`, `FR-AUTH-04`, `NFR-SEC-01` (Trang 1, 4) |
| **Phạm vi áp dụng** | Middleware Ủy quyền Backend (`[Authorize(Roles = ...)]`) & Route Guard trên Web Admin / POS |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Thiết lập ma trận phân quyền chi tiết cấp API và Màn hình cho 3 vai trò `Owner`, `Staff`, `Cashier`. | `INIT-USR-02` |
| `v1.1.0` | `2026-11-03` | Nhóm dự án OISMS | Cập nhật phân quyền lịch kiểm kê, kỳ kiểm kê, nộp duyệt, đếm lại và chốt kiểm kê (OISMS-47-U03). | `OISMS-47-U03` |
| `v1.2.0` | `2026-11-06` | Nhóm dự án OISMS | Sửa tên claim JWT theo `JwtTokenService`; thêm dòng Quản lý người dùng; thêm mục Lệch giữa yêu cầu và code (báo cáo lãi gộp, giá vốn trong tồn kho cho Cashier, quyền kho của Staff, quyền xóa danh mục). | `OISMS-46` |
| `v1.3.0` | `2026-11-09` | Nhóm dự án OISMS | Bổ sung phân quyền Trả hàng từ khách (CS-01); chuẩn hóa bảo mật báo cáo: Staff bị ẩn toàn bộ giá vốn và lãi gộp (`null`) theo ma trận. | `OISMS-58-CS01-P2` |

---

## 1. Cấu Trúc Claims Trong JWT Token (`FR-AUTH-02`, `NFR-SEC-01`)

Khi người dùng đăng nhập thành công qua Email/SĐT + Mật khẩu, hệ thống phát hành cặp Token gồm:
- **Access Token (JWT):** Thời hạn hiệu lực **60 phút**, bắt buộc đính kèm trong header `Authorization: Bearer <token>`.
- **Refresh Token:** Lưu trữ bảo mật trong cơ sở dữ liệu, hỗ trợ cơ chế luân chuyển nguyên tử (`Token Rotation`) và thu hồi (`Revocation`).

Các trường định danh bắt buộc bên trong Payload của JWT:
- `sub` và `userId`: Định danh duy nhất của người dùng.
- `tenantId`: Định danh cửa hàng, `CurrentTenantService` đọc claim này để kích hoạt **EF Core Global Query Filter** cô lập dữ liệu.
- `role` và `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` (`ClaimTypes.Role`): Một trong 3 giá trị chuẩn `Owner`, `Staff`, `Cashier`; `[Authorize(Roles=...)]` đọc claim thứ hai.
- `email`, `name`, `jti`: Email, họ tên, mã token.
- Ký bằng HMAC-SHA256 với `Jwt:Secret`, kiểm tra `Jwt:Issuer`/`Jwt:Audience`, `ClockSkew = 0`.

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
| **Xác thực & Tổ chức** | Quản lý người dùng (`/api/users`): tạo, sửa, khóa/mở tài khoản | `CS-02` | ✅ Toàn quyền | ❌ Cấm (`403 Forbidden`) | ❌ Cấm (`403 Forbidden`) |
| **Danh mục & Sản phẩm** | Thêm, sửa, xóa Danh mục phân cấp (`Category`) & Thương hiệu (`Brand`) | `FR-PROD-01` | ✅ Toàn quyền | ✅ Toàn quyền | 👁️ Chỉ xem |
| **Danh mục & Sản phẩm** | Tạo, cập nhật Sản phẩm (`Product`) & Biến thể (`SKU`) | `FR-PROD-02` | ✅ Toàn quyền | ✅ Toàn quyền | 👁️ Chỉ tra cứu POS |
| **Danh mục & Sản phẩm** | Sinh tự động / Cập nhật Mã vạch (`EAN-13`/`Code128`) | `FR-PROD-03` | ✅ Toàn quyền | ✅ Toàn quyền | 👁️ Chỉ quét mã |
| **Danh mục & Sản phẩm** | Tạo phiếu đổi giá, xem phiếu và lịch sử giá | `FR-PROD-04`, `OISMS-47-U04` | ✅ Phiếu được duyệt ngay | ✅ Phiếu chờ `Owner` duyệt | ❌ Cấm (`403 Forbidden`) |
| **Danh mục & Sản phẩm** | Duyệt, từ chối phiếu đổi giá; hủy giá đã lên lịch | `OISMS-47-U04` | ✅ Toàn quyền | ❌ Cấm (`403 Forbidden`) | ❌ Cấm (`403 Forbidden`) |
| **Danh mục & Sản phẩm** | Đọc bảng giá và giá hiệu lực | `OISMS-47-U04` | ✅ Mọi thời điểm | ✅ Mọi thời điểm | 👁️ Chỉ giá hiện tại |
| **Danh mục & Sản phẩm** | Quản lý Đơn vị tính (`Unit`) & Quy cách SKU (`SkuUnit`) | `OISMS-47-U05` | ✅ Toàn quyền | ✅ Toàn quyền | 👁️ Chỉ xem (`GET`), trừ lịch sử hệ số |
| **Kho & Sổ cái** | Xem lịch sử Sổ cái kho (`InventoryTransaction`) & Số dư tồn kho | `FR-INV-01` | ✅ Toàn quyền | ✅ Toàn quyền | ❌ Cấm (Chỉ xem `available` tại POS) |
| **Kho & Sổ cái** | Tạo, Xác nhận & Hủy nháp Phiếu nhập hàng từ Nhà cung cấp (`Goods Receipt`) | `FR-INV-02`, `OISMS-47-U01A` | ✅ Toàn quyền | ✅ Toàn quyền | ❌ Cấm (`403 Forbidden`) |
| **Kho & Sổ cái** | Xem, lập và sửa nháp Phiếu mua hàng (`Purchase Order`) | `OISMS-47-U01A` | ✅ Toàn quyền | ✅ Toàn quyền | ❌ Cấm (`403 Forbidden`) |
| **Kho & Sổ cái** | Duyệt, đóng và hủy Phiếu mua hàng | `OISMS-47-U01A` | ✅ Toàn quyền | ❌ Cấm (`403 Forbidden`) | ❌ Cấm (`403 Forbidden`) |
| **Kho & Sổ cái** | Thực hiện Chuyển kho 2 bước (Xuất đi & Xác nhận nhập) | `FR-INV-03` | ✅ Toàn quyền | ✅ Toàn quyền | ❌ Cấm (`403 Forbidden`) |
| **Kho & Sổ cái** | Thiết lập cấu hình Lịch kiểm kê định kỳ (`StockCountSchedule`) | `OISMS-47-U03` | ✅ Toàn quyền (`POST`, `PUT`) | 👁️ Chỉ xem (`GET`) | ❌ Cấm (`403 Forbidden`) |
| **Kho & Sổ cái** | Xem danh sách Kỳ kiểm kê & Bắt đầu đếm kỳ (`StockCountPeriod`) | `OISMS-47-U03` | ✅ Toàn quyền | ✅ Toàn quyền | ❌ Cấm (`403 Forbidden`) |
| **Kho & Sổ cái** | Lập phiếu kiểm kê, Ghi số đếm (`Record`), Đếm lại (`Recount`), Nộp duyệt (`Submit`) | `FR-INV-04`, `OISMS-47-U03` | ✅ Toàn quyền | ✅ Toàn quyền | ❌ Cấm (`403 Forbidden`) |
| **Kho & Sổ cái** | Yêu cầu đếm lại (`RequestRecount`) & Phê duyệt chốt kiểm kê (`Confirm`) cân bằng kho | `FR-INV-04`, `OISMS-47-U03` | ✅ Toàn quyền (chặn tự duyệt khi có Owner khác) | ❌ Cấm (`403 Forbidden`) | ❌ Cấm (`403 Forbidden`) |
| **Đơn hàng & Giữ chỗ** | Xem danh sách đơn đa kênh, duyệt đơn (`Confirm`), in phiếu giao, hủy đơn | `FR-ORD-03` | ✅ Toàn quyền | ✅ Toàn quyền | ❌ Cấm quản lý đơn Admin |
| **Đơn hàng & Giữ chỗ** | Gọi API Giữ chỗ tồn kho (`Reserve Stock`) | `FR-RSE-02` | ✅ Cho phép | ✅ Cho phép | ✅ Cho phép (trong luồng POS) |
| **Bán hàng tại quầy** | Tra cứu nhanh SKU, quét Barcode tại giao diện PWA POS | `FR-POS-01` | ✅ Cho phép | ✅ Cho phép | ✅ Toàn quyền |
| **Bán hàng tại quầy** | Thanh toán nhanh nguyên tử tại quầy POS (`Fast Checkout`) & In hóa đơn | `FR-POS-03..04` | ✅ Cho phép | ✅ Cho phép | ✅ Toàn quyền |
| **Hóa đơn & Truy vết** | Tra cứu danh sách và chi tiết hóa đơn bán hàng (`Sales Invoices`) | `OISMS-47-U02` | ✅ Toàn quyền | ✅ Toàn quyền | ❌ Cấm (`403 Forbidden`) |
| **Hóa đơn & Truy vết** | In lại hóa đơn bán lẻ theo ID hoặc số hóa đơn (dữ liệu che giấu giá vốn) | `OISMS-47-U02` | ✅ Cho phép | ✅ Cho phép | ✅ Cho phép (in lại tại POS) |
| **Hóa đơn & Truy vết** | Truy vết chuỗi chứng từ xuất kho theo Hóa đơn, Đơn hàng, hoặc Dòng sổ kho | `OISMS-47-U02` | ✅ Toàn quyền | ✅ Toàn quyền (xem giá vốn dòng) | ❌ Cấm (`403 Forbidden`) |
| **Trả hàng từ khách** | Lập phiếu trả, tra cứu dòng trả được, xác nhận và hủy phiếu trả (`/api/sales-returns`, `/api/orders/{id}/returnable-items`) | `CS-01` | ✅ Toàn quyền | ✅ Toàn quyền | ❌ Cấm (`403 Forbidden`) |
| **Báo cáo & AI** | Báo cáo Doanh thu thuần, Tổng giá vốn (`COGS`), Lợi nhuận gộp | `FR-REP-01` | ✅ Toàn quyền | 👁️ Chỉ xem doanh thu/hàng trả (ẩn lãi gộp & giá vốn) | ❌ Cấm (`403 Forbidden`) |
| **Báo cáo & AI** | Báo cáo Giá trị tồn kho, Hàng bán chạy/chậm & Cảnh báo tồn tối thiểu | `FR-REP-02..03` | ✅ Toàn quyền | ✅ Xem cảnh báo kho | ❌ Cấm (`403 Forbidden`) |
| **Báo cáo & AI** | Xem dự báo nhu cầu AI & Gợi ý nhập lại hàng | `FR-AI-01` | ✅ Toàn quyền | ✅ Xem gợi ý nhập | ❌ Cấm (`403 Forbidden`) |
| **Cấu hình & Thanh toán** | Quản lý tài khoản ngân hàng thụ hưởng (`/api/payment-accounts`): thêm, sửa, đặt mặc định, ngừng dùng | `CS-05` | ✅ Toàn quyền | 👁️ Chỉ xem tài khoản active | 👁️ Chỉ xem tài khoản active |

> **Từ Upgrade 04:** không còn đường sửa giá trực tiếp, kể cả với `Owner`. Giá ban đầu trên form tạo SKU hoặc đơn vị cũng đi qua phiếu: của `Owner` có hiệu lực ngay, của `Staff` chờ duyệt. Vai trò lấy từ JWT, client không tự khai được. Xem [tài liệu 04](../Upgrade-recommended/04-bang-gia-theo-thoi-gian.md#phân-quyền).

---

## 2.1. Lệch Giữa Yêu Cầu Và Code Hiện Tại (đối chiếu 06/11/2026)

Bảng trên là **yêu cầu**. Đối chiếu `[Authorize(Roles = ...)]` và dữ liệu trả về trong code thấy các điểm lệch sau; cần nhóm quyết định sửa code hay sửa yêu cầu.

| Chức năng | Yêu cầu | Code hiện tại | Mức độ |
| :--- | :--- | :--- | :--- |
| `GET /api/reports/revenue-profit` | Chỉ `Owner` xem lãi gộp | **Đã sửa tại CS-01 P2 (09/11/2026):** Service kiểm tra vai trò `Owner`, `Staff` chỉ nhận doanh thu và hàng trả; các trường giá vốn (`costOfGoodsSold`, `returnCogs`, `netCogs`) và lãi gộp (`grossProfit`, `grossMarginPercentage`) đều trả `null`. | Đã xử lý |
| `GET /api/inventory/balances` | `Cashier` không xem sổ cái/số dư chi tiết, không thấy giá vốn | Cho `Cashier` gọi và trả cả `averageCost` (WAC). | Cao (lộ giá vốn) |
| Kho (`POST/PUT /api/warehouses`, `toggle-status`) | `Staff` chỉ xem | `Staff` được tạo, sửa, bật/tắt kho. Chi nhánh thì đúng (chỉ `Owner`). | Trung bình |
| Xóa danh mục, thương hiệu, sản phẩm | `Staff` toàn quyền | Chỉ `Owner` (`DELETE` gắn `Roles = "Owner"`) | Thấp (code chặt hơn) |
| `GET /api/warehouses`, `/api/branches/{id}`, `/api/suppliers` | Không ghi rõ cho `Cashier` | Mọi vai trò đã đăng nhập đều đọc được | Thấp |

## 3. Cơ Chế Kiểm Soát Hai Lớp (Dual-Layer Enforcement)

1. **Tầng Giao diện (Frontend Route Guard & Unified Login):**
   - Sau khi xác thực JWT, hệ thống đọc trường `Role`:
     - `Cashier`: Bán hàng tại ứng dụng POS riêng (`VITE_POS_URL`, local cổng 5174). Trang đăng nhập Admin có nút "Bạn là thu ngân? Đăng nhập tại quầy POS" mở thẳng màn đăng nhập POS. Nếu Cashier vẫn đăng nhập ở Admin, phiên Admin bị thu hồi và ứng dụng chuyển sang màn đăng nhập POS kèm dòng nhắc; hai ứng dụng không chia sẻ token. Nếu cố tình truy cập đường dẫn `/admin/*`, ứng dụng chuyển hướng sang `/403`.
     - `Staff` & `Owner`: Điều hướng vào `/admin`. Trang Báo cáo (`/reports`) và Tổ chức (`/organization`) chỉ mở cho `Owner` (`ProtectedRoute allowedRoles={["Owner"]}`); các nút vượt quyền của `Staff` bị ẩn.
2. **Tầng Backend API (Server-side Policy):**
   - Mọi Controller/Action đều gắn thuộc tính `[Authorize(Roles = "...")]`.
   - Ngay cả khi người dùng dùng Postman/cURL gửi trực tiếp JWT của `Cashier` vào các API quản trị sản phẩm hoặc nhập kho, Backend sẽ trả về mã lỗi chuẩn `HTTP 403 Forbidden`.
