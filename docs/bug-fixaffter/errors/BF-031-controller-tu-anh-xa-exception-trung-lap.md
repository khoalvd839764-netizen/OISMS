# BF-031 — Controller tự ánh xạ exception, trùng lặp và không đồng nhất

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | 🟡 Đã sửa, chờ kiểm chứng PostgreSQL trên CI |
| **Ưu tiên** | P2 |
| **Khu vực** | Backend / Clean Architecture / WebApi |
| **Phát hiện** | 06/11/2026, rà soát lại Clean Architecture sau Upgrade 06 (commit `6ad3cf6`) |
| **Đợt fix** | Đợt 1 |
| **Kế hoạch chung** | [CHECKLIST.md mục E](../CHECKLIST.md#e-kế-hoạch-hoàn-thiện-clean-architecture-bf-028035) |

## Mô tả lỗi

19 controller tự `try/catch` rồi dựng ProblemDetails (khoảng 170 lần gọi `CreateProblemResult` hoặc `BadRequest`/`Conflict`/`NotFound`), trùng với việc `ExceptionHandlingMiddleware` đã làm. Ngoài ra có 25 bản sao `GetCurrentUserId()` và 14 lần kiểm tra `ModelState.IsValid` thừa (mọi controller đã có `[ApiController]` và `InvalidModelStateResponseFactory` trong `Program.cs`).

Thống kê cách ánh xạ hiện tại (06/11/2026):

| Exception | Cách xử lý | Controller |
| :--- | :--- | :--- |
| `OismException` | ProblemDetails theo `ErrorCode`, `StatusCode` (39 chỗ) | GoodsReceipts, Inventory, Orders, PosCheckoutActionHelper, Pricing, PurchaseOrders |
| `OismException` | `BadRequest`/`Conflict`/`NotFound`/`Unauthorized`/`UnprocessableEntity` với body `{ message }`, không phải ProblemDetails (25 chỗ) | Auth, Categories, PosCatalog, Products, Warehouses, Units |
| `KeyNotFoundException` | 404 `NOT_FOUND` | GoodsReceipts, Inventory, Orders |
| `InvalidOperationException` | 409 `INVALID_STATE_TRANSITION` ở chỗ này, 409 `VALIDATION_ERROR` ở chỗ khác, `Conflict()` ở chỗ khác nữa | GoodsReceipts, Inventory, Orders |
| `ArgumentException` | 400 `VALIDATION_ERROR` | GoodsReceipts, Inventory |
| `PriceMismatchException` | ProblemDetails tự dựng kèm `currentPrices` | Orders, PosCheckoutActionHelper |
| `Exception` | `BadRequest`/500 tự dựng | Auth, Simulator, Webhooks |

**Ảnh hưởng:** cùng một lỗi nghiệp vụ trả hình dạng body khác nhau tùy endpoint (ProblemDetails hay `{ message }`), mã lỗi không thống nhất; frontend phải đọc cả `detail` lẫn `message`. Sửa một quy tắc phải sửa nhiều nơi.

## Kế hoạch fix

1. Mở rộng `ExceptionHandlingMiddleware` (hoặc `IExceptionHandler` của ASP.NET 8) để ánh xạ tập trung: `OismException` → theo `ErrorCode`/`StatusCode`; `KeyNotFoundException` → 404 `NOT_FOUND`; `ArgumentException` → 400 `VALIDATION_ERROR`; `PriceMismatchException` → 400 `PRICE_MISMATCH` kèm `currentPrices`; còn lại → 500.
2. `InvalidOperationException` đang mang nghĩa khác nhau ở mỗi service: chuyển dần các chỗ ném sang `OismException` có mã rõ (`INVALID_STATE_TRANSITION`, `INSUFFICIENT_STOCK`...) trước khi xóa catch tương ứng; không đổi status code mà frontend và test đang dựa vào.
3. Xóa `try/catch` và `CreateProblemResult` trong controller theo từng nhóm (mỗi nhóm một commit): Auth, Catalog (Categories, Brands, Products, Units), Inventory/GoodsReceipts, Orders/POS, StockTransfers/StockCounts, Pricing/PurchaseOrders/SalesInvoices, Simulator/Webhooks.
4. Gom `GetCurrentUserId()` vào extension `ClaimsPrincipal.GetUserId()` hoặc `ICurrentUserService`.
5. Bỏ 14 lần kiểm tra `ModelState.IsValid` thừa.
6. Endpoint auth đang trả `{ message }`: chốt với frontend trước khi chuyển sang ProblemDetails (cập nhật `authAdapter.ts`, `posApi.ts` nếu cần).
7. Test: toàn bộ integration test (các test đang so `errorCode` và HTTP status là lưới an toàn), `npm test` POS, đối chiếu lại [danh mục mã lỗi](../../12-error-and-status-codes/02-error-codes-catalog.md).

## Cách đã sửa

Gom ánh xạ exception vào bộ lọc MVC và dùng chung bộ dựng ProblemDetails với middleware. Giữ các dạng body đã có cho từng endpoint, kể cả `message` và `currentPrices`. Dùng chung extension đọc user ID và bộ kiểm tra ModelState của `[ApiController]`.

## Điều kiện đóng

- Không còn vi phạm mô tả ở trên trong code (kiểm bằng `grep`/build).
- Toàn bộ unit test, integration test liên quan và `npm test` của POS PASS; thay đổi chạm transaction, khóa, ledger phải có bằng chứng trên PostgreSQL thật.
- Cập nhật trạng thái trong file này và [CHECKLIST.md](../CHECKLIST.md).
