# 08. Trả hàng từ khách (CS-01)

> Trạng thái: **backend và giao diện Admin đã xong (09/11/2026)**. Đây không phải đề xuất của GVHD. Mô hình tham khảo KiotViet (trả hàng theo hóa đơn) và Odoo (return picking với giá vốn AVCO). Phần so sánh viết theo hiểu biết về hai hệ thống này, chưa đối chiếu lại tài liệu chính thức.

## Vấn đề cần giải quyết

Đơn đã `Confirmed` hoặc `Completed` thì đã trừ `OnHand` và ghi sổ `OUT`, nên không hủy được. Muốn nhận lại hàng khách trả phải lập phiếu trả hàng gắn với đơn gốc. Phiếu này nhập lại kho bằng dòng sổ `IN`, ghi nhận tiền hoàn cho khách và làm giảm doanh thu cũng như giá vốn trong báo cáo ([state machine](../04-architecture-and-design/04-state-machines.md)).

## Quyết định đã chốt

| Câu hỏi | Quyết định | Nguồn |
| :--- | :--- | :--- |
| Chứng từ gốc | Bắt buộc gắn đơn gốc (`order_id NOT NULL`). Chưa làm "trả nhanh" không có hóa đơn. | Đề xuất của nhóm, người dùng đồng ý 09/11/2026 |
| Đơn được trả | Đơn `Confirmed` hoặc `Completed`, tức là đã có dòng `OUT`. | Như trên |
| Đơn sàn (Shopee, TikTok, Lazada) | Được lập phiếu trả từ Admin. Phiếu nhập lại kho, không ghi hoàn tiền vì sàn hoàn cho khách. Báo cáo vẫn trừ doanh thu bằng giá trị dòng trả (SL trả × đơn giá snapshot), vì doanh thu thực của shop giảm. | Như trên; phần báo cáo do người dùng chốt 09/11/2026 |
| Quy trình | `Draft → Confirmed`; chỉ phiếu `Draft` được chuyển sang `Cancelled`. Phiếu đã `Confirmed` không hủy được, nếu sai thì xử lý bằng phiếu điều chỉnh. | Như trên, khớp CHECK hiện có |
| Vai trò | Owner và Staff được tạo, xác nhận và hủy phiếu. Cashier chưa được dùng: CS-02 chưa có phân công chi nhánh nên không giới hạn được theo chi nhánh. | Như trên |
| Giá vốn hàng nhập lại | `unit_cost = OrderItem.CostPrice` (giá vốn lúc bán, theo đơn vị gốc). Sau đó tính lại WAC chi nhánh bằng `WacCalculator`. | Như trên, giống KiotViet và Odoo AVCO |
| Ngày ghi nhận trong báo cáo | Ghi vào ngày xác nhận phiếu trả, không phải ngày bán gốc. Ngày nghiệp vụ theo UTC+07:00, lọc nửa mở. | Như trên |
| Trạng thái đơn gốc | Không đổi. API chỉ trả thêm số lượng đã trả theo từng dòng. | Như trên |
| Ngoài phạm vi | Trả nhanh không có hóa đơn, đổi hàng, hủy phiếu đã xác nhận, trả hàng tại POS. | Như trên |

## Đối chiếu KiotViet và Odoo

