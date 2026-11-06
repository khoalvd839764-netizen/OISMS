# 06. Kiến Thức Nền Tảng Về Deploy

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-AWS-06` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-06` |
| **Phạm vi áp dụng** | Giải thích khái niệm đứng sau cách deploy hiện tại, dùng để ôn tập và bảo vệ đồ án |

---

## 1. Hạ Tầng AWS

| Khái niệm | Giải thích | Trong OISMS |
| :--- | :--- | :--- |
| **EC2** | Máy ảo thuê theo giờ. Loại máy (`t3.micro`, `t3.medium`...) quyết định CPU/RAM. Dòng `t3` là "burstable": có tín dụng CPU, dùng hết thì bị giới hạn. | Một máy chạy toàn bộ stack. |
| **AMI** | Ảnh hệ điều hành dùng để tạo máy. | Ubuntu 24.04 LTS. |
| **EBS** | Ổ đĩa mạng gắn vào EC2, tồn tại độc lập với máy; có snapshot. | Chứa Docker image và volume dữ liệu. |
| **Security Group** | Tường lửa trạng thái (stateful) ở mức card mạng, mặc định chặn mọi inbound. | Chỉ mở 22 (IP quản trị), 80, 443. |
| **Elastic IP** | IPv4 công khai cố định, không đổi khi Stop/Start. | DNS trỏ vào đây. |
| **Region / AZ** | Vùng địa lý / trung tâm dữ liệu trong vùng. Một máy = một AZ, không chịu được sự cố AZ. | Chưa có dự phòng. |
| **Stop vs Terminate** | Stop: tắt, giữ ổ. Terminate: xóa máy, ổ gốc có thể bị xóa theo. | Tắt máy khi không demo bằng Stop. |
| **IAM Role cho EC2** | Cấp quyền AWS cho máy mà không cần lưu access key. | Cần nếu muốn đẩy backup lên S3. |

Các dịch vụ có thể thay thế khi mở rộng: **RDS for PostgreSQL** (backup tự động, failover), **ECR** (registry image), **ECS/Fargate** hoặc **App Runner** (chạy container không quản lý máy), **ALB + ACM** (cân bằng tải, chứng chỉ miễn phí của AWS), **CloudWatch** (log, cảnh báo), **S3 + CloudFront** (phục vụ frontend tĩnh).

## 2. DNS

- Bản ghi **A** ánh xạ tên miền → IPv4; **AAAA** → IPv6; **CNAME** → tên miền khác.
- **TTL** là thời gian các resolver được cache bản ghi. Đổi IP mà TTL lớn thì phải chờ.
- Subdomain như `pos.oisms.khoadang.site` chỉ là một bản ghi A khác, trỏ cùng IP; Caddy phân biệt bằng header `Host`/SNI.
- **sslip.io / nip.io**: dịch vụ DNS wildcard, `x.203.0.113.10.sslip.io` luôn trả `203.0.113.10`; tiện khi chưa mua domain.

## 3. HTTPS, TLS Và ACME

- TLS mã hóa kết nối và chứng minh máy chủ đúng là chủ tên miền nhờ **chứng chỉ** do CA ký.
- **Let's Encrypt** là CA miễn phí, chứng chỉ 90 ngày, cấp tự động qua giao thức **ACME**. CA kiểm tra quyền sở hữu domain bằng **HTTP-01** (đặt file ở `http://<domain>/.well-known/acme-challenge/...`, cần cổng 80) hoặc **TLS-ALPN-01** (qua cổng 443).
- **Caddy** làm việc này tự động ("Automatic HTTPS"): thấy site có tên miền là xin chứng chỉ, chuyển HTTP → HTTPS, gia hạn khi còn khoảng 1/3 thời hạn. Chứng chỉ lưu ở `/data` (volume `caddy_data`), nên đừng xóa volume để tránh xin lại liên tục và chạm giới hạn tần suất.
- **SNI** cho phép nhiều domain dùng chung một IP và cổng 443; trình duyệt gửi tên miền ngay trong bước bắt tay TLS.
- **HSTS** (chưa bật) buộc trình duyệt luôn dùng HTTPS; có thể thêm bằng `header Strict-Transport-Security` trong Caddyfile.

## 4. Reverse Proxy

Reverse proxy đứng trước các dịch vụ nội bộ, nhận mọi request từ Internet rồi chuyển tiếp.

