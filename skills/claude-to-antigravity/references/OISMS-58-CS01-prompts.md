# Prompt giao Anti — OISMS-58-CS01

Gửi tuần tự: P1, rồi P2 sau khi P1 được VERIFIED, rồi P3 sau khi P2 được VERIFIED. Mỗi lần chỉ dán một prompt.

## P1 — Backend lõi trả hàng

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-58-CS01-P1 — Backend lõi trả hàng | Mức: CRITICAL | Prompt 1/3
BASELINE: branch feature/OISMS-58-cac-chuc-nang-con-lai, commit 6ebffc7. Giữ nguyên các thay đổi docs chưa commit có sẵn.

MỤC TIÊU: Lập phiếu trả hàng gắn đơn gốc (Draft → Confirmed / Cancelled). Khi xác nhận: nhập lại kho bằng dòng sổ IN theo giá vốn lúc bán, tính lại WAC chi nhánh, ghi tiền hoàn.

NGUỒN CẦN ĐỌC (chỉ các mục này):
- docs/Upgrade-recommended/08-tra-hang-cs01.md: "Quyết định đã chốt", "Quy tắc nghiệp vụ", B1, B2, B3, B4, B6
- docs/database/oisms_schema.sql: bảng sales_returns, sales_return_items, inventory_transactions
- Mẫu code bắt chước:
  - Luồng ghi sổ và WAC: src/backend/src/OISM.Application/Features/Inventory/Services/GoodsReceiptService.cs (ConfirmReceiptAsync)
  - Khóa và idempotency: src/backend/src/OISM.Application/Features/Orders/Services/PosCheckoutService.cs, OrderService.Expiry.cs
  - Controller, mã lỗi: src/backend/src/OISM.WebApi/Controllers/SalesInvoicesController.cs, src/backend/src/OISM.WebApi/Http/ApiErrorTitles.cs
  - Migration: docker/postgres/migrations/20261106_payment_accounts.sql
  - Test PostgreSQL: test-oisms/backend/OISM.IntegrationTests/PosCheckoutIntegrationTests.cs, MultiUnitFlowIntegrationTests.cs
- Skill kỹ thuật: skills/postgresql/SKILL.md, skills/csharp-pro/SKILL.md

ĐƯỢC SỬA:
- docker/postgres/migrations/ (một file mới 20261110_sales_returns_request_key.sql)
- docs/database/oisms_schema.sql
- OISM.Domain (entity, DocumentStatuses), OISM.Infrastructure (Configurations, AppDbContext, SqlLockService, DatabaseMigrationService)
- OISM.Application/Common/Interfaces (IAppDbContext, ISqlLockService), OISM.Application/Features/SalesReturns/ (mới), Application DependencyInjection.cs
- OISM.WebApi/Controllers/SalesReturnsController.cs (mới), ApiErrorTitles.cs, ApiErrorProfile nếu cần
- test-oisms/backend/ (test mới)

KHÔNG ĐƯỢC SỬA: ReportService, SalesInvoiceService (thuộc P2), frontend, các migration cũ, luồng POS checkout và luồng đơn hàng.

ACCEPTANCE CRITERIA:
- AC-01 (B1): migration thêm request_key uuid NOT NULL + UNIQUE (tenant_id, request_key), total_refund_amount numeric(20,2) NOT NULL DEFAULT 0 CHECK >= 0, note text NULL. Script chạy được nhiều lần; nếu đã có dòng cũ thì gán gen_random_uuid() trước khi đặt NOT NULL. Cập nhật oisms_schema.sql và phần kiểm tra cột trong DatabaseMigrationService. Vẫn là 44 bảng → build pass, /health/ready trên DB đã migrate không báo thiếu.
- AC-02 (B2): có entity SalesReturn, SalesReturnItem kèm configuration, DbSet và query filter theo tenant; có SalesReturnStatus; có LockSalesReturnAsync và LockSalesReturnNumberAsync (pg_advisory_xact_lock) → build pass.
- AC-03 (B3, Quy tắc nghiệp vụ):
  - GET returnable-items trả về đã bán / đã trả (chỉ tính phiếu Confirmed) / còn được trả theo đơn vị của dòng đơn.
  - Create tạo phiếu Draft, số phiếu RT-yyyyMMdd-xxxx. Cùng key và cùng payload trả lại phiếu cũ (200); khác payload trả 409 IDEMPOTENCY_PAYLOAD_MISMATCH.
  - Kiểm tra: đơn phải Confirmed hoặc Completed; kho phải thuộc chi nhánh của đơn; số lượng không vượt số còn được trả; tiền hoàn không vượt SL × UnitPrice và tổng hoàn không vượt Order.TotalAmount; đơn sàn bắt buộc tiền hoàn = 0.