| Điểm | KiotViet | Odoo | OISMS |
| :--- | :--- | :--- | :--- |
| Chứng từ gốc | Trả theo hóa đơn, có thêm trả nhanh | Trả theo phiếu xuất gốc | Bắt buộc gắn đơn gốc |
| Số lượng được trả | ≤ đã mua − đã trả | ≤ đã giao − đã trả | ≤ `Quantity` của dòng đơn − tổng các phiếu `Confirmed`, kiểm tra lại dưới khóa |
| Quy trình | Một bước | Draft → Done | Draft → Confirmed |
| Giá vốn hàng nhập lại | Lấy giá vốn lúc bán | Lấy giá trị của phiếu xuất gốc | Lấy `OrderItem.CostPrice`, tính lại WAC |
| Tiền hoàn | Có phí trả hàng; tiền trả khách có thể nhỏ hơn giá bán | Credit note là chứng từ riêng | `refund_amount` ≤ SL × đơn giá snapshot của dòng |
| Đơn gốc | Hóa đơn giữ nguyên | SO giữ nguyên, chỉ giảm SL đã giao | Không đổi trạng thái |
| Báo cáo | Doanh thu thuần = doanh thu − tiền trả | Credit note làm giảm doanh thu | Trừ tiền hoàn và giá vốn hàng trả theo ngày xác nhận phiếu |
| Đổi hàng | Có | Làm bằng đơn mới | Giai đoạn 2: phiếu trả + POS checkout mới |

## Hiện trạng trước khi làm (kiểm tra 09/11/2026)

| Nội dung | Hiện trạng |
| :--- | :--- |
| Bảng `sales_returns`, `sales_return_items` | Đã có trong `oisms_schema.sql` và `RequiredTables`, đã cấp quyền cho `oisms_runtime` |
| `inventory_transactions.sales_return_item_id` | Đã có. CHECK bắt buộc chỉ được `IN`; UNIQUE `(tenant_id, sales_return_item_id)` nên mỗi dòng trả có đúng một dòng sổ |
| `daily_sales_summaries` | Đã có cột `returned_quantity` và `return_amount` |
| Entity, service, API | Chưa có. Chỉ `InventoryService` và `SalesInvoiceService` nhận ra loại nguồn `"SalesReturn"` |
| Idempotency | `sales_returns` chưa có cột `request_key` |
| Báo cáo | `ReportService` đang cộng `Order.TotalAmount` và `CostPrice × BaseQuantity` mà chưa trừ hàng trả |
| Đa đơn vị | `OrderItem.CostPrice` theo đơn vị gốc; `BaseQuantity = Quantity × FactorSnapshot` |

## Quy tắc nghiệp vụ

- **Đơn vị tính:** `sales_return_items.quantity` tính theo đơn vị của dòng đơn gốc. Số lượng gốc được suy từ `OrderItem.FactorSnapshot` (không đổi sau khi bán): `BaseQuantity = quantity × FactorSnapshot`. Tồn, sổ cái và WAC luôn dùng số lượng gốc.
- **Số lượng còn được trả** = `OrderItem.Quantity` − tổng `quantity` của các phiếu `Confirmed` trên cùng dòng đơn. Kiểm tra khi tạo phiếu, và kiểm tra lại dưới khóa khi xác nhận.
- **Tiền hoàn:** mặc định bằng `quantity × OrderItem.UnitPrice`; được nhập thấp hơn (phí trả hàng) nhưng không cao hơn. Tổng tiền hoàn của mọi phiếu trên một đơn không vượt `Order.TotalAmount`. Đơn sàn: `refund_amount = 0`, `refund_method = NULL`.
- **Kho nhận:** một phiếu nhận vào một kho, kho phải thuộc chi nhánh của đơn (FK `(tenant_id, warehouse_id, branch_id)` đã ràng buộc). Đơn gốc xuất từ nhiều kho vẫn trả về một kho.
- **Khi xác nhận:** mỗi dòng làm tăng `OnHand` ở cấp chi nhánh và cấp kho. Ghi `IN` với `unit_cost = original_unit_cost`, `BalanceAfter` và `SequenceNo` tiếp theo. Tính `AverageCost = WAC(OnHand cũ, AverageCost cũ, BaseQuantity, original_unit_cost)`. Không đụng `Reserved`.
- **Hoàn tiền:** `refund_method` (`Cash` hoặc `BankTransfer`) và `refunded_at` ghi cùng lúc xác nhận. Không tạo dòng `payments` mới.
- **Số phiếu:** `RT-yyyyMMdd-xxxx`, duy nhất trong tenant.

