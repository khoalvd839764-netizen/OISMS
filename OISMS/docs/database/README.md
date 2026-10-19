# OISMS — Bàn giao database v0.5

## Bàn giao kết nối Docker–database — 16/10/2026

- PostgreSQL đã chạy healthy trong Docker; backend kết nối thành công bằng tài khoản runtime `oisms_app` qua `postgres:5432`.
- Endpoint `http://localhost:5000/health/ready` trả **200 Healthy**, xác nhận đủ **29 bảng**, không thiếu bảng trong danh sách kiểm tra và trigger bảo vệ ledger đang bật.
- Khi phát triển local, công cụ quản lý database kết nối `localhost:5432` (hoặc cổng host đã cấu hình); thông tin đăng nhập lấy từ `.env`, không đưa mật khẩu vào Git. Không copy đè `.env.example` lên `.env` mỗi lần khởi động.
- Có thể bàn giao nền tảng kết nối để Khoa/TV2 triển khai Entity/DbContext, tenant filter và API. Kết quả readiness không thay kiểm thử nghiệp vụ WAC/nhập/xuất/giữ chỗ; việc giữ dữ liệu sau restart vẫn cần xác minh bằng bản ghi thử.

Kiểm tra lại kết nối: `Invoke-RestMethod http://localhost:5000/health/ready`.

Tài liệu chuẩn chung cho Khoa (Backend 1), TV2 (Backend 2) và các thành viên tích hợp. PostgreSQL 16+, **29 bảng nghiệp vụ, 94 khóa ngoại, 86 CHECK trên bảng**, cùng UNIQUE/index và trigger. Việc gom file không thay đổi mô hình nghiệp vụ v0.5.

## Bộ file bàn giao

```text
docs/database/
├── oisms_schema.sql   # Tạo database, bảng, ràng buộc, trigger và group quyền backend
└── README.md          # Thiết kế, quy ước, tích hợp, từ điển dữ liệu và cách vận hành
```

Chỉ cần hai file này để hiểu và cài database mới. File SQL không chứa mật khẩu, không tạo LOGIN backend, không thêm dữ liệu demo và không ghi đè database cũ.

Script database duy nhất được giữ trong `scripts` là `scripts/test-database-schema.cjs`. Script đọc trực tiếp SQL và README tại đây, tạo dữ liệu kiểm thử trong PostgreSQL nhúng rồi đóng phiên; không cần generator, seed hay file SQL phụ. SQL bàn giao là nguồn cấu trúc hiện hành.

## Cài mới bằng một file SQL

Điều kiện: PostgreSQL **16 trở lên**, client `psql`, tài khoản quản trị/migration có CREATEDB và CREATEROLE cùng quyền tạo schema objects. Database tên `oisms` chưa tồn tại. Chạy ở môi trường riêng của dự án, không có tiến trình cùng tạo database/role.

```powershell
psql -h localhost -p 5432 -U postgres -d postgres -W -f "D:\CNPM workbase\docs\database\oisms_schema.sql"
```

Thay host/port/user theo môi trường. `-W` nhập mật khẩu qua prompt; không đặt mật khẩu trong SQL/Git. Script tạo `oisms`, dùng lệnh `\connect` của psql để chuyển kết nối, rồi cài bảng/trigger/quyền trong một transaction.

**Phải chạy bằng psql**, không dán nguyên file vào pgAdmin Query Tool hoặc dbdiagram vì `\connect` không phải cú pháp SQL của server. Để đổi tên database, sửa đồng thời `CREATE DATABASE oisms` và `\connect oisms` trong file SQL bàn giao rồi giữ nhất quán với connection string.

- Nếu `oisms` đã tồn tại, script dừng; không DROP, không xóa dữ liệu, không tự bỏ qua bảng cũ.
- CREATE DATABASE không nằm trong transaction PostgreSQL. Nếu phần schema/quyền lỗi thì phần này rollback, nhưng database vừa tạo có thể còn tồn tại và trống. Kiểm tra lỗi; không tự DROP database chưa xác minh.
- Khi đã triển khai, dùng migration có review/backup thay vì chạy lại file tạo mới.

### Kiểm tra sau cài

Kết nối tới `oisms`:

```sql
SELECT count(*) FROM information_schema.tables
WHERE table_schema='public' AND table_type='BASE TABLE'; -- 29
SELECT count(*) FROM pg_constraint WHERE contype='f'; -- 94 trong DB dự án mới
SELECT rolname,rolcanlogin,rolsuper FROM pg_roles WHERE rolname='oisms_runtime';
SELECT tgrelid::regclass AS table_name,tgname
FROM pg_trigger WHERE NOT tgisinternal ORDER BY 1,2;
```

### Tài khoản backend

SQL cài mới đã bao gồm quyền cho group `oisms_runtime` (NOLOGIN). Không cần chạy thêm script quyền. DBA tạo LOGIN riêng bằng công cụ quản lý bí mật, rồi cấp group:

```sql
-- oisms_app là LOGIN do DBA tạo riêng, không phải tài khoản có sẵn trong file cài.
GRANT oisms_runtime TO oisms_app;
```

LOGIN backend phải NOSUPERUSER, NOCREATEDB, NOCREATEROLE, NOREPLICATION, NOBYPASSRLS; không sở hữu database/schema/bảng hoặc thừa hưởng role migration. Không dùng `postgres` chạy API. Nếu `oisms_runtime` đã tồn tại trong cluster, DBA kiểm tra thuộc tính/membership cũ; script không tự thu hồi các membership ngoài dự án.

Owner/Staff/Cashier trong Users là **quyền ứng dụng**, không phải role PostgreSQL. Backend kiểm tra JWT/RBAC. Chưa có RLS: quyền SELECT của runtime group không tự lọc tenant, vì vậy Global Query Filters và write guards vẫn bắt buộc.

## Tích hợp Docker Compose

Cơ sở dữ liệu PostgreSQL 16 đã được tích hợp vào `docker-compose.yml` của dự án với volume lưu trữ bền vững, healthcheck và cơ chế khởi tạo tự động.

### Khởi động môi trường Docker

Khởi chạy PostgreSQL và toàn bộ các dịch vụ hệ thống:

```powershell
# Khởi chạy toàn bộ hệ thống (PostgreSQL, Redis, PgAdmin, Backend, Frontend...)
docker compose up -d

# Hoặc chỉ khởi chạy PostgreSQL
docker compose up -d postgres
```

Cấu hình container PostgreSQL (`oism_postgres`):
- **Image**: `postgres:16-alpine`
- **Port mapping**: `5432:5432`
- **Volume lưu dữ liệu**: `postgres_data:/var/lib/postgresql/data` (dữ liệu bền vững, không mất khi container restart/down).
- **Healthcheck**: Sử dụng `pg_isready -U ${POSTGRES_USER:-postgres} -d ${POSTGRES_DB:-oisms}` với khoảng kiểm tra 10s.
- **Phụ thuộc khởi động**: Service backend và pgadmin được cấu hình `depends_on: { postgres: { condition: service_healthy } }` để đảm bảo cơ sở dữ liệu đã sẵn sàng nhận kết nối trước khi các dịch vụ khác khởi động.

### Khởi tạo lần đầu (First-time Initialization)

Cơ chế khởi tạo tự động hoạt động dựa trên cơ chế entrypoint `/docker-entrypoint-initdb.d/` của PostgreSQL image:
- File hỗ trợ Docker: `docker/postgres/init-oisms.sh` (mount vào `/docker-entrypoint-initdb.d/init-oisms.sh`).
- File schema bàn giao: `docs/database/oisms_schema.sql` (mount read-only vào `/docker-entrypoint-initdb.d/oisms_schema.sql`).
- Thư mục `docs/database` được giữ nguyên vẹn chỉ gồm 2 file bàn giao: `oisms_schema.sql` và `README.md`.

**Quy trình khởi tạo của `init-oisms.sh`:**
1. Khi volume `postgres_data` còn trống, PostgreSQL chạy script initdb.
2. Script phát hiện database `oisms` đã được tạo sẵn qua biến `POSTGRES_DB=oisms`.
3. Script xử lý việc file `oisms_schema.sql` có chứa `CREATE DATABASE oisms` và `\connect oisms` bằng cách bóc tách an toàn phần cài đặt DDL từ `-- OISMS_SCHEMA_BEGIN`, rồi thực thi trực tiếp vào database `oisms`.
4. Nếu bảng nghiệp vụ đã tồn tại (≥29 bảng), script bỏ qua DDL để bảo toàn dữ liệu. Nếu phát hiện bảng dở dang (1-28 bảng), script cảnh báo dữ liệu hiện có và yêu cầu migration chứ không tự ý ghi đè hay xóa bảng.
5. Tạo nhóm quyền `oisms_runtime` (NOLOGIN), sau đó cấu hình tài khoản runtime backend `oisms_app`:
   - Sử dụng cơ chế truyền biến `\getenv` của psql và hàm `format('%I', ...)` / `format('%L', ...)` trong khối PL/pgSQL; tuyệt đối **không nội suy chuỗi SQL**.
   - Hỗ trợ mật khẩu có dấu nháy và ký tự đặc biệt, không bao giờ in mật khẩu ra log.
   - Chặn không cho tài khoản runtime trùng tên với `postgres` hoặc nhóm `oisms_runtime`.
   - Kiểm tra an toàn: Nếu tài khoản `oisms_app` đã tồn tại, kiểm tra nghiêm ngặt xem tài khoản có đặc quyền quản trị (`rolsuper`, `rolcreatedb`, `rolcreaterole`, `rolreplication`, `rolbypassrls`), có thừa hưởng role superuser, hoặc có sở hữu database/schema/bảng hay không. Nếu phát hiện không an toàn, script dừng ngay lập tức và báo lỗi rõ ràng; không tự ý thay đổi quyền tài khoản ngoài phạm vi dự án.
   - Cấp quyền qua `GRANT oisms_runtime TO oisms_app;` và đặt `search_path = public, pg_catalog`.