- AC-04 (B3, ConfirmAsync): chạy trong ExecuteWithRetryAsync + transaction, ĐÚNG thứ tự khóa:
  LockSkuStockAsync (SKU đã sắp xếp, chi nhánh của đơn) → LockRequestKeyAsync(return.RequestKey) → LockOrderAsync → LockSalesReturnAsync → LockBranchInventoryRowsAsync → LockWarehouseInventoryRowsAsync (kho nhận) → SetConstraintsImmediateAsync.
  Dưới khóa phải đọc lại và kiểm tra lại số còn được trả. Với mỗi dòng:
  - BaseQty = quantity × OrderItem.FactorSnapshot;
  - OnHand chi nhánh và kho tăng BaseQty (chưa có dòng balance ở kho thì tạo mới, giống GoodsReceipt);
  - ghi InventoryTransaction IN với SalesReturnItemId, UnitCost = original_unit_cost = OrderItem.CostPrice, BalanceAfter và SequenceNo đúng;
  - AverageCost = WacCalculator(OnHand cũ, AverageCost cũ, BaseQty, original_unit_cost).
  Ghi confirmed_by, confirmed_at, refund_method, refunded_at (đơn sàn để NULL). Phát SignalR stock.changed sau commit. Xác nhận lần hai trả lại kết quả cũ, không ghi sổ thêm.
- AC-05 (B3): Cancel chỉ áp dụng cho Draft. Confirmed hoặc Cancelled thì trả 409 INVALID_STATE_TRANSITION.
- AC-06 (B4): có đủ 6 endpoint theo bảng B4, [Authorize(Roles = "Owner,Staff")], Create bắt buộc Idempotency-Key hoặc X-Request-Key. 5 mã lỗi mới trả đúng HTTP, dạng ProblemDetails + errorCode, có trong ApiErrorTitles. Cashier gọi thì nhận 403.
- AC-07 (B6, PostgreSQL thật), test mới tên PostgresSalesReturnIntegrationTests:
  (a) xác nhận phiếu: OnHand chi nhánh và kho, dòng IN, BalanceAfter, WAC đúng công thức;
  (b) hai phiếu Draft cùng dòng đơn có tổng vượt SL bán, xác nhận đồng thời: đúng 1 phiếu thành công, phiếu kia 422 RETURN_QUANTITY_EXCEEDED;
  (c) replay Create cùng key: không tạo phiếu thứ hai; khác payload: 409;
  (d) Confirm hai lần: chỉ có 1 dòng sổ;
  (e) đơn bán theo đơn vị có FactorSnapshot = 24, trả 1 thì cộng 24 đơn vị gốc;
  (f) tenant khác Get hoặc Confirm nhận 404;
  (g) đơn sàn: nhập kho, refund NULL.
- AC-08 (B6): unit test cho validation, tiền hoàn mặc định và giới hạn, tính số còn được trả.

INVARIANT PHẢI GIỮ: inventory_transactions chỉ INSERT; không âm tồn; tenant chỉ lấy từ JWT; khóa sau khi mở transaction; controller không dùng AppDbContext, không mở transaction; không đụng Reserved.

GATE (chạy thật, ghi kết quả):
- dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal
- dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false (full suite, vì có đổi IAppDbContext và ISqlLockService)
- dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --filter 'FullyQualifiedName~SalesReturn' trên PostgreSQL thật (biến OISM_TEST_*). Ghi rõ provider.
- Hồi quy: --filter 'FullyQualifiedName~PosCheckout|FullyQualifiedName~GoodsReceipt|FullyQualifiedName~MultiUnit'

