# TÀI LIỆU BÀN GIAO & KẾT QUẢ TRIỂN KHAI BACKEND 1 (KHOA -> NHỰT)
**Dự án:** OISMS - Omnichannel Inventory & Sales Management System  
**Sprint:** Sprint 2  
**Người thực hiện:** Đặng Khoa (Backend 1 / Persistence & Security Lead)  
**Người tiếp nhận:** Nhựt (Backend 2 / Auth & Catalog API Lead)  
**Ngày hoàn thiện:** 17/10/2026  
**Trạng thái kiểm thử:** **105/105 Tests Pass 100%** (47 Unit Tests, 58 Integration Tests trên PostgreSQL thật)

---

## 1. Mục tiêu & Phạm vi hoàn thành của Backend 1

Backend 1 đã khắc phục hoàn toàn toàn bộ các điểm yếu và toàn bộ lỗi phát hiện sau review hệ thống:
1. **Sửa Migration cài Database mới hoàn toàn (`DatabaseMigrationService`):**
   - Loại bỏ các lệnh psql meta-commands (`\set`, `\connect`) và `CREATE DATABASE`.
   - Quản lý transaction ở một nơi duy nhất: loại bỏ `BEGIN;` và `COMMIT;` độc lập cấp cao nhất trong file SQL, sử dụng `NpgsqlTransaction` trong C# quản lý khối DDL + ghi nhận baseline nguyên tử (ACID), hỗ trợ rollback 100% khi lỗi. Bảo tồn đầy đủ từ khóa `BEGIN` trong thân hàm PL/pgSQL.
   - Tìm kiếm file `oisms_schema.sql` động thông qua biến môi trường `OISM_SCHEMA_SQL_PATH` hoặc duyệt ngược cây thư mục từ `BaseDirectory`/`CurrentDirectory`, độc lập môi trường và CI.
   - Đã kiểm chứng tự động cài mới thành công từ database hoàn toàn trống.
2. **Cách ly bộ Test khỏi PostgreSQL làm việc (`PostgresTestEnvironment`):**
   - Sử dụng database test chuyên dụng (`oisms_test`) và role test riêng (`oisms_test_app`), tách biệt khỏi database làm việc `oisms`.
   - Cơ chế bảo vệ mục tiêu nghiêm ngặt (`AssertSafeTestTarget`): Chặn đứng ngay lập tức mọi mutation/truncate trên database làm việc (`oisms`), database hệ thống (`postgres`) hoặc bất kỳ database nào không chứa từ khóa `test`.
   - Sử dụng bảng marker xác thực `_oisms_test_fixture_marker` kèm `RunId` ngẫu nhiên cho mỗi lượt chạy: `ResetTestDataAsync` và `CleanupTestDatabaseAsync` chỉ tương tác với tài nguyên thuộc quyền sở hữu của chính lượt chạy đó. Từ chối trước mọi mutation đối với database có sẵn hoặc mang marker lạ.
   - Bảo toàn role có sẵn: Nếu role đã tồn tại trong PostgreSQL cluster, fixture giữ nguyên và không tự ý đổi mật khẩu hay thu hồi quyền.
   - Xử lý SQL an toàn qua `EscapeSqlIdentifier` và `EscapeSqlLiteral`, ngăn ngừa hoàn toàn SQL injection và lỗi parse với mật khẩu chứa ký tự đặc biệt (`' " # ; & $`).
