# 01. Hướng Dẫn Chạy Hệ Thống Bằng Docker Compose (Docker Compose Guide)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-OPS-01` |
| **Phiên bản (Version)** | `v1.1.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-06` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `3.2.e`, `3.2.f`, `3.2.g` (Trang 5, 6) |
| **Phạm vi áp dụng** | `docker-compose.yml`, `docker-compose.prod.yml`, `docker/`, `src/backend/Dockerfile`, Dockerfile của 2 frontend |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả kiến trúc đóng gói Docker Compose trọn bộ và hướng dẫn khởi chạy hệ thống tức thì. | `INIT-OPS-01` |
| `v1.1.0` | `2026-11-06` | Nhóm dự án OISMS | Đồng bộ với compose hiện tại: 6 service (thêm `redis`, `pgadmin`), schema 43 bảng do `init-oisms.sh` tạo, backend không tự chạy migration, seed demo chỉ ở Development. | `OISMS-46` |

---

## 1. Các Service Trong `docker-compose.yml` (`3.2.f`)

| Service | Container | Image / Build | Cổng host → container | Vai trò |
| :--- | :--- | :--- | :---: | :--- |
| `postgres` | `oism_postgres` | `postgres:16-alpine` | `${POSTGRES_PORT:-5432}` → `5432` | Database. Volume `postgres_data`. Lần đầu volume trống chạy `docker/postgres/init-oisms.sh` để nạp `docs/database/oisms_schema.sql` (44 bảng) và tạo tài khoản runtime. Có `healthcheck` bằng `pg_isready`. |
| `redis` | `oism_redis` | `redis:7-alpine` | `${REDIS_PORT:-6379}` → `6379` | Có sẵn nhưng code backend chưa dùng. |
| `pgadmin` | `oism_pgadmin` | `dpage/pgadmin4:latest` | `${PGADMIN_PORT:-5050}` → `80` | Giao diện quản trị DB, chỉ dùng local. |
| `backend` | `oism_backend` | `src/backend/Dockerfile` (SDK 8.0 → ASP.NET 8.0) | `${BACKEND_PORT:-5000}` → `8080` | Web API, SignalR hub `/hubs/notifications`, health check. Chờ `postgres` healthy. |
| `admin-dashboard` | `oism_admin_dashboard` | Node 22 build → `nginx:alpine` | `${ADMIN_PORT:-5173}` → `80` | Web Admin; `/pos` chuyển sang POS 5174. |
| `pos-app` | `oism_pos_app` | Node 22 build → `nginx:alpine` | `${POS_PORT:-5174}` → `80` | POS hiện tại chuyển từ Admin, một implementation quầy. |

Mọi service nằm trong network `oism_network`. Bên trong network, backend gọi DB bằng host `postgres`, cổng `5432`.

---

## 2. Chạy Local

```powershell
Copy-Item .env.example .env      # rồi đổi mật khẩu trong .env
docker compose up -d --build
docker compose ps
```

Hoặc dùng `scripts/start-docker.ps1`.

| Địa chỉ | Nội dung |
| :--- | :--- |
| `http://localhost:5000/swagger` | Swagger (chỉ khi `ASPNETCORE_ENVIRONMENT=Development`, mặc định của compose) |
| `http://localhost:5000/health/live` | Tiến trình còn chạy |
| `http://localhost:5000/health/ready` | Kết nối bằng tài khoản runtime, đủ 43 bảng, trigger `ledger_append_only` bật, tài khoản không có quyền quản trị. Lỗi trả `503`. `/health` là bí danh. |
| `http://localhost:5173` | Admin Dashboard, đăng nhập tại `/login` |
| `http://localhost:5174` | POS PWA |
| `http://localhost:5050` | pgAdmin |

Chạy không cần Docker cho backend/frontend: `scripts/run-backend.ps1`, `scripts/run-admin.ps1`, `scripts/run-pos.ps1` (vẫn cần PostgreSQL, có thể chỉ chạy `docker compose up -d postgres`).

---

## 3. Điều Cần Biết Về Database Trong Compose

- `init-oisms.sh` chỉ chạy khi volume `postgres_data` trống. Volume đã có dữ liệu thì script không chạy lại; rebuild image cũng không nâng cấp schema.
- Backend **không** chạy EF migrations và không tự tạo bảng. Nếu `/health/ready` trả `503` với thông điệp `Database schema is incomplete`, phải chạy các file còn thiếu trong `docker/postgres/migrations/` theo [hướng dẫn nâng cấp](../database/README.md#nâng-cấp-database-đã-có-dữ-liệu-cả-nhóm-đọc-khi-kéo-code-mới).
- Không chạy `oisms_schema.sql` lên DB đã có dữ liệu. Không dùng `docker compose down -v` trừ khi chủ động xóa sạch dữ liệu local.
- Đổi mật khẩu trong `.env` không đổi mật khẩu role trên volume cũ; dùng `ALTER ROLE` bằng tài khoản `postgres`.
- Seed dữ liệu demo chỉ chạy khi môi trường là `Development` hoặc `Test`, `Database:SeedDemoData=true` và có `Database:DemoSeedPassword` (`DemoSeedPolicy`). Compose không đặt hai biến này nên mặc định không seed; `appsettings.Development.json` bật seed khi chạy bằng `dotnet run`.

---

## 4. Chạy Bản Production

Bản deploy dùng chồng thêm `docker-compose.prod.yml` (đóng mọi cổng nội bộ, tắt pgAdmin, bắt buộc `JWT_SECRET`, thêm Caddy cho HTTPS):

```bash
docker compose --env-file .env.production -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

Chi tiết hạ tầng AWS, DNS, HTTPS và vận hành: [`docs/13-deploy-aws/`](../13-deploy-aws/README.md).
