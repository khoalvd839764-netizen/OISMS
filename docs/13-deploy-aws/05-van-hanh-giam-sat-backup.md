# 05. Vận Hành, Giám Sát Và Sao Lưu

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-AWS-05` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-06` |
| **Phạm vi áp dụng** | Máy EC2 đang chạy bản demo |

---

Các lệnh dùng alias `dcp` như [tài liệu 04](./04-quy-trinh-deploy-va-cap-nhat.md):

```bash
alias dcp='docker compose --env-file .env.production -f docker-compose.yml -f docker-compose.prod.yml'
```

## 1. Trạng Thái Và Log

```bash
dcp ps                          # trạng thái container, postgres phải "healthy"
dcp logs --tail 200 backend     # lỗi API, lỗi kết nối DB
dcp logs -f caddy               # cấp/gia hạn chứng chỉ, lỗi proxy 502
dcp logs --tail 100 postgres
docker stats --no-stream        # RAM/CPU từng container
df -h /                         # dung lượng ổ
```

Log container dùng driver `json-file` mặc định và không giới hạn kích thước. Máy chạy lâu nên dọn định kỳ (`docker system prune` cho image cũ, **không** thêm `--volumes`) hoặc cấu hình `log-opts` trong `/etc/docker/daemon.json`.

## 2. Health Check

| Endpoint | Ý nghĩa | Lỗi thường gặp |
| :--- | :--- | :--- |
| `/health/live` | Tiến trình backend còn trả lời | Không trả lời → backend chết hoặc Caddy không tới được |
| `/health/ready` (và `/health`) | Kết nối bằng `oisms_app`, đủ 43 bảng, trigger `ledger_append_only` bật, user không có quyền quản trị | 503 `schema is incomplete` → thiếu migration; 503 `Security alert` → user runtime có quyền quá cao; 503 `timed out` → PostgreSQL không phản hồi |

Có thể gắn một dịch vụ kiểm tra uptime bên ngoài (UptimeRobot, Route 53 health check, CloudWatch Synthetics) gọi `/health/ready` mỗi vài phút.

## 3. Sao Lưu Database

Dữ liệu chỉ nằm trên volume `postgres_data` của EC2. Không có backup tự động sẵn.

**Backup thủ công:**

```bash
STAMP=$(date +%Y%m%d-%H%M%S)
mkdir -p ~/backups
dcp exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc -f /tmp/oisms.dump'
dcp cp postgres:/tmp/oisms.dump ~/backups/oisms-$STAMP.dump
dcp exec -T postgres pg_restore --list /tmp/oisms.dump > /dev/null && echo "Archive đọc được"
```

**Backup hằng ngày bằng cron** (ví dụ 2:00 sáng, giữ 14 bản):

```bash
crontab -e
```

```cron
0 2 * * * cd ~/OISMS && docker compose --env-file .env.production -f docker-compose.yml -f docker-compose.prod.yml exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > ~/backups/oisms-$(date +\%Y\%m\%d).dump && ls -1t ~/backups/oisms-*.dump | tail -n +15 | xargs -r rm --
```

**Đưa bản backup ra khỏi máy:** bản dump nằm cùng ổ EBS với dữ liệu, hỏng ổ là mất cả hai. Nên chép sang S3 (`aws s3 cp`, cần IAM role cho EC2 có quyền ghi một bucket riêng) hoặc bật **EBS Snapshot** theo lịch bằng Amazon Data Lifecycle Manager.

File dump chứa dữ liệu nghiệp vụ và hash mật khẩu: không commit, không để public.

