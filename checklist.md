# CHECKLIST TIẾN ĐỘ ĐỒ ÁN OISMS & NGHIỆM THU SPRINT 2 (ĐỒNG BỘ CONFLUENCE)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-CHK-S2` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày cập nhật** | `2026-10-22` |
| **Nguồn truy vết gốc** | `C:\Users\Jacki Khoa\Documents\De bai CNPM.pdf` (Trang 1 – 6) & Confluence Page ID `5275650` (Version 9) |
| **Bằng chứng kiểm thử Sprint 2** | **194/194 Tests Pass (100%)** — 103 Unit Tests + 91 Integration Tests trên PostgreSQL 16+ thật; Code Coverage Core Inventory đạt **`92.49% – 94.83%`** |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả nội dung & Chi tiết rà soát thay đổi (Change Check) | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | • Tổng hợp toàn bộ yêu cầu đề bài (`28 FR + AI`, `11 NFR`, `5 Task Packages`, `6 Deliverables`) và đánh dấu nghiệm thu `[x]` các hạng mục đã hoàn thành trong Sprint 2.<br/>• **Check thay đổi khi đối chiếu Confluence (`Page ID: 5275650`, Version 9):** Đồng bộ chính xác lịch trình (`S2: 13–17/10`, `S3: 18–22/10`, `S4: 23–27/10`, `S5: 28/10–01/11`) và phân công 5 thành viên cho các hạng mục chưa hoàn thành `[ ]`. | `INIT-SYNC-CHK-S2` |

---

## 1. Checklist Yêu Cầu Chức Năng (Functional Requirements — `FR-*`)

### 1.1. Quản lý Đa khách thuê, Xác thực & Phân quyền (`FR-AUTH`) — *Phụ trách: Nhựt (TV2) & Đăng Khoa*
- [x] **`FR-AUTH-01` (Tenant Registration — Hoàn thành Sprint 2):** Đăng ký cửa hàng mới (`POST /api/auth/register-tenant`), tạo không gian dữ liệu cô lập với `TenantId` riêng, khởi tạo nguyên tử Chi nhánh/Kho mặc định và Owner; chuẩn hóa số điện thoại `E.164` (`PhoneHelper.cs`).
- [x] **`FR-AUTH-02` (User Authentication — Hoàn thành Sprint 2):** Đăng nhập/Đăng xuất qua Email hoặc SĐT + Mật khẩu (`POST /api/auth/login`, `/logout`, `/refresh-token`, `/me`); trả về JWT chứa `TenantId`, `UserId`, `Role`; xoay vòng Refresh Token nguyên tử với khóa dòng `FOR UPDATE` và chặn Tenant/User bị khóa (`IsActive = false`).
- [x] **`FR-AUTH-03` (Role-Based Access Control - RBAC — Hoàn thành Sprint 2):** Phân quyền chặt chẽ 3 vai trò `Owner` (Toàn quyền), `Staff` (Kho/Đơn hàng), `Cashier` (Chỉ POS) trên cả Backend API (`[Authorize(Roles)]`) và cổng đăng nhập tập trung Frontend (`Smart RBAC` + chặn trang `/403`).
- [x] **`FR-AUTH-04` (Branch & Warehouse Management — Hoàn thành Sprint 2):** Cho phép `Owner` tạo, cập nhật, bật/tắt trạng thái hiển thị (`IsActive`) của Chi nhánh (`/api/branches`) và Kho hàng (`/api/warehouses`); chặn đổi chi nhánh đối với kho đã có lịch sử giao dịch sổ cái (`409 Conflict`).

### 1.2. Quản lý Danh mục, Sản phẩm, Biến thể SKU, Mã vạch & Giá (`FR-PROD`) — *Phụ trách: Nhựt (TV2), Phúc Hữu (TV3) & TV5*
- [x] **`FR-PROD-01` (Category & Brand Management — Hoàn thành Sprint 2):** Quản lý cây danh mục phân cấp cha - con (`/api/categories`) và thương hiệu (`/api/brands`); bảo vệ chống tạo vòng lặp chu trình bằng `PostgreSQL Advisory Lock` và thuật toán duyệt tổ tiên.
- [x] **`FR-PROD-02` (Product & Variant/SKU Management — Hoàn thành Sprint 2):** Hỗ trợ sản phẩm đơn giản và đa biến thể (Màu sắc, Kích thước, JSON Attributes giữ nguyên kiểu dữ liệu và `null` lồng nhau); đảm bảo mã `SKU` duy nhất trong cùng một Tenant (`uq_skus_tenant_sku`).
- [x] **`FR-PROD-03` (Barcode Management — Hoàn thành Sprint 2):** Tự động sinh mã vạch chuẩn `EAN-13` (có cơ chế retry 10 lần chống trùng) hoặc nhập thủ công `EAN-13`/`Code128` cho từng SKU; bảo vệ ràng buộc duy nhất theo Tenant (`uq_skus_3`).
- [x] **`FR-PROD-04` (Listed Price Management — Hoàn thành Sprint 2):** Thiết lập và cập nhật Giá bán lẻ (`RetailPrice`) và Giá bán sỉ (`WholesalePrice`) theo từng SKU (`PUT /api/products/{pId}/variants/{vId}/prices`), tách biệt hoàn toàn với giá vốn (`UnitCost`).