### Tách biệt tài khoản Quản trị và Runtime

Tuân thủ nguyên tắc đặc quyền tối thiểu (Least Privilege):
- **Tài khoản quản trị (`postgres`)**: Dùng cho DBA, chạy migration DDL, backup và restore. Biến môi trường: `POSTGRES_USER` và `POSTGRES_PASSWORD`.
- **Tài khoản runtime backend (`oisms_app`)**: Dùng cho Backend Web API trong thời gian chạy. Gán role `oisms_runtime`, chỉ có quyền thao tác dữ liệu (`SELECT`, `INSERT`, `UPDATE`), không sở hữu bảng, không có quyền `DROP`, `ALTER`, `TRUNCATE`, và bị trigger chặn `UPDATE`/`DELETE` trên bảng sổ cái kho. Biến môi trường: `OISMS_APP_USER` và `OISMS_APP_PASSWORD`.

> [!NOTE]
> **Lưu ý về Volume đã tồn tại:** Thay đổi `POSTGRES_PASSWORD` hoặc `OISMS_APP_PASSWORD` trong `.env` sẽ **không** tự động cập nhật role trên volume đã có dữ liệu (vì script initdb chỉ chạy khi volume trống). Đối với volume cũ, hãy dùng lệnh `ALTER ROLE` thông qua tài khoản quản trị `postgres`.

Cấu hình kết nối Backend (.NET):
Hệ thống hỗ trợ 2 cơ chế cấu hình kết nối, ưu tiên truyền biến cấu hình riêng biệt để chống lỗi phân tích ký tự đặc biệt (đặc biệt là dấu chấm phẩy `;` trong mật khẩu):

1. **Biến môi trường riêng biệt (Khuyên dùng - được Docker Compose sử dụng mặc định):**
   - Chạy Backend trong Docker (kết nối service `postgres`):
     - `Database__Host=postgres`
     - `Database__Port=5432`
     - `Database__Database=oisms`
     - `Database__Username=oisms_app`
     - `Database__Password=<OISMS_APP_PASSWORD>`
   - Chạy Backend tại máy Host (Local Dev kết nối Docker DB qua `localhost`):
     - `Database__Host=localhost`
     - `Database__Port=5432`
     - `Database__Database=oisms`
     - `Database__Username=oisms_app`
     - `Database__Password=<OISMS_APP_PASSWORD>`
   *(ASP.NET Core tự động liên kết các biến môi trường dạng `Database__Key` vào section cấu hình `Database:Key`)*

2. **Connection String tổng hợp (Fallback):**
   - Docker: `ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=oisms;Username=oisms_app;Password=<OISMS_APP_PASSWORD>`
   - Local Host: `ConnectionStrings__DefaultConnection=Host=localhost;Port=5432;Database=oisms;Username=oisms_app;Password=<OISMS_APP_PASSWORD>`

### Kiểm tra kết nối và trạng thái

Kiểm tra trạng thái container và healthcheck:
```powershell
docker compose ps postgres
```

Kiểm tra kết nối bằng tài khoản quản trị (`postgres`):
```powershell
docker compose exec -it postgres psql -U postgres -d oisms -c "SELECT count(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE';"
```

Kiểm tra kết nối bằng tài khoản runtime backend (`oisms_app`):
```powershell
docker compose exec -it postgres psql -U oisms_app -d oisms -c "SELECT rolname, rolcanlogin, rolsuper FROM pg_roles WHERE rolname='oisms_app';"
```

Kiểm tra danh sách trigger bảo vệ:
```powershell
docker compose exec -it postgres psql -U oisms_app -d oisms -c "SELECT tgrelid::regclass AS table_name, tgname FROM pg_trigger WHERE NOT tgisinternal ORDER BY 1,2;"
```

Kiểm tra trạng thái sẵn sàng của Backend qua Readiness Endpoint:
```powershell
curl http://localhost:5000/health/ready
```
Endpoint `/health/ready` thực hiện mở kết nối PostgreSQL bằng tài khoản runtime `oisms_app`, kiểm tra đủ 29 bảng, trigger bất biến sổ cái và quyền hạn trước khi trả về HTTP 200 `Healthy` (trả về HTTP 503 `Unhealthy` nếu thất bại, không để lộ connection string hoặc mật khẩu).

### Xem log cơ sở dữ liệu

Theo dõi log container PostgreSQL theo thời gian thực:
```powershell
docker compose logs -f postgres
```

Lọc log liên quan đến quá trình khởi tạo schema:
```powershell
docker compose logs postgres | grep -E "init-oisms|OISMS"
```

### Báo cáo kết quả kiểm chứng & xác nhận kết nối Database

Hệ thống đã trải qua quy trình kiểm chứng tự động toàn diện đối với cơ chế cấp phát tài khoản, kết nối runtime, bảo vệ sổ cái và tích hợp Docker Compose:

#### 1. Kết quả kiểm chứng tài khoản Runtime & Bảo vệ Least-Privilege
- **Cơ chế cấp phát an toàn:** Script `docker/postgres/init-oisms.sh` sử dụng bảng tạm riêng phiên `_oisms_init_params` và hàm `format('%L', ...)` / `format('%I', ...)`, giải quyết triệt để lỗi không nội suy biến dollar-quote của psql (`SQLSTATE 42601`).
- **Hỗ trợ mật khẩu đặc biệt:** Đã kiểm chứng thành công với mật khẩu phức tạp chứa dấu nháy đơn, nháy kép, dấu chấm phẩy, ký tự $, \ và khoảng trắng (`p@ss'w"ord;with$pec!al#chars\and spaces`). Mật khẩu không bao giờ bị lộ ra log.
- **Chặn đặc quyền quản trị & quyền sở hữu:** Tự động phát hiện và chặn nếu tài khoản runtime có quyền admin trực tiếp (`rolsuper`, `rolcreatedb`, `rolcreaterole`, `rolreplication`, `rolbypassrls`), có membership kế thừa từ admin/server roles, hoặc sở hữu database/schema/bảng.
- **Bảo vệ bất biến sổ cái (Append-Only):** Trigger `ledger_append_only` trên `public.inventory_transactions` chặn hoàn toàn các lệnh `UPDATE`, `DELETE`, `TRUNCATE` (kể cả với tài khoản schema owner), trả về mã lỗi `23514`.

#### 2. Kết quả kiểm chứng cấu hình chuỗi kết nối Backend (.NET 8)
- **Phân tách biến môi trường riêng biệt:** Chuyển đổi từ việc nối chuỗi raw sang cấu hình rời rạc (`Database__Host`, `Database__Port`, `Database__Database`, `Database__Username`, `Database__Password`), gán trực tiếp vào từng property của `NpgsqlConnectionStringBuilder`, loại bỏ hoàn toàn lỗi ngắt chuỗi kết nối do dấu chấm phẩy trong mật khẩu.
- **Cố định cổng nội bộ Docker:** Cấu hình `Database__Port: "5432"` trong `docker-compose.yml` đảm bảo backend kết nối chính xác vào cổng nội bộ của PostgreSQL container, không bị ảnh hưởng khi người dùng thay đổi `POSTGRES_PORT` ngoài máy host.
- **Đồng bộ cổng chạy local:** File `Properties/launchSettings.json` được chuẩn hóa về `http://localhost:5000` và `https://localhost:5001`, đồng bộ với `scripts/run-backend.ps1`, `.env.example` và cấu hình gọi API của Frontend.

#### 3. Kết quả kiểm chứng Endpoint Sẵn sàng (/health/ready)
Endpoint `GET /health/ready` thực hiện kiểm tra sâu thực tế:
- Mở kết nối thật tới CSDL bằng tài khoản runtime `oisms_app` (timeout 5s).
- Kiểm tra tài khoản kết nối không có đặc quyền quản trị trực tiếp hoặc thừa hưởng, không sở hữu schema/bảng (trả về HTTP 503 `Degraded` nếu vi phạm).
- Xác minh đầy đủ 29/29 bảng nghiệp vụ trong schema `public` (trả về HTTP 503 `Unhealthy` nếu thiếu bảng).
- Xác minh trigger `ledger_append_only` đang kích hoạt (`tgenabled IN ('O', 'A')`) trên `public.inventory_transactions`.
- Bảo mật: Tuyệt đối không để lộ chuỗi kết nối hoặc mật khẩu ra HTTP response khi xảy ra lỗi.

