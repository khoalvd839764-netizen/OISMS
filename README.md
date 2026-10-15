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

### Cách 1: Khởi chạy bằng Docker Compose (Khuyên dùng)
Yêu cầu đã cài Docker Desktop:
```powershell
docker compose up -d
```
- **Backend API (Swagger):** `http://localhost:5000/swagger`
- **Admin Dashboard:** `http://localhost:5173`
- **POS Web App:** `http://localhost:5174`
- **PgAdmin:** `http://localhost:5050` (Tài khoản: `hong@oism.local` / Mật khẩu: `hong`)
- **PostgreSQL:** `localhost:5432`

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
