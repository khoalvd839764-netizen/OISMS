# 04. ĐẶC TẢ MÀN HÌNH QUẢN LÝ KHO & SỔ CÁI BẤT BIẾN (INVENTORY & LEDGER SCREENS)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SCR-04` |
| **Phiên bản (Version)** | `v1.2.0` |
| **Ngày hiệu lực** | `2026-11-19` |
| **Đường dẫn URL Routes** | `/admin/inventory`, `/inventory` |
| **Quyền truy cập (RBAC)** | `Owner`, `Staff` |
| **Source code Frontend** | [`InventoryPage.tsx`](../../src/frontend/admin-dashboard/src/pages/InventoryPage.tsx), [`InventoryPeriodTab.tsx`](../../src/frontend/admin-dashboard/src/components/inventory/InventoryPeriodTab.tsx), [`StockCardModal.tsx`](../../src/frontend/admin-dashboard/src/components/inventory/StockCardModal.tsx) |
| **Controllers Backend** | [`InventoryController.cs`](../../src/backend/src/OISM.WebApi/Controllers/InventoryController.cs), [`GoodsReceiptsController.cs`](../../src/backend/src/OISM.WebApi/Controllers/GoodsReceiptsController.cs), [`StockTransfersController.cs`](../../src/backend/src/OISM.WebApi/Controllers/StockTransfersController.cs), [`StockCountsController.cs`](../../src/backend/src/OISM.WebApi/Controllers/StockCountsController.cs), [`ReportsController.cs`](../../src/backend/src/OISM.WebApi/Controllers/ReportsController.cs) |

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
│ ┌───────────────────────────────┬───────────────────────────────┬───────────────────────────────┐ │
│ │ [✓] SỐ DƯ TỒN KHO KHẢ DỤNG    │ [ ] SỔ CÁI TỒN KHO BẤT BIẾN   │ [ ] TỒN KHO THEO KỲ (NXT)     │ │
│ └───────────────────────────────┴───────────────────────────────┴───────────────────────────────┘ │
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

### 3.1. Modal Nhập Kho Nhà Cung Cấp (Goods Receipt Modal — `U01-A`, `Upgrade 12`)
* **Thông tin phiếu:** Chọn đơn mua đã duyệt (`Approved` hoặc `PartiallyReceived`), kho tiếp nhận thuộc chi nhánh trên đơn mua và nhập ghi chú. Nhà cung cấp lấy cố định từ đơn mua.
* **Bảng dòng sản phẩm & Phân quyền hiển thị giá (`Upgrade 12`):**
  * Dòng hàng và đơn vị mua được điền sẵn từ đơn mua. Nhân viên nhập số lượng thực nhận (không vượt lượng còn lại) hoặc bỏ dòng chưa nhận.
  * **Đối với `Owner`:** Hiển thị đầy đủ giá vốn đơn vị sau phân bổ (`allocatedUnitCost`) và tổng giá trị phiếu nhập.
  * **Đối với `Staff` (Nhận hàng mù):** Ẩn hoàn toàn cột đơn giá nhập, thành tiền dòng và tổng giá trị phiếu nhập trên cả form lập phiếu, danh sách `Phiếu nhập đã lưu` và màn `Chi tiết phiếu nhập`.
* **Nút bấm:**
  * `[Lưu nháp]`: Lưu ở trạng thái `Draft`, chưa làm thay đổi số dư tồn kho hay giá vốn.
  * `[Xác nhận nhập kho]`: Xác nhận nguyên tử để tăng `OnHand` $\rightarrow$ Ghi sổ cái `IN` $\rightarrow$ Tự động tính lại giá vốn bình quân theo `allocatedUnitCost / factorSnapshot`:
    $$\text{WAC}_{\text{mới}} = \frac{(\text{TồnCũ} \times \text{GiáVốnCũ}) + (\text{SốLượngNhậpGốc} \times \text{GiáVốnĐơnVịGốcSauPhânBổ})}{\text{TồnCũ} + \text{SốLượngNhậpGốc}}$$

#### Màn hình Đơn mua hàng (`/purchase-orders` — `FE-12.1`)
* **Vai trò `Staff`:** Bấm `+ Đề nghị mua hàng` chỉ chọn Chi nhánh, SKU, Đơn vị mua và Số lượng (không có ô Nhà cung cấp, không có ô giá hay phí). Khi đơn ở `Draft`, `Staff` có nút **Sửa đề nghị nháp**, **Gửi duyệt** (`Draft -> PendingApproval`) và **Hủy đơn**. Nếu đơn bị `Owner` trả về, hệ thống hiển thị rõ hộp cảnh báo **Lý do trả về từ Owner**.
* **Vai trò `Owner`:** Tạo đơn vào thẳng `PendingApproval`; chọn Nhà cung cấp, Ngày đặt hàng (`orderDate <= hôm nay`), nhập Phí vận chuyển, Chiết khấu thương mại, Thuế VAT và đơn giá từng dòng. Dưới mỗi ô đơn giá hiển thị **WAC hiện tại** (quy theo đơn vị mua) và nút **Gợi ý lần mua gần nhất** để áp nhanh. Bảng tính trực tiếp **Giá vốn sau phân bổ** của từng dòng và **Tổng thanh toán (gồm VAT)**. Ở chi tiết đơn `PendingApproval`, `Owner` có các nút: **Lưu / Sửa NCC & Giá**, **Duyệt đơn mua**, **Trả về Draft** (bắt buộc nhập lý do) và **Hủy đơn**.
* **Gỡ giao diện Hóa đơn nhà cung cấp (`FE-12.2`):** Đã gỡ hoàn toàn các nút `Chênh lệch chờ duyệt`, `Lịch sử hóa đơn NCC` trên trang Kho cũng như `SupplierInvoicePanel` và `PurchaseOrderInvoices` trong chi tiết phiếu nhập / đơn mua.