### 1.3. Sổ cái tồn kho & Nghiệp vụ kho (`FR-INV`) — *Phụ trách: Đăng Khoa, Phúc Hữu (TV3) & TV5*
- [x] **`FR-INV-01` (Append-Only Inventory Ledger — Hoàn thành Sprint 2):** Ghi nhận nghiêm ngặt biến động kho vào bảng sổ cái chỉ ghi thêm `InventoryTransaction` (`TenantId`, `BranchId`, `WarehouseId`, `SKUId`, `Type (IN/OUT)`, `Quantity`, `BalanceAfter`, `ReferenceId`, `CreatedAt`); cấm tuyệt đối sửa trực tiếp cột `on_hand` hoặc `UPDATE`/`DELETE` sổ cái; hoàn thiện giao diện tra cứu Sổ cái trên Web Admin.
- [x] **`FR-INV-02` (Purchase Receipt Management — Hoàn thành Sprint 2):** Lập phiếu nhập hàng đa dòng từ Nhà cung cấp (`Draft`) và xác nhận nhập kho nguyên tử (`POST /api/inventory/goods-receipts/{id}/confirm`) $\rightarrow$ Tăng `on_hand` $\rightarrow$ Ghi sổ cái `IN` $\rightarrow$ Cập nhật lại giá vốn bình quân (`WAC`). Hoàn thiện toàn diện cả Backend và giao diện Web Admin.
- [ ] **`FR-INV-03` (Stock Transfer Management — Kế hoạch Sprint 3: `18/10 – 22/10`):** Quy trình chuyển kho 2 bước giữa Chi nhánh A và Chi nhánh B (`Out-bound transit -> In-bound confirmation`) do **Đăng Khoa (BE1)** và **Phúc Hữu (TV3)** thực hiện.
- [ ] **`FR-INV-04` (Stocktake Management — Kế hoạch Sprint 3: `18/10 – 22/10`):** Tạo phiên kiểm kê vật lý, đối chiếu tồn phần mềm và sinh bút toán điều chỉnh cân bằng kho do **Đăng Khoa (BE1)** và **Phúc Hữu (TV3)** thực hiện.

### 1.4. Tính Giá vốn bình quân gia quyền (`FR-COST`) — *Phụ trách: Đăng Khoa*
- [x] **`FR-COST-01` (Automated Batch COGS Calculation — Hoàn thành Sprint 2):** Tự động tính lại giá vốn đơn vị của SKU ngay khi xác nhận Phiếu nhập kho theo công thức bình quân gia quyền: $\text{NewCost} = \frac{(\text{OldStock} \times \text{OldCost}) + (\text{ImportQty} \times \text{ImportPrice})}{\text{OldStock} + \text{ImportQty}}$.
- [ ] **`FR-COST-02` (COGS Snapshot on Dispatch — Nền Sprint 2, Hoàn thiện đủ ở Sprint 3: `18/10 – 22/10`):** Chụp chính xác giá vốn bình quân đơn vị hiện hành vào `OrderItem.CostPrice` khi đơn hàng chuyển sang `Confirmed` hoặc thanh toán tại quầy POS.