#### 4. Tổng hợp số liệu kiểm thử thực tế
| Phạm vi kiểm thử | Công cụ / Môi trường | Số test | Kết quả | Chi tiết kiểm chứng |
| :--- | :--- | :--- | :--- | :--- |
| **CSDL & Provisioning** | `scripts/test-database-schema.cjs` (PostgreSQL 16 PGlite) | 26 / 26 | **PASS (100%)** | 29 bảng, 94 FK, 86 CHECK, chống UPDATE/DELETE sổ cái, cân bằng tồn kho, mật khẩu ký tự đặc biệt, chặn quyền admin/thừa hưởng role |
| **Backend Integration** | `src/backend/tests/OISM.IntegrationTests` (xUnit, .NET 8) | 11 / 11 | **PASS (100%)** | Dựng chuỗi kết nối với mật khẩu chấm phẩy/nháy kép, fallback default, endpoint liveness, timeout readiness, missing tables, trigger status, admin rejection |
| **Toàn bộ dự án** | `scripts/build-all.ps1` (PowerShell) | 4 công đoạn | **PASS (100%)** | Backend build (0 warn, 0 error), Unit & Integration tests, Admin Dashboard build, POS App build |

> [!NOTE]
> **Ranh giới kiểm thử môi trường:** Máy host Windows hiện tại chưa có Docker CLI trong PATH, nên việc khởi chạy container thực tế được kiểm chứng tự động 100% qua môi trường PostgreSQL 16 nhúng (PGlite) và bộ test tích hợp .NET WebApplicationFactory.

### Cập nhật cơ sở dữ liệu đã có dữ liệu (Migration Strategy)

> [!WARNING]
> **Tuyệt đối không chạy `docker compose down -v`** khi volume đã có dữ liệu, vì cờ `-v` (`--volumes`) sẽ xóa sạch volume lưu trữ `postgres_data`.

Khi container khởi động lại hoặc khi volume `postgres_data` đã tồn tại dữ liệu:
- PostgreSQL tự động bỏ qua `/docker-entrypoint-initdb.d/`, không chạy lại script init và không làm mất dữ liệu hiện có.
- File `init-oisms.sh` cũng có cơ chế phòng vệ: kiểm tra nếu bảng đã tồn tại thì bỏ qua DDL.

Để cập nhật schema trên cơ sở dữ liệu đã có dữ liệu, nhóm thực hiện theo quy trình migration:
1. **Sao lưu trước khi migration (Backup):**
   ```powershell
   docker compose exec postgres pg_dump -U postgres -d oisms -F c -b -v -f /var/lib/postgresql/data/backup_pre_migration.dump
   ```
2. **Soạn script migration tăng dần (Incremental SQL):**
   Không chạy lại toàn bộ `oisms_schema.sql`. Tạo file SQL migration tăng dần (ví dụ `migrations/V0.6__add_column.sql`) chứa các lệnh `ALTER TABLE`, `CREATE INDEX`, v.v.
3. **Thực thi script bằng tài khoản quản trị `postgres`:**
   ```powershell
   docker compose exec -i postgres psql -U postgres -d oisms -f /path/to/migration.sql
   ```
4. **Nếu sử dụng EF Core Migration:**
   Cấu hình connection string migration với tài khoản `postgres` (hoặc tài khoản DBA có quyền DDL) để thực thi `dotnet ef database update`.

## Quy ước tên và dữ liệu

| Nội dung | Quy ước |
|---|---|
| Tên bảng/cột | snake_case; Entity C# PascalCase phải mapping rõ |
| PK | UUID, default gen_random_uuid(); ID không thể hiện thứ tự thời gian |
| TenantId | Phạm vi doanh nghiệp; lấy từ xác thực, không tin client tự truyền |
| Thời gian | timestamptz; API ISO 8601 có múi giờ, ứng dụng chuẩn hóa UTC |
| Ngày báo cáo | business_date là ngày kinh doanh đã thống nhất; không mặc định UTC date nếu báo cáo theo giờ Việt Nam |
| created_at / updated_at | created_at default now(); updated_at do backend gán, không có trigger tự cập nhật |
| Số lượng | numeric(18,3) |
| Giá vốn/đơn giá nhập | numeric(20,6) |
| Giá bán/tổng tiền | numeric(20,2); C# decimal, không dùng float/double cho tiền |
| NULL | Chưa có/chưa biết; khác số 0. Draft có thể chưa có CostPrice |
| is_active | Ngừng sử dụng, không xóa lịch sử; backend kiểm tra khi tạo chứng từ mới |
| Enum | Đúng chuỗi CHECK, phân biệt hoa thường; không ghi nhãn tiếng Việt |
| Email | lowercase + trim, unique toàn hệ thống hiện tại |
| Điện thoại | Dạng quốc tế +..., ví dụ +84912345678; chuẩn hóa ngay cột chính |
| SKU code | uppercase + trim, unique trong tenant |
| Barcode | EAN13/Code128; có mã thì có loại. DB kiểm tra 13 số EAN13; checksum/Code128 do backend |
| request_key | Giữ nguyên cho retry cùng yêu cầu, không sinh mới mỗi lần thử |
| reference_id | Ledger tự sinh từ FK **dòng nguồn**, không phải ID đầu phiếu; không truyền khi INSERT |
| FK | Thường ghép tenant_id; ON DELETE RESTRICT, ON UPDATE NO ACTION; không cascade xóa chứng từ |

Scale numeric không thay chính sách làm tròn. Nhóm phải thống nhất rounding cho WAC, tổng tiền và báo cáo. Một tenant có nhiều chi nhánh, mỗi chi nhánh có nhiều kho. Một product có ít nhất một SKU ở mức nghiệp vụ; database không tự tạo SKU khi tạo product.

## Phạm vi và quyết định đã chốt

WAC theo chi nhánh/SKU; tồn và giữ chỗ theo từng kho; chi nhánh dùng tổng các kho; thuộc tính biến thể tùy ý; đăng nhập bằng email hoặc SĐT (ít nhất một, không bắt buộc cả hai). QR chưa chốt. Đây là các quyết định của nhóm để hiện thực đề; không phải số bảng được đề liệt kê sẵn.

## Hai cấp tồn và WAC

- `warehouse_inventory_balances`: một dòng mỗi tenant/kho/SKU; OnHand, Reserved, LastSequence. Không lưu WAC riêng kho.
- `inventory_balances`: một dòng mỗi tenant/chi nhánh/SKU; tổng OnHand/Reserved, AverageCost, ReorderPoint và LastSequence.
- Tổng OnHand/Reserved của các kho phải bằng các giá trị tương ứng ở chi nhánh. Hai cấp là dữ liệu dẫn xuất từ nghiệp vụ kho, được cập nhật cùng transaction dưới khóa; Constraint trigger v0.5 từ chối COMMIT nếu hai cấp số dư, ledger hoặc reservations không khớp. Backend vẫn phải ghi đầy đủ trong transaction; database không tự cộng/trừ thay service.
- Available chi nhánh = tổng Available các kho. Hàng đang vận chuyển được theo dõi từ phiếu chuyển, không tính vào Available tại kho nguồn hoặc đích.
- Nhập từ nhà cung cấp: WAC mới = (lượng thuộc phạm vi tính giá trước nhập × WAC cũ + lượng nhập × đơn giá nhập) / (lượng trước nhập + lượng nhập). Khi không có hàng vận chuyển nội bộ, lượng trước nhập là OnHand chi nhánh.
- WAC dùng chung cho mọi kho thuộc chi nhánh. Khi Confirmed/POS, chụp vào OrderItems.CostPrice; không sửa giá vốn đơn cũ theo WAC mới.
- Chuyển cùng chi nhánh không tự làm thay đổi WAC. Nếu đang có hàng vận chuyển nội bộ và phát sinh nhập mới, cơ chế tính giá phải bao gồm lượng/giá trị nội bộ đang vận chuyển trong phạm vi giá vốn chi nhánh, tránh coi là mua mới khi nhận. Quy tắc và kiểm thử định giá vận chuyển phải được triển khai trước nghiệm thu.
- Chuyển khác chi nhánh: chụp chi phí khi xuất, nhập đích tính WAC theo chi phí chuyển đó. Lượng/giá trị đang vận chuyển trình bày riêng; không ghi mất hàng.

## Chứng từ, giữ chỗ và ledger

Phiếu nhập, kiểm kê, điều chỉnh và trả hàng bắt buộc chỉ ra kho và chi nhánh. FK ghép bảo đảm kho thuộc đúng chi nhánh và tenant. Một phiếu trong các loại này xử lý một kho; nhiều kho dùng nhiều phiếu.