### 3.2. Modal Chuyển Kho 2 Bước (Stock Transfer Modal)
* **Bước 1 (Xuất chuyển - Dispatch):** Chọn Chi nhánh đi, Kho xuất, Chi nhánh đến, Kho nhận (Bắt buộc Kho xuất $\ne$ Kho nhận). Nhập danh sách SKU và số lượng. Hệ thống trừ `OnHand` kho xuất và đưa phiếu sang `InTransit`.
* **Bước 2 (Tiếp nhận - Receive):** Nhân viên kho nhận kiểm đếm thực tế, bấm `[Tiếp nhận phiếu]` để tăng `OnHand` kho nhận và hoàn tất chu trình (`Received`).

### 3.3. Modal Kiểm Kê & Phát Hiện Sai Lệch (Stock Count Modal)
* Nhân viên nhập danh sách số đếm thực tế (`ActualQuantity`).
* Khi bấm `[Chốt kiểm kê]`: Hệ thống tự động so khớp tồn phần mềm tại thời điểm mở phiếu với tồn hiện tại. Nếu có giao dịch bán phát sinh làm thay đổi tồn, hệ thống từ chối và báo lỗi `STOCK_COUNT_DRIFT_DETECTED`. Nếu hợp lệ, hệ thống tự động sinh các dòng bút toán điều chỉnh cân bằng kho.

### 3.4. Tab Tồn Kho Theo Kỳ (NXT) & Modal Thẻ Kho S12/S10 (`FE-13.1, FE-13.2`)
* **Thanh công cụ lọc:**
  * Bộ chọn mốc thời gian nhanh: `Hôm nay`, `Hôm qua`, `7 ngày qua`, `30 ngày qua`, `Tháng này`, `Tháng trước`, hoặc tùy chọn khoảng ngày `Từ ngày - Đến ngày` (giới hạn tối đa 366 ngày, không cho chọn ngày tương lai).
  * Bộ chọn Chi nhánh: Hỗ trợ chọn từng chi nhánh cụ thể hoặc `Tất cả chi nhánh`.
  * Bộ lọc Danh mục sản phẩm và Ô tìm kiếm SKU theo mã/tên.
  * Nút chức năng: `[Xuất Excel]` (tải về file `.xlsx` báo cáo NXT định dạng chuẩn kế toán) và `[Làm mới]`.
* **Bảng dữ liệu Nhập - Xuất - Tồn:**
  * Cột thông tin: Mã SKU, Tên sản phẩm, Đơn vị tính cơ sở.
  * Số dư đầu kỳ (Số lượng; và Thành tiền nếu là `Owner`).
  * Nhóm Nhập kho: Tổng số lượng nhập (và Thành tiền nếu là `Owner`). Nút bấm `[+]` mở rộng chi tiết phân rã: Mua NCC, Khách trả, Chuyển đến, Kiểm kê/Điều chỉnh thừa.
  * Nhóm Xuất kho: Tổng số lượng xuất (và Thành tiền nếu là `Owner`). Nút bấm `[+]` mở rộng chi tiết phân rã: Bán POS, Bán Shopee, Bán TikTok, Bán Lazada, Bán Admin, Chuyển đi, Kiểm kê/Điều chỉnh thiếu.
  * Số dư cuối kỳ: Số lượng; và Thành tiền nếu là `Owner`.
  * Cột Hàng đang chuyển (`InTransit`): Hiển thị khi xem chế độ `Tất cả chi nhánh`.
  * Cột Thao tác: Nút `[Thẻ kho]` mở modal lịch sử chi tiết cho từng mặt hàng.
  * Hàng Tổng cộng ở cuối bảng: Tổng hợp toàn bộ số lượng và giá trị (cho Owner).
  * Phân quyền hiển thị: Nhân viên (`Staff`) hoàn toàn không nhìn thấy các cột Thành tiền (cột tự động ẩn trên bảng và file Excel).