### 1.5. Cơ chế Giữ chỗ & Chống bán vượt (`FR-RSE`) — *Phụ trách: Đăng Khoa (Trưởng nhóm)*
- [x] **`FR-RSE-01` (Available Stock Calculation — Hoàn thành Sprint 2):** Duy trì động tồn khả dụng theo SKU tại từng chi nhánh/kho (`available = on_hand - reserved`) trên toàn bộ API tồn kho và API tra cứu POS.
- [x] **`FR-RSE-02` (Stock Reservation Execution — Hoàn thành nền tảng ở Sprint 2):** Hiện thực `ReservationService.cs` và endpoint `POST /api/inventory/reserve`: tăng `reserved` khi giữ chỗ, từ chối bằng `409 Conflict` nếu `available < Quantity`, kiểm tra `IsActive` 4 cấp (Branch/Warehouse/Product/SKU) và chống giữ chỗ lặp bằng `Idempotency-Key` / `RequestKey` + `PostgreSQL Advisory Lock`.
- [ ] **`FR-RSE-03` (Deduct/Release Execution — Nền Sprint 2, Hoàn thiện đủ ở Sprint 3: `18/10 – 22/10`):** Trừ `on_hand` & giảm `reserved` + ghi Sổ cái khi đơn `Confirmed`; giải phóng `reserved` về lại `available` khi đơn `Cancelled` hoặc hết hạn.

### 1.6. Trung tâm Đơn hàng đa kênh (`FR-ORD`) — *Phụ trách: Đăng Khoa, Nhựt (TV2) & Phúc Hữu (TV3)*
- [x] **`FR-ORD-01` (Canonical Order Model Foundation — Hoàn thành nền tảng CSDL & Entity ở Sprint 2):** Đã thiết kế và khởi tạo bảng `orders`, `order_items` chuẩn hóa đa kênh và hỗ trợ khởi tạo đơn nháp trong luồng giữ chỗ.
- [ ] **`FR-ORD-01..03` (Full Order Hub, State Machine & Order Processing — Hoàn thiện đủ ở Sprint 3: `18/10 – 22/10`):**
  - **Đăng Khoa:** Xây dựng máy trạng thái đơn hàng (`Draft -> Reserved -> Confirmed -> Completed / Cancelled`) và lõi tiếp nhận Webhook đa kênh vào `Canonical Order Model`.
  - **Nhựt (TV2):** Xây dựng API `list / detail / filter` đơn hàng và chứng từ kho.
  - **Phúc Hữu (TV3):** Xây dựng form tạo đơn trên Admin, duyệt đơn online, in phiếu giao hàng và hủy đơn.

### 1.7. Bán hàng tại quầy PWA POS (`FR-POS`) — *Phụ trách: Phương (TV4) & Đăng Khoa*
- [x] **`FR-POS-01` (Fast Sales Interface — Hoàn thành Sprint 2):** Hoàn thiện ứng dụng `src/frontend/pos-app` (React 19 PWA) tối ưu cảm ứng và phím tắt, kết nối API thật tìm kiếm sản phẩm theo Tên, mã SKU (`/api/pos/search`) và quét mã vạch (`/api/pos/barcode/{code}`).
- [x] **`FR-POS-02` (Counter Sales Constraint — Hoàn thành Sprint 2):** Kiểm tra tức thì số lượng `available` khi chọn hàng vào giỏ, chặn tuyệt đối việc thêm/tăng số lượng vượt quá tồn khả dụng để bảo vệ phần hàng đã giữ chỗ cho đơn online.
- [x] **`FR-POS-03` (Receipt Printing & Payment UI — Hoàn thành giao diện & in hóa đơn ở Sprint 2):** Hỗ trợ chọn phương thức thanh toán Tiền mặt (tự tính tiền thừa) / QR Code và in hóa đơn nhiệt khổ `80mm`/`58mm` qua CSS `@media print` và `window.print()`.
- [ ] **`FR-POS-04` (Fast Checkout Atomic Workflow — Hoàn thiện đủ ở Sprint 3: `18/10 – 22/10` & Tối ưu ở Sprint 4):** Kết nối nút Thanh toán POS với API giao dịch toàn giỏ nguyên tử (`Reserve -> Confirm -> Deduct`), xử lý idempotency chống bấm đúp và hoàn thiện trải nghiệm PWA/QR ở Sprint 4.

