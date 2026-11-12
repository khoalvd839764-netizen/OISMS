# Sổ task — OISMS-58-CS01 (Trả hàng từ khách)

- **Mục tiêu / nguồn docs:** CS-01 trả hàng gắn đơn gốc, backend + giao diện Admin. Nguồn: `docs/Upgrade-recommended/08-tra-hang-cs01.md`.
- **Mức / định tuyến / lý do:** CRITICAL (schema, ledger, WAC, idempotency, concurrency). **CHIA** làm 3 phần chạy tuần tự, vì tổng hơn 8 AC và trải nhiều tầng:
  - P1: backend lõi (B1–B4, B6)
  - P2: backend báo cáo, truy vết, docs (B5, B7)
  - P3: giao diện Admin (G1–G5)
- **Baseline:** branch `feature/OISMS-58-cac-chuc-nang-con-lai` tạo từ `origin/develop` `6ebffc7`, chưa đặt upstream. Có sẵn thay đổi chưa commit ở docs: `08-tra-hang-cs01.md`, `09-chatbot-ai-co-ban.md`, `README.md`.
- **Trạng thái:** P1, P2, P3 đều VERIFIED (09/11/2026; Claude tự sửa finding, không tốn vòng Anti). Chờ người dùng đồng ý commit.
- **Bộ đếm:** mỗi phần tính riêng `prompt x/3`, trần vòng sửa CRITICAL = 1, P3 STANDARD = 2.
  - P1: 1/3 · vòng 0/1 · finding chặn 1 → 0
  - P2: 1/3 · vòng 0/1 · finding chặn 1 → 0
  - P3: 1/3 · vòng 0/2 · finding chặn 1 → 0

| AC | Phần | Nguồn docs | Người làm | Trạng thái | Evidence |
| --- | --- | --- | --- | --- | --- |
| P1 AC-01..08 | P1 | 08-tra-hang-cs01.md#b1..b4, #b6 | Anti + Claude sửa | VERIFIED | build 0 lỗi; Unit 284/284; Integration PostgreSQL SalesReturn 9/9 (B và B2 chạy lặp 3 lần đều PASS); hồi quy 177/177; migration 2 lần trên DB tạm: PASS (chạy lúc review P2) |
| P2 AC-01..05 | P2 | 08-tra-hang-cs01.md#b5, #b7 | Anti + Claude sửa | VERIFIED | build 0 lỗi; Unit 291/291; Integration PostgreSQL 115/115 (Report, SalesReturn, SalesInvoice, MultiUnit, OnlineOrderLifecycle, PosCheckout, Health); migration chạy 2 lần trên DB tạm dựng từ schema 6ebffc7: PASS |
| P3 AC-01..08 | P3 | 08-tra-hang-cs01.md#kế-hoạch-giao-diện-admin | Anti + Claude sửa | VERIFIED | `npm run build` admin PASS (tsc + vite); Anti chạy 9 kịch bản trên backend local và chụp 6 ảnh (Claude đã xem ảnh 02, 06: số liệu khớp NetRevenue − NetCogs = lãi gộp); Claude không chạy lại kịch bản tay |

| Finding | Mức | Số lần trượt | Trạng thái |
| --- | --- | --- | --- |
| F-01 P1 · CancelAsync không có transaction và khóa: hủy và xác nhận cùng lúc thì CHECK `ck_sales_returns_confirmed` chặn ở DB, nhưng API trả 500 thay vì 409 | P2, chặn | 0 | Đóng (Claude sửa: transaction + LockOrderAsync → LockSalesReturnAsync, có test B2) |
| F-02 P1 · ConfirmAsync không kiểm tra lại tổng tiền hoàn của đơn dưới khóa | P3 | 0 | Đóng (Claude sửa) |
| F-03 P1 · Không chặn SL quá 3 chữ số thập phân và tiền hoàn quá 2 chữ số, PostgreSQL làm tròn ngầm | P3 | 0 | Đóng (Claude sửa, có unit test) |
| F-04 P1 · Số phiếu RT-yyyyMMdd lấy ngày theo UTC, không theo UTC+7 | P3 | 0 | Đóng (Claude sửa) |
| F-01 P2 · `TotalCostOfGoodsSold` là giá vốn gộp trong khi `TotalNetRevenue` và `TotalGrossProfit` là số thuần, nên trang Báo cáo hiển thị doanh thu − giá vốn ≠ lãi gộp khi có hàng trả | P2, chặn | 0 | Đóng (Claude sửa: Total* đều là số thuần; cập nhật 2 assertion và docs) |
| F-02 P2 · Docs `/api/sales-returns` ghi bộ lọc `fromConfirmedDate`/`toConfirmedDate` không có trong DTO | P3 | 0 | Đóng (Claude sửa docs) |