## Kế hoạch triển khai backend

### B1. Migration

File mới `docker/postgres/migrations/20261110_sales_returns_request_key.sql`:

- `sales_returns.request_key uuid NOT NULL`, UNIQUE `(tenant_id, request_key)`.
- `sales_returns.total_refund_amount numeric(20,2) NOT NULL DEFAULT 0`, CHECK `>= 0`.
- `sales_returns.note text NULL`.
- Bảng đang trống nên thêm `NOT NULL` được. Nếu lúc chạy đã có dòng thì script phải gán `gen_random_uuid()` trước.

Cập nhật `oisms_schema.sql` và phần kiểm tra cột, ràng buộc trong `DatabaseMigrationService`. Không thêm bảng, nên số bảng vẫn là 44; `RequiredTables` và `init-oisms.sh` giữ nguyên.

### B2. Domain và Infrastructure

- `OISM.Domain/Entities/SalesReturn.cs`, `SalesReturnItem.cs` (implement `IMustHaveTenant`). Thêm `SalesReturnStatus` vào `DocumentStatuses.cs`.
- `Persistence/Configurations/SalesReturnConfiguration.cs`, `SalesReturnItemConfiguration.cs`. Thêm `DbSet` vào `AppDbContext` và `IAppDbContext`, kèm query filter theo tenant.
- `ISqlLockService`: thêm `LockSalesReturnAsync(tenantId, returnId)` và `LockSalesReturnNumberAsync(tenantId)`.
- Bổ sung quan hệ `InventoryTransaction.SalesReturnItem` để truy vết.

### B3. Application: `Features/SalesReturns/`

| Method | Nội dung |
| :--- | :--- |
| `GetReturnableItemsAsync(orderId)` | Với mỗi dòng đơn: SL đã bán, đã trả, còn được trả, đơn giá, đơn vị |
| `CreateAsync(dto, requestKey, userId)` | Tạo phiếu `Draft`. Cùng key và cùng payload trả lại phiếu cũ; khác payload trả `409 IDEMPOTENCY_PAYLOAD_MISMATCH` |
| `ConfirmAsync(id, refundMethod, userId)` | Ghi sổ `IN`, tính lại WAC, ghi tiền hoàn |
| `CancelAsync(id, userId)` | Chỉ cho phiếu `Draft` |
| `GetListAsync`, `GetByIdAsync` | Lọc theo chi nhánh, trạng thái, khoảng ngày (nửa mở), đơn gốc |

`ConfirmAsync` chạy trong `CreateExecutionStrategy()` và một transaction:

1. Mở transaction, sau đó mới lấy khóa. Thứ tự khóa: SKU đã sắp xếp (`LockSkuStockAsync`) → phiếu trả → đơn gốc → `LockBranchInventoryRowsAsync` → `LockWarehouseInventoryRowsAsync`. Trước khi code phải đối chiếu với thứ tự trong `PosCheckoutService` và `ConfirmOrderAsync` để không tạo deadlock.
2. Phiếu đã `Confirmed` thì trả kết quả cũ (idempotent). Phiếu `Cancelled` thì trả `409 INVALID_STATE_TRANSITION`.
3. Kiểm tra lại số lượng còn được trả và trạng thái đơn.
4. Tăng `OnHand`, ghi `InventoryTransaction` loại `IN`, tính lại WAC.
5. Chuyển sang `Confirmed`, ghi `confirmed_by`, `confirmed_at` và thông tin hoàn tiền. Lưu, rồi commit.
6. Sau commit, phát SignalR `stock.changed`.

Đăng ký service trong `OISM.Application/DependencyInjection.cs`.

### B4. WebApi: `SalesReturnsController`

