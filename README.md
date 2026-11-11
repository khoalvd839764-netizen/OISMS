# Omnichannel Inventory and Sales Management System (OISM)

## 🌐 Link web đã deploy trên AWS

[https://oisms.khoadang.site](https://oisms.khoadang.site)

> Bản demo chạy trên một máy EC2 và có thể được tắt khi không demo để tiết kiệm chi phí. Cách deploy và lưu ý khi chạy local: xem [Triển khai lên AWS](#triển-khai-lên-aws-ec2--docker-compose).

> **Yêu cầu từ đề bài:** Xem [docs/00-overview/03-de-bai-cnpm-reference.md](docs/00-overview/03-de-bai-cnpm-reference.md) để tra cứu FR/NFR, sản phẩm bàn giao, các điểm cần làm rõ và checklist nghiệm thu từ PDF đề bài. Đối chiếu tài liệu này khi thiết kế hoặc đánh giá phạm vi dự án.

> **Cập nhật ngày:** 11/11/2026  
> **Trạng thái:** Sprint 4 hoàn thành phần lớn ([báo cáo tổng kết](docs/08-sprints/sprint-4/BAO_CAO_TONG_KET_NGHIEM_THU_SPRINT_4.md)). Đã có Upgrade 01-A, 02, 03, 04, 05, 06; Coming Soon CS-01, 03, 04, 05, 07, 08; Trợ lý OISMS (`FR-AI-02`). Còn thiếu: CS-06 (in chứng từ, in báo cáo), U01-B (hóa đơn NCC), phân công chi nhánh của CS-02. Chi tiết ở [mục Hiện trạng](#hiện-trạng-chức-năng-11112026).

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
| **Tác vụ nền** | Hangfire, lưu trên PostgreSQL (mặc định tắt, bật bằng `HANGFIRE_ENABLED`) |
| **Bộ nhớ đệm** | Redis 7 (có container, code backend chưa dùng) |
| **Thời gian thực** | ASP.NET Core SignalR |
| **Frontend Admin** | React 19, TypeScript, Vite, Tailwind CSS / UI Libs |
| **Frontend POS** | React 19, TypeScript, Vite (PWA - Progressive Web App) |
| **App giả lập sàn** | React 19, TypeScript, Vite, chỉ gọi `/api/webhooks/*` |
| **Trợ lý AI** | Groq hoặc Gemini qua API chuẩn OpenAI (`LlmRouter`), mặc định tắt (`AI_ENABLED`), chỉ dùng gói miễn phí |
| **Container & CI/CD** | Docker, Docker Compose, GitHub Actions |

---

## 3. Cấu Trúc Thư Mục Dự Án (Project Folder Structure)

Mã nguồn nằm trong `src/` (Backend .NET và 3 ứng dụng Frontend); toàn bộ test tự động gom trong `test-oisms/`:

```text
OISMS/
├── .github/
│   └── workflows/
│       └── ci.yml                     # CI: build + test Backend, build 3 Frontend, chạy npm test POS & Simulator
├── docker/
│   ├── caddy/Caddyfile                # Reverse proxy HTTPS khi deploy AWS
│   ├── pos-tests/                     # Môi trường backend/PostgreSQL cô lập cho test live của POS
│   └── postgres/
│       ├── init-oisms.sh              # Khởi tạo DB khi volume trống
│       └── migrations/                # Script nâng cấp DB đã có dữ liệu (chỉ thêm file mới)
├── docs/                              # Tài liệu: yêu cầu, kiến trúc, API, kiểm thử, deploy, sprint, báo cáo
│   └── database/oisms_schema.sql      # Nguồn cấu trúc DB hiện hành (44 bảng)
├── scripts/                           # Script PowerShell/Node hỗ trợ chạy nhanh và kiểm tra
│   ├── build-all.ps1                  # Kiểm tra build toàn bộ dự án
│   ├── run-backend.ps1 / run-admin.ps1 / run-pos.ps1
│   └── start-docker.ps1               # Chạy toàn bộ hệ thống bằng Docker Compose
├── skills/                            # Skill hướng dẫn cho AI agent (Codex/Antigravity/Claude)
├── src/
│   ├── backend/                       # .NET 8 Clean Architecture
│   │   ├── OISM.sln                   # Solution gồm 4 project nguồn + 2 project test trong test-oisms/backend
│   │   ├── Dockerfile                 # Đóng gói container Web API
│   │   └── src/
│   │       ├── OISM.Domain/           # Entity, enum, exception lõi (Common, Entities, Enums, Exceptions)
│   │       ├── OISM.Application/      # Use case theo Features/<Module>/, DTO, interface nghiệp vụ
│   │       ├── OISM.Infrastructure/   # AppDbContext, PostgreSQL, JWT, khóa SQL, job Hangfire, health check
│   │       └── OISM.WebApi/           # Controllers, Hubs (SignalR), Middlewares, Program.cs
│   └── frontend/
│       ├── admin-dashboard/           # Web quản trị (Vite + React 19 + TS), cổng 5173; /pos chỉ chuyển sang pos-app
│       ├── pos-app/                   # POS bán hàng tại quầy duy nhất, PWA (Vite + React 19 + TS), cổng 5174
│       └── marketplace-simulator/     # Giả lập bắn đơn Shopee/TikTok/Lazada qua webhook, cổng 5175
├── test-oisms/                        # Toàn bộ test tự động (xem test-oisms/README.md)
│   ├── backend/
│   │   ├── OISM.UnitTests/            # Unit test (xUnit, Moq, FluentAssertions, EF InMemory)
│   │   └── OISM.IntegrationTests/     # Integration test API (WebApplicationFactory, PostgreSQL thật)
│   ├── frontend/
│   │   ├── pos-app/                   # Test POS (node --test) + test-support/ harness dùng chung
│   │   ├── admin-dashboard/           # Test định dạng câu trả lời Trợ lý AI
│   │   └── marketplace-simulator/     # Test dựng payload webhook
│   ├── api-python/                    # Script test API trả hàng, seed dữ liệu đo báo cáo doanh thu
│   ├── ai-eval/                       # Bộ câu hỏi đánh giá Trợ lý OISMS
│   └── test-data/                     # Fixture dùng chung (dự phòng)
├── docker-compose.yml                 # PostgreSQL, Redis, PgAdmin, Backend, 3 Frontend (local)
├── docker-compose.prod.yml            # Cấu hình chạy production trên AWS EC2
├── .env.example / .env.production.example  # Biến môi trường mẫu
├── checklist.md                       # Checklist nghiệm thu toàn dự án
├── CLAUDE.md                          # Bối cảnh dự án cho Claude Code
└── README.md                          # Tài liệu tổng quan dự án
```

Chạy test:

```bash
dotnet test src/backend/OISM.sln                       # Unit + Integration (Integration cần PostgreSQL, cấu hình qua biến OISM_TEST_*)
npm test --prefix src/frontend/pos-app                 # Test POS trong test-oisms/frontend/pos-app
npm test --prefix src/frontend/marketplace-simulator   # Test simulator trong test-oisms/frontend/marketplace-simulator
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
  - Readiness (Kiểm tra kết nối DB, đủ 44 bảng, trigger, quyền runtime): `http://localhost:5000/health/ready`
- **Admin Dashboard:** `http://localhost:5173`
- **POS Web App:** `http://localhost:5174`
- **App giả lập đơn sàn:** `http://localhost:5175` (dán webhook secret tạo ở màn Kết nối sàn của Admin)
- **PgAdmin:** `http://localhost:5050` (Tài khoản mặc định: `admin@oisms.local` / cấu hình qua `PGADMIN_EMAIL` & `PGADMIN_PASSWORD` trong `.env`)
- **PostgreSQL 16:** `localhost:5432` (Database: `oisms`)

#### Cơ chế tự động hóa Database & Healthcheck trong Docker Compose
1. **Khởi tạo lần đầu tự động:** Khi volume `postgres_data` trống, PostgreSQL thực thi script `docker/postgres/init-oisms.sh` cùng file schema bàn giao `docs/database/oisms_schema.sql`, tự động tạo đủ 44 bảng, ràng buộc, trigger sổ cái và nhóm quyền `oisms_runtime`.
2. **Tách biệt tài khoản (Least Privilege) & Bảo mật:**
   - **Admin / DBA (`postgres`)**: Dùng cho migration, backup/restore. Bắt buộc cung cấp qua `POSTGRES_PASSWORD`.
   - **Runtime Backend (`oisms_app`)**: Thuộc nhóm `oisms_runtime`, chỉ có quyền đọc/ghi nghiệp vụ, bị trigger chặn sửa/xóa sổ cái `inventory_transactions`. Bắt buộc cung cấp qua `OISMS_APP_PASSWORD`.
   - Script khởi tạo sử dụng cơ chế truyền tham số an toàn (không nội suy chuỗi SQL), hỗ trợ mật khẩu chứa dấu nháy và ký tự đặc biệt, không ghi mật khẩu ra log, và tự động kiểm tra chặn nếu tài khoản có đặc quyền quản trị hoặc sở hữu schema/bảng.
3. **Chờ Database Healthy & Backend Readiness:**
   - Docker Compose cấu hình `backend` và `pgadmin` chờ PostgreSQL vượt qua `healthcheck` (`pg_isready`).
   - Backend cung cấp endpoint `/health/ready` thực hiện mở kết nối bằng tài khoản runtime `oisms_app`, kiểm tra đủ 44 bảng, trigger bảo vệ sổ cái và quyền hạn trước khi báo sẵn sàng (trả về HTTP 503 khi chưa sẵn sàng và không để lộ chuỗi kết nối hay mật khẩu).
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

# Kiểm tra kết nối và số lượng bảng (44 bảng)
docker compose exec -it postgres psql -U oisms_app -d oisms -c "SELECT count(*) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE';"

# Kiểm tra readiness endpoint của backend
curl http://localhost:5000/health/ready
```

> [!NOTE]
> Chi tiết tài liệu kiến trúc cơ sở dữ liệu, từ điển 44 bảng và quy trình cập nhật/migration dữ liệu xem tại [docs/database/README.md](docs/database/README.md).

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

---

## Triển khai lên AWS (EC2 + Docker Compose)

Toàn bộ stack chạy trên một máy EC2. Chỉ Caddy mở cổng `80`/`443` và tự cấp chứng chỉ HTTPS; PostgreSQL, Redis và backend chỉ nằm trong mạng Docker. Mỗi frontend gọi API qua chính domain của nó nên không vướng CORS.

```text
Internet → Caddy (80/443)
             ├─ oisms.khoadang.site       → admin-dashboard
             ├─ pos.oisms.khoadang.site   → pos-app
             └─ /api, /hubs, /health      → backend → postgres (cả hai domain)
```

| File | Vai trò |
| :--- | :--- |
| `docker-compose.prod.yml` | Dùng chồng lên `docker-compose.yml`: đóng cổng nội bộ, tắt pgAdmin, bắt buộc có JWT secret, thêm Caddy |
| `docker/caddy/Caddyfile` | HTTPS và định tuyến theo domain |
| `.env.production.example` | Mẫu biến môi trường cho máy chủ |

Hướng dẫn đầy đủ (hạ tầng, cập nhật, migration, backup, xử lý sự cố, kiến thức nền): [docs/13-deploy-aws/](docs/13-deploy-aws/README.md).

**Các bước deploy:**

1. Tạo EC2 Ubuntu 24.04 (tối thiểu 4 GB RAM khi build, ổ 30 GB), Security Group chỉ mở `80`, `443` và `22` cho IP của bạn; gắn Elastic IP và cài Docker.
2. Tạo bản ghi DNS loại A cho 2 domain, trỏ về Elastic IP.
3. Clone repo, chép `.env.production.example` thành `.env.production`, đổi domain và mọi giá trị `CHANGE_ME` (tạo bằng `openssl rand -hex 32`).
4. Build và chạy:

```bash
docker compose --env-file .env.production -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

5. Kiểm tra `https://<domain>/health/ready`.

**Lưu ý khi vận hành:**

- Production luôn chặn seed dữ liệu demo. Database mới trống, tài khoản đầu tiên tạo qua `POST /api/auth/register-tenant`.
- Frontend nhúng URL API lúc build, nên đổi domain thì phải chạy lại lệnh build ở trên.
- Production không bật `/swagger`.
- `init-oisms.sh` chỉ chạy khi volume database trống. Khi cập nhật hệ thống đang có dữ liệu, backup rồi chạy thủ công script trong `docker/postgres/migrations/`.
- Không commit `.env.production`. Dữ liệu nằm trên ổ của EC2, nên cần tự backup (`pg_dump`).

### Lưu ý khi chạy local

Cách chạy local không đổi, các thay đổi cho deploy không áp dụng nếu không truyền `docker-compose.prod.yml`:

- Vẫn dùng `docker compose up -d` với file `.env`. Không cần `.env.production`, Caddy không chạy, các cổng `5000`, `5173`, `5174`, `5432`, `5050` vẫn mở như cũ.
- Frontend không truyền `VITE_API_BASE_URL` thì vẫn gọi `http://localhost:5000`. Nếu tự đặt biến này thì **không** thêm đuôi `/api`, vì code tự nối `/api/...` và `/hubs/notifications`.
- Không chạy bản production và bản local cùng lúc trên một máy với cùng project Compose: hai bản dùng chung tên container và volume, nên có thể ghi đè nhau.
- `init-oisms.sh` kiểm tra đúng 44 bảng; database cũ thiếu bảng thì `/health/ready` trả 503 "schema is incomplete", cần chạy các script còn thiếu trong `docker/postgres/migrations/`. Volume đang có dữ liệu không bị ảnh hưởng; không cần chạy `docker compose down -v`, vì lệnh đó xóa toàn bộ dữ liệu local.

---

## Hiện trạng chức năng (11/11/2026)

| Nhóm | Đã có | Còn thiếu / lưu ý |
| :--- | :--- | :--- |
| Nền tảng | Multi-tenant, JWT, RBAC 3 vai trò (`Owner`, `Staff`, `Cashier`), sổ cái append-only, WAC, giữ chỗ chống bán vượt, idempotency | — |
| Vận hành kho | Phiếu mua → phiếu nhập (U01-A), chuyển kho 2 bước, kiểm kê theo lịch và duyệt chênh lệch (U03), đa đơn vị tính (U05), bảng giá theo thời gian (U04) | U01-B (hóa đơn NCC, chênh lệch) chưa làm |
| Bán hàng | Đơn đa kênh, POS riêng `pos-app` có VietQR (CS-05), hóa đơn bán và truy vết xuất kho (U02), trả hàng (CS-01), webhook sàn và app giả lập | Xác nhận thanh toán QR tự động qua ngân hàng chưa làm |
| Quản trị | Nhân viên (CS-02), ngưỡng cảnh báo tồn (CS-03), tìm kiếm toàn hệ thống (CS-04), KPI Tổng quan (CS-07), gợi ý nhập hàng (CS-08), tài khoản nhận tiền | CS-02 chưa phân công chi nhánh; CS-06 in chứng từ, in báo cáo chưa làm |
| AI | Trợ lý OISMS (`POST /api/ai/chat`) cho Owner, Staff, chỉ đọc, trả lời theo tài liệu | Mặc định tắt khi chạy local; bản demo AWS đã bật từ 11/11/2026. Hạn mức gói miễn phí rất thấp |
| Tác vụ nền | Hết hạn giữ chỗ, sinh kỳ kiểm kê | Hangfire mặc định tắt khi chạy local; bản demo AWS đã bật từ 11/11/2026. Chưa có job tổng hợp báo cáo hằng ngày |

Kiểm thử gần nhất (11/11/2026): unit backend 380/380 PASS, `npm test` POS 89/89 PASS. Integration trên PostgreSQL 372/372 PASS ngày 07/11/2026.

Tài liệu nên đọc: [docs/README.md](docs/README.md), [checklist lỗi và Coming Soon](docs/bug-fixaffter/CHECKLIST.md), [RTM](docs/06-testing-and-qa/01-requirements-matrix-rtm.md), [rà soát docs Sprint 4](docs/08-sprints/sprint-4/ra-soat-lech-docs-2026-11-11.md).

---

## 5. Nghiệm Thu & Bàn Giao Kỹ Thuật Sprint 2 (F1 – F7)

> Mốc lịch sử Sprint 2 (19/10/2026), giữ nguyên tại thời điểm nghiệm thu. Số test và đường dẫn trong mục này không còn là hiện trạng.

Toàn bộ 7 vấn đề kỹ thuật từ F1 đến F7 đã được khắc phục triệt để và nghiệm thu thực tế:

| Mã | Hạng mục & Phụ trách | Vấn đề ban đầu | Giải pháp kỹ thuật đã triển khai & Kiểm chứng | Kết quả kiểm thử thực tế | Trạng thái |
| :---: | :--- | :--- | :--- | :--- | :---: |
| **F1** | Frontend Admin<br>*(Phúc Hữu)* | Đóng/mở form làm mất ID phiếu nhập chờ xác nhận; retry tạo phiếu trùng | Lưu thông tin phiếu và ID vào `sessionStorage` (`oism_pending_receipt_${tenantId}`). Mở lại modal tự động đối soát `GET /api/goods-receipts/{id}`. Đổi nhãn nút thành `"Bỏ theo dõi phiếu này / Tạo phiếu mới"`, giải thích rõ chứng từ vẫn lưu Draft trên server. | - Build pass 100%<br>- Live PostgreSQL test: chỉ 1 phiếu Confirmed, OnHand tăng đúng 10, không tạo phiếu trùng | **ĐẠT** |
| **F2** | Backend + Frontend<br>*(Đăng Khoa + Phúc Hữu)* | Client gọi reserve không truyền OrderId/OrderItemId; retry tạo reservation mới và tăng Reserved | **Backend (Đăng Khoa):** Hỗ trợ `RequestKey` UUID qua body và headers `Idempotency-Key` / `X-Request-Key`. Advisory Lock `(tenantId, RequestKey)` chống race condition. Cùng key & payload trả 200 OK giữ nguyên reservation; khác payload trả 409 Conflict; cách ly đa tenant.<br>**Frontend (Phúc Hữu):** Quản lý UUID RFC4122 chuẩn; lưu pending reservation và pending order vào `sessionStorage`; áp dụng cả Checkout OrdersPage và Modal Kho; bypass client optimistic balance check khi retry (`isRetry: true`). | - 18/18 Backend integration tests pass<br>- 21/21 Live PostgreSQL checks pass<br>- 12/12 Frontend logic checks pass<br>- 5 concurrent requests giữ nguyên `Reserved = 9` | **ĐẠT** |
| **F3** | Frontend Admin<br>*(Phúc Hữu)* | Bảng kho giữ số dư cũ khi đổi kho hoặc lỗi API riêng của kho đang lọc | Refetch kho đang chọn sau mutation. Kiểm tra `whRes?.error`: nếu lỗi, reset ngay `warehouseBalances = []` và bật `warehouseBalanceError` kèm nút thử lại; không hiển thị số dư kho cũ. Dùng `balanceReqSeqRef` chống race condition. | - Frontend logic test: isolate warehouse error pass<br>- Sequence counter loại trừ phản hồi cũ | **ĐẠT** |
| **F4** | Frontend Admin<br>*(Phúc Hữu)* | API sổ kho lỗi nhưng hiển thị "Chưa có bút toán" | Tách biệt trạng thái `ledgerError` khỏi trạng thái danh sách rỗng. Hiển thị banner lỗi màu đỏ và nút `"Thử lại tải sổ kho"` độc lập. | - Fault injection HTTP 500: hiển thị banner lỗi đúng chuẩn, không nhầm empty state | **ĐẠT** |
| **F5** | Frontend Admin<br>*(Phúc Hữu)* | Lọc theo tên chứa chuỗi nên chọn "Kho" dính cả "Kho 2" | Chuyển sang so sánh chính xác theo `warehouseId` (`item.warehouseId === warehouseFilter`). Dropdown hiển thị đầy đủ `[Chi nhánh] — [Kho hàng]`. | - Test so sánh ID: không còn tình trạng dính chéo dòng kho con | **ĐẠT** |
| **F6** | Frontend Admin<br>*(Phúc Hữu)* | Bộ tải phân trang chỉ lấy tối đa 1.000 dòng (10 trang) | Loại bỏ giới hạn cứng 10 trang trong `fetchAllPages`. Tải toàn bộ `totalPages` theo batch song song (5 request/lượt). | - Diagnostic tải thành công 2.345 dòng qua 24 trang đầy đủ | **ĐẠT** |
| **F7** | Docker / Nginx<br>*(Đăng Khoa)* | Truy cập trực tiếp route con (`/login`, `/inventory`,...) hoặc F5 trả 404 Not Found | Tạo `nginx.conf` với SPA fallback `try_files $uri $uri/ /index.html;` và rule chặn file tĩnh thiếu `try_files $uri =404;`. Tích hợp `COPY nginx.conf` vào Dockerfile của `admin-dashboard` và `pos-app`, rebuild container image. | - Curl HTTP 200 cho `/`, `/login`, `/inventory`, `/products`, `/403`<br>- Curl HTTP 404 cho asset tĩnh không tồn tại | **ĐẠT** |

### Kết Quả Kiểm Thử Thực Tế:
- **Build Frontend (`npm run build`):**
  - `admin-dashboard`: Hoàn thành trong ~300ms, 0 lỗi TypeScript, 0 lỗi build.
  - `pos-app`: Hoàn thành trong ~250ms, 0 lỗi TypeScript, 0 lỗi build.
- **Backend Tests (`dotnet test src/backend/OISM.sln`):** **194/194 tests Passed 100%** (103 Unit Tests + 91 Integration Tests trên PostgreSQL thật).
- **Kiểm thử Liên thông PostgreSQL Thật (`scratch/verify_s2_fixes.js`):** **21/21 checks Passed 100%**.
- **Kiểm thử Logic Frontend (`scratch/verify_frontend_logic.js`):** **12/12 checks Passed 100%**.
- **Kiểm thử Docker / Nginx Routing:** SPA fallback HTTP 200, missing asset HTTP 404.
- **Tuyên bố Minh bạch về Giới hạn Kiểm chứng:** Đã kiểm chứng đạt trên tầng build, unit/integration test, API thật và container HTTP routing. Trạng thái đối với thao tác trực quan trên trình duyệt (E2E Browser): **`CHỜ XÁC MINH UI BROWSER`** (do công cụ browser tự động bị đóng trong phiên làm việc). Khuyến nghị QA/Tester cùng phối hợp nghiệm thu trực quan trên browser trước khi release.

