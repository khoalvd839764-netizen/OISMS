# Báo cáo sau nâng cấp — Cấu hình giá bán đơn mua, sinh giá khi nhập kho và xử lý xung đột Q19 (Upgrade 12 — Gói 4)

| Thông tin | Chi tiết |
| :--- | :--- |
| **Mã gói công việc** | `OISMS-66-L2-G4` |
| **Ngày thực hiện** | `2026-11-21` |
| **Mức độ ưu tiên** | `CRITICAL` |
| **Tài liệu nguồn** | [`docs/Upgrade-recommended/Pedding/12-don-mua-va-cau-hinh-gia.md`](../Upgrade-recommended/Pedding/12-don-mua-va-cau-hinh-gia.md) (Luồng B, C bước 4, D; Q15–Q19, Q25; BE-12.7, BE-12.8, FE-12.3, FE-12.4); [`docs/Upgrade-recommended/Done/04-bang-gia-theo-thoi-gian.md`](../Upgrade-recommended/Done/04-bang-gia-theo-thoi-gian.md) |
| **Migration SQL** | `docker/postgres/migrations/20261121_purchase_order_sale_price_configs.sql` |
| **Baseline commit** | `759f881` |

---

## 1. Tóm tắt thay đổi nghiệp vụ và kiến trúc

### 1.1. Cấu hình giá bán trên đơn mua (`purchase_order_sale_price_configs` - AC-01, AC-02)
- **Bảng thứ 51:** Bổ sung bảng `purchase_order_sale_price_configs` với đầy đủ ràng buộc foreign key, tenant isolation, CHECK constraints cho các chế độ giá và unique index `(tenant_id, purchase_order_item_id, unit_id, price_list_id)`.
- **Đồng bộ 51 bảng:** Đồng bộ số bảng 50 -> 51 tại `DatabaseMigrationService.RequiredTables`, `oisms_schema.sql`, `init-oisms.sh`, `CLAUDE.md`, `README.md`, `docs/README.md`, `docs/database/README.md`, `01-docker-compose-guide.md`, `04-quy-trinh-deploy-va-cap-nhat.md`.
- **Cấu hình độc lập theo từng đơn vị bán (`canSell`):** Owner thiết lập cấu hình giá bán lẻ và giá bán sỉ cho từng đơn vị bán của SKU tại trạng thái `PendingApproval`. Mỗi đơn vị có cấu hình riêng biệt, không tự nhân theo hệ số quy đổi:
  - `KEEP`: Giữ nguyên giá bán hiện hành (mặc định).
  - `WAC_MARGIN`: Giá bán theo tỷ lệ lãi gộp dự kiến: $\text{Giá} = \text{WAC}_{\text{dự kiến}} \div (1 - \% \text{lãi})$, tự động làm tròn LÊN bội 1.000đ. Tỷ lệ lãi gộp mặc định lấy từ mức lãi gộp hiện tại của SKU $\times$ đơn vị. Bắt buộc có `% lãi` nếu SKU chưa có giá bán hoặc WAC = 0.
  - `FIXED`: Giá bán cố định, nguồn giá bắt buộc chọn `OWNER` hoặc `SUPPLIER`.
- **Chế độ áp dụng:** `ON_RECEIPT` (ngay khi nhập kho) hoặc `FROM_DATE` (từ ngày hiệu lực `apply_from` chỉ định).
- **Cảnh báo và chặn duyệt bán lỗ:** Hệ thống tính toán `projectedPrice`, `currentUnitWac` và gắn cờ cảnh báo bán lỗ `isBelowCost = true` nếu giá bán dự kiến nhỏ hơn giá vốn WAC dự kiến. Khi Owner bấm duyệt (`POST /api/purchase-orders/{id}/approve`), nếu có bất kỳ dòng nào bán lỗ mà không gửi cờ `confirmLossPrice = true`, hệ thống từ chối với mã lỗi `422 PURCHASE_ORDER_LOSS_PRICE_CONFIRM_REQUIRED`.

