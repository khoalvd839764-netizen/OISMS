# 03. Cấu Hình Môi Trường Production

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-AWS-03` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-06` |
| **Phạm vi áp dụng** | `.env.production.example`, `docker-compose.prod.yml`, `docker/caddy/Caddyfile` |

---

## 1. Tạo `.env.production`

```bash
cd ~/OISMS
cp .env.production.example .env.production
chmod 600 .env.production
nano .env.production
```

| Biến | Ví dụ | Ghi chú |
| :--- | :--- | :--- |
| `ADMIN_DOMAIN` | `oisms.khoadang.site` | Không có `https://`, không có `/` cuối |
| `POS_DOMAIN` | `pos.oisms.khoadang.site` | Như trên |
| `SIM_DOMAIN` | `sim.oisms.khoadang.site` | Giao diện giả lập đơn sàn. Bắt buộc: thiếu thì Compose dừng. Cần thêm bản ghi DNS A trỏ về Elastic IP |
| `POSTGRES_USER` | `postgres` | Tài khoản quản trị DB, chỉ dùng cho init, migration, backup |
| `POSTGRES_PASSWORD` | `openssl rand -hex 32` | |
| `POSTGRES_DB` | `oisms` | |
| `OISMS_APP_USER` | `oisms_app` | Tài khoản runtime; không được là `postgres` hay `oisms_runtime` |
| `OISMS_APP_PASSWORD` | `openssl rand -hex 32` | |
| `JWT_SECRET` | `openssl rand -hex 32` | Tối thiểu 32 ký tự. Đổi giá trị làm mọi token cũ hết hiệu lực |
| `PGADMIN_PASSWORD` | bất kỳ | `docker-compose.yml` bắt buộc có, dù production không chạy pgAdmin |

Tạo nhanh giá trị ngẫu nhiên:

```bash
openssl rand -hex 32
```

Dùng chuỗi hex để tránh ký tự `$` (Compose hiểu là biến) và dấu nháy. Nếu buộc phải có `$`, viết thành `$$`.

**Không** commit, gửi qua chat hay in file này ra log. `.gitignore` đã liệt kê `.env`, `.env.local`, `.env.production`, nhưng vẫn kiểm tra `git status` trước khi commit trên máy chủ.

## 2. `docker-compose.prod.yml` Làm Gì

File này được nạp **sau** `docker-compose.yml`; Compose gộp hai file, khóa ở file sau ghi đè khóa ở file trước.

| Service | Thay đổi | Tác dụng |
| :--- | :--- | :--- |
| `postgres`, `redis`, `backend`, `admin-dashboard`, `pos-app`, `marketplace-simulator` | `ports: !reset []` | Xóa toàn bộ cổng publish của file gốc; chỉ còn truy cập trong `oism_network` |
| `pgadmin` | `profiles: ["tools"]` | Không khởi động trừ khi chạy kèm `--profile tools` |
| `backend` | `ASPNETCORE_ENVIRONMENT: Production`, `Jwt__Secret: ${JWT_SECRET:?...}` | Tắt Swagger, chặn seed demo, bắt buộc có JWT secret (thiếu thì Compose báo lỗi và dừng) |
| `admin-dashboard` | build arg `VITE_API_BASE_URL: https://${ADMIN_DOMAIN}` | Bundle gọi API trên chính domain Admin |
| `pos-app` | build arg `VITE_API_BASE_URL: https://${POS_DOMAIN}` | Bundle gọi API trên chính domain POS |
| `marketplace-simulator` | build arg `VITE_API_BASE_URL: https://${SIM_DOMAIN}`, `VITE_ADMIN_URL: https://${ADMIN_DOMAIN}` | Bundle chỉ gọi `/api/webhooks/*` trên chính domain giả lập; link sang trang Kết nối sàn của Admin |
| `admin-dashboard` (thêm) | build arg `VITE_SIMULATOR_URL: https://${SIM_DOMAIN}` | Nút "Mở app giả lập" ở trang Kết nối sàn |
| `caddy` (mới) | image `caddy:2.10-alpine`, cổng `80:80`, `443:443`, mount `Caddyfile`, volume `caddy_data`, `caddy_config` | Reverse proxy + HTTPS |

Kiểm tra cấu hình gộp trước khi chạy (không in bí mật ra màn hình chung):

```bash
docker compose --env-file .env.production -f docker-compose.yml -f docker-compose.prod.yml config --quiet && echo OK
```

## 3. `Caddyfile`

```caddy
(backend_routes) {
	@backend path /api/* /hubs/* /health /health/*
	handle @backend {
		reverse_proxy backend:8080
	}
}

{$ADMIN_DOMAIN} {
	encode gzip
	import backend_routes
	handle {
		reverse_proxy admin-dashboard:80
	}
}

{$POS_DOMAIN} {
	encode gzip
	import backend_routes
	handle {
		reverse_proxy pos-app:80
	}
}

{$SIM_DOMAIN} {
	encode gzip
	handle /api/webhooks/* {
		reverse_proxy backend:8080
	}
	handle /api/* {
		respond 404
	}
	handle {
		reverse_proxy marketplace-simulator:80
	}
}
```