StockTransfers có FromBranchId/ToBranchId và FromWarehouseId/ToWarehouseId. Hai kho phải khác nhau và thuộc đúng chi nhánh. Chuyển khác chi nhánh thực hiện yêu cầu đề; chuyển cùng chi nhánh hỗ trợ quản lý vị trí theo thiết kế bổ sung. Luồng xuất → InTransit → nhận; chưa đặc tả nhận thiếu/nhiều đợt.

Orders vẫn gắn chi nhánh, không bắt buộc một kho cho cả đơn. Reservations phân bổ từng dòng hàng xuống một hoặc nhiều kho của chi nhánh đơn. Tổng lượng phân bổ phải khớp dòng đơn và không vượt tồn khả dụng tại từng kho. Chính sách chọn kho do service thực hiện; chưa tự chọn FIFO hoặc kho ưu tiên.

Ledger giữ các trường đề yêu cầu và thêm WarehouseId để truy vết. BalanceAfter/SequenceNo ở cấp chi nhánh/SKU. LastSequence của số dư kho ghi mốc sequence chi nhánh gần nhất tác động vào kho đó. Kiểm kê so với số dư và LastSequence của kho, không so một kho với tổng chi nhánh.

Ledger có đúng một dòng nguồn. ReferenceId được sinh từ FK nguồn. Dòng bán có thể phát sinh một OUT tại mỗi kho phân bổ; unique theo dòng bán/kho chống ghi lặp. Các nguồn còn lại giữ một bút toán mỗi dòng; chuyển có một OUT và một IN. Service phải kiểm tra SKU, vị trí, lượng nguồn và tổng phân bổ: FK không tự chứng minh tất cả quan hệ nghiệp vụ này.

Mọi thao tác tồn/giữ chỗ/WAC/ledger/trạng thái phải nguyên tử, chống retry và cạnh tranh. Ledger cấm UPDATE/DELETE, sửa sai bằng bút toán bù. v0.5 đã có append-only trigger, constraint kiểm tra tồn ở COMMIT, khóa giao dịch và script runtime permissions riêng. Posting service/WAC vẫn thuộc phần triển khai backend. Xem phần hợp đồng transaction bên dưới.

## Biến thể tùy ý

Ví dụ `Skus.Attributes`:

```json
{"màu":"đen","size":"L","chất liệu":"cotton"}
```

Sản phẩm khác có thể dùng `{"dung lượng":"256GB","bảo hành":"24 tháng"}`. Sản phẩm không có biến thể dùng `{}`. Không thêm cột SQL mỗi khi có thuộc tính mới.

Database kiểm tra đây là JSON object. Backend phải chuẩn hóa tên/giá trị, giới hạn kích thước, từ chối thuộc tính rỗng/sai định dạng và kiểm tra tổ hợp thuộc tính trùng trong cùng product dưới khóa. DDL chưa bảo đảm uniqueness theo tổ hợp; SKU code vẫn unique theo tenant. Thiết kế chưa có danh mục thuộc tính/giá trị dùng chung có quản trị riêng, vì yêu cầu hiện tại là thêm thuộc tính tùy ý.


## Đối chiếu chức năng

| Nhóm yêu cầu | Bảng hỗ trợ |
|---|---|
| Tenant, auth, vai trò, chi nhánh/kho | Tenants, Users, RefreshTokens, Branches, Warehouses |
| Sản phẩm, biến thể, barcode, giá | Categories, Brands, Products, Skus |
| Nhập và WAC | Suppliers, GoodsReceipts/Items, hai cấp số dư, ledger |
| Chuyển, kiểm kê, điều chỉnh | StockTransfers/Items, StockCounts/Items, InventoryAdjustments/Items |
| Đơn đa kênh, giữ chỗ, POS, thanh toán | Orders/Items, Reservations, Payments |
| Return trong ledger và báo cáo thuần | SalesReturns/Items |
| Báo cáo, cảnh báo tồn thấp | DailySalesSummaries, Orders/Items, ledger, ReorderPoint |
| Simulator, SignalR, Hangfire | Mã đơn ngoài, thời hạn Reservations, tổng hợp ngày; cần triển khai service/job |
| AI gợi ý nhập | ReplenishmentSuggestions và lịch sử bán/tồn; cần mô hình và đánh giá |

Tên và số bảng là thiết kế triển khai, không phải danh sách được đề liệt kê. InventoryTransactions là log kho bắt buộc; chưa có audit log toàn hệ thống vì đề không yêu cầu bảng riêng.


## Database bảo đảm gì?

- Ledger không UPDATE/DELETE/TRUNCATE. Runtime role không có quyền sửa cấu trúc hoặc tắt trigger. Tài khoản quản trị vẫn có thể thay đổi schema, vì vậy phải tách tài khoản.
- Không xóa/truncate bảng số dư và reservations. Giải phóng giữ chỗ bằng chuyển trạng thái, không xóa lịch sử.
- Mọi ghi lên ledger, số dư và reservations lấy transaction advisory lock theo tenant/chi nhánh/SKU. Chỉ hỗ trợ READ COMMITTED cho các thao tác này.
- Constraint trigger chạy cuối transaction: tổng số dư kho = số dư chi nhánh; số dư kho = tổng IN/OUT ledger của kho; reserved kho = tổng reservations Active; sequence và BalanceAfter liên tục/đúng; tổng Active + Consumed phân bổ cho một dòng đơn không vượt lượng dòng đơn.
- Sửa riêng OnHand, hoặc sửa cả hai bảng số dư mà không có ledger tương ứng, không commit được. Các constraint không tự cập nhật số dư thay backend.
- Đơn có ConfirmedAt bắt buộc có dòng hàng và CostPrice không null. Không xóa mốc ConfirmedAt hoặc thay giá vốn, giá bán, số lượng đã chốt. Điều này kiểm tra sự tồn tại/bảo toàn snapshot; không tự tính WAC.
- FK chống tham chiếu chéo tenant/kho/chi nhánh, CHECK không âm, UNIQUE chống nguồn ledger/đơn trùng và các index hỗ trợ truy vấn vẫn được giữ.

## Thứ tự transaction bắt buộc

Backend dùng một connection và một transaction READ COMMITTED cho toàn thao tác:

1. Xác thực quyền và tenant từ JWT; không tin TenantId do client gửi. Query filters và write guards vẫn phải triển khai. Chưa có RLS trong bản v0.5.
2. Lấy khóa **trước khi đọc để tính toán**, theo thứ tự tenant/branch/SKU cố định cho toàn bộ SKU bị tác động; khóa cả nguồn và đích của chuyển kho. Khóa dùng cùng biểu thức với trigger:

```sql
SELECT pg_advisory_xact_lock(
  hashtextextended(@tenant::text || '/' || @branch::text || '/' || @sku::text, 0)
);
```

`@tenant/@branch/@sku` là tham số UUID của Npgsql. Trigger tự khóa khi ghi là lớp bảo vệ bổ sung, không thay bước khóa trước khi đọc WAC/available. Khi dùng SQL trực tiếp, thay bằng tham số bind tương ứng của client.

3. Đọc/khóa chứng từ và số dư; kiểm tra trạng thái, tenant, SKU, kho, số lượng, gửi lặp. Tạo dòng số dư 0 nếu cần trong cùng transaction.
4. Tính và ghi thay đổi giữ chỗ, WAC, hai cấp số dư, snapshot và ledger. Khi bán: giảm reserved trước hoặc cùng câu UPDATE giảm on_hand để không vi phạm CHECK tức thời. Sequence ledger tăng tại chi nhánh/SKU; LastSequence của kho là sequence cuối tác động kho đó.
5. Cập nhật trạng thái chứng từ/đơn. Có thể gọi `SET CONSTRAINTS ALL IMMEDIATE` ở cuối để phát hiện lỗi trước COMMIT. Không gọi sớm khi dữ liệu trong transaction còn đang cập nhật dở.
6. COMMIT. Nếu commit bị từ chối, rollback toàn bộ và không phát thông báo thành công. Chỉ gửi SignalR sau commit. Retry toàn transaction có giới hạn đối với deadlock; giữ nguyên request key và không retry vô hạn lỗi dữ liệu.

## Phân công tích hợp

- Khoa/Backend 1: service ghi kho dùng chung, WAC chi nhánh, nhập/chuyển/kiểm kê/điều chỉnh, khóa, hai cấp số dư, ledger và rollback. Không tạo API chỉnh OnHand tùy ý.
- TV2/Backend 2: identity/catalog, chuẩn hóa attributes/barcode, tenant filters và RBAC; dùng contract số dư có chi nhánh/kho. Không tự viết cơ chế cộng/trừ tồn thứ hai.
- Hai thành viên thống nhất đường đi confirm/cancel/expire; test thao tác đồng thời, source/SKU/quantity đúng chứng từ, trả không vượt bán, snapshot đúng WAC và tổng lượng phân bổ đủ để xác nhận. Các guard hiện tại không thay mọi validation nghiệp vụ.

## Phạm vi chưa được thực hiện bằng SQL