### 1.2. Tự động sinh phiếu đổi giá và giá bán khi xác nhận nhập kho (AC-03)
- **Sinh giá nguyên tử trong `ConfirmReceiptAsync`:**
  - Tại phiếu nhập kho ĐẦU TIÊN của dòng đơn mua (`ConfirmReceiptAsync`), sau khi cập nhật WAC chi nhánh nhận hàng và trong cùng một transaction PostgreSQL:
  - Tìm tất cả cấu hình giá bán có `mode != 'KEEP'` và `applied_at IS NULL` thuộc các dòng đơn mua có mặt trong phiếu nhập.
  - Tính giá bán mới (với `WAC_MARGIN` sử dụng chính xác giá vốn WAC vừa được cập nhật).
  - Tự động sinh một `PriceChangeRequest` tập trung ở trạng thái `Approved`, loại lý do `reason_type = FROM_PURCHASE_ORDER`, lý do `"Theo đơn mua <PO_NUMBER>"`, người duyệt ghi nhận theo người đã duyệt đơn mua (`po.ApprovedBy`).
  - Sinh các dòng giá mới `SkuUnitPrice` với mốc thời gian `valid_from = max(apply_from, confirmed_at)`.
  - Cập nhật `applied_at` và `price_change_request_id` trên bản ghi cấu hình giá bán.
- **Không sinh lặp:** Các đợt nhập kho tiếp theo của cùng dòng đơn mua không sinh lại giá (do `applied_at` đã có giá trị).
- **Đảm bảo tính toàn vẹn:** Bất kỳ lỗi nào trong quá trình sinh giá đều kích hoạt rollback toàn bộ transaction phiếu nhập kho, bảo vệ tính bất biến của sổ cái và bảng giá.

### 1.3. Thứ tự khóa giao dịch tránh Deadlock (Q25 - AC-04)
- **Thứ tự khóa chuẩn trong `ConfirmReceiptAsync`:**
  1. Khóa phiếu nhập kho (`goods_receipts`)
  2. Khóa đơn mua hàng (`purchase_orders`)
  3. Khóa tồn kho SKU chi nhánh (`branch_sku_stocks`, sắp xếp theo `SkuId` tăng dần)
  4. Khóa giá bán bằng PostgreSQL Advisory Lock: `pg_advisory_xact_lock(hashtext(tenant_id || ':price:' || sku_id || ':' || unit_id || ':' || price_list_id))` sắp xếp theo tổ hợp `(SkuId, UnitId, PriceListId)` tăng dần.
- Giữ nguyên toàn bộ thứ tự khóa cũ, chỉ bổ sung tầng khóa giá ở bước cuối cùng trước khi ghi nhận giá.
- Cơ chế khóa giá này cũng được áp dụng đồng bộ khi Owner duyệt phiếu đổi giá tay (`PriceChangeService.ApproveAsync`).

### 1.4. Bắt buộc lý do đổi giá và xử lý xung đột giá chờ Q19 (AC-05)
- **Loại lý do bắt buộc (`reason_type`):**
  - Thêm cột `reason_type` NOT NULL trên bảng `price_change_requests` (`SHORTAGE`, `PROMOTION`, `FROM_PURCHASE_ORDER`, `OTHER`).
  - Dữ liệu lịch sử cũ được tự động backfill giá trị `'OTHER'`.
  - Khi lập phiếu đổi giá tay (`POST /api/price-change-requests`), nếu thiếu `reasonType` hoặc chọn `OTHER` mà không có giải trình chi tiết (tối thiểu 5 ký tự) hệ thống trả về mã lỗi `400 BAD_REQUEST`.
- **API tra cứu giá chờ (`GET /api/pricing/pending-po-prices?skuId={skuId}`):**
  - Trả về danh sách cấu hình giá bán chưa áp dụng (`applied_at IS NULL` và `mode != 'KEEP'`) từ các đơn mua đã được duyệt hoặc đang nhận một phần (`Approved`, `PartiallyReceived`).
