# 04. ĐẶC TẢ MÀN HÌNH QUẢN LÝ KHO & SỔ CÁI BẤT BIẾN (INVENTORY & LEDGER SCREENS)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SCR-04` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực** | `2026-10-25` |
| **Đường dẫn URL Routes** | `/admin/inventory`, `/inventory` |
| **Quyền truy cập (RBAC)** | `Owner`, `Staff` |
| **Source code Frontend** | [`InventoryPage.tsx`](file:///d:/OISMS/src/frontend/admin-dashboard/src/pages/InventoryPage.tsx) |
| **Controllers Backend** | [`InventoryController.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/InventoryController.cs), [`GoodsReceiptsController.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/GoodsReceiptsController.cs), [`StockTransfersController.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/StockTransfersController.cs), [`StockCountsController.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/StockCountsController.cs) |

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
* **Thông tin phiếu:** Chọn Chi nhánh, Kho tiếp nhận, Nhà cung cấp, Ghi chú nhập.
* **Bảng dòng sản phẩm:** Chọn SKU, nhập Số lượng nhập (`Quantity > 0`), Đơn giá nhập (`UnitPrice >= 0`).
* **Nút bấm:**
  * `[Lưu Nháp]`: Lưu ở trạng thái `Draft`, chưa làm thay đổi số dư tồn kho hay giá vốn.
  * `[Xác Nhận Nhập Kho]`: Thực hiện kiểm tra nguyên tử: Tăng `OnHand` $\rightarrow$ Ghi sổ cái `IN` $\rightarrow$ Tự động tính lại giá vốn bình quân theo công thức:
    $$\text{WAC}_{\text{mới}} = \frac{(\text{TồnCũ} \times \text{GiáVốnCũ}) + (\text{SốLượngNhập} \times \text{ĐơnGiáNhập})}{\text{TồnCũ} + \text{SốLượngNhập}}$$

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
| `POST` | `/api/inventory/goods-receipts` | `GoodsReceiptsController.Create` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#1-danh-mục-endpoints-sổ-cái-tồn-kho--phiếu-nhập-fr-inv-0102-fr-cost-01) | `VALIDATION_ERROR` |
| `POST` | `/api/inventory/goods-receipts/{id}/confirm` | `GoodsReceiptsController.Confirm` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#1-danh-mục-endpoints-sổ-cái-tồn-kho--phiếu-nhập-fr-inv-0102-fr-cost-01) | `INVALID_STATE_TRANSITION`, `LEDGER_IMMUTABLE_VIOLATION` |
| `POST` | `/api/inventory/transfers` | `StockTransfersController.Create` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#2-danh-mục-endpoints-chuyển-kho-2-bước--kiểm-kê-fr-inv-0304) | `SAME_WAREHOUSE_TRANSFER`, `DUPLICATE_TRANSFER_SKU` |
| `POST` | `/api/inventory/transfers/{id}/dispatch` | `StockTransfersController.Dispatch` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#2-danh-mục-endpoints-chuyển-kho-2-bước--kiểm-kê-fr-inv-0304) | `INSUFFICIENT_STOCK`, `INVALID_STATE_TRANSITION` |
| `POST` | `/api/inventory/transfers/{id}/receive` | `StockTransfersController.Receive` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#2-danh-mục-endpoints-chuyển-kho-2-bước--kiểm-kê-fr-inv-0304) | `INVALID_STATE_TRANSITION` |
| `POST` | `/api/inventory/stocktakes` | `StockCountsController.Create` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#2-danh-mục-endpoints-chuyển-kho-2-bước--kiểm-kê-fr-inv-0304) | `VALIDATION_ERROR`, `NO_ACTIVE_WAREHOUSE` |
| `POST` | `/api/inventory/stocktakes/{id}/finalize` | `StockCountsController.Finalize` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#2-danh-mục-endpoints-chuyển-kho-2-bước--kiểm-kê-fr-inv-0304) | `STOCK_COUNT_DRIFT_DETECTED`, `INCOMPLETE_STOCK_COUNT`, `ADJUSTMENT_COST_REQUIRED` |
