# FR-06: Trung Tâm Đơn Hàng Đa Kênh & Máy Trạng Thái (Centralized Order Hub)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-06` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-ORD-01..03` (Trang 2) |
| **Gói công việc (Task Package)** | Task Package 3 — Centralized Order Hub & Anti-Oversell Engine |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả chi tiết mô hình đơn hàng hợp nhất (`Canonical Order Model`), máy trạng thái và quy trình xử lý đơn. | `INIT-FR-06` |

---

## 1. Bảng Đặc Tả Chi Tiết Yêu Cầu (`FR-ORD`)

| Mã yêu cầu | Tên yêu cầu | Mô tả nghiệp vụ & Quy tắc kỹ thuật | Tiêu chí nghiệm thu (Acceptance Criteria) |
| :---: | :--- | :--- | :--- |
| **`FR-ORD-01`** | **Omnichannel Order Ingestion** | Chuẩn hóa đơn hàng từ đa kênh (Bán tại quầy `POS`, Tạo thủ công trên `Admin`, và `Webhook Simulator` từ Shopee/TikTok/Lazada) về một **Mô hình Đơn hàng Hợp nhất (`Canonical Order Model`)**. | - Lưu rõ nguồn kênh bán (`Channel`: `POS`, `ADMIN`, `SHOPEE`, `TIKTOK`, `LAZADA`) và mã đơn gốc từ sàn (`ExternalOrderId`). |
| **`FR-ORD-02`** | **Order State Machine** | Thực thi nghiêm ngặt vòng đời chuyển trạng thái đơn hàng: `Draft -> Reserved -> Confirmed -> Completed` (hoặc `Cancelled`). | - Chặn mọi bước nhảy trạng thái trái phép (ví dụ: từ `Cancelled` sang `Confirmed` hoặc từ `Draft` thẳng sang `Completed` mà không qua kiểm tra tồn kho). |
| **`FR-ORD-03`** | **Order Processing** | Cho phép nhân viên (`Staff`/`Owner`) xem danh sách đơn hàng chờ xử lý, phê duyệt đơn (`Approve/Confirm`), in phiếu giao hàng (`Print Shipping Slips`) hoặc hủy đơn (`Cancel`). | - Giao diện Web Admin hiển thị danh sách đơn theo trạng thái, hỗ trợ duyệt đơn, hủy đơn và mở mẫu in phiếu giao hàng. |