GIỚI HẠN:
- Không commit/push. Không thêm dependency. Chỉ thêm đúng một migration như trên.
- Không đổi thứ tự khóa đã chỉ định. Thấy deadlock hoặc lý do phải đổi thì dừng, trả BLOCKED kèm bằng chứng.
- Một lỗi tối đa 2 lần thử. Không nới assertion, không skip test.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md. Kèm git diff --stat và danh sách endpoint/DTO cuối cùng (P3 cần làm contract).
```

## P2 — Báo cáo, truy vết, tài liệu (gửi sau khi P1 VERIFIED)

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-58-CS01-P2 — Báo cáo, truy vết, docs trả hàng | Mức: CRITICAL | Prompt 1/3
BASELINE: branch feature/OISMS-58-cac-chuc-nang-con-lai, commit 6ebffc7 + P1 chưa commit (đã VERIFIED, gồm SalesReturns service/API/test). Giữ nguyên mọi thay đổi chưa commit có sẵn.

MỤC TIÊU: Báo cáo và Tổng quan ra doanh thu thuần và giá vốn thuần sau khi trừ hàng trả. Truy vết đơn và hóa đơn hiện phiếu trả. Cập nhật docs cho contract.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/08-tra-hang-cs01.md: B5, B7, "Quyết định đã chốt" (ngày ghi nhận)
- docs/01-users-and-rbac/02-rbac-matrix.md (lãi gộp chỉ Owner)
- Mẫu: src/backend/src/OISM.Application/Features/Reports/Services/ReportService.cs, SalesInvoices/Services/SalesInvoiceService.cs (TraceByOrderIdAsync)
- Test có sẵn: test-oisms/backend/OISM.UnitTests/ReportServiceTests.cs

ĐƯỢC SỬA: ReportService và DTO báo cáo; SalesInvoiceService và DTO truy vết; OrderQueryService/DTO chi tiết đơn (thêm returnedQuantity theo dòng); test liên quan; docs/05-api-contracts/, docs/12-error-and-status-codes/02-error-codes-catalog.md, docs/01-users-and-rbac/02-rbac-matrix.md, docs/04-architecture-and-design/04-state-machines.md, docs/02-functional-requirements/README.md.
KHÔNG ĐƯỢC SỬA: luồng SalesReturns của P1 (trừ khi sửa lỗi chặn, phải ghi vào bàn giao), frontend, schema.

ACCEPTANCE CRITERIA:
- AC-01: báo cáo doanh thu/lợi nhuận (tổng, theo kênh, chi nhánh, ngày) và KPI Tổng quan có thêm returnAmount, netRevenue, returnCogs, netCogs. Chỉ tính phiếu Confirmed, theo confirmed_at, ngày UTC+7, lọc nửa mở. Không phá các trường cũ.
- AC-02: grossProfit tính trên số thuần, vẫn chỉ Owner thấy. Staff không nhận trường lãi gộp và giá vốn bị cấm theo ma trận.
- AC-03: chi tiết đơn có returnedQuantity theo từng dòng. TraceByOrderIdAsync trả thêm danh sách phiếu trả kèm các dòng sổ IN.
- AC-04: unit test cho trường hợp phiếu trả nằm ở ngày khác ngày bán (tách đúng kỳ) và phiếu Draft/Cancelled không bị tính. Có integration test PostgreSQL: bán, trả rồi gọi báo cáo.
- AC-05: docs cập nhật đúng các file được sửa ở trên; đổi trạng thái 08-tra-hang-cs01.md thành "backend đã xong" kèm ngày.

GATE: build; full UnitTests; IntegrationTests --filter 'FullyQualifiedName~Report|FullyQualifiedName~SalesReturn|FullyQualifiedName~SalesInvoice' trên PostgreSQL thật.

GIỚI HẠN: không commit/push; không nới assertion; một lỗi tối đa 2 lần thử.
BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md, kèm JSON mẫu của response báo cáo mới.
```