- `{$ADMIN_DOMAIN}`, `{$POS_DOMAIN}` là biến môi trường của container Caddy, được compose truyền vào.
- `{$SIM_DOMAIN}` không dùng `backend_routes`: chỉ mở đường webhook (xác thực bằng `X-Webhook-Secret`), các API khác trả 404 nên domain giả lập không lộ API quản trị.
- `(backend_routes)` là snippet dùng lại cho Admin và POS; `handle` loại trừ lẫn nhau nên request API không bao giờ rơi xuống frontend.
- Site có tên miền thật nên Caddy tự bật HTTPS: xin chứng chỉ Let's Encrypt, chuyển HTTP sang HTTPS, gia hạn trước khi hết hạn.
- `reverse_proxy` tự xử lý WebSocket cho SignalR và tự thêm `X-Forwarded-For`, `X-Forwarded-Proto`.

Muốn thêm header bảo mật hoặc chặn `/health/ready` khỏi Internet thì sửa file này rồi `docker compose ... restart caddy`.

## 4. Cấu Hình Backend Trên Production

| Khóa | Nguồn | Giá trị |
| :--- | :--- | :--- |
| `ASPNETCORE_ENVIRONMENT` | `docker-compose.prod.yml` | `Production` |
| `Database__Host/Port/Database/Username/Password` | `docker-compose.yml` | `postgres`, `5432`, `${POSTGRES_DB}`, `${OISMS_APP_USER}`, `${OISMS_APP_PASSWORD}` |
| `Jwt__Secret` | `docker-compose.prod.yml` | `${JWT_SECRET}` |
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpiryMinutes` | `appsettings.json` | `OISM_WebAPI`, `OISM_Client`, `60` |
| `Hangfire__Enabled` | `docker-compose.yml` | `${HANGFIRE_ENABLED:-false}` |
| `Hangfire__WorkerCount` | `docker-compose.yml` | `${HANGFIRE_WORKER_COUNT:-2}` |

Bản demo hiện chưa bật Hangfire. Hangfire dùng chung kết nối `Database__*` với backend nên không cần `HangfireConnection`. Các bước bật:

1. Backup DB theo [sao lưu database](./05-van-hanh-giam-sat-backup.md#3-sao-lưu-database).
2. Đếm đơn giữ chỗ đã quá hạn, vì lần chạy đầu job sẽ hủy hết:
   ```bash
   docker compose -f docker-compose.yml -f docker-compose.prod.yml --env-file .env.production exec postgres \
     psql -U postgres -d oisms -c "SELECT count(*) FROM reservations WHERE status = 'Active' AND expires_at <= now();"
   ```
3. Tạo schema `hangfire` bằng tài khoản `postgres` (chạy lại nhiều lần vẫn an toàn):
   ```bash
   docker compose -f docker-compose.yml -f docker-compose.prod.yml --env-file .env.production exec -T postgres \
     psql -U postgres -d oisms -v ON_ERROR_STOP=1 < docker/postgres/migrations/20261108_hangfire_schema.sql
   ```
4. Đặt `HANGFIRE_ENABLED=true` trong `.env.production`, rồi build lại và khởi động lại `backend`.
5. Kiểm tra log backend có `Starting Hangfire Server ... Schema: hangfire`, `/health/ready` trả 200, và `SELECT statename, count(*) FROM hangfire.job GROUP BY 1;` chỉ có `Succeeded` sau 1–2 phút.

Tắt lại: đặt `HANGFIRE_ENABLED=false` rồi khởi động lại `backend`; schema `hangfire` để nguyên không ảnh hưởng.

## 5. Bí Mật Và Quyền

- Backend chỉ biết mật khẩu `oisms_app`; mật khẩu `postgres` chỉ có trong container `postgres` và `.env.production`.
- Đổi mật khẩu trong `.env.production` sau khi volume đã khởi tạo **không** đổi mật khẩu role trong DB. Đổi bằng:

```bash
docker compose --env-file .env.production -f docker-compose.yml -f docker-compose.prod.yml exec postgres \
  sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "\password oisms_app"'
```

  rồi cập nhật `OISMS_APP_PASSWORD` cho khớp và tạo lại container backend.
- `Program.cs` còn secret JWT fallback viết cứng; production không được dựa vào nó, vì vậy compose production bắt buộc `JWT_SECRET`.

Bước tiếp theo: [deploy và cập nhật](./04-quy-trinh-deploy-va-cap-nhat.md).

## Liên kết Admin và POS sau khi tách 08/11/2026

Compose nhúng `VITE_POS_URL=https://<POS_DOMAIN>` vào Admin và `VITE_ADMIN_URL=https://<ADMIN_DOMAIN>` vào POS lúc build. Giữ `POS_DOMAIN`/service `pos-app`. Hai origin có phiên riêng; không truyền JWT qua URL. Sau đổi domain phải rebuild frontend.
