# Báo cáo sau nâng cấp: Vòng đời đơn hàng và tính nguyên tử theo kênh (OISMS-59-G3A)

| Mục | Nội dung |
| --- | --- |
| Task | `OISMS-59-G3A` (Gói 3 Phase A — Prompt 1/3) |
| Ngày kiểm tra | 15/11/2026 |
| Phạm vi | Docs (`fr-06-omnichannel-orders.md`, `04-state-machines.md`, `03-reliability-and-acid.md`), comment XML trong `ReservationService.cs`, Integration Tests trên PostgreSQL thật (`OnlineOrderLifecycleIntegrationTests.cs`, `PostgresOrderLifecycleAtomicityTests.cs`) |
| Trạng thái | Hoàn tất bổ sung tài liệu, chú thích XML và kiểm thử PostgreSQL (chờ review) |

## Đã làm

1. **Tài liệu hóa ranh giới giao dịch theo kênh và quy ước `Draft`:**
   - `docs/02-functional-requirements/fr-06-omnichannel-orders.md` (v1.1.0): làm rõ ở `FR-ORD-02` và mục 2 rằng `Draft` chỉ là trạng thái khởi tạo nội bộ trong giao dịch tạo đơn, không lưu `Draft` riêng với đơn online (`Admin`, `Shopee`, `TikTok`, `Lazada`), và mô tả luồng giữ chỗ kỹ thuật POS qua `POST /api/inventory/reserve`.
   - `docs/04-architecture-and-design/04-state-machines.md` (v1.5.0): giữ nguyên mục `1.1` (Máy trạng thái phiếu giữ chỗ) để bảo toàn anchor liên kết tài liệu tri thức, bổ sung mục `1.2` bảng ranh giới giao dịch theo kênh (`POS`: 1 giao dịch; `Admin`/sàn: 2 giao dịch; `Hủy`/hết hạn: 1 giao dịch chỉ giảm `Reserved`, không ghi `IN` bù) và ghi chú luồng giữ chỗ kỹ thuật `Draft` của POS.
   - `docs/03-non-functional-requirements/03-reliability-and-acid.md` (v1.1.0): bổ sung mục 2 ghi nhận cách diễn giải `NFR-SEC-02` đã chốt theo từng kênh bán.

2. **Comment XML trong `ReservationService.cs`:**
   - `src/backend/src/OISM.Application/Features/Inventory/Services/ReservationService.cs`: bổ sung comment XML trên class `ReservationService` và method `ReserveStockAsync` (cùng chú thích dòng tạo đơn) giải thích đây là luồng giữ chỗ kỹ thuật của POS qua `POST /api/inventory/reserve`, đơn phải giữ nguyên trạng thái `Draft` vì `Order.CanCancel` và `Order.CanExpireReservations` của kênh `POS` chỉ cho hủy/hết hạn khi đơn ở `Draft` chưa thanh toán.

