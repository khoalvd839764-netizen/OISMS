# BF-030 — Controller bắt lỗi PostgreSQL trực tiếp

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ❌ Chưa sửa |
| **Ưu tiên** | P2 |
| **Khu vực** | Backend / Clean Architecture / WebApi |
| **Phát hiện** | 06/11/2026, rà soát lại Clean Architecture sau Upgrade 06 (commit `6ad3cf6`) |
| **Đợt fix** | Đợt 1 |
| **Kế hoạch chung** | [CHECKLIST.md mục E](../CHECKLIST.md#e-kế-hoạch-hoàn-thiện-clean-architecture-bf-028035) |

## Mô tả lỗi

`OISM.WebApi/Controllers/GoodsReceiptsController.cs` (action `Create`) bắt `Microsoft.EntityFrameworkCore.DbUpdateException` khi `InnerException is Npgsql.PostgresException { SqlState: "23505" }` để trả 409 "Mã phiếu nhập đã tồn tại". Tầng trình bày biết mã lỗi của PostgreSQL và EF Core.

## Kế hoạch fix

1. Bắt lỗi trùng mã trong `GoodsReceiptService.CreateReceiptAsync` và ném `OismException` (409, mã lỗi rõ ràng, ví dụ `GOODS_RECEIPT_NUMBER_DUPLICATE`; ghi vào danh mục mã lỗi).
2. Tạm thời dùng cách bắt lỗi giống các service khác; khi làm BF-033 thì chuyển sang `IDbErrorTranslator`.
3. Xóa khối catch trong controller.
4. Test: tạo hai phiếu nhập cùng `number` trên PostgreSQL thật trả 409 với đúng `errorCode`.

## Điều kiện đóng

- Không còn vi phạm mô tả ở trên trong code (kiểm bằng `grep`/build).
- Toàn bộ unit test, integration test liên quan và `npm test` của POS PASS; thay đổi chạm transaction, khóa, ledger phải có bằng chứng trên PostgreSQL thật.
- Cập nhật trạng thái trong file này và [CHECKLIST.md](../CHECKLIST.md).