| Method | Đường dẫn | Vai trò | Ghi chú |
| :--- | :--- | :--- | :--- |
| `GET` | `/api/orders/{id}/returnable-items` | Owner, Staff | |
| `POST` | `/api/sales-returns` | Owner, Staff | Bắt buộc `Idempotency-Key` hoặc `X-Request-Key`. Tạo mới trả `201`, replay trả `200` |
| `GET` | `/api/sales-returns` | Owner, Staff | Phân trang, có lọc |
| `GET` | `/api/sales-returns/{id}` | Owner, Staff | Có `originalUnitCost` (Staff được xem giá vốn theo ma trận RBAC) |
| `POST` | `/api/sales-returns/{id}/confirm` | Owner, Staff | Body: `refundMethod` |
| `POST` | `/api/sales-returns/{id}/cancel` | Owner, Staff | |

Mã lỗi mới, thêm vào `ApiErrorTitles` và [catalog](../12-error-and-status-codes/02-error-codes-catalog.md):

| Mã | HTTP | Khi nào |
| :--- | :--- | :--- |
| `SALES_RETURN_NOT_FOUND` | 404 | Phiếu không có hoặc thuộc tenant khác |
| `ORDER_NOT_RETURNABLE` | 409 | Đơn chưa xuất kho hoặc đã hủy |
| `RETURN_QUANTITY_EXCEEDED` | 422 | Vượt số lượng còn được trả |
| `REFUND_AMOUNT_EXCEEDED` | 422 | Tiền hoàn vượt giá bán của dòng hoặc tổng đơn |
| `RETURN_WAREHOUSE_MISMATCH` | 422 | Kho không thuộc chi nhánh của đơn |

Dùng lại `INVALID_STATE_TRANSITION`, `IDEMPOTENCY_PAYLOAD_MISMATCH`, `VALIDATION_ERROR`, `ORDER_NOT_FOUND`.

### B5. Báo cáo và truy vết

- `ReportService`, gồm báo cáo doanh thu/lợi nhuận theo kênh, chi nhánh, ngày và KPI Tổng quan:
  - doanh thu thuần = doanh thu − Σ `refund_amount`;
  - giá vốn thuần = giá vốn − Σ (`BaseQuantity` × `original_unit_cost`).
  - Chỉ tính phiếu `Confirmed`, theo `confirmed_at`. Lãi gộp vẫn chỉ Owner xem.
- `SalesInvoiceService.TraceByOrderIdAsync`: trả thêm danh sách phiếu trả của đơn và dòng sổ `IN` tương ứng.
- Lịch sử xuất kho cho AI gợi ý nhập (`GetExportHistoryAsync`): giữ nguyên, chỉ tính `OUT` bán. Có trừ hàng trả hay không thì ghi lại để chốt sau.

### B6. Kiểm thử

Mức rủi ro CRITICAL (ledger, WAC, concurrency), nên phải có bằng chứng trên PostgreSQL thật.

| Loại | Trường hợp |
| :--- | :--- |
| Unit | Kiểm tra đầu vào; tính tiền hoàn mặc định và giới hạn; WAC khi tồn cũ ≤ 0; quy đổi `FactorSnapshot` |
| Integration PostgreSQL | Xác nhận phiếu: `OnHand` cấp chi nhánh và cấp kho tăng đúng; dòng `IN` có `BalanceAfter`, `SequenceNo` khớp; WAC đúng công thức |
| Integration PostgreSQL | Hai phiếu đồng thời trên cùng dòng đơn, tổng vượt số lượng đã bán: một phiếu qua, phiếu kia `422` |
| Integration PostgreSQL | Replay cùng key và cùng payload trả kết quả cũ; khác payload trả `409`; xác nhận hai lần không ghi sổ hai lần |
| Integration PostgreSQL | Đơn bán theo thùng (`FactorSnapshot` = 24), trả 1 thùng thì cộng 24 đơn vị gốc |
| Integration PostgreSQL | Tenant khác không thấy, không xác nhận được phiếu (`404`) |
| Integration | Báo cáo doanh thu thuần, giá vốn thuần theo ngày trả |
| Integration | Đơn sàn: không hoàn tiền, vẫn nhập kho |

