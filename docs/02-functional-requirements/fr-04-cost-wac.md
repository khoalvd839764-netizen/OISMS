# FR-04: Tính Giá Vốn Bình Quân Gia Quyền & Chụp Giá Vốn Lịch Sử (Weighted Average Cost Calculation)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-04` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-COST-01`, `FR-COST-02` (Trang 2) |
| **Gói công việc (Task Package)** | Task Package 2 — Product Catalog & Append-Only Inventory Ledger |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả chi tiết công thức tính Giá vốn bình quân (WAC) và cơ chế chụp giá vốn khi xuất kho/bán hàng. | `INIT-FR-04` |

---

## 1. Bảng Đặc Tả Chi Tiết Yêu Cầu (`FR-COST`)

| Mã yêu cầu | Tên yêu cầu | Mô tả nghiệp vụ & Quy tắc kỹ thuật | Tiêu chí nghiệm thu (Acceptance Criteria) |
| :---: | :--- | :--- | :--- |
| **`FR-COST-01`** | **Automated Batch COGS Calculation** | Tự động tính toán lại giá vốn đơn vị của từng SKU ngay khi xác nhận Phiếu nhập hàng (`Purchase Receipt confirmation`) theo công thức bình quân gia quyền liên hoàn. | - Tính chính xác tuyệt đối qua nhiều lần nhập kho liên tiếp với các mức giá nhập khác nhau.<br/>- Xử lý an toàn trường hợp tồn kho ban đầu bằng `0` (`OldStock = 0` $\rightarrow$ `NewCost = ImportPrice`). |
| **`FR-COST-02`** | **COGS Snapshot on Dispatch** | Chụp (snapshot) chính xác giá vốn bình quân đơn vị hiện hành vào từng dòng sản phẩm của đơn hàng (`OrderItem.CostPrice`) ngay tại thời điểm đơn hàng chuyển sang trạng thái `Confirmed` hoặc được thanh toán tại quầy `POS`. | - `OrderItem.CostPrice` lưu giá trị bất biến tại thời điểm bán.<br/>- Các đợt nhập hàng mới làm thay đổi WAC sau đó không làm thay đổi `CostPrice` của các đơn hàng đã hoàn tất trước đó. |

---

## 2. Công Thức Toán Học Chuẩn Hóa (`FR-COST-01`)

$$\text{NewCost} = \frac{(\text{OldStock} \times \text{OldCost}) + (\text{ImportQty} \times \text{ImportPrice})}{\text{OldStock} + \text{ImportQty}}$$

**Ví dụ kiểm chứng:**
1. **Lô nhập 1:** Tồn đầu `OldStock = 0`, `OldCost = 0`. Nhập `100` sản phẩm với giá `100,000đ` $\rightarrow$ `NewCost = 100,000đ`, `OnHand = 100`.
2. **Xuất bán 20 sản phẩm:** Tồn còn `OldStock = 80`, giá vốn giữ nguyên `OldCost = 100,000đ`. Dòng đơn hàng lưu `OrderItem.CostPrice = 100,000đ`.
3. **Lô nhập 2:** Nhập thêm `120` sản phẩm với giá `150,000đ`:
   $$\text{NewCost} = \frac{(80 \times 100,000) + (120 \times 150,000)}{80 + 120} = \frac{8,000,000 + 18,000,000}{200} = 130,000\text{đ}$$
