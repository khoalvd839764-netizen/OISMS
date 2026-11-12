# CHECKLIST YÊU CẦU VÀ BÀN GIAO ĐỒ ÁN OISMS

## 1. Tổng quan

File này dùng để tra cứu yêu cầu OISMS theo mã, xem tiến độ triển khai và mở code, test hoặc hồ sơ làm bằng chứng khi nghiệm thu.

| Thông tin | Chi tiết |
| --- | --- |
| Mã tài liệu | `DOC-CHK-S2` |
| Phiên bản | `v1.7.2` |
| Ngày cập nhật | 12/11/2026 (Asia/Saigon) |
| Mốc rà soát mã nguồn | 12/11/2026 trên nhánh `feature/OISMS-58-nghiem-thu-sprint-5` |
| Phạm vi đề bài | 30 FR, 1 mục dự báo nhập hàng, 11 NFR, 5 gói công việc và 6 nhóm hồ sơ. [Bản đối chiếu đề bài](docs/00-overview/03-de-bai-cnpm-reference.md) |
| Tiến độ tại mốc rà soát | 22/31 mục chức năng đã triển khai, 4/11 NFR đã đánh dấu đạt theo bằng chứng ghi ở từng mục. |
| Kết quả kiểm thử tại mốc rà soát | Backend unit 380/380, integration PostgreSQL chọn lọc 7/7, POS 10/10 file và simulator 1/1 file đều pass. Mốc integration toàn bộ 372/372 pass ngày 07/11/2026. [RTM và kết quả](docs/06-testing-and-qa/01-requirements-matrix-rtm.md) |

- `[x]` Đã triển khai chức năng hoặc có bằng chứng đáp ứng tiêu chí ghi tại mục đó.
- `[ ]` Còn thiếu chức năng, phép đo, hồ sơ hoặc bằng chứng nghiệm thu.
- **Dẫn chứng** dẫn đến mã nguồn, bài test hoặc tài liệu bàn giao. Kết quả test và phạm vi môi trường được ghi theo mốc kiểm tra.

