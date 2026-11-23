# Báo cáo sau nâng cấp: Phiếu xuất kho 02-VT và Bảng kê xuất bán POS theo ngày (Upgrade 13 — Gói 3)

| Mục | Nội dung |
| --- | --- |
| Task | `OISMS-66-L2-G3` — Phiếu xuất kho 02-VT lưu kèm dòng sổ `OUT`, in đơn lẻ, in hàng loạt, bảng kê POS theo ngày |
| Ngày kiểm tra | 19/11/2026 |
| Đặc tả | [`docs/Upgrade-recommended/Pedding/13-ton-kho-theo-ky.md`](../Upgrade-recommended/Pedding/13-ton-kho-theo-ky.md) (mục 5 P1–P6, Q10, Q13; mục 6; mục 7 BE-13.4→13.6, FE-13.3) |
| Phạm vi | Database schema & migration (`50 bảng`), Backend (`Domain`, `Application`, `Infrastructure`, `WebApi`), Frontend Admin (`OrdersPage`, modal in 02-VT & bảng kê POS), Unit & Integration Tests trên PostgreSQL 16 thật, Tài liệu |
| Trạng thái | Hoàn thành & đã kiểm chứng trên PostgreSQL 16 thật |

## 1. Tóm tắt nội dung đã triển khai

- **Cơ sở dữ liệu & Migration (`AC-01`):**
  - Thêm 2 bảng mới `goods_issues` và `goods_issue_items` trong [`docs/database/oisms_schema.sql`](../database/oisms_schema.sql) và migration [`docker/postgres/migrations/20261119_goods_issues.sql`](../../docker/postgres/migrations/20261119_goods_issues.sql), nâng tổng số bảng bắt buộc từ **48 lên 50 bảng** (đồng bộ ở `oisms_schema.sql`, `docker/postgres/init-oisms.sh`, `DatabaseMigrationService.RequiredTables`).
  - Ràng buộc chính: `tenant_id`, `UNIQUE (tenant_id, number)`, `UNIQUE (tenant_id, order_id, warehouse_id)`, `UNIQUE (tenant_id, inventory_transaction_id)` (bảo đảm mỗi dòng sổ cái `OUT` bán hàng gắn đúng 1 dòng phiếu xuất kho), cùng các FK ghép `tenant_id` tới `orders`, `sales_invoices`, `warehouses`, `inventory_transactions`, `order_items`, `skus`, `units`, `branches`, `users`.
  - Bất biến chứng từ: cấp quyền `SELECT, INSERT` cho `oisms_runtime` / `oisms_app` / `oisms_test_app`, thu hồi `UPDATE, DELETE, TRUNCATE` và gắn trigger `goods_issues_append_only`, `goods_issue_items_append_only`.
  - Guard nâng cấp: nếu DB đã có dòng `inventory_transactions` xuất bán (`order_item_id IS NOT NULL`) chưa gắn `goods_issue_items`, migration chủ động `RAISE EXCEPTION` hướng dẫn reset và seed lại DB; chạy 2 lần liên tiếp trên DB sạch không lỗi.
- **Lập nguyên tử cùng giao dịch xuất bán (`AC-02`, `AC-03`, `AC-04`):**
  - Tại `PosCheckoutService.CheckoutAsync` và `OrderService.ConfirmOrderAsync`, ngay sau khi ghi các dòng `InventoryTransaction` (`Type = "OUT"`) và tạo `SalesInvoice`, `GoodsIssueFactory.CreateForOrderOutTransactions` lập luôn `GoodsIssue` + `GoodsIssueItem` trong cùng database transaction, giữ nguyên thứ tự khóa hiện có và không thêm khóa mới.
  - Quy tắc đánh số (`P5`, `Q13`): đơn xuất từ 1 kho mang số `PXK-<số hóa đơn>`; đơn xuất từ nhiều kho nhóm theo `WarehouseId` tăng dần (`OrderBy(g => g.Key)`), đánh số `PXK-<số hóa đơn>-1`, `PXK-<số hóa đơn>-2`, ... kèm tên kho chụp (`warehouse_name_snapshot`).
  - Mỗi dòng `GoodsIssueItem` lưu `quantity` (theo đơn vị dòng đơn), `base_quantity` (theo đơn vị gốc = `inventory_transactions.quantity`) và `unit_cost = inventory_transactions.unit_cost`.
  - Replay cùng `Idempotency-Key` / `RequestKey` trả lại kết quả cũ, không tạo thêm phiếu xuất; lỗi phát sinh giữa chừng rollback toàn bộ transaction, không để lại phiếu xuất mồ côi.
