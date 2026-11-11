# 04. Đặc Tả Hợp Đồng API Đơn Hàng Đa Kênh, Webhook & Báo Cáo (Order, Webhook & Report Endpoints)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-API-04` |
| **Phiên bản (Version)** | `v1.1.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-06` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-ORD-01..03`, `FR-SIM-01`, `FR-REP-01..03`, `FR-AI` (Trang 2, 3, 6) |
| **Hợp đồng chi tiết Sprint 3** | [`../08-sprints/sprint-3/01-api-contracts-sprint-3.md`](../08-sprints/sprint-3/01-api-contracts-sprint-3.md) |
| **Phạm vi áp dụng** | Trung tâm Đơn hàng (`Orders`), Trình giả lập Webhook (`Simulator`), Báo cáo (`Reports`) & Dự báo AI |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.3.1` | `2026-11-10` | Nhóm dự án OISMS | Thêm `GET /api/webhooks/simulator/catalog` cho app giả lập chọn sản phẩm theo secret. | `OISMS-48` |
| `v1.3.0` | `2026-11-09` | Nhóm dự án OISMS | OISMS-58 (CS-01): Bổ sung endpoints Phiếu trả hàng `/api/sales-returns` (lập draft, xác nhận nhập kho, hủy, lấy dòng có thể trả); chi tiết đơn có `returnedQuantity`; truy vết chứng từ có `salesReturns` và dòng sổ `IN`; báo cáo doanh thu/lợi nhuận và dashboard summary phản ánh số thuần (`returnAmount`, `netRevenue`, `returnCogs`, `netCogs`) và ẩn thông tin lãi gộp/giá vốn (`null`) với Staff. | `OISMS-58-CS01-P2` |
| `v1.2.0` | `2026-11-06` | Nhóm dự án OISMS | Thêm API quản lý kết nối gian hàng sàn `/api/shop-integrations` (Owner): tạo, sửa, đổi secret webhook. Thêm `GET /api/webhooks/simulator/whoami`. Webhook tra secret theo hash. | `OISMS-48` |
| `v1.1.0` | `2026-11-06` | Nhóm dự án OISMS | Đối chiếu controller: thêm `process`, `simulator/batch`, `dashboard-summary`, `/api/search`, `/api/ai/export-history`; sửa quyền báo cáo theo code và ghi lệch RBAC lãi gộp; Hangfire mặc định tắt. | `OISMS-46` |
| `v1.0.5` | `2026-11-04` | Nhóm dự án OISMS | `GET /api/reports/inventory-velocity` thêm `quantityByUnit` và `baseUnitName`; `currentOnHand` cộng theo SKU trên phạm vi chi nhánh đang lọc. | `OISMS-46` |
| `v1.0.4` | `2026-10-26` | Đăng Khoa (Backend 1) | **Hoàn thiện Task 6 v1.0:** Tác vụ nền Hangfire tự động hủy đơn giữ chỗ quá hạn 15 phút (`oisms-reservation-expiry`, cadence `*/1 * * * *` UTC). Chuẩn hóa ranh giới hết hạn `ExpiresAt <= cutoffUtc`, giải phóng `Reserved` hai cấp (chi nhánh & kho) nguyên tử theo toàn giỏ, phát sự kiện SignalR `StockLevelChanged` sau khi database transaction commit thành công. | `SYNC-S3-T06-V10` |
| `v1.0.3` | `2026-10-26` | Đăng Khoa (Backend 1) | **Hoàn thiện Task 5 v1.2:** Chuẩn hóa validation omission scalar `channel` & `unitPrice` (trả 400 `VALIDATION_ERROR` khi omit, chấp nhận explicit `unitPrice: 0` khi `totalAmount: 0`), Fast Replay đơn Admin khi chi nhánh ngừng hoạt động trả `200 OK`, đồng bộ phát sự kiện SignalR (`OrderCreated`, `StockLevelChanged`, `LowStockWarning`) sau khi database transaction commit thành công. | `SYNC-S3-T05-V12` |
| `v1.0.2` | `2026-10-24` | Đăng Khoa (Backend 1) | Bổ sung thao tác hoàn tất đơn hàng online `POST /api/orders/{id}/complete` (`Confirmed -> Completed`), cập nhật link tương đối và quy tắc bảo mật webhook/idempotency. | `SYNC-S3-COMPLETE-ORD` |
| `v1.0.1` | `2026-10-24` | Đăng Khoa (Backend 1) | Cập nhật hợp đồng chi tiết Sprint 3: Bổ sung endpoint alias `POST /api/orders/checkout`, chi tiết máy trạng thái `Order State Machine` và liên kết tới `DOC-CTR-S3-01`. | `SYNC-S3-ORDERS` |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả hợp đồng API cho Trung tâm Đơn hàng đa kênh, Webhook Simulator, Báo cáo lãi gộp và Gợi ý AI. | `INIT-API-04` |