Không có API/auth service, posting service tự tính WAC, state machine đầy đủ, chọn kho, đối soát trả hàng, Hangfire/SignalR, AI hay tích hợp QR. QR tiếp tục để mở theo quyết định của Khoa. SQL không chọn chính sách hủy sau xuất hàng hoặc nhận chuyển từng phần.

Kiểm tra commit hiện đối soát lịch sử của SKU/chi nhánh bị tác động để ưu tiên tính đúng. Chưa có bằng chứng đạt p95/triệu dòng hoặc 50 client đồng thời; cần benchmark và tối ưu trước nghiệm thu. Không coi bộ kiểm tra PostgreSQL nhúng là test tải thực tế.

## Tái tạo và kiểm thử

```powershell
node scripts/test-database-schema.cjs
```

Test cần `@electric-sql/pglite`; đặt `OISMS_TEST_MODULES` trỏ đến thư mục node_modules đã cài. Mặc định dùng `tmp/oisms-schema-validation/node_modules`. Không cần database thật và không đụng dữ liệu đang chạy.


## Quản lý thay đổi và bàn giao giữa hai backend

- SQL này là baseline cài mới. Khi đưa vào EF Core migration, phải mang theo function/trigger/index/quyền, không chỉ generate entity rồi coi tương đương.
- Thay đổi cấu trúc sửa file SQL bàn giao và phần tương ứng trong README, review cùng nhau; nếu database đã triển khai thì tạo migration riêng có chuyển dữ liệu rõ ràng. Không sửa migration đã chạy trên môi trường chung.
- Một người phụ trách migration tại một thời điểm; người còn lại cập nhật sau khi merge. Không chỉnh tay DB cá nhân rồi quên cập nhật nguồn.
- Entity, DTO, Swagger và frontend phải thống nhất kiểu, nullable, enum và phạm vi tenant/branch/warehouse.
- Danh mục có is_active; chứng từ đã ghi ledger phải bảo toàn lịch sử. Các điều kiện nguồn chứng từ đúng SKU/kho/lượng, trả không vượt bán và chống sửa chứng từ đã ghi kho vẫn phải được kiểm tra trong service.
- Không tự chọn chính sách hủy sau Confirmed/Completed, nhận chuyển thiếu/từng phần hoặc split payment. Hiện một payment/đơn, chuyển hai bước cơ bản.
- Không thêm bảng dự phòng; thay đổi dựa trên yêu cầu rõ ràng. Hangfire có thể tạo các bảng kỹ thuật riêng khi tích hợp, ngoài 29 bảng nghiệp vụ.

## Index và bằng chứng kiểm thử

Index phục vụ lịch sử ledger theo tenant/thời gian và tenant/branch/SKU; Orders theo tenant/thời gian/chi nhánh/trạng thái; OrderItems theo SKU; reservation hết hạn; đối soát tồn kho/chi nhánh. UNIQUE cũng tạo index tương ứng. Chưa có index tìm tên sản phẩm toàn văn; bổ sung dựa trên truy vấn và EXPLAIN thực tế.

22 ca kiểm tra PostgreSQL nhúng bao gồm bộ file bàn giao, cài đủ schema, nhập/bán mẫu, bảo vệ ledger, hai cấp số dư và rollback, giữ chỗ, snapshot, tenant FK, chống nguồn trùng và quyền runtime. Kết quả in trực tiếp ra terminal. Không còn kiểm thử generator, seed rời hoặc migration cũ vì các file đó đã được bỏ.

Test nhúng chạy phần SQL sau bước kết nối, **không thực thi client psql hoặc CREATE DATABASE trên server thật**. Chưa triển khai vào database đang chạy. Chưa chứng minh hiệu năng/50 client đồng thời, coverage 80%, bộ tính WAC, API/RBAC, jobs, AI hoặc QR.

## Từ điển dữ liệu đầy đủ

Phần dưới mô tả file SQL bàn giao; khi sửa SQL phải cập nhật tương ứng tại đây. “Bắt buộc” là NOT NULL, không đồng nghĩa backend luôn phải truyền nếu đã có default. FK nullable chỉ kiểm tra khi có giá trị. Các CHECK mô tả đúng mức database kiểm tra; validation nghiệp vụ còn lại xem các mục trên.

<!-- DATABASE_DICTIONARY_START -->

### 1. `tenants`

Doanh nghiệp sử dụng hệ thống.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `name` | `varchar(200)` | Có | Backend phải cung cấp |
| `is_active` | `boolean` | Có | `true` |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

### 2. `users`

Email hoặc SĐT định danh duy nhất toàn hệ thống; mỗi user thuộc một tenant.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `email` | `varchar(254)` | Không | Backend cung cấp khi có giá trị |
| `phone` | `varchar(16)` | Không | Backend cung cấp khi có giá trị |
| `password_hash` | `varchar(255)` | Có | Backend phải cung cấp |
| `display_name` | `varchar(150)` | Có | Backend phải cung cấp |
| `role` | `varchar(20)` | Có | Backend phải cung cấp |
| `is_active` | `boolean` | Có | `true` |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(email)`; `(phone)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.

**CHECK:**

- `role IN ('Owner', 'Staff', 'Cashier')`.
- `email IS NOT NULL OR phone IS NOT NULL`.
- `email IS NULL OR (email = lower(btrim(email)) AND length(email) > 0)`.
- `phone IS NULL OR phone ~ '^\+[1-9][0-9]{7,14}$'`.

### 3. `refresh_tokens`

Lưu hash; logout thu hồi, refresh xoay token trong transaction.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `user_id` | `uuid` | Có | Backend phải cung cấp |
| `token_hash` | `varchar(128)` | Có | Backend phải cung cấp |
| `expires_at` | `timestamptz` | Có | Backend phải cung cấp |
| `revoked_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |
| `created_at` | `timestamptz` | Có | `now()` |

**UNIQUE:** `(tenant_id, id)`; `(token_hash)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, user_id)` → `users(tenant_id, id)`.

**CHECK:**

- `expires_at > created_at`.

### 4. `branches`

Chi nhánh của tenant.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `name` | `varchar(200)` | Có | Backend phải cung cấp |
| `address` | `text` | Không | Backend cung cấp khi có giá trị |
| `is_active` | `boolean` | Có | `true` |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.

### 5. `warehouses`

Kho thuộc chi nhánh theo FR-AUTH-04; không dùng kho làm giới hạn tồn khả dụng của đơn.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `branch_id` | `uuid` | Có | Backend phải cung cấp |
| `name` | `varchar(200)` | Có | Backend phải cung cấp |
| `is_active` | `boolean` | Có | `true` |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, id, branch_id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, branch_id)` → `branches(tenant_id, id)`.

### 6. `categories`

Danh mục cha–con; service kiểm tra chu trình nhiều cấp.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `parent_id` | `uuid` | Không | Backend cung cấp khi có giá trị |
| `name` | `varchar(200)` | Có | Backend phải cung cấp |
| `is_active` | `boolean` | Có | `true` |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, parent_id)` → `categories(tenant_id, id)`.

**CHECK:**

- `parent_id IS NULL OR parent_id <> id`.

### 7. `brands`

Thương hiệu sản phẩm.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `name` | `varchar(200)` | Có | Backend phải cung cấp |
| `is_active` | `boolean` | Có | `true` |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.

### 8. `products`

Thông tin chung; sản phẩm đơn giản vẫn có một SKU.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `category_id` | `uuid` | Không | Backend cung cấp khi có giá trị |
| `brand_id` | `uuid` | Không | Backend cung cấp khi có giá trị |
| `name` | `varchar(250)` | Có | Backend phải cung cấp |
| `description` | `text` | Không | Backend cung cấp khi có giá trị |
| `is_active` | `boolean` | Có | `true` |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, category_id)` → `categories(tenant_id, id)`.
- `(tenant_id, brand_id)` → `brands(tenant_id, id)`.

### 9. `skus`