3. **Kiểm tra tiếp nhận Baseline toàn diện & Chặn đứng Schema Drift (`DatabaseMigrationService`):**
   - **Trigger (Sửa Bug B):** Truy vấn trực tiếp `pg_trigger.tgtype` (bitmask) để kiểm tra đầy đủ: mức ROW vs STATEMENT (`tgtype & 1`), Timing (`BEFORE` vs `AFTER`), từng sự kiện độc lập (`INSERT`, `UPDATE`, `DELETE`, `TRUNCATE`), schema hàm (`public`), tên hàm (`oisms_deny_mutation`), trạng thái enabled (`tgenabled IN ('O', 'A')`) và đặc tính deferred. Kiểm tra điều kiện `WHEN` qua `pg_get_expr(t.tgqual, t.tgrelid)` (ngăn chặn bypass qua `WHEN (false)`) và giới hạn cột qua `unnest(t.tgattr)`. Phát hiện và chặn đứng ngay lập tức nếu `ledger_append_only` bị bỏ sót sự kiện `DELETE`, thêm điều kiện WHEN hoặc giới hạn cột.
   - **Foreign Key:** Truy vấn `pg_constraint` (`contype = 'f'`) xác minh: schema/bảng/cột nguồn và đích, thứ tự cột, hành vi `ON DELETE` (`confdeltype = 'r'` - RESTRICT), `ON UPDATE` (`confupdtype = 'a'` - NO ACTION), đặc tính `condeferrable = false` và `convalidated = true`. Chặn đứng ngay lập tức nếu FK bị đổi sang `ON DELETE CASCADE` (`confdeltype = 'c'`).
   - **CHECK & UNIQUE Constraints (Sửa Bug A):** Truy vấn `pg_get_expr(c.conbin, c.conrelid)` lấy biểu thức giải mã chuẩn của PostgreSQL. Chuẩn hóa qua bộ quét ký tự `NormalizeExpression`: bảo toàn nguyên vẹn literal string `'...'` (kể cả escape `''`) và quoted identifier `""`, chỉ chuẩn hóa keyword/operator bên ngoài; so sánh bằng `StringComparison.Ordinal`. Chặn đứng ngay lập tức nếu CHECK bị sửa điều kiện (VD: cho phép `retail_price = -1`), sai case trong chuỗi (VD: `'owner'` thay vì `'Owner'`), UNIQUE bị sai thứ tự cột, hoặc ràng buộc bị đánh dấu `NOT VALID` (`convalidated = false`).
   - **Từ chối phi phá hủy (Non-destructive rejection):** Khi phát hiện sai lệch, hệ thống trả về thông báo chẩn đoán đích danh thành phần sai lệch, TUYỆT ĐỐI không ghi nhận vào `__EFMigrationsHistory` và bảo toàn 100% dữ liệu nghiệp vụ trên database.
4. **Khóa chặt chính sách Seed tài khoản Demo (`DemoSeedPolicy`):**
   - Chỉ cho phép seed demo trên môi trường `Development` và `Test` khi được bật chủ động (`Database:SeedDemoData = true`).
   - Khóa cứng (`hard lockout`) tuyệt đối trên `Production`, `Staging` và các môi trường không xác định, bất kể cờ cấu hình.
   - Bắt buộc cấu hình mật khẩu demo rõ ràng qua `Database:DemoSeedPassword`; ném `InvalidOperationException` nếu thiếu hoặc chuỗi rỗng/khoảng trắng. Không dùng mật khẩu mặc định hardcode.
5. **Chặn cập nhật / xóa chéo tenant bằng detached entity:**
   - Chuyển đổi toàn bộ khóa chính sang Composite Key `(TenantId, Id)` trên tất cả thực thể có `IMustHaveTenant`. Đảm bảo câu lệnh SQL do EF Core sinh ra luôn chứa mệnh đề vật lý `WHERE tenant_id = @p0 AND id = @p1`, ngăn chặn triệt để tấn công đổi ID hay tráo `TenantId`.
6. **Đóng các đường bỏ qua Tenant Guard:**
   - Mặc định mọi thao tác ghi (`SaveChanges`) khi thiếu Tenant Context (`CurrentTenantId == null`) đều bị ném `SecurityException`.
   - Bao phủ toàn diện cả 4 overload của `SaveChanges` (`SaveChanges()`, `SaveChanges(bool)`, `SaveChangesAsync(ct)`, `SaveChangesAsync(bool, ct)`).
   - Thiết kế cơ chế **Privileged Scope (`IAppDbContext.OpenPrivilegedScope()`)** an toàn cho các tác vụ tin cậy nội bộ (đăng ký tenant, đăng nhập lưu refresh token, seeder) mà không mở lỏng bảo vệ toàn cục.

---

## 2. Chi tiết thay đổi Model & Mapping Entity (Dành cho Nhựt)

### 2.1. Composite Primary Key `(TenantId, Id)`
Toàn bộ 10 thực thể có `IMustHaveTenant` hiện tại sử dụng khóa chính ghép:
```csharp
builder.HasKey(x => new { x.TenantId, x.Id });
```
**Danh sách 10 thực thể áp dụng:**
1. `User` (bảng `users`)
2. `RefreshToken` (bảng `refresh_tokens`)
3. `Branch` (bảng `branches`)
4. `Warehouse` (bảng `warehouses`)
5. `Category` (bảng `categories`)
6. `Brand` (bảng `brands`)
7. `Product` (bảng `products`)
8. `ProductVariant` (bảng `skus`)
9. `Supplier` (bảng `suppliers`)
10. `InventoryBalance` (bảng `inventory_balances`)