---

## 1. Danh Mục Endpoints Đơn Hàng Đa Kênh & Webhook (`FR-ORD-01..03`, `FR-SIM-01`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `GET` | `/api/orders` | `Owner, Staff` | `FR-ORD-03` | Lấy danh sách đơn hàng hợp nhất (`Canonical Orders`), lọc theo Trạng thái, Kênh bán, Chi nhánh và Thời gian. |
| `GET` | `/api/orders/{id}` | `Owner, Staff` | `FR-ORD-03` | Lấy chi tiết đơn hàng kèm danh sách mặt hàng, giá vốn WAC snapshot, người xác nhận (`confirmedBy`), thông tin hóa đơn (`salesInvoiceId`, `salesInvoiceNumber`), thanh toán và số lượng đã trả từng dòng (`returnedQuantity` tính từ các phiếu trả `Confirmed`). |
| `GET` | `/api/orders/{id}/trace` | `Owner, Staff` | `OISMS-47-U02, CS-01` | Truy vết chuỗi chứng từ: đơn hàng, hóa đơn xuất kho, dòng sổ kho `OUT` và danh sách phiếu trả hàng (`salesReturns`) kèm các dòng sổ kho `IN` (`DocumentTraceDto`). |
| `POST` | `/api/orders` | `Owner, Staff` | `FR-ORD-01..02` | Tạo đơn hàng thủ công trên Admin và thực hiện giữ chỗ tồn kho (`Draft -> Reserved`). Bắt buộc `RequestKey` hợp lệ do client cung cấp. |
| `POST` | `/api/orders/{id}/confirm` | `Owner, Staff` | `FR-ORD-02..03`, `FR-RSE-03`, `FR-COST-02` | Duyệt đơn hàng (`Reserved -> Confirmed`): Trừ `on_hand`, giảm `reserved`, ghi Sổ cái `OUT` và chụp `OrderItem.CostPrice`. |
| `POST` | `/api/orders/{id}/process` | `Owner, Staff` | `FR-ORD-02` | Xử lý đóng gói đơn `Reserved`: gọi lại luồng Confirm; đơn đã `Confirmed` trả nguyên chi tiết; `Cancelled`/`Completed` trả `409 INVALID_STATE_TRANSITION`. |
| `POST` | `/api/orders/{id}/complete` | `Owner, Staff` | `FR-ORD-02..03` | **Hoàn tất đơn online (`Confirmed -> Completed`):** Ghi nhận khách đã nhận hàng thành công. **Tuyệt đối KHÔNG trừ tồn kho hay ghi ledger OUT lần thứ hai** (vì đã ghi khi Confirm). Gọi lặp lại trên đơn đã `Completed` là idempotent (trả về 200 OK). Đơn chưa Confirmed chuyển sang Completed trả `409 Conflict` (`INVALID_STATE_TRANSITION`). |
| `POST` | `/api/orders/{id}/cancel` | `Owner, Staff` | `FR-ORD-02..03`, `FR-RSE-03` | Hủy đơn hàng (`Reserved -> Cancelled`): Giải phóng `reserved` về lại quỹ `available`. |
| `POST` | `/api/orders/checkout`<br/>*(Canonical: `/api/pos/checkout`)* | `Owner, Staff, Cashier` | `FR-POS-03`, `FR-POS-04`, `FR-COST-02` | Endpoint alias tương thích ngược phục vụ Web POS PWA và Admin POS, trỏ vào cùng lõi `IOrderService.CheckoutPosAsync`. Trả về `PosReceiptDto` không lộ giá vốn. |
| `POST` | `/api/webhooks/simulator/{channel}` | `Owner, Staff / Webhook Key` | `FR-SIM-01`, `FR-ORD-01` | **Tiếp nhận đơn TMĐT (Shopee, TikTok, Lazada):**<br/>- Xác thực kép: Header `X-Webhook-Secret` (production/simulator webhook) hoặc `Bearer JWT` (`Owner`, `Staff`; `Cashier` bị từ chối 403 `FORBIDDEN`). Tuyệt đối không đọc `TenantId` từ request body.<br/>- Tự động ánh xạ chi nhánh fulfillment từ bảng `shop_integrations` theo `(tenant_id, channel, external_shop_id)`.<br/>- Khóa chống trùng: `(tenant_id, channel, external_shop_id, external_order_id)`. Cùng khóa + cùng payload &rarr; 200 OK replay; Cùng khóa + khác payload &rarr; 409 Conflict (`WEBHOOK_PAYLOAD_MISMATCH`).<br/>- Kênh URL khác payload channel &rarr; 400 Bad Request (`CHANNEL_MISMATCH`).<br/>- Áp dụng đơn giá tin cậy từ sàn TMĐT, kiểm tra tổng tiền và làm tròn `AwayFromZero` (lệch trả 400 `TOTAL_AMOUNT_MISMATCH`).<br/>- Giữ chỗ tồn kho bằng lõi chung `CreateReservedOrderCoreAsync`: tạo đơn `Reserved`, sinh `Reservation` `Active` 15 phút, tăng `Reserved` ở kho/chi nhánh, OnHand/WAC không đổi. |
| `GET` | `/api/webhooks/simulator/whoami` | `Webhook Key` | `FR-SIM-01` | Header `X-Webhook-Secret` bắt buộc, không cần JWT. Trả 200 `{ channel, externalShopId, branchName, branchIsActive }` để công cụ giả lập tự điền. Không trả `tenantId`, `branchId`. Thiếu hoặc sai secret, kết nối tắt, doanh nghiệp ngừng hoạt động: 401 `UNAUTHORIZED`. Chi nhánh tắt vẫn trả 200 với `branchIsActive: false`. Xem [`DOC-SIM-14`](../14-webhook-simulator/README.md). |
| `GET` | `/api/webhooks/simulator/catalog` | `Webhook Key` | `FR-SIM-01` | Header `X-Webhook-Secret` bắt buộc, không cần JWT. Trả 200 mảng `{ skuCode, productName, variantName, unitName, unitPrice, available }` gồm SKU đang bán của doanh nghiệp sở hữu secret (tối đa 500), giá bán lẻ hiện hành của đơn vị gốc và tồn có thể bán tại chi nhánh kết nối. Không trả giá vốn, `tenantId`, `skuId`. Lỗi 401 như `whoami`. |
| `POST` | `/api/webhooks/simulator/batch` | `Owner, Staff` | `NFR-PERF-02` | Chạy mô phỏng nhiều đơn đồng thời cho 1 SKU để kiểm chứng chống bán vượt. |
| `WS/SSE` | `/hubs/notifications` | `Owner, Staff, Cashier` | `FR-SIM-02` | **SignalR Hub thông báo thời gian thực:**<br/>- Xác thực JWT Bearer qua Query Token (`/hubs/notifications?access_token=...`) dành riêng cho Hub path.<br/>- Tự động join nhóm `tenant_{tenantId}`. Client có thể gọi `JoinBranchGroup(Guid branchId)` và `LeaveBranchGroup(Guid branchId)` sau khi server xác thực chi nhánh hợp lệ.<br/>- Các sự kiện phát ra: `OrderCreated` (đơn Reserved mới), `StockLevelChanged` (khi tồn khả dụng thay đổi), `LowStockWarning` (khi tồn khả dụng &le; `reorder_point`). Payload cam kết không chứa giá vốn / bí mật. |

