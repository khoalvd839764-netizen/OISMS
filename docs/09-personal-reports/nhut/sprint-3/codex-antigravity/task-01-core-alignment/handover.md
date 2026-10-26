# Bàn Giao S3-BE2-T01 — Hợp Nhất Baseline và Một Lõi Ghi Tồn

**Phiên bản:** v1.0  
**Ngày:** 2026-10-26  
**Vai trò thực hiện:** Antigravity  
**Trạng thái bàn giao:** `READY_FOR_REVIEW v1.0` (Bàn giao Codex review, không tự ghi VERIFIED)  
**Nhánh công tác:** `feature/OISMS-42-sprint-3-backend-2`  
**PR mục tiêu:** [#20](https://github.com/khoalvd839764-netizen/OISMS/pull/20)  

---

## 1. Thông Tin Baseline & Ancestry

- **Input Head BE2 (Nhựt):** `d7fa4bc8738a1f22053d9b313f8e90c423b0483d` (PR #20).
- **Baseline BE1 ban đầu:** `4206cc4b12f0128ee08927dc83550c9e8db5cd6e` (còn lỗi B-01).
- **Baseline BE1 có Fix B-01 đã kiểm chứng:**
  - Commit fix: `b5ba71ef7ac2bf055f4ee8963e159d4f27c69aae` (`[OISMS-40] fix: allow clean warehouse branch reassignment`).
  - PR #21 đã được CI kiểm chứng (Run `#40303797966` green 396/396 tests) và merge vào `develop` tại commit `4dbfd8eef0c2acea48171c56d406a3897aa45df8`.
  - Ancestry merge vào `feature/OISMS-42-sprint-3-backend-2`: tích hợp trực tiếp từ `origin/develop` (`4dbfd8eef0c2acea48171c56d406a3897aa45df8`), chứa đầy đủ fix B-01 và toàn bộ lõi BE1 Sprint 3 đã nghiệm thu.
- **Tích hợp đồng thời commit mới của Nhựt:**
  - Commit `a477d53d457da46590d46638d46f6e31341afff4` (`[OISMS-41] feat(orders): Bo sung endpoint process, complete don hang va alias inventory-transactions`): giải quyết xung đột tại `OrdersController.cs`, giữ route alias `[HttpGet("/api/inventory-transactions")]`, bổ sung `RequireTenantId()` cho `ProcessOrderAsync` / `CompleteOrderAsync`, đồng bộ hỗ trợ cả trạng thái `Confirmed` và `Processing` khi hoàn tất đơn.
- **Bảo toàn workspace ngoài phạm vi:**
  - `docs/11-ui-screens/` và `docs/12-error-and-status-codes/` giữ nguyên trạng thái untracked, tuyệt đối không stage hay commit.
  - Gói tài liệu `docs/09-personal-reports/nhut/sprint-3/codex-antigravity/` được lưu giữ và cập nhật đầy đủ.

---

## 2. Các Finding Đã Khắc Phục (T01 Scope)

| Finding ID | Mức độ | Vấn đề gốc | Giải pháp khắc phục tại T01 |
| --- | --- | --- | --- |
| **BE2-F01** | **P0** | Engine đơn hàng riêng của BE2 không bảo vệ tồn kho (tạo đơn Reserved nhưng không tạo `Reservation`, confirm trừ tồn bằng `Math.Max(0, OnHand)`, ghi ledger với `WarehouseId=Guid.Empty`, không có advisory locking). | Loại bỏ hoàn toàn engine ghi tồn riêng; thống nhất sử dụng 100% lõi BE1 Core Mutation Engine trong `OrderService.cs` (phân bổ kho `activeWarehouses`, giữ chỗ `Reservation` 15 phút, snapshot WAC `CostPrice`, khóa phân cấp advisory locks và khóa dòng `FOR UPDATE`, ghi sổ cái `InventoryTransaction` có `WarehouseId` thật). |
| **BE2-F02** | **P1** | Xung đột Git tại 5 file cốt lõi (`OrderDtos.cs`, `IOrderService.cs`, `OrderService.cs`, `OrdersController.cs`, `Program.cs`) và trùng tuyến đường webhook `POST /api/webhooks/simulator/{channel}` giữa `SimulatorController.cs` và `WebhooksController.cs`. | - Giải quyết triệt để các conflict theo đúng ngữ nghĩa nghiệp vụ, không dùng `ours/theirs`.<br>- Hợp nhất DTO và interface có tương thích ngược cho query và các endpoint `Process`/`Complete` của Nhựt.<br>- Xóa bỏ action `IngestWebhookOrder` trên `SimulatorController.cs`, giữ duy nhất một webhook ingress canonical tại `WebhooksController.cs` (`IWebhookService`). Giữ lại `POST /api/webhooks/simulator/batch` trên `SimulatorController` phục vụ runner T05.<br>- DI thống nhất, không đăng ký trùng hay thiếu service. |
| **BE2-F06** | **P1** | Webhook idempotency ở BE2 thiếu nhận diện shop (`ExternalShopId`) và không có chữ ký payload (`WebhookPayloadSignature`); replay khác shop bị lẫn đơn. | Thống nhất toàn bộ luồng tiếp nhận đơn sàn TMĐT (Shopee, TikTok, Lazada) qua `WebhooksController` và `IWebhookService` của BE1 với deduplication key chuẩn `(tenant_id, channel, external_shop_id, external_order_id)` cùng chữ ký Canonical SHA-256. Replay trùng đơn trả kết quả idempotent 200; replay lệch nội dung trả 409 `WEBHOOK_PAYLOAD_MISMATCH`. |

---

## 3. Ma Trận Nghiệm Thu (Acceptance Criteria)

| AC | Nội dung yêu cầu | Trạng thái | Bằng chứng kiểm thử / Kiểm chứng |
| --- | --- | --- | --- |
| **AC-01** | Thực hiện trên `feature/OISMS-42-sprint-3-backend-2`; ghi SHA trước/sau và nguồn baseline; bảo toàn tài liệu ngoài phạm vi. | **PASS** | Branch: `feature/OISMS-42-sprint-3-backend-2`. Baseline: `origin/develop` (`4dbfd8e`). Untracked `docs/11` và `docs/12` được bảo toàn không stage. |
| **AC-02** | Không còn conflict marker, duplicate DI implementation hoặc duplicate webhook route. Release build và WebApplicationFactory startup pass. | **PASS** | `dotnet build src/backend/OISM.sln -c Release`: 0 Error, 0 Warning. 0 conflict markers (`<<<<<<<`). WebApplicationFactory khởi động thành công và vượt qua 22/22 webhook tests, không xảy ra `AmbiguousMatchException`. |
| **AC-03** | HTTP create/confirm/cancel/complete/POS vẫn dùng BE1; RequestKey, snapshot, reservation, allocation, ledger và expiry contract được giữ. Chạy các suite BE1 liên quan trên baseline ghép. | **PASS** | - `OnlineOrderLifecycleIntegrationTests`: **34/34 PASS**<br>- `PosCheckoutIntegrationTests`: **16/16 PASS**<br>- `ReservationExpiryIntegrationTests`: **11/11 PASS**<br>- `StockTransferIntegrationTests`: **28/28 PASS**<br>- `StockCountIntegrationTests`: **50/50 PASS**<br>- Regression B-01 PostgreSQL: **1/1 PASS** |
| **AC-04** | Webhook 3 kênh đi qua ingress BE1; một request hợp lệ tạo reservation thật; thiếu tồn không tạo Reserved giả. HTTP request không gặp ambiguous endpoint. | **PASS** | `WebhookSimulatorControllerTests` và `WebhookIntegrationTests`: **22/22 PASS**. Route `POST /api/webhooks/simulator/{channel}` đi duy nhất qua `WebhooksController.ReceiveMarketplaceWebhook`. |
| **AC-05** | Replay cùng tenant/channel/shop/order/payload không tăng giữ chỗ; shop khác không trả nhầm đơn; payload mismatch bị từ chối theo error contract BE1. | **PASS** | Kiểm chứng qua các kịch bản Replay, Signature Mismatch (409) và Shop Isolation trong `WebhookIntegrationTests` và `SimulatorServiceTests`. |
| **AC-06** | Ghi rõ query stub còn lại dành cho T02 và trạng thái batch tuần tự dành cho T05; không gọi toàn bộ BE2 đã hoàn thành. | **PASS** | - Query stubs: `GetOrdersAsync(OrderFilterRequestDto)` giữ stub cho T02; read endpoints chuyển kho/kiểm kê chưa bổ sung GET trong T01 (dành cho T02).<br>- `RunConcurrencySimulationAsync` giữ cấu trúc tuần tự chờ nâng cấp barrier/concurrent trong T05. |

---

## 4. Kết Quả Kiểm Thử Thực Tế

### 4.1. Unit Tests
```bash
dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj -c Release
```
- **Kết quả:** **141/141 PASSED**, 0 Failed, 0 Skipped (Thời lượng: ~1s).

### 4.2. Targeted Regression B-01 trên PostgreSQL Thật
```bash
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~Postgres_Warehouse_ChangeBranch_WhenHasHistory_ReturnsControlled409_AndCleanWarehouseSucceeds"
```
- **Kết quả:** **1/1 PASSED** (Thời lượng: 5s). Khẳng định baseline B-01 fix chạy hoàn hảo trên nhánh ghép.

### 4.3. Targeted Webhooks & Simulator Integration Tests
```bash
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --no-build --filter "FullyQualifiedName~Webhook"
```
- **Kết quả:** **22/22 PASSED**, 0 Failed, 0 Skipped (Thời lượng: 1m 1s).

### 4.4. Targeted Online Order Lifecycle Integration Tests
```bash
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --no-build --filter "FullyQualifiedName~OnlineOrderLifecycle"
```
- **Kết quả:** **34/34 PASSED**, 0 Failed, 0 Skipped (Thời lượng: 2m 6s).

### 4.5. Toàn Bộ Integration Tests Trước Khi Merge
```bash
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --no-build
```
- **Kết quả:** **267/267 PASSED**, 0 Failed, 0 Skipped (Thời lượng: 15m 38s).
- Bao gồm:
  - `WebhookIntegrationTests` & `WebhookSimulatorControllerTests`: 22 tests
  - `OnlineOrderLifecycleIntegrationTests`: 34 tests
  - `PosCheckoutIntegrationTests`: 16 tests
  - `ReservationExpiryIntegrationTests`: 11 tests
  - `StockTransferIntegrationTests`: 28 tests
  - `StockCountIntegrationTests`: 50 tests
  - `PostgresTenantIsolationTests`: 14 tests
  - `Sprint3PersistenceIntegrationTests`: 10 tests
  - `WarehouseBranchReassignmentRegressionTests`: 4 tests
  - `RealtimeHubIntegrationTests`: 4 tests
  - Toàn bộ các integration tests Sprint 1 & Sprint 2.

**Tổng kết:** **408 tests PASSED** trên toàn bộ suite backend.

---

## 5. Danh Sách File Thay Đổi Thuộc Phạm Vi T01

1. `src/backend/src/OISM.Application/Features/Orders/Dtos/OrderDtos.cs`: Hợp nhất DTO BE1 & BE2.
2. `src/backend/src/OISM.Application/Features/Orders/Services/IOrderService.cs`: Hợp nhất hợp đồng dịch vụ.
3. `src/backend/src/OISM.Application/Features/Orders/Services/OrderService.cs`: Áp dụng lõi mutation BE1, bổ sung query và adapter methods cho BE2 (`ProcessOrderAsync`, `CompleteOrderAsync`, `RequireTenantId`).
4. `src/backend/src/OISM.Application/Features/Simulator/Services/SimulatorService.cs`: Chuyển hướng ingest đơn sàn TMĐT qua lõi `CreateMarketplaceReservedOrderAsync` của BE1.
5. `src/backend/src/OISM.WebApi/Controllers/OrdersController.cs`: Tích hợp các route mutation BE1 và query/processing BE2.
6. `src/backend/src/OISM.WebApi/Controllers/InventoryController.cs`: Bổ sung route alias `[HttpGet("/api/inventory-transactions")]`.
7. `src/backend/src/OISM.WebApi/Controllers/SimulatorController.cs`: Bỏ route trùng `POST {channel}`, giữ lại `POST batch`.
8. `src/backend/src/OISM.WebApi/Program.cs`: Hợp nhất đăng ký DI services.
9. `src/backend/tests/OISM.UnitTests/TestDbContextFactory.cs`: Bổ sung bỏ qua `TransactionIgnoredWarning` cho InMemory.
10. `src/backend/tests/OISM.UnitTests/OrderServiceTests.cs`: Điều chỉnh fixture test theo đúng lõi giữ chỗ và snapshot của BE1; bổ sung test `ProcessAndCompleteOrder_ShouldTransitionStateProperly`.
11. `src/backend/tests/OISM.UnitTests/SimulatorServiceTests.cs`: Điều chỉnh fixture test theo đúng quy trình phân bổ kho của BE1.
12. `docs/09-personal-reports/nhut/sprint-3/codex-antigravity/task-01-core-alignment/handover.md`: Tài liệu bàn giao T01.
13. `docs/09-personal-reports/nhut/sprint-3/codex-antigravity/README.md`: Cập nhật trạng thái T01.

---

## 6. Các Nhiệm Vụ Tiếp Theo (T02 - T06)

- **S3-BE2-T02:** Hoàn thiện API đọc cho Orders, Stock Transfers, Stock Counts (`GetOrdersAsync`, `GetOrderDetailByIdAsync`, list/detail cho transfer và count).
- **S3-BE2-T03:** Kiểm soát phân quyền Owner-only cho Báo cáo Doanh thu / Lợi nhuận (`ReportsController`), kiểm thử HTTP RBAC và Tenant Isolation.
- **S3-BE2-T04:** Sửa logic tính Revenue/COGS (chỉ nhận đơn Completed, filter SKU) và sửa Velocity/Thresholds.
- **S3-BE2-T05:** Xây dựng runner mô phỏng tải đồng thời thật (concurrency barrier, isolated scopes) và automated core tests trên PostgreSQL.
- **S3-BE2-T06:** Chạy regression toàn diện, lập báo cáo tổng kết và bàn giao PR #20.
