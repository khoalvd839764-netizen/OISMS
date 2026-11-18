# Báo cáo sau nâng cấp: Phân công chi nhánh cho nhân viên — CS-02 (OISMS-59-G1B)

| Mục | Nội dung |
| --- | --- |
| Task | `OISMS-59-G1B` (`CS-02`) |
| Ngày cập nhật | 14/11/2026 |
| Phạm vi | Database schema (48 bảng), Backend (`Domain`, `Application`, `Infrastructure`, `WebApi`), Frontend (`admin-dashboard`, `pos-app`), Integration/Unit/POS Tests, Tài liệu kỹ thuật |
| Trạng thái | Hoàn tất triển khai và chạy kiểm chứng tự động (chờ review) |

## Đã làm

1. **Cơ sở dữ liệu & Entity (`48 bảng`):**
   - Thêm entity `UserBranch` (`src/backend/src/OISM.Domain/Entities/UserBranch.cs`, triển khai `IMustHaveTenant`) và cấu hình EF Core `UserBranchConfiguration` (`src/backend/src/OISM.Infrastructure/Persistence/Configurations/UserBranchConfiguration.cs`) với khóa chính ghép `(tenant_id, user_id, branch_id)`, các khóa ngoại ghép tenant (`fk_user_branches_tenant`, `fk_user_branches_user` `ON DELETE CASCADE`, `fk_user_branches_branch`) và chỉ mục `ix_user_branches_branch (tenant_id, branch_id)`.
   - Khai báo `DbSet<UserBranch> UserBranches` trong `IAppDbContext` và `AppDbContext` kèm global tenant query filter.
   - Bổ sung block `-- OISMS_USER_BRANCHES_BEGIN ... -- OISMS_USER_BRANCHES_END` vào `docs/database/oisms_schema.sql`, tạo migration idempotent `docker/postgres/migrations/20261114_user_branches.sql` (cấp quyền `SELECT, INSERT, UPDATE, DELETE` cho `oisms_runtime`), và cập nhật kiểm tra 48 bảng trong `docker/postgres/init-oisms.sh` và `src/backend/src/OISM.Infrastructure/Persistence/DatabaseMigrationService.cs` (`RequiredTables`, `ExpectedForeignKeys`, `UpgradeDatabaseSchemaAsync`, `crucialColumns`).

2. **API quản lý nhân viên & lọc chi nhánh / checkout (`Application` & `WebApi`):**
   - `UserDtos` & `UserService`: bổ sung `BranchIds` trong `UserResponse`, `CreateUserRequest`, `UpdateUserRequest`. Khi `POST /api/users` hoặc `PUT /api/users/{id}`, nếu có `branchIds`, kiểm tra toàn bộ ID tồn tại và thuộc tenant hiện tại (sai trả `400 BRANCH_NOT_FOUND`, không ghi DB); khi `PUT` truyền `branchIds == null` thì giữ nguyên gán cũ, truyền mảng thì thay thế toàn bộ trong cùng `SaveChangesAsync`; vai trò `Owner` luôn bỏ qua `branchIds` và xóa mọi gán cũ.
   - Thêm `IBranchAccessService` / `BranchAccessService` (đăng ký trong `OISM.Application/DependencyInjection.cs`) với `GetAssignedBranchIdsAsync` và `EnsureCashierBranchAllowedAsync` (chặn `403 BRANCH_NOT_ASSIGNED` nếu `Cashier` có bản ghi `user_branches` và `branchId` nằm ngoài danh sách).
   - `BranchController.GetBranches` & `BranchService.GetBranchesAsync`: chỉ lọc danh sách chi nhánh theo `user_branches` khi người gọi là `Cashier` và đã có ít nhất 1 chi nhánh được gán.
   - `PosCheckoutActionHelper.ExecuteCheckoutAsync` (dùng chung cho `POST /api/pos/checkout` và alias `POST /api/orders/checkout` ở `PosCatalogController` và `OrdersController`) gọi `EnsureCashierBranchAllowedAsync` **trước** khi gọi `IPosCheckoutService.CheckoutPosAsync`, không thay đổi logic transaction/lock bên trong `OrderService`. Bổ sung mapping `"BRANCH_NOT_ASSIGNED"` vào `ApiErrorTitles.cs`.

