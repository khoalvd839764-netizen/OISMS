# FR-12: Tích Hợp Dự Báo AI Gợi Ý Nhập Hàng (AI Stock Reorder Recommendations)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-12` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `3.2.g - Task package 5` (Trang 6) |
| **Gói công việc (Task Package)** | Task Package 5 — Reporting, AI Integration, Testing & Deployment |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả yêu cầu tích hợp dự báo nhu cầu bằng AI để gợi ý kế hoạch nhập lại hàng hóa. | `INIT-FR-12` |

---

## 1. Bảng Đặc Tả Chi Tiết Yêu Cầu (`FR-AI`)

| Mã yêu cầu | Tên yêu cầu | Mô tả nghiệp vụ & Quy tắc kỹ thuật | Tiêu chí nghiệm thu (Acceptance Criteria) |
| :---: | :--- | :--- | :--- |
| **`FR-AI-01`** | **AI Stock Reorder Forecasting** | Phân tích dữ liệu lịch sử biến động xuất bán từ Sổ cái (`InventoryTransaction`) và Đơn hàng (`OrderItems`) kết hợp số dư tồn khả dụng hiện tại (`available`) và thời gian chờ cung ứng (`Lead Time`) để dự báo tốc độ tiêu thụ và đề xuất số lượng cần nhập thêm cho từng SKU. | - Cung cấp API và giao diện bảng gợi ý nhập hàng thông minh trên Admin Dashboard (hiển thị SKU, Tồn hiện tại, Tốc độ bán trung bình/ngày, Số ngày dự kiến hết hàng và Số lượng đề xuất nhập). |
