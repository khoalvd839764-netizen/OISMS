# OISMS — bối cảnh cho Claude Code

Hệ thống quản lý tồn kho và bán hàng đa kênh (POS, Shopee, TikTok Shop, Lazada), SaaS multi-tenant, là đồ án môn CNPM.
Bản demo: `oisms.khoadang.site` (Admin), `pos.oisms.khoadang.site` (POS), chạy trên một máy EC2.

Trả lời bằng tiếng Việt. Docs là yêu cầu, code và test là hiện trạng. Nếu chúng mâu thuẫn về nghiệp vụ thì báo lại để người dùng quyết định, không tự đổi yêu cầu.

## Cấu trúc

| Đường dẫn | Nội dung |
| --- | --- |
| `src/backend/OISM.sln` | .NET 8, ASP.NET Core, EF Core 8, Npgsql, PostgreSQL 16 |
| `src/backend/src/OISM.Domain` | Entity, enum, exception |
| `src/backend/src/OISM.Application` | `Features/<Module>/` chứa service, DTO, interface nghiệp vụ |
| `src/backend/src/OISM.Infrastructure` | `AppDbContext`, JWT, BCrypt, `SqlLockService`, job Hangfire |
| `src/backend/src/OISM.WebApi` | Controller, `NotificationHub`, middleware lỗi, `Program.cs` |
| `src/backend/tests` | `OISM.UnitTests`, `OISM.IntegrationTests` |
| `src/frontend/admin-dashboard` | React 19 + Vite, cổng 5173; có cả POS tích hợp tại `/pos` (`PosPage.tsx`) |
| `src/frontend/pos-app` | POS PWA riêng, cổng 5174 |
| `docs/database/oisms_schema.sql` | Nguồn cấu trúc DB hiện hành |
| `docker/postgres/migrations/` | Script nâng cấp DB đã có dữ liệu |

Không sửa và không tìm code trong `backend/` (chỉ còn thư mục build, không có trong git), `scratch/` và `tmp/` (bản snapshot review, có cả `OISM.sln` cũ). Khi tìm kiếm nên loại các thư mục này cùng `bin`, `obj`, `node_modules`.

## Lệnh

```bash
dotnet build src/backend/OISM.sln
dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj --filter "FullyQualifiedName~<TenTest>"
```

Một phần integration test cần PostgreSQL thật và các biến `OISM_TEST_HOST`, `OISM_TEST_PORT`, `OISM_TEST_ADMIN_USER`, `OISM_TEST_ADMIN_PASSWORD`, `OISM_TEST_APP_USER`, `OISM_TEST_APP_PASSWORD`. Xem cách đặt trong `.github/workflows/ci.yml`.

```bash
npm run build --prefix src/frontend/admin-dashboard
npm run build --prefix src/frontend/pos-app
npm test --prefix src/frontend/pos-app
npm run lint --prefix src/frontend/pos-app
```

Chạy local: `docker compose up -d`, hoặc `scripts/run-backend.ps1`, `run-admin.ps1`, `run-pos.ps1`. Backend ở cổng 5000, có `/swagger` khi Development và `/health/ready`.
CI chạy khi push hoặc mở PR vào `main`, `master`, `develop`: build và test backend, build hai frontend, chạy `npm test` của POS.

## Quy tắc không được phá

- **Tenant isolation:** `TenantId` chỉ lấy từ JWT đã xác thực (`CurrentTenantService`), không lấy từ header hoặc body. Bảng mới phải có `tenant_id` và query filter trong `AppDbContext`. `IgnoreQueryFilters` và `OpenPrivilegedScope` chỉ dùng cho luồng chưa có JWT như đăng ký, đăng nhập, webhook.
- **Sổ cái append-only:** `inventory_transactions` chỉ được INSERT, trigger `ledger_append_only` chặn UPDATE/DELETE. Không sửa `on_hand` mà không ghi sổ. Sửa sai bằng bút toán bù.
- **Tồn kho:** `Available = OnHand - Reserved`, không bao giờ âm.
  - Reserve: tăng `Reserved`, chưa ghi sổ.
  - Confirm hoặc POS checkout: trừ `OnHand`, ghi `OUT`, chụp `OrderItem.CostPrice`.
  - Hủy hoặc hết hạn trước khi xuất: chỉ giảm `Reserved`, không ghi `IN` bù.
