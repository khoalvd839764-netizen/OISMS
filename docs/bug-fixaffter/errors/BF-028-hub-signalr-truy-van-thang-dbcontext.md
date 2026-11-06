# BF-028 — NotificationHub truy vấn thẳng DbContext

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa và kiểm chứng |
| **Ưu tiên** | P2 |
| **Khu vực** | Backend / Clean Architecture / WebApi |
| **Phát hiện** | 06/11/2026, rà soát lại Clean Architecture sau Upgrade 06 (commit `6ad3cf6`) |
| **Đợt fix** | Đợt 2 |
| **Kế hoạch chung** | [CHECKLIST.md mục E](../CHECKLIST.md#e-kế-hoạch-hoàn-thiện-clean-architecture-bf-028035) |

## Mô tả lỗi

`OISM.WebApi/Hubs/NotificationHub.cs` inject `IAppDbContext` và tự chạy `_dbContext.Branches.IgnoreQueryFilters().AnyAsync(...)` trong `JoinBranchGroup` để kiểm tra chi nhánh thuộc tenant. Tầng trình bày đang tự truy cập dữ liệu và tự bỏ query filter tenant, thay vì gọi một use case trong `OISM.Application`.

**Ảnh hưởng:** logic kiểm tra quyền vào nhóm chi nhánh nằm ngoài Application, khó test và dễ bị sao chép sai khi thêm hub khác.

## Kế hoạch fix

1. Thêm `IBranchService.ExistsActiveAsync(Guid tenantId, Guid branchId, CancellationToken)` trong `Features/Branches`.
2. Hub gọi service này, bỏ inject `IAppDbContext` và bỏ `IgnoreQueryFilters` trong WebApi.
3. Test: integration test SignalR hiện có (join chi nhánh đúng tenant, sai tenant, chi nhánh tắt) vẫn PASS.

## Cách đã sửa

`NotificationHub` gọi `IBranchService.ExistsActiveAsync` với tenant từ claim. Truy vấn giữ query filter của tenant và kiểm tra chi nhánh đang hoạt động. Test SignalR bao phủ chi nhánh hợp lệ, khác tenant, không tồn tại và đã ngưng.

## Kiểm chứng

Các test SignalR qua CI, gồm join chi nhánh đang hoạt động của cùng tenant và từ chối chi nhánh khác tenant, không tồn tại hoặc đã ngưng. [GitHub Actions #134](https://github.com/khoalvd839764-netizen/OISMS/actions/runs/42185858352)

## Điều kiện đóng

- Không còn vi phạm mô tả ở trên trong code (kiểm bằng `grep`/build).
- Toàn bộ unit test, integration test liên quan và `npm test` của POS PASS; thay đổi chạm transaction, khóa, ledger phải có bằng chứng trên PostgreSQL thật.
- Cập nhật trạng thái trong file này và [CHECKLIST.md](../CHECKLIST.md).
