# 04. Độ Sẵn Sàng & Khả Năng Sử Dụng Giao Diện POS (Availability & Usability)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-NFR-04` |
| **Phiên bản (Version)** | `v1.1.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-06` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `NFR-USA-01`, `NFR-USA-02` (Trang 4) |
| **Phạm vi áp dụng** | Hạ tầng Vận hành, Web Admin Dashboard & Web PWA POS |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả chỉ tiêu Uptime 99.5% và tiêu chuẩn trải nghiệm người dùng tối giản trên PWA POS. | `INIT-NFR-04` |
| `v1.1.0` | `2026-11-06` | Nhóm dự án OISMS | Ghi chú hiện trạng độ sẵn sàng của bản demo một máy. | `OISMS-46` |

---

## 1. Đặc Tả Chi Tiết (`NFR-USA-01`, `NFR-USA-02`)

| Mã NFR | Tên yêu cầu | Đặc tả kỹ thuật bắt buộc |
| :---: | :--- | :--- |
| **`NFR-USA-01`** | **Availability** (Độ sẵn sàng của hệ thống) | - Hướng tới mục tiêu thời gian hoạt động liên tục (**Uptime đạt `99.5%`**) trong suốt khung giờ kinh doanh cao điểm từ **`07:00 AM – 10:00 PM`**. |
| **`NFR-USA-02`** | **POS Usability** (Tính dễ sử dụng tại quầy POS) | - Bố cục giao diện POS tối giản, cho phép thu ngân hoàn tất thanh toán đơn hàng trong **dưới 3 cú nhấp chuột (`under 3 clicks`)** hoặc thao tác **Quét mã vạch + Phím Enter (`Barcode Scan + Enter`)**.<br/>- Giao diện đáp ứng (**Responsive Layout**) tương thích mượt mà trên máy tính để bàn (Desktop), máy tính bảng (Tablet) và thiết bị POS cầm tay (Mobile Handheld POS). |

> **Hiện trạng (06/11/2026):** bản demo chạy trên một máy EC2, không có dự phòng và có thể bị tắt ngoài giờ demo để tiết kiệm chi phí, nên chưa có số đo uptime. Hướng đáp ứng `NFR-USA-01` khi chạy thật: xem [giới hạn mô hình](../13-deploy-aws/01-kien-truc-trien-khai.md#7-giới-hạn-của-mô-hình-hiện-tại).
