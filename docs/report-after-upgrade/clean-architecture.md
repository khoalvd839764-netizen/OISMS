# Báo cáo sau nâng cấp: Clean Architecture backend

| Mục | Nội dung |
| --- | --- |
| Task | `ARCH-CLEAN-01` |
| Ngày kiểm tra | 29/10/2026 |
| Baseline | Branch `feature/OISMS-43-orders-transfers`, commit `e3813da` |
| Phạm vi | Backend `src/backend/OISM.sln`; không gồm frontend |
| Trạng thái | Đợt refactor controller/DI và các finding F-01–F-03 đã được kiểm chứng. **Chưa kết luận toàn backend đạt Clean Architecture nghiêm ngặt.** |

## Kết quả trong phạm vi đợt này

- Tám controller `Auth`, `Branch`, `Brands`, `Categories`, `PosCatalog`, `Products`, `Suppliers` và `Warehouses` chuyển thao tác dữ liệu/nghiệp vụ sang các service trong `OISM.Application`. Các controller trong diff hiện điều phối request, phân quyền và HTTP response; không còn trực tiếp dùng `AppDbContext` hoặc gọi `SaveChangesAsync`.
- Đăng ký dịch vụ được tách thành [`AddApplicationServices`](../../src/backend/src/OISM.Application/DependencyInjection.cs) và [`AddInfrastructureServices`](../../src/backend/src/OISM.Infrastructure/DependencyInjection.cs), gọi từ [`Program.cs`](../../src/backend/src/OISM.WebApi/Program.cs).
- BCrypt được đặt sau [`IPasswordHasher`](../../src/backend/src/OISM.Application/Common/Interfaces/IPasswordHasher.cs) và hiện thực ở [`PasswordHasher`](../../src/backend/src/OISM.Infrastructure/Services/PasswordHasher.cs).
- Các lệnh khóa PostgreSQL được đặt trong [`SqlLockService`](../../src/backend/src/OISM.Infrastructure/Services/SqlLockService.cs), qua `ISqlLockService`; thao tác đổi chi nhánh kho đặt trong [`WarehouseBranchReassignmentStore`](../../src/backend/src/OISM.Infrastructure/Services/WarehouseBranchReassignmentStore.cs), qua `IWarehouseBranchReassignmentStore`. Các caller vẫn lấy khóa sau khi mở transaction.

| Finding review | Kết quả đóng |
| --- | --- |
| F-01: controller tự làm nghiệp vụ và ghi DB | Đã chuyển các controller thuộc diff sang Application service. |
| F-02: mở rộng `IAppDbContext` để phục vụ controller | Các thành viên EF được thêm ở vòng đầu (`DatabaseFacade`, `ChangeTracker`, `Entry`) đã được bỏ khỏi diff. |
| F-03: SQL PostgreSQL vừa chuyển nằm trong Application | Đã chuyển các thao tác khóa và cập nhật kho liên quan sang adapter Infrastructure. |

## Kiểm chứng

Các lệnh chạy tại `/home/jackie-khoa/Downloads/OISMS`, trên working tree chưa commit dựa trên `e3813da`, với .NET SDK `8.0.132`. Đặt `DOTNET_CLI_HOME=/tmp/oisms-dotnet-cli` và `DOTNET_CLI_TELEMETRY_OPTOUT=1` khi chạy CLI.

| Kiểm tra | Lệnh chính | Kết quả |
| --- | --- | --- |
| Restore | `dotnet restore src/backend/OISM.sln -v:minimal` | PASS, 6 project được restore. |
| Build mặc định | `dotnet build src/backend/OISM.sln --no-restore -v:minimal` | FAIL tại bước tạo `OISM.WebApi/obj/Debug/net8.0/apphost`: môi trường từ chối ghi file. Không phải lỗi biên dịch C#. |
| Build không tạo apphost | `dotnet build src/backend/OISM.sln --no-restore -p:UseAppHost=false -v:minimal` | PASS, 0 warning, 0 error; cả 4 project nguồn và 2 project test biên dịch. |
| Unit tests | `dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj --no-build --no-restore -p:UseAppHost=false -v:minimal` | PASS 158/158. |
| Integration InMemory | `dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj --no-build --no-restore -p:UseAppHost=false -v:minimal --filter 'FullyQualifiedName~AuthIntegrationTests\|FullyQualifiedName~CatalogAndProductIntegrationTests\|FullyQualifiedName~BranchAndSupplierIntegrationTests'` | PASS 11/11; fixture dùng EF InMemory. |
| Integration PostgreSQL | `dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj --no-build --no-restore -p:UseAppHost=false -v:minimal --filter 'FullyQualifiedName~WarehouseBranchReassignmentRegressionTests\|FullyQualifiedName~PostgresBackend2RegressionTests'` | PASS 20/20 trên PostgreSQL thật, database test riêng do fixture tạo. |
| Diff | `git diff --check -- src/backend` | PASS; chỉ có cảnh báo chuẩn hóa CRLF của Git. |

**Tổng test đã chạy: 189 PASS, 0 FAIL, 0 SKIP.** Không chạy toàn bộ integration suite; bằng chứng trên chỉ bao phủ các nhóm liên quan trực tiếp đến đợt refactor. Build với apphost mặc định còn cần được chạy trong môi trường có quyền tạo file đó.

## Phần chưa hoàn tất của Clean Architecture toàn backend

[`IAppDbContext`](../../src/backend/src/OISM.Application/Common/Interfaces/IAppDbContext.cs) hiện vẫn công khai `DbSet`, transaction, execution strategy và raw SQL; `OISM.Application` vẫn tham chiếu EF Core. Các service cũ như `GoodsReceiptService`, `ReservationService`, `OrderService`, `StockCountService` và `StockTransferService` còn chứa SQL PostgreSQL. Đây là nợ kiến trúc đã tồn tại ngoài các lệnh vừa chuyển ở F-03; cần chốt mức tách persistence trước khi refactor tiếp vì chúng liên quan ledger, reservation, transaction và concurrency.

Báo cáo này xác nhận **đợt nâng cấp trong phạm vi review đã hoàn tất và các gate đã chọn đạt**; không xác nhận mọi file backend đã được chuẩn hóa. Không có migration, deploy hoặc commit trong đợt kiểm tra này.