[FR](#3-yêu-cầu-chức-năng-functional-requirements) · [NFR](#4-yêu-cầu-phi-chức-năng-non-functional-requirements) · [Gói công việc và hồ sơ](#5-gói-công-việc-và-hồ-sơ-bàn-giao) · [Yêu cầu bổ sung](#6-yêu-cầu-bổ-sung)

Hồ sơ theo sprint và các lỗi chi tiết có tại [báo cáo BE1 Sprint 3](docs/09-personal-reports/dang-khoa/sprint-3/SUMMARY.md), [báo cáo Sprint 5](docs/08-sprints/sprint-5/BAO_CAO_TONG_KET_NGHIEM_THU_SPRINT_5.md) và [checklist lỗi](docs/bug-fixaffter/CHECKLIST.md).

## 2. Lịch sử phiên bản

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả nội dung & Chi tiết rà soát thay đổi (Change Check) | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | • Tổng hợp các yêu cầu, gói công việc và hồ sơ đề bài, ghi nhận các hạng mục đã hoàn thành trong Sprint 2.<br/>• **Check thay đổi khi đối chiếu Confluence (`Page ID: 5275650`, Version 9):** Đồng bộ lịch trình (`S2: 13–17/10`, `S3: 18–22/10`, `S4: 23–27/10`, `S5: 28/10–01/11`) và phân công 5 thành viên cho các hạng mục chưa hoàn thành `[ ]`. | `INIT-SYNC-CHK-S2` |
| `v1.1.0` | `2026-10-27` | Lê Võ Đăng Khoa — Backend 1 | Cập nhật checklist BE1 Sprint 3 theo hồ sơ T01–T06, B-01 và PR #21; tách phần backend đã đạt khỏi UI, API đọc/báo cáo BE2 và công việc Sprint 4; đồng bộ phân công Simulator theo bản v11 đã lưu trong repo. | `SYNC-BE1-S3-CHECKLIST` |
| `v1.2.0` | `2026-10-29` | Rà soát sản phẩm | Bổ sung checklist hiện trạng Admin/POS, 20 nhóm lỗi và 8 mục Coming soon; giữ nguyên bằng chứng Sprint 2/BE1 lịch sử, không tự nâng thành nghiệm thu toàn sản phẩm. | `docs/bug-fixaffter/` |
| `v1.3.0` | `2026-10-30` | Nhóm dự án OISMS | Cập nhật 20/20 lỗi `BF-001..020` đã sửa, Upgrade 06 hoàn tất; thêm mục 0A.2, 0A.2B. *(Dòng lịch sử bổ sung 11/11/2026 theo trường phiên bản trước đó; bản gốc thiếu dòng này.)* | `OISMS-46` |
| `v1.4.0` | `2026-11-05` | Phùng Minh Nhựt — Backend 2 (chỉnh sửa sau review: Lê Võ Đăng Khoa) | Rà soát toàn diện VS Code, Docker, GitHub Actions CI, Jira/Checklist, Backend .NET 8 (ForwardedHeaders, CORS order, null safety, log encoding) và Frontend React 19 (Multi-unit LineKey, pagination race condition, receipt order matching). | `MASTER-REVIEW-OISMS-47` |
| `v1.5.0` | `2026-11-11` | Rà soát docs Sprint 4 | Thêm mục 0B hiện trạng 11/11/2026; cập nhật CS-01, CS-05, POS sau khi gộp, BF-003/015/019/020, BE2-F03, BE2-F04; ghi chú các mục 0, 0A, 1, 2, 3 đã đổi; bổ sung `NFR-USA-01` còn thiếu và thư mục docs mới. Xem [rà soát docs](docs/08-sprints/sprint-4/ra-soat-lech-docs-2026-11-11.md). | `RA-SOAT-DOCS-S4` |
| `v1.6.0` | `2026-11-12` | Rà soát mã nguồn | Cập nhật FR/NFR theo từng mã, ghi rõ phần đã có code và phần còn thiếu bằng chứng, bổ sung checklist gói công việc và hồ sơ bàn giao. | `DOC-OVW-03` |
| `v1.7.0` | `2026-11-12` | Rà soát tài liệu | Đưa tổng quan và lịch sử phiên bản lên đầu. Trình bày từng FR/NFR với tiêu chí, hiện trạng và file dẫn chứng. | `DOC-CHK-S2` |
| `v1.7.1` | `2026-11-12` | Rà soát tài liệu | Cập nhật dấu tick theo phạm vi năm gói công việc trong đề bài. | `DOC-CHK-S2` |
| `v1.7.2` | `2026-11-12` | Rà soát tài liệu | Liên kết kế hoạch Gói 5 và các tiêu chí còn mở của Gói 1–4. | `DOC-CHK-S2` |

## 3. Yêu cầu chức năng (Functional Requirements)

### 3.1. Đa khách thuê, xác thực và phân quyền (`FR-AUTH`) — Nhựt, Đăng Khoa

[Đặc tả](docs/02-functional-requirements/fr-01-auth-tenant.md)

- [x] **`FR-AUTH-01` — Đăng ký cửa hàng.** Tạo cửa hàng với `TenantId` duy nhất và cô lập dữ liệu.
  - **Hiện trạng.** API tạo Tenant và tài khoản Owner trong một giao dịch. Owner tạo chi nhánh và kho qua luồng quản lý tổ chức.
  - **Dẫn chứng.** [AuthService](src/backend/src/OISM.Application/Features/Auth/Services/AuthService.cs#L74)

- [ ] **`FR-AUTH-02` — Đăng nhập và đăng xuất.** Đăng nhập bằng email hoặc SĐT và mật khẩu, có đăng xuất. JWT mang định danh tenant, người dùng và vai trò.
  - **Hiện trạng.** Đã có đăng nhập bằng email, JWT, làm mới token, đăng xuất và kiểm tra trạng thái Tenant/User. API và hai màn hình đăng nhập hiện nhận email. Còn thiếu đăng nhập bằng SĐT.
  - **Dẫn chứng.** [AuthService](src/backend/src/OISM.Application/Features/Auth/Services/AuthService.cs#L144) · [Auth Admin](src/frontend/admin-dashboard/src/pages/AuthPage.tsx#L163) · [Auth POS](src/frontend/pos-app/src/pages/LoginPage.tsx#L21)

- [ ] **`FR-AUTH-03` — RBAC theo API và màn hình.** Phân quyền trên API và giao diện. Owner có toàn quyền, Staff quản lý kho và đơn, Cashier chỉ dùng POS.
  - **Hiện trạng.** Có ba vai trò và định tuyến POS riêng. API số dư tồn vẫn cho Cashier gọi và có thể trả `AverageCost`.
  - **Dẫn chứng.** [ma trận RBAC](docs/01-users-and-rbac/02-rbac-matrix.md#21-lệch-giữa-yêu-cầu-và-code-hiện-tại-đối-chiếu-06112026). [InventoryController](src/backend/src/OISM.WebApi/Controllers/InventoryController.cs#L39)

- [x] **`FR-AUTH-04` — Quản lý chi nhánh và kho.** Cho Owner tạo, sửa và bật/tắt chi nhánh và kho thuộc tenant mình.
  - **Hiện trạng.** Owner có thể tạo, sửa và bật/tắt. Quyền ghi kho của Staff được ghi ở `FR-AUTH-03`.
  - **Dẫn chứng.** [BranchController](src/backend/src/OISM.WebApi/Controllers/BranchController.cs) · [WarehousesController](src/backend/src/OISM.WebApi/Controllers/WarehousesController.cs)

### 3.2. Danh mục, SKU, mã vạch và giá (`FR-PROD`) — Nhựt, Phúc Hữu, Nam Nguyễn

[Đặc tả](docs/02-functional-requirements/fr-02-product-catalog.md)

- [x] **`FR-PROD-01` — Danh mục và thương hiệu.** Quản lý danh mục cha–con và thương hiệu.
  - **Hiện trạng.** Có cây danh mục cha–con, chặn vòng lặp và quản lý thương hiệu.
  - **Dẫn chứng.** [CategoryService](src/backend/src/OISM.Application/Features/Catalog/Categories/Services/CategoryService.cs) · [BrandService](src/backend/src/OISM.Application/Features/Catalog/Brands/Services/BrandService.cs) · [API catalog](docs/05-api-contracts/02-catalog-endpoints.md)

- [x] **`FR-PROD-02` — Sản phẩm và biến thể.** Quản lý sản phẩm đơn giản và sản phẩm có biến thể. Mã SKU duy nhất trong mỗi tenant.
  - **Hiện trạng.** Có sản phẩm đơn giản, biến thể và ràng buộc SKU duy nhất theo Tenant.
  - **Dẫn chứng.** [ProductService](src/backend/src/OISM.Application/Features/Catalog/Products/Services/ProductService.cs) · [Schema](docs/database/oisms_schema.sql)

- [ ] **`FR-PROD-03` — Mã vạch.** Sinh hoặc nhập mã vạch EAN-13/Code128 để quét tại POS và khi kiểm kê.
  - **Hiện trạng.** Có sinh EAN-13, nhập EAN-13/Code128 và quét tại POS. Màn kiểm kê hiện chọn SKU và nhập số đếm, chưa có luồng quét barcode.
  - **Dẫn chứng.** [Catalog POS](src/frontend/pos-app/src/components/pos/ProductCatalog.tsx) · [Phiếu kiểm kê](src/frontend/admin-dashboard/src/components/stockCounts/StockCountDetailDrawer.tsx)

- [x] **`FR-PROD-04` — Giá bán lẻ và bán sỉ theo SKU.** Thiết lập giá bán lẻ và bán sỉ theo SKU, lưu riêng với giá vốn.
  - **Hiện trạng.** Giá hiện hành lấy từ bảng giá và phiếu đổi giá, giá vốn lưu riêng. Endpoint cập nhật giá biến thể cũ trả `410 Gone`.
  - **Dẫn chứng.** [ProductService](src/backend/src/OISM.Application/Features/Catalog/Products/Services/ProductService.cs#L530) · [PricingController](src/backend/src/OISM.WebApi/Controllers/PricingController.cs)

### 3.3. Sổ cái và nghiệp vụ kho (`FR-INV`) — Đăng Khoa, Phúc Hữu, Nam Nguyễn

[Đặc tả](docs/02-functional-requirements/fr-03-inventory-ledger.md)

- [x] **`FR-INV-01` — Sổ cái tồn kho.** Ghi mọi biến động nhập, xuất, bán, trả, kiểm kê và chuyển kho vào sổ cái chỉ ghi thêm. Mỗi dòng có tenant, chi nhánh, SKU, loại IN/OUT, số lượng, số dư sau giao dịch, tham chiếu và thời gian. Cấm sửa tồn trực tiếp ngoài luồng ghi sổ.
  - **Hiện trạng.** Nhập, xuất bán, trả hàng, kiểm kê và chuyển kho ghi bút toán `IN/OUT` có chứng từ nguồn. Số dư vật lý thay đổi qua các giao dịch nghiệp vụ, sổ cái có trigger chỉ cho ghi thêm.
  - **Dẫn chứng.** [Thiết kế sổ kho](docs/02-functional-requirements/fr-03-inventory-ledger.md) · [Trigger](docs/database/oisms_schema.sql#L1176)

- [x] **`FR-INV-02` — Phiếu nhập từ nhà cung cấp.** Xác nhận phiếu nhập từ nhà cung cấp để tăng tồn vật lý và cập nhật giá vốn bình quân.
  - **Hiện trạng.** Admin lập phiếu từ phiếu mua đã duyệt. Xác nhận phiếu cập nhật tồn, ghi `IN` và tính WAC nguyên tử.
  - **Dẫn chứng.** [Luồng mua hàng–nhập kho](docs/report-after-upgrade/mua-hang-nhap-kho.md) · [Test PostgreSQL](test-oisms/backend/OISM.IntegrationTests/PurchaseOrderIntegrationTests.cs)

- [x] **`FR-INV-03` — Chuyển kho hai bước.** Chuyển hàng giữa hai chi nhánh qua hai bước xuất kho gửi và xác nhận nhập kho nhận.
  - **Hiện trạng.** Admin có luồng lập phiếu, xuất và nhận. Integration test xuất và nhận đều pass ngày 12/11/2026.
  - **Dẫn chứng.** [Giao diện chuyển kho](src/frontend/admin-dashboard/src/components/transfers/TransferDetailDrawer.tsx#L71) · [Test PostgreSQL](test-oisms/backend/OISM.IntegrationTests/StockTransferIntegrationTests.cs)

- [x] **`FR-INV-04` — Kiểm kê kho.** Tạo phiên kiểm kê, ghi số đếm thực tế, đối chiếu tồn hệ thống và ghi bút toán điều chỉnh.
  - **Hiện trạng.** Admin có kiểm kê theo lịch và đột xuất. Integration test sinh bút toán chênh lệch pass ngày 12/11/2026.
  - **Dẫn chứng.** [Giao diện kiểm kê](src/frontend/admin-dashboard/src/components/stockCounts/StockCountDetailDrawer.tsx) · [Test PostgreSQL](test-oisms/backend/OISM.IntegrationTests/StockCountIntegrationTests.cs)

- [ ] **Kiểm thử E2E chuyển kho và kiểm kê.** Chưa có biên bản cho toàn luồng UI trên bản hiện tại. [Kế hoạch kiểm thử](docs/06-testing-and-qa/02-unit-and-integration-test.md)

### 3.4. Giá vốn bình quân (`FR-COST`) — Đăng Khoa

[Đặc tả](docs/02-functional-requirements/fr-04-cost-wac.md)

- [x] **`FR-COST-01` — Tính WAC khi nhập.** Tự tính giá vốn bình quân gia quyền (WAC) theo SKU khi xác nhận phiếu nhập.
  - **Hiện trạng.** Xác nhận phiếu nhập tính lại giá vốn bình quân theo số lượng quy đổi về đơn vị gốc.
  - **Dẫn chứng.** [Luồng mua hàng–nhập kho](docs/report-after-upgrade/mua-hang-nhap-kho.md)

- [x] **`FR-COST-02` — Chụp giá vốn khi xuất.** Lưu giá vốn bình quân vào `OrderItem.CostPrice` khi xác nhận xuất đơn hoặc thanh toán POS.
  - **Hiện trạng.** Luồng xác nhận đơn online và thanh toán POS chụp giá vốn vào dòng đơn và bút toán xuất.
  - **Dẫn chứng.** [OrderService](src/backend/src/OISM.Application/Features/Orders/Services/OrderService.cs) · [PosCheckoutService](src/backend/src/OISM.Application/Features/Orders/Services/PosCheckoutService.cs)

### 3.5. Giữ chỗ và chống bán vượt (`FR-RSE`) — Đăng Khoa

[Đặc tả](docs/02-functional-requirements/fr-05-stock-reservation.md)

- [x] **`FR-RSE-01` — Tồn khả dụng.** Tính `available = on_hand - reserved` theo SKU tại từng chi nhánh.
  - **Hiện trạng.** API tồn kho trả tồn khả dụng theo chi nhánh và kho.
  - **Dẫn chứng.** [InventoryService](src/backend/src/OISM.Application/Features/Inventory/Services/InventoryService.cs)

- [x] **`FR-RSE-02` — Giữ hàng khi nhận đơn online.** Tăng tồn giữ chỗ khi nhận đơn online ở trạng thái `Reserved`. Từ chối nếu tồn khả dụng nhỏ hơn số lượng mua.
  - **Hiện trạng.** Tăng `reserved` trong giao dịch tạo đơn và trả `409` khi tồn khả dụng thiếu. Có khóa CSDL và chống gửi lặp.
  - **Dẫn chứng.** [OrderIntakeService](src/backend/src/OISM.Application/Features/Orders/Services/OrderIntakeService.cs) · [Test vòng đời đơn](test-oisms/backend/OISM.IntegrationTests/OnlineOrderLifecycleIntegrationTests.cs)

- [x] **`FR-RSE-03` — Xuất hoặc giải phóng giữ chỗ.** Khi xác nhận, giảm tồn vật lý và tồn giữ chỗ rồi ghi sổ xuất. Khi hủy, giải phóng tồn giữ chỗ.
  - **Hiện trạng.** Confirm giảm tồn vật lý và giữ chỗ, ghi `OUT`. Hủy hoặc hết hạn giải phóng giữ chỗ.
  - **Dẫn chứng.** [OrderService](src/backend/src/OISM.Application/Features/Orders/Services/OrderService.cs) · [Test hết hạn](test-oisms/backend/OISM.IntegrationTests/ReservationExpiryIntegrationTests.cs)

### 3.6. Đơn hàng đa kênh (`FR-ORD`) — Đăng Khoa, Nhựt, Phúc Hữu, Nam Nguyễn

[Đặc tả](docs/02-functional-requirements/fr-06-omnichannel-orders.md)

- [x] **`FR-ORD-01` — Mô hình đơn hợp nhất.** Chuẩn hóa đơn POS, đơn tạo tay trên Admin và webhook giả lập Shopee/TikTok vào một mô hình đơn chung.
  - **Hiện trạng.** Cả ba nguồn ghi vào `orders` và `order_items`.
  - **Dẫn chứng.** [Test truy vết POS/Admin/sàn](test-oisms/backend/OISM.IntegrationTests/SalesInvoiceIntegrationTests.cs#L113) · [Đơn Admin](src/frontend/admin-dashboard/src/pages/OrdersPage.tsx)

- [ ] **`FR-ORD-02` — Vòng đời đơn.** Áp dụng vòng đời `Draft → Reserved → Confirmed → Completed` và chuyển `Cancelled` khi hủy hợp lệ.
  - **Hiện trạng.** Đã có `Reserved → Confirmed → Completed`, hủy đơn trước xuất và chặn chuyển trạng thái sai. Đơn Admin/webhook mới được tạo trực tiếp ở `Reserved`. Bước `Draft` trong đề chưa thành một bước tạo đơn riêng của luồng hiện tại.
  - **Dẫn chứng.** [OrderIntakeService](src/backend/src/OISM.Application/Features/Orders/Services/OrderIntakeService.cs#L651) · [Test vòng đời đơn](test-oisms/backend/OISM.IntegrationTests/OnlineOrderLifecycleIntegrationTests.cs)

- [ ] **`FR-ORD-03` — Xử lý đơn.** Cho nhân viên xem đơn chờ, duyệt đơn, in phiếu giao hàng và hủy đơn.
  - **Hiện trạng.** API và Admin có danh sách, chi tiết, bộ lọc, duyệt, hoàn tất và hủy. Nút in chứng từ giao hàng còn Coming soon (CS-06).
  - **Dẫn chứng.** [OrdersController](src/backend/src/OISM.WebApi/Controllers/OrdersController.cs) · [Chi tiết đơn](src/frontend/admin-dashboard/src/components/orders/OrderDetailDrawer.tsx#L247)

### 3.7. POS (`FR-POS`) — Phương Lê, Đăng Khoa

[Đặc tả](docs/02-functional-requirements/fr-07-pos-checkout.md)

- [x] **`FR-POS-01` — Giao diện bán nhanh.** Tìm nhanh bằng tên, SKU hoặc mã vạch. Giao diện hỗ trợ cảm ứng và phím tắt.
  - **Hiện trạng.** `pos-app` tìm theo tên/SKU, quét mã vạch và có phím tắt F2/F4.
  - **Dẫn chứng.** [PosPage](src/frontend/pos-app/src/pages/PosPage.tsx) · [Catalog POS](src/frontend/pos-app/src/components/pos/ProductCatalog.tsx)

- [x] **`FR-POS-02` — Chặn bán vượt.** Kiểm tra tồn khả dụng khi chọn hàng và chặn bán vào tồn đã giữ cho đơn online.
  - **Hiện trạng.** Giỏ kiểm tra tồn khả dụng khi thêm/sửa số lượng. Backend khóa và kiểm tra lại trước khi thanh toán.
  - **Dẫn chứng.** [PosPage](src/frontend/pos-app/src/pages/PosPage.tsx) · [PosCheckoutService](src/backend/src/OISM.Application/Features/Orders/Services/PosCheckoutService.cs)

- [ ] **`FR-POS-03` — Thanh toán và in biên nhận.** Thanh toán tiền mặt hoặc chuyển khoản QR và tự mở hộp thoại in biên nhận của trình duyệt.
  - **Hiện trạng.** Đã có tiền mặt, VietQR xác nhận thủ công và biên nhận 58/80 mm. Sau checkout, POS mở bản xem trước khi chọn in. Hộp thoại trình duyệt mở sau khi thu ngân bấm **In**, còn thiếu bước tự mở theo đề.
  - **Dẫn chứng.** [VietQR](docs/report-after-upgrade/vietqr-cs05-review-pr39.md) · [Biên nhận POS](src/frontend/pos-app/src/components/pos/ReceiptPrintModal.tsx#L49)

- [x] **`FR-POS-04` — Checkout nguyên tử.** Giữ chỗ, xác nhận và trừ tồn trong một giao dịch cơ sở dữ liệu khi thanh toán POS.
  - **Hiện trạng.** Backend tạo đơn, ghi thanh toán và bút toán `OUT` trong một giao dịch. Integration test checkout nguyên tử pass ngày 12/11/2026. Có test POS xử lý khi chưa xác định được kết quả.
  - **Dẫn chứng.** [PosCheckoutService](src/backend/src/OISM.Application/Features/Orders/Services/PosCheckoutService.cs) · [Test backend](test-oisms/backend/OISM.IntegrationTests/PosCheckoutIntegrationTests.cs) · [Test POS](test-oisms/frontend/pos-app/test-checkout.mjs)

### 3.8. Webhook, SignalR và Hangfire (`FR-SIM`) — Đăng Khoa, Nhựt, nhóm frontend

[Đặc tả](docs/02-functional-requirements/fr-08-marketplace-sync.md) · [Đặc tả SignalR](docs/02-functional-requirements/fr-09-realtime-signalr.md) · [Đặc tả Hangfire](docs/02-functional-requirements/fr-10-background-hangfire.md)

- [x] **`FR-SIM-01` — Giả lập webhook Shopee/TikTok/Lazada.** Giả lập webhook Shopee, TikTok Shop và Lazada để kiểm thử thông lượng và khóa giữ chỗ.
  - **Hiện trạng.** Ứng dụng có chế độ gửi 1–200 đơn đồng thời và nhật ký kết quả HTTP.
  - **Dẫn chứng.** [Code gửi đồng thời](src/frontend/marketplace-simulator/src/App.tsx#L245) · [Hướng dẫn và test simulator](docs/14-webhook-simulator/README.md#5-kịch-bản-kiểm-thử)

- [ ] **`FR-SIM-02` — Thông báo đơn mới thời gian thực.** Dùng SignalR đẩy popup hoặc âm thanh báo đơn mới lên Admin và POS theo thời gian thực.
  - **Hiện trạng.** Admin nhận `OrderCreated` và có thông báo/âm thanh. POS nhận `StockLevelChanged`, chưa có popup/âm thanh đơn mới.
  - **Dẫn chứng.** [Thông báo Admin](src/frontend/admin-dashboard/src/components/NotificationCenter.tsx) · [SignalR POS](src/frontend/pos-app/src/services/realtimeService.ts#L63)

- [ ] **`FR-SIM-03` — Tác vụ nền.** Dùng Hangfire tự hủy đơn giữ chỗ quá hạn và tổng hợp báo cáo hằng ngày.
  - **Hiện trạng.** Đã có job hủy đơn hết hạn giữ chỗ và job sinh kỳ kiểm kê. Chưa có job tổng hợp báo cáo hằng ngày.
  - **Dẫn chứng.** [Cấu hình Hangfire](src/backend/src/OISM.Infrastructure/Jobs/HangfireConfigurationExtensions.cs#L93) · [Test hết hạn](test-oisms/backend/OISM.IntegrationTests/ReservationExpiryIntegrationTests.cs)

### 3.9. Báo cáo và cảnh báo (`FR-REP`) — Nhựt, Phúc Hữu, Nam Nguyễn, Đăng Khoa

[Đặc tả](docs/02-functional-requirements/fr-11-reports-analytics.md)

- [ ] **`FR-REP-01` — Doanh thu thuần, COGS và lãi gộp.** Tính doanh thu thuần, COGS và lãi gộp bằng doanh thu trừ COGS. Lọc theo thời gian, chi nhánh, kênh và SKU.
  - **Hiện trạng.** Có số liệu đơn `Confirmed` / `Completed`, khấu trừ hàng trả và lọc thời gian, chi nhánh, kênh. Còn thiếu lọc doanh thu/COGS theo SKU.
  - **Dẫn chứng.** [ReportService](src/backend/src/OISM.Application/Features/Reports/Services/ReportService.cs) · [Bộ lọc API](src/backend/src/OISM.Application/Features/Reports/Dtos/ReportDtos.cs#L3)

- [x] **`FR-REP-02` — Giá trị tồn và tốc độ luân chuyển.** Hiển thị giá trị tồn theo giá vốn, hàng bán chạy và hàng luân chuyển chậm.
  - **Hiện trạng.** API báo cáo và màn Admin đã hiển thị đủ ba nhóm số liệu.
  - **Dẫn chứng.** [ReportService](src/backend/src/OISM.Application/Features/Reports/Services/ReportService.cs#L322) · [Báo cáo Admin](src/frontend/admin-dashboard/src/pages/ReportsPage.tsx)

- [x] **`FR-REP-03` — Cảnh báo tồn thấp.** Tự cảnh báo trên Dashboard khi tồn khả dụng chạm hoặc thấp hơn ngưỡng của SKU.
  - **Hiện trạng.** Có ngưỡng theo SKU/chi nhánh, truy vấn `available <= threshold` và thông báo `LowStockWarning` trên Admin.
  - **Dẫn chứng.** [ReportService](src/backend/src/OISM.Application/Features/Reports/Services/ReportService.cs#L493) · [Thông báo tồn thấp](src/frontend/admin-dashboard/src/services/realtimeAdapter.ts#L110)

### 3.10. Dự báo và gợi ý nhập hàng (Gói 5) — Đăng Khoa, Nhựt, Phúc Hữu, Nam Nguyễn

[Đặc tả](docs/02-functional-requirements/fr-12-ai-forecasting.md)

- [x] **`FR-AI-01` (CS-08) — API và bảng gợi ý nhập hàng.** Dự báo nhu cầu và gợi ý nhập hàng dựa trên dữ liệu bán, tồn khả dụng và thời gian chờ cung ứng.
  - **Hiện trạng.** Dùng lịch sử xuất bán, tồn khả dụng và thời gian chờ cung ứng để tính gợi ý theo công thức.
  - **Dẫn chứng.** [AiForecastingService](src/backend/src/OISM.Application/Features/Ai/Services/AiForecastingService.cs#L186) · [Giao diện Kho](src/frontend/admin-dashboard/src/pages/InventoryPage.tsx#L145) · [Test dự báo](test-oisms/backend/OISM.UnitTests/AiForecastingTests.cs)

## 4. Yêu cầu phi chức năng (Non-functional Requirements)

### 4.1. Hiệu năng và tải

[Đặc tả](docs/03-non-functional-requirements/01-performance-and-load.md)

- [ ] **`NFR-PERF-01` — Thời gian phản hồi.** POS tìm SKU/quét barcode `p95 < 200 ms`. Tạo đơn/giao dịch ghi `< 500 ms`. Báo cáo tổng hợp `< 2 giây` với dưới `100.000` bản ghi.
  - **Hiện trạng.** Có phép đo POS Sprint 3 và báo cáo Sprint 5 ghi nhận thử tải 5.000 đơn local. Chưa có kết quả đo đủ cả ba ngưỡng cho bản hiện tại.
  - **Dẫn chứng.** [Đặc tả hiệu năng](docs/03-non-functional-requirements/01-performance-and-load.md) · [Script dữ liệu thử tải](test-oisms/api-python/seed_revenue_perf.py)

- [x] **`NFR-PERF-02` — 50 đơn đồng thời tranh 1 đơn vị tồn cuối.** Dùng khóa dòng hoặc kiểm tra phiên bản để xử lý 50 đơn online đồng thời tranh một đơn vị tồn cuối. Tồn khả dụng không được âm.
  - **Hiện trạng.** Test PostgreSQL pass ngày 12/11/2026 với 50 webhook Shopee, đúng một đơn thành công, 49 trả `409` và tồn khả dụng về 0. Chưa chạy lại kịch bản trên AWS.
  - **Dẫn chứng.** [Integration test 50 đơn](test-oisms/backend/OISM.IntegrationTests/WebhookIntegrationTests.cs#L1773)

### 4.2. Cô lập tenant và khả năng mở rộng

[Đặc tả](docs/03-non-functional-requirements/02-security-and-isolation.md)

- [x] **`NFR-TENANT-01` — Cô lập dữ liệu.** Dùng cơ sở dữ liệu chung với cơ chế định danh tenant. Bảng nghiệp vụ có `TenantId` và EF Core Global Query Filters để cô lập dữ liệu.
  - **Hiện trạng.** Có kiểm soát ghi và khóa ngoại ghép theo tenant. Integration test lọc dữ liệu giữa tenant pass ngày 12/11/2026.
  - **Dẫn chứng.** [AppDbContext](src/backend/src/OISM.Infrastructure/Persistence/AppDbContext.cs) · [Test cô lập](test-oisms/backend/OISM.IntegrationTests/PostgresTenantIsolationTests.cs)

- [x] **`NFR-TENANT-02` — Chỉ mục theo tenant.** Có chỉ mục tenant/thời gian và tenant/SKU cho truy vấn sổ kho và đơn hàng ở quy mô hàng triệu dòng sổ cái.
  - **Hiện trạng.** Có chỉ mục thời gian trên ledger và orders, chỉ mục SKU trên ledger và `order_items`. Chưa có phép đo quy mô hàng triệu dòng sổ cái.
  - **Dẫn chứng.** [Chỉ mục PostgreSQL](docs/database/oisms_schema.sql#L1143)

### 4.3. Bảo mật, giao dịch và sổ cái

[Đặc tả](docs/03-non-functional-requirements/03-reliability-and-acid.md)

- [ ] **`NFR-SEC-01` — Mã hóa và xác thực.** Băm mật khẩu bằng BCrypt hoặc Argon2, dùng HTTPS/TLS 1.3 và JWT 60 phút kèm refresh token an toàn.
  - **Hiện trạng.** Có BCrypt, JWT 60 phút, refresh token xoay vòng và HTTPS qua Caddy. Tài liệu triển khai ghi hỗ trợ TLS 1.2/1.3. Chưa có kết quả xác nhận TLS 1.3 trên bản deploy.
  - **Dẫn chứng.** [Hiện trạng bảo mật](docs/03-non-functional-requirements/README.md#hiện-trạng-đáp-ứng-đối-chiếu-12112026) · [Caddyfile](docker/caddy/Caddyfile)

- [ ] **`NFR-SEC-02` — Giao dịch nguyên tử.** Chuỗi tạo đơn, giữ chỗ và trừ tồn chạy nguyên tử và hoàn tác toàn bộ khi gặp lỗi.
  - **Hiện trạng.** Checkout POS và xác nhận nhập kho có giao dịch nguyên tử và rollback. Đơn online có giao dịch riêng cho tạo đơn/giữ chỗ và xác nhận xuất sau đó. Cần chốt cách diễn giải yêu cầu một giao dịch của đề đối với đơn online.
  - **Dẫn chứng.** [Ghi chú đề bài về vòng đời](docs/08-sprints/sprint-2/DE_BAI_OISM.md#6-điểm-cần-làm-rõ-trước-khi-chốt-thiết-kế) · [Test vòng đời đơn](test-oisms/backend/OISM.IntegrationTests/OnlineOrderLifecycleIntegrationTests.cs)

- [x] **`NFR-SEC-03` — Sổ cái append-only.** Sổ cái chỉ ghi thêm. Điều chỉnh sai sót bằng bút toán bù, cấm cập nhật và xóa dòng cũ.
  - **Hiện trạng.** Trigger chặn `UPDATE`, `DELETE` và `TRUNCATE`. Điều chỉnh tồn ghi thêm bút toán qua nghiệp vụ kho.
  - **Dẫn chứng.** [Trigger PostgreSQL](docs/database/oisms_schema.sql#L1176) · [Test kiểm kê](test-oisms/backend/OISM.IntegrationTests/StockCountIntegrationTests.cs)

### 4.4. Độ sẵn sàng và khả năng sử dụng POS

[Đặc tả](docs/03-non-functional-requirements/04-usability-and-pwa.md)

- [ ] **`NFR-USA-01` — Uptime 99,5% từ 07:00 đến 22:00.** Uptime đạt 99,5% trong khung 07:00–22:00.
  - **Hiện trạng.** Có chính sách khởi động lại container. Chưa có số đo uptime theo khung giờ đề bài.
  - **Dẫn chứng.** [Hiện trạng triển khai](docs/03-non-functional-requirements/README.md#hiện-trạng-đáp-ứng-đối-chiếu-12112026)

- [ ] **`NFR-USA-02` — Thao tác và thiết bị POS.** Thanh toán dưới ba lần bấm hoặc bằng quét barcode và Enter. Giao diện đáp ứng desktop, tablet và máy POS cầm tay.
  - **Hiện trạng.** Có quét barcode + Enter để thêm hàng, F4 để thanh toán và giao diện responsive. Chưa có kết quả nghiệm thu toàn luồng thanh toán dưới 3 lần bấm hoặc theo chuỗi quét/phím trên các thiết bị yêu cầu.
  - **Dẫn chứng.** [Catalog POS](src/frontend/pos-app/src/components/pos/ProductCatalog.tsx#L43) · [Phím tắt](src/frontend/pos-app/src/pages/PosPage.tsx#L94)

### 4.5. Kiến trúc và độ phủ kiểm thử

[Đặc tả](docs/03-non-functional-requirements/05-maintainability-testing.md)

- [ ] **`NFR-MAINT-01` — Clean Architecture.** Tách Domain, Application, Infrastructure và Presentation. Domain giữ nghiệp vụ lõi, Application chứa ca sử dụng, Infrastructure chứa tích hợp và Presentation phục vụ API.
  - **Hiện trạng.** Có bốn project Domain, Application, Infrastructure và Web API. Một số dịch vụ WAC/Reserve và truy vấn EF Core hiện nằm trong Application.
  - **Dẫn chứng.** [Đặc tả phân tầng](docs/03-non-functional-requirements/05-maintainability-testing.md) · [Hiện trạng kiến trúc](docs/03-non-functional-requirements/README.md#hiện-trạng-đáp-ứng-đối-chiếu-12112026)

- [ ] **`NFR-MAINT-02` — Coverage tối thiểu 80% cho Ledger, Anti-Oversell Reserve và COGS.** Coverage Unit và Integration tối thiểu 80% cho Ledger, Anti-Oversell Reserve và COGS.
  - **Hiện trạng.** Mốc Sprint 2 ghi nhận 92,49–94,83% cho nhóm Inventory. Chưa đo lại coverage unit và integration của đủ ba module trên bản hiện tại.
  - **Dẫn chứng.** [Đặc tả coverage](docs/03-non-functional-requirements/05-maintainability-testing.md) · [RTM](docs/06-testing-and-qa/01-requirements-matrix-rtm.md)

## 5. Gói công việc và hồ sơ bàn giao

### 5.1. Năm gói công việc

Dấu tick ở mục này theo các đầu việc được liệt kê cho từng gói trong đề bài. Tiêu chí chi tiết theo từng mã có trạng thái riêng ở mục 3 và 4. [Phạm vi các gói](docs/08-sprints/sprint-2/DE_BAI_OISM.md#5-các-gói-công-việc-trong-pdf-trang-5-6) · [Kế hoạch phần còn mở](docs/Upgrade-recommended/Pedding/README.md)

- [x] **Gói 1 — Nền tảng.** Đã triển khai cấu trúc bốn tầng, schema PostgreSQL, cô lập tenant, JWT và RBAC. Đăng nhập SĐT và các điểm lệch về quyền, phân tầng được theo dõi tại `FR-AUTH-02`, `FR-AUTH-03` và `NFR-MAINT-01`. [Kiến trúc backend](docs/04-architecture-and-design/01-clean-architecture.md) · [Schema](docs/database/oisms_schema.sql)
- [x] **Gói 2 — Catalog và sổ cái.** Có sản phẩm, SKU, mã vạch, bảng giá, phiếu nhập, chuyển kho và WAC. [API catalog](docs/05-api-contracts/02-catalog-endpoints.md) · [Test chuyển kho](test-oisms/backend/OISM.IntegrationTests/StockTransferIntegrationTests.cs)
- [x] **Gói 3 — Đơn hàng và chống bán vượt.** Đã triển khai mô hình đơn chung, chuyển trạng thái, khóa giữ chỗ, giả lập webhook và Hangfire hết hạn. Bước Draft riêng và job báo cáo hằng ngày được theo dõi tại `FR-ORD-02` và `FR-SIM-03`. [Test vòng đời đơn](test-oisms/backend/OISM.IntegrationTests/OnlineOrderLifecycleIntegrationTests.cs) · [Test 50 webhook](test-oisms/backend/OISM.IntegrationTests/WebhookIntegrationTests.cs#L1773)
- [x] **Gói 4 — Frontend và realtime.** Đã triển khai Admin, PWA POS, checkout nguyên tử, in qua trình duyệt và SignalR. Tự mở hộp thoại in và thông báo đơn mới trên POS được theo dõi tại `FR-POS-03` và `FR-SIM-02`. [Test checkout POS](test-oisms/backend/OISM.IntegrationTests/PosCheckoutIntegrationTests.cs) · [SignalR POS](src/frontend/pos-app/src/services/realtimeService.ts#L63)
- [ ] **Gói 5 — Báo cáo, AI, test và triển khai.** Có báo cáo, gợi ý nhập hàng, Docker, CI và bộ kiểm thử. Hồ sơ bàn giao ở mục 5.2 còn hạng mục chưa đạt. Lọc theo SKU, đánh giá dự báo và các phép đo NFR còn mở ở mục 3.9, 4 và 6.1. [RTM](docs/06-testing-and-qa/01-requirements-matrix-rtm.md) · [Báo cáo nghiệm thu Sprint 5](docs/08-sprints/sprint-5/BAO_CAO_TONG_KET_NGHIEM_THU_SPRINT_5.md)

### 5.2. Sáu nhóm hồ sơ nghiệm thu

- [ ] **1. URS và RTM.** Có [đặc tả FR](docs/02-functional-requirements/README.md), [NFR](docs/03-non-functional-requirements/README.md), [RBAC](docs/01-users-and-rbac/README.md) và [RTM](docs/06-testing-and-qa/01-requirements-matrix-rtm.md). RTM còn nhóm nhiều mã và chưa có bằng chứng nghiệm thu theo từng FR/NFR.
- [ ] **2. Tài liệu kiến trúc.** Có [Clean Architecture](docs/04-architecture-and-design/01-clean-architecture.md) và [thiết kế đa tenant](docs/04-architecture-and-design/02-multi-tenant-design.md). Package Diagrams mục 1 SDD còn cần rà soát theo báo cáo Sprint 5.
- [ ] **3. Thiết kế chi tiết UML 2.0.** Có [ERD](docs/04-architecture-and-design/03-database-design.md), [máy trạng thái](docs/04-architecture-and-design/04-state-machines.md) và [sequence diagrams](docs/04-architecture-and-design/sequence-diagrams/). SDD 3.4–3.6 và 3.10–3.11 chưa đạt theo [báo cáo Sprint 5](docs/08-sprints/sprint-5/BAO_CAO_TONG_KET_NGHIEM_THU_SPRINT_5.md#b-phân-hệ-tài-liệu-thiết-kế-chi-tiết-sdd-nhóm-3-bạn-tài-liệu--hỗ-trợ--jira-oisms-5359).
- [x] **4. Đặc tả API.** Có [API contracts và Swagger ở Development](docs/05-api-contracts/README.md).
- [x] **5. Tài liệu kiểm thử.** Có [kế hoạch unit/integration](docs/06-testing-and-qa/02-unit-and-integration-test.md), [kịch bản concurrency](docs/06-testing-and-qa/03-concurrency-stress-test.md) và [tiêu chí nghiệm thu](docs/06-testing-and-qa/04-acceptance-criteria-dod.md). Kết quả đo hiện tại xem mục 4.
- [ ] **6. Hiện thực, triển khai và hướng dẫn sử dụng.** Có [Docker Compose](docker-compose.yml), [seed demo](src/backend/src/OISM.Infrastructure/Persistence/OismsDatabaseSeeder.cs), [CI](.github/workflows/ci.yml), [hướng dẫn cài đặt](docs/07-devops-and-deployment/README.md) và [triển khai AWS](docs/13-deploy-aws/README.md). Chưa đối chiếu đủ hướng dẫn sử dụng cho các luồng Admin/POS.

## 6. Yêu cầu bổ sung

### 6.1. Chức năng sản phẩm

- [x] **CS-01 / Upgrade 08 — Trả hàng từ khách.** Có phiếu trả liên kết đơn gốc, hoàn kho theo giá vốn lúc bán, tính lại WAC và khấu trừ doanh thu. [Đặc tả](docs/Upgrade-recommended/Done/08-tra-hang-cs01.md) · [API](src/backend/src/OISM.WebApi/Controllers/SalesReturnsController.cs) · [Màn hình Admin](src/frontend/admin-dashboard/src/pages/SalesReturnsPage.tsx)
- [ ] **CS-02 — Nhân viên và phân công chi nhánh.** Có danh sách, tạo, sửa, khóa và gán vai trò Staff/Cashier. Còn thiếu phân công chi nhánh. [API nhân viên](src/backend/src/OISM.WebApi/Controllers/UsersController.cs) · [Ma trận quyền](docs/01-users-and-rbac/02-rbac-matrix.md)
- [x] **CS-03 — Cấu hình ngưỡng tồn thấp.** Có API và UI lưu ngưỡng theo SKU và chi nhánh. [API tồn kho](src/backend/src/OISM.WebApi/Controllers/InventoryController.cs#L120) · [Màn hình Kho](src/frontend/admin-dashboard/src/pages/InventoryPage.tsx)
- [x] **CS-04 — Tìm kiếm toàn hệ thống.** Có ô tìm kiếm và API. Điều hướng kết quả phiếu nhập còn được theo dõi ở BF-024. [API tìm kiếm](src/backend/src/OISM.WebApi/Controllers/SearchController.cs) · [BF-024](docs/bug-fixaffter/errors/BF-024-tim-kiem-phieu-nhap-khong-mo-dung-phieu.md)
- [x] **CS-05 / Upgrade 07 — VietQR ở POS.** Owner quản lý tài khoản nhận tiền, thu ngân xác nhận đã nhận tiền, giao dịch lưu thông tin người nhận. [Đặc tả](docs/Upgrade-recommended/Done/07-thanh-toan-qr-vietqr.md) · [Nghiệm thu VietQR](docs/report-after-upgrade/vietqr-cs05-review-pr39.md)
- [ ] **Upgrade 07, giai đoạn C — Xác nhận chuyển khoản tự động.** Webhook ngân hàng đang tạm gác. [Kế hoạch VietQR](docs/Upgrade-recommended/Done/07-thanh-toan-qr-vietqr.md)
- [ ] **CS-06 — In chứng từ giao hàng và in báo cáo.** Hai luồng còn Coming soon. Đã có in biên nhận POS và in lại hóa đơn bán. [Chi tiết đơn](src/frontend/admin-dashboard/src/components/orders/OrderDetailDrawer.tsx#L247) · [Báo cáo Admin](src/frontend/admin-dashboard/src/pages/ReportsPage.tsx) · [Bàn giao hóa đơn](docs/report-after-upgrade/hoa-don-truy-vet-xuat-kho.md)
- [x] **CS-07 — Tổng quan từ dữ liệu thật.** KPI, biểu đồ và đơn gần đây lấy từ backend. Số liệu hiện theo phạm vi lũy kế của API. [Màn Tổng quan](src/frontend/admin-dashboard/src/pages/DashboardPage.tsx) · [ReportService](src/backend/src/OISM.Application/Features/Reports/Services/ReportService.cs)
- [ ] **CS-08 — Đánh giá dự báo.** API và tab gợi ý đã triển khai theo `FR-AI-01`. Còn thiếu kết quả đối chiếu dự báo với dữ liệu thực tế và tiêu chí chất lượng. [Đặc tả dự báo](docs/02-functional-requirements/fr-12-ai-forecasting.md) · [Test công thức](test-oisms/backend/OISM.UnitTests/AiForecastingTests.cs)
- [x] **`FR-AI-02` / Upgrade 09 — Trợ lý OISMS.** Có API và panel hỏi đáp chỉ đọc cho Owner/Staff. Mặc định tắt ở local và đã bật trên bản demo AWS theo báo cáo Sprint 5. [Đặc tả trợ lý](docs/02-functional-requirements/fr-13-ai-assistant.md) · [API](src/backend/src/OISM.WebApi/Controllers/AiController.cs#L26) · [Unit test](test-oisms/backend/OISM.UnitTests/AiChatServiceTests.cs)
- [ ] **Upgrade 10 — Quên mật khẩu và đăng nhập bằng SĐT.** Còn thiếu luồng đặt lại mật khẩu và đăng nhập SĐT. Trạng thái đăng nhập xem `FR-AUTH-02`. [Kế hoạch](docs/Upgrade-recommended/Pedding/10-quen-mat-khau-dang-nhap-sdt.md)

### 6.2. Nâng cấp nghiệp vụ và kiến trúc

- [x] **Upgrade 01-A — Phiếu mua và nhập kho.** Có duyệt phiếu mua, gắn phiếu nhập, chặn nhập vượt và lấy giá từ phiếu mua. [Báo cáo](docs/report-after-upgrade/mua-hang-nhap-kho.md) · [Integration test](test-oisms/backend/OISM.IntegrationTests/PurchaseOrderIntegrationTests.cs)
- [x] **Upgrade 01-B — Hóa đơn nhà cung cấp và chênh lệch.** Có hóa đơn NCC gắn phiếu nhập, đối chiếu ba bên, chênh lệch Owner duyệt; hóa đơn kèm hàng đã duyệt quyết định giá nhập, hóa đơn đến sau chỉ lưu bằng chứng. Không gồm công nợ, thanh toán NCC. Nghiệm thu 12/11/2026 trên PostgreSQL thật, chưa chạy trên bản demo AWS. [Đặc tả](docs/Upgrade-recommended/Done/01-mua-hang-nhap-kho-gia-von.md) · [Báo cáo](docs/report-after-upgrade/hoa-don-nha-cung-cap-u01b.md)
- [x] **Upgrade 02 — Truy vết xuất kho.** Có chứng từ nguồn cho dòng OUT, hóa đơn bán, màn xem và in lại. [Báo cáo](docs/report-after-upgrade/hoa-don-truy-vet-xuat-kho.md) · [Integration test](test-oisms/backend/OISM.IntegrationTests/SalesInvoiceTraceIntegrationTests.cs)
- [x] **Upgrade 03 — Lịch kiểm kê và duyệt chênh lệch.** Có lịch giữa tháng, cuối tháng, kiểm kê đột xuất và duyệt chênh lệch. Hangfire sinh kỳ tự động khi bật. [Báo cáo](docs/report-after-upgrade/lich-kiem-ke-duyet-chenh-lech.md) · [Integration test](test-oisms/backend/OISM.IntegrationTests/StockCountU03IntegrationTests.cs)
- [x] **Upgrade 04 — Bảng giá theo hiệu lực.** Có phiếu đổi giá, phê duyệt, lịch sử và snapshot giá vào dòng đơn. [Báo cáo](docs/report-after-upgrade/bang-gia-theo-thoi-gian.md) · [RTM bảng giá](docs/06-testing-and-qa/01-requirements-matrix-rtm.md#2-truy-vết-upgrade-04--bảng-giá-theo-thời-gian-oisms-47-u04)
- [x] **Upgrade 05 — Nhiều đơn vị tính.** Có đơn vị gốc, hệ số quy đổi theo SKU và snapshot số lượng/hệ số vào chứng từ. [Đặc tả](docs/Upgrade-recommended/Done/05-nhieu-don-vi-tinh.md) · [Báo cáo giao diện](docs/report-after-upgrade/frontend-coming-soon-va-upgrade.md) · [Integration test](test-oisms/backend/OISM.IntegrationTests/MultiUnitIntegrationTests.cs)
- [x] **Upgrade 06 — Tách nghiệp vụ khỏi controller.** Đã hoàn thành phạm vi refactor của đợt nâng cấp. Các điểm còn lệch đặc tả kiến trúc theo dõi ở `NFR-MAINT-01`. [Báo cáo refactor](docs/report-after-upgrade/clean-architecture.md)

### 6.3. Triển khai và hồi quy

- [x] **Đóng gói local và production.** Có Docker Compose, PostgreSQL, backend, Admin, POS, simulator và Caddy production. [Compose local](docker-compose.yml) · [Compose production](docker-compose.prod.yml) · [Caddyfile](docker/caddy/Caddyfile)
- [x] **CI tự động.** Có build và test backend với PostgreSQL, build các ứng dụng frontend và chạy test POS/simulator. [GitHub Actions](.github/workflows/ci.yml)
- [x] **Gộp POS về một ứng dụng.** `pos-app` phục vụ giao diện quầy. Admin `/pos` chuyển hướng. [Báo cáo tách POS](docs/report-after-upgrade/tach-pos-khoi-admin-2026-11-08.md) · [Điều hướng Admin](src/frontend/admin-dashboard/src/App.tsx#L32)
- [x] **Tác vụ nền trên bản demo AWS.** Báo cáo ghi Hangfire đã bật ngày 11/11/2026, có job hết hạn giữ chỗ và sinh kỳ kiểm kê. [Hiện trạng Hangfire](docs/02-functional-requirements/fr-10-background-hangfire.md) · [Tài liệu triển khai](docs/13-deploy-aws/README.md)
- [ ] **Hồi quy POS BF-019 và BF-020.** Cần kiểm thử riêng phản hồi HTTP 200 sai hợp đồng và checkout cũ khi đổi chi nhánh. [BF-019](docs/bug-fixaffter/errors/BF-019-pos-nhan-http-200-sai-contract-la.md) · [BF-020](docs/bug-fixaffter/errors/BF-020-pos-rieng-chua-khoa-gio-va-ngu.md)

[Toàn bộ lỗi và bằng chứng sửa lỗi](docs/bug-fixaffter/CHECKLIST.md) · [Danh mục tài liệu dự án](docs/README.md)
