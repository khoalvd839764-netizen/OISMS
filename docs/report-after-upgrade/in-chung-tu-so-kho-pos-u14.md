# Báo cáo nâng cấp — In chứng từ sổ kho, truy vết và POS (Upgrade 14)

- **Mã đợt:** `OISMS-66-U14-A` (Prompt 1/3 — Backend)
- **Ngày cập nhật:** 2026-11-21
- **Phạm vi:** Backend API in chứng từ và phân quyền `Cashier` / `Staff`

---

## 1. Phần Backend (`OISMS-66-U14-A`)

### 1.1. Đã làm

1. **Endpoint POS cho Cashier đọc phiếu xuất kho 02-VT (`AC-01`, `AC-02`):**
   - Thêm `GET /api/pos/orders/{orderId}/goods-issues` trên `PosCatalogController` (`[Authorize(Roles = "Owner,Staff,Cashier")]`), giữ nguyên `[Authorize(Roles = "Owner,Staff")]` trên `GoodsIssuesController`.
   - Bổ sung `IGoodsIssueService.GetByOrderIdForPosAsync`:
     - Lấy `tenantId` từ `ICurrentTenantService` (JWT).
     - Khi vai trò không phải `Owner`/`Staff` (tức `Cashier`), kiểm tra `Order.CreatedBy == currentUserId`. Nếu đơn không tồn tại, thuộc tenant khác, hoặc thuộc `Cashier` khác → trả `404 Not Found` với `errorCode = "GOODS_ISSUE_NOT_FOUND"` (không tiết lộ sự tồn tại).
     - Che giá vốn (`UnitCost = null`, `TotalCost = null`) qua `ICostVisibilityService.CanViewCost` cho cả `Cashier` và `Staff`.

2. **Endpoint đọc chi tiết phiếu điều chỉnh cân bằng kho (`AC-04`):**
   - Thêm `GET /api/inventory/adjustments/{id}` trên `InventoryController` (`[Authorize(Roles = "Owner,Staff")]`, `Cashier` bị chặn `403`).
   - Bổ sung `InventoryAdjustmentDto` / `InventoryAdjustmentItemDto` và `IInventoryService.GetAdjustmentByIdAsync`:
     - Cô lập tenant theo JWT; không tìm thấy hoặc khác tenant → `404 Not Found` với `errorCode = "ADJUSTMENT_NOT_FOUND"`.
     - Trả đầy đủ số phiếu (`Number`), lý do (`Reason`), chiều (`Direction`: `"IN"` / `"OUT"` / `"MIXED"`), phiếu kiểm kê liên quan (`StockCountId`, `StockCountNumber`), người lập/duyệt (`CreatedByName`, `ConfirmedByName`), các dòng SKU kèm đơn vị gốc (`BaseUnitName`) và chiều từng dòng.
     - Che giá vốn (`UnitCost = null`, `TotalCost = null`) cho `Staff` qua `ICostVisibilityService`.

3. **Che giá Staff và bổ sung trường in trên API chi tiết (`AC-03`, `AC-05`):**
   - Duy trì che giá qua `ICostVisibilityService` cho `Staff` (`null`) và hiển thị đủ cho `Owner` trên cả 5 API chi tiết: phiếu nhập (`GoodsReceipt`), chuyển kho (`StockTransfer`), kiểm kê (`StockCount`), trả hàng (`SalesReturn`), phiếu điều chỉnh (`InventoryAdjustment`).
   - Bổ sung trường phục vụ mẫu in (không đổi nghĩa trường cũ):
     - `GoodsReceiptItemResponseDto`: `BaseUnitName`.
     - `StockTransferResponseDto` / `StockTransferItemResponseDto`: `CreatedByName`, `DispatchedByName`, `ReceivedByName`, `BaseUnitName`.
     - `StockCountResponseDto` / `StockCountItemResponseDto`: `CreatedByName`, `SubmittedByName`, `ConfirmedByName`, `GeneratedAdjustmentNumber`, `BaseUnitName`, `CountedByName`.
     - `SalesReturnResponseDto` / `SalesReturnItemResponseDto`: `CustomerName`, `BaseUnitName`.

