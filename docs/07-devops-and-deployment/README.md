# 07. TRIỂN KHAI, ĐÓNG GÓI & VẬN HÀNH (DEVOPS & DEPLOYMENT)

| Thông tin kiểm soát thư mục | Chi tiết |
| :--- | :--- |
| **Mã phân hệ (Module ID)** | `MOD-OPS-07` |
| **Phiên bản hiện hành** | `v1.0.0` |
| **Ngày cập nhật gần nhất** | `2026-10-22` |
| **Nguồn đối chiếu gốc** | `De bai CNPM.pdf` — Mục 3.2.e, 3.2.f, 3.2.g (Trang 5, 6) |

---

## Danh mục Tài liệu trong Phân hệ `07-devops-and-deployment/`

| STT | Mã tài liệu | Tên file | Phiên bản | Nội dung chính |
| :---: | :---: | :--- | :---: | :--- |
| **1** | `DOC-OPS-01` | [`01-docker-compose-guide.md`](./01-docker-compose-guide.md) | `v1.0.0` | Kiến trúc 4 dịch vụ Docker Compose (`postgres`, `backend`, `admin-dashboard`, `pos-app`) và lệnh khởi chạy. |
| **2** | `DOC-OPS-02` | [`02-ci-cd-github-actions.md`](./02-ci-cd-github-actions.md) | `v1.0.0` | Đặc tả quy trình CI/CD GitHub Actions tự động build và chạy test với PostgreSQL container. |
| **3** | `DOC-OPS-03` | [`03-environment-configuration.md`](./03-environment-configuration.md) | `v1.0.0` | Danh mục biến môi trường `.env` và cấu hình Nginx SPA Fallback (`try_files`). |
