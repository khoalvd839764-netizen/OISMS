# 03. Đặc Tả Hợp Đồng API Tồn Kho, Sổ Cái & Nhập/Chuyển/Kiểm Kê (Inventory & Ledger Endpoints)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-API-03` |
| **Phiên bản (Version)** | `v1.3.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-06` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-INV-01..04`, `FR-COST-01`, `FR-RSE-01..03` (Trang 2, 3, 5) |
| **Phạm vi áp dụng** | `InventoryController`, `GoodsReceiptsController`, `StockTransfersController`, `StocktakesController` |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả danh sách Endpoints quản lý Sổ cái kho bất biến, Phiếu nhập kho, Giữ chỗ, Chuyển kho và Kiểm kê. | `INIT-API-03` |
| `v1.1.0` | `2026-11-02` | Nhóm dự án OISMS | Thêm API phiếu mua, đổi hợp đồng lập phiếu nhập, thêm hủy phiếu nhập nháp; sửa route phiếu nhập theo code. | `OISMS-47-U01A` |
| `v1.2.0` | `2026-11-03` | Nhóm dự án OISMS | Bổ sung API lịch kiểm kê (`stock-count-schedules`), kỳ kiểm kê (`stock-count-periods`), quy trình nộp duyệt chênh lệch (`stock-counts`). | `OISMS-47-U03` |
| `v1.3.0` | `2026-11-06` | Nhóm dự án OISMS | Sửa route chuyển kho thành `/api/stock-transfers`; thêm API tồn theo SKU/chi nhánh, lịch sử xuất kho, ngưỡng đặt hàng lại, alias `/api/inventory-transactions`; sửa quyền xem tồn. | `OISMS-46` |

---

## 1. Danh Mục Endpoints Sổ Cái, Tồn Kho & Phiếu Nhập (`FR-INV-01..02`, `FR-COST-01`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `GET` | `/api/inventory/balances` | `Owner, Staff, Cashier` | `FR-INV-01`, `FR-RSE-01` | Tra cứu số dư tồn kho chi tiết (`OnHand`, `Reserved`, `Available = OnHand - Reserved`, `UnitCost` WAC) theo Chi nhánh/Kho/SKU. |
| `GET` | `/api/inventory/ledger`<br/>*(Alias: `/api/inventory-transactions`)* | `Owner, Staff` | `FR-INV-01` | Tra cứu lịch sử Sổ cái kho chỉ ghi thêm (`InventoryTransaction`), hỗ trợ lọc theo Chi nhánh, Kho, SKU, Loại giao dịch (`IN`/`OUT`) và Thời gian. Trả kèm `salesInvoiceId` và `salesInvoiceNumber` đối với các dòng xuất bán hàng (Upgrade 02). |
| `GET` | `/api/inventory/balances/sku/{skuId}/branch/{branchId}` | `Owner, Staff, Cashier` | `FR-INV-01` | Tồn của một SKU tại một chi nhánh (theo đơn vị gốc). |
| `GET` | `/api/inventory/export-history` | `Owner, Staff` | `FR-AI-01` | Lịch sử xuất kho theo ngày dùng cho biểu đồ và gợi ý nhập hàng. |
| `PUT` | `/api/inventory/reorder-point` | `Owner, Staff` | `CS-03` | Đặt ngưỡng đặt hàng lại (`reorder_point`) cho một SKU tại chi nhánh. |
| `PUT` | `/api/inventory/reorder-point/bulk` | `Owner, Staff` | `CS-03` | Đặt ngưỡng cho nhiều SKU một lần. |
| `GET` | `/api/inventory/ledger/{id}/trace` | `Owner, Staff` | `OISMS-47-U02` | Truy vết chuỗi chứng từ nguồn và các dòng sổ kho liên quan bắt đầu từ một dòng sổ cái kho (`DocumentTraceDto`). |
| `POST` | `/api/goods-receipts` | `Owner, Staff` | `FR-INV-02`, `OISMS-47-U01A` | Lập phiếu nhập `Draft` từ phiếu mua đã duyệt. Xem mục 1.1. |
| `GET` | `/api/goods-receipts`, `/api/goods-receipts/{id}` | `Owner, Staff` | `FR-INV-02` | Danh sách và chi tiết phiếu nhập. Lọc thêm được theo `purchaseOrderId`. |
| `POST` | `/api/goods-receipts/{id}/confirm` | `Owner, Staff` | `FR-INV-02`, `FR-COST-01` | Xác nhận phiếu nhập nguyên tử: kiểm tra phiếu mua và lượng còn được nhận, ghi Sổ cái `IN`, tăng `OnHand`, tính lại `WAC`. Xác nhận lặp trả `409 INVALID_STATE_TRANSITION`. Hóa đơn `Draft` hoặc `PendingApproval` trả `409 GOODS_RECEIPT_INVOICE_PENDING`. |
| `POST` | `/api/goods-receipts/{id}/cancel` | `Owner, Staff` | `OISMS-47-U01A` | Hủy phiếu `Draft`; không đổi tồn, sổ cái hay lượng đã nhận. |
| `POST` | `/api/inventory/reserve` | `Owner, Staff, Cashier` | `FR-RSE-02` | Giữ chỗ tồn kho chống bán vượt với khóa dòng CSDL và `Idempotency-Key` / `RequestKey`. |

**Nhiều đơn vị tính (Upgrade 05):**

- Dòng **phiếu mua** nhận `unitId` tùy chọn; để trống là đơn vị gốc. Đơn vị không cho mua trả `422 UNIT_NOT_ALLOWED`; số lượng sai số lẻ trả `400 INVALID_UNIT_QUANTITY`.
- Đơn vị và hệ số được chụp khi lập phiếu mua; phiếu nhập chép từ dòng phiếu mua, đổi hệ số SKU sau đó không ảnh hưởng.
- Khi xác nhận: tồn và sổ cái tăng theo `baseQuantity`; giá vốn đưa vào WAC là `unitPrice / factorSnapshot`.
- Chuyển kho, kiểm kê, sổ cái và số dư chỉ dùng đơn vị gốc.

### 1.1. Phiếu nhập từ phiếu mua (`OISMS-47-U01A`)

**Thay đổi phá vỡ tương thích:** `POST /api/goods-receipts` không còn nhận `branchId`, `supplierId`, `items[].skuId`, `items[].unitId`, `items[].unitPrice`. Các trường này nếu client vẫn gửi sẽ bị bỏ qua; chi nhánh, nhà cung cấp, SKU, đơn vị, hệ số và giá lấy từ phiếu mua.

```json
{
  "purchaseOrderId": "GUID, bắt buộc",
  "warehouseId": "GUID, kho thuộc chi nhánh của phiếu mua",
  "number": "tùy chọn, tối đa 50 ký tự",
  "note": "tùy chọn",
  "items": [{ "purchaseOrderItemId": "GUID", "quantity": 60 }]
}
```

- `quantity` là số thực nhận theo **đơn vị của dòng phiếu mua**. Được bỏ bớt dòng để nhận một phần.
- Trả `201` với `GoodsReceiptResponseDto`, có thêm `purchaseOrderId`, `purchaseOrderNumber`, và `items[].purchaseOrderItemId`.
- Phiếu nháp không giữ lượng. Lúc lập chỉ kiểm tra để báo sớm; kết luận cuối cùng là lúc xác nhận.
- `GoodsReceiptResponseDto` gồm `invoiceId` và `invoiceStatus` của hóa đơn còn hiệu lực; hai trường là `null` khi không có hóa đơn còn hiệu lực.
- Phiếu nháp cũ không gắn phiếu mua: xác nhận trả `422 PURCHASE_ORDER_REQUIRED`, vẫn hủy được.

### 1.2. Hóa đơn nhà cung cấp và đối chiếu ba bên (U01-B)

| Method | Endpoint | Quyền | Kết quả |
| :---: | :--- | :--- | :--- |
| `GET` | `/api/supplier-invoices` | Owner, Staff | Phân trang và lọc theo `supplierId`, `status`, `fromDate`, `toDate`, `goodsReceiptId`, `purchaseOrderId` (mọi hóa đơn của các phiếu nhập thuộc phiếu mua), `pendingOnly`, `pageIndex`, `pageSize`. Hai mốc ngày hóa đơn gồm cả ngày biên. |
| `GET` | `/api/supplier-invoices/{id}` | Owner, Staff | Chi tiết hóa đơn, bảng `comparison` và `variance` nếu có. |
| `POST` | `/api/supplier-invoices` | Owner, Staff | Tạo `Draft`. Header `Idempotency-Key` là GUID bắt buộc. Tạo mới trả `201`; gửi lại nội dung gốc trả `200` với cùng ID và `isReplay: true`; đổi nội dung với cùng key trả `409 IDEMPOTENCY_PAYLOAD_MISMATCH`. |
| `PUT` | `/api/supplier-invoices/{id}` | Owner, Staff | Thay nội dung và các dòng khi còn `Draft`. Liên kết phiếu nhập và nhà cung cấp cố định. |
| `POST` | `/api/supplier-invoices/{id}/submit` | Owner, Staff | Gửi `Draft`, tự chuyển `Matched` hoặc `PendingApproval`. |
| `POST` | `/api/supplier-invoices/{id}/approve` | Owner | Duyệt `PendingApproval`. Có thể gửi `{ "decisionNote": "..." }`. |
| `POST` | `/api/supplier-invoices/{id}/reject` | Owner | Từ chối `PendingApproval`, bắt buộc `{ "decisionNote": "..." }`. Nếu loại `AtReceipt`, phiếu nhập còn nháp được hủy cùng transaction. |
| `POST` | `/api/supplier-invoices/{id}/cancel` | Owner, Staff | Hủy `Draft`, `Matched` hoặc `Approved`; bắt buộc `{ "reason": "..." }`. |

Mọi phản hồi hóa đơn có thêm các trường chỉ đọc `supplierName`, `goodsReceiptNumber`, `goodsReceiptStatus`, `purchaseOrderId`, `purchaseOrderNumber` để hiển thị danh sách và lịch sử.

Body tạo mới gồm `goodsReceiptId`, `supplierId`, `invoiceSeries`, `invoiceNumber`, `invoiceDate` (YYYY-MM-DD), `vatAmount`, `shippingFee` và `items` với `goodsReceiptItemId`, `quantity`, `unitPrice`. Body sửa dùng các trường nội dung và dòng. Ký hiệu và số tối đa 50 ký tự, lý do/ghi chú quyết định tối đa 500 ký tự. Đơn giá trước thuế theo đơn vị mua, tiền hàng là tổng lượng nhân đơn giá, tổng thanh toán trên chứng từ cộng thuế và phí vận chuyển.

Mỗi dòng `comparison` có ba cột `purchaseOrder`, `goodsReceipt`, `invoice`, cùng `expectedQuantity`, `expectedUnitPrice`, `quantityDiff`, `priceDiff` và `amountDiff`. Dòng thực nhận thiếu trên hóa đơn có cột `invoice: null` và chênh lệch lượng âm khi gửi. Chênh lệch tiền bằng tiền hàng hóa đơn trừ thực nhận nhân giá phiếu mua. Thuế và phí vận chuyển được loại khỏi đối chiếu. `variance` trả loại `AtReceipt` hoặc `AfterReceipt`, tổng chênh lệch và thông tin quyết định.

Cashier nhận `403` trên mọi API hóa đơn. Với ID thuộc tenant khác, API chi tiết hoặc ghi trả `404`. Lỗi nghiệp vụ trả `ProblemDetails` với `errorCode`: `SUPPLIER_INVOICE_DUPLICATE` (409), `SUPPLIER_INVOICE_INVALID_STATE` (409), `SUPPLIER_INVOICE_RECEIPT_MISMATCH` (422), `SUPPLIER_INVOICE_ALREADY_EXISTS` (409). Xác nhận phiếu nhập trả `GOODS_RECEIPT_INVOICE_PENDING` (409) nếu hóa đơn còn chờ gửi hoặc duyệt.

### 1.3. Phiếu mua hàng (`OISMS-47-U01A`)

| Method | Endpoint | Quyền (RBAC) | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :--- |
| `GET` | `/api/purchase-orders?status=&supplierId=&branchId=&pageIndex=&pageSize=` | `Owner, Staff` | Danh sách phân trang: `items`, `pageIndex`, `pageSize`, `totalCount`. |
| `GET` | `/api/purchase-orders/{id}` | `Owner, Staff` | Chi tiết kèm các dòng. Tenant khác trả `404`. |
| `POST` | `/api/purchase-orders` | `Owner, Staff` | Tạo phiếu `Draft`. Bắt buộc header `Idempotency-Key` hoặc `X-Request-Key` (GUID). Tạo mới trả `201`; gửi lại cùng key, cùng nội dung trả `200` với `isReplay = true`; cùng key, khác nội dung trả `409 IDEMPOTENCY_PAYLOAD_MISMATCH`. |
| `PUT` | `/api/purchase-orders/{id}` | `Owner, Staff` | Chỉ khi `Draft`. Cùng body với `POST`, thay toàn bộ dòng và chụp lại đơn vị. |
| `POST` | `/api/purchase-orders/{id}/approve` | `Owner` | Body `{ "confirmZeroPrice": false }`. Phiếu có dòng giá 0 phải gửi `true`. Sau khi duyệt không sửa được. |
| `POST` | `/api/purchase-orders/{id}/close` | `Owner` | Body `{ "reason": "bắt buộc, tối đa 500 ký tự" }`. Đóng phần chưa nhận từ `Approved` hoặc `PartiallyReceived`. |
| `POST` | `/api/purchase-orders/{id}/cancel` | `Owner` | Hủy từ `Draft` hoặc `Approved` chưa nhận hàng. |

Body tạo hoặc sửa phiếu:

```json
{
  "supplierId": "GUID",
  "branchId": "GUID",
  "number": "tùy chọn, tối đa 50 ký tự; trống thì tự sinh PO-yyyyMMdd-XXXXXX",
  "note": "tùy chọn, tối đa 1000 ký tự",
  "items": [{ "skuId": "GUID", "unitId": "GUID hoặc null", "quantity": 100, "unitPrice": 12000 }]
}
```

- Mỗi SKU một dòng. `unitPrice` là giá thực mua sau chiết khấu theo đơn vị mua, tối đa 6 chữ số lẻ.

Response phiếu mua có: `id`, `number`, `requestKey`, `supplierId`, `supplierName`, `branchId`, `branchName`, `status`, `note`, `totalAmount`, `createdBy`, `createdByName`, `approvedBy`, `approvedByName`, `approvedAt`, `closedBy`, `closedByName`, `closedAt`, `closeReason`, `cancelledBy`, `cancelledByName`, `cancelledAt`, `createdAt`, `updatedAt`, `isReplay`, `items`.

Mỗi dòng trong `items` có:

| Trường | Ý nghĩa |
| :--- | :--- |
| `id` | Dùng làm `purchaseOrderItemId` khi lập phiếu nhập |
| `skuId`, `skuCode`, `productName` | Mặt hàng |
| `unitId`, `unitName`, `factorSnapshot`, `quantityDecimalPlaces` | Đơn vị đã chụp và số chữ số lẻ được phép nhập |
| `quantity`, `unitPrice`, `lineTotal` | Lượng đặt và giá theo đơn vị mua |
| `orderedBaseQuantity`, `receivedBaseQuantity`, `remainingBaseQuantity` | Lượng đặt, đã nhận, còn lại theo đơn vị gốc |
| `remainingQuantity` | Lượng còn lại theo đơn vị mua, làm tròn 3 chữ số lẻ, dùng để hiển thị |

**Mã lỗi** theo RFC 7807, đọc `extensions.errorCode` (JSON phẳng là `errorCode`) và `detail`:

| Mã | HTTP | Khi nào |
| :--- | :---: | :--- |
| `PURCHASE_ORDER_REQUIRED` | `422` | Thiếu phiếu mua khi lập phiếu nhập, hoặc xác nhận phiếu nháp cũ |
| `PURCHASE_ORDER_NOT_RECEIVABLE` | `409` | Phiếu mua không ở trạng thái nhận được, gồm `Draft`, `Closed`, `Cancelled`; lúc lập phiếu nhập còn gồm `Received` |
| `PURCHASE_ORDER_OVER_RECEIPT` | `409` | Vượt lượng còn được nhận, kể cả xác nhận khi phiếu mua đã `Received` |
| `PURCHASE_ORDER_ITEM_MISMATCH` | `422` | Dòng nhập không thuộc phiếu mua, bị trùng, hoặc không khớp phiếu mua |
| `PURCHASE_ORDER_ZERO_PRICE_CONFIRM_REQUIRED` | `422` | Duyệt phiếu có giá 0 mà chưa xác nhận |
| `INVALID_STATE_TRANSITION` | `409` | Sai chuyển trạng thái phiếu mua hoặc phiếu nhập |
| `IDEMPOTENCY_PAYLOAD_MISMATCH` | `409` | Cùng key nhưng khác nội dung |

Trạng thái phiếu mua: xem [máy trạng thái](../04-architecture-and-design/04-state-machines.md).

---

## 2. Danh Mục Endpoints Chuyển Kho 2 Bước & Kiểm Kê (`FR-INV-03..04`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `GET` | `/api/stock-transfers`, `/api/stock-transfers/{id}` | `Owner, Staff` | `FR-INV-03` | Danh sách và chi tiết phiếu chuyển kho. |
| `POST` | `/api/stock-transfers` | `Owner, Staff` | `FR-INV-03` | Tạo phiếu chuyển kho `Draft` giữa kho của Chi nhánh A và Chi nhánh B. |
| `POST` | `/api/stock-transfers/{id}/dispatch` | `Owner, Staff` | `FR-INV-03` | Bước 1 — Xuất kho vận chuyển (`Draft → InTransit`): trừ tồn kho A, chụp giá vốn, ghi Sổ cái `OUT`. |
| `POST` | `/api/stock-transfers/{id}/receive` | `Owner, Staff` | `FR-INV-03` | Bước 2 — Xác nhận nhập tại kho B (`InTransit → Received`): tăng tồn kho B, tính lại WAC, ghi Sổ cái `IN`. |
| `POST` | `/api/stock-transfers/{id}/cancel` | `Owner, Staff` | `FR-INV-03` | Hủy phiếu khi còn `Draft`. |

---

## 2.1. Lịch Kiểm Kê, Kỳ Kiểm Kê & Phê Duyệt Chênh Lệch (`OISMS-47-U03`)

### Bảng Endpoints Quản Lý Lịch Kiểm Kê (`/api/stock-count-schedules`)

| Method | Endpoint | Quyền (RBAC) | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :--- |
| `GET` | `/api/stock-count-schedules?warehouseId=&pageNumber=&pageSize=` | `Owner, Staff` | Tra cứu danh sách cấu hình lịch kiểm kê định kỳ theo kho. |
| `POST` | `/api/stock-count-schedules` | `Owner` | Thiết lập cấu hình lịch kiểm kê định kỳ mới (`MidMonth` hoặc `EndOfMonth`). `day_of_month` chỉ áp dụng cho MidMonth (1..28, mặc định 15). Trùng cặp `(warehouse_id, period_type)` trả `409 STOCK_COUNT_SCHEDULE_DUPLICATE`. |
| `PUT` | `/api/stock-count-schedules/{id}` | `Owner` | Cập nhật cấu hình lịch kiểm kê (`day_of_month`, `due_days`, `assignee_user_id`, `is_active`). |

### Bảng Endpoints Quản Lý Kỳ Kiểm Kê Định Kỳ (`/api/stock-count-periods`)

| Method | Endpoint | Quyền (RBAC) | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :--- |
| `GET` | `/api/stock-count-periods?branchId=&warehouseId=&status=&overdue=&from=&to=&pageNumber=&pageSize=` | `Owner, Staff` | Tra cứu danh sách các kỳ kiểm kê sinh ra bởi job định kỳ. |
| `POST` | `/api/stock-count-periods/{id}/start` | `Owner, Staff` | Bắt đầu đếm kỳ (`Planned` -> `InProgress`): Chụp snapshot số lượng tồn hệ thống tại thời điểm bấm, tạo phiếu `Draft`. Không cần header idempotency: khóa theo kỳ, gọi lại khi đã có phiếu còn hiệu lực trả phiếu đó với `isReplay = true`. Nếu phiếu bị hủy, kỳ quay về `Planned` và có thể bắt đầu lại. |

### Bảng Endpoints Quy Trình Kiểm Kê & Duyệt Chênh Lệch (`/api/stock-counts`)

| Method | Endpoint | Quyền (RBAC) | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :--- |
| `GET` | `/api/stock-counts?branchId=&warehouseId=&status=&pageNumber=&pageSize=` | `Owner, Staff` | Tra cứu danh sách đợt kiểm kê. |
| `GET` | `/api/stock-counts/{id}` | `Owner, Staff` | Chi tiết đợt kiểm kê, so sánh chênh lệch giữa thực đếm và sổ sách, danh sách lịch sử phê duyệt (`Approvals`), và cờ `isDrifted` tính theo số dư hiện tại. |
| `POST` | `/api/stock-counts` | `Owner, Staff` | Tạo đợt kiểm kê đột xuất (`AdHoc`). Bắt buộc trường `reason`. |
| `POST` | `/api/stock-counts/{id}/record` | `Owner, Staff` | Ghi nhận số lượng thực tế nhân viên kiểm đếm (`Draft` -> `Counting`). Lưu `counted_by`, `counted_at` trên từng dòng. |
| `POST` | `/api/stock-counts/{id}/submit` | `Owner, Staff` | Nộp kết quả kiểm kê (`Counting` -> `PendingApproval`). Yêu cầu đếm đủ mọi mặt hàng. Đối soát sequence drift: nếu có giao dịch xen giữa làm lệch sequence, gom mọi SKU lệch và trả `409 STOCK_COUNT_SEQUENCE_DRIFT`. Ghi lịch sử duyệt `Submitted`. |
| `POST` | `/api/stock-counts/{id}/recount` | `Owner, Staff` | Nhân viên kiểm đếm lại cho các SKU bị lệch sequence khi đang ở `Counting`. Chỉ cho phép SKU đang lệch; SKU không lệch trả `409 STOCK_COUNT_SKU_NOT_DRIFTED`. Ghi lại snapshot mới, xóa số đếm cũ (nhưng đã lưu trong `differences` của approval `Recounted`). |
| `POST` | `/api/stock-counts/{id}/request-recount` | `Owner` | Owner yêu cầu đếm lại các SKU khi ở `PendingApproval`. Bắt buộc `reason`. Chuyển về `Counting`, xóa `submitted_by`, `submitted_at`, ghi approval `RecountRequested`. |
| `POST` | `/api/stock-counts/{id}/confirm` | **Owner** | Phê duyệt chốt kiểm kê (`PendingApproval` -> `Confirmed`). **Chặn tự duyệt:** Người duyệt trùng người đếm của bất kỳ dòng nào và tenant còn Owner khác -> trả `409 STOCK_COUNT_SELF_APPROVAL`. Nếu là Owner duy nhất -> bắt buộc `Note` không rỗng giải trình lý do duyệt, thiếu trả `422 STOCK_COUNT_APPROVAL_REASON_REQUIRED`. Trong cùng transaction: đối soát sequence drift, tạo phiếu điều chỉnh, ghi sổ kho `IN`/`OUT`, cập nhật kỳ `Completed`, ghi approval `Approved`. |
| `POST` | `/api/stock-counts/{id}/cancel` | `Owner, Staff` | Hủy đợt kiểm kê chưa chốt (`Draft`/`Counting` -> `Cancelled`). Ghi approval `Cancelled`. Nếu gắn với kỳ định kỳ thì đưa kỳ về `Planned`. |