**Nhiều đơn vị tính (Upgrade 05):**

- `POST /api/orders`: dòng hàng nhận `unitId` tùy chọn, cùng quy tắc giá và mã lỗi như POS. Giữ chỗ tính theo lượng gốc và được chia theo từng dòng.
- Dòng trong response có thêm `unitId`, `unitName`, `factorSnapshot`, `baseQuantity`. `quantity`, `unitPrice`, `subtotal` theo đơn vị giao dịch; `costPrice` là WAC trên đơn vị gốc, `totalCost = baseQuantity × costPrice`.
- Replay cùng `RequestKey` so theo (SKU, đơn vị); gửi `unitId` của đơn vị gốc được coi như để trống.
- `POST /api/orders` nhận `priceListCode`: `retail` (mặc định) hoặc `wholesale`; mã khác trả `404 PRICE_LIST_NOT_FOUND`. Replay với bảng giá khác trả `409 IDEMPOTENCY_PAYLOAD_MISMATCH`.
- Giá được kiểm lại sau khi khóa SKU. Lệch trả `400 PRICE_MISMATCH` kèm `extensions.currentPrices`, cấu trúc như POS. Dòng đơn lưu `sku_unit_price_id` và `price_source` (`PriceList`, `Marketplace`, `Legacy`). Xác nhận, hoàn tất, hủy đơn và hết hạn giữ chỗ dùng giá đã chụp.
- Đơn từ sàn không tra bảng giá, dùng giá do sàn gửi với nguồn `Marketplace`.
- Đơn từ sàn TMĐT luôn tính theo đơn vị gốc.
- Báo cáo: `totalCostOfGoodsSold`, `totalItemsSold`, `quantitySold` tính theo lượng gốc.

