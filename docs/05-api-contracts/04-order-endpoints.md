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
| `GET` | `/api/orders/{id}` | `Owner, Staff` | `FR-ORD-03` | Lấy chi tiết đơn hàng kèm danh sách mặt hàng, giá vốn WAC snapshot, người xác nhận (`confirmedBy`), thông tin hóa đơn (`salesInvoiceId`, `salesInvoiceNumber`) và thanh toán. |
| `GET` | `/api/orders/{id}/trace` | `Owner, Staff` | `OISMS-47-U02` | Truy vết chuỗi chứng từ xuất kho và các dòng sổ kho tương ứng với đơn hàng (`DocumentTraceDto`). |
| `POST` | `/api/orders` | `Owner, Staff` | `FR-ORD-01..02` | Tạo đơn hàng thủ công trên Admin và thực hiện giữ chỗ tồn kho (`Draft -> Reserved`). Bắt buộc `RequestKey` hợp lệ do client cung cấp. |
| `POST` | `/api/orders/{id}/confirm` | `Owner, Staff` | `FR-ORD-02..03`, `FR-RSE-03`, `FR-COST-02` | Duyệt đơn hàng (`Reserved -> Confirmed`): Trừ `on_hand`, giảm `reserved`, ghi Sổ cái `OUT` và chụp `OrderItem.CostPrice`. |
| `POST` | `/api/orders/{id}/process` | `Owner, Staff` | `FR-ORD-02` | Xử lý đóng gói đơn `Reserved`: gọi lại luồng Confirm; đơn đã `Confirmed` trả nguyên chi tiết; `Cancelled`/`Completed` trả `409 INVALID_STATE_TRANSITION`. |
| `POST` | `/api/orders/{id}/complete` | `Owner, Staff` | `FR-ORD-02..03` | **Hoàn tất đơn online (`Confirmed -> Completed`):** Ghi nhận khách đã nhận hàng thành công. **Tuyệt đối KHÔNG trừ tồn kho hay ghi ledger OUT lần thứ hai** (vì đã ghi khi Confirm). Gọi lặp lại trên đơn đã `Completed` là idempotent (trả về 200 OK). Đơn chưa Confirmed chuyển sang Completed trả `409 Conflict` (`INVALID_STATE_TRANSITION`). |
| `POST` | `/api/orders/{id}/cancel` | `Owner, Staff` | `FR-ORD-02..03`, `FR-RSE-03` | Hủy đơn hàng (`Reserved -> Cancelled`): Giải phóng `reserved` về lại quỹ `available`. |
| `POST` | `/api/orders/checkout`<br/>*(Canonical: `/api/pos/checkout`)* | `Owner, Staff, Cashier` | `FR-POS-03`, `FR-POS-04`, `FR-COST-02` | Endpoint alias tương thích ngược phục vụ Web POS PWA và Admin POS, trỏ vào cùng lõi `IOrderService.CheckoutPosAsync`. Trả về `PosReceiptDto` không lộ giá vốn. |
| `POST` | `/api/webhooks/simulator/{channel}` | `Owner, Staff / Webhook Key` | `FR-SIM-01`, `FR-ORD-01` | **Tiếp nhận đơn TMĐT (Shopee, TikTok, Lazada):**<br/>- Xác thực kép: Header `X-Webhook-Secret` (production/simulator webhook) hoặc `Bearer JWT` (`Owner`, `Staff`; `Cashier` bị từ chối 403 `FORBIDDEN`). Tuyệt đối không đọc `TenantId` từ request body.<br/>- Tự động ánh xạ chi nhánh fulfillment từ bảng `shop_integrations` theo `(tenant_id, channel, external_shop_id)`.<br/>- Khóa chống trùng: `(tenant_id, channel, external_shop_id, external_order_id)`. Cùng khóa + cùng payload &rarr; 200 OK replay; Cùng khóa + khác payload &rarr; 409 Conflict (`WEBHOOK_PAYLOAD_MISMATCH`).<br/>- Kênh URL khác payload channel &rarr; 400 Bad Request (`CHANNEL_MISMATCH`).<br/>- Áp dụng đơn giá tin cậy từ sàn TMĐT, kiểm tra tổng tiền và làm tròn `AwayFromZero` (lệch trả 400 `TOTAL_AMOUNT_MISMATCH`).<br/>- Giữ chỗ tồn kho bằng lõi chung `CreateReservedOrderCoreAsync`: tạo đơn `Reserved`, sinh `Reservation` `Active` 15 phút, tăng `Reserved` ở kho/chi nhánh, OnHand/WAC không đổi. |
| `GET` | `/api/webhooks/simulator/whoami` | `Webhook Key` | `FR-SIM-01` | Header `X-Webhook-Secret` bắt buộc, không cần JWT. Trả 200 `{ channel, externalShopId, branchName, branchIsActive }` để công cụ giả lập tự điền. Không trả `tenantId`, `branchId`. Thiếu hoặc sai secret, kết nối tắt, doanh nghiệp ngừng hoạt động: 401 `UNAUTHORIZED`. Chi nhánh tắt vẫn trả 200 với `branchIsActive: false`. Xem [`DOC-SIM-14`](../14-webhook-simulator/README.md). |
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
| `GET` | `/api/sales-invoices/{id}/trace` | `Owner, Staff` | Truy vết chuỗi chứng từ xuất kho và các dòng sổ kho từ ID hóa đơn (`DocumentTraceDto`). |