4. **Kiểm thử & Tài liệu (`AC-06`):**
   - Bổ sung 2 route GET mới vào `PostgresCostVisibilityScanIntegrationTests` (`api/pos/orders/{orderId:guid}/goods-issues`, `api/inventory/adjustments/{id:guid}`).
   - Viết mới `PostgresU14PrintDocumentApiIntegrationTests` kiểm thử đầy đủ AC-01..AC-05 trên PostgreSQL thật.
   - Cập nhật `docs/05-api-contracts/03-inventory-endpoints.md`, `05-pos-endpoints.md`, `docs/01-users-and-rbac/02-rbac-matrix.md`, `docs/12-error-and-status-codes/02-error-codes-catalog.md`.

### 1.2. Ảnh hưởng chức năng hiện có

- Không thay đổi chữ ký các endpoint hiện có, không đổi tên hoặc ý nghĩa trường cũ trên DTO.
- Không chạm vào luồng ghi kho (checkout, confirm, nhập, chuyển, kiểm kê, trả hàng).
- Tương thích ngược với các unit test khởi tạo service trực tiếp (tham số `ICurrentUserService?` ở `GoodsIssueService` có giá trị mặc định `null`; tra cứu tên user ở `StockTransferService`/`StockCountService` dùng truy vấn phụ để không làm rơi bản ghi trong test InMemory).

### 1.3. Thay đổi DB và cách nâng cấp DB đang chạy

- **Không đổi schema DB, không thêm migration, không đổi `OismsDatabaseSeeder`.**

### 1.4. Kiểm chứng

- `dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal` → Build succeeded (0 warnings, 0 errors).
- `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false` → PASS 428/428.
- `dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~U14|FullyQualifiedName~CostVisibility|FullyQualifiedName~GoodsIssue"` (PostgreSQL thật) → PASS 11/11 (0 failed, 0 skipped).

### 1.5. Giới hạn

- Phần Frontend Admin, Frontend POS và Seed chứng từ (Prompt 2/3, 3/3) chưa thực hiện trong đợt này.

---

## 2. Phần Frontend Admin (`OISMS-66-U14-B`)

### 2.1. Đã làm

1. **Hàm thuần định tuyến chứng từ (`AC-01`):**
   - Tạo `src/utils/resolveLedgerDocument.ts` ánh xạ đủ bảng Q6 (`GoodsReceipt`, `Order` có/thiếu `goodsIssueId`, `StockTransfer` IN/OUT, `StockCount` IN/OUT kèm `highlightSkuId`, `SalesReturn`, `InventoryAdjustment`, `Unknown`).
   - Tạo unit test `test-oisms/frontend/admin-dashboard/test-ledger-document.mjs` (8 test cases).

2. **Kiểu dữ liệu & API Adapter (`AC-02`):**
   - Bổ sung `documentType`, `documentId`, `documentNumber`, `goodsIssueId`, `salesInvoiceId` vào `LedgerEntry` (`types/domain.ts`) và map từ `getLedger` (`services/apiAdapter.ts`).
   - Thêm `types/inventoryAdjustment.ts` và `services/inventoryAdjustmentService.ts` (`GET /api/inventory/adjustments/{id}`).
   - Bổ sung các trường in từ U14-A vào `types/goodsReceipt.ts`, `types/stockTransfer.ts`, `types/stockCount.ts`, `types/salesReturns.ts`.

3. **Bộ mẫu in & Modal xem trước (`AC-03`, `AC-04`, `AC-06`, `AC-07`):**
   - Tạo `PrintDocumentFooter.tsx` (hiển thị `Ngày in` theo `Asia/Ho_Chi_Minh` UTC+07:00 và `Người in`, không ghi log in, không watermark).
   - Tạo 6 mẫu mới trong `components/print/`: `GoodsReceiptPrintPaper.tsx` (01-VT, có link mở đơn mua), `StockTransferOutPrintPaper.tsx`, `StockTransferInPrintPaper.tsx`, `StockCountPrintPaper.tsx` (05-VT, tô nổi dòng SKU đang bấm), `SalesReturnPrintPaper.tsx`, `InventoryAdjustmentPrintPaper.tsx`, cùng `GoodsIssuePrintPaper.tsx` và `LegacyOrderPrintPaper.tsx`.
   - Ẩn toàn bộ cột giá nhập/giá vốn khi API trả `null` (`Staff`), không tự suy giá ở frontend.
   - Tạo `DocumentPrintModal.tsx` dùng `PrintArea`, xử lý trạng thái tải và lỗi 403/404.