- **API Phiếu xuất kho, Bảng kê POS theo ngày & Truy vết (`AC-05`, `AC-06`):**
  - `GET /api/orders/{orderId}/goods-issues` (alias `GET /api/goods-issues/by-order/{orderId}`): danh sách phiếu xuất của một đơn hàng.
  - `GET /api/goods-issues/{id}`: chi tiết một phiếu xuất kho.
  - `POST /api/goods-issues/by-orders`: lấy danh sách phiếu xuất của nhiều đơn để in hàng loạt.
  - `GET /api/goods-issues/pos-daily-summary?date=YYYY-MM-DD&branchId=`: bảng kê xuất bán POS theo ngày nghiệp vụ `UTC+07:00` (tổng hợp số lượng gốc và thành tiền vốn theo SKU + danh sách phiếu xuất/hóa đơn POS trong ngày).
  - Phân quyền & che giá vốn: `Owner` nhận đầy đủ `unitCost` và `totalCost`; `Staff` nhận `unitCost = null`, `totalCost = null`; `Cashier` bị chặn `403 Forbidden`.
  - DTO sổ cái (`InventoryLedgerDto`) và truy vết chứng từ (`DocumentTraceLineDto`) bổ sung `goodsIssueId` và `goodsIssueNumber` cho các dòng `OUT` bán hàng.
- **Giao diện Web Admin (`AC-07`):**
  - [`GoodsIssuePrintModal.tsx`](../../src/frontend/admin-dashboard/src/components/orders/GoodsIssuePrintModal.tsx): biểu mẫu in **Phiếu xuất kho — Mẫu số 02-VT** (kèm 3 chữ ký *Người lập phiếu*, *Thủ kho*, *Người nhận hàng*), hỗ trợ in đơn lẻ và in hàng loạt nhiều đơn/nhiều kho trong một lệnh in; tự động ẩn cột Đơn giá vốn & Thành tiền vốn khi `Staff` đăng nhập.
  - [`PosDailyGoodsIssueSummaryModal.tsx`](../../src/frontend/admin-dashboard/src/components/orders/PosDailyGoodsIssueSummaryModal.tsx): xem và in **Bảng kê xuất bán POS theo ngày** theo ngày `UTC+07:00` và chi nhánh; ẩn cột giá trị vốn đối với `Staff`.
  - [`OrdersPage.tsx`](../../src/frontend/admin-dashboard/src/pages/OrdersPage.tsx): thêm nút **In phiếu xuất kho** trên từng đơn đã xác nhận/hoàn tất (không tự bật hộp thoại in khi xác nhận đơn), checkbox chọn nhiều đơn để **In phiếu xuất kho hàng loạt**, và nút mở **Bảng kê xuất bán POS theo ngày**.

## 2. Kết quả kiểm chứng

| Lệnh kiểm tra | Môi trường | Kết quả |
| --- | --- | --- |
| `dotnet build src/backend/OISM.sln` | .NET 8 SDK | **PASS** (0 warnings, 0 errors) |
| `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj` | xUnit / InMemory | **PASS 428/428** |
| `dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj --filter "FullyQualifiedName~PostgresGoodsIssueIntegrationTests"` | PostgreSQL 16 thật (`oism_postgres`) | **PASS 4/4** |
| `dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj --filter "FullyQualifiedName~PosCheckout\|FullyQualifiedName~SalesInvoice\|FullyQualifiedName~OrderLifecycle\|FullyQualifiedName~Concurrency"` | PostgreSQL 16 thật (`oism_postgres`) | **PASS 93/93** |
| `npm run build` (`src/frontend/admin-dashboard`) | Vite / TypeScript | **PASS** |

## 3. Sửa sau review (19/11/2026)

- Thẻ kho S12/S10 (`GET /api/reports/stock-card`) hiện số phiếu xuất kho (`goodsIssueNumber`) cho dòng `OUT` bán hàng, cùng với sổ cái và truy vết, theo mục Đối chiếu của đặc tả.
- `POST`/`GET /api/goods-issues/by-orders` chặn quá 100 đơn một lần (`400 VALIDATION_ERROR`), khớp hợp đồng API đã ghi.
- Review trên PostgreSQL 16 thật: toàn bộ integration test PASS trừ 6 test G2 (đã sửa riêng), gồm 4/4 test phiếu xuất kho, checkout POS, xác nhận đơn và test đồng thời.

## 4. Sửa sau kiểm tra lại (21/11/2026)

- **Bảng kê POS đếm trùng tiền hóa đơn:** đơn POS xuất từ nhiều kho có nhiều phiếu cùng một hóa đơn, trước đây mỗi phiếu đều mang nguyên `totalSalesAmount` của hóa đơn nên cộng cột bị trùng. Nay tiền hóa đơn chỉ ghi ở phiếu đầu tiên (theo số phiếu), các phiếu còn lại là `0`; thêm `totalSalesAmount` tổng của bảng kê, mỗi hóa đơn tính một lần.
- **Hợp đồng API:** mục bảng kê trong `docs/05-api-contracts/04-order-endpoints.md` (`v1.5.1`) ghi lại đúng tên trường mà backend trả (`items`, `invoices`, `totalOrders`, `totalInvoices`, `totalGoodsIssues`), trước đó ghi `skuLines`, `vouchers`, `totalVoucherCount`.
- **Migration và khởi tạo DB:** trên PostgreSQL 16 tạm, `init-oisms.sh` với volume trống dựng đủ 50 bảng; `20261119_purchase_order_pending_approval.sql` và `20261119_goods_issues.sql` chạy 2 lần không lỗi trên DB mới và trên DB 48 bảng của commit `553563e`; schema sau nâng cấp khớp schema cài mới (`pg_dump -s`, chỉ khác schema `hangfire` do `init-oisms.sh` tạo).
