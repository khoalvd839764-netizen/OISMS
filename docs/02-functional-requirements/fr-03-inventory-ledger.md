# FR-03: Sổ Cái Kho Bất Biến, Nhập Kho, Chuyển Kho & Kiểm Kê (Inventory Ledger & Stock Management)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-03` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-INV-01..04`, `NFR-SEC-03` (Trang 2, 4) |
| **Gói công việc (Task Package)** | Task Package 2 — Product Catalog & Append-Only Inventory Ledger |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả chi tiết nhóm yêu cầu chức năng `FR-INV-01` đến `FR-INV-04` và quy tắc bất biến Sổ cái. | `INIT-FR-03` |

---

## 1. Bảng Đặc Tả Chi Tiết Yêu Cầu (`FR-INV`)

| Mã yêu cầu | Tên yêu cầu | Mô tả nghiệp vụ & Quy tắc kỹ thuật | Tiêu chí nghiệm thu (Acceptance Criteria) |
| :---: | :--- | :--- | :--- |
| **`FR-INV-01`** | **Inventory Ledger Recording** (Sổ cái kho bất biến) | Ghi nhận nghiêm ngặt mọi biến động kho (`Import`, `Export`, `Sale`, `Return`, `Stocktake`, `Transfer`) vào bảng sổ cái chỉ ghi thêm `InventoryTransaction` gồm các trường: `TenantId`, `BranchId`, `SKUId`, `Type (IN/OUT)`, `Quantity`, `BalanceAfter`, `ReferenceId`, `CreatedAt`. **Nghiêm cấm cập nhật trực tiếp cột `on_hand`.** | - Không tồn tại bất kỳ lệnh `UPDATE` hoặc `DELETE` nào trên bảng `inventory_transactions`.<br/>- Mọi thay đổi số lượng tồn đều có bản ghi sổ cái đối ứng với `BalanceAfter` khớp tuyệt đối. |
| **`FR-INV-02`** | **Purchase Receipt Management** (Nhập kho nhà cung cấp) | Xử lý phiếu nhập hàng từ Nhà cung cấp (`Draft -> Confirmed`): Khi xác nhận phiếu nhập $\rightarrow$ Ghi sổ cái `IN` $\rightarrow$ Tăng tồn vật lý (`on_hand`) $\rightarrow$ Tự động tính lại giá vốn bình quân (`WAC`). | - Toàn bộ quá trình xác nhận phiếu nhập nhiều dòng thực thi trong 1 Transaction ACID.<br/>- Chặn xác nhận lặp phiếu nhập đã `Confirmed` (`409 Conflict`). |
| **`FR-INV-03`** | **Stock Transfer Management** (Chuyển kho 2 bước) | Điều chuyển hàng hóa giữa Chi nhánh A và Chi nhánh B thông qua quy trình 2 bước: Bước 1 — Xuất kho vận chuyển (`Out-bound transit`, ghi sổ cái `OUT` tại kho xuất); Bước 2 — Xác nhận nhập tại kho nhận (`In-bound confirmation`, ghi sổ cái `IN` tại kho nhận). | - Theo dõi rõ trạng thái hàng đang đi đường (`In-Transit`).<br/>- Mỗi bước chuyển trạng thái đều ghi nhận bút toán sổ cái độc lập tại chi nhánh tương ứng. |
| **`FR-INV-04`** | **Stocktake Management** (Kiểm kê & Cân bằng kho) | Tạo phiên kiểm kê kho (`Stock Audit Session`), ghi nhận số lượng kiểm đếm thực tế (`Physical Count`), so sánh với số dư phần mềm (`Software Balance`) và tự động sinh bút toán điều chỉnh (`Adjustment Ledger entries` loại `IN` hoặc `OUT`) để cân bằng kho. | - Chênh lệch thừa sinh bút toán `IN`, chênh lệch thiếu sinh bút toán `OUT` có `ReferenceId` trỏ về mã phiên kiểm kê. |