> [!IMPORTANT]
> **Tác động đến Code của Nhựt khi truy vấn:**
> - Khi dùng `_context.FindAsync<T>(id)`: Vì khóa chính là composite `(TenantId, Id)`, nếu gọi `FindAsync(id)` bằng 1 tham số sẽ bị EF Core báo lỗi thiếu key.
> - **Cách làm chuẩn:** Luôn truy vấn qua LINQ với `FirstOrDefaultAsync` hoặc `FirstAsync`:
>   ```csharp
>   // ĐÚNG: Tự động hưởng lợi từ Global Query Filter theo CurrentTenantId
>   var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == productId);
>   var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
>   ```
> - Nếu bắt buộc dùng `FindAsync`: Phải truyền đủ 2 tham số:
>   ```csharp
>   var supplier = await _context.Suppliers.FindAsync(currentTenantId, supplierId);
>   ```

### 2.2. Mapping bảng và cột theo PostgreSQL Schema v0.5
| Entity C# | Bảng Postgres v0.5 | Lưu ý đặc biệt |
| :--- | :--- | :--- |
| `User` | `users` | - Vai trò lưu tại cột `role varchar(20)` (`'Owner'`, `'Staff'`, `'Cashier'`). Không còn bảng `roles` hay `user_roles`.<br>- Tên hiển thị là cột `display_name` (thay cho `FullName`).<br>- Số điện thoại lưu cột `phone` chuẩn hóa E.164 (VD: `+84912345678`). |
| `RefreshToken` | `refresh_tokens` | - Lưu `token_hash varchar(128)` (SHA-256 hash).<br>- Không có cột `updated_at`.<br>- FK kép `(tenant_id, user_id)` tham chiếu `users(tenant_id, id)`. |
| `Tenant` | `tenants` | - Bảng chỉ có `id`, `name`, `is_active`, `created_at`, `updated_at`.<br>- Thuộc tính `Code` là `[NotMapped]`. |
| `ProductVariant` | `skus` | - Ánh xạ bảng `skus`.<br>- Thuộc tính `SKU` map vào cột `code` (chuẩn hóa `upper(btrim(code))`).<br>- Thuộc tính `AttributesJson` map vào cột `attributes jsonb`.<br>- `VariantName`, `Color`, `Size` là unmapped (helper đọc/ghi JSON).<br>- FK kép `(tenant_id, product_id)` tham chiếu `products(tenant_id, id)`. |
| `InventoryBalance`| `inventory_balances` | - Tồn theo chi nhánh và SKU (`tenant_id, branch_id, sku_id`).<br>- Có trigger nghiêm ngặt `stock_consistency`: `on_hand` bắt buộc phải khớp với tổng `warehouse_inventory_balances` và ledger sequence `inventory_transactions`. Không được tự ý `UPDATE` trực tiếp cột `on_hand`. |

---

## 3. Hướng dẫn sử dụng Tenant Guard & Privileged Scope

### 3.1. Luồng API Nghiệp vụ thông thường (Authenticated)
Khi request có header `Authorization: Bearer <jwt_token>`:
1. `CurrentTenantService` tự động trích xuất `tenant_id` từ Claims của JWT.
2. `AppDbContext` tự động áp dụng Global Query Filter: chỉ đọc bản ghi thuộc tenant đó.
3. Khi gọi `SaveChangesAsync()`:
   - Các thực thể thêm mới sẽ tự động được gán `TenantId = CurrentTenantId`.
   - Nếu client cố tình gửi `TenantId` khác: Ném `SecurityException`.
   - EF Core sinh lệnh UPDATE / DELETE kèm `WHERE tenant_id = @currentTenantId AND id = @id`.

### 3.2. Luồng nội bộ / Auth / Unauthenticated (Privileged Scope)
Đối với các endpoint như Đăng ký Tenant mới, Đăng nhập, Refresh Token hoặc Seeder hệ thống (nơi chưa có JWT bearer token của tenant):
1. **Đọc dữ liệu (User/Tenant):** Bắt buộc dùng `.IgnoreQueryFilters()`:
   ```csharp
   var user = await _context.Users
       .IgnoreQueryFilters()
       .FirstOrDefaultAsync(u => u.Email == emailNormalized);
   ```