- **Một cửa vào:** chỉ Caddy mở cổng; backend, DB nằm trong mạng riêng.
- **Định tuyến theo đường dẫn:** `/api/*`, `/hubs/*`, `/health*` → backend; còn lại → frontend. Nhờ vậy frontend và API **cùng origin** (cùng scheme + host + port), trình duyệt không áp dụng CORS. Đó là lý do không phải thêm domain production vào chính sách CORS của backend (chỉ có `localhost:5173/5174`).
- **TLS termination:** Caddy giải mã HTTPS, gọi backend bằng HTTP nội bộ. `UseHttpsRedirection()` chỉ bật khi không phải Development và có cấu hình `UseHttpsRedirection=true`; sau Caddy không cần bật vì Caddy đã chuyển HTTP sang HTTPS.
- **Header chuyển tiếp:** `X-Forwarded-For`, `X-Forwarded-Proto` mang IP/scheme gốc. Backend bật `UseForwardedHeaders` và chỉ tin proxy trong dải mạng riêng (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, loopback), nên `RemoteIpAddress` là IP người dùng khi đi qua Caddy trong mạng Docker.
- **WebSocket:** SignalR nâng cấp kết nối HTTP bằng header `Upgrade: websocket`. Caddy `reverse_proxy` hỗ trợ sẵn. JWT cho hub đi qua query `access_token` vì WebSocket trình duyệt không gửi được header `Authorization`; backend chỉ đọc query này với đường dẫn `/hubs/notifications`.

## 5. Docker

- **Image** là bản đóng gói chỉ đọc gồm nhiều **layer**; **container** là tiến trình chạy từ image.
- **Multi-stage build:** backend build bằng `dotnet/sdk:8.0` rồi chỉ chép kết quả `publish` sang `dotnet/aspnet:8.0`; frontend build bằng `node:22-alpine` rồi chỉ chép `dist/` sang `nginx:alpine`. Image chạy nhỏ, không chứa SDK và mã nguồn.
- **Cache layer:** Dockerfile backend chép `*.csproj` và chạy `dotnet restore` trước khi chép mã nguồn, nên đổi code không phải tải lại NuGet. Frontend tương tự với `package*.json` → `npm install`.
- **Volume** lưu dữ liệu ngoài vòng đời container; xóa container không mất volume, `down -v` thì mất.
- **Network bridge** do Compose tạo cho phép container gọi nhau bằng tên service (`postgres`, `backend`) qua DNS nội bộ của Docker.
- **`restart: always`** tự khởi động lại container khi lỗi hoặc khi máy khởi động lại.
- **`healthcheck` + `depends_on: condition: service_healthy`:** backend chỉ chạy khi `pg_isready` thành công.
- **`.dockerignore`** loại file thừa khỏi build context để build nhanh và không lọt bí mật: file ở gốc repo (cho backend) loại `bin`, `obj`, `node_modules`, `.env*`; `pos-app` có file riêng loại `node_modules`, `dist`. `admin-dashboard` chưa có `.dockerignore`; trên EC2 clone mới không có `node_modules` nên không ảnh hưởng, nhưng build ở máy dev sẽ gửi cả `node_modules` vào context.

## 6. Docker Compose Nhiều File

- `docker compose -f a.yml -f b.yml` gộp file theo thứ tự; giá trị đơn ghi đè, map được gộp, **danh sách như `ports` mặc định được nối thêm**. Vì vậy `docker-compose.prod.yml` dùng thẻ `!reset []` để xóa hẳn cổng của file gốc (Compose v2.24+).
- `--env-file` chọn file biến dùng để thay `${VAR}` trong YAML. Cú pháp `${VAR:?thông báo}` làm Compose dừng nếu thiếu biến (dùng cho `JWT_SECRET`, `ADMIN_DOMAIN`, mật khẩu DB); `${VAR:-mặc định}` dùng giá trị mặc định.
- `profiles` cho phép tắt một service trừ khi gọi `--profile` (pgAdmin dùng profile `tools`).
- Tên project mặc định lấy theo tên thư mục; hai stack cùng tên project dùng chung container và volume.

## 7. Biến Build-time Và Runtime

- Backend .NET đọc cấu hình **lúc chạy** từ `appsettings*.json` rồi biến môi trường (biến môi trường thắng). `Database__Host` tương ứng khóa `Database:Host`.
- Vite thay `import.meta.env.VITE_*` **lúc build**; bundle JS đã chứa sẵn chuỗi URL. Vì vậy URL API là `ARG` trong Dockerfile frontend và phải build lại khi đổi domain. Mọi biến `VITE_*` đều lộ ra trình duyệt, không đặt bí mật vào đó.

## 8. Ứng Dụng Một Trang (SPA) Và PWA

