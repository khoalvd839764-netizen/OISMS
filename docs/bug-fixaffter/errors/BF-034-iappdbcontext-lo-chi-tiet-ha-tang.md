# BF-034 — IAppDbContext lộ chi tiết hạ tầng, nhánh IsRelationalDatabase

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ❌ Chưa sửa |
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

## Điều kiện đóng

- Không còn vi phạm mô tả ở trên trong code (kiểm bằng `grep`/build).
- Toàn bộ unit test, integration test liên quan và `npm test` của POS PASS; thay đổi chạm transaction, khóa, ledger phải có bằng chứng trên PostgreSQL thật.
- Cập nhật trạng thái trong file này và [CHECKLIST.md](../CHECKLIST.md).
