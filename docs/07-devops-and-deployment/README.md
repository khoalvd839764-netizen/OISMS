# 07. TRIỂN KHAI, ĐÓNG GÓI & VẬN HÀNH (DEVOPS & DEPLOYMENT)

| Thông tin kiểm soát thư mục | Chi tiết |
| :--- | :--- |
| **Mã phân hệ (Module ID)** | `MOD-OPS-07` |
| **Phiên bản hiện hành** | `v1.1.0` |
| **Ngày cập nhật gần nhất** | `2026-11-06` |
| **Nguồn đối chiếu gốc** | `De bai CNPM.pdf` — Mục 3.2.e, 3.2.f, 3.2.g (Trang 5, 6) |

---

## Danh mục Tài liệu trong Phân hệ `07-devops-and-deployment/`

| STT | Mã tài liệu | Tên file | Phiên bản | Nội dung chính |
| :---: | :---: | :--- | :---: | :--- |
| **1** | `DOC-OPS-01` | [`01-docker-compose-guide.md`](./01-docker-compose-guide.md) | `v1.1.0` | 6 service Docker Compose (`postgres`, `redis`, `pgadmin`, `backend`, `admin-dashboard`, `pos-app`), cách chạy local và lưu ý database. |
| **2** | `DOC-OPS-02` | [`02-ci-cd-github-actions.md`](./02-ci-cd-github-actions.md) | `v1.1.0` | Pipeline GitHub Actions: build/test backend với PostgreSQL, build 2 frontend, `npm test` của POS. Không có bước deploy. |
| **3** | `DOC-OPS-03` | [`03-environment-configuration.md`](./03-environment-configuration.md) | `v1.1.0` | Biến `.env`, khóa cấu hình backend (`Database:*`, `Jwt:*`, `Hangfire:*`), `VITE_API_BASE_URL` và Nginx SPA fallback. |
| **4** | — | [`02-update-backend-sprint3.md`](./02-update-backend-sprint3.md) | lịch sử | Runbook nâng cấp DB Sprint 2 → Sprint 3 (`20261027_sprint3.sql`). Giữ làm mẫu quy trình backup → migration → khởi động lại. |

> Deploy lên AWS EC2 (Caddy, HTTPS, DNS, backup, cập nhật phiên bản) xem [`13-deploy-aws/`](../13-deploy-aws/README.md).
