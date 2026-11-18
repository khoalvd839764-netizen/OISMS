# Nguồn và invariant OISMS

Đường dẫn tính từ root repo. Chỉ đọc hàng liên quan. Thứ tự ưu tiên khi đối chiếu: yêu cầu của người dùng → kế hoạch Sprint hiện hành và đặc tả nâng cấp → FR/NFR, API contract → tài liệu kiến trúc.

| Việc | Nguồn |
| --- | --- |
| Chọn task, phân công Sprint 4 | `docs/08-sprints/sprint-4/README.md`, `docs/bug-fixaffter/CHECKLIST-BUGFIX-AND-UPGRADE.md`, `checklist.md` |
| Chức năng CS-01..08 | `docs/bug-fixaffter/Comming Soon function/COMING-SOON.md` |
| Upgrade 01..06 | `docs/Upgrade-recommended/<số>-*.md`; kết quả Upgrade 06: `docs/report-after-upgrade/clean-architecture.md` |
| Yêu cầu chức năng / phi chức năng | `docs/02-functional-requirements/fr-*.md`, `docs/03-non-functional-requirements/` |
| Khái niệm nghiệp vụ | `docs/00-overview/02-core-domain-concepts.md` |
| Kiến trúc, multi-tenant, ERD, state machine | `docs/04-architecture-and-design/` (tên class cũ: `ApplicationDbContext` → `AppDbContext`, `ITenantContext` → `ICurrentTenantService`) |
| Schema | `docs/database/oisms_schema.sql` |
| API, RBAC, lỗi, màu | `docs/05-api-contracts/`, `docs/01-users-and-rbac/02-rbac-matrix.md`, `docs/12-error-and-status-codes/` |
| UI | `docs/11-ui-screens/` |
| Test, DoD | `docs/06-testing-and-qa/` |

## Invariant theo tác động

| Phần bị tác động | Phải giữ |
| --- | --- |
| Tầng kiến trúc | Controller chỉ lo HTTP và phân quyền, không dùng `AppDbContext`, không mở transaction. Use case ở `OISM.Application/Features`. SQL đặc thù PostgreSQL ở Infrastructure sau interface |
| Tenant | Lấy từ JWT qua `ICurrentTenantService`, không tin client. Global Query Filter. Truy vấn chéo tenant chỉ trong `OpenPrivilegedScope()` |
| Kho | `InventoryTransaction` chỉ INSERT; không sửa `on_hand` trực tiếp; sai thì ghi bút toán bù. `Available = OnHand − Reserved`, không âm |
| Giá vốn | WAC qua `WacCalculator`; snapshot `CostPrice` khi xuất bán |
| Transaction, đồng thời | `CreateExecutionStrategy()`, khóa qua `ISqlLockService` sau khi mở transaction, thứ tự lock thống nhất với luồng cũ |
| Retry | Idempotency qua `Idempotency-Key` / `X-Request-Key`; frontend giữ nguyên requestKey khi retry |
| API | `[Authorize(Roles = ...)]` đúng ma trận; lỗi `ProblemDetails` + `errorCode` |
| Báo cáo | Múi giờ Asia/Saigon, khoảng nửa mở |
| Schema | Cập nhật `oisms_schema.sql`, EF Configuration và `RequiredTables` trong `DatabaseMigrationService.cs`; tương thích dữ liệu cũ |

## Thay đổi → docs phải cập nhật

Luật cứng: đổi gì thì cập nhật docs đó trong cùng task, kèm báo cáo. Chỉ sửa docs hiện hành, không sửa tài liệu lịch sử (`docs/08-sprints/`, `docs/09-personal-reports/`, báo cáo cũ trong `docs/report-after-upgrade/`).

| Thay đổi | Docs |
| --- | --- |
| Bảng, cột, ràng buộc, trigger | `docs/database/oisms_schema.sql`, `docs/database/README.md`, `docs/04-architecture-and-design/03-database-design.md`; số bảng ở `CLAUDE.md`, `docs/README.md`, `docs/07-devops-and-deployment/01-docker-compose-guide.md`, `docs/13-deploy-aws/04-quy-trinh-deploy-va-cap-nhat.md` (kèm bước chạy migration) |
| Hành vi nghiệp vụ, kho, giá vốn | `docs/02-functional-requirements/fr-*.md` liên quan, `docs/00-overview/02-core-domain-concepts.md` nếu đổi khái niệm |
| Trạng thái chứng từ | `docs/04-architecture-and-design/04-state-machines.md` |
| Endpoint, request/response | `docs/05-api-contracts/` |
| Mã lỗi | `docs/12-error-and-status-codes/02-error-codes-catalog.md` |
| Quyền | `docs/01-users-and-rbac/02-rbac-matrix.md` |
| Màn hình | `docs/11-ui-screens/` |
| Mọi thay đổi trên | Báo cáo mới `docs/report-after-upgrade/<tên>.md`: đã làm, ảnh hưởng chức năng hiện có, thay đổi DB và cách nâng cấp DB đang chạy, kiểm chứng, giới hạn |
| Sau khi VERIFIED (Claude làm) | `checklist.md`, `docs/Upgrade-recommended/` (Pedding/Done, README), `docs/bug-fixaffter/CHECKLIST.md` nếu có dòng liên quan |

## Lệnh kiểm chứng

```bash
dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal
dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false
dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --filter 'FullyQualifiedName~<TênClassTest>'
cd src/frontend/admin-dashboard && npm run build
cd src/frontend/pos-app && npm run build && npm run lint && npm test
```

Test PostgreSQL thật cần biến `OISM_TEST_*` (xem `test-oisms/backend/OISM.IntegrationTests/PostgresTestEnvironment.cs`).