## 2. Danh Mục Endpoints Báo Cáo, Tìm Kiếm & Dự Báo AI (`FR-REP-01..03`, `FR-AI-01`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `GET` | `/api/reports/revenue-profit` | Yêu cầu: `Owner`<br/>Code: `Owner, Staff` | `FR-REP-01` | Báo cáo Doanh thu thuần, Tổng giá vốn (`COGS`) và Lợi nhuận gộp (`Gross Profit = Revenue - COGS`), phân rã theo kênh, chi nhánh, ngày. **Lệch hiện trạng:** controller cho `Staff` gọi và service không ẩn `costOfGoodsSold`/`grossProfit`; giao diện chỉ mở trang Báo cáo cho `Owner`. Xem [ma trận RBAC](../01-users-and-rbac/02-rbac-matrix.md). |
| `GET` | `/api/reports/dashboard-summary` | `Owner, Staff` | `CS-07` | KPI trang Tổng quan: doanh thu, số đơn, top đơn mới. `totalGrossProfit`, `totalInventoryValue` là `null` và giá vốn/lãi theo kênh là `0` khi không phải `Owner`. |
| `GET` | `/api/reports/inventory-velocity` | `Owner, Staff` | `FR-REP-02` | Báo cáo giá trị tồn kho theo giá vốn, top hàng bán chạy và hàng luân chuyển chậm. |
| `GET` | `/api/reports/stock-alerts` | `Owner, Staff` | `FR-REP-03` | Danh sách SKU có `available <= reorder_point`, phân cấp `Warning`, `Critical` (dưới 50%), `OutOfStock`. Lọc theo `branchId`. |
| `GET` | `/api/ai/export-history` | `Owner, Staff` | `FR-AI-01` | Chuỗi lịch sử xuất kho theo ngày và SKU (đơn vị gốc) làm đầu vào dự báo. Trùng dữ liệu với `/api/inventory/export-history`. |
| `GET` | `/api/ai/reorder-recommendations` | `Owner, Staff` | `FR-AI-01` | Dự báo nhu cầu tiêu thụ và gợi ý số lượng đặt hàng nhập kho theo từng SKU. |

**Tìm kiếm toàn cục:** `GET /api/search?q=&limit=5` (mọi vai trò đã đăng nhập) tìm sản phẩm/SKU, đơn hàng, hóa đơn... theo từ khóa; kết quả lọc theo vai trò lấy từ JWT.

**Tác vụ nền:** job `oisms-reservation-expiry` (`*/1 * * * *` UTC) chỉ chạy khi `Hangfire:Enabled=true`. `appsettings.json` và Docker Compose hiện để `false`, nên đơn giữ chỗ quá hạn không tự hủy trên bản demo.

> **Tồn theo đơn vị (04/11/2026):** `GET /api/reports/inventory-velocity` trả thêm `quantityByUnit`, mỗi phần tử gồm `unitName`, `onHand`, `reserved`, `available` theo đơn vị gốc, sắp theo `onHand` giảm dần. SKU chưa có đơn vị gốc nằm trong nhóm `Chưa xác định`. Các trường `totalOnHandQuantity`, `totalReservedQuantity`, `totalAvailableQuantity` vẫn trả về để tương thích nhưng cộng gộp khác đơn vị, không dùng để hiển thị. Trong `bestSellers` và `slowMovingItems`, mỗi SKU một dòng; `currentOnHand` là tổng tồn trong phạm vi chi nhánh đang lọc; `baseUnitName` là `null` khi SKU chưa có đơn vị gốc.

> **Ghi chú 04/11/2026:** `GET /api/ai/reorder-recommendations`, `lowStockCount` của `GET /api/reports/dashboard-summary` và báo cáo cảnh báo tồn chỉ tính SKU còn kinh doanh (`IsActive = true`). `daysUntilStockout = 999` nghĩa là chưa có dữ liệu bán để ước tính.