## P3 — Giao diện Admin (gửi sau khi P2 VERIFIED)

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-58-CS01-P3 — Giao diện Admin trả hàng | Mức: STANDARD | Prompt 1/3
BASELINE: branch feature/OISMS-58-cac-chuc-nang-con-lai, commit 6ebffc7 + P1, P2 chưa commit. Giữ nguyên mọi thay đổi chưa commit có sẵn.

MỤC TIÊU: Màn Admin để lập, xác nhận, hủy và tra cứu phiếu trả hàng; nối vào trang Kho, chi tiết đơn, truy vết, Báo cáo.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/08-tra-hang-cs01.md: "Kế hoạch giao diện Admin" (G1–G5, service, xử lý lỗi, kiểm thử)
- Contract: docs/05-api-contracts/ (mục trả hàng do P2 viết); DTO thực tế: src/backend/src/OISM.Application/Features/SalesReturns/Dtos/SalesReturnDtos.cs, controller: src/backend/src/OISM.WebApi/Controllers/SalesReturnsController.cs. Docs và DTO lệch nhau thì theo DTO và ghi vào bàn giao.
- Mẫu: src/frontend/admin-dashboard/src/pages/SalesInvoicesPage.tsx (danh sách, lọc ngày nửa mở), services/stockTransferService.ts (giữ requestKey BF-004), components/orders/OrderDetailDrawer.tsx, components/ui.tsx, components/NumberField.tsx
- Skill: skills/typescript-pro/SKILL.md, skills/react-state-management/SKILL.md

ĐƯỢC SỬA: src/frontend/admin-dashboard/src/ (types/salesReturns.ts, services/salesReturnService.ts, pages/SalesReturnsPage.tsx, components/salesReturns/*, App.tsx, AdminShell.tsx, InventoryPage.tsx (chỉ nút trả hàng), OrderDetailDrawer.tsx, SalesInvoiceTraceModal.tsx, ReportsPage.tsx, DashboardPage.tsx); docs/11-ui-screens/05-order-screens.md.
KHÔNG ĐƯỢC SỬA: backend, pos-app, phần khác của InventoryPage.

ACCEPTANCE CRITERIA:
- AC-01 (G1): route /returns và /admin/returns chỉ cho Owner, Staff; mục menu "Trả hàng" sau "Đơn đa kênh"; danh sách có lọc, phân trang, badge trạng thái, trạng thái rỗng, banner lỗi.
- AC-02 (G2): modal 3 bước. Số lượng giới hạn theo số còn được trả và decimalPlaces; có nút "Trả hết"; tiền hoàn tự tính, sửa được nhưng không vượt; lý do bắt buộc; đơn sàn ẩn phần hoàn tiền; có "Lưu nháp" và "Lưu và xác nhận".
- AC-03 (G3, G4): drawer chi tiết; xác nhận và hủy đều có hộp thoại hỏi lại; cột giá vốn chỉ hiện cho Owner, Staff.
- AC-04 (G5): bật nút ở trang Kho (bỏ ComingSoonBadge); thêm nút "Trả hàng" và cột "Đã trả" ở chi tiết đơn; truy vết có phần phiếu trả; Báo cáo và Tổng quan hiện hàng trả và doanh thu thuần.
- AC-05: Create giữ nguyên Idempotency-Key theo chữ ký payload khi kết quả chưa rõ; khóa nút theo id (busyIdsRef); "Lưu và xác nhận" mà xác nhận lỗi thì giữ phiếu Draft và mở drawer.
- AC-06: bảng thông báo lỗi tiếng Việt đúng theo docs; khi gặp RETURN_QUANTITY_EXCEEDED thì tải lại số còn được trả.
- AC-07: npm run build --prefix src/frontend/admin-dashboard pass, không có lỗi TypeScript mới.
- AC-08: chạy kịch bản kiểm tay ở mục "Kiểm thử giao diện" trên backend local, ghi kết quả từng kịch bản và ảnh chụp; cập nhật docs/11-ui-screens/05-order-screens.md.

GIỚI HẠN: không commit/push; không thêm dependency; một lỗi tối đa 2 lần thử.
BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md, kèm đường dẫn ảnh chụp.
```
