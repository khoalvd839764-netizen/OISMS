# FR-08: Trình Giả Lập Webhook Sàn TMĐT (E-commerce Webhook Simulator)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-08` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-SIM-01`, `FR-ORD-01`, `NFR-PERF-02` (Trang 2, 3) |
| **Gói công việc (Task Package)** | Task Package 3 — Centralized Order Hub & Anti-Oversell Engine |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả công cụ giả lập Webhook sàn TMĐT (Shopee, TikTok Shop, Lazada) phục vụ kiểm thử đồng bộ đơn và chịu tải. | `INIT-FR-08` |

---

## 1. Bảng Đặc Tả Chi Tiết Yêu Cầu (`FR-SIM-01`)

| Mã yêu cầu | Tên yêu cầu | Mô tả nghiệp vụ & Quy tắc kỹ thuật | Tiêu chí nghiệm thu (Acceptance Criteria) |
| :---: | :--- | :--- | :--- |
| **`FR-SIM-01`** | **E-commerce Webhook Simulator** | Cung cấp công cụ/endpoint giả lập cho phép lập trình viên và kiểm thử viên bắn các payload đơn hàng giả lập từ các sàn TMĐT (`Shopee`, `TikTok Shop`, `Lazada`) vào hệ thống để kiểm thử thông lượng xử lý (`Throughput`) và cơ chế khóa giữ chỗ (`Reservation Locks`). | - Chuẩn hóa payload từ các sàn về `Canonical Order Model` (`FR-ORD-01`).<br/>- Hỗ trợ bắn đồng thời nhiều request (ví dụ: 50 đơn hàng cùng lúc tranh 1 SKU) để kiểm chứng chống bán vượt (`NFR-PERF-02`). |
