# Báo cáo triển khai — OISMS-66-FIN-A: Lọc báo cáo doanh thu theo nhiều SKU

- **Ngày:** 2026-11-21
- **Nhánh / Commit nền:** `feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2` @ `879a1ef`
- **Phạm vi:** Backend `GET /api/reports/revenue-profit` (không sửa Admin UI — thuộc `FIN-B`).

## 1. Đã làm

- `ReportFilterDto`: thêm `List<Guid>? SkuIds`.
- `RevenueProfitReportDto`: thêm `List<SkuRevenueBreakdownDto>? SkuBreakdown` và `SkuSoldUnitQuantityDto` (`UnitName`, `Quantity`, `BaseQuantity`).
- `RevenueProfitCalculationHelper`: thêm tham số `skuIds` vào `QueryRevenueOrdersAsync` (`o.Items.Any(i => skuIds.Contains(i.SkuId))`) và `QueryConfirmedReturnsAsync` (`r.Items.Any(i => skuIds.Contains(i.SkuId))`) để lọc trực tiếp trên PostgreSQL.
- `ReportService.GetRevenueProfitReportAsync`:
  - Loại trùng `SkuIds` (bỏ `Guid.Empty`); nếu > 20 phần tử ném `OismException("VALIDATION_ERROR", ..., 400)`.
  - Khi có `SkuIds`: tính ở cấp dòng bằng `AllocateOrderRevenueByItem`, `AllocateReturnRevenueByItem`, `CalculateOrderItemCogs`, `CalculateReturnItemCogs`, ngày kinh doanh UTC+07 (`GetOrderBusinessDate`, `GetReturnBusinessDate`), đếm đơn khác nhau (`OrderCount`), gom số lượng theo từng đơn vị (`QuantityByUnit`), và che toàn bộ trường COGS/lãi gộp (kể cả trong `SkuBreakdown`) khi `!isOwner`.
- `ReportsController.GetRevenueProfit`: gắn `[ApiErrorContract(ApiErrorProfile.Reports)]` để trả `ProblemDetails` với `errorCode = "VALIDATION_ERROR"` khi HTTP 400.

## 2. Ảnh hưởng chức năng hiện có & DB

- **Chức năng cũ:** Khi không truyền `skuIds` (hoặc rỗng), `ReportService` đi nguyên nhánh cũ và `SkuBreakdown = null`, giữ nguyên payload và kết quả.
- **Thay đổi DB:** Không có thay đổi schema hay migration.

## 3. Kiểm chứng

- Unit tests (`OISM.UnitTests`): 431/431 PASS (gồm 14 test cũ và 3 test mới trong `ReportServiceTests`).
- Integration tests PostgreSQL (`OISM.IntegrationTests` bộ `Report|DailySalesSummary|CostVisibility`): xem chi tiết tại `docs/06-testing-and-qa/evidence/FR-REP-01/2026-11-21-FIN-A.md`.

## 4. Giới hạn

- Giao diện Admin bộ chọn SKU ở tab P&L thực hiện ở task `FIN-B`.
- Nếu SKU hợp lệ không có giao dịch nào trong kỳ lọc, KPI bằng `0` và `skuBreakdown` là mảng rỗng `[]`.

---

## 5. Phần Admin UI (`OISMS-66-FIN-B`, 21/11/2026)

- **Đã làm:**
  - `types/reports.ts`: thêm `ReportFilter.skuIds`, `SkuSoldUnitQuantity`, `SkuRevenueBreakdown`, `RevenueProfitReport.skuBreakdown`.
  - `services/reportService.ts`: truyền `skuIds` dạng tham số lặp (`URLSearchParams`) khi gọi `GET /api/reports/revenue-profit`.
  - `components/reports/SkuMultiSelect.tsx`: ô chọn nhiều SKU dùng dữ liệu từ `apiAdapter.getSkus()`, tìm kiếm theo mã/tên, chip đã chọn, nút bỏ chọn, chặn quá 20 SKU kèm thông báo.
  - `pages/ReportsPage.tsx`: đồng bộ `?skus=` trên URL (F5 giữ lựa chọn), tăng `activeReqRef` chống race, hiển thị bảng `skuBreakdown` (số lượng theo từng đơn vị), ẩn khối đối soát `daily_sales_summaries` khi đang lọc SKU, và in danh sách SKU đang lọc ở tiêu đề bản in.