3. **Bổ sung và gia cố test PostgreSQL thật (`OISM.IntegrationTests`):**
   - Bổ sung class `test-oisms/backend/OISM.IntegrationTests/PostgresOrderLifecycleAtomicityTests.cs` (4 test):
     - `AC01_IllegalStateTransitions_ConfirmCancelled_And_DoubleConfirm_ShouldReturn409InvalidStateTransition`: xác nhận đơn `Cancelled` và xác nhận lần hai đều trả `409 Conflict` (`INVALID_STATE_TRANSITION`), không trừ tồn hai lần.
     - `AC04_ConcurrentConfirmAndReservationExpiryJob_OnExpiredOrder_TenIterations_ExactlyOneWins`: chạy song song `POST /api/orders/{id}/confirm` và `ReservationExpiryJob.ExecuteAsync` trên cùng đơn đã hết hạn qua 10 vòng lặp; mỗi vòng đúng một bên thắng (`ReservationExpiryJob` thắng, `Confirm` nhận `409`), `Reserved` giảm đúng 1 lần về `0`, không phát sinh dòng `IN` bù.
     - `AC05_PosDraftFromReserveApi_Expires_JobReleasesStockAndCancelsOrder`: gọi `POST /api/inventory/reserve` sinh đơn `POS`/`Draft` + giữ chỗ `Active`, hết hạn trong PostgreSQL rồi chạy `ReservationExpiryJob.ExecuteAsync`; xác minh đơn chuyển `Cancelled`, giữ chỗ sang `Expired`, `Reserved` hoàn về `0`, không ghi thêm sổ kho.
     - `AC06_ReconciliationAfterLifecycles_LedgerNetEqualsOnHand_AndReservedEqualsActiveSum`: chạy đầy đủ vòng đời `tạo -> xác nhận -> hoàn tất` và `tạo -> hủy` (kèm 1 đơn đang `Reserved` để kiểm chứng trạng thái `Active > 0` trước khi hủy nốt); đối soát `SUM(IN) - SUM(OUT) == OnHand` và `Reserved == SUM(Reservation Active)` ở cả cấp chi nhánh và cấp kho.
   - Bổ sung kiểm chứng trong `test-oisms/backend/OISM.IntegrationTests/OnlineOrderLifecycleIntegrationTests.cs`:
     - `AC07_AtomicRollback_OneItemOutOfStock_RollsBackEntireCart` (AC-02): bổ sung kiểm tra `WarehouseInventoryBalances.Reserved == 0`, bảng `Reservations` rỗng và số dòng `InventoryTransactions` không đổi.
     - `T3_F05_FaultInjection_ConfirmOrder_MidstreamFailure_ShouldRollbackCompletely_AndAllowRetry` (AC-03): bổ sung kiểm tra `OrderItem.CostPrice == null`, `WarehouseInventoryBalances`, tổng số dòng `InventoryTransactions` không đổi và không tạo `SalesInvoice`.
   - Các tiêu chí đã có test tương đương từ trước (không viết trùng):
     - AC-01 (`Reserved -> Completed`, `Confirmed -> Cancelled`): `AC04_CompleteOrder_FromNonConfirmed_ShouldReturn409`, `AC05_CancelOrder_FromConfirmedOrCompleted_ShouldReturn409`.
     - AC-02: `AC07_AtomicRollback_OneItemOutOfStock_RollsBackEntireCart`.
     - AC-03: `T3_F05_FaultInjection_ConfirmOrder_MidstreamFailure_ShouldRollbackCompletely_AndAllowRetry`.

## Ảnh hưởng chức năng hiện có

- **Không đổi chức năng hay luồng đơn hàng:** Không sửa logic trong `Order`, `OrderIntakeService`, `OrderService`, `OrderService.Expiry`, `ReservationService`, `ReservationExpiryJob`, schema DB hay frontend. Chỉ bổ sung comment XML trong `ReservationService.cs`, tài liệu nghiệp vụ/kiến trúc và test tích hợp PostgreSQL.

## Thay đổi cơ sở dữ liệu

- **Không đổi DB:** Không thêm bảng, cột, ràng buộc hay migration mới.

## Kiểm chứng

| Lệnh | Phạm vi / Provider | Kết quả |
| --- | --- | --- |
| `dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal` | Toàn bộ solution backend | PASS (0 warning, 0 error) |
| `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false --no-build` | Toàn bộ UnitTests (gồm `KnowledgeFilesTests`) | PASS 381/381 (Failed: 0, Skipped: 0) |
| `dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --no-build --filter "FullyQualifiedName~OnlineOrderLifecycle\|FullyQualifiedName~ReservationExpiry\|FullyQualifiedName~PostgresOrderLifecycleAtomicityTests"` | PostgreSQL thật (`PostgresTestEnvironment`, `WebApplicationFactoryWithPostgres`) | PASS 49/49 (Failed: 0, Skipped: 0) |

## Giới hạn

- Không thay đổi mã nguồn thực thi; phạm vi kiểm thử tích hợp chỉ chạy bộ lọc `OnlineOrderLifecycle`, `ReservationExpiry` và `PostgresOrderLifecycleAtomicityTests` (chưa chạy toàn bộ các suite integration test khác ngoài Phase A và chưa build frontend do không chạm frontend).
- Trong PostgreSQL thật, ràng buộc `ck_inventory_balances_reservation_limit` (`reserved <= on_hand`) và trigger `oisms_check_stock` chặn việc hạ `on_hand` xuống dưới `reserved` trước khi xác nhận, nên AC-03 được kiểm chứng qua `MidstreamFailureInterceptor` (tiêm lỗi ngay sau `SaveChangesAsync` trong giao dịch PostgreSQL trước `COMMIT`, rollback toàn bộ ghi nhận `OnHand`, `Reserved`, `inventory_transactions`, `OrderItem.CostPrice` và `SalesInvoices`).