### B7. Tài liệu và bàn giao

- Hợp đồng API trong `docs/05-api-contracts/`, ma trận RBAC, `04-state-machines.md` (thêm state machine của phiếu trả), `docs/02-functional-requirements/README.md` (`FR-INV-01` có `Return`).
- Báo cáo kết quả trong `docs/report-after-upgrade/` sau khi xong.
- Gửi contract cho nhóm FE để làm màn Admin "Nhận hàng trả", xem [kế hoạch giao diện](#kế-hoạch-giao-diện-admin).

## Kế hoạch giao diện Admin

Giao diện do nhóm Frontend làm (`OISMS-46`), bắt đầu khi B4 đã có contract. Chỉ làm trên Admin (`src/frontend/admin-dashboard`). Chưa làm trên POS.

### Hiện trạng giao diện (kiểm tra 09/11/2026)

| Vị trí | Hiện trạng |
| :--- | :--- |
| `InventoryPage.tsx` | Nút "Ghi nhận trả hàng" bị tắt, gắn nhãn `ComingSoonBadge` |
| `InventoryPage.tsx`, bộ lọc sổ kho | Đã có lựa chọn "Khách trả hàng"; `apiAdapter.ts` đã đổi nguồn `SalesReturn` thành nhãn này |
| `OrderDetailDrawer.tsx` | Chưa có nút trả hàng, chưa hiện số lượng đã trả |
| `SalesInvoiceTraceModal.tsx` | Chưa hiện phiếu trả |
| `App.tsx`, `AdminShell.tsx` | Chưa có route và mục menu trả hàng |
| Mẫu giữ `requestKey` khi retry | `stockTransferService.ts` (BF-004): giữ khóa theo chữ ký payload trong `sessionStorage` |

### Luồng người dùng

```text
Điểm vào (menu Trả hàng, nút ở Kho, nút ở chi tiết đơn)
  → Bước 1. Chọn đơn gốc
  → Bước 2. Nhập dòng trả, kho nhận, lý do, tiền hoàn
  → Bước 3. Xem lại → [Lưu nháp] hoặc [Lưu và xác nhận]
  → Danh sách phiếu trả → Chi tiết → [Xác nhận] / [Hủy] khi còn nháp
```

### Màn hình và thành phần

**G1. Trang danh sách phiếu trả** (`pages/SalesReturnsPage.tsx`)

- Route `/returns` và `/admin/returns`, chỉ Owner và Staff (`ProtectedRoute allowedRoles={["Owner","Staff"]}`).
- Thêm mục menu "Trả hàng" (icon `RotateCcw`) ngay sau "Đơn đa kênh" trong `AdminShell.tsx`.
- Bộ lọc: số phiếu hoặc số đơn gốc, chi nhánh, trạng thái, khoảng ngày. Ngày tính theo UTC+07:00 và gửi `toDate` là đầu ngày kế tiếp (nửa mở), giống `SalesInvoicesPage`.
- Bảng: số phiếu, đơn gốc (bấm để mở chi tiết đơn), kênh, chi nhánh, kho nhận, số dòng, tiền hoàn, trạng thái, người tạo, ngày xác nhận.
- Badge trạng thái: `Draft` màu xám, `Confirmed` màu xanh lá, `Cancelled` màu đỏ nhạt, theo [color tokens](../12-error-and-status-codes/03-color-system-tokens.md).
- Nút "Tạo phiếu trả" ở tiêu đề trang. Bảng phân trang, có trạng thái rỗng và banner lỗi.

**G2. Modal tạo phiếu trả** (`components/salesReturns/CreateSalesReturnModal.tsx`), 3 bước:

1. **Chọn đơn gốc:** tìm theo số đơn hoặc số hóa đơn. Nếu mở từ chi tiết đơn thì bỏ qua bước này. Đơn không ở trạng thái `Confirmed` hay `Completed` thì báo "Đơn chưa xuất kho, không trả được".
2. **Nhập dòng trả:** gọi `GET /api/orders/{id}/returnable-items`. Bảng gồm SKU, đơn vị, đơn giá, đã bán, đã trả, còn được trả, ô **SL trả** và ô **Tiền hoàn**.
   - Ô SL trả dùng `NumberField`, số lẻ theo `decimalPlaces` của đơn vị, tối đa bằng số còn được trả. Dòng hết số được trả thì khóa ô.
   - Nút "Trả hết" điền toàn bộ số còn được trả.
   - Tiền hoàn tự tính bằng SL × đơn giá; được sửa thấp hơn nhưng không cao hơn. Hiện dòng "Phí trả hàng" bằng phần chênh lệch.
   - Kho nhận: chọn trong các kho của chi nhánh đơn, mặc định kho đầu tiên.
   - Lý do: bắt buộc, có gợi ý (Lỗi sản phẩm, Sai hàng, Khách đổi ý, Khác) và ô ghi chú.
   - Phương thức hoàn: `Cash` hoặc `BankTransfer`. Với đơn sàn thì ẩn mục này, tiền hoàn bằng 0 và hiện ghi chú "Tiền do sàn hoàn cho khách".
3. **Xem lại:** tổng số lượng, tổng tiền hoàn, kho nhận. Hai nút: "Lưu nháp" (chỉ tạo phiếu) và "Lưu và xác nhận" (tạo xong thì gọi xác nhận ngay).

**G3. Drawer chi tiết phiếu trả** (`components/salesReturns/SalesReturnDetailDrawer.tsx`)

- Phần đầu: số phiếu, trạng thái, đơn gốc, chi nhánh, kho, lý do, người tạo/xác nhận, thời gian.
- Bảng dòng: SKU, đơn vị, SL trả, SL quy đổi gốc, tiền hoàn, giá vốn gốc. Cột giá vốn chỉ hiện cho Owner và Staff, theo ma trận RBAC.
- Phiếu `Draft`: nút "Xác nhận nhập kho" và "Hủy phiếu".
- Phiếu `Confirmed`: liên kết sang sổ kho đã lọc "Khách trả hàng" và sang truy vết hóa đơn của đơn gốc.

**G4. Hộp thoại xác nhận** (`components/salesReturns/ConfirmSalesReturnDialog.tsx`)

- Nội dung dạng: "Nhập lại X sản phẩm vào kho Y và hoàn Z đ bằng tiền mặt. Sau khi xác nhận không hủy được."
- Hủy phiếu nháp cũng phải hỏi lại.

**G5. Chỉnh các màn có sẵn**

| File | Thay đổi |
| :--- | :--- |
| `InventoryPage.tsx` | Bật nút "Ghi nhận trả hàng", bỏ `ComingSoonBadge`; bấm thì mở G2 ở bước 1 |
| `OrderDetailDrawer.tsx` | Thêm nút "Trả hàng" khi đơn `Confirmed`/`Completed` và còn số lượng được trả; mở G2 ở bước 2. Thêm cột "Đã trả" và danh sách phiếu trả của đơn |
| `SalesInvoiceTraceModal.tsx` | Thêm phần "Phiếu trả hàng" kèm dòng sổ `IN` |
| `ReportsPage.tsx`, `DashboardPage.tsx` | Khi backend trả thêm trường: hiện "Hàng trả" và "Doanh thu thuần". Lãi gộp vẫn chỉ Owner |
| `App.tsx`, `AdminShell.tsx` | Route và mục menu của G1 |

### Service, type và xử lý lỗi

- `types/salesReturns.ts`: `SalesReturnSummary`, `SalesReturnDetail`, `ReturnableItem`, `CreateSalesReturnRequest`, `SalesReturnFilter`.
- `services/salesReturnService.ts`: `getReturnableItems`, `create`, `list`, `getById`, `confirm`, `cancel`.
- **Idempotency:** `create` gửi `Idempotency-Key` và giữ nguyên khóa theo chữ ký payload khi kết quả chưa rõ (timeout, mất mạng), giống `stockTransferService.ts`. Chỉ tạo khóa mới khi payload đổi hoặc đã có kết quả thành công.
- **Chống bấm nhiều lần:** khóa nút theo id phiếu (`busyIdsRef`) khi đang xác nhận hoặc hủy. Kết quả chưa rõ thì giữ khóa form và hiện "Đang kiểm tra kết quả", không cho gửi payload khác.
- **"Lưu và xác nhận":** tạo thành công mà xác nhận lỗi thì giữ phiếu ở `Draft`, báo rõ và mở G3 để người dùng xác nhận lại.
- **Thông báo lỗi** (đọc `extensions.errorCode` từ ProblemDetails):

| Mã | Thông báo |
| :--- | :--- |
| `RETURN_QUANTITY_EXCEEDED` | "Số lượng trả vượt số còn được trả", sau đó tải lại số còn được trả |
| `REFUND_AMOUNT_EXCEEDED` | "Tiền hoàn vượt giá bán" |
| `ORDER_NOT_RETURNABLE` | "Đơn chưa xuất kho hoặc đã hủy" |
| `RETURN_WAREHOUSE_MISMATCH` | "Kho không thuộc chi nhánh của đơn" |
| `INVALID_STATE_TRANSITION` | "Phiếu đã được xử lý", sau đó tải lại |
| `IDEMPOTENCY_PAYLOAD_MISMATCH` | "Yêu cầu trùng với một phiếu khác", sau đó tạo khóa mới |

- **Realtime:** khi nhận `stock.changed` thì trang Kho tải lại tồn. Sau khi xác nhận thì tải lại danh sách phiếu và chi tiết đơn.

### Kiểm thử giao diện

- `npm run build --prefix src/frontend/admin-dashboard` không lỗi. Admin chưa có script test nên kiểm bằng tay.
- Kiểm tay trên dữ liệu demo:
  - đơn POS bán theo đơn vị gốc;
  - đơn bán theo thùng (`FactorSnapshot` > 1);
  - đơn sàn;
  - trả một phần rồi trả tiếp phần còn lại;
  - nhập vượt số được trả;
  - tắt mạng giữa lúc tạo phiếu rồi gửi lại: không sinh hai phiếu;
  - hai tab cùng xác nhận một phiếu;
  - đăng nhập Cashier không thấy menu và bị chặn khi vào `/returns`.
- Sau khi xác nhận, sổ kho lọc "Khách trả hàng" có dòng `IN`, và tồn trên trang Kho tăng đúng.
- Cập nhật đặc tả màn hình: thêm mục trả hàng vào `docs/11-ui-screens/05-order-screens.md`, kèm ảnh chụp trong `screenshots/`.

### Thứ tự làm

1. G1 (trang danh sách) chạy trên dữ liệu thật, cùng service và type.
2. G2 và G4: tạo và xác nhận phiếu.
3. G3 (drawer chi tiết) và G5: nút ở chi tiết đơn, nút ở trang Kho, truy vết.
4. Báo cáo và Tổng quan, sau khi backend làm xong B5.

## Câu hỏi còn mở

- Lịch sử xuất kho dùng cho AI gợi ý nhập có trừ hàng trả không.
- Trả hàng tại POS cho Cashier: chờ CS-02 có phân công chi nhánh.
- `daily_sales_summaries` hiện chưa được ghi. Nếu sau này dùng bảng này thì phải cộng `returned_quantity` và `return_amount` từ phiếu trả.
