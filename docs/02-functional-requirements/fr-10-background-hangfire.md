# FR-10: Xử Lý Tác Vụ Nền Với Hangfire (Background Processing)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-10` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-SIM-03`, `FR-RSE-03` (Trang 3, 6) |
| **Gói công việc (Task Package)** | Task Package 3 — Centralized Order Hub & Anti-Oversell Engine |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả các tác vụ nền bất đồng bộ sử dụng Hangfire: tự động hủy đơn giữ chỗ quá hạn và tổng hợp báo cáo hằng ngày. | `INIT-FR-10` |
| `v1.1.0` | `2026-10-26` | Đăng Khoa (BE1) | Đính chính boundary auto-expiry thành `ExpiresAt <= cutoffUtc` (đồng bộ Confirm guard), phạm vi Task 6 chuyên biệt cho auto-cancel/release. | `S3-BE1-T06` |

---

## 1. Bảng Đặc Tả Chi Tiết Yêu Cầu (`FR-SIM-03`)

| Mã yêu cầu | Tên yêu cầu | Mô tả nghiệp vụ & Quy tắc kỹ thuật | Tiêu chí nghiệm thu (Acceptance Criteria) |
| :---: | :--- | :--- | :--- |
| **`FR-SIM-03`** | **Background Processing (Hangfire)** | Sử dụng **Hangfire** để thực thi các tác vụ bất đồng bộ nặng chạy ngầm:<br/>1. **Auto-canceling expired reserved orders:** Quét định kỳ các đơn hàng có phiếu giữ chỗ Active đã vượt quá thời gian giữ chỗ (`ExpiresAt <= cutoffUtc`, chuẩn hóa UTC server-side đồng bộ với Confirm guard), tự động chuyển đơn sang `Cancelled`, giải phóng `reserved` hai cấp (warehouse & branch balances) về lại `available`, và phát sự kiện Realtime `StockLevelChanged` ngoài transaction.<br/>*(Lưu ý phạm vi Task 6: Thực thi auto-canceling expired reservations. Hạng mục Daily report aggregations thuộc trách nhiệm BE2 trong task riêng).*<br/>2. **Daily report aggregations:** Tổng hợp trước dữ liệu doanh thu, giá vốn và lợi nhuận hằng ngày cho từng Tenant. | - Tác vụ giải phóng reservation quá hạn chạy an toàn trong transaction có khóa advisory (SKU tăng dần -> RequestKey -> Order) và khóa hàng (`FOR UPDATE`), tránh xung đột (race condition/deadlock) nếu nhân viên bấm `Confirm` hoặc POS checkout đúng lúc job đang quét.<br/>- Mỗi reservation chỉ được giải phóng đúng 1 lần duy nhất (idempotent), hỗ trợ xử lý cô lập lỗi theo batch. |
