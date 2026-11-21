# FR-07: Bán Hàng Tại Quầy PWA POS & Thanh Toán Nguyên Tử (Counter Sales PWA POS)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-07` |
| **Phiên bản (Version)** | `v1.2.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-19` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-POS-01..04`, `NFR-USA-02`, `NFR-PERF-01` (Trang 3, 4) |
| **Gói công việc (Task Package)** | Task Package 4 — POS Front-end & Real-time Integration |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả chi tiết giao diện bán hàng PWA POS, kiểm soát tồn khả dụng tại quầy, in hóa đơn nhiệt và luồng Fast Checkout nguyên tử. | `INIT-FR-07` |
| `v1.1.0` | `2026-11-06` | Nhóm dự án OISMS | Ghi chú cách hiện thực FR-POS-04: một transaction trừ tồn trực tiếp, không tạo bản ghi giữ chỗ. | `OISMS-46` |
| `v1.2.0` | `2026-11-19` | Nhóm dự án OISMS | Lập Phiếu xuất kho 02-VT (`goods_issues`, `goods_issue_items`) trong cùng transaction POS checkout và hỗ trợ Bảng kê xuất bán POS theo ngày (`UTC+07:00`). | `OISMS-66-L2-G3` |
| `v1.3.0` | `2026-11-21` | Nhóm dự án OISMS | Cho phép thu ngân bấm nút **In phiếu xuất kho** (khổ A4/A5 mẫu 02-VT, không có cột giá vốn, mỗi kho một trang) ngay tại màn hình sau checkout thành công qua `GET /api/pos/orders/{orderId}/goods-issues`. | `OISMS-66-U14-C` |

---

## 1. Bảng Đặc Tả Chi Tiết Yêu Cầu (`FR-POS`)

| Mã yêu cầu | Tên yêu cầu | Mô tả nghiệp vụ & Quy tắc kỹ thuật | Tiêu chí nghiệm thu (Acceptance Criteria) |
| :---: | :--- | :--- | :--- |
| **`FR-POS-01`** | **Fast Sales Interface** | Giao diện tối ưu hóa cho màn hình cảm ứng (Touch-friendly) và phím tắt bàn phím (Hotkey-optimized), hỗ trợ tìm kiếm sản phẩm tức thì theo Tên, mã SKU hoặc quét mã vạch Barcode. | - Phản hồi tìm kiếm/quét mã vạch đạt `p95 < 200ms`.<br/>- Cấu hình đầy đủ Web App Manifest cho chuẩn PWA. |
| **`FR-POS-02`** | **Counter Sales Constraint** | Kiểm tra tức thì số lượng tồn khả dụng (`available`) ngay khi chọn sản phẩm vào giỏ; chặn tuyệt đối việc bán vượt quá số lượng `available` để bảo vệ phần hàng đã giữ chỗ cho đơn online (`reserved`). | - Không cho phép tăng số lượng trong giỏ vượt quá `available`.<br/>- Các mặt hàng có `available == 0` bị khóa nút thêm vào giỏ. |
| **`FR-POS-03`** | **Checkout & Receipt Printing** | Hỗ trợ phương thức thanh toán Tiền mặt (tự tính tiền thừa trả khách) và Chuyển khoản QR Code; tự động kích hoạt hộp thoại in hóa đơn của trình duyệt qua `Browser Print API` (`window.print()`). | - Bản in hóa đơn nhiệt được định dạng chuẩn khổ giấy `80mm`/`58mm` qua CSS `@media print`. |
| **`FR-POS-04`** | **Fast Checkout Workflow** | Thực thi chuỗi `Reserve -> Confirm -> Deduct` (Giữ chỗ $\rightarrow$ Xác nhận $\rightarrow$ Trừ tồn & Ghi sổ cái + Chụp `CostPrice`) trong **một giao dịch cơ sở dữ liệu nguyên tử duy nhất (Single Atomic Database Transaction)** ngay khi thanh toán POS. | - Nếu bất kỳ dòng hàng nào trong giỏ không đủ tồn kho tại thời điểm bấm thanh toán, toàn bộ giao dịch được rollback 100%. |

> **Hiện thực FR-POS-04 (06/11/2026, cập nhật 19/11/2026 `OISMS-66-L2-G3`):** `POST /api/pos/checkout` (alias `/api/orders/checkout`) gộp `Reserve -> Confirm -> Deduct` thành một transaction: khóa tồn theo SKU, kiểm `available`, trừ `on_hand` trực tiếp, ghi `OUT`, chụp `CostPrice`, lưu đơn `Completed`, `payments`, `sales_invoices`, đồng thời lập luôn chứng từ **Phiếu xuất kho 02-VT** (`goods_issues` mang số `PXK-<số hóa đơn>` hoặc `PXK-<số hóa đơn>-<k>` theo kho xuất, và `goods_issue_items` gắn 1–1 với từng dòng `inventory_transactions` `OUT`). Không tạo dòng trong `reservations` vì giữ chỗ và tiêu thụ xảy ra trong cùng transaction; kết quả nghiệp vụ tương đương yêu cầu.

> **Hiện thực FR-POS-03 (15/11/2026, `OISMS-59-G4`):** Khi checkout thành công, `PosPage` mở `ReceiptPrintModal` với `autoPrint = true`; component tự gọi `window.print()` đúng 1 lần sau khi render (`requestAnimationFrame` + ref chặn gọi lặp), không có công tắc tắt. Phiếu tạm tính (`isDraft = true`), checkout lỗi hoặc trạng thái chưa rõ (`unknown` trong `PaymentModal`) không tự in (`shouldAutoPrint`). Nút In lại trên modal vẫn giữ nguyên làm đường dự phòng.

> **Bảng kê xuất bán POS theo ngày (`Upgrade 13`, `OISMS-66-L2-G3`):** Tại quầy POS chỉ tự động in hóa đơn bán lẻ nhiệt cho khách, không bắt thu ngân ký từng tờ phiếu xuất kho 02-VT lẻ. Chứng từ `goods_issues` của đơn POS vẫn được lưu tự động trong DB cho mỗi lần checkout; cuối ngày thủ kho/chủ cửa hàng xem và in **Bảng kê xuất bán POS theo ngày** (`GET /api/goods-issues/pos-daily-summary?date=YYYY-MM-DD&branchId=...`, tính theo múi giờ `UTC+07:00`) gồm bảng tổng hợp số lượng + giá trị xuất vốn theo SKU và danh sách các phiếu xuất/hóa đơn POS trong ngày (với `Staff`, các cột đơn giá vốn và thành tiền vốn được ẩn `null`).

> **Thu ngân in phiếu xuất kho 02-VT sau checkout (`Upgrade 14`, `OISMS-66-U14-C`):** Trên `ReceiptPrintModal`, khi checkout thành công (`!isDraft` và có `orderId`), hiển thị nút **In phiếu xuất kho** cạnh **In hóa đơn** (không hiện ở phiếu tạm tính, lỗi hoặc trạng thái chưa rõ; giữ nguyên hành vi tự in biên nhận 1 lần qua `shouldAutoPrint`). Khi bấm, `GoodsIssuePrintModal` gọi `GET /api/pos/orders/{orderId}/goods-issues`, hiển thị mẫu **02-VT** khổ **A4/A5**, không có cột giá vốn; nếu đơn xuất từ nhiều kho thì tách mỗi kho một trang (`pos-gi-page-break`) để in một lần. Khi gặp `404`, hiển thị *"Không tìm thấy phiếu xuất kho của đơn này"*; khi lỗi mạng, cho phép bấm **Thử lại** mà không gọi lại checkout.
