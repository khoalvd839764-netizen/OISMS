# BF-032 — Application chứa SQL PostgreSQL thô (nợ đã biết)

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa và kiểm chứng |
| **Ưu tiên** | P2 |
| **Khu vực** | Backend / Clean Architecture / Application |
| **Phát hiện** | 06/11/2026, rà soát lại Clean Architecture sau Upgrade 06 (commit `6ad3cf6`) |
| **Đợt fix** | Đợt 3 (cần nhóm đồng ý) |
| **Kế hoạch chung** | [CHECKLIST.md mục E](../CHECKLIST.md#e-kế-hoạch-hoàn-thiện-clean-architecture-bf-028035) |

## Mô tả lỗi

6 service trong `OISM.Application` gọi SQL PostgreSQL thô qua `ExecuteSqlRawAsync`/`FromSqlRaw` (`pg_advisory_xact_lock`, `SELECT ... FOR UPDATE`, `SET CONSTRAINTS ALL IMMEDIATE`):

| Service | Số chỗ |
| :--- | :---: |
| `Orders/Services/OrderService.cs` | 46 |
| `StockCounts/Services/StockCountService.cs` | 34 |
| `StockTransfers/Services/StockTransferService.cs` | 24 |
| `StockCounts/Services/StockCountScheduleService.cs` | 10 |
| `Inventory/Services/GoodsReceiptService.cs` | 9 |
| `Inventory/Services/ReservationService.cs` | 5 |

Đây là **nợ đã biết** ghi trong CLAUDE.md ("không tiện tay refactor"): thứ tự khóa và transaction ở các service này đã được chứng minh đúng trên PostgreSQL thật.

**Ảnh hưởng:** Application phụ thuộc PostgreSQL; không đổi được DB; chuỗi SQL khóa lặp lại ở nhiều nơi.

## Kế hoạch fix

1. Mở rộng `ISqlLockService` (đã có từ Upgrade 06) thành các hàm có ngữ nghĩa: `LockSkuStockAsync(tenantId, branchId, skuIds)` (sắp xếp SKU cố định), `LockOrderAsync`, `LockRequestKeyAsync`, `LockStockCountAsync`, `SetConstraintsImmediateAsync`. Hiện thực trong `OISM.Infrastructure/Services/SqlLockService.cs`.
2. Chuyển từng service, **giữ nguyên thứ tự khóa và vị trí lấy khóa sau khi mở transaction**: `ReservationService` → `GoodsReceiptService` → `StockTransferService` → `StockCountScheduleService` → `StockCountService` → `OrderService`. Mỗi service một PR.
3. Bằng chứng bắt buộc trên PostgreSQL thật cho mỗi PR: 50 request đồng thời tranh 1 SKU, replay idempotency, duyệt kiểm kê song song, nhập vượt phiếu mua, chuyển kho đồng thời. Mock/InMemory không đủ.
4. Cập nhật CLAUDE.md và `docs/04-architecture-and-design/01-clean-architecture.md` khi xong.

## Cách đã sửa

Sáu service gọi các thao tác khóa theo nghiệp vụ qua `ISqlLockService`. SQL giữ nguyên tham số tenant, khóa request, khóa chứng từ và khóa dòng. Infrastructure lấy khóa SKU theo thứ tự tăng dần. Xác nhận nhập kho dùng thứ tự phiếu nhập → phiếu mua → SKU, còn hết hạn giữ chỗ giữ thứ tự SKU → request key → đơn hàng → khóa dòng.

## Kiểm chứng

17 test tích hợp chọn lọc trên PostgreSQL 16.15 đã qua, gồm 50 yêu cầu tranh một đơn vị tồn, replay idempotency, duyệt kiểm kê song song, nhập vượt phiếu mua và xuất nhận chuyển kho đồng thời. Toàn bộ suite backend cũng qua trên PostgreSQL 16 trong CI. [GitHub Actions #136](https://github.com/khoalvd839764-netizen/OISMS/actions/runs/42193764424)

## Điều kiện đóng

- Không còn vi phạm mô tả ở trên trong code (kiểm bằng `grep`/build).
- Toàn bộ unit test, integration test liên quan và `npm test` của POS PASS; thay đổi chạm transaction, khóa, ledger phải có bằng chứng trên PostgreSQL thật.
- Cập nhật trạng thái trong file này và [CHECKLIST.md](../CHECKLIST.md).