- **Giá vốn WAC:** tính lại khi xác nhận phiếu nhập: `(tồn cũ × giá cũ + SL nhập × giá nhập) / (tồn cũ + SL nhập)`. Giá bán niêm yết tách riêng khỏi giá vốn.
- **Nhiều đơn vị tính:** tồn và WAC luôn theo đơn vị gốc. `BaseQuantity = Quantity × FactorSnapshot`; giá vốn nhập là `UnitPrice / FactorSnapshot`.
- **Giao dịch và đồng thời:** thao tác kho nguyên tử trong một transaction, lấy khóa sau khi mở transaction (`FOR UPDATE`, `pg_advisory_xact_lock`) và giữ thứ tự khóa như luồng cũ. Sự kiện SignalR phát sau commit.
- **Idempotency:** thao tác ghi quan trọng dùng `RequestKey` hoặc `Idempotency-Key`: cùng key và payload trả kết quả cũ, khác payload trả 409. Frontend giữ nguyên key khi retry lúc kết quả chưa rõ.
- **Trạng thái đơn:** `Draft → Reserved → Confirmed → Completed`; `Draft` hoặc `Reserved → Cancelled`. Xem `docs/04-architecture-and-design/04-state-machines.md`.
- **Phân quyền:** ba vai trò `Owner`, `Staff`, `Cashier`. Báo cáo tài chính và trường giá vốn phải kiểm tra quyền ở cả dữ liệu trả về.
- **Clean Architecture:** controller điều phối HTTP, Application điều phối nghiệp vụ qua `IUnitOfWork` và `ISqlLockService`. SQL khóa và cách thực thi transaction nằm trong Infrastructure. Application vẫn tham chiếu EF Core cho truy vấn và lưu entity.
- **Múi giờ báo cáo:** ngày nghiệp vụ theo `UTC+07:00`, lọc theo khoảng nửa mở `CreatedAt < endBoundary`.

## Database

- Không dùng EF migrations. Mỗi thay đổi schema gồm: một file mới trong `docker/postgres/migrations/` (không sửa file cũ), cập nhật `oisms_schema.sql`, thêm bảng vào `DatabaseMigrationService.RequiredTables` trong Infrastructure, sửa số bảng kiểm tra trong `docker/postgres/init-oisms.sh`.
- Không chạy `oisms_schema.sql` lên DB đã có dữ liệu. `init-oisms.sh` chỉ chạy khi volume trống; rebuild không nâng cấp schema. `/health/ready` trả 503 "schema is incomplete" nghĩa là còn thiếu migration.
- Không tự chạy `docker compose down -v` vì lệnh này xóa dữ liệu local.
- Backend chạy bằng tài khoản `oisms_app` ít quyền; `postgres` chỉ dùng cho migration và backup.

## Điểm cần biết

- **Docs cũ:** tài liệu lịch sử (sprint, báo cáo cá nhân) còn ghi 29 bảng; hiện là **43 bảng** (`oisms_schema.sql`, `init-oisms.sh`, `RequiredTables`). Docs kiến trúc còn ghi `ApplicationDbContext`, `OISM.WebAPI` thay vì `AppDbContext`, `OISM.WebApi`.
- **Tiến độ:** hiện trạng frontend xem `docs/report-after-upgrade/frontend-coming-soon-va-upgrade.md`. Còn thiếu CS-01, CS-05 ở POS Admin, CS-06, U01-B và phân công chi nhánh của CS-02. Vẫn phải kiểm tra code trước khi kết luận chức năng còn thiếu. Dấu `[x]` Sprint 2 chỉ là baseline lịch sử, không phải nghiệm thu toàn sản phẩm.
- **Tạm ẩn:** tab Gợi ý nhập hàng được comment trong `InventoryPage.tsx`, API vẫn chạy. Màn Handoff đã gỡ nhưng đặc tả `docs/11-ui-screens/07-handoff-screens.md` còn giữ. Quên mật khẩu mới là nút tắt có nhãn Coming soon.
- **Hangfire:** `Hangfire:Enabled` mặc định `false` và compose không bật, nên job hết hạn giữ chỗ và lịch kiểm kê không tự chạy trừ khi cấu hình.
- **Redis:** có container nhưng code backend chưa dùng.
- **POS đa đơn vị:** `posCartUnits.ts` có một bản trong mỗi app, sửa thì sửa cả hai; `pos-app/test-cart-units.mjs` chạy cả hai bản. Giá theo đơn vị phải lấy từ `/api/pos/search` hoặc `/api/sku-unit-prices/effective`, vì `salePrice` trong `/api/products` có thể rỗng; không tự tính giá theo hệ số.
- **Quyền xem:** Staff được xem giá vốn ở truy vết và doanh thu ở Tổng quan theo `docs/01-users-and-rbac/02-rbac-matrix.md`; chỉ lãi gộp là Owner.
- **JWT:** `Program.cs` có secret fallback viết cứng. Môi trường thật phải đặt `Jwt:Secret`; không dựa vào fallback.
- **Deploy:** frontend nhúng URL API lúc build. `VITE_API_BASE_URL` không thêm đuôi `/api`. Production không có Swagger và không seed demo.
- **Bí mật:** không đọc, in hay commit `.env`, `.env.production`, file dump.