SKU chứa trực tiếp một barcode và giá sỉ/lẻ. Attributes là object JSON cho thuộc tính biến thể tùy ý; service chuẩn hóa tên/giá trị và kiểm tra tổ hợp trùng trong product.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `product_id` | `uuid` | Có | Backend phải cung cấp |
| `code` | `varchar(80)` | Có | Backend phải cung cấp |
| `attributes` | `jsonb` | Có | `'{}'::jsonb` |
| `unit` | `varchar(30)` | Có | `'piece'` |
| `barcode_type` | `varchar(20)` | Không | Backend cung cấp khi có giá trị |
| `barcode` | `varchar(128)` | Không | Backend cung cấp khi có giá trị |
| `retail_price` | `numeric(20,2)` | Có | Backend phải cung cấp |
| `wholesale_price` | `numeric(20,2)` | Có | Backend phải cung cấp |
| `is_active` | `boolean` | Có | `true` |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, code)`; `(tenant_id, barcode)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, product_id)` → `products(tenant_id, id)`.

**CHECK:**

- `jsonb_typeof(attributes) = 'object'`.
- `code = upper(btrim(code)) AND length(code) > 0`.
- `(barcode_type IS NULL) = (barcode IS NULL)`.
- `barcode_type IN ('EAN13', 'Code128')`.
- `barcode_type <> 'EAN13' OR barcode ~ '^[0-9]{13}$'`.
- `retail_price >= 0`.
- `wholesale_price >= 0`.

### 10. `suppliers`

Nhà cung cấp của phiếu nhập.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `name` | `varchar(200)` | Có | Backend phải cung cấp |
| `phone` | `varchar(32)` | Không | Backend cung cấp khi có giá trị |
| `email` | `varchar(254)` | Không | Backend cung cấp khi có giá trị |
| `address` | `text` | Không | Backend cung cấp khi có giá trị |
| `is_active` | `boolean` | Có | `true` |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.

### 11. `goods_receipts`

Nhập vào kho cụ thể thuộc chi nhánh; cập nhật đồng thời tồn kho, tổng tồn chi nhánh và WAC chi nhánh.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `number` | `varchar(50)` | Có | Backend phải cung cấp |
| `branch_id` | `uuid` | Có | Backend phải cung cấp |
| `warehouse_id` | `uuid` | Có | Backend phải cung cấp |
| `supplier_id` | `uuid` | Có | Backend phải cung cấp |
| `status` | `varchar(20)` | Có | `'Draft'` |
| `note` | `text` | Không | Backend cung cấp khi có giá trị |
| `created_by` | `uuid` | Có | Backend phải cung cấp |
| `confirmed_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |
| `confirmed_by` | `uuid` | Không | Backend cung cấp khi có giá trị |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, number)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, branch_id)` → `branches(tenant_id, id)`.
- `(tenant_id, warehouse_id, branch_id)` → `warehouses(tenant_id, id, branch_id)`.
- `(tenant_id, supplier_id)` → `suppliers(tenant_id, id)`.
- `(tenant_id, created_by)` → `users(tenant_id, id)`.
- `(tenant_id, confirmed_by)` → `users(tenant_id, id)`.

**CHECK:**

- `status IN ('Draft', 'Confirmed', 'Cancelled')`.
- `(status = 'Confirmed') = (confirmed_at IS NOT NULL) AND (confirmed_at IS NULL) = (confirmed_by IS NULL)`.

### 12. `goods_receipt_items`

Mỗi SKU một dòng/phiếu; không phân bổ phí nhập/thuế mở rộng.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `receipt_id` | `uuid` | Có | Backend phải cung cấp |
| `sku_id` | `uuid` | Có | Backend phải cung cấp |
| `quantity` | `numeric(18,3)` | Có | Backend phải cung cấp |
| `unit_price` | `numeric(20,6)` | Có | Backend phải cung cấp |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, receipt_id, sku_id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, receipt_id)` → `goods_receipts(tenant_id, id)`.
- `(tenant_id, sku_id)` → `skus(tenant_id, id)`.

**CHECK:**

- `quantity > 0`.
- `unit_price >= 0`.

### 13. `inventory_balances`

Tổng tồn dẫn xuất từ các kho thuộc chi nhánh; WAC chốt theo chi nhánh/SKU. Cập nhật cùng transaction với số dư kho và ledger.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `branch_id` | `uuid` | Có | Backend phải cung cấp |
| `sku_id` | `uuid` | Có | Backend phải cung cấp |
| `on_hand` | `numeric(18,3)` | Có | `0` |
| `reserved` | `numeric(18,3)` | Có | `0` |
| `average_cost` | `numeric(20,6)` | Có | `0` |
| `reorder_point` | `numeric(18,3)` | Có | `0` |
| `last_sequence` | `bigint` | Có | `0` |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, branch_id, sku_id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, branch_id)` → `branches(tenant_id, id)`.
- `(tenant_id, sku_id)` → `skus(tenant_id, id)`.

**CHECK:**

- `on_hand >= 0`.
- `reserved >= 0`.
- `average_cost >= 0`.
- `reorder_point >= 0`.
- `last_sequence >= 0`.
- `reserved <= on_hand`.

### 14. `warehouse_inventory_balances`

Tồn vật lý và giữ chỗ riêng mỗi kho/SKU; không lưu WAC riêng kho. LastSequence ghi sequence chi nhánh của lần cập nhật gần nhất.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `branch_id` | `uuid` | Có | Backend phải cung cấp |
| `warehouse_id` | `uuid` | Có | Backend phải cung cấp |
| `sku_id` | `uuid` | Có | Backend phải cung cấp |
| `on_hand` | `numeric(18,3)` | Có | `0` |
| `reserved` | `numeric(18,3)` | Có | `0` |
| `last_sequence` | `bigint` | Có | `0` |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, warehouse_id, sku_id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, warehouse_id, branch_id)` → `warehouses(tenant_id, id, branch_id)`.
- `(tenant_id, sku_id)` → `skus(tenant_id, id)`.

**CHECK:**

- `on_hand >= 0`.
- `reserved >= 0`.
- `last_sequence >= 0`.
- `reserved <= on_hand`.

### 15. `stock_transfers`

Chuyển giữa hai kho qua xuất/nhận; có thể khác chi nhánh theo đề hoặc cùng chi nhánh để quản lý vị trí. Cùng chi nhánh không đổi WAC; hàng vận chuyển theo dõi riêng.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `number` | `varchar(50)` | Có | Backend phải cung cấp |
| `from_branch_id` | `uuid` | Có | Backend phải cung cấp |
| `to_branch_id` | `uuid` | Có | Backend phải cung cấp |
| `from_warehouse_id` | `uuid` | Có | Backend phải cung cấp |
| `to_warehouse_id` | `uuid` | Có | Backend phải cung cấp |
| `status` | `varchar(20)` | Có | `'Draft'` |
| `created_by` | `uuid` | Có | Backend phải cung cấp |
| `dispatched_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |
| `received_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |
| `received_by` | `uuid` | Không | Backend cung cấp khi có giá trị |
| `note` | `text` | Không | Backend cung cấp khi có giá trị |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, number)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, from_branch_id)` → `branches(tenant_id, id)`.
- `(tenant_id, to_branch_id)` → `branches(tenant_id, id)`.
- `(tenant_id, created_by)` → `users(tenant_id, id)`.
- `(tenant_id, received_by)` → `users(tenant_id, id)`.
- `(tenant_id, from_warehouse_id, from_branch_id)` → `warehouses(tenant_id, id, branch_id)`.
- `(tenant_id, to_warehouse_id, to_branch_id)` → `warehouses(tenant_id, id, branch_id)`.

**CHECK:**

- `status IN ('Draft', 'InTransit', 'Received', 'Cancelled')`.
- `from_warehouse_id <> to_warehouse_id`.
- `(status IN ('InTransit','Received')) = (dispatched_at IS NOT NULL)`.
- `(status = 'Received') = (received_at IS NOT NULL) AND (received_at IS NULL) = (received_by IS NULL)`.

### 16. `stock_transfer_items`

Chụp unit_cost khi xuất; nhận ở đích dùng đúng chi phí này.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `transfer_id` | `uuid` | Có | Backend phải cung cấp |
| `sku_id` | `uuid` | Có | Backend phải cung cấp |
| `quantity` | `numeric(18,3)` | Có | Backend phải cung cấp |
| `unit_cost` | `numeric(20,6)` | Không | Backend cung cấp khi có giá trị |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, transfer_id, sku_id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, transfer_id)` → `stock_transfers(tenant_id, id)`.
- `(tenant_id, sku_id)` → `skus(tenant_id, id)`.

**CHECK:**

- `quantity > 0`.
- `unit_cost >= 0`.

### 17. `stock_counts`

Kiểm kê SKU tại kho; đối chiếu số dư và sequence của kho, cập nhật tổng chi nhánh cùng transaction.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `number` | `varchar(50)` | Có | Backend phải cung cấp |
| `branch_id` | `uuid` | Có | Backend phải cung cấp |
| `warehouse_id` | `uuid` | Có | Backend phải cung cấp |
| `status` | `varchar(20)` | Có | `'Draft'` |
| `created_by` | `uuid` | Có | Backend phải cung cấp |
| `confirmed_by` | `uuid` | Không | Backend cung cấp khi có giá trị |
| `confirmed_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |
| `note` | `text` | Không | Backend cung cấp khi có giá trị |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, number)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, branch_id)` → `branches(tenant_id, id)`.
- `(tenant_id, warehouse_id, branch_id)` → `warehouses(tenant_id, id, branch_id)`.
- `(tenant_id, created_by)` → `users(tenant_id, id)`.
- `(tenant_id, confirmed_by)` → `users(tenant_id, id)`.

**CHECK:**

- `status IN ('Draft', 'Counting', 'Confirmed', 'Cancelled')`.
- `(status = 'Confirmed') = (confirmed_at IS NOT NULL) AND (confirmed_at IS NULL) = (confirmed_by IS NULL)`.

### 18. `stock_count_items`