3. **Frontend (`admin-dashboard` & `pos-app`):**
   - `admin-dashboard` (`src/services/apiAdapter.ts`, `src/pages/OrganizationPage.tsx`): bảng nhân viên bổ sung cột **Chi nhánh phân công** hiển thị `"Tất cả chi nhánh"` khi `branchIds` rỗng (hoặc `Owner`); modal Thêm/Sửa nhân viên hiển thị checkbox chọn nhiều chi nhánh cho `Staff` và `Cashier`, tự ẩn khi vai trò là `Owner`.
   - `pos-app` (`src/services/posApi.ts`, `src/components/pos/PaymentModal.tsx`): xử lý rõ mã lỗi `403 BRANCH_NOT_ASSIGNED` khi checkout, xóa `pendingCheckout` và hiển thị thông báo rõ cho thu ngân, không fallback vào nhánh retry. Dropdown chi nhánh sử dụng danh sách đã lọc từ `GET /api/branches`.

4. **Tài liệu & kiểm thử:**
   - Cập nhật mốc **48 bảng** và mô tả `user_branches` tại `CLAUDE.md`, `docs/README.md`, `docs/database/README.md`, `docs/04-architecture-and-design/03-database-design.md`, `docs/07-devops-and-deployment/01-docker-compose-guide.md`, `docs/13-deploy-aws/04-quy-trinh-deploy-va-cap-nhat.md`.
   - Cập nhật đặc tả API, mã lỗi, RBAC và màn hình tại `docs/05-api-contracts/01-auth-endpoints.md`, `docs/05-api-contracts/05-pos-endpoints.md`, `docs/12-error-and-status-codes/02-error-codes-catalog.md`, `docs/01-users-and-rbac/02-rbac-matrix.md`, `docs/11-ui-screens/09-organization-screens.md`, `docs/11-ui-screens/06-pos-terminal-screens.md`.
   - Thêm `test-oisms/backend/OISM.IntegrationTests/UserBranchAssignmentIntegrationTests.cs` (PostgreSQL thật), cập nhật `UserServiceTests.cs`, cập nhật assertion số bảng `47 -> 48` trong `DatabaseBaselineValidatorTests.cs` và `PostgresTenantIsolationTests.cs`, và bổ sung ca kiểm thử `CS-02 POS` trong `test-oisms/frontend/pos-app/test-checkout.mjs`.

## Ảnh hưởng chức năng hiện có

- **Tương thích ngược cho tài khoản cũ:** Nhân viên (`Staff` và `Cashier`) chưa có dòng gán nào trong `user_branches` (`branchIds = []`) vẫn nhìn thấy và thao tác trên mọi chi nhánh như trước đây. Tài khoản `Owner` không gắn giới hạn chi nhánh.
- **`Cashier` đã được phân công chi nhánh:**
  - `GET /api/branches` chỉ trả về các chi nhánh nằm trong danh sách được gán (dropdown POS tự hiển thị danh sách đã lọc này).
  - `POST /api/pos/checkout` và `POST /api/orders/checkout` gửi `branchId` ngoài danh sách sẽ trả `403 Forbidden` với `errorCode = "BRANCH_NOT_ASSIGNED"`, không tạo đơn hàng, không ghi `inventory_transactions` và không thay đổi tồn kho.
- **`Staff` đã được phân công chi nhánh:** Danh sách `branchIds` được lưu/hiển thị trên `/api/users` và màn Tổ chức Admin, nhưng các API đọc/ghi nghiệp vụ khác của `Staff` **chưa** bị lọc hay chặn theo chi nhánh ở đợt này.