2. **Ghi dữ liệu (Tạo Tenant, User, RefreshToken):** Bắt buộc kích hoạt `OpenPrivilegedScope()`:
   ```csharp
   using var privilegedScope = _context.OpenPrivilegedScope();
   
   // Khi ở trong scope này, AppDbContext cho phép ghi entity có TenantId hợp lệ mà không cần JWT
   _context.Tenants.Add(tenant);
   _context.Users.Add(user);
   await _context.SaveChangesAsync();
   ```
3. **Thao tác có Transaction:** Vì Npgsql đã bật `EnableRetryOnFailure`, cần bọc transaction trong `CreateExecutionStrategy()`:
   ```csharp
   var strategy = _context.Database.CreateExecutionStrategy();
   await strategy.ExecuteAsync(async () =>
   {
       using var privilegedScope = _context.OpenPrivilegedScope();
       await using var tx = await _context.Database.BeginTransactionAsync();

       // Thao tác ghi DB
       await _context.SaveChangesAsync();

       await tx.CommitAsync();
   });
   ```

### 3.3. Viết Unit / Integration Test
Khi viết test controller hoặc test repository:
- **Nếu test API có JWT:** Client chỉ cần đính kèm Bearer token (như trong `Postgres_EndToEnd_SmokeTest_RegisterLoginCreateSkuReadSku`).
- **Nếu test service trực tiếp bằng `AppDbContext`:**
  - Để test ngữ cảnh tenant A: Mock hoặc gán `tenantService.SetTenant(tenantAId)`.
  - Để khởi tạo dữ liệu mồi (seed/fixture): Dùng `using (context.OpenPrivilegedScope()) { ... await context.SaveChangesAsync(); }`.

---

## 4. Hướng dẫn Cấu hình Môi trường & Chạy Test

### 4.1. Biến cấu hình trong `appsettings.json` / Environment Variables
Hệ thống hỗ trợ cả 2 định dạng cấu hình:
```json
{
  "Database": {
    "Host": "localhost",
    "Port": 5432,
    "Database": "oisms",
    "Username": "oisms_app",
    "Password": "your_secure_password",
    "SeedDemoData": false,
    "DemoSeedPassword": "YourDemoPassword@123"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=oisms;Username=oisms_app;Password=your_secure_password"
  }
}
```

> [!CAUTION]
> **Quy định an ninh:**
> - Tài khoản runtime ứng dụng bắt buộc là `oisms_app` (thuộc group `oisms_runtime`). Tuyệt đối không cấu hình user `postgres` cho API backend.
> - Cấu hình `Database:SeedDemoData` mặc định là `false`. Chỉ bật `true` trong môi trường local dev khi cần tạo 2 shop mẫu (`Shop A` và `Shop B`).

### 4.2. Khởi chạy PostgreSQL bằng Docker
```powershell
# Khởi động dịch vụ postgres
docker compose up -d postgres

# Kiểm tra container đã healthy
docker inspect --format='{{.State.Health.Status}}' oism_postgres
```

### 4.3. Chạy kiểm thử tự động
```powershell
# 1. Chạy toàn bộ 37 bài test của solution (Unit Tests + In-Memory + PostgreSQL thật)
dotnet test src/backend/OISM.sln -l "console;verbosity=normal"

# 2. Chạy riêng bộ kiểm thử cô lập Tenant và PostgreSQL
dotnet test --filter "FullyQualifiedName~PostgresTenantIsolationTests" src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -l "console;verbosity=normal"
```

---

## 5. Danh sách các bài Test đã Kiểm chứng Đạt 100% (99/99 Tests)