**Khôi phục:** xem [tài liệu 04, mục Rollback](./04-quy-trinh-deploy-va-cap-nhat.md#4-rollback). Nên diễn tập khôi phục vào một máy/volume khác; `pg_restore --list` chỉ chứng minh file đọc được.

## 4. Tắt Và Bật Máy Để Tiết Kiệm Chi Phí

- **Stop** instance (không phải Terminate): ổ EBS và Elastic IP giữ nguyên, không tính tiền giờ máy, vẫn tính tiền ổ đĩa và IPv4 công khai.
- Bật lại: các container có `restart: always` nên tự chạy khi Docker khởi động. Kiểm tra `dcp ps` và `/health/ready`.
- Caddy gia hạn chứng chỉ khi đang chạy. Máy tắt lâu (gần 90 ngày) thì lần bật đầu Caddy sẽ xin chứng chỉ mới, cần cổng 80/443 mở và DNS đúng.

## 5. Bảo Mật Vận Hành

- SSH chỉ mở cho IP quản trị; tắt đăng nhập mật khẩu (mặc định Ubuntu trên EC2 đã dùng key).
- Cập nhật hệ điều hành định kỳ: `sudo apt-get update && sudo apt-get upgrade -y`, khởi động lại khi kernel đổi.
- Cập nhật image nền: `dcp pull postgres redis caddy` rồi `dcp up -d`. Đổi phiên bản lớn của PostgreSQL (16 → 17) cần `pg_dump`/`pg_restore`, không chỉ đổi tag image.
- Không in hay chia sẻ `.env.production`, file dump.
- Muốn xem DB bằng pgAdmin thì dùng SSH tunnel tới máy local, không mở cổng 5432/5050 trên Security Group.

## 6. Xử Lý Sự Cố

| Hiện tượng | Nguyên nhân thường gặp | Cách xử lý |
| :--- | :--- | :--- |
| Trình duyệt báo lỗi chứng chỉ, Caddy log `challenge failed` | DNS chưa trỏ đúng, cổng 80 bị chặn, proxy DNS bật | Kiểm tra `dig +short <domain>`, Security Group, tắt proxy DNS; `dcp restart caddy` |
| HTTP 502 từ Caddy | Container backend/frontend đang khởi động hoặc đã dừng | `dcp ps`, `dcp logs backend` |
| Admin báo "Không thể tải dữ liệu từ máy chủ", `/health/ready` 503 `schema is incomplete` | Code mới cần migration chưa chạy | [Chạy migration](./04-quy-trinh-deploy-va-cap-nhat.md#33-có-migration) |
| Frontend gọi `http://localhost:5000` | Image frontend build không có `VITE_API_BASE_URL` (quên `-f docker-compose.prod.yml`) | Build lại bằng lệnh production đầy đủ |
| Gọi API ra `/api/api/...` 404 | `VITE_API_BASE_URL` có đuôi `/api` | Bỏ đuôi `/api`, build lại |
| Compose báo `JWT_SECRET must be set` hoặc `ADMIN_DOMAIN must be set` | Thiếu biến hoặc quên `--env-file .env.production` | Thêm biến / thêm cờ |
| Compose báo lỗi ở `!reset` | Docker Compose cũ hơn v2.24 | Cập nhật Docker |
| Build bị kill, `exit code 137` | Hết RAM | Thêm swap hoặc tăng loại máy |
| Đăng nhập xong bị đá ra liên tục | Đổi `JWT_SECRET` làm token cũ vô hiệu, hoặc giờ hệ thống lệch (`ClockSkew = 0`) | Đăng nhập lại; kiểm tra `timedatectl` đồng bộ NTP |
| POS không nhận cập nhật realtime | WebSocket bị chặn ở mạng khách, token hết hạn | Kiểm tra tab Network `/hubs/notifications`; SignalR tự fallback long polling |
| `postgres` không healthy sau khi đổi mật khẩu trong `.env.production` | Mật khẩu role trong DB không đổi theo | Đổi bằng `ALTER ROLE`/`\password` (xem [tài liệu 03](./03-cau-hinh-moi-truong-production.md#5-bí-mật-và-quyền)) |
| Ổ đầy | Image cũ, log, backup | `docker image prune -a`, dọn log, chuyển backup lên S3 |
