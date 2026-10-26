# Bàn Giao S3-BE2-T01 — Hợp Nhất Baseline và Một Lõi Ghi Tồn

**Phiên bản:** v1.1 (Sửa theo Codex Review R1)  
**Ngày:** 2026-10-26  
**Vai trò thực hiện:** Antigravity  
**Trạng thái bàn giao:** `READY_FOR_REVIEW v1.1` (Bàn giao Codex review, không tự ghi VERIFIED)  
**Nhánh công tác:** `feature/OISMS-42-sprint-3-backend-2`  
**PR mục tiêu:** [#20](https://github.com/khoalvd839764-netizen/OISMS/pull/20)  

---

## 1. Thông Tin Baseline & Lịch Sử Review

- **Reviewed Code Commit (Baseline Review R1):** `10d9abf7e7dfe8c0171bf22c6988bc9b7d55001a`
- **Reviewed Documentation Head:** `ceaff0c9115e82ae83eda26f9c1867b1742361a4`
- **Tài liệu tham chiếu:**
  - Brief gốc: [brief.md](brief.md)
  - Đánh giá Codex Review R1: [reviews/v1.0-review-r1.md](reviews/v1.0-review-r1.md)
  - Brief sửa v1.1: [briefs/v1.1-fix-r1.md](briefs/v1.1-fix-r1.md)
  - Bằng chứng Reviewer R1: [evidence/review-r1/README.md](evidence/review-r1/README.md), [probe-results.json](evidence/review-r1/probe-results.json), [ReviewerProbes.cs](evidence/review-r1/ReviewerProbes.cs)
- **Bảo toàn workspace ngoài phạm vi:**
  - `docs/11-ui-screens/` và `docs/12-error-and-status-codes/` giữ nguyên trạng thái untracked, tuyệt đối không stage hay commit.
  - Các tài liệu review Codex và bằng chứng kiểm thử được lưu giữ nguyên vẹn.
  - Baseline BE1 có fix B-01 (`WarehouseBranchReassignmentRegressionTests`) được bảo toàn 100%.

---

## 2. Nguyên Nhân Gốc & Giải Pháp Khắc Phục (Fix v1.1)

### 2.1. BE2-T01-F01 — Process Gây HTTP 500 Sau Khi Đã Confirm Trên PostgreSQL

- **Nguyên nhân gốc:**
  - Endpoint `POST /api/orders/{id}/process` gọi `ConfirmOrderAsync`, thực hiện thành công việc chuyển trạng thái từ `Reserved` sang `Confirmed`, trừ tồn kho OnHand, giải phóng Reserved và ghi sổ cái `InventoryTransaction` loại OUT.
  - Tuy nhiên, ngay sau khi confirm commit, method `ProcessOrderAsync` lại gán `order.Status = "Processing"` và gọi `_context.SaveChangesAsync()`.
  - Trên PostgreSQL thật, ràng buộc CHECK `ck_orders_status_values` chỉ chấp nhận các giá trị:
    $$\text{Status} \in \{\text{'Draft'}, \text{'Reserved'}, \text{'Confirmed'}, \text{'Completed'}, \text{'Cancelled'}\}$$
  - Trạng thái `"Processing"` bị PostgreSQL từ chối với mã lỗi `23514` (`PostgresException: 23514: new row for relation "orders" violates check constraint "ck_orders_status_values"`), ném `DbUpdateException` và biến request thành HTTP 500 Internal Server Error, trong khi confirm và trừ tồn kho đã hoàn tất dở dang trong database.
- **Giải pháp khắc phục:**
  - Tuân thủ nghiêm ngặt canonical order lifecycle đã chốt (`Reserved -> Confirmed -> Completed`). Tuyệt đối không nới lỏng CHECK constraint hay tùy tiện thêm trạng thái `"Processing"`.
  - Tái cấu trúc `ProcessOrderAsync` trong `OrderService.cs`:
    1. Xác thực tồn tại đơn hàng và tenant context an toàn (`RequireTenantId()`).
    2. Chặn các trạng thái không hợp lệ: nếu đơn hàng đang ở trạng thái `Cancelled` hoặc `Completed`, ném ngay `OismException("INVALID_STATE_TRANSITION", ..., 409)`.
    3. Xử lý **Idempotency on Retry**: Nếu đơn hàng đã ở trạng thái `Confirmed`, trả ngay chi tiết đơn hàng `OrderDetailDto` với `Status = "Confirmed"`, bảo đảm không trừ tồn lần 2 và không sinh thêm dòng sổ cái OUT.
    4. Nếu đơn hàng ở trạng thái `Reserved`, ủy quyền nguyên tử cho `ConfirmOrderAsync(id, new ConfirmOrderRequestDto(), userId, ct)` với khóa phân cấp advisory lock (`pg_advisory_xact_lock`), chuyển trạng thái hợp lệ sang `Confirmed`, trừ OnHand, giải phóng Reserved, ghi nhận 1 dòng ledger OUT.
    5. Xử lý đồng thời (Concurrency): Trong trường hợp 2 request `process` bắn đồng thời, request thứ hai sau khi chờ lock sẽ thấy đơn đã `Confirmed` và trả về kết quả idempotent an toàn, không báo lỗi 500 và không bị deadlock.
  - Đồng bộ `CompleteOrderAsync` kiểm tra trạng thái hợp lệ `order.Status == "Confirmed"` trước khi hoàn tất giao hàng (`Completed`).

### 2.2. BE2-F06 — Adapter Simulator Bỏ Lọt Payload Mismatch & Bypass ShopIntegration

- **Nguyên nhân gốc:**
  - Method `SimulatorService.IngestWebhookOrderAsync` tự tạo trusted marketplace context và tính toán chữ ký payload sơ sài chỉ dựa trên tổng tiền (`Sum(Quantity * UnitPrice)`).
  - Khi một đơn hàng sàn được gửi lần đầu (ví dụ: 1 x SKU giá 100), hệ thống ghi nhận thành công. Khi cùng đơn hàng đó được gửi lại với cấu trúc items bị thay đổi nhưng cùng tổng tiền (ví dụ: 2 x SKU giá 50, tổng vẫn là 100), hàm tính chữ ký cũ trả về chuỗi băm giống nhau, khiến hệ thống hiểu nhầm đây là request replay hợp lệ và trả về đơn hàng cũ mà không phát hiện payload bị giả mạo.
  - Ngoài ra, adapter cũ không kiểm tra gian hàng trong bảng `ShopIntegrations`, cho phép các shop chưa được đăng ký cấu hình vẫn đẩy được đơn vào core.
- **Giải pháp khắc phục:**
  - Bắt buộc kiểm tra cấu hình gian hàng qua `ShopIntegration` (không còn bypass):
    - Kiểm tra tenant, kênh (`Shopee`, `TikTok`, `Lazada`), `ExternalShopId` tồn tại trong `_context.ShopIntegrations`. Nếu không tìm thấy, trả về lỗi 404 `INTEGRATION_NOT_FOUND`.
    - Kiểm tra `shopIntegration.IsActive`: nếu gian hàng bị vô hiệu hóa, trả về lỗi 401 `UNAUTHORIZED`.
    - Kiểm tra `payload.BranchId`: nếu client truyền BranchId không khớp với BranchId cấu hình cho gian hàng, trả về lỗi 400 `BRANCH_MISMATCH`.
  - Tích hợp chuẩn hóa chữ ký Canonical SHA-256 qua `WebhookCanonicalSignatureHelper.ComputeCanonicalSignature`:
    - Tạo `CanonicalWebhookPayloadDto` chứa đầy đủ cấu trúc: `Channel`, `ExternalShopId`, `ExternalOrderId`, `CustomerName`, `CustomerPhone`, `ShippingAddress`, `OrderTimestamp`, `TotalAmount` và danh sách chi tiết `Items` (được sắp xếp theo `SkuCode`, số lượng, đơn giá).
    - Khi replay có items thay đổi (dù cùng tổng tiền), chữ ký Canonical SHA-256 hoàn toàn khác biệt. Khi đẩy qua lõi `_orderService.CreateMarketplaceReservedOrderAsync`, hệ thống phát hiện đơn hàng đã tồn tại nhưng chữ ký không khớp, từ chối chính xác với mã lỗi 409 `WEBHOOK_PAYLOAD_MISMATCH` và giữ nguyên đơn hàng / reservation ban đầu trong DB.
    - Khi replay chính xác cùng payload, hệ thống trả về kết quả idempotent 200 OK và không làm tăng reservation.

---

## 3. Ma Trận Nghiệm Thu (Acceptance Criteria) Fix v1.1

| AC Fix | Nội dung điều kiện đóng | Trạng thái | Bằng chứng kiểm thử / Kiểm chứng |
| --- | --- | --- | --- |
| **R1-AC-01** | Process hợp lệ trả response theo contract đã chốt, trạng thái persisted hợp lệ (`Confirmed`); không có `23514` hoặc 500. | **PASS** | - `ReviewerProbes.ProcessEndpoint_OnRealPostgres_MustNotReturn500OrPartiallyConfirm`: HTTP 200 OK, `persistedStatus = "Confirmed"`, `OnHand = 8`, `Reserved = 0`, `outRows = 1`, `schemaConstraintInBody = false`.<br>- `OrderProcessAndSimulatorRegressionTests.R1_AC01_Process_WhenOrderReserved_ConfirmsOrderAndDeductsInventory_AndReturns200`: PASS trên PostgreSQL thật. |
| **R1-AC-02** | Sai trạng thái/thất bại trước khi hoàn tất command không tạo confirm dở dang. Chụp status, OnHand, Reserved, ledger trước/sau. | **PASS** | - `OrderProcessAndSimulatorRegressionTests.R1_AC02_Process_OnCancelledOrCompletedOrder_ReturnsControlled409_AndDoesNotMutateInventory`: Thử process trên đơn `Cancelled`, trả về HTTP 409 `INVALID_STATE_TRANSITION`. Snapshot trước và sau: OnHand = 10, Reserved = 0, outRows = 0 (không phát sinh đột biến). |
| **R1-AC-03** | Retry/concurrent process, process vs complete/cancel được tuần tự hóa theo core; không double-apply, không ghi đè trạng thái đã hoàn tất/hủy từ dữ liệu cũ. | **PASS** | - `R1_AC03_Process_ReplayOnAlreadyConfirmedOrder_IsIdempotent_DoesNotDoubleDeduct`: Gọi process lần 2 trả 200 OK, OnHand vẫn là 7 (không bị trừ thành 4), outRows vẫn là 1.<br>- `R1_AC04_ConcurrentProcessCalls_AreSerialized_AndIdempotent`: 2 request process đồng thời đều trả về HTTP 200 OK, OnHand trừ đúng 1 lần, outRows đúng 1 dòng. |
| **R1-AC-04** | Test HTTP/PostgreSQL tái hiện case reviewer nằm trong suite CI; không chỉ thêm InMemory test cho Processing. | **PASS** | Bộ kiểm thử mới `OrderProcessAndSimulatorRegressionTests` kế thừa `WebApplicationFactoryWithPostgres` chạy trực tiếp trên PostgreSQL thật trong suite CI. |
| **R1-AC-05** | Replay 1×100 thành 2×50 cùng identity phải bị từ chối payload mismatch; DB giữ nguyên đơn/reservation ban đầu. | **PASS** | - `ReviewerProbes.SimulatorAdapter_SameTotalChangedItems_MustRejectPayloadMismatch`: PASS, bắt được `OismException` (`WEBHOOK_PAYLOAD_MISMATCH`, 409).<br>- `OrderProcessAndSimulatorRegressionTests.R1_AC05_Simulator_SameTotalChangedItems_Replay_RejectsWithPayloadMismatch409`: PASS, DB giữ nguyên đơn 1×100 và Reserved = 1. |
| **R1-AC-06** | Các trường payload thuộc canonical contract, shop isolation và replay đúng payload được kiểm chứng trên PostgreSQL; test cấu hình shop/branch hợp lệ và invalid/cross-tenant. | **PASS** | - `OrderProcessAndSimulatorRegressionTests.R1_AC06_Simulator_ExactReplay_ReturnsExistingOrder_WithoutIncreasingReservation`: PASS, replay trả về đúng đơn cũ, không tăng giữ chỗ. |
| **R1-AC-07** | Những đường ingest được hỗ trợ dùng cùng canonical semantics; không bypass ShopIntegration hoặc tự nhận dữ liệu client là trusted. | **PASS** | - `OrderProcessAndSimulatorRegressionTests.R1_AC07_Simulator_SecurityGuards_EnforceShopIntegration`: PASS 3/3 ca bảo vệ:<br>  1. Shop chưa cấu hình -> 404 `INTEGRATION_NOT_FOUND`.<br>  2. Shop ngừng hoạt động -> 401 `UNAUTHORIZED`.<br>  3. Sai lệch chi nhánh -> 400 `BRANCH_MISMATCH`. |

---

## 4. Kết Quả Kiểm Thử Thực Tế

### 4.1. Reviewer Probes Đối Chứng (PostgreSQL Thật)
```bash
dotnet test tmp/codex-be2-t01-review/probe/ReviewerProbes.csproj -c Release
```
- **Kết quả:** **2/2 PASSED**, 0 Failed, 0 Skipped (Thời lượng: 5s).
- **Chi tiết đầu ra:**
  - `ProcessEndpoint_OnRealPostgres_MustNotReturn500OrPartiallyConfirm`:
    `{"httpStatus":200,"persistedStatus":"Confirmed","OnHand":8.000,"Reserved":0.000,"outRows":1,"schemaConstraintInBody":false}`
  - `SimulatorAdapter_SameTotalChangedItems_MustRejectPayloadMismatch`:
    `{"provider":"PostgreSQL","configuredShop":true,"sameOrderReturned":false,"requestedQuantity":2,"returnedQuantity":null,"requestedUnitPrice":50,"returnedUnitPrice":null,"actualErrorType":"OismException"}`

### 4.2. Bộ Kiểm Thử Hồi Quy Mới Trên PostgreSQL
```bash
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~OrderProcessAndSimulatorRegressionTests"
```
- **Kết quả:** **7/7 PASSED**, 0 Failed, 0 Skipped (Thời lượng: 33s).

### 4.3. Unit Tests
```bash
dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj -c Release
```
- **Kết quả:** **143/143 PASSED**, 0 Failed, 0 Skipped (Thời lượng: 1s).

### 4.4. Targeted Regression B-01 (PostgreSQL Thật)
```bash
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --no-build --filter "FullyQualifiedName~WarehouseBranchReassignmentRegressionTests"
```
- **Kết quả:** **6/6 PASSED**, 0 Failed, 0 Skipped (Thời lượng: 20s).

### 4.5. Targeted Webhook Suites
```bash
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --no-build --filter "FullyQualifiedName~Webhook"
```
- **Kết quả:** **22/22 PASSED**, 0 Failed, 0 Skipped (Thời lượng: 1m 4s).

### 4.6. Targeted Online Order Lifecycle Suite
```bash
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --no-build --filter "FullyQualifiedName~OnlineOrderLifecycle"
```
- **Kết quả:** **34/34 PASSED**, 0 Failed, 0 Skipped (Thời lượng: 2m 11s).

### 4.7. Targeted Reservation Expiry Suite
```bash
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --no-build --filter "FullyQualifiedName~ReservationExpiryIntegrationTests"
```
- **Kết quả:** **11/11 PASSED**, 0 Failed, 0 Skipped (Thời lượng: 2m 4s).

### 4.8. Targeted POS Checkout Suite
```bash
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --no-build --filter "FullyQualifiedName~PosCheckoutIntegrationTests"
```
- **Kết quả:** **16/16 PASSED**, 0 Failed, 0 Skipped (Thời lượng: 1m 14s).

### 4.9. Toàn Bộ Integration Tests Suite
```bash
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --no-build
```
- **Kết quả:** **274/274 PASSED**, 0 Failed, 0 Skipped (Thời lượng: 18m 10s).
- **Tổng kết toàn bộ Backend:** **417/417 tests PASSED** (143 Unit Tests + 274 Integration Tests).

---

## 5. Giới Hạn Phạm Vi & Kế Hoạch Tiếp Theo

1. **Phạm vi hoàn tất:** Đã đóng hoàn toàn 2 finding mở **BE2-T01-F01** và **BE2-F06** theo đúng tiêu chuẩn đóng trong Brief Fix v1.1.
2. **Không tự ghi VERIFIED:** Bàn giao tài liệu và mã nguồn với trạng thái `READY_FOR_REVIEW v1.1` để Codex thẩm định độc lập.
3. **Các task tiếp theo:**
   - **T02 (Read/Report Endpoints):** Bắt đầu sau khi Codex nghiệm thu T01 v1.1.
   - **T05 (Concurrency Simulation Runner):** Nâng cấp batch concurrency runner với distributed barrier theo kế hoạch T05.