- **Kiểm chứng:**
  - `npm run build --prefix src/frontend/admin-dashboard` → Thành công (`0` lỗi).
  - Đối chiếu UI/API trên Admin local (`http://localhost:5173`): lọc 1 SKU (`1.495.000 đ` DT thuần, `600.000 đ` COGS, `895.000 đ` lãi gộp, `3` đơn) và lọc 2/2 SKU hiện có của Shop A (`1.774.000 đ` DT thuần, `680.000 đ` COGS, `1.094.000 đ` lãi gộp, `3` đơn) khớp 100% với JSON API `GET /api/reports/revenue-profit`.
  - Bằng chứng & ảnh: [`docs/06-testing-and-qa/evidence/FR-REP-01/2026-11-21-FIN-B.md`](../06-testing-and-qa/evidence/FR-REP-01/2026-11-21-FIN-B.md).

---

## 6. Tối ưu tải dữ liệu (`OISMS-66-FIN-J`, 21/11/2026)

- **Đã làm:**
  - `RevenueProfitCalculationHelper.cs`: thay `Include(o => o.Items)` / `Include(o => o.Branch)` và `Include(r => r.Items).ThenInclude(i => i.OrderItem)` bằng các projection nhẹ (`RevenueOrderRow`, `RevenueOrderItemRow`, `RevenueReturnRow`, `RevenueReturnItemRow`) chỉ chọn các cột thực sự dùng. Tách truy vấn dòng hàng (`OrderBy(OrderId, Id)`) để tránh nhân bản cột cha và giữ nguyên thứ tự phân bổ phần dư làm tròn; tách truy vấn tên chi nhánh (`QueryBranchNamesAsync`) theo tập `BranchId` phân biệt.
  - `ReportService.cs` và `DailySalesSummaryService.cs`: dùng các record nhẹ và overload `Allocate*`/`Calculate*` tương ứng, giữ nguyên công thức, múi giờ UTC+07, lọc nửa mở và che giá vốn với Staff.
  - `test-oisms/nfr/measure-latency.mjs`: bổ sung ca đo `GET /api/reports/revenue-profit (30 days)` theo định dạng UTC+07 của `ReportsPage`.
  - `PostgresReportSkuFilterIntegrationTests.cs`: thêm `RevenueProfit_MatchesHandCalculatedSnapshot_AcrossBranchesDiscountAndReturn_FinJ_AC02` đối chiếu số liệu tính tay (chiết khấu đơn, trả hàng khác ngày, 2 chi nhánh, có và không lọc `skuIds`).
- **Kiểm chứng:**
  - So sánh JSON trước/sau trên `oisms_perf` (4 ca): `0` khác biệt ([`fin-j-response-diff.txt`](../06-testing-and-qa/evidence/goi-5/NFR-PERF-01/fin-j-response-diff.txt)).
  - Độ trễ p95 giảm mạnh ở 7/8 ca (không lọc c=1: `2876.27 -> 420.02 ms`, c=10: `3537.79 -> 863.20 ms`; 30 ngày c=1: `1972.49 -> 403.86 ms`, c=10: `4203.08 -> 799.72 ms`; 1 SKU c=1: `332.24 -> 65.96 ms`, c=10: `280.62 -> 132.49 ms`; 20 SKU c=1: `1888.87 -> 367.56 ms`). Riêng 20 SKU ở `c=10` đạt `5282.51 ms` (**CHƯA ĐẠT** `< 2000 ms`). Chi tiết tại [`2026-11-21-FIN-J.md`](../06-testing-and-qa/evidence/goi-5/NFR-PERF-01/2026-11-21-FIN-J.md).

> **Review 21/11/2026:** ca 20 SKU c=10 CHƯA ĐẠT là do container PostgreSQL thiếu `/dev/shm` khi chạy nhiều truy vấn song song (EF retry), không do code. Tắt song song để kiểm chứng thì cả 8 ca đều dưới 1,1 s. [Chi tiết](../06-testing-and-qa/evidence/goi-5/NFR-PERF-01/2026-11-21-FIN-J-review.md).
