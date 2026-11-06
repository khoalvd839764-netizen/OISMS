# BF-033 — Application bắt PostgresException

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa và kiểm chứng |
| **Ưu tiên** | P2 |
| **Khu vực** | Backend / Clean Architecture / Application |
| **Phát hiện** | 06/11/2026, rà soát lại Clean Architecture sau Upgrade 06 (commit `6ad3cf6`) |
| **Đợt fix** | Đợt 2 |
| **Kế hoạch chung** | [CHECKLIST.md mục E](../CHECKLIST.md#e-kế-hoạch-hoàn-thiện-clean-architecture-bf-028035) |

## Mô tả lỗi

8 chỗ trong 7 service của Application kiểm tra kiểu `PostgresException`/`SqlState`/tên constraint để đổi lỗi DB thành lỗi nghiệp vụ: `AuthService`, `ProductService`, `ReservationService`, `OrderService`, `PriceChangeService`, `SkuPriceResolver`, `WarehouseService`. Một số chỗ dùng reflection (`GetType().Name == "PostgresException"`) để né tham chiếu Npgsql.

## Kế hoạch fix

1. Thêm interface `IDbErrorTranslator` trong `Application/Common/Interfaces` với các hàm như `IsUniqueViolation(Exception, string? constraint)`, `IsForeignKeyViolation(...)`, `IsCheckViolation(...)`.
2. Hiện thực bằng Npgsql trong `OISM.Infrastructure`.
3. Thay 8 chỗ trên, giữ nguyên `errorCode` và status đang trả.
4. Test: các integration test trùng email/số điện thoại, trùng SKU/barcode, trùng mốc giá, kho có phụ thuộc vẫn PASS.

## Cách đã sửa

Infrastructure đọc SQLSTATE và tên ràng buộc qua `IDbErrorTranslator`. Application giữ mã nghiệp vụ cho từng ràng buộc. Lỗi kho chỉ được nhận là FK khi PostgreSQL trả đúng SQLSTATE của khóa ngoại.

## Kiểm chứng

Unit test bộ dịch lỗi và suite tích hợp backend qua CI với PostgreSQL 16, gồm các luồng auth, SKU, barcode, kho và giá. [GitHub Actions #134](https://github.com/khoalvd839764-netizen/OISMS/actions/runs/42185858352)

## Điều kiện đóng

- Không còn vi phạm mô tả ở trên trong code (kiểm bằng `grep`/build).
- Toàn bộ unit test, integration test liên quan và `npm test` của POS PASS; thay đổi chạm transaction, khóa, ledger phải có bằng chứng trên PostgreSQL thật.
- Cập nhật trạng thái trong file này và [CHECKLIST.md](../CHECKLIST.md).
