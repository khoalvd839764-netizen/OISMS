# Báo cáo sau nâng cấp: Clean Architecture backend

| Mục | Nội dung |
| --- | --- |
| Task | `ARCH-CLEAN-01` (Upgrade 06), tiếp tục ở BF-028..035 |
| Ngày kiểm tra | 29/10/2026 (đợt đầu), 06/11/2026 (đợt 1–4) |
| Baseline | Đợt đầu: `feature/OISMS-43-orders-transfers`, commit `e3813da`. Đợt 1–4: `fix/OISMS-47-master-review-fixes`, PR #38 |
| Phạm vi | Backend `src/backend/OISM.sln`; không gồm frontend |
| Trạng thái | ✅ Đã đóng mọi finding F-01..F-03 và BF-028..035. Chiều phụ thuộc Domain ← Application ← Infrastructure/WebApi đúng; Application chỉ còn tham chiếu EF Core để truy vấn và lưu entity theo quyết định kiến trúc của nhóm. |

## 1. Đợt đầu (29/10/2026): controller và DI

- Tám controller `Auth`, `Branch`, `Brands`, `Categories`, `PosCatalog`, `Products`, `Suppliers` và `Warehouses` chuyển thao tác dữ liệu, nghiệp vụ sang các service trong `OISM.Application`. Controller chỉ điều phối request, phân quyền và HTTP response.
- Đăng ký dịch vụ tách thành [`AddApplicationServices`](../../src/backend/src/OISM.Application/DependencyInjection.cs) và [`AddInfrastructureServices`](../../src/backend/src/OISM.Infrastructure/DependencyInjection.cs), gọi từ [`Program.cs`](../../src/backend/src/OISM.WebApi/Program.cs).
- BCrypt đặt sau [`IPasswordHasher`](../../src/backend/src/OISM.Application/Common/Interfaces/IPasswordHasher.cs), hiện thực ở [`PasswordHasher`](../../src/backend/src/OISM.Infrastructure/Services/PasswordHasher.cs).
- Khóa PostgreSQL đặt trong [`SqlLockService`](../../src/backend/src/OISM.Infrastructure/Services/SqlLockService.cs) qua `ISqlLockService`; đổi chi nhánh kho đặt trong [`WarehouseBranchReassignmentStore`](../../src/backend/src/OISM.Infrastructure/Services/WarehouseBranchReassignmentStore.cs).

| Finding | Kết quả |
| --- | --- |
| F-01: controller tự làm nghiệp vụ và ghi DB | Đã chuyển sang Application service. |
| F-02: mở rộng `IAppDbContext` để phục vụ controller | Đã bỏ `DatabaseFacade`, `ChangeTracker`, `Entry` khỏi interface. |
| F-03: SQL PostgreSQL vừa chuyển nằm trong Application | Đã chuyển sang adapter Infrastructure. |

Kiểm chứng đợt đầu: build PASS, 158 unit test, 11 integration test InMemory và 20 integration test PostgreSQL PASS (tổng 189 PASS, 0 FAIL). Đợt này chưa xử lý SQL khóa và transaction còn lại trong các service kho, đơn hàng; phần đó được ghi thành BF-032, BF-034 và làm ở đợt 3 bên dưới.

## 2. Rà soát lại và đợt 1–4 (06/11/2026)

