# Báo cáo sau nâng cấp: Khóa kiến trúc 4 tầng & đối chiếu NFR-MAINT-01 (OISMS-59-G1C)

| Mục | Nội dung |
| --- | --- |
| Task | `OISMS-59-G1C` (`NFR-MAINT-01`) |
| Ngày kiểm tra | 14/11/2026 |
| Phạm vi | `test-oisms/backend/OISM.IntegrationTests/ArchitectureRulesTests.cs` và tài liệu `NFR-MAINT-01` / Clean Architecture / RTM (không sửa code sản phẩm `src/` và không đổi DB) |
| Trạng thái | Hoàn tất viết test khóa kiến trúc, cập nhật tài liệu và kiểm chứng `FAIL → PASS` (chờ review) |

## Đã làm

1. **Khóa ranh giới kiến trúc bằng reflection & quét nguồn (`test-oisms/backend/OISM.IntegrationTests/ArchitectureRulesTests.cs`):**
   - `AC01_Domain_ShouldOnlyReferenceBclAssemblies`: xác nhận `OISM.Domain` chỉ tham chiếu BCL (`System.*`, `Microsoft.CSharp`, `netstandard`).
   - `AC02_Application_ShouldNotReferenceInfrastructureWebApiOrProviderPackages`: xác nhận `OISM.Application` chỉ tham chiếu `OISM.Domain` và các gói hợp lệ (cho phép ngoại lệ có chủ đích `Microsoft.EntityFrameworkCore` qua `IAppDbContext`, cấm `OISM.Infrastructure`, `OISM.WebApi`, `Npgsql*`, `Hangfire*`, `Microsoft.AspNetCore*`, `BCrypt*`).
   - `AC03_Infrastructure_ShouldNotReferenceWebApi`: xác nhận `OISM.Infrastructure` không tham chiếu `OISM.WebApi`.
   - `AC04_WebApiControllers_ShouldNotDependOnDbContextUnitOfWorkOrSqlLockService`: quét toàn bộ controller non-abstract kế thừa `ControllerBase` trong `OISM.WebApi`, bảo đảm không có tham số constructor hoặc field kiểu `DbContext`, `AppDbContext`, `IAppDbContext`, `IUnitOfWork`, `ISqlLockService`; liệt kê tên controller và thành phần vi phạm nếu fail.
   - `AC05_ApplicationSource_ShouldNotInvokeEfRawSqlMethods`: tìm gốc repo từ `AppContext.BaseDirectory` và quét toàn bộ file `*.cs` dưới `src/backend/src/OISM.Application` (loại `bin/`, `obj/`) bằng regex để cấm `ExecuteSqlRaw`, `ExecuteSqlInterpolated`, `FromSqlRaw`, `FromSqlInterpolated`, `SqlQueryRaw`.
2. **Cập nhật tài liệu kiến trúc và truy vết:**
   - `docs/04-architecture-and-design/01-clean-architecture.md` (v1.2.0): bổ sung mục **4. Đối Chiếu Phụ Thuộc Bốn Tầng (14/11/2026)** gồm bảng quy tắc được tham chiếu/cấm, hiện trạng 4 file `.csproj`, lý do chốt ngoại lệ `Application` dùng EF Core qua `IAppDbContext` (39 file, ~645 lời gọi EF; refactor sang repository rủi ro cao đối với transaction/reservation), nơi đặt SQL/lock/transaction (`ISqlLockService`, `IUnitOfWork` ở Infrastructure) và tên 5 test khóa quy tắc.
   - `docs/03-non-functional-requirements/05-maintainability-testing.md` (v1.1.0): bổ sung mục **2. Hiện Trạng Tuân Thủ & Ngoại Lệ Đã Chốt (`NFR-MAINT-01`, 14/11/2026)**.
   - `docs/06-testing-and-qa/01-requirements-matrix-rtm.md` (v1.3.0): thêm dòng `NFR-MAINT-01` trỏ tới `ArchitectureRulesTests` (`5/5 PASS`).

## Không thay đổi code sản phẩm & CSDL

- Không sửa bất kỳ file nào trong `src/` (tầng Domain, Application, Infrastructure, WebApi giữ nguyên 100%).
- Không thêm NuGet package mới (chỉ dùng `System.Reflection`, `Assembly.GetReferencedAssemblies()` và regex).
- Không thay đổi schema hay migration cơ sở dữ liệu.

## Kiểm chứng

1. **Chứng minh bắt được vi phạm (`FAIL → PASS`):**
   - Tạm tạo hai file vi phạm cục bộ (không giữ lại trong repo):
     - `src/backend/src/OISM.WebApi/Controllers/_TempArchitectureViolationController.cs` (tiêm `IAppDbContext` qua constructor).
     - `src/backend/src/OISM.Application/_TempRawSqlViolationProbe.cs` (gọi `ExecuteSqlRaw("SELECT 1")`).
   - Kết quả chạy thử: `AC04` và `AC05` **FAIL** (2 failed, 3 passed), in đúng `[AC-04 / NFR-MAINT-01]` kèm tên controller `TempArchitectureViolationController` (`ctor parameter 'db': IAppDbContext`, `field '_db'`) và `[AC-05 / NFR-MAINT-01]` kèm đường dẫn `src/backend/src/OISM.Application/_TempRawSqlViolationProbe.cs:5`.
   - Sau khi xóa 2 file tạm và chạy lại:

| Lệnh | Phạm vi | Kết quả |
| --- | --- | --- |
| `dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal` | Toàn bộ solution backend | PASS (0 Warning, 0 Error) |
| `dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --no-build -v:minimal --filter 'FullyQualifiedName~ArchitectureRulesTests'` | 5 test quy tắc `NFR-MAINT-01` (reflection + quét source, không chạm DB) | PASS 5/5 (Failed: 0, Skipped: 0) |

## Giới hạn

- `ArchitectureRulesTests` không dùng NetArchTest theo ràng buộc không thêm package; quy tắc `AC-01..AC-03` kiểm tra ở cấp độ assembly references (`GetReferencedAssemblies`) và `AC-04` kiểm tra constructor/field của các `ControllerBase`, chưa kiểm tra lời gọi giữa các namespace nội bộ bên trong cùng một assembly hay các Minimal API/Hub ngoài `ControllerBase`.
- `AC-05` dùng regex trên mã nguồn `OISM.Application/*.cs` (đã loại `bin/` và `obj/`), nên không phân tích AST sâu nếu tên phương thức raw SQL xuất hiện trong chuỗi/comment không phải comment thường.