---


## 1.0a. Kết Nối Gian Hàng Sàn (`FR-SIM-01`, `OISMS-48`)

Mỗi kết nối ánh xạ `(channel, externalShopId)` của Tenant về một chi nhánh xử lý đơn và giữ secret cho header `X-Webhook-Secret`. Chỉ `Owner` được gọi; `Staff`, `Cashier` nhận 403. `TenantId` lấy từ JWT.

| Method | Endpoint | Mô tả |
| :---: | :--- | :--- |
| `GET` | `/api/shop-integrations` | Danh sách kết nối của Tenant: `id`, `channel`, `externalShopId`, `branchId`, `branchName`, `isActive`, `createdAt`, `updatedAt`. Không trả secret hay hash. |
| `GET` | `/api/shop-integrations/{id}` | Chi tiết một kết nối. Không thuộc Tenant hiện tại trả 404 `SHOP_INTEGRATION_NOT_FOUND`. |
| `POST` | `/api/shop-integrations` | Body `{ channel, externalShopId, branchId }`. `channel` nhận `Shopee`, `TikTok`, `Lazada` không phân biệt hoa thường. Trả 201 `{ integration, webhookSecret }`; `webhookSecret` (`whsec_` + 64 ký tự hex) chỉ hiện một lần. Trùng gian hàng trên cùng kênh: 409 `SHOP_INTEGRATION_DUPLICATE`. Chi nhánh không tồn tại, đã tắt hoặc thuộc Tenant khác: 404 `BRANCH_INACTIVE_OR_INVALID`. |
| `PUT` | `/api/shop-integrations/{id}` | Body `{ branchId, isActive }`. Không đổi được `channel`, `externalShopId`. Đặt `isActive = false` thì webhook với secret đó trả 401. |
| `POST` | `/api/shop-integrations/{id}/rotate-secret` | Sinh secret mới, trả 200 `{ integration, webhookSecret }`. Secret cũ mất hiệu lực ngay. |

## 1.1. Hóa Đơn Bán Hàng & Truy Vết Chứng Từ Xuất Kho (`OISMS-47-U02`)

Mỗi lần xuất bán (POS Checkout hoặc Online Order Confirm) tạo đúng một hóa đơn bán hàng trong cùng giao dịch CSDL với dòng `OUT`.

