# 03. Cấu Hình Biến Môi Trường & Nginx SPA (Environment & Nginx Configuration)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-OPS-03` |
| **Phiên bản (Version)** | `v1.2.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-06` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `NFR-SEC-01`, `3.2.e`, `3.2.f` (Trang 4, 5) |
| **Phạm vi áp dụng** | `.env.example`, `.env.production.example`, `docker-compose*.yml`, `appsettings*.json`, `nginx.conf` của 2 frontend |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả các biến môi trường trọng yếu và cấu hình Nginx SPA Fallback chống lỗi 404 khi F5 trang con. | `INIT-OPS-03` |
| `v1.1.0` | `2026-11-06` | Nhóm dự án OISMS | Sửa tên khóa cấu hình theo code (`Jwt:*`, `Database:*`, `Hangfire:*`), `VITE_API_BASE_URL` không có đuôi `/api`, thêm biến production. | `OISMS-46` |
| `v1.2.0` | `2026-11-08` | Nhóm dự án OISMS | Bật Hangfire qua `HANGFIRE_ENABLED`/`HANGFIRE_WORKER_COUNT`; Hangfire dùng chung kết nối `Database:*`; schema `hangfire` qua migration `20261108_hangfire_schema.sql`. | `OISMS-46` |

---

## 1. Biến Trong `.env` (Docker Compose)

| Biến | Mặc định / ví dụ | Dùng ở đâu |
| :--- | :--- | :--- |
| `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB` | `postgres`, *(bắt buộc)*, `oisms` | Tài khoản quản trị DB, chỉ dùng cho init, migration, backup |
| `OISMS_APP_USER`, `OISMS_APP_PASSWORD` | `oisms_app`, *(bắt buộc)* | Tài khoản runtime của backend, thuộc nhóm `oisms_runtime`. Không được trùng `postgres` hay `oisms_runtime`. Ký tự `$` trong `.env` viết thành `$$`. |
| `POSTGRES_PORT`, `REDIS_PORT`, `PGADMIN_PORT`, `BACKEND_PORT`, `ADMIN_PORT`, `POS_PORT` | `5432`, `6379`, `5050`, `5000`, `5173`, `5174` | Cổng mở ra host khi chạy local |
| `PGADMIN_EMAIL`, `PGADMIN_PASSWORD` | `admin@oisms.com`, *(bắt buộc)* | pgAdmin. Compose bắt buộc có mật khẩu kể cả khi production không chạy pgAdmin |
| `ASPNETCORE_ENVIRONMENT` | `Development` | Môi trường backend. Development mới có Swagger |

`.env.example` còn có `JWT_ISSUER`, `JWT_AUDIENCE`, `JWT_EXPIRY_MINUTES`, `VITE_API_BASE_URL`, `BACKEND_HTTPS_PORT` nhưng `docker-compose.yml` **không** chuyển các biến này vào container. Chỉ `JWT_SECRET` được dùng, và chỉ trong `docker-compose.prod.yml`.

## 2. Khóa Cấu Hình Backend (.NET)

Đặt trong `appsettings*.json` hoặc biến môi trường (dấu `:` thay bằng `__`).

| Khóa | Mặc định | Ý nghĩa |
| :--- | :--- | :--- |
| `Database:Host`, `Database:Port`, `Database:Database`, `Database:Username`, `Database:Password` | `localhost`, `5432`, `oisms`, `oisms_app` | Kết nối runtime. Compose truyền qua `Database__*` để an toàn với ký tự đặc biệt. |
| `ConnectionStrings:DefaultConnection` | — | Cách cấu hình dự phòng bằng một chuỗi kết nối |
| `Database:SeedDemoData`, `Database:DemoSeedPassword` | `false` | Seed dữ liệu demo, chỉ có hiệu lực ở `Development`/`Test` |
| `Jwt:Secret` | fallback viết cứng trong `Program.cs` | Khóa ký HMAC-SHA256, tối thiểu 32 ký tự. **Môi trường thật phải đặt**; production compose truyền `Jwt__Secret` từ `JWT_SECRET`. |
| `Jwt:Issuer`, `Jwt:Audience` | `OISM_WebAPI`, `OISM_Client` | Kiểm tra issuer/audience của token |
| `Jwt:ExpiryMinutes` | `60` | Thời hạn access token (`NFR-SEC-01`) |
| `Hangfire:Enabled` | `false` | Bật job hết hạn giữ chỗ và sinh kỳ kiểm kê. Compose truyền từ `HANGFIRE_ENABLED` trong `.env` (mặc định `false`). Khi bật cần schema `hangfire`: volume mới đã có qua `init-oisms.sh`; DB đã có dữ liệu phải chạy `docker/postgres/migrations/20261108_hangfire_schema.sql` bằng tài khoản `postgres` trước. |
| `Hangfire:WorkerCount` | `min(4, max(2, số CPU))`; Compose: `2` | Số worker Hangfire. Compose truyền từ `HANGFIRE_WORKER_COUNT`. |
| `ConnectionStrings:HangfireConnection` | — | Không bắt buộc. Không đặt thì Hangfire dùng cùng kết nối với `AppDbContext` (`Database:*` trước, rồi `DefaultConnection`). |

