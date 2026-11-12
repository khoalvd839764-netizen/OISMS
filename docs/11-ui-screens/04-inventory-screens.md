# 04. ĐẶC TẢ MÀN HÌNH QUẢN LÝ KHO & SỔ CÁI BẤT BIẾN (INVENTORY & LEDGER SCREENS)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SCR-04` |
| **Phiên bản (Version)** | `v1.1.0` |
| **Ngày hiệu lực** | `2026-11-06` |
| **Đường dẫn URL Routes** | `/admin/inventory`, `/inventory` |
| **Quyền truy cập (RBAC)** | `Owner`, `Staff` |
| **Source code Frontend** | [`InventoryPage.tsx`](../../src/frontend/admin-dashboard/src/pages/InventoryPage.tsx) |
| **Controllers Backend** | [`InventoryController.cs`](../../src/backend/src/OISM.WebApi/Controllers/InventoryController.cs), [`GoodsReceiptsController.cs`](../../src/backend/src/OISM.WebApi/Controllers/GoodsReceiptsController.cs), [`StockTransfersController.cs`](../../src/backend/src/OISM.WebApi/Controllers/StockTransfersController.cs), [`StockCountsController.cs`](../../src/backend/src/OISM.WebApi/Controllers/StockCountsController.cs) |

---

## 1. Mục Tiêu & Luồng Nghiệp Vụ

Màn hình **Quản lý Kho & Sổ cái** là trái tim vận hành kho vận của OISMS với 4 phân hệ lõi:
1. **Quản lý Số dư Tồn kho thực tế (Inventory Balances):** Theo dõi động bộ ba chỉ số tồn kho theo từng SKU/Kho:
   $$\text{Tồn khả dụng (Available)} = \text{Thực tế trong kho (OnHand)} - \text{Đang giữ chỗ (Reserved)}$$
2. **Sổ cái tồn kho bất biến (Append-Only Inventory Ledger):** Kiểm toán minh bạch 100% mọi biến động tăng/giảm tồn kho qua các bút toán chỉ ghi thêm (`IN`/`OUT`), tuyệt đối nghiêm cấm chỉnh sửa hay xóa bản ghi sổ cái.
3. **Quy trình Nhập kho Nhà cung cấp & Tính giá vốn WAC:** Lập phiếu nhập đa dòng, duyệt nhập kho nguyên tử để tăng `OnHand`, ghi sổ cái `IN`, và tự động tính lại giá vốn bình quân gia quyền ($WAC$).
4. **Quy trình Chuyển kho 2 bước & Kiểm kê kho:** Chuyển kho liên chi nhánh với trạng thái trung gian `InTransit`, và đợt kiểm kê thực tế có cơ chế phát hiện sai lệch số liệu phần mềm (`Stock Count Drift Detection`).

---

## 2. Bố Cục Giao Diện Wireframe (ASCII Wireframe)