- React Router xử lý đường dẫn ở trình duyệt. Khi người dùng F5 tại `/admin/inventory`, máy chủ phải trả `index.html` thay vì 404: Nginx dùng `try_files $uri $uri/ /index.html`.
- File trong `/assets/` có hash trong tên nên cache 1 năm (`immutable`); `index.html` không cache lâu để bản mới có hiệu lực ngay.
- POS là **PWA**: `manifest.webmanifest` cho phép cài như app, `sw.js` (Service Worker) cache vỏ ứng dụng. `sw.js` không cache `/api`, `/hubs`, `/health`, và tải trang theo kiểu network-first nên deploy bản mới không bị kẹt bản cũ. Service Worker chỉ chạy trên HTTPS hoặc `localhost`, nên production bắt buộc HTTPS.

## 9. Cơ Sở Dữ Liệu Trong Container

- **Khởi tạo một lần:** image PostgreSQL chạy file trong `/docker-entrypoint-initdb.d/` chỉ khi thư mục dữ liệu trống. OISMS mount `init-oisms.sh` vào đó.
- **Migration tăng dần:** hệ thống không dùng EF migrations; mỗi thay đổi là một file SQL mới trong `docker/postgres/migrations/`, chạy thủ công theo thứ tự tên, viết để chạy lại an toàn.
- **Least privilege:** `postgres` (quản trị) chỉ dùng cho DDL, migration, backup; backend dùng `oisms_app` thuộc nhóm `oisms_runtime`, không sở hữu bảng. Trigger `ledger_append_only` chặn `UPDATE`/`DELETE` sổ cái kể cả khi ứng dụng có lỗi.
- **Health probe:** `liveness` (`/health/live`) trả lời "tiến trình còn sống"; `readiness` (`/health/ready`) trả lời "sẵn sàng phục vụ" (DB, schema, quyền). Orchestrator như ECS/Kubernetes dùng hai loại này khác nhau: liveness lỗi thì khởi động lại, readiness lỗi thì ngừng gửi traffic.
- **Backup logic** (`pg_dump -Fc`) độc lập phiên bản nhỏ và khôi phục chọn lọc được; **snapshot EBS** chụp cả ổ, khôi phục nhanh nhưng phải dừng ghi hoặc chấp nhận trạng thái crash-consistent.

## 10. CI/CD

- **CI** (đang có): mỗi push/PR vào `main`, `master`, `develop` build và test trên GitHub Actions, có PostgreSQL thật trong service container.
- **CD** (chưa có): hiện deploy bằng tay qua SSH. Hướng mở rộng: GitHub Actions build image, đẩy lên ECR/GHCR, rồi SSH (hoặc AWS SSM) vào EC2 chạy `docker compose pull && up -d`; migration vẫn cần bước backup và chạy có kiểm soát.

## 11. Chi Phí Và Tối Ưu

| Khoản | Tính phí khi | Giảm chi phí |
| :--- | :--- | :--- |
| Giờ chạy EC2 | Máy ở trạng thái Running | Stop khi không demo; dùng Savings Plan/Reserved nếu chạy lâu |
| EBS | Luôn tính theo GB, kể cả khi máy Stop | Không cấp dư dung lượng; dọn snapshot cũ |
| IPv4 công khai / Elastic IP | Theo giờ | Giải phóng khi bỏ hệ thống |
| Băng thông ra Internet | Theo GB | Gzip (Caddy, Nginx), cache asset |
| Snapshot, S3 | Theo GB lưu trữ | Vòng đời xóa bản cũ |

Đặt **AWS Budgets** cảnh báo qua email để không bị bất ngờ khi quên tắt máy.

## 12. Bảng Câu Hỏi Ôn Tập Nhanh

| Câu hỏi | Trả lời ngắn |
| :--- | :--- |
| Vì sao không mở cổng 5432 ra ngoài? | DB chỉ cần cho backend trong mạng Docker; mở ra là mặt tấn công không cần thiết. |
| Vì sao không cần sửa CORS khi deploy? | Mỗi frontend gọi API qua chính domain của nó, Caddy định tuyến `/api` → cùng origin. |
| Vì sao đổi domain phải build lại frontend? | Vite nhúng `VITE_API_BASE_URL` vào bundle lúc build. |
| Rebuild có cập nhật schema không? | Không. Phải chạy migration; `init-oisms.sh` chỉ chạy khi volume trống. |
| Caddy lấy chứng chỉ thế nào? | ACME với Let's Encrypt, xác minh qua cổng 80/443, tự gia hạn. |
| Tại sao production không có tài khoản demo? | `DemoSeedPolicy` chỉ cho seed ở Development/Test, tránh tài khoản mặc định trên Internet. |
| Mất máy EC2 thì sao? | Mất cả DB nếu không có backup ngoài máy; cần pg_dump lên S3 hoặc snapshot EBS. |
