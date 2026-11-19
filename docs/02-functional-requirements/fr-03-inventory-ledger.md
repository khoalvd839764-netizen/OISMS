# FR-03: Sổ Cái Kho Bất Biến, Nhập Kho, Chuyển Kho & Kiểm Kê (Inventory Ledger & Stock Management)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-03` |
| **Phiên bản (Version)** | `v1.2.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-19` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-INV-01..04`, `NFR-SEC-03` (Trang 2, 4); `Upgrade 13 / OISMS-66` |
| **Gói công việc (Task Package)** | Task Package 2 — Product Catalog & Append-Only Inventory Ledger |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả chi tiết nhóm yêu cầu chức năng `FR-INV-01` đến `FR-INV-04` và quy tắc bất biến Sổ cái. | `INIT-FR-03` |
| `v1.1.0` | `2026-11-19` | Nhóm dự án OISMS | Bổ sung quy tắc đọc sổ cái cho Báo cáo Tồn kho theo kỳ (NXT) và Thẻ kho S12/S10 (Upgrade 13 / OISMS-66-G2). Quy định số dư đầu/cuối, bóc tách giao dịch, hàng đang chuyển và che giá theo vai trò. | `OISMS-66-G2` |
| `v1.2.0` | `2026-11-19` | Nhóm dự án OISMS | Mỗi dòng sổ cái `OUT` bán hàng có 1 dòng chứng từ `goods_issue_items` bất biến; DTO sổ cái và truy vết trả kèm `goodsIssueId`, `goodsIssueNumber`. | `OISMS-66-L2-G3` |

---

## 1. Bảng Đặc Tả Chi Tiết Yêu Cầu (`FR-INV`)

| Mã yêu cầu | Tên yêu cầu | Mô tả nghiệp vụ & Quy tắc kỹ thuật | Tiêu chí nghiệm thu (Acceptance Criteria) |
| :---: | :--- | :--- | :--- |
| **`FR-INV-01`** | **Inventory Ledger Recording** (Sổ cái kho bất biến) | Ghi nhận nghiêm ngặt mọi biến động kho (`Import`, `Export`, `Sale`, `Return`, `Stocktake`, `Transfer`) vào bảng sổ cái chỉ ghi thêm `InventoryTransaction` gồm các trường: `TenantId`, `BranchId`, `SKUId`, `Type (IN/OUT)`, `Quantity`, `BalanceAfter`, `ReferenceId`, `CreatedAt`. **Nghiêm cấm cập nhật trực tiếp cột `on_hand`.** | - Không tồn tại bất kỳ lệnh `UPDATE` hoặc `DELETE` nào trên bảng `inventory_transactions`.<br/>- Mọi thay đổi số lượng tồn đều có bản ghi sổ cái đối ứng với `BalanceAfter` khớp tuyệt đối. |
| **`FR-INV-02`** | **Purchase Receipt Management** (Nhập kho nhà cung cấp) | Xử lý phiếu nhập hàng từ Nhà cung cấp (`Draft -> Confirmed`): Khi xác nhận phiếu nhập $\rightarrow$ Ghi sổ cái `IN` $\rightarrow$ Tăng tồn vật lý (`on_hand`) $\rightarrow$ Tự động tính lại giá vốn bình quân (`WAC`). | - Toàn bộ quá trình xác nhận phiếu nhập nhiều dòng thực thi trong 1 Transaction ACID.<br/>- Chặn xác nhận lặp phiếu nhập đã `Confirmed` (`409 Conflict`).<br/>- Chặn xác nhận khi hóa đơn còn `Draft` hoặc `PendingApproval` (`409 GOODS_RECEIPT_INVOICE_PENDING`). |
| **`FR-INV-03`** | **Stock Transfer Management** (Chuyển kho 2 bước) | Điều chuyển hàng hóa giữa Chi nhánh A và Chi nhánh B thông qua quy trình 2 bước: Bước 1 — Xuất kho vận chuyển (`Out-bound transit`, ghi sổ cái `OUT` tại kho xuất); Bước 2 — Xác nhận nhập tại kho nhận (`In-bound confirmation`, ghi sổ cái `IN` tại kho nhận). | - Theo dõi rõ trạng thái hàng đang đi đường (`In-Transit`).<br/>- Mỗi bước chuyển trạng thái đều ghi nhận bút toán sổ cái độc lập tại chi nhánh tương ứng. |
| **`FR-INV-04`** | **Stocktake Management** (Kiểm kê & Cân bằng kho) | Tạo phiên kiểm kê kho (`Stock Audit Session`), ghi nhận số lượng kiểm đếm thực tế (`Physical Count`), so sánh với số dư phần mềm (`Software Balance`) và tự động sinh bút toán điều chỉnh (`Adjustment Ledger entries` loại `IN` hoặc `OUT`) để cân bằng kho. | - Chênh lệch thừa sinh bút toán `IN`, chênh lệch thiếu sinh bút toán `OUT` có `ReferenceId` trỏ về mã phiên kiểm kê. |


## 2. Giá và lượng ghi sổ khi nhập từ nhà cung cấp

Dòng `IN` dùng số thực nhận theo đơn vị gốc. Đơn giá đến từ phiếu mua đã duyệt, hoặc từ dòng hóa đơn trước thuế khi chênh lệch của hóa đơn kèm hàng được Owner duyệt trước lúc xác nhận nhập. Chia đơn giá theo đơn vị mua cho `FactorSnapshot` để ghi giá theo đơn vị gốc.

Hóa đơn gắn sau khi phiếu nhập đã `Confirmed` được lưu để đối chiếu. Gửi, duyệt, từ chối hoặc hủy hóa đơn đến sau không ghi dòng sổ kho hay cập nhật số dư hoặc đơn giá đã nhập.

---

## 3. Khai Thác Sổ Cái: Báo Cáo Tồn Kho Theo Kỳ & Thẻ Kho S12/S10 (`Upgrade 13 / OISMS-66-G2`)

Bảng sổ cái `inventory_transactions` là nguồn chân lý duy nhất (single source of truth) để tổng hợp báo cáo biến động kho và thẻ kho:

1. **Xác định số dư đầu kỳ và cuối kỳ:**
   - Số dư đầu kỳ tại thời điểm `startBoundaryUtc` (00:00 UTC+07 của `FromDate`) của một SKU tại một chi nhánh là `BalanceAfter` của dòng giao dịch cuối cùng có `created_at < startBoundaryUtc`. Nếu trước mốc này SKU chưa có giao dịch nào thì số dư đầu kỳ bằng `0`.
   - Khi xem toàn hệ thống (`branchId = null`), số dư đầu kỳ của SKU là tổng số dư đầu kỳ tại tất cả các chi nhánh.
   - Số dư cuối kỳ tại thời điểm `endBoundaryUtc` (00:00 UTC+07 ngày hôm sau của `ToDate`) là `BalanceAfter` của dòng giao dịch cuối cùng có `created_at < endBoundaryUtc`. Khi kỳ báo cáo kéo dài đến ngày hiện tại, số dư cuối kỳ khớp chính xác với `inventory_balances.on_hand`.

2. **Cơ chế bóc tách nguồn gốc biến động Nhập - Xuất:**
   - **Nhập kho (`IN`):** Bóc tách thành Mua hàng (`Purchase`), Khách trả lại (`Return`), Chuyển kho đến (`TransferIn`), và Kiểm kê / Điều chỉnh kho (`AdjustmentIn`).
   - **Xuất kho (`OUT`):** Bóc tách thành Bán hàng POS, Bán hàng Sàn TMĐT, Bán hàng Admin (dựa trên liên kết `OrderItem -> Order.Channel`), Chuyển kho đi (`TransferOut`), và Kiểm kê / Điều chỉnh kho (`AdjustmentOut`).
   - **Khi xem toàn bộ hệ thống (`branchId = null`):** Các giao dịch chuyển kho nội bộ giữa các chi nhánh (`transfer_item_id != null`) được loại trừ khỏi cột Nhập và Xuất để tránh tính trùng quy mô doanh nghiệp. Các giao dịch xuất kho điều chuyển mà chi nhánh nhận chưa xác nhận nhập trước mốc báo cáo được theo dõi riêng tại cột **Hàng đang chuyển (`InTransit`)**. Công thức bảo toàn: $\Sigma$ Tồn các chi nhánh + Hàng đang chuyển = Tổng tồn hệ thống.

3. **Thẻ kho (Stock Card S12 / S10):**
   - Truy vấn chi tiết theo từng SKU tại chi nhánh cụ thể theo khoảng thời gian.
   - Cung cấp dòng Số dư đầu kỳ, danh sách chi tiết các dòng sổ cái theo thứ tự thời gian (`SequenceNo`), và dòng Số dư cuối kỳ.
   - Mỗi dòng hiển thị mã chứng từ gốc (`ReferenceNumber` hoặc trích xuất từ bảng liên quan `Orders`, `PurchaseOrders`, `SalesReturns`, `StockTransfers`, `StockCounts`) kèm `ReferenceId` để hỗ trợ truy vết ngược.
   - **Mẫu S12-DN (Nhân viên - Staff):** Chỉ hiển thị số lượng nhập, xuất và tồn sau giao dịch. Toàn bộ thông tin đơn giá và thành tiền bị ẩn (`null`).
   - **Mẫu S10-DN (Chủ cửa hàng - Owner):** Hiển thị chi tiết số lượng, đơn giá vốn bình quân và thành tiền. Khi kỳ báo cáo kết thúc ở thời điểm hiện tại, hiển thị dòng chênh lệch giá trị làm tròn (RoundDiff) giữa tổng tích lũy giao dịch và giá trị sổ sách thực tế (`OnHand * CurrentUnitCost`).

4. **Hiệu năng & Tối ưu hóa truy vấn:**
   - Bắt buộc thực hiện phân trang trên danh mục SKU (`Skus`) trước (kèm bộ lọc danh mục/tìm kiếm), sau đó mới tổng hợp số liệu sổ cái cho tập SKU của trang (ví dụ 50 SKU).
   - Tận dụng triệt để index `ix_inventory_transactions_sku_history` `(tenant_id, sku_id, branch_id, created_at, sequence_no)`.

---

## 4. Chứng từ Phiếu xuất kho 02-VT gắn với dòng sổ cái `OUT` bán hàng (`Upgrade 13`, `OISMS-66-L2-G3`)

- Cấu trúc bảng `inventory_transactions` giữ nguyên bất biến (chỉ `INSERT`, `order_item_id` tiếp tục là nguồn chứng từ của dòng `OUT` bán hàng).
- Mỗi khi ghi dòng `inventory_transactions` loại `OUT` từ bán hàng (`order_item_id IS NOT NULL`), trong cùng database transaction hệ thống tạo kèm chứng từ `goods_issues` và dòng `goods_issue_items` tham chiếu `inventory_transaction_id` (`UNIQUE (tenant_id, inventory_transaction_id)`), lưu `base_quantity = inventory_transactions.quantity` và `unit_cost = inventory_transactions.unit_cost`.
- Khi truy vấn sổ cái (`GET /api/inventory/ledger`) hoặc truy vết chứng từ xuất kho (`GET /api/inventory/transactions/{id}/trace`), các dòng `OUT` bán hàng trả thêm `goodsIssueId` và `goodsIssueNumber` (`PXK-<số hóa đơn>` hoặc `PXK-<số hóa đơn>-<k>`).