### 5.1. Integration Tests trên PostgreSQL thật (`OISM.IntegrationTests` - 55 tests pass 100%)
| Tên bài Test | Mục đích & Cơ chế kiểm chứng | Kết quả |
| :--- | :--- | :--- |
| `Postgres_BaselineAdoption_WhenCheckConstraintAllowsNegativeRetailPrice_MustRejectWithDetailedDiagnostic` | **[MỚI]** Tái hiện lỗi CHECK giữ nguyên tên nhưng sửa điều kiện cho phép giá âm (>= -1): Baseline phát hiện sai lệch biểu thức qua `pg_get_expr` và `NormalizeExpression`, từ chối tiếp nhận, không ghi migration history, bảo toàn dữ liệu. | **PASS** |
| `Postgres_BaselineAdoption_WhenLedgerTriggerDropsDeleteEvent_MustRejectWithDetailedDiagnostic` | **[MỚI]** Tái hiện lỗi trigger `ledger_append_only` giữ nguyên hàm nhưng bỏ sự kiện `DELETE`: Baseline kiểm tra bitmask `tgtype`, phát hiện thiếu DELETE, từ chối tiếp nhận với chẩn đoán rõ ràng. | **PASS** |
| `Postgres_BaselineAdoption_WhenForeignKeyAlteredToCascade_MustRejectWithDetailedDiagnostic` | **[MỚI]** Tái hiện lỗi FK giữ nguyên bảng/cột nhưng đổi `ON DELETE CASCADE`: Baseline kiểm tra `confdeltype`, phát hiện sai lệch ('c' thay vì 'r'), từ chối tiếp nhận và không ghi baseline. | **PASS** |
| `Postgres_BaselineAdoption_WhenConstraintNotValidated_MustRejectWithDetailedDiagnostic` | **[MỚI]** Ràng buộc được tạo với `NOT VALID` (`convalidated = false`): Baseline phát hiện trạng thái chưa validate và từ chối tiếp nhận. | **PASS** |
| `Postgres_TestEnvironment_PreExistingDatabase_WithoutMarker_MustReject_AndPreserveData` | Database có sẵn từ trước chứa từ khóa "test", có bản ghi sentinel, chưa có marker: Reset bị từ chối; không tự tạo marker; sentinel và schema giữ nguyên 100%. | **PASS** |
| `Postgres_TestEnvironment_ForeignOrFakeMarker_MustReject` | Database chứa marker của run khác hoặc marker giả: Bị từ chối trước mọi thao tác mutation. | **PASS** |
| `Postgres_TestEnvironment_OwnedDatabase_InitializeResetCleanup_ShouldSucceed` | Vòng đời tài nguyên do chính fixture tạo: Tự động sinh `RunId`, khởi tạo schema, reset dữ liệu và teardown dọn dẹp hoạt động chính xác. | **PASS** |
| `Postgres_TestEnvironment_PreExistingRole_MustNotChangePasswordOrGrantExtraRoles` | Role đã tồn tại sẵn từ trước ngoài phạm vi fixture không bị đổi mật khẩu và không bị cấp thêm quyền. | **PASS** |
| `Postgres_BaselineAdoption_WhenFkSkusProductIdMissing_MustRejectWithDriftError` | Khi thiếu khóa ngoại `fk_skus_product_id`, baseline bị từ chối và thông báo chính xác tên FK thiếu. | **PASS** |
| `Postgres_BaselineAdoption_WhenFkDefinitionOmitsTenantId_MustRejectWithDriftError` | Khi FK `fk_skus_product_id` thiếu `tenant_id` (chỉ tham chiếu `product_id -> products(id)`), baseline bị từ chối do sai lệch cột bảo vệ tenant isolation. | **PASS** |
| `Postgres_BaselineAdoption_WhenRoleCheckLiteralCaseDrifts_MustRejectWithDetailedDiagnostic` | **[MỚI - SỬA BUG A]** Tái hiện lỗi CHECK constraint bị đổi sang chữ thường `'owner'`, validator phát hiện sai biểu thức kiểm tra và từ chối tiếp nhận baseline an toàn. | **PASS** |
| `Postgres_BaselineAdoption_WhenLedgerTriggerHasWhenClause_MustRejectWithDetailedDiagnostic` | **[MỚI - SỬA BUG B]** Tái hiện lỗi trigger `ledger_append_only` bị thêm `WHEN (false)`, validator phát hiện điều kiện WHEN trái phép và từ chối tiếp nhận baseline. | **PASS** |
| `Postgres_BaselineAdoption_WhenLedgerTriggerHasColumnRestrictions_MustRejectWithDetailedDiagnostic` | **[MỚI - SỬA BUG B]** Tái hiện lỗi trigger `ledger_append_only` bị giới hạn theo danh sách cột (`UPDATE OF`), validator phát hiện vi phạm và từ chối tiếp nhận baseline. | **PASS** |
| `Postgres_BaselineAdoption_WhenWarehouseStockConsistencyDisabled_MustRejectWithDriftError` | Khi trigger `stock_consistency` trên `warehouse_inventory_balances` bị vô hiệu hóa, baseline bị từ chối và báo rõ bảng/trigger. | **PASS** |
| `Postgres_BaselineAdoption_WhenReservationOrLedgerTriggersMissing_MustRejectWithDriftError` | Khi thiếu trigger bảo vệ trên `reservations` (`stock_lock`), baseline bị từ chối ngay lập tức. | **PASS** |
| `Postgres_BaselineAdoption_WhenCheckConstraintMissing_MustRejectWithDriftError` | Khi thiếu check constraint bảo vệ tồn kho không âm `ck_inventory_balances_on_hand_nonnegative`, baseline bị từ chối. | **PASS** |
| `Postgres_BaselineAdoption_WhenRejected_NoMigrationRecorded_AndDataPreserved` | Khi baseline bị từ chối do drift: không ghi nhận bản ghi nào vào `__EFMigrationsHistory`, không sửa đổi database đích, dữ liệu nghiệp vụ giữ nguyên vẹn. | **PASS** |
| `Postgres_Migration_FreshInstall_OnCleanDatabase_ShouldSucceed_AndPreserveAllObjects` | Khởi tạo schema v0.5 từ database trống qua `DatabaseMigrationService`: bóc tách psql, C# `NpgsqlTransaction` quản lý nguyên tử, bảo tồn `BEGIN` trong PL/pgSQL, đủ 29 bảng, trigger `ledger_append_only` và `__EFMigrationsHistory`. | **PASS** |
| `Postgres_BaselineAdoption_WhenLedgerTriggerDisabled_MustRejectWithDriftError` | Phát hiện schema drift khi trigger `ledger_append_only` bị disable (`status='D'`) hoặc gọi sai hàm; từ chối tiếp nhận baseline với thông báo lỗi chi tiết. | **PASS** |
| `Postgres_TestEnvironment_TargetGuard_ProhibitsDestructiveActionsOnWorkingOrSystemDatabases` | Target Guard chặn mutation/truncate trên `oisms`, `postgres` và các database không chứa từ khóa `test`. | **PASS** |
| `Postgres_TestEnvironment_HandlesSpecialCharactersInPassword_WithoutSqlInjection` | Kết nối xác thực thành công với mật khẩu phức tạp chứa ký tự đặc biệt (`P@ss'\"#;& $Test2026`) mà không bị SQL injection. | **PASS** |
| `Postgres_TenantGuard_DetachedEntity_CrossTenantUpdate_MustBeRejected_AndPreserveTargetData` | Tenant A attach ID của Tenant B với TenantId=A để cập nhật; Postgres trả về 0 rows affected, EF Core ném DbUpdateConcurrencyException, dữ liệu Tenant B giữ nguyên. | **PASS** |
| `Postgres_TenantGuard_DetachedEntity_CrossTenantDelete_MustBeRejected_AndPreserveTargetData` | Tenant A attach ID của Tenant B để xóa; xóa bị chặn, dữ liệu Tenant B được bảo toàn toàn vẹn. | **PASS** |
| `Postgres_TenantGuard_ValidEntity_SameTenantUpdateAndDelete_ShouldWorkProperly` | Thao tác cập nhật và xóa hợp lệ trong cùng tenant hoạt động trơn tru. | **PASS** |
| `Postgres_TenantGuard_MissingTenantContext_MustBlockWrites_OnAllFourSaveChangesOverloads` | Kiểm tra cả 4 overload của SaveChanges chặn ghi khi thiếu TenantId (`SecurityException`). | **PASS** |
| `Postgres_TenantGuard_PrivilegedScope_AllowsExplicitServerWrites` | Privileged Scope cho phép ghi hệ thống tin cậy, nhưng vẫn chặn TenantId rỗng (`Guid.Empty`). | **PASS** |
| `Postgres_TenantGuard_WriteMismatch_ShouldThrowSecurityException` | Chặn ghi khi TenantId trong entity sai lệch với JWT Claims. | **PASS** |
| `Postgres_TenantGuard_ImmutableTenantId_ShouldThrowException` | Chặn hành vi sửa đổi cột `TenantId` của thực thể đã tồn tại. | **PASS** |
| `Postgres_TenantGuard_CrossTenantFK_ShouldFailAtPostgresLevel` | Composite FK `(tenant_id, product_id)` ở tầng database chặn liên kết chéo SKU với Product của tenant khác. | **PASS** |
| `Postgres_Seeder_ShouldBeAtomic_Idempotent_AndSelfHealing` | Seeder chạy nguyên tử; chạy lại không trùng lặp; tự phục hồi dữ liệu thiếu; không ghi đè dữ liệu user/supplier đã sửa; tồn ban đầu = 0. | **PASS** |
| `Postgres_MigrationService_ShouldVerifyAndAdoptBaseline` | Xác minh 29 bảng schema v0.5 và ghi nhận baseline vào `__EFMigrationsHistory` an toàn. | **PASS** |
| `Postgres_TenantIsolation_ReadFilter_ShouldStrictlyIsolateData` | Query Filter chặn Tenant A đọc dữ liệu của Tenant B. | **PASS** |
| `Postgres_TenantIsolation_Unauthenticated_ShouldReturnZeroRecords` | Request chưa đăng nhập truy vấn trả về rỗng, không rò rỉ dữ liệu. | **PASS** |
| `Postgres_ConnectionAndReadiness_ShouldUseRuntimeAccount_AndVerifySchema` | Kiểm tra tài khoản `oisms_test_app` và tính sẵn sàng của 29 bảng qua Readiness Service. | **PASS** |
| `Postgres_EndToEnd_SmokeTest_RegisterLoginCreateSkuReadSku` | Chu trình hoàn chỉnh: Đăng ký shop -> Đăng nhập lấy JWT -> Tạo SKU -> Quét mã vạch POS -> Tìm kiếm POS. | **PASS** |
| *Cùng 23 integration tests khác cho API Controllers & Repositories* | Bao phủ các controller Auth, Products, Suppliers, Branches với WebApplicationFactory. | **PASS** |

