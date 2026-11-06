# BF-029 — Health check readiness viết bằng Npgsql nằm trong WebApi

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | 🟡 Đã sửa, chờ kiểm chứng PostgreSQL trên CI |
| **Ưu tiên** | P2 |
| **Khu vực** | Backend / Clean Architecture / WebApi |
| **Phát hiện** | 06/11/2026, rà soát lại Clean Architecture sau Upgrade 06 (commit `6ad3cf6`) |
| **Đợt fix** | Đợt 2 |
| **Kế hoạch chung** | [CHECKLIST.md mục E](../CHECKLIST.md#e-kế-hoạch-hoàn-thiện-clean-architecture-bf-028035) |

## Mô tả lỗi

`OISM.WebApi/Program.cs` chứa nguyên lớp `NpgsqlDatabaseReadinessService` (khoảng 180 dòng): mở `NpgsqlConnection`, chạy SQL kiểm tra quyền role, đếm bảng, kiểm tra trigger `ledger_append_only`. Vì vậy `OISM.WebApi.csproj` phải tham chiếu gói `Npgsql`. Danh sách `RequiredTables` cũng nằm ở đây, trong khi `DatabaseMigrationService` ở Infrastructure có một bản riêng.

**Ảnh hưởng:** code hạ tầng nằm ở tầng trình bày; `Program.cs` dài, khó đọc; hai nơi giữ danh sách bảng.

## Kế hoạch fix

1. Chuyển `IDatabaseReadinessService`, `ReadinessCheckResult`, `NpgsqlDatabaseReadinessService` sang `OISM.Infrastructure/Health/` và đăng ký trong `AddInfrastructureServices`.
2. Giữ một nguồn duy nhất cho `RequiredTables` (dùng chung với `DatabaseMigrationService`). Cập nhật quy trình đổi schema trong CLAUDE.md và `docs/database/README.md` cho đúng vị trí mới.
3. Gỡ `PackageReference Npgsql` khỏi `OISM.WebApi.csproj`.
4. Test: các integration test readiness (thiếu bảng, trigger tắt, user có quyền admin, timeout) vẫn PASS; `/health/ready` trả 200 trên DB đủ 43 bảng.

## Cách đã sửa

Readiness được đăng ký trong Infrastructure và dùng chung `DatabaseMigrationService.RequiredTables`. Các route `/health/ready` và `/health` tiếp tục trả payload readiness hiện có. WebApi đã bỏ tham chiếu gói Npgsql trực tiếp.

## Điều kiện đóng

- Không còn vi phạm mô tả ở trên trong code (kiểm bằng `grep`/build).
- Toàn bộ unit test, integration test liên quan và `npm test` của POS PASS; thay đổi chạm transaction, khóa, ledger phải có bằng chứng trên PostgreSQL thật.
- Cập nhật trạng thái trong file này và [CHECKLIST.md](../CHECKLIST.md).
