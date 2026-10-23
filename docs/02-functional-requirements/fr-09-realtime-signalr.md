# FR-09: Thông Báo Thời Gian Thực Qua SignalR (Real-time Push Notification)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-09` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-SIM-02` (Trang 3, 6) |
| **Gói công việc (Task Package)** | Task Package 4 — POS Front-end & Real-time Integration |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả cơ chế đẩy thông báo đơn hàng mới và cảnh báo tồn kho thời gian thực bằng SignalR. | `INIT-FR-09` |

---

## 1. Bảng Đặc Tả Chi Tiết Yêu Cầu (`FR-SIM-02`)

| Mã yêu cầu | Tên yêu cầu | Mô tả nghiệp vụ & Quy tắc kỹ thuật | Tiêu chí nghiệm thu (Acceptance Criteria) |
| :---: | :--- | :--- | :--- |
| **`FR-SIM-02`** | **Real-time Push Notification** | Sử dụng **ASP.NET Core SignalR** để đẩy cảnh báo âm thanh và thông báo nổi (`Sound/Popup Alerts`) ngay khi có đơn hàng mới hoặc biến động tồn kho trực tiếp tới màn hình `Admin Dashboard` và `POS` mà không cần tải lại trang (`No page refresh`). | - Cô lập kênh nhận thông báo theo `TenantId` (Nhóm SignalR `tenant:{TenantId}`), đảm bảo cửa hàng A không nhận thông báo đơn của cửa hàng B.<br/>- Hiển thị tức thì popup thông báo trên giao diện khi có đơn mới từ Webhook/POS. |
