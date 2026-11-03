# CHECKLIST TỔNG HỢP: BUG ĐÃ FIX, UPGRADE ĐÃ LÀM & KẾ HOẠCH 2 NHÓM (BACKEND & FRONTEND)

| Thông tin kiểm soát | Chi tiết |
| :--- | :--- |
| **Ngày cập nhật** | `03/11/2026` (Asia/Saigon) |
| **Confluence Sprint Plan** | [Page ID `5275650` (Version 17)](https://khoalevodang301007.atlassian.net/wiki/spaces/MFS/pages/5275650) |
| **Jira Tasks (Sprint 4 — ID `8`)** | [`OISMS-45`](https://khoalevodang301007.atlassian.net/browse/OISMS-45) *(Nhóm 1: Backend)* · [`OISMS-46`](https://khoalevodang301007.atlassian.net/browse/OISMS-46) *(Nhóm 2: Frontend)* |
| **Cơ cấu Phân nhóm** | **Nhóm 1: Backend** (`Đăng Khoa` & `Nhựt Phùng Minh`) · **Nhóm 2: Frontend** (`Phúc Hữu`, `Nam Nguyễn` & `Phương Lê`) |
| **Trạng thái Kiểm thử** | **20/20 Bugs PASS** (`23/23` probes) · **13/13 FE Regression PASS** · **159/159 BE Unit Tests PASS** |

---

## I. MẢNG A — CHECKLIST FIX 20 LỖI (`BF-001..020`) & CLEAN ARCHITECTURE (`Upgrade 06`) [ĐÃ HOÀN TẤT]

Tài liệu đối chiếu: [BUG-LIST.md](Report%20Bugfix/BUG-LIST.md), [report result affter fix.md](Report%20Bugfix/report%20result%20affter%20fix.md) & [clean-architecture.md](../report-after-upgrade/clean-architecture.md)

### 1. Phần đã hoàn thành của Nhóm 1 (Backend — `Đăng Khoa` & `Nhựt Phùng Minh`)
- [x] **`Upgrade 06` — Rà soát & Chuẩn hóa Clean Architecture Backend:**
  - [x] **Tách nghiệp vụ khỏi 8 Controller (`F-01`):** Chuyển toàn bộ logic nghiệp vụ và thao tác DB từ `AuthController`, `BranchController`, `BrandsController`, `CategoriesController`, `PosCatalogController`, `ProductsController`, `SuppliersController`, `WarehousesController` sang các Service trong tầng `OISM.Application` (`AuthService`, `BranchService`, `BrandService`, `CategoryService`, `PosCatalogService`, `ProductService`, `SupplierService`, `WarehouseService`).
  - [x] **Giữ sạch interface `IAppDbContext` (`F-02`):** Loại bỏ việc phơi bày `DatabaseFacade`, `ChangeTracker`, `Entry` ra Controller.
  - [x] **Cô lập hạ tầng kỹ thuật sang `OISM.Infrastructure` (`F-03`):** Đặt BCrypt sau `IPasswordHasher` (`PasswordHasher.cs`), đặt khóa PostgreSQL vào `ISqlLockService` (`SqlLockService.cs`), và tách thao tác đổi chi nhánh kho vào `IWarehouseBranchReassignmentStore` (`WarehouseBranchReassignmentStore.cs`).
  - [x] **Chuẩn hóa DI & Thư mục:** Tách đăng ký `AddApplicationServices` / `AddInfrastructureServices` trong `Program.cs`; xóa thư mục `OISMS/` trùng lặp ở gốc dự án.
- [x] **[P1] `BF-011` (Backend `ReportService.cs`, `ReportServiceTests.cs`):**
  - [x] Chuyển điều kiện lọc ngày sang khoảng nửa mở `CreatedAt < endBoundary`, chuẩn hóa mọi mốc `endBoundary.Second == 59` (kể cả `.000` và `.999`) lên đầu giây kế tiếp (`00:00:00.000` ngày kế tiếp).
  - [x] Gom nhóm xu hướng hàng ngày `dailyTrend` theo múi giờ Việt Nam `UTC+07:00` (`CreatedAt.ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-dd")`).
  - [x] Bổ sung unit test `GetRevenueProfitReport_ShouldIncludeSubSecondEndOfDay_AndGroupDailyTrendInVietnamTime` (đạt `159/159` Unit Tests PASS).

### 2. Phần đã hoàn thành của Nhóm 2 (Frontend — `Phúc Hữu`, `Nam Nguyễn` & `Phương Lê`)
- [x] **[P1] `BF-001` — HTTP và refresh gắn chặt với chủ sở hữu phiên (`httpClient.ts`, `AuthContext.tsx`):**
  - [x] Theo dõi `currentAuthGeneration` và `sessionPreviousTokens`; gắn `generation` vào `failedQueue` và cờ `isStaleSession = true` để refresh thất bại muộn của User A không xóa phiên của User B; chặn replay tự động mutation 401 sang token tài khoản khác.
- [x] **[P1] `BF-002` — Kết nối SignalR thay thế và cô lập theo phiên (`realtimeAdapter.ts`, `AuthContext.tsx`):**
  - [x] Quản lý kết nối theo `currentConnectionToken` và `connectionGeneration`; thêm guard `if (connection !== hubConnection) return;` trong `onreconnecting`, `onreconnected`, `onclose`.
- [x] **[P1] `BF-003` — POS giữ nguyên `requestKey` khi thử lại thanh toán (`PaymentModal.tsx` trên cả 2 POS):**
  - [x] Gắn `requestKey` theo chữ ký giỏ hàng/chi nhánh/thu ngân (`cartSignature`) lưu trong `sessionStorage` + `useRef`.
- [x] **[P1] `BF-004` — Tạo phiếu chuyển kho bảo toàn `requestKey` khi retry (`stockTransferService.ts`, `CreateTransferModal.tsx`):**
  - [x] Lưu trữ `lastTransferIntent` đồng bộ với `sessionStorage` theo chữ ký payload.
- [x] **[P1] `BF-005` — Tạo đợt kiểm kê bảo toàn `requestKey` khi retry (`stockCountService.ts`, `CreateStockCountModal.tsx`):**
  - [x] Lưu trữ `lastCountIntent` đồng bộ với `sessionStorage` theo chữ ký payload.
- [x] **[P1] `BF-006` — Xóa số đếm đã lưu rồi chốt kiểm kê phải ghi nhận lại số sổ sách trước khi chốt (`StockCountDetailDrawer.tsx`):**
  - [x] Lưu các chỉnh sửa hợp lệ (`hasNonBlankDirtyEdits`) và bắt buộc gọi `recordCounts` thành công trước khi `confirmStockCount`.
- [x] **[P2] `BF-007` — Lưu kết quả đếm có ô trống phải hỏi xác nhận (`StockCountDetailDrawer.tsx`).**
- [x] **[P2] `BF-008` — Drawer chứng từ không bị kẹt khóa thao tác khi đổi chứng từ (`busyIdsRef` theo từng ID chứng từ).**
- [x] **[P1] `BF-009` — Báo cáo không bị phản hồi cũ ghi đè (`activeReqRef` generation guard trong `ReportsPage.tsx`).**
- [x] **[P2] `BF-010` — Xóa dữ liệu báo cáo cũ khi truy vấn bộ lọc mới bị lỗi (`ReportsPage.tsx`).**
- [x] **[P1] `BF-011` (Frontend `ReportsPage.tsx`):** Tính ngày "Hôm nay" và biên ngày theo chuẩn múi giờ `Asia/Saigon` (`+07:00`).
- [x] **[P2] `BF-012` — Hiển thị đúng tồn kho của kho nguồn trên form tạo chuyển kho (`CreateTransferModal.tsx`):**
  - [x] Nạp `{ onHand, reserved, available }` theo `fromWarehouseId`, không fallback về tồn tổng tenant khi tồn kho nguồn bằng `0`, xóa dòng cũ khi đổi kho nguồn và khóa chọn SKU `isActive === false`.
- [x] **[P1] `BF-013` — Giữ chỗ tồn kho khóa form và bảo toàn `idempotencyKey` khi kết quả chưa rõ (`InventoryPage.tsx`).**
- [x] **[P2] `BF-014` — Trang Chi nhánh & Kho hiển thị rõ banner lỗi API kèm nút "Thử lại" (`OrganizationPage.tsx`).**
- [x] **[P2] `BF-015` — POS xử lý đầy đủ lỗi 401 hết phiên (`pos-auth-expired`), bảo toàn giỏ hàng và trích xuất `ProblemDetails` (`posApi.ts`, `api.ts`, `pos-app/src/App.tsx`).**
- [x] **[P2] `BF-016` — Đồng bộ realtime tồn kho đầy đủ (`joinedBranchIds`, lắng nghe `stock.changed`, `reservation.expired`, `reconnected` trên `InventoryPage.tsx` và `OrdersPage.tsx`).**
- [x] **[P2] `BF-017` — Link thông báo cảnh báo `?tab=alerts` và sự kiện `oism:navigate` mở đúng tab (`ReportsPage.tsx`, `NotificationCenter.tsx`).**
- [x] **[P2] `BF-018` — Bộ lọc báo cáo hỗ trợ lọc riêng đơn `Admin` và loại bỏ `Web` không hợp lệ (`ReportsPage.tsx`).**
- [x] **[P2] `BF-019` — POS từ chối phản hồi HTTP 200 thiếu định danh đơn hàng hoặc chứa GUID rỗng (`posApi.ts`, `api.ts`).**
- [x] **[P1] `BF-020` — POS độc lập khóa giỏ hàng và ngữ cảnh chi nhánh khi checkout đang chạy (`pos-app/src/App.tsx`).**

---

## II. MẢNG B & C — KẾ HOẠCH TRIỂN KHAI CHO 2 NHÓM (BACKEND & FRONTEND)

### 1. NHÓM 1: NHÓM BACKEND (`Đăng Khoa` — Backend 01 Lead & `Nhựt Phùng Minh` — Backend 02)

#### B1. Backend cho 8 chức năng Coming Soon (`docs/bug-fixaffter/Comming Soon function/COMING-SOON.md`)
* **Đợt 1 — Các API độc lập (Làm ngay và bàn giao sớm cho Nhóm Frontend nối UI):**
  - [ ] **`CS-02` (Nhân viên & Phân quyền — `Nhựt`):** Xây dựng API danh sách, tạo mới, cập nhật nhân viên, gán vai trò RBAC (`Owner`, `Staff`, `Cashier`) và phân công chi nhánh theo tenant.
  - [ ] **`CS-04` (Tìm kiếm toàn hệ thống — `Nhựt`):** Xây dựng endpoint tìm kiếm đa thực thể (Sản phẩm/SKU, Đơn hàng, Phiếu kho) theo quyền và tenant.
  - [ ] **`CS-07` (Tổng quan Dashboard — `Nhựt`):** Chuẩn hóa API/DTO thống kê KPI doanh thu/lợi nhuận/số đơn, đơn gần đây và biểu đồ xu hướng từ dữ liệu server thật.
  - [ ] **`CS-03` (Ngưỡng cảnh báo tồn `ReorderPoint` — `Nhựt`):** Xây dựng API lưu cấu hình `ReorderPoint` trên server (tính theo đơn vị tồn gốc), đồng bộ cảnh báo tồn thấp và sự kiện SignalR.
  - [ ] **`CS-08` (`FR-AI-01` Dự báo & Gợi ý nhập hàng AI — `Đăng Khoa` & `Nhựt`):** Xây dựng lõi thuật toán dự báo nhu cầu & đánh giá độ tin cậy (`Đăng Khoa`) + API lấy lịch sử xuất kho theo đơn vị gốc và trả danh sách gợi ý nhập hàng (`Nhựt`).
* **Đợt 2 — Các API gắn với chứng từ Đơn hàng/POS (Làm sau khi chốt schema `Upgrade 05 & 04`):**
  - [ ] **`CS-05` (Thanh toán QR POS — `Đăng Khoa`):** Hỗ trợ API sinh thông tin QR / xác nhận trạng thái thanh toán QR và đối soát idempotency trong luồng checkout.
  - [ ] **`CS-01` (Nhận hàng trả từ khách — `Đăng Khoa` & `Nhựt`):** Xây dựng API nhận hàng trả gắn đơn gốc, kiểm tra lượng đã bán/đã trả và ghi bút toán `IN` bù trừ theo `BaseQuantity` & `FactorSnapshot` của đơn gốc.

#### C1. Backend Nâng cấp Nghiệp vụ Lõi theo GVHD (`docs/Upgrade-recommended/`)
- [ ] **Bước 1 — `Upgrade 05`: Nhiều đơn vị tính cho hàng hóa vật lý (`Đăng Khoa` & `Nhựt`):**
  - [x] Tạo bảng `units`, `sku_units`, `sku_unit_factor_history`, API cấu hình đơn vị (P1); thêm `BaseUnitId` trên SKU và `UnitId`, `FactorSnapshot`, `BaseQuantity = Quantity × FactorSnapshot` trên `GoodsReceiptItem` & `OrderItem`.
  - [x] Cập nhật `GoodsReceiptService`, `OrderService`, `ReservationService` tính tồn/giữ chỗ/WAC/COGS theo `BaseQuantity`; hỗ trợ tương thích ngược dữ liệu cũ (`FactorSnapshot = 1`).
- [ ] **Bước 2 — `Upgrade 04`: Bảng giá bán có thời gian hiệu lực (`Nhựt` & `Đăng Khoa`):**
  - [x] Tạo bảng `price_lists` (giá lẻ, giá sỉ), `price_change_requests`, `price_change_request_items` và `sku_unit_prices` chỉ thêm dòng mới theo khóa `(Bảng giá, SKU, UnitId, valid_from)`; phiếu đổi giá có trạng thái `Pending`, `Approved`, `Rejected`.
  - [x] Thêm resolver giá dùng chung cho POS, đơn Admin và giữ chỗ; chuyển dữ liệu giá cũ bằng phiếu "Giá đầu kỳ".
  - [x] Lưu đơn giá, ID dòng giá và nguồn giá vào dòng đơn; đóng API sửa giá trực tiếp trên biến thể SKU và đơn vị.
- [ ] **Bước 3 — `Upgrade 01`: Mua hàng, nhập kho và giá vốn WAC (`Đăng Khoa` & `Nhựt`):**
  - [x] **U01-A:** Tạo bảng `purchase_orders`, `purchase_order_items` (có chi nhánh, đơn vị và hệ số snapshot), bước duyệt, đóng và hủy phiếu; API hủy phiếu nhập nháp.
  - [x] **U01-A:** Bắt buộc phiếu nhập kho NCC gắn phiếu mua đã duyệt, lấy giá và hệ số từ phiếu mua, chặn nhập vượt; có test đồng thời trên PostgreSQL thật.
  - [ ] **U01-B (làm sau U01-A):** Đối chiếu hóa đơn NCC (`supplier_invoices`) và xử lý chứng từ chênh lệch (`purchase_variances`).
- [x] **Bước 4 — `Upgrade 02`: Truy vết chứng từ xuất kho (`Đăng Khoa`):** [báo cáo](../report-after-upgrade/hoa-don-truy-vet-xuat-kho.md).
  - [x] Liên kết mọi dòng `InventoryTransaction.OUT` với chứng từ nguồn hợp lệ và hóa đơn/biên nhận bán hàng (`sales_invoices`); có API in lại và truy vết, test đồng thời trên PostgreSQL thật.
- [x] **Bước 5 — `Upgrade 03`: Quy định kiểm kê định kỳ & đột xuất (`Đăng Khoa` & `Nhựt`):** [báo cáo](../report-after-upgrade/lich-kiem-ke-duyet-chenh-lech.md).
  - [x] Tạo bảng lịch kiểm kê `stock_count_schedules` (Giữa tháng ngày 15, Cuối tháng, Đột xuất có lý do) và bước phê duyệt chênh lệch `PendingApproval`.

---

### 2. NHÓM 2: NHÓM FRONTEND (`Phúc Hữu & Nam Nguyễn` — Web Admin 5173 & `Phương Lê` — Web POS PWA 5174)

#### B2. Frontend cho 8 chức năng Coming Soon (`docs/bug-fixaffter/Comming Soon function/COMING-SOON.md`)
* **Giai đoạn 1 — Các màn hình độc lập (Làm ngay, không đụng vào form Đơn/Kho đang nâng cấp):**
  - [ ] **`CS-02` (Web Admin — `Phúc Hữu & Nam Nguyễn`):** Giao diện danh sách, tạo/sửa nhân viên, phân quyền RBAC và phân công chi nhánh trên `OrganizationPage.tsx`.
  - [ ] **`CS-04` (Web Admin — `Phúc Hữu & Nam Nguyễn`):** Ô tìm kiếm toàn hệ thống trên thanh Header (`AdminShell.tsx`), hiển thị kết quả phân nhóm và điều hướng đúng quyền.
  - [ ] **`CS-07` (Web Admin — `Phúc Hữu & Nam Nguyễn`):** Nối dữ liệu thật cho các thẻ KPI doanh thu/đơn hàng/lợi nhuận, bảng đơn gần đây và biểu đồ trên `DashboardPage.tsx`.
  - [ ] **`CS-03` (Web Admin — `Phúc Hữu & Nam Nguyễn`):** Form lưu ngưỡng cảnh báo tồn `ReorderPoint` thật trên server, đồng bộ giữa trang Kho và tab Cảnh báo tồn kho.
  - [ ] **`CS-08` (Web Admin — `Phúc Hữu & Nam Nguyễn`):** Giao diện Dự báo & Gợi ý nhập hàng bằng AI (`FR-AI-01`), hiển thị độ tin cậy và trạng thái khi thiếu dữ liệu; xây dựng màn hình demo concurrency.
* **Giai đoạn 2 — Các màn hình Đơn hàng/Kho/POS (Làm cùng hợp đồng DTO mới của Backend):**
  - [ ] **`CS-06` (Web Admin — `Phúc Hữu & Nam Nguyễn`):** Bố cục in chứng từ giao hàng và in báo cáo chuẩn khổ A4/80mm (`@media print`), cô lập vùng in, ẩn giá vốn theo role, in kèm đơn vị tính & số chứng từ mới.
  - [ ] **`CS-05` (Web POS — `Phương Lê`):** Hoàn thiện giao diện thanh toán QR động và xác nhận thanh toán trong `PaymentModal.tsx` trên cả POS PWA và POS Admin.
  - [ ] **`CS-01` (Web Admin — `Phúc Hữu & Nam Nguyễn`):** Giao diện Nhận hàng trả từ khách (chọn đơn gốc, kiểm tra số lượng trả theo đơn vị đã mua, xác nhận nhập hoàn tồn).

#### C2. Frontend Ghép nối Nâng cấp Nghiệp vụ (`docs/Upgrade-recommended/`)
- [ ] **`Upgrade 05` (Admin & POS — `Phúc Hữu, Nam Nguyễn & Phương Lê`):**
  - [ ] Trên **Web Admin (`ProductsPage`, `InventoryPage`, `OrdersPage`)**: Cấu hình đơn vị gốc & danh sách đơn vị quy đổi cho SKU; chọn đơn vị mua/bán trên phiếu nhập và đơn hàng; hiển thị cả số lượng giao dịch (`1 bao`) và lượng quy đổi gốc (`25 kg`).
  - [ ] Trên **Web POS (`pos-app/src/App.tsx`, `CartTable.tsx`)**: Chọn đơn vị bán trên từng dòng giỏ hàng (khóa dòng theo `variantId + unitId`), quét barcode quy cách chọn đúng đơn vị và in hóa đơn giữ nguyên đơn vị đã bán.
- [ ] **`Upgrade 04` (Web Admin — `Phúc Hữu & Nam Nguyễn`):**
  - [ ] Giao diện lập và duyệt Phiếu đổi giá, lịch giá sắp tới và lịch sử giá theo SKU; bỏ ô sửa giá trên form sửa SKU, chỉ giữ ô giá ban đầu khi tạo mới; hiện giá mới khi gặp `PRICE_MISMATCH`.
- [ ] **`Upgrade 01 & 02` (Admin & POS — `Phúc Hữu, Nam Nguyễn & Phương Lê`):**
  - [ ] **U01-A:** Giao diện tạo, duyệt, đóng và hủy Phiếu mua hàng (`purchase_orders`); lập Phiếu nhập kho từ phiếu mua đã duyệt, khóa ô giá, hiện đơn vị và lượng còn được nhận; hủy phiếu nhập nháp.
  - [ ] **U01-B (làm sau U01-A):** Giao diện đối chiếu Hóa đơn NCC & chứng từ chênh lệch.
  - [ ] **Upgrade 02:** Hiển thị liên kết truy vết Hóa đơn bán ↔ Đơn hàng ↔ Sổ cái kho `OUT`.
- [ ] **`Upgrade 03` (Web Admin — `Phúc Hữu & Nam Nguyễn`):**
  - [ ] Giao diện cấu hình Lịch kiểm kê định kỳ (ngày 15 / cuối tháng), tạo kiểm kê đột xuất có lý do và màn hình Phê duyệt chênh lệch kiểm kê (`PendingApproval`).