* **Modal Thẻ Kho S12/S10:**
  * Tiêu đề chuẩn hóa: Mẫu S10-DN (khi `Owner` xem, có cột Đơn giá & Thành tiền) hoặc Mẫu S12-DN (khi `Staff` xem, chỉ có cột Số lượng).
  * Danh sách biến động gồm: Dòng Số dư đầu kỳ, các dòng giao dịch sổ cái chi tiết (Thời gian, Mã chứng từ, Loại biến động, Nhập, Xuất, Tồn sau giao dịch), dòng Số dư cuối kỳ.
  * Dòng Chênh lệch làm tròn WAC (`RoundDiff`): Chỉ hiển thị cho `Owner` khi kỳ báo cáo kéo dài đến ngày hiện tại để phản ánh chênh lệch giữa lũy kế giá trị sổ cái và giá trị theo đơn giá WAC hiện hành.
  * Nút `[In Thẻ Kho]`: Mở hộp thoại in trình duyệt với CSS chuyên dụng cho in ấn kế toán.
  * Nút `[Xuất Excel]`: Tải file `.xlsx` mẫu thẻ kho chi tiết dạng stream.

---

## 4. Bảng Hàm API Backend & Tài Liệu Tham Chiếu

| Method | Endpoint Backend | Hàm xử lý Controller / Service | Tài liệu Hợp đồng API tham chiếu | Mã lỗi liên quan |
| :---: | :--- | :--- | :--- | :--- |
| `GET` | `/api/inventory/balances` | `InventoryController.GetBalances` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#1-danh-mục-endpoints-sổ-cái-tồn-kho--phiếu-nhập-fr-inv-0102-fr-cost-01) | `NO_ACTIVE_WAREHOUSE`, `DATABASE_CONNECTION_ERROR` (Xem [`DOC-ERR-02`](../12-error-and-status-codes/02-error-codes-catalog.md)) |
| `GET` | `/api/inventory/ledger` | `InventoryController.GetLedger` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#1-danh-mục-endpoints-sổ-cái-tồn-kho--phiếu-nhập-fr-inv-0102-fr-cost-01) | `AUTH_UNAUTHORIZED` |
| `POST` | `/api/goods-receipts` | `GoodsReceiptsController.Create` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#1-danh-mục-endpoints-sổ-cái-tồn-kho--phiếu-nhập-fr-inv-0102-fr-cost-01) | `VALIDATION_ERROR`, `PURCHASE_ORDER_NOT_RECEIVABLE`, `PURCHASE_ORDER_OVER_RECEIPT` |
| `POST` | `/api/goods-receipts/{id}/confirm` | `GoodsReceiptsController.Confirm` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#1-danh-mục-endpoints-sổ-cái-tồn-kho--phiếu-nhập-fr-inv-0102-fr-cost-01) | `INVALID_STATE_TRANSITION`, `LEDGER_IMMUTABLE_VIOLATION`, `PURCHASE_ORDER_OVER_RECEIPT` |
| `POST` | `/api/stock-transfers` | `StockTransfersController.Create` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#2-danh-mục-endpoints-chuyển-kho-2-bước--kiểm-kê-fr-inv-0304) | `SAME_WAREHOUSE_TRANSFER`, `DUPLICATE_TRANSFER_SKU` |
| `POST` | `/api/stock-transfers/{id}/dispatch` | `StockTransfersController.Dispatch` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#2-danh-mục-endpoints-chuyển-kho-2-bước--kiểm-kê-fr-inv-0304) | `INSUFFICIENT_STOCK`, `INVALID_STATE_TRANSITION` |
| `POST` | `/api/stock-transfers/{id}/receive` | `StockTransfersController.Receive` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#2-danh-mục-endpoints-chuyển-kho-2-bước--kiểm-kê-fr-inv-0304) | `INVALID_STATE_TRANSITION` |
| `POST` | `/api/stock-counts` | `StockCountsController.Create` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#2-danh-mục-endpoints-chuyển-kho-2-bước--kiểm-kê-fr-inv-0304) | `VALIDATION_ERROR`, `NO_ACTIVE_WAREHOUSE` |
| `POST` | `/api/stock-counts/{id}/confirm` | `StockCountsController.Confirm` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#2-danh-mục-endpoints-chuyển-kho-2-bước--kiểm-kê-fr-inv-0304) | `STOCK_COUNT_DRIFT_DETECTED`, `INCOMPLETE_STOCK_COUNT`, `ADJUSTMENT_COST_REQUIRED` |
| `GET` | `/api/reports/inventory-period` | `ReportsController.GetInventoryPeriodReport` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#3-báo-cáo-tồn-kho-theo-kỳ-nxt--thẻ-kho-s12s10-upgrade-13--oisms-66-g2) | `VALIDATION_ERROR`, `AUTH_FORBIDDEN` |
| `GET` | `/api/reports/stock-card` | `ReportsController.GetStockCardReport` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#3-báo-cáo-tồn-kho-theo-kỳ-nxt--thẻ-kho-s12s10-upgrade-13--oisms-66-g2) | `VALIDATION_ERROR`, `AUTH_FORBIDDEN` |

Route đầy đủ (phiếu mua, lịch/kỳ kiểm kê, `record`, `submit`, `recount`, `request-recount`, `cancel`, ngưỡng đặt hàng lại) xem [DOC-API-03](../05-api-contracts/03-inventory-endpoints.md). Màn Chuyển kho ở `/inventory/transfers`, Kiểm kê ở `/inventory/counts`, Đơn mua ở `/purchase-orders`.
