# Báo cáo sau nâng cấp: Job tổng hợp báo cáo ngày và giao diện đối soát (OISMS-59-G3B)

| Mục | Nội dung |
| --- | --- |
| Task | `OISMS-59-G3B` (Gói 3 Phase B + C — Prompt 1/3) |
| Ngày kiểm tra | 15/11/2026 |
| Phạm vi | Domain (`DailySalesSummary`), Persistence (`DailySalesSummaryConfiguration`, `AppDbContext`, `SqlLockService`), Application (`RevenueProfitCalculationHelper`, `ReportService`, `DailySalesSummaryService`, DTOs), Infrastructure (`DailySalesSummaryJob`, `HangfireConfigurationExtensions`), WebApi (`ReportsController`, `appsettings.json`, Error contracts), Frontend (`ReportsPage.tsx`, `reportService.ts`, `types/reports.ts`), UnitTests + Postgres IntegrationTests + Docker AC-08 |
| Trạng thái | Hoàn tất triển khai, kiểm thử và kiểm chứng Docker (chờ review) |

## Đã làm

1. **Entity, cấu hình EF Core & Global Tenant Query Filter (AC-01):**
   - `OISM.Domain.Entities.DailySalesSummary`: ánh xạ đầy đủ các cột của bảng `daily_sales_summaries` (`business_date`, `branch_id`, `channel`, `sku_id`, `sold_quantity`, `returned_quantity`, `revenue`, `return_amount`, `cost_of_goods_sold`, khóa kép `(tenant_id, id)`).
   - `DailySalesSummaryConfiguration` & `AppDbContext`: cấu hình kiểu PostgreSQL (`date`, `numeric(18,3)`, `numeric(20,2)`), hai unique index `uq_daily_sales_summaries_1` và `uq_daily_sales_summaries_2`, quan hệ FK và `HasQueryFilter` theo `_currentTenantService.TenantId`.

2. **Tách helper dùng chung & dịch vụ tổng hợp `DailySalesSummaryService` (AC-02, AC-03):**
   - Tách `RevenueProfitCalculationHelper` chứa toàn bộ quy tắc ngày kinh doanh UTC+07, lọc nửa mở `[StartDate, EndDate)`, truy vấn đơn `Confirmed`/`Completed` theo `CreatedAt`, phiếu trả `Confirmed` theo `ConfirmedAt`, quy đổi `BaseQuantity` và giá vốn từ `OrderItem.CostPrice` (`COGS bán - COGS trả`).
   - Refactor `ReportService` (`GetRevenueProfitReportAsync`, `GetDashboardSummaryAsync`) dùng chung `RevenueProfitCalculationHelper`, giữ nguyên kết quả báo cáo trực tiếp.
   - `DailySalesSummaryService.RebuildAsync(tenantId, businessDate)`: chạy qua `IUnitOfWork.ExecuteWithRetryAsync`, mở 1 transaction, khóa `pg_advisory_xact_lock(tenant, ngày)` qua `ISqlLockService.LockDailySalesSummaryAsync` **sau** khi mở transaction, xóa dòng ngày cũ và chèn lại tập dòng mới (không ghi dòng toàn `0`).

3. **Hangfire Job `oisms-daily-sales-summary` & API `Owner` (AC-04, AC-05):**
   - `DailySalesSummaryJob`: quét `Tenants.Where(t => t.IsActive)` trong `OpenPrivilegedScope().IgnoreQueryFilters().AsNoTracking()`, tạo DI scope riêng cho từng tenant, tổng hợp cửa sổ `Reports:SummaryRebuildDays` ngày (mặc định `7` ngày tính từ hôm qua theo UTC+07). Lỗi ở một tenant được ghi nhận riêng và tiếp tục các tenant còn lại trước khi ném `AggregateException`.
   - `HangfireConfigurationExtensions.UseOismHangfire`: đăng ký job id `oisms-daily-sales-summary`, cron `30 0 * * *`, múi giờ `Asia/Ho_Chi_Minh` (`SE Asia Standard Time` / fallback `UTC+07`).
   - `ReportsController`: bổ sung `GET /api/reports/daily-summaries` và `POST /api/reports/daily-summaries/rebuild` gắn `[Authorize(Roles = "Owner")]` (`Staff` và `Cashier` nhận `403 Forbidden`), kiểm tra tenant từ JWT, giới hạn khoảng tính lại tối đa `31` ngày và trả lỗi theo RFC 7807 `ProblemDetails` + `errorCode` (`VALIDATION_ERROR`).

4. **Giao diện đối soát trên Admin Dashboard (AC-07):**
   - `src/frontend/admin-dashboard/src/pages/ReportsPage.tsx`, `services/reportService.ts`, `types/reports.ts`: thêm khu vực **"Tổng hợp theo ngày"** hiển thị bảng từng ngày (`Ngày`, `Doanh thu TH/TT`, `Hàng trả TH/TT`, `Giá vốn TH/TT`, `Lãi gộp TH/TT`, trạng thái `Khớp`/`Lệch`/`Chưa tổng hợp`, `Cập nhật lúc`) và nút **"Tính lại"** theo khoảng lọc (khóa khi đang chạy, hiển thị lỗi `ProblemDetails` + `errorCode`, không dùng mock data).