### 1.8. Giả lập Webhook, Thời gian thực SignalR & Tác vụ nền Hangfire (`FR-SIM`) — *Phụ trách: Đăng Khoa & Nhựt (TV2)*
- [ ] **`FR-SIM-01` (E-commerce Webhook Simulator — Kế hoạch Sprint 3: `18/10 – 22/10`):** **Nhựt (TV2)** xây dựng công cụ simulator gửi payload 3 kênh (`Shopee`, `TikTok`, `Lazada`) qua endpoint tiếp nhận của **Đăng Khoa**.
- [ ] **`FR-SIM-02` (Real-time Push Notification SignalR — Kế hoạch Sprint 3: `18/10 – 22/10`):** **Đăng Khoa** cấu hình SignalR events; **Phúc Hữu (TV3)** và **Phương (TV4)** nối giao diện nhận popup thông báo và âm thanh chuông đón đơn mới.
- [ ] **`FR-SIM-03` (Background Processing Hangfire — Kế hoạch Sprint 3 & Sprint 4):**
  - **Sprint 3 (`18/10 – 22/10`):** **Đăng Khoa** cấu hình Hangfire tự động hủy đơn `Reserved` quá hạn (`auto-canceling expired reserved orders`).
  - **Sprint 4 (`23/10 – 27/10`):** **Đăng Khoa** cấu hình Hangfire tổng hợp báo cáo hằng ngày (`daily report aggregations`).

### 1.9. Báo cáo & Cảnh báo tồn kho (`FR-REP`) — *Phụ trách: Nhựt (TV2), Phúc Hữu (TV3) & Đăng Khoa*
- [ ] **`FR-REP-01..03` (Báo cáo cơ bản & Cảnh báo — Kế hoạch Sprint 3: `18/10 – 22/10`):** **Nhựt (TV2)** viết câu truy vấn báo cáo `net revenue`, `COGS`, `gross profit`, `inventory value`, `velocity`, `low-stock`, `threshold`; **Phúc Hữu (TV3)** nối các báo cáo cơ bản vào Dashboard đúng session, filter và role.
- [ ] **`FR-REP-01..03` (Báo cáo nâng cao & Biểu đồ — Kế hoạch Sprint 4: `23/10 – 27/10`):** Hoàn thiện toàn bộ bộ lọc filter, biểu đồ trực quan doanh thu/giá vốn/lãi gộp, tốc độ luân chuyển và cảnh báo tồn thấp.

### 1.10. Dự báo nhu cầu AI & Gợi ý nhập hàng (`AI` - Gói 5) — *Phụ trách: Đăng Khoa, Nhựt (TV2) & Phúc Hữu (TV3)*
- [ ] **Chốt thiết kế AI (Sprint 3: `18/10 – 22/10`) & Hiện thực chạy thật (Sprint 4: `23/10 – 27/10`):**
  - **Đăng Khoa:** Thiết kế và hiện thực lõi `AI forecasting & evaluation`.
  - **Nhựt (TV2):** Chuẩn bị dữ liệu lịch sử và xây dựng API `recommendation / report` theo model của Khoa.
  - **Phúc Hữu (TV3):** Xây dựng giao diện AI gợi ý nhập hàng trên Web Admin.

---

## 2. Checklist Yêu Cầu Phi Chức Năng (`NFR-*`) — *Sprint 2 – 3 (Xây dựng), Sprint 4 – 5 (Đo kiểm & Hồi quy)*