| Method | Endpoint | Quyền (RBAC) | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :--- |
| `GET` | `/api/sales-invoices?number=&orderId=&branchId=&fromDate=&toDate=&pageIndex=&pageSize=` | `Owner, Staff` | Danh sách hóa đơn bán hàng phân trang (`SalesInvoiceSummaryDto`). Bộ lọc `toDate` nửa mở (`IssuedAt < toDate`). |
| `GET` | `/api/sales-invoices/{id}` | `Owner, Staff` | Chi tiết tóm tắt một hóa đơn bán hàng theo ID. |
| `GET` | `/api/sales-invoices/{id}/receipt` | `Owner, Staff, Cashier` | Dữ liệu in lại hóa đơn theo ID (`SalesInvoiceReceiptDto`). Tuyệt đối không chứa các trường giá vốn (`costPrice`, `unitCost`, `totalCogs`). |
| `GET` | `/api/sales-invoices/receipt?number=` | `Owner, Staff, Cashier` | Dữ liệu in lại hóa đơn theo số hóa đơn. Thu ngân POS dùng endpoint này để in lại từ số `INV-`. |
| `GET` | `/api/sales-invoices/{id}/trace` | `Owner, Staff` | Truy vết chuỗi chứng từ: hóa đơn, đơn hàng, dòng sổ kho `OUT` và các phiếu trả hàng (`salesReturns`) kèm dòng sổ kho `IN` (`DocumentTraceDto`). |

## 1.2. Phiếu Trả Hàng & Hoàn Tiền (`CS-01`, `OISMS-58`)

Quản lý chu trình khách đổi trả hàng bán từ đơn `Confirmed`/`Completed`. Khi xác nhận phiếu trả, hệ thống tự động sinh dòng sổ kho `IN`, hoàn lại tồn khả dụng/thực tế, tính lại giá vốn bình quân gia quyền (WAC) và khấu trừ doanh thu/giá vốn thuần trong báo cáo.

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `GET` | `/api/orders/{id}/returnable-items` | `Owner, Staff` | `CS-01` | Lấy danh sách mặt hàng có thể trả từ đơn hàng: số lượng đã bán (`soldQuantity`), đã trả (`returnedQuantity`), còn được trả (`returnableQuantity`), đơn giá và giá vốn gốc snapshot. |
| `POST` | `/api/sales-returns` | `Owner, Staff` | `CS-01` | Lập phiếu trả hàng ở trạng thái `Draft`. Bắt buộc `Idempotency-Key` hoặc `X-Request-Key` (header hoặc body). Hỗ trợ trả từng phần hoặc toàn bộ; kiểm tra không vượt quá số lượng và số tiền đã thanh toán của đơn. Replay cùng key & payload trả 200 OK. |
| `GET` | `/api/sales-returns` | `Owner, Staff` | `CS-01` | Danh sách phiếu trả hàng phân trang, lọc theo `orderId`, `branchId`, `warehouseId`, `status` (`Draft`, `Confirmed`, `Cancelled`), `searchTerm` (số phiếu hoặc số đơn), `fromDate`, `toDate` (theo ngày tạo phiếu, nửa mở), `pageIndex`, `pageSize` (tối đa 100). |
| `GET` | `/api/sales-returns/{id}` | `Owner, Staff` | `CS-01` | Chi tiết một phiếu trả hàng kèm danh sách dòng hàng trả (`items`), số tiền hoàn, phương thức hoàn tiền (`Cash` hoặc `BankTransfer`; đơn sàn để `null`), người lập và người xác nhận. |
| `POST` | `/api/sales-returns/{id}/confirm` | `Owner, Staff` | `CS-01` | **Xác nhận trả hàng (`Draft -> Confirmed`):** Khóa kho bằng advisory lock, tăng `on_hand`, tính lại WAC theo công thức chuẩn, sinh dòng sổ kho `IN` gắn `sales_return_item_id`. Cập nhật `confirmed_at` và `confirmed_by`. Ghi nhận ngày doanh thu/giá vốn hoàn lại vào báo cáo theo ngày xác nhận (UTC+7). |
| `POST` | `/api/sales-returns/{id}/cancel` | `Owner, Staff` | `CS-01` | Hủy phiếu trả hàng (`Draft -> Cancelled`): Chỉ áp dụng cho phiếu đang `Draft`. Không tác động tới tồn kho và sổ cái. Phiếu đã `Confirmed` không được phép hủy (trả `409 INVALID_STATE_TRANSITION`). |