- **Giải quyết xung đột giá chờ Q19:**
  - Khi tạo hoặc duyệt phiếu đổi giá tay có trùng SKU $\times$ đơn vị $\times$ bảng giá với đơn mua đang chờ áp dụng:
  - Client bắt buộc gửi kèm hướng giải quyết `poConflictResolution`:
    - `KEEP_MANUAL`: Hủy cấu hình giá trên đơn mua (chuyển sang `KEEP`), bảo toàn giá đổi tay khi đơn mua nhập kho sau này.
    - `APPLY_PO_LATER`: Giữ nguyên cấu hình trên đơn mua, khi đơn mua nhập kho sẽ sinh giá mới đè lên giá đổi tay.
  - Nếu thiếu `poConflictResolution`, hệ thống trả về `409 PRICE_CHANGE_PO_CONFLICT` kèm chi tiết danh sách đơn mua xung đột (`conflictingPurchaseOrders`).

### 1.5. Phân quyền và che giá bảo mật (AC-06)
- **Nhân viên (`Staff`):**
  - Khi xem chi tiết đơn mua hàng, các trường cấu hình giá bán, giá dự kiến, tỷ lệ margin đều trả về `null`.
  - Không thể cấu hình giá bán đơn mua (`PUT /api/purchase-orders/{id}/sale-price-configs` trả về `403 Forbidden`).
- **Thu ngân (`Cashier`):**
  - Bị chặn truy cập toàn bộ API đơn mua (`403 Forbidden`).

### 1.6. Giao diện Frontend Admin (AC-07)
- **`SalePriceConfigTable.tsx`:** Tích hợp trực tiếp vào modal chi tiết đơn mua (`PurchaseOrderDetailModal.tsx`), hiển thị bảng cấu hình giá theo từng dòng hàng $\times$ đơn vị bán $\times$ loại giá (lẻ/sỉ). Hỗ trợ tính toán live giá dự kiến theo công thức WAC, cảnh báo bán lỗ nhãn đỏ, chọn ngày áp dụng.
- **Hộp thoại xác nhận bán lỗ:** Khi Owner bấm Duyệt đơn mua có dòng bán lỗ, hiển thị modal cảnh báo danh sách các mặt hàng bán dưới giá vốn và yêu cầu xác nhận trước khi gửi `confirmLossPrice = true`.
- **`CreatePriceChangeModal.tsx` & `PriceManagementModal.tsx`:**
  - Bổ sung trường chọn loại lý do đổi giá và ô nhập bắt buộc giải trình khi chọn lý do "Khác".
  - Hiển thị huy hiệu loại lý do trên danh sách phiếu đổi giá.
  - Hiển thị modal cảnh báo xung đột Q19 cho phép chọn giữa `KEEP_MANUAL` và `APPLY_PO_LATER` khi phát hiện đơn mua đang chờ giá.

---

## 2. Chi tiết Migration Database

File: `docker/postgres/migrations/20261121_purchase_order_sale_price_configs.sql`
- **Tính Idempotent:** Thiết kế an toàn với `CREATE TABLE IF NOT EXISTS`, `DO $$ BEGIN ... EXCEPTION ... END $$`, kiểm tra `information_schema` trước khi thêm cột và trigger.
- **Khả năng chạy lại:** Đã chạy thử nghiệm 2 lần liên tiếp trên PostgreSQL thật, hoàn thành mà không có bất kỳ lỗi cú pháp hoặc xung đột schema nào.
- **Quyền hạn:** Cấp quyền đầy đủ `SELECT, INSERT, UPDATE, DELETE` cho role ứng dụng `oisms_app` và `oisms_runtime`.

---

## 3. Kết quả Kiểm thử (Gates Verification)

### 3.1. Build & Unit Tests
- `dotnet build src/backend/OISM.sln`: **PASS** (0 Warning, 0 Error).
- `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj`: **PASS** (428/428 tests passed).
- `npm run build --prefix src/frontend/admin-dashboard`: **PASS** (TypeScript & Vite build clean, exit code 0).