| F-01 P3 · Tiền hoàn tối đa tính bằng số thực JS (vd 1.1 × 3 = 3.3000000000000003), backend trả 400 vì quá 2 chữ số thập phân | P2, chặn | 0 | Đóng (Claude sửa: tính bằng số nguyên xu, cắt xuống 2 chữ số) |
| F-02 P3 · Ô số lượng trả `decimals={0}` nên không nhập được hàng bán lẻ (kg, mét) | P3 | 0 | Đóng (Claude sửa: 3 chữ số) |
| F-03 P3 · Khi gặp RETURN_QUANTITY_EXCEEDED, giảm SL nhưng không tính lại tiền hoàn tối đa, sửa trực tiếp object trong state | P3 | 0 | Đóng (Claude sửa) |
| F-04 P3 · Chữ ký idempotency thiếu `note`; IDEMPOTENCY_PAYLOAD_MISMATCH không xóa khóa cũ | P3 | 0 | Đóng (Claude sửa) |
| F-05 P3 · `types/reports.ts` chưa có trường thuần, trang Báo cáo và Tổng quan dùng 20 chỗ `as any` | P3 | 0 | Đóng (Claude sửa: thêm trường, đổi trường giá vốn/lãi sang `number \| null`) |

- **Quyết định đã chốt (người dùng, 09/11/2026):**
  - Owner và Staff được thao tác; Cashier chưa.
  - Đơn sàn được trả, nhưng không ghi hoàn tiền.
  - Báo cáo tính hàng trả theo ngày xác nhận phiếu.
  - Ngoài phạm vi: trả nhanh không có hóa đơn, đổi hàng, hủy phiếu đã xác nhận, trả tại POS.
- **Thứ tự khóa do Claude chốt** (đối chiếu `OrderService.Expiry.cs:216-271` và `OrderService.cs:121-125`):
  - Confirm: `LockSkuStockAsync` (SKU đã sắp xếp, chi nhánh của đơn) → `LockRequestKeyAsync(return.RequestKey)` → `LockOrderAsync(orderId)` → `LockSalesReturnAsync(returnId)` → `LockBranchInventoryRowsAsync` → `LockWarehouseInventoryRowsAsync` (kho nhận) → `SetConstraintsImmediateAsync`.
  - Create: `LockRequestKeyAsync` → `LockOrderAsync` → `LockSalesReturnNumberAsync`.
  - Cancel: `LockOrderAsync` → `LockSalesReturnAsync`.
- **Backlog ngoài scope:** dòng đơn cũ có `CostPrice` NULL thì phiếu trả lấy giá vốn gốc = 0, làm WAC tụt (cân nhắc lấy `AverageCost` hiện tại); `CreateAsync` chưa có `LockOrderAsync` (không sai, vì Confirm kiểm tra lại dưới khóa); `README.md` gốc có thay đổi ngoài task (không do Anti báo, Claude không đụng); trả tại POS (chờ CS-02), ghi `daily_sales_summaries`, lịch sử xuất kho cho AI có trừ hàng trả hay không.
- **Phạm vi nhánh OISMS-58:** sau CS-01 còn CS-06, U01-B, phân công chi nhánh của CS-02. Mỗi việc sẽ có sổ task riêng khi bắt đầu.
- **Đã chốt (09/11/2026):** đơn sàn trả hàng thì báo cáo trừ doanh thu bằng SL trả × đơn giá snapshot (`ReportService.ReturnRevenueAmount`), phiếu vẫn để refund = 0.
- **Ghi chú cho P3:** breakdown theo kênh, chi nhánh, ngày có `revenue` và `costOfGoodsSold` là số gộp, còn `grossProfit` là số thuần. Giao diện nên hiện thêm `netRevenue` và `netCogs`. Staff nhận `null` cho các trường giá vốn và lãi gộp.
- **Bước tiếp theo:** chờ bàn giao P3; người dùng quyết định commit P1 + P2.