4. **Tích hợp màn Sổ kho, Thẻ kho và Truy vết (`AC-02`, `AC-05`, `AC-07`):**
   - `InventoryPage.tsx` (tab Sổ kho) và `StockCardModal.tsx`: thêm nút **Chứng từ**; vô hiệu hóa kèm tooltip *"Không xác định được chứng từ nguồn"* khi `Unknown`.
   - `SalesInvoiceTraceModal.tsx`: thêm nút **Xem/In** ở từng bước (hóa đơn, đơn hàng, 02-VT theo kho, phiếu trả hàng) và nút **In tất cả** (gộp bộ chứng từ trong `PrintArea`, ngắt trang mỗi chứng từ).

### 2.2. Kiểm chứng

- `npm run build --prefix src/frontend/admin-dashboard` → PASS (`tsc -b && vite build`, 0 lỗi).
- `node --test test-oisms/frontend/admin-dashboard/test-ledger-document.mjs test-oisms/frontend/admin-dashboard/test-ai-chat-format.mjs` → PASS 15/15.
- Ảnh xem trước từng mẫu với Owner/Staff: **NOT RUN** (chưa khởi chạy server/seed dữ liệu mẫu đầy đủ cho 8 loại chứng từ; sẽ thực hiện sau `OISMS-66-U14-D`).

### 2.3. Giới hạn

- Phần Frontend POS (`OISMS-66-U14-C`) và Seed chứng từ (`OISMS-66-U14-D`) chưa thực hiện trong đợt này.

---

## 3. Phần Frontend POS (`OISMS-66-U14-C`)

### 3.1. Đã làm

1. **Giữ `orderId` và điều kiện hiển thị nút (`AC-01`, `AC-04`):**
   - Bổ sung `orderId?: string` vào `ReceiptData` (`types/pos.ts`), `checkoutPosOrder` (`services/posApi.ts`), `mapServerReceipt` (`services/receiptMapper.ts`), truyền `result.orderId` từ `PaymentModal.tsx`.
   - Tạo helper thuần `services/goodsIssuePrint.ts` (`canPrintGoodsIssue`, `splitGoodsIssuePages`, `getGoodsIssueFetchErrorMessage`).
   - Trên `ReceiptPrintModal.tsx`: giữ nguyên `shouldAutoPrint` (tự in biên nhận đúng 1 lần); thêm nút **In phiếu xuất kho** cạnh **In hóa đơn** chỉ khi `!receipt.isDraft && Boolean(receipt.orderId)`.

2. **Modal in phiếu xuất kho 02-VT cho POS (`AC-02`, `AC-03`):**
   - Tạo `components/pos/GoodsIssuePrintModal.tsx` gọi `GET /api/pos/orders/{orderId}/goods-issues` (`getPosOrderGoodsIssues`).
   - Hỗ trợ chọn khổ giấy **A4 / A5**, mẫu **02-VT** không có cột giá vốn, tách mỗi kho một trang (`pos-gi-page-break`), in tất cả trong một lần `window.print()`.
   - Xử lý `404` với thông báo *"Không tìm thấy phiếu xuất kho của đơn này"*, lỗi mạng có nút **Thử lại** (không gọi lại checkout).

3. **Kiểm thử (`AC-05`):**
   - Tạo `test-oisms/frontend/pos-app/test-goods-issue-print.mjs` và thêm vào script `test` trong `src/frontend/pos-app/package.json`.

### 3.2. Kiểm chứng

- `npm run build --prefix src/frontend/pos-app` → PASS (`tsc -b && vite build`, 0 lỗi).
- `npm run lint --prefix src/frontend/pos-app` → PASS (`oxlint`, 0 lỗi).
- `npm test --prefix src/frontend/pos-app` → PASS 98/98 (bao gồm `test-receipt.mjs` và `test-goods-issue-print.mjs`).

### 3.3. Giới hạn

- Phần Seed chứng từ & Reset local (`OISMS-66-U14-D`) chưa thực hiện trong đợt này.

---

## 4. Phần Seed chứng từ, Reset local & Docker (`OISMS-66-U14-D`)

### 4.1. Đã làm

