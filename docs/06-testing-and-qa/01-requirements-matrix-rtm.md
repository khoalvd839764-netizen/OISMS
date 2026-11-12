# 01. Ma Trận Truy Vết Yêu Cầu Toàn Diện (Requirements Traceability Matrix - RTM)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-QA-01` |
| **Phiên bản (Version)** | `v1.2.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-12` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` (Trang 1 – 6) & Confluence Page ID `5275650` (Version 9) |
| **Phạm vi áp dụng** | Truy vết từ Yêu cầu Đề bài $\leftrightarrow$ Thành viên phụ trách $\leftrightarrow$ Mã nguồn/Test $\leftrightarrow$ Giai đoạn Sprint |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả nội dung & Chi tiết rà soát thay đổi (Change Check) | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | • Thiết lập ma trận RTM đối chiếu toàn bộ FR/NFR với thành phần mã nguồn và bài kiểm thử.<br/>• **Check thay đổi khi đối chiếu Confluence (`Page ID: 5275650`):** Bổ sung Bảng đối chiếu tổng thể theo Mục I của Confluence, cập nhật chính xác cột Thành viên phụ trách và điều chỉnh giai đoạn thực hiện (`FR-SIM-01..03` bắt đầu từ Sprint 3, `FR-AI-01` thực hiện ở Sprint 4, Sprint 5 dành cho Regression Test & Hồ sơ). | `INIT-SYNC-QA-01` |
| `v1.1.0` | `2026-11-11` | Nhóm dự án OISMS | Thêm mục 4 truy vết các chức năng Sprint 4 về sau (Upgrade 05, 01-A, 03, CS-01..08, FR-AI-01, FR-AI-02, kết nối sàn); sửa ghi chú giao diện U04, U02 đã có. Bảng mục 1 giữ nguyên là mốc sau Sprint 2. | `OISMS-58` |
| `v1.2.0` | `2026-11-12` | Rà soát mã nguồn | Cập nhật kết quả kiểm thử, nguồn simulator và liên kết checklist theo từng mã yêu cầu. | `DOC-CHK-S2` |

---

## 1. Ma Trận Đối Chiếu Tổng Thể Theo Confluence (Mục I — Page `5275650`)

| Mã yêu cầu | Tên nội dung yêu cầu | Thành viên phụ trách (Confluence) | Giai đoạn thực hiện (Confluence) | Trạng thái sau Sprint 2 |
| :---: | :--- | :--- | :--- | :---: |
| **`FR-AUTH (01-04)`** | Đăng ký cửa hàng, Đăng nhập bảo mật, Phân quyền RBAC và Quản lý chi nhánh | **Nhựt & Đăng Khoa** | **Sprint 2** *(bổ sung Sprint 3)* | **Đã hoàn thành & Pass 100% Test** |
| **`FR-PROD (01-04)`** | Danh mục, Thương hiệu, Sản phẩm, Biến thể SKU, Sinh mã vạch và Bảng giá | **Nhựt, Phúc Hữu & Nam Nguyễn** | **Sprint 2** *(bổ sung Sprint 3)* | **Đã hoàn thành & Pass 100% Test** |
| **`FR-INV (01-02)` & `FR-COST-01`** | Sổ cái kho bất biến chỉ thêm, Nhập kho NCC và Tự động tính giá vốn bình quân WAC | **Đăng Khoa, Phúc Hữu & Nam Nguyễn** | **Sprint 2** | **Đã hoàn thành & Pass 100% Test** |
| **`FR-INV (03-04)`** | Chuyển kho nội bộ 2 bước (xuất gửi $\rightarrow$ nhận) và Kiểm kê cân bằng kho | **Đăng Khoa, Phúc Hữu & Nam Nguyễn** | **Sprint 3** (`18/10 – 22/10`) | Đã có bảng DB, triển khai ở Sprint 3 |
| **`FR-COST-02` & `FR-ORD (01-03)`** | Chốt giá vốn vào đơn, Tiếp nhận đơn đa kênh Canonical Model, Máy trạng thái và Duyệt đơn | **Đăng Khoa, Nhựt, Phúc Hữu & Nam Nguyễn** | **Sprint 2 (nền), Sprint 3 (đủ)** | Đã xong nền DB, hoàn thiện ở Sprint 3 |
| **`FR-RSE (01-03)`** | Công thức tồn khả dụng động, Động cơ giữ chỗ và Khóa dữ liệu chống bán vượt | **Đăng Khoa (Trưởng nhóm)** | **Sprint 2 (nền), Sprint 3 (đủ)** | Đã xong API Reserve + Lock + Idempotency |
| **`FR-POS (01-04)`** | Giao diện bán nhanh cảm ứng, Quét mã vạch, Thanh toán giỏ nguyên tử và In hóa đơn | **Phương Lê & Đăng Khoa** | **Sprint 2 (nền), Sprint 3 (đủ)** | Đã xong UI POS, Search/Barcode, Print |
| **`FR-SIM (01-03)`** | Trình giả lập Webhook 3 sàn, Đẩy tin SignalR thời gian thực và Tác vụ nền Hangfire | **Đăng Khoa & Nhựt** *(FE nối ở Sprint 3)* | **Sprint 2, 3 & 4** *(Sprint 3: Simulator, SignalR, Hangfire expire; Sprint 4: Hangfire daily report)* | Triển khai trọng tâm ở Sprint 3 & 4 |
| **`FR-REP (01-03)`** | Báo cáo doanh thu thuần, giá vốn, lãi gộp; Tốc độ luân chuyển kho và Cảnh báo tồn thấp | **Nhựt, Phúc Hữu, Nam Nguyễn & Đăng Khoa** | **Sprint 3 (cơ bản), Sprint 4 (đủ)** | Triển khai ở Sprint 3 & 4 |
| **`AI` (Gói 5 theo đề)** | Dự báo nhu cầu và gợi ý nhập hàng tự động (`Stock Reorder Recommendations`) | **Đăng Khoa & Nhựt** *(Phúc Hữu & Nam Nguyễn làm UI)* | **Sprint 3 (chốt), Sprint 4 (chạy)** | Chốt ở Sprint 3, chạy thật ở Sprint 4 |
| **`NFR-PERF, TENANT, SEC, MAINT`** | Thử tải 50 đơn đồng thời Flash Sale, Cô lập đa người thuê, Bảo mật JWT/BCrypt, $\ge 80\%$ coverage | **Đăng Khoa & Cả nhóm cùng kiểm test** | **Sprint 2 – 3 (xây), Sprint 4 – 5 (đo & hồi quy)** | Đã đạt `194/194 (mốc Sprint 2) Tests Pass`, Coverage `> 92%` |

---

## 2. Truy Vết Upgrade 04 — Bảng Giá Theo Thời Gian (`OISMS-47-U04`)

Nguồn yêu cầu: [tài liệu 04](../Upgrade-recommended/Done/04-bang-gia-theo-thoi-gian.md#tiêu-chí-nghiệm-thu). Kết quả: [báo cáo sau nâng cấp](../report-after-upgrade/bang-gia-theo-thoi-gian.md). Trạng thái dưới đây chỉ nói về backend. Giao diện Admin (lập, duyệt phiếu đổi giá, lịch sử giá) đã có từ 04/11/2026, chưa có test tự động.

| Tiêu chí | Mã nguồn chính | Test | Trạng thái |
| :--- | :--- | :--- | :--- |
| 1. Mỗi khóa chỉ ra một giá | `SkuPriceResolver`, `ux_sku_unit_prices_active_key` | `SkuPriceResolverUnitTests`, `AC03_Cancellation_...` | Đạt |
| 2. Giá chuyển đúng mốc, đơn giữ giá đã chụp | `SkuPriceResolver`, `OrderService.BuildOrderLines` | `AC06_PriceResolution_...` | Đạt phần đơn hàng; hóa đơn in lại và báo cáo chưa có test riêng |
| 3. Đóng đường ghi cũ, mọi dòng giá có phiếu | `ProductsController`, `ProductService`, `UnitService`, `PriceChangeService` | `AC08_...`, `Review_UpdateVariant_...`, `AC05_...` | Đạt |
| 4. Staff chờ duyệt, Cashier không tạo được phiếu | `PricingController`, `PriceChangeService` | `AC04_StaffPendingFlow_...` | Đạt |
| 5. Hủy giá đã lên lịch, dòng đã duyệt bất biến | `oisms_sku_unit_price_guard`, `PriceChangeService` | `AC03_RuntimeRole_...`, `AC03_Cancellation_...` | Đạt |
| 6. Đơn giữ chỗ không lỗi khi giá mới hiệu lực | `OrderService` | `AC06_PriceResolution_...` | Đạt |
| 7. Replay sau khi giá đổi trả đơn cũ | `OrderService` | `AC07_Idempotency_...` | Đạt |
| 8. Chưa có giá thì báo lỗi | `OrderService`, `ReservationService` | `AC07_Idempotency_...`, `AC05_...` | Đạt |
| 9. Cách ly tenant | `AppDbContext` (Global Query Filter), FK ghép `tenant_id` | `AC04_TenantIsolation_...` | Đạt |

## 3. Truy Vết Upgrade 02 — Hóa Đơn Bán Và Chứng Từ Xuất (`OISMS-47-U02`)

Nguồn yêu cầu: [tài liệu 02](../Upgrade-recommended/Done/02-truy-vet-chung-tu-xuat-kho.md#tiêu-chí-nghiệm-thu). Kết quả: [báo cáo sau nâng cấp](../report-after-upgrade/hoa-don-truy-vet-xuat-kho.md). Chỉ bao phủ backend. Giao diện Admin (trang Hóa đơn bán `/invoices`: chi tiết, truy vết, in lại) đã có, chưa có test tự động.

| Tiêu chí | Mã nguồn chính | Test | Trạng thái |
| :--- | :--- | :--- | :--- |
| 1. Từ dòng `OUT` truy được nguồn và người xác nhận; đơn cũ không lỗi | `SalesInvoiceService` | `AC07_TraceFromLedger_...`, `AC07_OrderTrace_LegacyActorFallback_...` | Đạt |
| 2. Retry, đồng thời chỉ một lần xuất và một hóa đơn | `OrderService`, `uq_sales_invoices_3` | `SalesInvoiceConcurrencyIntegrationTests` | Đạt |
| 3. Lỗi tạo hóa đơn thì hoàn tác toàn bộ | `OrderService` | `AC02_PosAdminMarketplace_...` | Đạt |
| 4. Chuyển kho, kiểm kê không sinh hóa đơn | `SalesInvoiceService` | `AC07_TraceFromLedger_...` | Đạt |
| 5. In lại giữ nguyên dữ liệu đã chụp | `SalesInvoiceFactory`, `oisms_protect_order_item` | `AC05_ReprintIgnoresLaterMasterDataChanges_...` | Đạt |
| 6. Giá vốn dòng đơn POS bằng đơn giá dòng `OUT` | `OrderService` | `AC06_AverageCostPrecision_...` | Đạt |
| 7. Cashier in được, không thấy giá vốn, không truy vết; không xem tenant khác | `SalesInvoicesController` | `AC07_RbacTenantIsolationErrorCodesAndDateFilter_...` | Đạt |

## 4. Truy Vết Các Chức Năng Từ Sprint 4 (cập nhật 12/11/2026)

Ngày 12/11/2026, unit backend 380/380 PASS, POS `npm test` 10/10 file PASS và simulator 1/1 file PASS. Integration chọn lọc trên PostgreSQL 16 đạt 7/7 gồm xuất chuyển kho, nhận chuyển kho, kiểm kê sinh bút toán, checkout POS nguyên tử, xác nhận đơn online, cô lập tenant và [50 webhook Shopee tranh một đơn vị tồn](../../test-oisms/backend/OISM.IntegrationTests/WebhookIntegrationTests.cs#L1773). Mốc chạy toàn bộ integration là 372/372 PASS ngày 07/11/2026. [Checklist hiện thực và nghiệm thu](../../checklist.md).

| Yêu cầu | Mã nguồn chính | Test | Trạng thái |
| :--- | :--- | :--- | :--- |
| Upgrade 05 — Đa đơn vị tính | `SkuUnitResolver`, `UnitService`, `UnitsController`; POS `posCartUnits.ts` | `MultiUnitIntegrationTests`, `MultiUnitFlowIntegrationTests`, `SkuUnitResolverTests`, `test-cart-units.mjs` | Đạt |
| Upgrade 01-A — Phiếu mua, nhập kho từ phiếu mua | `PurchaseOrderService`, `PurchaseOrdersController`, `GoodsReceiptService` | `PurchaseOrderIntegrationTests` | Đạt |
| Upgrade 01-B — Hóa đơn NCC, chênh lệch | — | — | Chưa làm |
| Upgrade 03 — Lịch kiểm kê, duyệt chênh lệch | `StockCountService`, `StockCountPeriodJob`, `StockCountSchedulesController` | `StockCountU03IntegrationTests`, `StockCountPeriodCalculatorTests` | Đạt |
| CS-01 — Trả hàng từ khách | `SalesReturnService`, `SalesReturnsController` | `SalesReturnServiceTests`, `PostgresSalesReturnIntegrationTests`, `test-oisms/api-python/test_sales_return_api.py` | Unit đạt; integration chưa chạy lại |
| CS-02 — Nhân viên, phân quyền | `UserService`, `UsersController` | `UserServiceTests` | Đạt một phần: chưa có phân công chi nhánh |
| CS-03 — Ngưỡng cảnh báo tồn | `InventoryService` (ReorderPoint) | `ReorderPointTests` | Đạt; chưa kiểm thông báo tồn thấp qua realtime |
| CS-04 — Tìm kiếm toàn hệ thống | `SearchController` | `GlobalSearchTests` | Đạt; kết quả phiếu nhập chưa mở đúng phiếu |
| CS-05 — Thanh toán VietQR | `PaymentAccountService`, `PaymentAccountsController`; POS `qrService.ts`, `pendingCheckoutStore.ts` | `PaymentAccountServiceTests`, `PaymentAccountIntegrationTests`, `test-qr.mjs`, `test-b03-qr.mjs`, `test-checkout.mjs` | Đạt; chưa có xác nhận tự động qua ngân hàng |
| CS-06 — In chứng từ giao hàng, in báo cáo | — | — | Chưa làm (nút còn nhãn Coming soon) |
| CS-07 — KPI Tổng quan | `ReportService` | `ReportServiceTests` | Đạt; số liệu lũy kế, API chưa lọc theo thời gian |
| `FR-AI-01` (CS-08) — Gợi ý nhập hàng | `AiForecastingService` | `AiForecastingTests`, `PostgresAiStockMovementIntegrationTests` | Unit đạt; integration chưa chạy lại |
| `FR-AI-02` — Trợ lý OISMS | `AiChatService`, `LlmRouter`, `AiController`, kho tài liệu RAG | `AiChatServiceTests`, `AiChatToolTests`, `AiChatEndpointTests`, `KnowledgeBaseTests`, `KnowledgeFilesTests`, `OpenAiCompatibleLlmClientTests`, `test-ai-chat-format.mjs` | Unit đạt; gọi model thật phụ thuộc hạn mức gói miễn phí |
| `FR-SIM-01` — Kết nối sàn, app giả lập | `ShopIntegrationService`, `WebhookService`, [app gửi 1–200 đơn đồng thời](../../src/frontend/marketplace-simulator/src/App.tsx#L245) | `ShopIntegrationServiceTests`, `ShopIntegrationApiIntegrationTests`, `WebhookIdentityTests`, `WebhookCatalogTests`, `test-payload.mjs` | Đã triển khai, test payload PASS. Kịch bản 50 đơn tranh 1 đơn vị tồn trên AWS theo dõi tại `NFR-PERF-02`. |
| `FR-SIM-03` — Hangfire tổng hợp báo cáo hằng ngày | — | — | Chưa làm (chỉ có job hết hạn giữ chỗ và lịch kiểm kê) |