### 3.2. Integration Tests trên PostgreSQL thật
File: `test-oisms/backend/OISM.IntegrationTests/PostgresPurchaseOrderSalePriceIntegrationTests.cs` (5/5 tests PASS):
1. `AC01_MigrationAndBaseline_Maintains51TablesAndPriceChangeReasonType`: Xác thực 51 bảng nghiệp vụ, cấu trúc bảng `purchase_order_sale_price_configs`, cột `reason_type` trên `price_change_requests` và tính idempotent của migration khi chạy 2 lần.
2. `AC02_SalePriceConfig_PendingApproval_CalculatesProjectedPriceAndRequiresLossConfirmation`: Xác thực tính toán giá dự kiến theo WAC + Margin (làm tròn lên bội 1.000đ), chặn duyệt đơn khi thiếu xác nhận bán lỗ (422) và duyệt thành công khi có xác nhận.
3. `AC03_ConfirmReceipt_GeneratesPriceChangeRequestAndUnitPricesAtomically`: Xác thực sinh tự động phiếu đổi giá (`FROM_PURCHASE_ORDER`) và dòng `SkuUnitPrice` khi xác nhận phiếu nhập đầu tiên, không sinh lặp ở lần nhập sau.
4. `AC04_AdvisoryLockOrdering_PricesLockedAfterSkuStock`: Kiểm tra dịch vụ khóa `LockPricesAsync` và thứ tự lấy khóa `pg_advisory_xact_lock` theo SKU + đơn vị + bảng giá tăng dần.
5. `AC05_ManualPriceChange_RequiresReasonType_AndHandlesQ19ConflictResolution`: Xác thực bắt buộc `reason_type` trên phiếu đổi giá tay (400 khi thiếu/thiếu ghi chú), phát hiện xung đột giá chờ Q19 (409) và áp dụng thành công khi gửi `poConflictResolution = KEEP_MANUAL`.

Hồi quy kiểm thử:
- `PostgresPurchaseOrderU12IntegrationTests.cs`: **PASS** (4/4 tests passed).

---

## 4. Danh sách tài liệu và mã nguồn đã cập nhật

- **Database / DevOps:**
  - `docker/postgres/migrations/20261121_purchase_order_sale_price_configs.sql`
  - `docs/database/oisms_schema.sql`
  - `docker/postgres/init-oisms.sh`
  - `CLAUDE.md`, `README.md`, `docs/README.md`, `docs/database/README.md`, `docs/07-devops-and-deployment/01-docker-compose-guide.md`, `docs/13-deploy-aws/04-quy-trinh-deploy-va-cap-nhat.md`
- **Tài liệu nghiệp vụ & Thiết kế:**
  - `docs/01-users-and-rbac/02-rbac-matrix.md`
  - `docs/02-functional-requirements/fr-02-product-catalog.md`, `fr-04-cost-wac.md`
  - `docs/04-architecture-and-design/04-state-machines.md`
  - `docs/05-api-contracts/02-catalog-endpoints.md`, `03-inventory-endpoints.md`
  - `docs/11-ui-screens/03-product-screens.md`, `04-inventory-screens.md`
  - `docs/12-error-and-status-codes/02-error-codes-catalog.md`
  - `docs/Upgrade-recommended/Done/04-bang-gia-theo-thoi-gian.md`
  - `docs/report-after-upgrade/cau-hinh-gia-ban-u12-g4.md`