## Thay đổi cơ sở dữ liệu & Cách nâng cấp DB đang chạy

- **Schema mới:** 48 bảng (`+1` bảng `public.user_branches`). File `docs/database/oisms_schema.sql` dùng cho khởi tạo volume PostgreSQL mới qua `docker/postgres/init-oisms.sh`.
- **Nâng cấp DB đã có dữ liệu (47 bảng từ baseline U01-B):**
  1. Sao lưu DB trước khi nâng cấp bằng `pg_dump -Fc`.
  2. Chạy migration idempotent bằng tài khoản quản trị schema (`postgres`), **không** chạy lại `oisms_schema.sql`:
     ```bash
     psql -X -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 -f docker/postgres/migrations/20261114_user_branches.sql
     ```
     (Nếu DB đang ở baseline cũ hơn 47 bảng, chạy tuần tự các file trong `docker/postgres/migrations/*.sql` theo thứ tự tên file.)
  3. Trong code test/hạ tầng, `DatabaseMigrationService.UpgradeDatabaseSchemaAsync` đọc đoạn `OISMS_USER_BRANCHES_BEGIN/END` từ `oisms_schema.sql` và đã được kiểm chứng chạy lặp lại 2 lần khớp hoàn toàn với schema tạo mới.
  4. Sau khi khởi động backend mới, kiểm tra `GET /health/ready` trả HTTP `200`, `tablesCount: 48`.

## Kiểm chứng

Các lệnh kiểm chứng thực tế sau lần chỉnh sửa cuối cùng:

| Lệnh | Phạm vi / Môi trường | Kết quả |
| --- | --- | --- |
| `dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal` | Toàn bộ solution backend (`net8.0`) | PASS (0 Warning, 0 Error) |
| `dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --no-build -v:minimal --filter 'FullyQualifiedName~UserBranchAssignmentIntegrationTests\|FullyQualifiedName~PostgresTenantIsolationTests\|FullyQualifiedName~PosCheckoutIntegrationTests\|FullyQualifiedName~BranchAndSupplierIntegrationTests\|FullyQualifiedName~SalesInvoiceTraceIntegrationTests'` | 5 bộ integration test (PostgreSQL thật `localhost:5432` cho CS-02, TenantIsolation, PosCheckout, SalesInvoiceTrace; InMemory cho BranchAndSupplier) | PASS 64/64 (Failed: 0, Skipped: 0) |
| `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false --no-build -v:minimal` | Toàn bộ UnitTests | PASS 381/381 (Failed: 0, Skipped: 0) |
| `npm run build --prefix src/frontend/admin-dashboard` | `tsc -b && vite build` | PASS |
| `npm run build --prefix src/frontend/pos-app && npm run lint --prefix src/frontend/pos-app && npm test --prefix src/frontend/pos-app` | `pos-app` build (`vite`), `oxlint` (5 cảnh báo sẵn có, 0 lỗi), Node test runner | PASS (build OK, lint 0 lỗi, 90/90 test PASS) |

## Giới hạn

- **Phạm vi lọc dữ liệu:** Theo thiết kế CS-02 đợt này, chỉ mới áp dụng kiểm tra/lọc chi nhánh cho `Cashier` tại `GET /api/branches` và POS checkout (`POST /api/pos/checkout`, `POST /api/orders/checkout`); `Staff` mới lưu danh sách gán chưa lọc dữ liệu, và giao diện Admin chưa giới hạn dữ liệu theo chi nhánh.
- **Kiểm thử UI Admin:** `admin-dashboard` mới được kiểm chứng qua `tsc -b && vite build`; chưa chạy kiểm thử giao diện tự động (browser E2E) cho cột và modal checkbox phân công chi nhánh trên `OrganizationPage`.
- **Phạm vi suite integration:** Đợt này chạy 5 class integration liên quan (`64` ca); các suite integration độc lập khác không sửa đổi trong G1B không chạy lại toàn bộ trong một lượt.