```text
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ BREADCRUMB: Quản lý kho vận & Sổ cái                                                           │
│ Quản Lý Kho & Biến Động Tồn Kho             [+ Nhập kho NCC] [ Chuyển kho ] [ Phiên kiểm kê ]  │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ CHỌN TAB NGHIỆP VỤ:                                                                            │
│ ┌───────────────────────────────┬───────────────────────────────┐                              │
│ │ [✓] SỐ DƯ TỒN KHO KHẢ DỤNG    │ [ ] SỔ CÁI TỒN KHO BẤT BIẾN   │                              │
│ └───────────────────────────────┴───────────────────────────────┘                              │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ THANH TÌM KIẾM & BỘ LỌC KHO:                                                                   │
│ [ 🔍 Tìm mã SKU, Barcode... ] [ Chi nhánh: Q.1 ▼ ] [ Kho: Kho Tổng ▼ ] [ Lọc: Dưới định mức ▼ ]│
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ TAB 1: BẢNG SỐ DƯ TỒN KHO (INVENTORY BALANCES)                                                 │
│ Mã SKU        | Mặt hàng           | OnHand | Reserved | Khả dụng  | Giá vốn WAC | Định mức an toàn │
│ ──────────────┼────────────────────┼────────┼──────────┼───────────┼─────────────┼───────────────── │
│ AO-POLO-TR-M  | Áo Polo Trắng - M  | 50 cái | 5 cái    | 45 cái    | 120,000 đ   | 🟢 An toàn (10)  │
│ QN-JEAN-X-31  | Quần Jean Slim - 31| 2 cái  | 2 cái    | 0 cái     | 210,000 đ   | 🔴 Hết hàng (5)  │
│ GY-SNK-D-42   | Giày Sneaker - 42  | 8 đôi  | 0 đôi    | 8 đôi     | 450,000 đ   | 🟡 Sắp hết (10)  │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ TAB 2: BẢNG SỔ CÁI TỒN KHO BẤT BIẾN (APPEND-ONLY LEDGER)                                       │
│ Thời gian     | Mã SKU       | Loại GD| Biến động | Số dư sau | Mã chứng từ gốc | Ghi chú        │
│ ──────────────┼──────────────┼────────┼───────────┼───────────┼─────────────────┼─────────────── │
│ 25/10 14:30:12| AO-POLO-TR-M | OUT    | - 2 cái   | 45 cái    | ORD-20261025-01 | Xuất bán POS   │
│ 25/10 10:15:00| AO-POLO-TR-M | IN     | + 20 cái  | 47 cái    | GR-20261025-09  | Nhập kho NCC   │
│ 24/10 16:45:22| QN-JEAN-X-31 | OUT    | - 1 cái   | 2 cái     | ORD-20261024-88 | Bán Shopee     │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Các Modal Nghiệp Vụ Kho Đặc Thù

### 3.1. Modal Nhập Kho Nhà Cung Cấp (Goods Receipt Modal)
* **Thông tin phiếu:** Chọn đơn mua đã duyệt, kho tiếp nhận thuộc chi nhánh trên đơn mua và nhập ghi chú. Nhà cung cấp lấy từ đơn mua.
* **Bảng dòng sản phẩm:** Dòng hàng, đơn vị và giá phiếu mua điền sẵn. Nhập số lượng thực nhận và bỏ dòng chưa nhận.
* **Nút bấm:**
  * `[Lưu Nháp]`: Lưu ở trạng thái `Draft`, chưa làm thay đổi số dư tồn kho hay giá vốn.
  * `[Xác nhận nhập kho]`: Khi đủ điều kiện, xác nhận nguyên tử để tăng `OnHand` $\rightarrow$ Ghi sổ cái `IN` $\rightarrow$ Tự động tính lại giá vốn bình quân theo công thức:
    $$\text{WAC}_{\text{mới}} = \frac{(\text{TồnCũ} \times \text{GiáVốnCũ}) + (\text{SốLượngNhập} \times \text{ĐơnGiáNhập})}{\text{TồnCũ} + \text{SốLượngNhập}}$$

#### Hóa đơn nhà cung cấp và đối chiếu ba bên (U01-B)

Tại **Kho & sổ kho → Phiếu nhập đã lưu → Xem phiếu**, Owner và Staff xem phần **Hóa đơn nhà cung cấp** trong chi tiết phiếu. Phiếu vừa lưu nháp cũng hiện phần này. Phiếu còn nháp hoặc đã xác nhận có liên kết phiếu mua hợp lệ và chưa có hóa đơn còn hiệu lực sẽ hiện nút **Nhập hóa đơn NCC**.

1. Form điền sẵn SKU, đơn vị mua, lượng thực nhận và giá phiếu mua. Nhập ký hiệu, số, ngày, lượng và đơn giá trước thuế trên hóa đơn, thuế GTGT và phí vận chuyển. Bỏ chọn **Có trên hóa đơn** đối với dòng thực nhận mà hóa đơn chưa ghi. **Lưu nháp hóa đơn** tạo bản nháp. Nếu kết quả lưu chưa rõ, **Thử lại lưu nháp hóa đơn** gửi lại đúng nội dung và `Idempotency-Key` trong phiên của tài khoản.
2. Hóa đơn nháp có **Sửa nháp** và **Gửi đối chiếu**. Sau khi gửi, bảng hiển thị ba cột **Phiếu mua / Thực nhận / Hóa đơn**, kèm kết quả. Dòng lệch được tô nền và có nhãn **Chênh lệch** cùng số liệu lệch. Trạng thái hiện ngay trong chi tiết hóa đơn.
3. **Xác nhận nhập kho** bị khóa với hóa đơn còn nháp hoặc đang chờ Owner duyệt. Dòng chú thích nêu lý do bên cạnh nhóm thao tác. Nếu hóa đơn đổi trạng thái khi đang xác nhận, lỗi `GOODS_RECEIPT_INVOICE_PENDING` yêu cầu gửi đối chiếu hoặc duyệt trước khi thử lại. Khi đã duyệt chênh lệch kèm hàng, dòng có trên hóa đơn dùng giá trước thuế của hóa đơn khi nhập.
4. Chỉ Owner thấy nút **Chênh lệch chờ duyệt** ở đầu trang. Danh sách tải qua `GET /api/supplier-invoices?pendingOnly=true`. Bấm **Xem và xử lý** để mở phiếu nhập tương ứng, sau đó **Duyệt chênh lệch** hoặc **Từ chối**. Từ chối cần lý do. Nếu từ chối chênh lệch kèm hàng, chi tiết hiện phiếu nhập đã hủy và kết quả từ chối.
5. Owner và Staff có thể **Hủy hóa đơn** từ trạng thái nháp, đã khớp hoặc đã duyệt. Nhập lý do và xác nhận hủy. Phiếu còn đủ điều kiện có thể nhập hóa đơn mới. Cashier không truy cập trang Admin này.

Phần hóa đơn và danh sách chờ duyệt có thông báo đang tải, rỗng và lỗi. Các lỗi nghiệp vụ được đọc theo `errorCode`. Hóa đơn gắn sau khi phiếu nhập đã xác nhận chỉ bổ sung chứng từ đối chiếu.

### 3.2. Modal Chuyển Kho 2 Bước (Stock Transfer Modal)
* **Bước 1 (Xuất chuyển - Dispatch):** Chọn Chi nhánh đi, Kho xuất, Chi nhánh đến, Kho nhận (Bắt buộc Kho xuất $\ne$ Kho nhận). Nhập danh sách SKU và số lượng. Hệ thống trừ `OnHand` kho xuất và đưa phiếu sang `InTransit`.
* **Bước 2 (Tiếp nhận - Receive):** Nhân viên kho nhận kiểm đếm thực tế, bấm `[Tiếp nhận phiếu]` để tăng `OnHand` kho nhận và hoàn tất chu trình (`Received`).

### 3.3. Modal Kiểm Kê & Phát Hiện Sai Lệch (Stock Count Modal)
* Nhân viên nhập danh sách số đếm thực tế (`ActualQuantity`).
* Khi bấm `[Chốt kiểm kê]`: Hệ thống tự động so khớp tồn phần mềm tại thời điểm mở phiếu với tồn hiện tại. Nếu có giao dịch bán phát sinh làm thay đổi tồn, hệ thống từ chối và báo lỗi `STOCK_COUNT_DRIFT_DETECTED`. Nếu hợp lệ, hệ thống tự động sinh các dòng bút toán điều chỉnh cân bằng kho.

---

## 4. Bảng Hàm API Backend & Tài Liệu Tham Chiếu

| Method | Endpoint Backend | Hàm xử lý Controller / Service | Tài liệu Hợp đồng API tham chiếu | Mã lỗi liên quan |
| :---: | :--- | :--- | :--- | :--- |
| `GET` | `/api/inventory/balances` | `InventoryController.GetBalances` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#1-danh-mục-endpoints-sổ-cái-tồn-kho--phiếu-nhập-fr-inv-0102-fr-cost-01) | `NO_ACTIVE_WAREHOUSE`, `DATABASE_CONNECTION_ERROR` (Xem [`DOC-ERR-02`](../12-error-and-status-codes/02-error-codes-catalog.md)) |
| `GET` | `/api/inventory/ledger` | `InventoryController.GetLedger` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#1-danh-mục-endpoints-sổ-cái-tồn-kho--phiếu-nhập-fr-inv-0102-fr-cost-01) | `AUTH_UNAUTHORIZED` |
| `POST` | `/api/goods-receipts` | `GoodsReceiptsController.Create` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#1-danh-mục-endpoints-sổ-cái-tồn-kho--phiếu-nhập-fr-inv-0102-fr-cost-01) | `VALIDATION_ERROR` |
| `GET`, `POST`, `PUT` | `/api/supplier-invoices` và các route theo ID | `SupplierInvoicesController` | [Hóa đơn NCC](../05-api-contracts/03-inventory-endpoints.md#12-hóa-đơn-nhà-cung-cấp-và-đối-chiếu-ba-bên-u01-b) | `SUPPLIER_INVOICE_DUPLICATE`, `SUPPLIER_INVOICE_INVALID_STATE`, `SUPPLIER_INVOICE_RECEIPT_MISMATCH`, `SUPPLIER_INVOICE_ALREADY_EXISTS` |
| `POST` | `/api/goods-receipts/{id}/confirm` | `GoodsReceiptsController.Confirm` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#1-danh-mục-endpoints-sổ-cái-tồn-kho--phiếu-nhập-fr-inv-0102-fr-cost-01) | `INVALID_STATE_TRANSITION`, `LEDGER_IMMUTABLE_VIOLATION`, `GOODS_RECEIPT_INVOICE_PENDING` |
| `POST` | `/api/stock-transfers` | `StockTransfersController.Create` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#2-danh-mục-endpoints-chuyển-kho-2-bước--kiểm-kê-fr-inv-0304) | `SAME_WAREHOUSE_TRANSFER`, `DUPLICATE_TRANSFER_SKU` |
| `POST` | `/api/stock-transfers/{id}/dispatch` | `StockTransfersController.Dispatch` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#2-danh-mục-endpoints-chuyển-kho-2-bước--kiểm-kê-fr-inv-0304) | `INSUFFICIENT_STOCK`, `INVALID_STATE_TRANSITION` |
| `POST` | `/api/stock-transfers/{id}/receive` | `StockTransfersController.Receive` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#2-danh-mục-endpoints-chuyển-kho-2-bước--kiểm-kê-fr-inv-0304) | `INVALID_STATE_TRANSITION` |
| `POST` | `/api/stock-counts` | `StockCountsController.Create` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#2-danh-mục-endpoints-chuyển-kho-2-bước--kiểm-kê-fr-inv-0304) | `VALIDATION_ERROR`, `NO_ACTIVE_WAREHOUSE` |
| `POST` | `/api/stock-counts/{id}/confirm` | `StockCountsController.Confirm` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#2-danh-mục-endpoints-chuyển-kho-2-bước--kiểm-kê-fr-inv-0304) | `STOCK_COUNT_DRIFT_DETECTED`, `INCOMPLETE_STOCK_COUNT`, `ADJUSTMENT_COST_REQUIRED` |

Route đầy đủ (phiếu mua, lịch/kỳ kiểm kê, `record`, `submit`, `recount`, `request-recount`, `cancel`, ngưỡng đặt hàng lại) xem [DOC-API-03](../05-api-contracts/03-inventory-endpoints.md). Màn Chuyển kho ở `/inventory/transfers`, Kiểm kê ở `/inventory/counts`, Đơn mua ở `/purchase-orders`.
