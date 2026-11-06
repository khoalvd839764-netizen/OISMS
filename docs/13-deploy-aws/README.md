# 13. TRIỂN KHAI LÊN AWS (DEPLOY AWS)

| Thông tin kiểm soát thư mục | Chi tiết |
| :--- | :--- |
| **Mã phân hệ (Module ID)** | `MOD-AWS-13` |
| **Phiên bản hiện hành** | `v1.0.0` |
| **Ngày cập nhật gần nhất** | `2026-11-06` |
| **Nguồn đối chiếu** | `docker-compose.yml`, `docker-compose.prod.yml`, `docker/caddy/Caddyfile`, `.env.production.example`, `src/backend/Dockerfile`, Dockerfile và `nginx.conf` của 2 frontend, `src/backend/src/OISM.WebApi/Program.cs` |

---

## Bản Demo Đang Chạy

| Ứng dụng | Địa chỉ |
| :--- | :--- |
| Admin Dashboard (kèm POS tích hợp tại `/pos`) | [https://oisms.khoadang.site](https://oisms.khoadang.site) |
| POS PWA riêng | [https://pos.oisms.khoadang.site](https://pos.oisms.khoadang.site) |
| Kiểm tra sẵn sàng | `https://oisms.khoadang.site/health/ready` |

Toàn bộ hệ thống chạy trên **một máy EC2** bằng Docker Compose, có **Caddy** đứng trước làm reverse proxy và tự cấp chứng chỉ HTTPS. Máy có thể được tắt khi không demo để tiết kiệm chi phí, nên link có lúc không truy cập được.

> Giá trị domain thực tế nằm trong `.env.production` trên máy chủ (không có trong git). Bảng trên lấy theo README và tài liệu dự án; nếu đổi domain thì sửa cả `.env.production` lẫn bảng này.

---

## Tóm Tắt Một Màn Hình

```text
Trình duyệt ──HTTPS──► EC2 (Elastic IP) ── Security Group: chỉ mở 80, 443, 22 (IP quản trị)
                         │
                         └─ Caddy :80/:443  (container oism_caddy, chứng chỉ Let's Encrypt)
                              ├─ /api/*, /hubs/*, /health, /health/* ─► backend:8080 (.NET 8)
                              │                                            └─► postgres:5432 (oisms_app)
                              ├─ ADMIN_DOMAIN, các đường dẫn khác ───────► admin-dashboard:80 (nginx, SPA)
                              └─ POS_DOMAIN, các đường dẫn khác ─────────► pos-app:80 (nginx, PWA)
```

Lệnh deploy duy nhất:

```bash
docker compose --env-file .env.production -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

---

## Danh Mục Tài Liệu

| STT | Tên file | Nội dung chính |
| :---: | :--- | :--- |
| **1** | [`01-kien-truc-trien-khai.md`](./01-kien-truc-trien-khai.md) | Thành phần, luồng request, cổng, mạng, volume; khác biệt giữa bản local và bản production. |
| **2** | [`02-chuan-bi-ha-tang-aws.md`](./02-chuan-bi-ha-tang-aws.md) | Tạo EC2, Security Group, Elastic IP, DNS, cài Docker, swap. |
| **3** | [`03-cau-hinh-moi-truong-production.md`](./03-cau-hinh-moi-truong-production.md) | `.env.production`, giải thích `docker-compose.prod.yml` và `Caddyfile`, bí mật. |
| **4** | [`04-quy-trinh-deploy-va-cap-nhat.md`](./04-quy-trinh-deploy-va-cap-nhat.md) | Deploy lần đầu, tạo tenant đầu tiên, cập nhật phiên bản, chạy migration, rollback. |
| **5** | [`05-van-hanh-giam-sat-backup.md`](./05-van-hanh-giam-sat-backup.md) | Log, health check, backup/restore `pg_dump`, tắt/bật máy, xử lý sự cố. |
| **6** | [`06-kien-thuc-nen-tang.md`](./06-kien-thuc-nen-tang.md) | Kiến thức deploy liên quan: EC2, DNS, TLS/ACME, reverse proxy, Docker multi-stage, Compose overlay, biến build-time, SPA, SignalR qua proxy, least privilege, chi phí. |

---

## Những Điều Không Được Quên

1. `.env.production` chỉ nằm trên máy chủ, không commit. Mọi giá trị `CHANGE_ME` phải đổi.
2. Production **không seed demo** và **không có Swagger**. Database mới trống; tạo tenant đầu tiên qua màn Đăng ký hoặc `POST /api/auth/register-tenant`.
3. Frontend nhúng URL API lúc build: đổi domain thì phải build lại (`--build`).
4. `init-oisms.sh` chỉ chạy khi volume database trống. Cập nhật hệ thống đang có dữ liệu thì backup rồi chạy migration thủ công.
5. Không chạy `docker compose down -v` trên máy chủ: lệnh này xóa database và chứng chỉ HTTPS.
6. CI trên GitHub Actions chỉ build và test, **không tự deploy**.
