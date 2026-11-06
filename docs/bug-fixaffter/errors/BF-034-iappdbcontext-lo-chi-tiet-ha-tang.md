# BF-034 — IAppDbContext lộ chi tiết hạ tầng, nhánh IsRelationalDatabase

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa và kiểm chứng |
| **Ưu tiên** | P2 |
| **Khu vực** | Backend / Clean Architecture / Application |
| **Phát hiện** | 06/11/2026, rà soát lại Clean Architecture sau Upgrade 06 (commit `6ad3cf6`) |
| **Đợt fix** | Đợt 3 (cần nhóm đồng ý) |
| **Kế hoạch chung** | [CHECKLIST.md mục E](../CHECKLIST.md#e-kế-hoạch-hoàn-thiện-clean-architecture-bf-028035) |

## Mô tả lỗi

`Application/Common/Interfaces/IAppDbContext.cs` công khai `ExecuteSqlRawAsync`, `BeginTransactionAsync` (trả `IDbContextTransaction` của EF), `IsRelationalDatabase`, `ClearChangeTracker`. Code nghiệp vụ có **38 nhánh** `if (_context.IsRelationalDatabase)` chỉ để bỏ qua khóa/SQL khi chạy test EF InMemory. `OrderService.cs` dài 3217 dòng.

**Ảnh hưởng:** đường chạy trong test InMemory khác đường chạy thật, nên test InMemory không chứng minh được hành vi production; Application phụ thuộc EF Core.

## Kế hoạch fix

1. Thêm `IUnitOfWork` (bắt đầu/commit/rollback transaction) thay cho `BeginTransactionAsync` trả kiểu EF.
2. Sau khi BF-032 xong, bỏ `ExecuteSqlRawAsync` khỏi `IAppDbContext`.
3. Thay 38 nhánh `IsRelationalDatabase` bằng bản hiện thực rỗng của `ISqlLockService`/`IUnitOfWork` đăng ký riêng cho test InMemory.
4. Làm cùng PR với từng service ở BF-032 để không chạm một service hai lần.

## Cách đã sửa

Application dùng `IUnitOfWork` cho retry, mở transaction, commit, rollback và đặt lại tracking. `IAppDbContext` không còn lộ SQL thô, kiểu transaction của EF hay cờ `IsRelationalDatabase`. Infrastructure dùng transaction và khóa PostgreSQL cho Npgsql. Với EF InMemory, các thao tác transaction và khóa không truy cập cơ sở dữ liệu quan hệ.

## Kiểm chứng

Số điểm bắt đầu, commit và rollback khớp với baseline ở cả sáu service. 223 unit test và 17 test tích hợp chọn lọc trên PostgreSQL 16.15 đã qua, gồm các ca rollback và retry của luồng hết hạn giữ chỗ. Toàn bộ suite backend và job frontend cũng qua trong CI. [GitHub Actions #136](https://github.com/khoalvd839764-netizen/OISMS/actions/runs/42193764424)

## Điều kiện đóng

- Không còn vi phạm mô tả ở trên trong code (kiểm bằng `grep`/build).
- Toàn bộ unit test, integration test liên quan và `npm test` của POS PASS; thay đổi chạm transaction, khóa, ledger phải có bằng chứng trên PostgreSQL thật.
- Cập nhật trạng thái trong file này và [CHECKLIST.md](../CHECKLIST.md).