## 2. Danh Mục Endpoints Báo Cáo, Tìm Kiếm & AI (`FR-REP-01..03`, `FR-AI-01..02`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `GET` | `/api/reports/revenue-profit` | `Owner, Staff` | `FR-REP-01`, `CS-01` | Báo cáo Doanh thu gộp, Tiền trả hàng (`returnAmount`: đơn POS/Admin là số tiền thực hoàn; đơn sàn là SL trả × đơn giá snapshot vì phiếu không ghi hoàn tiền), Doanh thu thuần (`netRevenue = totalRevenue - returnAmount`), Giá vốn gộp, Giá vốn trả lại (`returnCogs`), Giá vốn thuần (`netCogs = cogs - returnCogs`) và Lợi nhuận gộp (`grossProfit = netRevenue - netCogs`). Khấu trừ phiếu trả `Confirmed` theo `confirmed_at` (UTC+7, lọc nửa mở `[StartDate, EndDate)`). Chỉ tính đơn `Confirmed` và `Completed`, lọc theo `orders.created_at` (UTC+7, nửa mở). Phân rã theo kênh, chi nhánh, ngày. Trường cũ `totalNetRevenue`, `totalCostOfGoodsSold`, `totalGrossProfit` là **số thuần** (bằng `netRevenue`, `netCogs`, `grossProfit`); `revenue`, `costOfGoodsSold` là số gộp trước khi trừ hàng trả. Trong breakdown, `revenue` và `costOfGoodsSold` là số gộp, `grossProfit` tính trên số thuần.<br/>**Bảo mật dữ liệu (RBAC):** Chỉ `Owner` thấy các trường giá vốn và lãi gộp (`costOfGoodsSold`, `returnCogs`, `netCogs`, `grossProfit`, `grossMarginPercentage`). Với `Staff`, các trường này trả về `null` (cả ở cấp tổng lẫn trong các breakdown). |
| `GET` | `/api/reports/dashboard-summary` | `Owner, Staff` | `CS-07`, `CS-01` | KPI trang Tổng quan: doanh thu gộp, tiền trả (`returnAmount`), doanh thu thuần (`netRevenue`), giá vốn thuần (`netCogs`), số đơn, xu hướng 7 ngày gần nhất.<br/>**Bảo mật dữ liệu (RBAC):** `totalGrossProfit`, `grossMarginPercentage`, `totalInventoryValue` và giá vốn/lợi nhuận theo kênh trả về `null`/`0` khi caller là `Staff`. |
| `GET` | `/api/reports/inventory-velocity` | `Owner, Staff` | `FR-REP-02` | Báo cáo giá trị tồn kho theo giá vốn, top hàng bán chạy và hàng luân chuyển chậm. |
| `GET` | `/api/reports/stock-alerts` | `Owner, Staff` | `FR-REP-03` | Danh sách SKU hết hàng (`available <= 0`, kể cả khi `reorder_point = 0`) hoặc đã đặt ngưỡng và `available <= reorder_point`; phân cấp `Warning`, `Critical` (dưới 50%), `OutOfStock`. Lọc theo `branchId`. |
| `GET` | `/api/ai/export-history` | `Owner, Staff` | `FR-AI-01` | Chuỗi lịch sử xuất kho theo ngày và SKU (đơn vị gốc) làm đầu vào dự báo. Trùng dữ liệu với `/api/inventory/export-history`. |
| `GET` | `/api/ai/reorder-recommendations` | `Owner, Staff` | `FR-AI-01` | Dự báo nhu cầu tiêu thụ và gợi ý số lượng đặt hàng nhập kho theo từng SKU. |
| `POST` | `/api/ai/chat` | `Owner, Staff` | `FR-AI-02` | Hướng dẫn sử dụng và tóm tắt số liệu đã lọc theo vai trò và tenant. Body nhận thêm `model` (tùy chọn, mã model từ `GET /api/ai/models`); response trả `model` đã dùng. Model không có trong danh sách trả 400 `AI_INVALID_REQUEST`. |
| `GET` | `/api/ai/models` | `Owner, Staff` | `FR-AI-02` | Danh sách model đang có key: `[{ id, label, isDefault }]` (ví dụ `groq`, `gemini`). AI tắt hoặc chưa có key thì trả mảng rỗng. Không trả key hay địa chỉ nhà cung cấp. |

**Tìm kiếm toàn cục:** `GET /api/search?q=&limit=5` (mọi vai trò đã đăng nhập) tìm sản phẩm/SKU, đơn hàng, hóa đơn... theo từ khóa; kết quả lọc theo vai trò lấy từ JWT.