1. **`OismsDocumentSeeder.cs` & `Program.cs` (`AC-01`, `AC-02`, `AC-03`, `AC-06`):**
   - Tạo `OismsDocumentSeeder.SeedAsync(IServiceProvider)` trong `OISM.Infrastructure/Persistence/`, gọi ngay sau `OismsDatabaseSeeder.SeedAsync` trong `Program.cs` (vẫn đi qua `DemoSeedPolicy`, tuyệt đối không chạy ở `Production`).
   - Với mỗi tenant demo (`TenantAId`, `TenantBId`), mở scope DI riêng và đặt tenant qua `ICurrentTenantService.SetTenant(tenantId)`.
   - Tạo đủ chuỗi chứng từ qua **Application service thật** (trong transaction và khóa thật, không INSERT trực tiếp vào `inventory_transactions`):
     1. Bảo đảm mỗi tenant có 2 kho và 2 SKU có bảng giá hợp lệ.
     2. `IPurchaseOrderService.CreateAsync` → `ApproveAsync` → `IGoodsReceiptService.CreateReceiptAsync` → `ConfirmReceiptAsync` (WAC).
     3. `IPosCheckoutService.CheckoutPosAsync` (POS checkout).
     4. `IOrderService.CreateOrderAsync` → `ConfirmOrderAsync` (đơn online confirm).
     5. `IStockTransferService.CreateTransferAsync` → `DispatchTransferAsync` → `ReceiveTransferAsync` (chuyển kho 2 bước).
     6. `IStockCountService.CreateStockCountAsync` → `RecordCountsAsync` (có cả chênh lệch thiếu và thừa) → `SubmitStockCountAsync` → `ConfirmStockCountAsync` (tự động sinh `InventoryAdjustment` có 2 dòng âm/dương).
     7. `ISalesReturnService.CreateAsync` → `ConfirmAsync` (trả hàng một phần trên đơn POS).
   - **Ghi chú về `InventoryAdjustment`:** Hệ thống không có luồng tạo phiếu điều chỉnh độc lập; `InventoryAdjustment` được sinh tự động khi `StockCountService.ConfirmStockCountAsync` chốt đợt kiểm kê có chênh lệch.
   - **Idempotency:** Dùng `RequestKey` cố định theo từng tenant và kiểm tra tồn tại trước khi gọi service; chạy lại lần 2 không tạo thêm chứng từ hay dòng sổ kho.

2. **Kiểm thử PostgreSQL (`PostgresU14DocumentSeederIntegrationTests.cs`):**
   - Kiểm chứng chạy seeder 2 lần liên tiếp trên DB trống: số lượng chứng từ và `inventory_transactions` giữ nguyên ở lần 2.
   - Kiểm chứng mọi dòng sổ kho có `DocumentType != "Unknown"`, dòng OUT bán hàng có `GoodsIssueId`, `on_hand == balance_after` cuối theo `(BranchId, SkuId)`, và `Available >= 0`.
   - Đã chạy full suite: `OISM.UnitTests` PASS `428/428`, `OISM.IntegrationTests` PASS `476/476`, `admin-dashboard` build PASS, `pos-app` build/lint/test PASS `98/98`.

### 4.2. Runbook Reset Local & Rebuild Docker (`AC-04`, `AC-05`)

1. Người dùng tự chạy lệnh xóa volume DB local:
   ```bash
   docker compose down -v
   ```
2. Sau khi người dùng xác nhận đã xóa volume, chạy:
   ```bash
   docker compose up -d --build
   ```
3. Kiểm tra `GET http://localhost:5000/health/ready` trả `200`, log backend có thông báo seed thành công, đăng nhập `owner@shopa.local` để kiểm tra sổ kho đủ 6 loại nguồn và chụp ảnh từng mẫu in.

### 4.3. Việc treo: Reset AWS (`Q13`)

- **AWS giữ nguyên dữ liệu hiện tại, chưa reset trong đợt Upgrade 14.**
- Các dòng OUT cũ thiếu `GoodsIssueId` trên AWS tiếp tục dùng phương án thay thế ở `Q6` (`LegacyOrderPrintPaper` hiển thị hóa đơn/đơn hàng kèm ghi chú *"Chứng từ trước khi có phiếu xuất kho"*).
- Khi reset AWS ở đợt sau cần lập runbook riêng có bước backup DB trước khi thao tác và phương án nạp dữ liệu mẫu phù hợp vì `Production` không chạy `DemoSeedPolicy`.