CORS chỉ cho phép `http(s)://localhost:5173` và `http(s)://localhost:5174`. Trên production mỗi frontend gọi API qua chính domain của nó (Caddy định tuyến `/api`, `/hubs`, `/health`) nên không cần thêm origin.

## 3. Biến Frontend Lúc Build

| Biến | Giá trị | Ghi chú |
| :--- | :--- | :--- |
| `VITE_API_BASE_URL` | Trống → `http://localhost:5000` | **Không** thêm đuôi `/api`; code tự nối `/api/...` và `/hubs/notifications`. Vite nhúng giá trị lúc build, đổi giá trị phải build lại image. |

## 4. Biến Production (`.env.production`)

Mẫu ở `.env.production.example`: `ADMIN_DOMAIN`, `POS_DOMAIN`, `POSTGRES_*`, `OISMS_APP_*`, `JWT_SECRET`, `PGADMIN_PASSWORD`. Tạo giá trị ngẫu nhiên bằng `openssl rand -hex 32`. Không commit `.env`, `.env.production`. Chi tiết: [`13-deploy-aws/03-cau-hinh-moi-truong-production.md`](../13-deploy-aws/03-cau-hinh-moi-truong-production.md).

## 5. Nginx Trong Image Frontend

Mỗi image frontend chạy `nginx:alpine` với `nginx.conf` riêng:

- `/assets/` và file tĩnh theo đuôi (`js`, `css`, ảnh, font): trả `404` nếu thiếu, không rơi về `index.html`; cache dài (`immutable` cho `/assets/`).
- Mọi route còn lại (`/login`, `/admin/...`, `/pos`, `/403`...) dùng SPA fallback:

```nginx
location / {
    try_files $uri $uri/ /index.html;
}
```

- Bật gzip cho file từ 10 KB. Nginx này chỉ phục vụ file tĩnh; HTTPS và reverse proxy do Caddy đảm nhận trên production.

## 6. Bật Trợ lý OISMS cho demo

> **Lưu ý kinh phí:** nhóm không có kinh phí cho API AI, nên chỉ dùng key gói miễn phí của Groq và Gemini với hạn mức rất thấp (Gemini Free Tier khoảng 20 request/ngày). Tính năng AI vì vậy bị giới hạn số câu hỏi mỗi ngày. Chi tiết: [FR-13](../02-functional-requirements/fr-13-ai-assistant.md#lưu-ý-giới-hạn-hạn-mức-api-ai).

AI mặc định tắt. Để bật trên Docker Compose, đặt các biến sau trong môi trường của tiến trình Compose hoặc cấu hình backend demo.

| Biến Compose | Khóa .NET | Giá trị demo |
| :--- | :--- | :--- |
| `AI_ENABLED` | `Ai__Enabled` | `true` để bật |
| `AI_BASE_URL` | `Ai__BaseUrl` | `https://api.groq.com/openai/v1` |
| `AI_MODEL` | `Ai__Model` | `openai/gpt-oss-120b` |
| `AI_API_KEY` | `Ai__ApiKey` | Key Groq từ biến môi trường backend |
| `AI_TIMEOUT_SECONDS` | `Ai__TimeoutSeconds` | `30` |
| `AI_MAX_TOKENS` | `Ai__MaxTokens` | `1024` |
| `AI_DEFAULT_PROVIDER` | `Ai__DefaultProvider` | `groq`: model chọn sẵn trong khung chat |
| `AI_GEMINI_MODEL` | `Ai__Providers__gemini__Model` | `gemini-3.5-flash` |
| `AI_GEMINI_API_KEY` | `Ai__Providers__gemini__ApiKey` | Key Google AI Studio. Để trống thì Gemini không hiện trong danh sách chọn |
| `AI_GEMINI_TIMEOUT_SECONDS` | `Ai__Providers__gemini__TimeoutSeconds` | `60` (Gemini suy luận lâu hơn Groq) |

Key Gemini ở gói Free Tier của AI Studio chỉ có khoảng 20 request/ngày cho mỗi model (đo 10/11/2026). Muốn dùng credit của gói Google AI Pro thì phải gắn tài khoản thanh toán Google Cloud có credit vào project tạo key.

Sau khi đặt biến, chạy `docker compose up -d --build backend` để tạo lại backend. Khi chạy .NET trực tiếp, dùng cột khóa .NET làm tên biến môi trường. Nếu bật mà thiếu key, backend vẫn khởi động, ghi cảnh báo và chat trả `AI_DISABLED`.

Giữ key trong môi trường backend hoặc secret do hệ thống triển khai cung cấp. Các file mẫu để trống key. Dùng dữ liệu giả cho demo Groq.

Bộ eval 20 câu là lệnh thủ công tách khỏi unit và integration. Khi có `Ai__ApiKey` trong môi trường và muốn gọi nhà cung cấp thật, chạy `AI_LIVE_EVAL=1 dotnet run --project test-oisms/ai-eval/Oism.AiEval.csproj` từ root repo. Kết quả ghi tỷ lệ đúng tool và đúng ngày. Bộ eval chỉ gửi câu hỏi mẫu, prompt và schema tool, rồi chấm lượt chọn công cụ đầu tiên.
