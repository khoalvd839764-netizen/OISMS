# 03. Đặc Tả Hợp Đồng API Tồn Kho, Sổ Cái & Nhập/Chuyển/Kiểm Kê (Inventory & Ledger Endpoints)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-API-03` |
| **Phiên bản (Version)** | `v1.1.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-02` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-INV-01..04`, `FR-COST-01`, `FR-RSE-01..03` (Trang 2, 3, 5) |
| **Phạm vi áp dụng** | `InventoryController`, `GoodsReceiptsController`, `StockTransfersController`, `StocktakesController` |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả danh sách Endpoints quản lý Sổ cái kho bất biến, Phiếu nhập kho, Giữ chỗ, Chuyển kho và Kiểm kê. | `INIT-API-03` |
| `v1.1.0` | `2026-11-02` | Nhóm dự án OISMS | Thêm API phiếu mua, đổi hợp đồng lập phiếu nhập, thêm hủy phiếu nhập nháp; sửa route phiếu nhập theo code. | `OISMS-47-U01A` |

---

## 1. Danh Mục Endpoints Sổ Cái, Tồn Kho & Phiếu Nhập (`FR-INV-01..02`, `FR-COST-01`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `GET` | `/api/inventory/balances` | `Owner, Staff` | `FR-INV-01`, `FR-RSE-01` | Tra cứu số dư tồn kho chi tiết (`OnHand`, `Reserved`, `Available = OnHand - Reserved`, `UnitCost` WAC) theo Chi nhánh/Kho/SKU. |
| `GET` | `/api/inventory/ledger` | `Owner, Staff` | `FR-INV-01` | Tra cứu lịch sử Sổ cái kho chỉ ghi thêm (`InventoryTransaction`), hỗ trợ lọc theo Chi nhánh, Kho, SKU, Loại giao dịch (`IN`/`OUT`) và Thời gian. |
| `POST` | `/api/goods-receipts` | `Owner, Staff` | `FR-INV-02`, `OISMS-47-U01A` | Lập phiếu nhập `Draft` từ phiếu mua đã duyệt. Xem mục 1.1. |
| `GET` | `/api/goods-receipts`, `/api/goods-receipts/{id}` | `Owner, Staff` | `FR-INV-02` | Danh sách và chi tiết phiếu nhập. Lọc thêm được theo `purchaseOrderId`. |
| `POST` | `/api/goods-receipts/{id}/confirm` | `Owner, Staff` | `FR-INV-02`, `FR-COST-01` | Xác nhận phiếu nhập nguyên tử: kiểm tra phiếu mua và lượng còn được nhận, ghi Sổ cái `IN`, tăng `OnHand`, tính lại `WAC`. Xác nhận lặp trả `409 INVALID_STATE_TRANSITION`. |
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
- Phiếu nháp cũ không gắn phiếu mua: xác nhận trả `422 PURCHASE_ORDER_REQUIRED`, vẫn hủy được.

### 1.2. Phiếu mua hàng (`OISMS-47-U01A`)

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
| `POST` | `/api/inventory/transfers` | `Owner, Staff` | `FR-INV-03` | Tạo phiếu chuyển kho giữa Chi nhánh A và Chi nhánh B. |
| `POST` | `/api/inventory/transfers/{id}/dispatch` | `Owner, Staff` | `FR-INV-03` | Bước 1 — Xuất kho vận chuyển (`Out-bound transit`): Trừ tồn Kho A và ghi Sổ cái `OUT`. |
| `POST` | `/api/inventory/transfers/{id}/receive` | `Owner, Staff` | `FR-INV-03` | Bước 2 — Xác nhận nhập tại Kho B (`In-bound confirmation`): Tăng tồn Kho B và ghi Sổ cái `IN`. |
| `POST` | `/api/inventory/stocktakes` | `Owner, Staff` | `FR-INV-04` | Tạo phiên kiểm kê vật lý, đối chiếu tồn phần mềm và sinh bút toán điều chỉnh Sổ cái (`Adjustment Ledger entries`). |