## CodeGraph

Có `.codegraph/` và CLI `codegraph` (`query`, `explore`, `context`, `callers`, `impact`). Chỉ mục gồm cả backend C# và frontend; vẫn đối chiếu source trước khi kết luận.

- **Bắt buộc giữ `codegraph.json`:** thư mục `backend/` ở gốc repo chỉ có `bin`/`obj` nên bị git ignore cả thư mục, và CodeGraph vì thế bỏ qua mọi thư mục tên `backend`, kể cả `src/backend`. Dòng `include` đưa `src/backend/` trở lại; dòng `exclude` loại `scratch/` (profile trình duyệt, bản snapshot).
- Nếu `AppDbContext` không tìm thấy hoặc số file C# tụt xuống, kiểm tra `codegraph.json` trước.
- `codegraph index` báo không dừng được daemon: chạy `codegraph daemon` để dừng rồi thử lại, hoặc đợi daemon tự thoát. Không tự index lại khi chưa được yêu cầu.

## Git và quy trình

- Nhánh: `main`, `develop` (nhánh tích hợp), `feature/OISMS-<id>-<mô-tả>`. PR thường vào `develop`.
- Commit: `[OISMS-<id>] <type>(<scope>): <mô tả tiếng Việt>`, ví dụ `[OISMS-47] feat(stock-count): lịch kiểm kê và duyệt chênh lệch`.
- Không thêm `Co-Authored-By: Claude` vào commit và không thêm dòng "Generated with Claude Code" vào PR.
- Thay đổi chạm auth, tenant, migration, transaction, ledger, WAC, reservation, idempotency hoặc concurrency là rủi ro cao: phải có bằng chứng trên PostgreSQL thật. Mock và InMemory không chứng minh transaction hay concurrency; build pass không có nghĩa là tính năng chạy đúng.
- Khi báo kết quả, ghi rõ lệnh đã chạy và tách PASS, FAIL, NOT RUN. Không nới assertion hoặc bỏ test để có PASS.
- Quy trình Codex–Antigravity (`skills/codex-antigravity-workflow/`) chỉ áp dụng khi được yêu cầu.

## Đọc thêm khi cần

| Chủ đề | Tài liệu |
| --- | --- |
| Yêu cầu chức năng | `docs/02-functional-requirements/` |
| Khái niệm nghiệp vụ | `docs/00-overview/02-core-domain-concepts.md` |
| Kiến trúc, trạng thái | `docs/04-architecture-and-design/` |
| API, mã lỗi | `docs/05-api-contracts/`, `docs/12-error-and-status-codes/` |
| Từ điển dữ liệu | `docs/database/README.md` |
| Kiểm thử, DoD | `docs/06-testing-and-qa/` |
| Bug, Coming soon | `docs/bug-fixaffter/` |
| Nâng cấp nghiệp vụ | `docs/Upgrade-recommended/`, `docs/report-after-upgrade/` |
| Màn hình | `docs/11-ui-screens/` |
| Sprint, báo cáo cá nhân | `docs/08-sprints/`, `docs/09-personal-reports/` |
