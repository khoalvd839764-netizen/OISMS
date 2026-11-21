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

> **Hiện trạng (21/11/2026 — `FIN-E`):**
> - **`NFR-USA-01` (Uptime):** đã có script `test-oisms/nfr/uptime-probe/uptime-probe.mjs` (đo khung `07:00–22:00 Asia/Ho_Chi_Minh`, xuất CSV + JSON), đã smoke-test local; bản deploy AWS chạy một máy EC2 và có thể tắt ngoài giờ demo nên **`CHƯA ĐO TRÊN DEPLOY`**. Bằng chứng: [`evidence/goi-5/NFR-USA-01/`](../06-testing-and-qa/evidence/goi-5/NFR-USA-01/2026-11-21-FIN-E.md).
> - **`NFR-USA-02` (POS Usability):** luồng `Barcode + Enter → F4 → xác nhận` hoàn tất trong `2` bước (`< 3` lần bấm) được kiểm chứng trong `test-oisms/frontend/pos-app/test-live.mjs`; phần giao diện giả lập 3 kích thước (`1366×768`, `768×1024`, `375×812`) và thiết bị cầm tay thật ghi rõ giới hạn tại [`evidence/goi-5/NFR-USA-02/`](../06-testing-and-qa/evidence/goi-5/NFR-USA-02/2026-11-21-FIN-E.md). Hướng đáp ứng `NFR-USA-01` khi chạy thật: xem [giới hạn mô hình](../13-deploy-aws/01-kien-truc-trien-khai.md#7-giới-hạn-của-mô-hình-hiện-tại).
