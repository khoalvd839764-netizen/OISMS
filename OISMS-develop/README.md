# Omnichannel Inventory and Sales Management System (OISM)

> **Yêu cầu từ đề bài:** Xem [docs/DE_BAI_OISM.md](docs/DE_BAI_OISM.md) để tra cứu FR/NFR, sản phẩm bàn giao, các điểm cần làm rõ và checklist nghiệm thu từ PDF đề bài. Đối chiếu tài liệu này khi thiết kế hoặc đánh giá phạm vi dự án.

> **Cập nhật ngày:** 12/10/2026  
> **Trạng thái:** Thiết lập môi trường và dựng khung sườn dự án (Scaffold & Environment Setup Only - **Chưa viết code nghiệp vụ**)

---

## 1. Tổng Quan Sơ Lược Dự Án

**OISM (Omnichannel Inventory and Sales Management System)** là giải pháp phần mềm quản lý tồn kho và bán hàng đa kênh dành cho doanh nghiệp bán lẻ:
- Kết nối đồng bộ giữa cửa hàng trực tiếp (**POS tại quầy**) và các sàn thương mại điện tử (**Shopee, TikTok Shop, Lazada**).
- Giải quyết bài toán chống bán vượt tồn (**Anti-Oversell**), tự động hóa tính giá vốn bình quân (**COGS**) và quản lý biến động kho bằng cơ chế sổ cái bất biến (**Append-Only Inventory Ledger**).
- Dự án được xây dựng theo mô hình kiến trúc sạch (**Clean Architecture**) và hướng dịch vụ đa khách thuê (**SaaS Multi-tenant**).

---

## 2. Công Nghệ Sử Dụng (Tech Stack)

