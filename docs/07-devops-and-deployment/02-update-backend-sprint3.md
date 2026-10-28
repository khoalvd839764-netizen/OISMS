# Cập nhật backend Docker từ Sprint 2 lên Sprint 3

Áp dụng cho Docker Compose trong repo OISMS, database v0.5 đã có dữ liệu. File SQL đóng gói từ `DatabaseMigrationService.UpgradeDatabaseSchemaAsync` tại `bfc83c0`: [20261027_sprint3.sql](../../docker/postgres/migrations/20261027_sprint3.sql). Đây là migration tăng dần, không phải file cài mới database.

Mở Docker Desktop, đợi engine chạy rồi mở PowerShell tại `D:\OISMS`. Giữ nguyên `.env` đang dùng và đúng nhánh chứa backend muốn triển khai. Khi một bước báo lỗi, dừng và đọc lỗi; không tiếp tục sang bước sau. Các lệnh không bật Hangfire hoặc thay đổi credential.

## Chuẩn bị

Chạy khối sau trong cùng một cửa sổ PowerShell. Helper dừng ngay khi lệnh Docker trả lỗi; dùng được với cả Windows PowerShell 5.1 và PowerShell 7.

```powershell
Set-Location D:\OISMS
$ErrorActionPreference = 'Stop'
function Invoke-OismDocker {
    & docker @args
    if ($LASTEXITCODE -ne 0) {
        throw "Docker command failed (exit $LASTEXITCODE). Stop here."
    }
}

Invoke-OismDocker version
Invoke-OismDocker compose up -d postgres redis

# Build trước khi dừng backend để phát hiện lỗi build sớm.
Invoke-OismDocker compose build backend

# Ngừng các tiến trình/API khác cùng ghi vào database này nếu có.
Invoke-OismDocker compose stop backend
```

## 1. Sao lưu database ra máy host

Tài khoản và tên database lấy từ biến môi trường trong container PostgreSQL; không cần chép mật khẩu vào lệnh. File dump được tạo trong container rồi copy ra host, tránh chuyển binary qua dấu `>` của Windows PowerShell.

```powershell
$upgradeStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$upgradeBackupDir = Join-Path (Get-Location) 'tmp\backups'
New-Item -ItemType Directory -Path $upgradeBackupDir -Force | Out-Null
$upgradeBackupFile = Join-Path $upgradeBackupDir "oisms-before-sprint3-$upgradeStamp.dump"

Invoke-OismDocker compose exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc -f /tmp/oisms-before-sprint3.dump'
Invoke-OismDocker compose exec -T postgres pg_restore --list /tmp/oisms-before-sprint3.dump | Out-Null
Invoke-OismDocker compose cp postgres:/tmp/oisms-before-sprint3.dump $upgradeBackupFile
if ((Get-Item -LiteralPath $upgradeBackupFile).Length -eq 0) {
    throw 'Backup file is empty. Do not migrate.'
}
Write-Host "Backup saved: $upgradeBackupFile"
```

`tmp/` đã được gitignore. Bản dump có dữ liệu nghiệp vụ: giữ trên máy/ổ sao lưu phù hợp, không commit. `pg_restore --list` kiểm tra archive đọc được, không thay thế diễn tập restore đầy đủ.

## 2. Chạy migration Sprint 3

```powershell
Invoke-OismDocker compose cp ./docker/postgres/migrations/20261027_sprint3.sql postgres:/tmp/oisms-sprint3.sql
Invoke-OismDocker compose exec -T postgres sh -c 'psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -f /tmp/oisms-sprint3.sql'
```

Thành công phải có `COMMIT` và không có `ERROR`. Script thêm trường idempotency, liên kết kiểm kê–điều chỉnh, chữ ký webhook và bảng `shop_integrations` cùng các constraint/quyền runtime tương ứng. Script không tự tạo shop hoặc secret.

SQL chạy trong một transaction, dừng ngay khi lỗi. Khi `psql` thoát trước COMMIT, các thay đổi của transaction được rollback; backend vẫn dừng để người vận hành kiểm tra. Dữ liệu cũ vi phạm constraint, trùng secret hoặc lock quá lâu sẽ làm bước này thất bại. Không bỏ qua lỗi hoặc chạy tiếp backend. Không chạy lại file cài mới `oisms_schema.sql` trên database cũ; không dùng `docker compose down -v`.

## 3. Khởi động backend đã build

```powershell
Invoke-OismDocker compose up -d --no-deps backend
Invoke-OismDocker compose ps
Invoke-OismDocker compose logs --tail 100 backend
```

Frontend hiện có tiếp tục dùng được nếu không thay đổi code frontend. Nếu frontend chưa được khởi động:

```powershell
Invoke-OismDocker compose up -d admin-dashboard pos-app
```

## 4. Kiểm tra API

Với cổng mặc định `BACKEND_PORT=5000`:

```powershell
Invoke-RestMethod http://localhost:5000/health/ready
```

Kỳ vọng HTTP 200, trạng thái `Healthy`. Nếu container vừa khởi động chưa nghe cổng, đợi backend báo `Now listening` trong log rồi thử lại. Khi `.env` đổi `BACKEND_PORT`, thay cổng tương ứng. Readiness không thay cho kiểm thử nghiệp vụ các trường mới.

Mở [Swagger](http://localhost:5000/swagger) trong môi trường Development. Một smoke test không ghi dữ liệu cho route webhook Sprint 3:

```powershell
try {
    Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/webhooks/simulator/Shopee -ContentType 'application/json' -Body '{}'
    throw 'Expected HTTP 400 for invalid payload.'
}
catch {
    if ($null -eq $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 400) {
        throw
    }
    Write-Host 'PASS: webhook route rejects invalid input with HTTP 400.'
}
```

Ca này chỉ chứng minh route/validation hoạt động, không chứng minh đủ luồng tạo đơn/giữ chỗ. Kiểm thử đơn hợp lệ cần shop integration, credential, tenant, SKU và tồn kho hợp lệ trên dữ liệu kiểm thử. T02–T06 của BE2 vẫn chưa được nghiệm thu tại baseline này; không kỳ vọng mọi API đọc/report đã hoàn thiện.

## Hangfire

Cấu hình hiện tại mặc định `Hangfire:Enabled=false`. Các lệnh trên không bật tự động hết hạn giữ chỗ. Muốn bật phải bổ sung cấu hình vào service backend của Compose, cung cấp kết nối storage và quyền cho schema `hangfire` theo [SQL provisioning](../database/hangfire_schema_provisioning.sql); backend runtime vẫn dùng tài khoản ít quyền. Chỉ thêm biến vào `.env` chưa đủ nếu Compose không chuyển biến đó vào container.

## Giới hạn kiểm chứng

Ngày 2026-10-27: `docker compose config --quiet` PASS; cả 7 khối PowerShell parse PASS; hai phần SQL khớp nguyên nội dung migration C# tại baseline đã ghi. Docker Desktop chưa chạy, PostgreSQL localhost:5432 cũng từ chối kết nối. Probe PostgreSQL dừng ở bước kết nối fixture trước khi thực thi SQL, nên chưa xác minh thực thi file SQL trong lượt này. Các lệnh Docker backup/build/redeploy chưa được chạy end-to-end; không có thay đổi nào được thực hiện lên database nghiệp vụ. Source probe/log ở `tmp/codex-s3-docker-upgrade/` (ignored).