Phát hiện giao dịch xen giữa bằng sequence; giá bổ sung cần cho hàng thừa chưa có WAC.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `stock_count_id` | `uuid` | Có | Backend phải cung cấp |
| `sku_id` | `uuid` | Có | Backend phải cung cấp |
| `expected_quantity` | `numeric(18,3)` | Có | Backend phải cung cấp |
| `counted_quantity` | `numeric(18,3)` | Không | Backend cung cấp khi có giá trị |
| `balance_sequence_at_snapshot` | `bigint` | Có | Backend phải cung cấp |
| `adjustment_unit_cost` | `numeric(20,6)` | Không | Backend cung cấp khi có giá trị |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, stock_count_id, sku_id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, stock_count_id)` → `stock_counts(tenant_id, id)`.
- `(tenant_id, sku_id)` → `skus(tenant_id, id)`.

**CHECK:**

- `expected_quantity >= 0`.
- `counted_quantity >= 0`.
- `balance_sequence_at_snapshot >= 0`.
- `adjustment_unit_cost >= 0`.

### 19. `inventory_adjustments`

Chứng từ tối thiểu cho bút toán bù/số dư đầu kỳ chi nhánh theo NFR-SEC-03.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `number` | `varchar(50)` | Có | Backend phải cung cấp |
| `branch_id` | `uuid` | Có | Backend phải cung cấp |
| `warehouse_id` | `uuid` | Có | Backend phải cung cấp |
| `reason` | `text` | Có | Backend phải cung cấp |
| `status` | `varchar(20)` | Có | `'Draft'` |
| `created_by` | `uuid` | Có | Backend phải cung cấp |
| `confirmed_by` | `uuid` | Không | Backend cung cấp khi có giá trị |
| `confirmed_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, number)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, branch_id)` → `branches(tenant_id, id)`.
- `(tenant_id, warehouse_id, branch_id)` → `warehouses(tenant_id, id, branch_id)`.
- `(tenant_id, created_by)` → `users(tenant_id, id)`.
- `(tenant_id, confirmed_by)` → `users(tenant_id, id)`.

**CHECK:**

- `status IN ('Draft', 'Confirmed', 'Cancelled')`.
- `length(btrim(reason)) > 0`.
- `(status = 'Confirmed') = (confirmed_at IS NOT NULL) AND (confirmed_at IS NULL) = (confirmed_by IS NULL)`.

### 20. `inventory_adjustment_items`

Tăng/giảm có lý do, có thể liên kết giao dịch cần bù; không sửa ledger cũ.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `adjustment_id` | `uuid` | Có | Backend phải cung cấp |
| `sku_id` | `uuid` | Có | Backend phải cung cấp |
| `quantity_delta` | `numeric(18,3)` | Có | Backend phải cung cấp |
| `unit_cost` | `numeric(20,6)` | Không | Backend cung cấp khi có giá trị |
| `original_transaction_id` | `uuid` | Không | Backend cung cấp khi có giá trị |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, adjustment_id, sku_id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, adjustment_id)` → `inventory_adjustments(tenant_id, id)`.
- `(tenant_id, sku_id)` → `skus(tenant_id, id)`.
- `(tenant_id, original_transaction_id)` → `inventory_transactions(tenant_id, id)`.

**CHECK:**

- `quantity_delta <> 0`.
- `unit_cost >= 0`.
- `quantity_delta < 0 OR unit_cost IS NOT NULL`.

### 21. `orders`

Đơn POS/Admin/sàn dùng tồn chi nhánh; không ràng buộc một kho cấp hàng. RequestKey giữ ổn định khi retry.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `number` | `varchar(50)` | Có | Backend phải cung cấp |
| `branch_id` | `uuid` | Có | Backend phải cung cấp |
| `channel` | `varchar(20)` | Có | Backend phải cung cấp |
| `external_shop_id` | `varchar(100)` | Không | Backend cung cấp khi có giá trị |
| `external_order_id` | `varchar(100)` | Không | Backend cung cấp khi có giá trị |
| `status` | `varchar(20)` | Có | `'Draft'` |
| `customer_name` | `varchar(200)` | Không | Backend cung cấp khi có giá trị |
| `customer_phone` | `varchar(32)` | Không | Backend cung cấp khi có giá trị |
| `shipping_address` | `text` | Không | Backend cung cấp khi có giá trị |
| `total_amount` | `numeric(20,2)` | Có | `0` |
| `request_key` | `uuid` | Có | Backend phải cung cấp |
| `created_by` | `uuid` | Không | Backend cung cấp khi có giá trị |
| `confirmed_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |
| `completed_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |
| `cancelled_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, number)`; `(tenant_id, request_key)`; `(tenant_id, channel, external_shop_id, external_order_id)`; `(tenant_id, id, branch_id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, branch_id)` → `branches(tenant_id, id)`.
- `(tenant_id, created_by)` → `users(tenant_id, id)`.

**CHECK:**

- `channel IN ('POS', 'Admin', 'Shopee', 'TikTok', 'Lazada')`.
- `status IN ('Draft', 'Reserved', 'Confirmed', 'Completed', 'Cancelled')`.
- `total_amount >= 0`.
- `(channel IN ('Shopee','TikTok','Lazada') AND external_shop_id IS NOT NULL AND external_order_id IS NOT NULL) OR (channel IN ('POS','Admin') AND external_shop_id IS NULL AND external_order_id IS NULL)`.
- `status NOT IN ('Confirmed','Completed') OR confirmed_at IS NOT NULL`.
- `(status <> 'Completed' OR completed_at IS NOT NULL) AND (completed_at IS NULL OR confirmed_at IS NOT NULL)`.
- `(status = 'Cancelled') = (cancelled_at IS NOT NULL)`.

### 22. `order_items`

Giữ tên/mã/giá bán lịch sử để in hóa đơn; CostPrice bắt buộc chụp khi xác nhận.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `order_id` | `uuid` | Có | Backend phải cung cấp |
| `sku_id` | `uuid` | Có | Backend phải cung cấp |
| `sku_code_snapshot` | `varchar(80)` | Có | Backend phải cung cấp |
| `sku_name_snapshot` | `varchar(250)` | Có | Backend phải cung cấp |
| `quantity` | `numeric(18,3)` | Có | Backend phải cung cấp |
| `unit_price` | `numeric(20,2)` | Có | Backend phải cung cấp |
| `cost_price` | `numeric(20,6)` | Không | Backend cung cấp khi có giá trị |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, order_id, sku_id)`; `(tenant_id, id, order_id, sku_id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, order_id)` → `orders(tenant_id, id)`.
- `(tenant_id, sku_id)` → `skus(tenant_id, id)`.

**CHECK:**

- `quantity > 0`.
- `unit_price >= 0`.
- `cost_price >= 0`.

### 23. `reservations`

Phân bổ giữ chỗ xuống kho thuộc chi nhánh đơn; một dòng đơn có thể phân bổ nhiều kho. Tổng lượng phân bổ phải khớp dòng đơn dưới khóa.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `order_id` | `uuid` | Có | Backend phải cung cấp |
| `order_item_id` | `uuid` | Có | Backend phải cung cấp |
| `branch_id` | `uuid` | Có | Backend phải cung cấp |
| `warehouse_id` | `uuid` | Có | Backend phải cung cấp |
| `sku_id` | `uuid` | Có | Backend phải cung cấp |
| `quantity` | `numeric(18,3)` | Có | Backend phải cung cấp |
| `status` | `varchar(20)` | Có | `'Active'` |
| `expires_at` | `timestamptz` | Có | Backend phải cung cấp |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, order_item_id, warehouse_id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, warehouse_id, branch_id)` → `warehouses(tenant_id, id, branch_id)`.
- `(tenant_id, order_item_id, order_id, sku_id)` → `order_items(tenant_id, id, order_id, sku_id)`.
- `(tenant_id, order_id, branch_id)` → `orders(tenant_id, id, branch_id)`.

**CHECK:**

- `quantity > 0`.
- `status IN ('Active', 'Consumed', 'Released', 'Expired')`.
- `expires_at > created_at`.

### 24. `payments`

Một khoản thanh toán/đơn; retry cập nhật cùng bản ghi, không tạo lần thu mới. Tiền thừa tính từ cash_received - amount.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `order_id` | `uuid` | Có | Backend phải cung cấp |
| `method` | `varchar(20)` | Có | Backend phải cung cấp |
| `status` | `varchar(20)` | Có | `'Pending'` |
| `amount` | `numeric(20,2)` | Có | Backend phải cung cấp |
| `cash_received` | `numeric(20,2)` | Không | Backend cung cấp khi có giá trị |
| `paid_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, order_id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, order_id)` → `orders(tenant_id, id)`.

**CHECK:**

- `method IN ('Cash', 'BankTransfer')`.
- `status IN ('Pending', 'Succeeded', 'Failed')`.
- `amount >= 0`.
- `cash_received >= 0`.
- `(status = 'Succeeded') = (paid_at IS NOT NULL)`.
- `(method = 'Cash' AND (status <> 'Succeeded' OR cash_received IS NOT NULL) AND (cash_received IS NULL OR cash_received >= amount)) OR (method = 'BankTransfer' AND cash_received IS NULL)`.

### 25. `sales_returns`

