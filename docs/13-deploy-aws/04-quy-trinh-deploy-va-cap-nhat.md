# 04. Quy Trình Deploy Và Cập Nhật Phiên Bản

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-AWS-04` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-06` |
| **Phạm vi áp dụng** | Deploy lần đầu, cập nhật code, migration database, rollback |

---

Mọi lệnh chạy trên EC2 tại thư mục repo (`~/OISMS`). Để gõ ngắn, đặt alias trong phiên SSH:

```bash
alias dcp='docker compose --env-file .env.production -f docker-compose.yml -f docker-compose.prod.yml'
```

Các ví dụ dưới đây dùng `dcp`. Không có alias thì thay bằng lệnh đầy đủ.

## 1. Deploy Lần Đầu

Điều kiện: đã làm xong [hạ tầng](./02-chuan-bi-ha-tang-aws.md) và [`.env.production`](./03-cau-hinh-moi-truong-production.md); DNS đã trỏ đúng Elastic IP.

```bash
dcp config --quiet && echo OK
dcp up -d --build
dcp ps
```

Chuyện gì xảy ra:

1. Build 3 image: backend (.NET SDK → ASP.NET runtime), admin và POS (Node 22 → nginx). Lần đầu mất vài phút.
2. `postgres` khởi động với volume trống, chạy `init-oisms.sh`: nạp `oisms_schema.sql` (43 bảng, trigger `ledger_append_only`), tạo nhóm `oisms_runtime` và login `oisms_app`, kiểm tra đủ 43 bảng. Lỗi ở bước này thì container dừng; xem `dcp logs postgres`.
3. `backend` chờ `postgres` healthy rồi chạy. Production không seed demo.
4. `caddy` xin chứng chỉ cho `ADMIN_DOMAIN` và `POS_DOMAIN`. Xem tiến trình: `dcp logs -f caddy`.

Kiểm tra:

```bash
curl -s https://<ADMIN_DOMAIN>/health/ready
curl -sI https://<POS_DOMAIN>/ | head -1
```

`/health/ready` phải trả HTTP 200, `status: "Healthy"`, `tablesCount: 43`, `triggerVerified: true`.

## 2. Tạo Dữ Liệu Ban Đầu

Database production trống, không có tài khoản mẫu.

1. Mở `https://<ADMIN_DOMAIN>/login`, chọn **Đăng ký doanh nghiệp** (gọi `POST /api/auth/register-tenant`). Một transaction tạo tenant và tài khoản `Owner`; chưa có chi nhánh hay kho.
2. Đăng nhập bằng Owner, tạo chi nhánh, kho, nhân viên (`Staff`, `Cashier`) ở màn Tổ chức.
3. Khai báo nhà cung cấp, sản phẩm, đơn vị tính, giá; nhập kho qua Phiếu mua → Phiếu nhập để có tồn và giá vốn.
4. Thu ngân đăng nhập POS tại `https://<POS_DOMAIN>` hoặc `https://<ADMIN_DOMAIN>/pos`.

## 3. Cập Nhật Phiên Bản Mới

### 3.1. Trước khi cập nhật

- Code cần deploy đã merge và CI xanh (CI không tự deploy).
- Xem có migration mới không:

```bash
git fetch origin
git diff --name-only HEAD origin/main -- docker/postgres/migrations/ docs/database/oisms_schema.sql
```

- Ghi lại commit đang chạy để rollback: `git rev-parse --short HEAD`.

### 3.2. Không có migration

```bash
git pull --ff-only
dcp up -d --build
dcp ps
curl -s https://<ADMIN_DOMAIN>/health/ready
```

`up -d --build` chỉ tạo lại container có image thay đổi. Trong lúc container backend khởi động lại sẽ có vài giây lỗi 502 từ Caddy.

### 3.3. Có migration

Backend không tự nâng cấp schema, và `init-oisms.sh` không chạy lại trên volume đã có dữ liệu. Nếu chỉ rebuild, `/health/ready` sẽ trả 503 `Database schema is incomplete`.

```bash
git pull --ff-only
dcp build backend admin-dashboard pos-app      # build trước để lỗi build lộ ra sớm
dcp stop backend                                # ngừng ghi vào DB

# 1. Backup
STAMP=$(date +%Y%m%d-%H%M%S)
mkdir -p ~/backups
dcp exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc -f /tmp/oisms.dump'
dcp cp postgres:/tmp/oisms.dump ~/backups/oisms-$STAMP.dump
test -s ~/backups/oisms-$STAMP.dump || echo "BACKUP RỖNG - DỪNG LẠI"

# 2. Chạy migration theo thứ tự tên file
for f in docker/postgres/migrations/*.sql; do
  dcp cp "$f" postgres:/tmp/migration.sql
  dcp exec -T postgres sh -c 'psql -X -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -q -f /tmp/migration.sql' \
    || { echo "LỖI: $f"; break; }
done

# 3. Khởi động lại
dcp up -d
curl -s https://<ADMIN_DOMAIN>/health/ready
```

- Các file migration được viết để chạy lại an toàn (`IF NOT EXISTS`, kiểm tra trước khi đổi), nên chạy cả thư mục theo thứ tự là cách đơn giản nhất. Mỗi file nằm trong transaction; file lỗi tự hoàn tác phần của nó.
- Gặp `LỖI` thì **không** khởi động backend. Đọc lỗi, sửa dữ liệu hoặc khôi phục backup ([mục 4](#4-rollback)).
- Không chạy `oisms_schema.sql` lên DB đã có dữ liệu.

### 3.4. Chỉ đổi domain hoặc URL API

Frontend nhúng `VITE_API_BASE_URL` lúc build:

```bash
nano .env.production           # sửa ADMIN_DOMAIN / POS_DOMAIN
dcp up -d --build admin-dashboard pos-app caddy
```

Cập nhật DNS trước, nếu không Caddy không xin được chứng chỉ cho tên mới.

## 4. Rollback

**Chỉ code, schema không đổi:**

```bash
git checkout <commit cũ>
dcp up -d --build
```

**Đã chạy migration:** migration chỉ thêm cột/bảng/ràng buộc, code cũ thường vẫn chạy được trên schema mới. Nếu không chạy được thì khôi phục backup:

```bash
dcp stop backend
dcp cp ~/backups/oisms-<STAMP>.dump postgres:/tmp/oisms.dump
dcp exec -T postgres sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists /tmp/oisms.dump'
git checkout <commit cũ>
dcp up -d --build
```

`pg_restore --clean` xóa dữ liệu hiện tại của database trước khi nạp lại; dữ liệu phát sinh sau thời điểm backup sẽ mất.

## 5. Kiểm Tra Sau Deploy (Smoke Test)

| Kiểm tra | Kỳ vọng |
| :--- | :--- |
| `https://<ADMIN_DOMAIN>/health/ready` | 200, `Healthy`, 43 bảng |
| `https://<ADMIN_DOMAIN>/swagger` | Không mở được (production không có Swagger; SPA trả trang chuyển về `/admin`) |
| `http://<ADMIN_DOMAIN>` | Chuyển hướng sang `https://` |
| Đăng nhập Admin, mở Sản phẩm, Tồn kho | Có dữ liệu, không lỗi mạng |
| F5 tại `/admin/inventory` | Vẫn vào được trang (SPA fallback) |
| POS: tìm sản phẩm, thanh toán tiền mặt | Có hóa đơn `INV-...`, tồn giảm; tab Admin khác nhận cập nhật realtime |
| `nc -vz <Elastic IP> 5432` từ máy ngoài | Bị từ chối/timeout |