### 5.2. Unit Tests (`OISM.UnitTests` - 47 tests pass 100%)
| Nhóm bài Test | Số lượng | Mục đích & Cơ chế kiểm chứng | Kết quả |
| :--- | :---: | :--- | :--- |
| `DatabaseBaselineValidatorTests` | 13 tests | **[CẬP NHẬT]** Kiểm tra hàm `NormalizeExpression`: bảo toàn literal string, casing, khoảng trắng nội tại và quoted identifier; kiểm tra thuộc tính định nghĩa của `ledger_append_only` (không có WHEN clause, không bị giới hạn cột), FK RESTRICT và CHECK. | **PASS** |
| `DemoSeedConfigurationTests` | 17 tests | Khóa cứng Production, Staging, UAT, CustomEnv; chỉ cho phép Dev/Test khi cấu hình bật; bắt buộc mật khẩu cấu hình, ném `InvalidOperationException` nếu thiếu/rỗng; kiểm tra validation của `OismsDatabaseSeeder`. | **PASS** |
| `BarcodeAndCatalogTests` | 6 tests | Kiểm tra thuật toán sinh mã EAN-13, tính check digit Modulo 10, format SKU và validation thuộc tính catalog. | **PASS** |
| `AuthValidatorTests` | 11 tests | Kiểm tra FluentValidation cho đăng ký shop, chuẩn hóa email, kiểm tra số điện thoại E.164, mật khẩu tối thiểu. | **PASS** |

---

## 6. Nhiệm vụ tiếp theo của Nhựt (Backend 2)
1. **API Quản lý Danh mục (Categories) & Nhãn hàng (Brands):**
   - Đã có sẵn bảng `categories` và `brands` được cấu hình đầy đủ trong `AppDbContext`.
   - Viết Controller, DTO, FluentValidation và Service CRUD cho Categories và Brands theo mô hình chuẩn của `ProductsController`.
2. **API Quản lý Chi nhánh (Branches) & Kho (Warehouses):**
   - Đã có sẵn bảng `branches` và `warehouses` với FK ghép `(tenant_id, branch_id)`.
   - Triển khai phân quyền Owner/Staff khi cấu hình kho và chi nhánh.
3. **Lưu ý bảo mật:**
   - Không tắt Global Query Filter (`.IgnoreQueryFilters()`) trong các API nghiệp vụ thông thường.
   - Luôn kiểm tra quyền vai trò người dùng (`[Authorize(Roles = "Owner,Staff")]`) trên các endpoint quản trị.