Nguồn giao dịch trả hàng cho ledger theo FR-INV-01; chi tiết quy trình hoàn tiền là thiết kế hỗ trợ, không phải yêu cầu nguyên văn.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `number` | `varchar(50)` | Có | Backend phải cung cấp |
| `order_id` | `uuid` | Có | Backend phải cung cấp |
| `branch_id` | `uuid` | Có | Backend phải cung cấp |
| `warehouse_id` | `uuid` | Có | Backend phải cung cấp |
| `status` | `varchar(20)` | Có | `'Draft'` |
| `reason` | `text` | Có | Backend phải cung cấp |
| `created_by` | `uuid` | Có | Backend phải cung cấp |
| `confirmed_by` | `uuid` | Không | Backend cung cấp khi có giá trị |
| `confirmed_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |
| `refund_method` | `varchar(20)` | Không | Backend cung cấp khi có giá trị |
| `refunded_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, number)`; `(tenant_id, id, order_id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, order_id)` → `orders(tenant_id, id)`.
- `(tenant_id, branch_id)` → `branches(tenant_id, id)`.
- `(tenant_id, warehouse_id, branch_id)` → `warehouses(tenant_id, id, branch_id)`.
- `(tenant_id, created_by)` → `users(tenant_id, id)`.
- `(tenant_id, confirmed_by)` → `users(tenant_id, id)`.

**CHECK:**

- `status IN ('Draft', 'Confirmed', 'Cancelled')`.
- `refund_method IN ('Cash', 'BankTransfer')`.
- `(status = 'Confirmed') = (confirmed_at IS NOT NULL) AND (confirmed_at IS NULL) = (confirmed_by IS NULL)`.
- `(refund_method IS NULL) = (refunded_at IS NULL) AND (refunded_at IS NULL OR status = 'Confirmed')`.

### 26. `sales_return_items`

Dòng trả phải thuộc đúng đơn/SKU. Giữ giá vốn gốc và số tiền hoàn để tính doanh thu thuần.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `sales_return_id` | `uuid` | Có | Backend phải cung cấp |
| `order_id` | `uuid` | Có | Backend phải cung cấp |
| `order_item_id` | `uuid` | Có | Backend phải cung cấp |
| `sku_id` | `uuid` | Có | Backend phải cung cấp |
| `quantity` | `numeric(18,3)` | Có | Backend phải cung cấp |
| `refund_amount` | `numeric(20,2)` | Có | Backend phải cung cấp |
| `original_unit_cost` | `numeric(20,6)` | Có | Backend phải cung cấp |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, sales_return_id, order_item_id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, sales_return_id, order_id)` → `sales_returns(tenant_id, id, order_id)`.
- `(tenant_id, order_item_id, order_id, sku_id)` → `order_items(tenant_id, id, order_id, sku_id)`.

**CHECK:**

- `quantity > 0`.
- `refund_amount >= 0`.
- `original_unit_cost >= 0`.

### 27. `inventory_transactions`

Ledger có trường đề yêu cầu và WarehouseId. BalanceAfter và SequenceNo theo chi nhánh/SKU; số dư kho được đối chiếu bằng tổng IN/OUT tại kho.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `branch_id` | `uuid` | Có | Backend phải cung cấp |
| `warehouse_id` | `uuid` | Có | Backend phải cung cấp |
| `sku_id` | `uuid` | Có | Backend phải cung cấp |
| `type` | `varchar(3)` | Có | Backend phải cung cấp |
| `quantity` | `numeric(18,3)` | Có | Backend phải cung cấp |
| `unit_cost` | `numeric(20,6)` | Có | Backend phải cung cấp |
| `balance_after` | `numeric(18,3)` | Có | Backend phải cung cấp |
| `sequence_no` | `bigint` | Có | Backend phải cung cấp |
| `receipt_item_id` | `uuid` | Không | Backend cung cấp khi có giá trị |
| `order_item_id` | `uuid` | Không | Backend cung cấp khi có giá trị |
| `transfer_item_id` | `uuid` | Không | Backend cung cấp khi có giá trị |
| `stock_count_item_id` | `uuid` | Không | Backend cung cấp khi có giá trị |
| `sales_return_item_id` | `uuid` | Không | Backend cung cấp khi có giá trị |
| `adjustment_item_id` | `uuid` | Không | Backend cung cấp khi có giá trị |
| `reference_id` | `uuid` | Có | Database tự sinh; không INSERT/UPDATE |
| `created_at` | `timestamptz` | Có | `now()` |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, branch_id, sku_id, sequence_no)`; `(tenant_id, receipt_item_id)`; `(tenant_id, order_item_id, warehouse_id)`; `(tenant_id, transfer_item_id, type)`; `(tenant_id, stock_count_item_id)`; `(tenant_id, sales_return_item_id)`; `(tenant_id, adjustment_item_id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, warehouse_id, branch_id)` → `warehouses(tenant_id, id, branch_id)`.
- `(tenant_id, sku_id)` → `skus(tenant_id, id)`.
- `(tenant_id, branch_id)` → `branches(tenant_id, id)`.
- `(tenant_id, receipt_item_id)` → `goods_receipt_items(tenant_id, id)`.
- `(tenant_id, order_item_id)` → `order_items(tenant_id, id)`.
- `(tenant_id, transfer_item_id)` → `stock_transfer_items(tenant_id, id)`.
- `(tenant_id, stock_count_item_id)` → `stock_count_items(tenant_id, id)`.
- `(tenant_id, sales_return_item_id)` → `sales_return_items(tenant_id, id)`.
- `(tenant_id, adjustment_item_id)` → `inventory_adjustment_items(tenant_id, id)`.

**CHECK:**

- `type IN ('IN', 'OUT')`.
- `quantity > 0`.
- `sequence_no > 0`.
- `unit_cost >= 0`.
- `balance_after >= 0`.
- `num_nonnulls(receipt_item_id, order_item_id, transfer_item_id, stock_count_item_id, sales_return_item_id, adjustment_item_id) = 1`.
- `(receipt_item_id IS NULL OR type = 'IN') AND (order_item_id IS NULL OR type = 'OUT') AND (sales_return_item_id IS NULL OR type = 'IN')`.

### 28. `daily_sales_summaries`

FR-REP: tổng hợp theo ngày, chi nhánh, kênh và SKU.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `business_date` | `date` | Có | Backend phải cung cấp |
| `branch_id` | `uuid` | Có | Backend phải cung cấp |
| `channel` | `varchar(20)` | Có | Backend phải cung cấp |
| `sku_id` | `uuid` | Có | Backend phải cung cấp |
| `sold_quantity` | `numeric(18,3)` | Có | Backend phải cung cấp |
| `returned_quantity` | `numeric(18,3)` | Có | Backend phải cung cấp |
| `revenue` | `numeric(20,2)` | Có | Backend phải cung cấp |
| `return_amount` | `numeric(20,2)` | Có | Backend phải cung cấp |
| `cost_of_goods_sold` | `numeric(20,2)` | Có | Backend phải cung cấp |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, business_date, branch_id, channel, sku_id)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, branch_id)` → `branches(tenant_id, id)`.
- `(tenant_id, sku_id)` → `skus(tenant_id, id)`.

**CHECK:**

- `channel IN ('POS', 'Admin', 'Shopee', 'TikTok', 'Lazada')`.
- `sold_quantity >= 0`.
- `returned_quantity >= 0`.
- `revenue >= 0`.
- `return_amount >= 0`.

### 29. `replenishment_suggestions`

Đề trang 6 yêu cầu AI gợi ý nhập; cấu trúc đầu ra này là phương án lưu tối thiểu, thuật toán chưa được đề chốt.

| Cột | Kiểu PostgreSQL | Bắt buộc | Mặc định / cách ghi |
|---|---|---|---|
| `id` (PK) | `uuid` | Có | `gen_random_uuid()` |
| `tenant_id` | `uuid` | Có | Backend phải cung cấp |
| `branch_id` | `uuid` | Có | Backend phải cung cấp |
| `sku_id` | `uuid` | Có | Backend phải cung cấp |
| `forecast_date` | `date` | Có | Backend phải cung cấp |
| `horizon_days` | `integer` | Có | Backend phải cung cấp |
| `predicted_quantity` | `numeric(18,3)` | Có | Backend phải cung cấp |
| `suggested_quantity` | `numeric(18,3)` | Có | Backend phải cung cấp |
| `created_at` | `timestamptz` | Có | `now()` |
| `updated_at` | `timestamptz` | Không | Backend cung cấp khi có giá trị |

**UNIQUE:** `(tenant_id, id)`; `(tenant_id, branch_id, sku_id, forecast_date, horizon_days)`.

**Quan hệ FK:**

- `(tenant_id)` → `tenants(id)`.
- `(tenant_id, branch_id)` → `branches(tenant_id, id)`.
- `(tenant_id, sku_id)` → `skus(tenant_id, id)`.

**CHECK:**

- `horizon_days > 0`.
- `predicted_quantity >= 0`.
- `suggested_quantity >= 0`.

<!-- DATABASE_DICTIONARY_END -->