5. **Kiểm thử Unit & Integration PostgreSQL thật (AC-06):**
   - `test-oisms/backend/OISM.UnitTests/DailySalesSummaryUnitTests.cs`: kiểm chứng validation khoảng ngày `> 31`, chặn cross-tenant, cô lập lỗi theo tenant trong `DailySalesSummaryJob`, và cấu hình cron `30 0 * * *` múi giờ `+07:00`.
   - `test-oisms/backend/OISM.IntegrationTests/DailySalesSummaryIntegrationTests.cs`: đối chiếu metadata PostgreSQL `information_schema.columns`, tenant query filter, kịch bản 2 tenant + đa chi nhánh + đa đơn vị (`FactorSnapshot = 24`) + đơn tạo ngày `D` xác nhận ngày `D+2` + hàng trả `D+1` + đơn hủy + idempotency 2 lần + chạy song song 4 luồng không trùng/lỗi unique + RBAC `Owner`/`Staff`/`Cashier` và `ProblemDetails`.

6. **Cập nhật tài liệu:**
   - `docs/02-functional-requirements/fr-10-background-hangfire.md`, `fr-11-reports-analytics.md`
   - `docs/05-api-contracts/04-order-endpoints.md`
   - `docs/01-users-and-rbac/02-rbac-matrix.md`
   - `docs/11-ui-screens/08-report-screens.md`
   - `docs/database/README.md` (mục 28)
   - `docs/07-devops-and-deployment/03-environment-configuration.md` (`Reports:SummaryRebuildDays`)

## Ảnh hưởng chức năng hiện có

- **Không đổi kết quả báo cáo trực tiếp hay luồng đơn/kho:** không sửa logic tạo/xác nhận/hủy đơn hay giữ chỗ tồn kho; `GET /api/reports/revenue-profit` dùng chung `RevenueProfitCalculationHelper` và tiếp tục cho kết quả khớp toàn bộ test hiện hữu (`ReportServiceTests`, `PostgresSalesReturn`, `MultiUnitFlow`).

## Thay đổi cơ sở dữ liệu

- **Không tạo migration hay sửa schema:** sử dụng bảng `daily_sales_summaries` đã có sẵn trong `docs/database/oisms_schema.sql`. Cột `cost_of_goods_sold` lưu giá vốn thuần (`COGS bán - COGS trả`), phù hợp cấu trúc hiện tại không đặt `CHECK >= 0` trên cột này.

## Kiểm chứng

| Hạng mục | Phạm vi / Provider | Kết quả |
| --- | --- | --- |
| `dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal` | Toàn bộ backend solution | PASS (0 warning, 0 error) |
| `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false --no-build` | UnitTests (gồm `ReportServiceTests`, `DailySalesSummaryUnitTests`, `KnowledgeFilesTests`) | PASS 385/385 |
| `dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --no-build --filter "FullyQualifiedName~DailySalesSummary\|FullyQualifiedName~PostgresSalesReturn\|FullyQualifiedName~MultiUnitFlow\|FullyQualifiedName~ArchitectureRules"` | PostgreSQL thật (`PostgresTestEnvironment`, `WebApplicationFactoryWithPostgres`) | PASS 28/28 (Failed: 0, Skipped: 0) |
| `npm run build --prefix src/frontend/admin-dashboard` | Frontend Admin Dashboard (`tsc -b && vite build`) | PASS |
| Docker (`HANGFIRE_ENABLED=true`) — AC-08 | Container `oism_backend`, `oism_postgres`, `oism_admin_dashboard` (`http://localhost:5173/reports`) | Job `oisms-daily-sales-summary` (`LastJobId = 401`, `Succeeded`) ghi 2 dòng `2026-11-13` (`1.495.000 đ`) & `2026-11-14` (`897.000 đ`), khớp 100% với `GET /api/reports/revenue-profit`; bấm **Tính lại** trên UI cập nhật đủ 3 dòng (`2026-11-13..2026-11-15`) về trạng thái `Khớp`. Ảnh chụp lưu cục bộ trên máy kiểm thử, không đưa vào repo. |

### Sửa sau review (15/11/2026)

- **So khớp theo sai số làm tròn:** `GetComparisonAsync` ban đầu so bằng tuyệt đối nên ngày có giá vốn WAC 6 chữ số lẻ bị báo `Lệch` sai (bảng tổng hợp làm tròn từng dòng). Nay cho phép chênh `0,01 × số dòng tổng hợp`. Test `GetComparisonAsync_WacCostWithSixDecimals_RoundedPerRow_ShouldStillBeMatched` FAIL trước khi sửa, PASS sau khi sửa; trường hợp lệch thật vẫn được kiểm trong `DailySummaryEndpoints_EnforceOwnerOnly_ProblemDetails_AndStatusTransitions`.
- **Cô lập test hết hạn giữ chỗ:** `ReservationExpiryIntegrationTests.AC05_TenantBackgroundIsolation` FAIL khi chạy chung với test mới vì con trỏ tenant tĩnh của `ReservationExpiryJob` còn từ class chạy trước. Thêm `ReservationExpiryJob.ResetTenantCursor()` vào `InitializeAsync`; không đổi logic job.
- Chạy lại: UnitTests PASS 386/386; IntegrationTests PostgreSQL (`DailySalesSummary`, `PostgresSalesReturn`, `MultiUnitFlow`, `ArchitectureRules`, `OnlineOrderLifecycle`, `ReservationExpiry`, `PostgresOrderLifecycleAtomicity`) PASS 77/77; `npm run build` admin PASS.

## Giới hạn

- `daily_sales_summaries` gom theo `(tenant_id, business_date, branch_id, channel, sku_id)`, nên khi người dùng chọn bộ lọc `Kênh bán` trên `ReportsPage`, phần đối soát `Tổng hợp theo ngày` vẫn so sánh tất cả kênh (có hiển thị dòng lưu ý trên UI).
- Dữ liệu kiểm chứng AC-08 trên Docker DB gồm 3 đơn `Completed` (`ORD-DOCKER-1113..1115`) để minh họa đủ cửa sổ job 7 ngày và ngày hiện tại.
