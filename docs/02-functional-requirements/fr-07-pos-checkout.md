# FR-07: Bán Hàng Tại Quầy PWA POS & Thanh Toán Nguyên Tử (Counter Sales PWA POS)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-07` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-POS-01..04`, `NFR-USA-02`, `NFR-PERF-01` (Trang 3, 4) |
| **Gói công việc (Task Package)** | Task Package 4 — POS Front-end & Real-time Integration |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả chi tiết giao diện bán hàng PWA POS, kiểm soát tồn khả dụng tại quầy, in hóa đơn nhiệt và luồng Fast Checkout nguyên tử. | `INIT-FR-07` |

---

## 1. Bảng Đặc Tả Chi Tiết Yêu Cầu (`FR-POS`)

| Mã yêu cầu | Tên yêu cầu | Mô tả nghiệp vụ & Quy tắc kỹ thuật | Tiêu chí nghiệm thu (Acceptance Criteria) |
| :---: | :--- | :--- | :--- |
| **`FR-POS-01`** | **Fast Sales Interface** | Giao diện tối ưu hóa cho màn hình cảm ứng (Touch-friendly) và phím tắt bàn phím (Hotkey-optimized), hỗ trợ tìm kiếm sản phẩm tức thì theo Tên, mã SKU hoặc quét mã vạch Barcode. | - Phản hồi tìm kiếm/quét mã vạch đạt `p95 < 200ms`.<br/>- Cấu hình đầy đủ Web App Manifest cho chuẩn PWA. |
| **`FR-POS-02`** | **Counter Sales Constraint** | Kiểm tra tức thì số lượng tồn khả dụng (`available`) ngay khi chọn sản phẩm vào giỏ; chặn tuyệt đối việc bán vượt quá số lượng `available` để bảo vệ phần hàng đã giữ chỗ cho đơn online (`reserved`). | - Không cho phép tăng số lượng trong giỏ vượt quá `available`.<br/>- Các mặt hàng có `available == 0` bị khóa nút thêm vào giỏ. |
| **`FR-POS-03`** | **Checkout & Receipt Printing** | Hỗ trợ phương thức thanh toán Tiền mặt (tự tính tiền thừa trả khách) và Chuyển khoản QR Code; tự động kích hoạt hộp thoại in hóa đơn của trình duyệt qua `Browser Print API` (`window.print()`). | - Bản in hóa đơn nhiệt được định dạng chuẩn khổ giấy `80mm`/`58mm` qua CSS `@media print`. |
| **`FR-POS-04`** | **Fast Checkout Workflow** | Thực thi chuỗi `Reserve -> Confirm -> Deduct` (Giữ chỗ $\rightarrow$ Xác nhận $\rightarrow$ Trừ tồn & Ghi sổ cái + Chụp `CostPrice`) trong **một giao dịch cơ sở dữ liệu nguyên tử duy nhất (Single Atomic Database Transaction)** ngay khi thanh toán POS. | - Nếu bất kỳ dòng hàng nào trong giỏ không đủ tồn kho tại thời điểm bấm thanh toán, toàn bộ giao dịch được rollback 100%. |