Rà soát sau Upgrade 06 ghi nhận 8 chỗ lệch, chi tiết từng lỗi trong [`docs/bug-fixaffter/errors/`](../bug-fixaffter/errors/) và kế hoạch ở [CHECKLIST mục E](../bug-fixaffter/CHECKLIST.md#e-kế-hoạch-hoàn-thiện-clean-architecture-bf-028035).

| Đợt | Lỗi | Thay đổi chính | Commit |
| :---: | :--- | :--- | :--- |
| 1 | BF-030, BF-031 | Phản hồi lỗi thống nhất qua [`ExceptionHandlingMiddleware`](../../src/backend/src/OISM.WebApi/Middlewares/ExceptionHandlingMiddleware.cs) và [`ApiErrorContractAttribute`](../../src/backend/src/OISM.WebApi/Http/ApiErrorContractAttribute.cs); bỏ `try/catch` và kiểm tra `ModelState` lặp trong controller; đọc user ID qua [`ClaimsPrincipalExtensions`](../../src/backend/src/OISM.WebApi/Http/ClaimsPrincipalExtensions.cs); bắt trùng mã phiếu nhập chuyển vào service | `c95c21c` |
| 2 | BF-028, BF-029, BF-033, gỡ MediatR | `NotificationHub` gọi `IBranchService`; readiness check chuyển sang [`NpgsqlDatabaseReadinessService`](../../src/backend/src/OISM.Infrastructure/Health/NpgsqlDatabaseReadinessService.cs), WebApi không còn tham chiếu Npgsql; lỗi DB dịch qua `IDbErrorTranslator` ([`PostgresDbErrorTranslator`](../../src/backend/src/OISM.Infrastructure/Persistence/PostgresDbErrorTranslator.cs)) | `c95c21c` |
| 3 | BF-032, BF-034 | SQL khóa của 6 service chuyển vào `ISqlLockService`; transaction, retry, commit, rollback qua [`IUnitOfWork`](../../src/backend/src/OISM.Application/Common/Interfaces/IUnitOfWork.cs) ([`EfUnitOfWork`](../../src/backend/src/OISM.Infrastructure/Persistence/EfUnitOfWork.cs)); bỏ SQL thô, kiểu transaction EF và 38 nhánh `IsRelationalDatabase` khỏi Application. Giữ nguyên thứ tự khóa và vị trí commit | `a73d96b` |
| 4 | BF-035 | Hằng số trạng thái theo loại chứng từ ([`DocumentStatuses.cs`](../../src/backend/src/OISM.Domain/Common/DocumentStatuses.cs)); luật chuyển trạng thái đưa vào entity; `OrderService` tách thành `PosCheckoutService`, `MarketplaceOrderService`, `OrderIntakeService`, `OrderQueryService` | `df26020` |
| Chốt | BF-035 | Đổi chỗ so `"Active"` còn sót trong `ReservationExpiryJob` sang `ReservationStatus.Active` | commit cuối PR #38 |

Hợp đồng API (status, `errorCode`, hình dạng body) và giá trị trạng thái lưu trong PostgreSQL không đổi.

## 3. Kiểm chứng

| Đợt | Kiểm tra | Kết quả |
| :---: | :--- | :--- |
| 1–2 | Build .NET 8, toàn bộ `dotnet test src/backend/OISM.sln` với PostgreSQL 16, build Admin/POS, test POS | PASS, [Actions #134](https://github.com/khoalvd839764-netizen/OISMS/actions/runs/42185858352) |
| 3 | 223 unit test; 17 integration test trên PostgreSQL 16.15 cục bộ (50 yêu cầu tranh giữ chỗ và checkout, replay idempotency, duyệt kiểm kê song song, nhập vượt phiếu mua, xuất nhận chuyển kho đồng thời, hết hạn giữ chỗ); 63 test POS; toàn bộ suite trong CI | PASS, [Actions #136](https://github.com/khoalvd839764-netizen/OISMS/actions/runs/42193764424) |
| 4 | 237 unit test (14 ca chuyển trạng thái Domain); 15 integration test cục bộ; 63 test POS; toàn bộ suite trong CI | PASS, [Actions #138](https://github.com/khoalvd839764-netizen/OISMS/actions/runs/42197568799) |
| Chốt | `dotnet build src/backend/OISM.sln`, `dotnet test` unit test; CI của PR #38 | Xem comment tổng kết trên PR #38 |

Kiểm tra tĩnh trên code hiện tại (`grep`, loại `bin`/`obj`):

| Điều kiện | Kết quả |
| --- | --- |
| `PostgresException`, `using Npgsql` trong Application | 0 |
| `ExecuteSqlRaw`, `FromSqlRaw`, `SqlQuery`, `IsRelationalDatabase` trong Application | 0 |
| Npgsql trong WebApi | 0 |
| `IAppDbContext` / `IgnoreQueryFilters` trong `NotificationHub` | 0 |
| `catch` và `ModelState.IsValid` trong controller | 0 |
| So trạng thái bằng chuỗi (`Status == "..."`) trong Application, Infrastructure, WebApi | 0 |
| Gói MediatR | Đã gỡ |

## 4. Giới hạn còn lại

- `OISM.Application` vẫn tham chiếu EF Core và `IAppDbContext` vẫn công khai `DbSet` để truy vấn và lưu entity. Đây là lựa chọn có chủ đích (xem `CLAUDE.md`), không phải lỗi còn mở.
- Seeder và audit log (`Action = "Approved"`, `"Cancelled"`) còn dùng chuỗi; đó là giá trị dữ liệu, không phải so sánh trạng thái chứng từ.
- Không có migration hay thay đổi schema trong toàn bộ các đợt.