| Thành phần | Công nghệ / Thư viện chính |
| :--- | :--- |
| **Backend Core** | .NET 8 (C#), ASP.NET Core Web API |
| **Kiến trúc Backend** | Clean Architecture (Domain, Application, Infrastructure, WebApi) |
| **Cơ sở dữ liệu** | PostgreSQL 16 (Entity Framework Core 8, Npgsql) |
| **Bộ nhớ đệm / Queue** | Redis 7, Hangfire (chạy tác vụ nền) |
| **Thời gian thực** | ASP.NET Core SignalR |
| **Frontend Admin** | React 19, TypeScript, Vite, Tailwind CSS / UI Libs |
| **Frontend POS** | React 19, TypeScript, Vite (PWA - Progressive Web App) |
| **Container & CI/CD** | Docker, Docker Compose, GitHub Actions |

---

## 3. Cấu Trúc Thư Mục Dự Án (Project Folder Structure)

Hiện tại toàn bộ cấu trúc thư mục đã được dựng sẵn khung sườn chuẩn hóa, phân tách rõ ràng giữa Backend và 2 ứng dụng Frontend:

```text
D:\CNPM workbase\
├── .github/
│   └── workflows/
│       └── ci.yml                 # Pipeline CI tự động kiểm tra build Backend & Frontend
├── scripts/                       # Các file script PowerShell hỗ trợ chạy nhanh
│   ├── build-all.ps1              # Kiểm tra build toàn bộ dự án
│   ├── run-backend.ps1            # Chạy nhanh Backend Web API
│   ├── run-admin.ps1              # Chạy nhanh Admin Dashboard
│   ├── run-pos.ps1                # Chạy nhanh POS App
│   └── start-docker.ps1           # Chạy toàn bộ hệ thống bằng Docker Compose
├── src/
│   ├── backend/                   # Khung sườn .NET 8 Clean Architecture
│   │   ├── OISM.sln               # Solution liên kết tất cả project backend
│   │   ├── Dockerfile             # File đóng gói container cho Web API
│   │   ├── src/
│   │   │   ├── OISM.Domain/       # Tầng Domain: Nơi định nghĩa Entities, Enums, Exceptions lõi
│   │   │   │   ├── Common/
│   │   │   │   ├── Entities/
│   │   │   │   ├── Enums/
│   │   │   │   └── Exceptions/
│   │   │   ├── OISM.Application/  # Tầng Application: Use cases, DTOs, Commands, Queries
│   │   │   │   ├── Common/
│   │   │   │   ├── Features/
│   │   │   │   └── Interfaces/
│   │   │   ├── OISM.Infrastructure/# Tầng Infrastructure: EF Core DbContext, PostgreSQL, JWT, SignalR
│   │   │   │   ├── Persistence/
│   │   │   │   └── Services/
│   │   │   └── OISM.WebApi/       # Tầng Presentation: Endpoints, Swagger UI, Controllers
│   │   │       ├── Controllers/
│   │   │       ├── Middlewares/
│   │   │       ├── Program.cs
│   │   │       └── appsettings.json
│   │   └── tests/
│   │       ├── OISM.UnitTests/    # Dự án Unit Test (xUnit, Moq, FluentAssertions)
│   │       └── OISM.IntegrationTests/ # Dự án Integration Test (WebApplicationFactory)
│   └── frontend/
│       ├── admin-dashboard/       # Web Admin Dashboard (Vite + React 19 + TypeScript)
│       │   ├── Dockerfile
│       │   ├── package.json
│       │   └── src/
│       └── pos-app/               # POS Web App PWA bán hàng tại quầy (Vite + React 19 + TypeScript)
│           ├── Dockerfile
│           ├── package.json
│           └── src/
├── docker-compose.yml             # Cấu hình chạy PostgreSQL, Redis, PgAdmin, Backend, Frontend
├── .env.example                   # Biến môi trường mẫu
├── .gitignore                     # Bỏ qua các file rác/cache của .NET và Node
└── README.md                      # Tài liệu tổng quan dự án
```

---

## 4. Hướng Dẫn Khởi Chạy Môi Trường

### Chuẩn bị file biến môi trường (.env)
Trước khi khởi chạy, copy file `.env.example` thành `.env` và cập nhật thông tin bảo mật (không commit mật khẩu thật lên Git):
```powershell
cp .env.example .env
```

### Cách 1: Khởi chạy bằng Docker Compose (Khuyên dùng)
Yêu cầu đã cài Docker Desktop. Khởi chạy toàn bộ hệ thống:
```powershell
docker compose up -d
```
Hoặc dùng script có sẵn:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\start-docker.ps1
```

- **Backend API (Swagger):** `http://localhost:5000/swagger`
- **Backend Health Check:**
  - Liveness: `http://localhost:5000/health/live`
  - Readiness (Kiểm tra kết nối DB, đủ 29 bảng, trigger, quyền runtime): `http://localhost:5000/health/ready`
- **Admin Dashboard:** `http://localhost:5173`
- **POS Web App:** `http://localhost:5174`
- **PgAdmin:** `http://localhost:5050` (Tài khoản mặc định: `admin@oisms.local` / cấu hình qua `PGADMIN_EMAIL` & `PGADMIN_PASSWORD` trong `.env`)
- **PostgreSQL 16:** `localhost:5432` (Database: `oisms`)

#### Cơ chế tự động hóa Database & Healthcheck trong Docker Compose
1. **Khởi tạo lần đầu tự động:** Khi volume `postgres_data` trống, PostgreSQL thực thi script `docker/postgres/init-oisms.sh` cùng file schema bàn giao `docs/database/oisms_schema.sql`, tự động tạo đủ 29 bảng, ràng buộc, trigger sổ cái và nhóm quyền `oisms_runtime`.
2. **Tách biệt tài khoản (Least Privilege) & Bảo mật:**
   - **Admin / DBA (`postgres`)**: Dùng cho migration, backup/restore. Bắt buộc cung cấp qua `POSTGRES_PASSWORD`.
   - **Runtime Backend (`oisms_app`)**: Thuộc nhóm `oisms_runtime`, chỉ có quyền đọc/ghi nghiệp vụ, bị trigger chặn sửa/xóa sổ cái `inventory_transactions`. Bắt buộc cung cấp qua `OISMS_APP_PASSWORD`.
   - Script khởi tạo sử dụng cơ chế truyền tham số an toàn (không nội suy chuỗi SQL), hỗ trợ mật khẩu chứa dấu nháy và ký tự đặc biệt, không ghi mật khẩu ra log, và tự động kiểm tra chặn nếu tài khoản có đặc quyền quản trị hoặc sở hữu schema/bảng.
3. **Chờ Database Healthy & Backend Readiness:**
   - Docker Compose cấu hình `backend` và `pgadmin` chờ PostgreSQL vượt qua `healthcheck` (`pg_isready`).
   - Backend cung cấp endpoint `/health/ready` thực hiện mở kết nối bằng tài khoản runtime `oisms_app`, kiểm tra đủ 29 bảng, trigger bảo vệ sổ cái và quyền hạn trước khi báo sẵn sàng (trả về HTTP 503 khi chưa sẵn sàng và không để lộ chuỗi kết nối hay mật khẩu).
4. **Lưu ý về Volume đã có dữ liệu:**
   - Docker chỉ chạy script khởi tạo khi volume `postgres_data` hoàn toàn trống.
   - Việc đổi mật khẩu trong `.env` **không** tự động đổi mật khẩu của role trên volume đã tồn tại. Đối với volume cũ, quản trị viên sử dụng lệnh `ALTER ROLE` qua tài khoản `postgres`.

#### Cấu hình kết nối Backend (.NET)
Hệ thống hỗ trợ 2 cơ chế cấu hình kết nối database:
1. **Biến môi trường riêng biệt (Mặc định trong Docker Compose - an toàn với mật khẩu có ký tự đặc biệt, dấu chấm phẩy):**
   - Chạy Backend trong Docker: `Database__Host=postgres`, `Database__Port=5432`, `Database__Database=oisms`, `Database__Username=oisms_app`, `Database__Password=<OISMS_APP_PASSWORD>`
   - Chạy Backend ở Host (Local Dev): `Database__Host=localhost`, `Database__Port=5432`, `Database__Database=oisms`, `Database__Username=oisms_app`, `Database__Password=<OISMS_APP_PASSWORD>`
2. **Connection String tổng hợp (Fallback):**
   - Docker: `ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Database=oisms;Username=oisms_app;Password=<giá_trị_OISMS_APP_PASSWORD_trong_.env>`
   - Host: `ConnectionStrings__DefaultConnection=Host=localhost;Port=5432;Database=oisms;Username=oisms_app;Password=<giá_trị_OISMS_APP_PASSWORD_trong_.env>`

#### Kiểm tra trạng thái & Log
```powershell
# Kiểm tra trạng thái các container
docker compose ps

# Theo dõi log PostgreSQL
docker compose logs -f postgres

# Theo dõi log Backend Web API
docker compose logs -f backend

# Kiểm tra kết nối và số lượng bảng (29 bảng)
docker compose exec -it postgres psql -U oisms_app -d oisms -c "SELECT count(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE';"

# Kiểm tra readiness endpoint của backend
curl http://localhost:5000/health/ready
```

> [!NOTE]
> Chi tiết tài liệu kiến trúc cơ sở dữ liệu, từ điển 29 bảng và quy trình cập nhật/migration dữ liệu xem tại [docs/database/README.md](docs/database/README.md).

---

### Cách 2: Khởi chạy thủ công từng phần (Local Dev)

#### 1. Kiểm tra build toàn bộ dự án:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-all.ps1
```

#### 2. Chạy Backend:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\run-backend.ps1
```

#### 3. Chạy Admin Dashboard:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\run-admin.ps1
```

#### 4. Chạy POS Application:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\run-pos.ps1
```