**Tác vụ nền:** job `oisms-reservation-expiry` (`*/1 * * * *` UTC) chỉ chạy khi `Hangfire:Enabled=true`. `appsettings.json` và Docker Compose mặc định để `false`, nên khi chạy local đơn giữ chỗ quá hạn không tự hủy; bản demo AWS đã bật từ 11/11/2026.

> **Tồn theo đơn vị (04/11/2026):** `GET /api/reports/inventory-velocity` trả thêm `quantityByUnit`, mỗi phần tử gồm `unitName`, `onHand`, `reserved`, `available` theo đơn vị gốc, sắp theo `onHand` giảm dần. SKU chưa có đơn vị gốc nằm trong nhóm `Chưa xác định`. Các trường `totalOnHandQuantity`, `totalReservedQuantity`, `totalAvailableQuantity` vẫn trả về để tương thích nhưng cộng gộp khác đơn vị, không dùng để hiển thị. Trong `bestSellers` và `slowMovingItems`, mỗi SKU một dòng; `currentOnHand` là tổng tồn trong phạm vi chi nhánh đang lọc; `baseUnitName` là `null` khi SKU chưa có đơn vị gốc.

> **Ghi chú 04/11/2026:** `GET /api/ai/reorder-recommendations`, `lowStockCount` của `GET /api/reports/dashboard-summary` và báo cáo cảnh báo tồn chỉ tính SKU còn kinh doanh (`IsActive = true`). `daysUntilStockout = 999` nghĩa là chưa có dữ liệu bán để ước tính.

### 2.1. Trợ lý OISMS (FR-AI-02)

`POST /api/ai/chat` yêu cầu JWT của Owner hoặc Staff. Cashier nhận 403, thiếu JWT nhận 401. Vai trò và tenant lấy từ JWT.

```json
{
  "messages": [
    { "role": "user", "content": "Doanh thu 7 ngày qua?" }
  ],
  "branchId": "11111111-0000-0000-0001-000000000001"
}
```

Gửi 1–10 tin theo thứ tự, mỗi tin 1–1.000 ký tự với role `user` hoặc `assistant`. Tin cuối phải là `user`. Phạm vi mặc định lấy từ `branchId` của panel hoặc là tất cả chi nhánh khi trường này vắng mặt. Nếu câu hỏi nhắc tên chi nhánh, backend ưu tiên tra tên trong các chi nhánh đang hoạt động của tenant. Ngày do công cụ nhận ở dạng `yyyy-MM-dd` theo UTC+07, mặc định hôm nay và tối đa 92 ngày tính cả hai đầu.

```json
{
  "reply": "Doanh thu thuần tại chi nhánh trong kỳ là 1.200.000 đồng.",
  "usedTools": ["get_revenue"],
  "dateRange": { "from": "2026-11-04", "to": "2026-11-10" },
  "scope": "Quận 1",
  "branchChoices": []
}
```

`usedTools` liệt kê công cụ cung cấp dữ liệu cho câu trả lời. `dateRange` là `null` khi các công cụ đã dùng không truy vấn khoảng ngày. Khi có nhiều khoảng, trường này bao từ ngày đầu sớm nhất đến ngày cuối muộn nhất. Câu trả lời hướng dẫn có thể trả `usedTools: []`.

`scope` là phạm vi của công cụ số liệu chạy cuối cùng hoặc `null` nếu chưa có công cụ nào trả số liệu. Khi tên chi nhánh không khớp duy nhất, `branchChoices` trả tối đa 10 lựa chọn `{ "id": "...", "name": "Quận 1" }`. Panel gửi lại câu hỏi gần nhất với `branchId` đã chọn và `confirmBranchChoice: true`. Backend kiểm tra ID thuộc tenant và dùng chi nhánh đã xác nhận cho lần gửi lại. Các request khác có thể bỏ cờ này.

Giới hạn 10 request mỗi phút theo user. Lỗi nghiệp vụ trả ProblemDetails với `errorCode` gồm `AI_INVALID_REQUEST` (400), `AI_RATE_LIMITED` (429), `AI_UPSTREAM_ERROR` (502) và `AI_DISABLED` (503).