- [x] **`NFR-PERF-01` (API Response Time cho POS Search/Barcode & Write Transactions — Hoàn thành xây dựng ở Sprint 2, Benchmark chính thức ở Sprint 4):** Tối ưu truy vấn tra cứu POS/Barcode `< 200ms` và giao dịch ghi Nhập kho/Giữ chỗ `< 500ms`.
- [x] **`NFR-PERF-02` (Concurrency & Anti-Oversell Handling — Hoàn thành lõi ở Sprint 2, Demo UI ở Sprint 4):** Áp dụng khóa dòng bi quan `SELECT ... FOR UPDATE` kết hợp `pg_advisory_xact_lock` trong `ReservationService`, vượt qua bài kiểm thử đồng thời tranh mua 1 SKU khả dụng cuối cùng (`available` không bao giờ âm).
- [x] **`NFR-TENANT-01` (Multi-tenant Data Isolation — Hoàn thành Sprint 2):** Toàn bộ 29 bảng nghiệp vụ đều có `tenant_id`, thực thi **EF Core Global Query Filters** và khóa ngoại kép theo Tenant, đạt **0% rò rỉ dữ liệu chéo**.
- [x] **`NFR-TENANT-02` (Scalability Indexing — Hoàn thành Sprint 2):** Đã thiết lập đầy đủ Composite Indexes trên `(tenant_id, created_at)` và `(tenant_id, sku_id)` cho bảng Sổ cái và Đơn hàng.
- [x] **`NFR-SEC-01` (Encryption & Authentication — Hoàn thành Sprint 2):** Băm mật khẩu bằng `BCrypt`, JWT Access Token 60 phút và Refresh Token an toàn.
- [x] **`NFR-SEC-02` (ACID Compliance — Hoàn thành Sprint 2):** Toàn bộ luồng Đăng ký Tenant, Xác nhận Nhập kho + Tính WAC, và Giữ chỗ tồn kho đều thực thi trong Transaction nguyên tử, rollback 100% khi có lỗi.
- [x] **`NFR-SEC-03` (Append-Only Ledger Auditability — Hoàn thành Sprint 2):** Bảng `inventory_transactions` chỉ cho phép `INSERT`, cấm tuyệt đối `UPDATE`/`DELETE`.
- [x] **`NFR-USA-02` (POS Usability — Hoàn thành nền tảng ở Sprint 2, tối ưu thiết bị thật ở Sprint 4 & 5):** Giao diện PWA POS tối giản, hỗ trợ quét Barcode + Enter, đáp ứng mượt trên Desktop, Tablet và Mobile POS.
- [x] **`NFR-MAINT-01` (Clean Architecture — Hoàn thành Sprint 2):** Phân tách nghiêm ngặt 4 tầng `OISM.Domain`, `OISM.Application`, `OISM.Infrastructure`, `OISM.WebAPI`.
- [x] **`NFR-MAINT-02` (Automated Test Coverage >= 80% — Đạt sớm ngay từ Sprint 2):** Đạt **194/194 bài test Pass 100%**, độ phủ mã nguồn module lõi `OISM.Application.Features.Inventory` đạt **`92.49% – 94.83%`** (vượt xa ngưỡng $\ge 80\%$ của đề bài).

---

## 3. Checklist Hạ Tầng, DevOps & Chuẩn Hóa Tài Liệu (`docs/`)

- [x] **Đóng gói Docker Compose trọn bộ (`docker-compose.yml` — Hoàn thành Sprint 2):** Chạy đồng bộ 4 container `postgres`, `backend`, `admin-dashboard`, `pos-app` kèm cấu hình Nginx SPA Fallback (`try_files`).
- [x] **Tích hợp liên tục GitHub Actions CI (`.github/workflows/ci.yml` — Hoàn thành Sprint 2):** Tự động build và chạy toàn bộ Unit/Integration Tests với PostgreSQL service container trên Linux runner.
- [x] **Chuẩn hóa kiến trúc thư mục tài liệu (`docs/` — Hoàn thành & Đồng bộ Confluence):**
  - [x] [`docs/00-overview/`](docs/00-overview/README.md) — Tầm nhìn, khái niệm cốt lõi & đối chiếu đề bài gốc.
  - [x] [`docs/01-users-and-rbac/`](docs/01-users-and-rbac/README.md) — Đặc tả Actors, Ma trận RBAC & Vòng đời Tenant.
  - [x] [`docs/02-functional-requirements/`](docs/02-functional-requirements/README.md) — 12 file đặc tả chi tiết từng module chức năng (`FR-01` đến `FR-12`).
  - [x] [`docs/03-non-functional-requirements/`](docs/03-non-functional-requirements/README.md) — 5 file đặc tả yêu cầu phi chức năng (`NFR-*`).
  - [x] [`docs/04-architecture-and-design/`](docs/04-architecture-and-design/README.md) — Clean Architecture, Multi-tenancy, ERD, State Machines & 4 Sequence Diagrams.
  - [x] [`docs/05-api-contracts/`](docs/05-api-contracts/README.md) — 5 file đặc tả hợp đồng REST API Endpoints.
  - [x] [`docs/06-testing-and-qa/`](docs/06-testing-and-qa/README.md) — Ma trận RTM (đồng bộ Confluence), Kế hoạch Test & DoD.
  - [x] [`docs/07-devops-and-deployment/`](docs/07-devops-and-deployment/README.md) — Hướng dẫn Docker Compose, CI/CD & Biến môi trường.
  - [x] [`docs/08-sprints/`](docs/08-sprints/README.md) — Kế hoạch & phân công chi tiết `sprint-2`, `sprint-3`, `sprint-4`, `sprint-5` khớp 100% với Confluence Page `5275650`.
  - [x] [`docs/09-personal-reports/`](docs/09-personal-reports/README.md) — Báo cáo cá nhân chuẩn hóa tên tiếng Anh ngắn gọn của 4 thành viên (`dang-khoa`, `nhut`, `phuc-huu`, `phuong`).