- **Backend (`OISM.Domain`, `OISM.Application`, `OISM.Infrastructure`, `OISM.WebApi`):**
  - Entities: `PurchaseOrderSalePriceConfig.cs`, `PriceChangeRequest.cs`, `PurchaseOrderItem.cs`
  - EF Configuration: `PurchaseOrderSalePriceConfigConfiguration.cs`, `PriceChangeRequestConfiguration.cs`
  - Persistence & Migration: `AppDbContext.cs`, `DatabaseMigrationService.cs`
  - Interfaces: `IAppDbContext.cs`, `ISqlLockService.cs`
  - Services: `SqlLockService.cs`, `PurchaseOrderService.cs`, `GoodsReceiptService.cs`, `PriceChangeService.cs`
  - DTOs & Controllers: `PurchaseOrderDtos.cs`, `PricingDtos.cs`, `PurchaseOrdersController.cs`, `PricingController.cs`
  - Error Handling: `ApiErrorTitles.cs`, `ApiErrorResponse.cs`, `ExceptionHandlingMiddleware.cs`, `OismException.cs`
- **Frontend (`admin-dashboard`):**
  - Types & Services: `purchaseOrder.ts`, `pricing.ts`, `purchaseOrderService.ts`, `pricingService.ts`
  - Components: `SalePriceConfigTable.tsx`, `PurchaseOrderDetailModal.tsx`, `CreatePriceChangeModal.tsx`, `PriceManagementModal.tsx`
- **Tests:**
  - `test-oisms/backend/OISM.UnitTests/DatabaseBaselineValidatorTests.cs`
  - `test-oisms/backend/OISM.IntegrationTests/PostgresPurchaseOrderSalePriceIntegrationTests.cs`

## Sửa sau nghiệm thu (21/11/2026)

Kiểm thử lại trên docker local (DB reset, 51 bảng) và PostgreSQL 16 tạm. Lỗi tìm thấy và cách sửa:

| # | Lỗi | Sửa |
| --- | --- | --- |
| 1 | `GET /api/price-change-requests` và chi tiết phiếu trả `reasonType = OTHER` cho mọi phiếu, dù DB lưu đúng `FROM_PURCHASE_ORDER`/`SHORTAGE` (`PriceQueryService` quên map) | Map `ReasonType` trong `PriceQueryService`; test `AC03_AC04_...` kiểm thêm API danh sách và chi tiết |
| 2 | Cấu hình `FIXED` chấp nhận giá mới = 0, khi nhập kho sẽ sinh giá bán 0đ | `ValidateConfigDto` yêu cầu `fixedPrice > 0` (CHECK DB vẫn `>= 0`, chặn ở tầng ứng dụng) |
| 3 | Q19 `KEEP_MANUAL`: cấu hình đơn mua được đọc trước khi khóa đơn mua; nếu phiếu nhập vừa áp giá trong lúc chờ khóa thì vẫn bị đổi sang `KEEP`. Mốc `valid_from` của giá tay lấy từ đồng hồ trước khi chờ khóa nên có thể sớm hơn giá vừa sinh từ phiếu nhập | Đọc lại cấu hình còn chờ sau khi giữ khóa đơn mua; tính `valid_from` của giá tay bằng đồng hồ đọc sau khi giữ khóa giá |
| 4 | Replay phiếu đổi giá cùng `Idempotency-Key` mà thiếu `reasonType` gây lỗi 500 (null) | So khớp payload chịu được `reasonType` null |
| 5 | Chạy **toàn bộ** integration suite: 164/468 test fail dây chuyền. `PriceListMigrationIntegrationTests` và `PurchaseOrderIntegrationTests` tạo lại bảng cha bằng `DROP ... CASCADE`, làm mất `fk_po_sale_price_configs_price_list`/`_pcr`/`_item`; đường nâng cấp của `DatabaseMigrationService` chỉ `CREATE TABLE IF NOT EXISTS` nên không thêm lại, DB test bị coi là schema drift | Khối `OISMS_PO_SALE_PRICE_CONFIGS` trong `oisms_schema.sql` thêm đoạn `DO $$` tạo lại 5 khóa ngoại nếu thiếu (giống migration `20261121`) |
| 6 | Các assertion số bảng còn ghi 50 (`PostgresGoodsIssueIntegrationTests`, `PostgresTenantIsolationTests`; kể cả bảng `__EFMigrationsHistory` là 51 → 52) | Đổi thành 51 (và 52 khi tính `__EFMigrationsHistory`) |
