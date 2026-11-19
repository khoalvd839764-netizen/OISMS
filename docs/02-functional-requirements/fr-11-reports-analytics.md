# FR-11: Báo Cáo Doanh Thu, Lợi Nhuận Gộp & Cảnh Báo Tồn Kho (Reports & Alerts)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-11` |
| **Phiên bản (Version)** | `v1.3.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-19` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-REP-01..05`, `NFR-PERF-01` (Trang 3, 6); `Upgrade 13 / OISMS-66` |
| **Gói công việc (Task Package)** | Task Package 5 — Reporting, AI Integration, Testing & Deployment |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả chi tiết nhóm yêu cầu báo cáo tài chính, báo cáo giá trị tồn kho, tốc độ luân chuyển và cảnh báo tồn tối thiểu. | `INIT-FR-11` |
| `v1.1.0` | `2026-11-11` | Nhóm dự án OISMS | Chốt quy tắc ghi nhận doanh thu (đóng `BE2-F04`): tính đơn `Confirmed` và `Completed`, không chỉ `Completed`. Bộ lọc theo `SKU` chưa có trong code, dời sang sprint sau. | `BE2-F04` |
| `v1.2.0` | `2026-11-15` | Nhóm dự án OISMS | Bổ sung quy tắc ngày kinh doanh UTC+07 dùng chung và vai trò bảng `daily_sales_summaries` cùng API đối soát/tính lại cho `Owner`. | `OISMS-59-G3B` |
| `v1.3.0` | `2026-11-19` | Nhóm dự án OISMS | Bổ sung Báo cáo tồn kho theo kỳ (NXT: đầu kỳ, nhập, xuất, cuối kỳ, hàng đang chuyển) và Thẻ kho S12/S10 (Upgrade 13 / OISMS-66-G2). Phân quyền giá trị cho Owner, che giá Staff, cấm Cashier, xuất Excel ClosedXML. | `OISMS-66-G2` |

---

## 1. Bảng Đặc Tả Chi Tiết Yêu Cầu (`FR-REP`)

| Mã yêu cầu | Tên yêu cầu | Mô tả nghiệp vụ & Quy tắc kỹ thuật | Tiêu chí nghiệm thu (Acceptance Criteria) |
| :---: | :--- | :--- | :--- |
| **`FR-REP-01`** | **Revenue & Gross Profit Report** | Tổng hợp Doanh thu thuần (`Net Revenue`), Tổng giá vốn hàng bán (`Total COGS` tính từ `OrderItem.CostPrice`), và Lợi nhuận gộp (`Gross Profit = Revenue - COGS`). Hỗ trợ bộ lọc theo Khoảng thời gian (`Timeframe`), Chi nhánh (`Branch`), Kênh bán (`Channel`) và `SKU` *(lọc theo `SKU` chưa có, hoàn thành ở sprint sau)*. | - Số liệu tính toán chính xác dựa trên giá vốn đã chụp (`CostPrice`) của các đơn đã xuất kho.<br/>- **Quy tắc ghi nhận doanh thu (chốt 11/11/2026):** tính các đơn ở trạng thái `Confirmed` và `Completed`. Lý do: từ `Confirmed` hàng đã trừ `OnHand`, đã ghi sổ `OUT` và đã chụp `CostPrice`, nên doanh thu khớp với giá vốn và sổ kho; POS checkout đi thẳng tới `Completed`. Đơn `Draft`, `Reserved`, `Cancelled` không tính. Lọc thời gian theo `orders.created_at` (UTC+7, nửa mở `CreatedAt < endBoundary`); hàng trả khấu trừ theo `sales_returns.confirmed_at`.<br/>- Thời gian phản hồi báo cáo `< 2 giây` với tập dữ liệu dưới `100,000` bản ghi (`NFR-PERF-01`). |
| **`FR-REP-02`** | **Inventory & Velocity Report** | Theo dõi tổng giá trị tồn kho hiện tại dựa trên giá vốn bình quân (`OnHand * UnitCost`), thống kê danh sách mặt hàng bán chạy nhất (`Best-sellers`) và hàng luân chuyển chậm (`Slow-moving items`). | - Hiển thị trực quan trên Admin Dashboard cho `Owner`. |
| **`FR-REP-03`** | **Stock Alert** (Cảnh báo tồn kho tối thiểu) | Tự động hiển thị thông báo cảnh báo trên Dashboard khi số lượng tồn khả dụng giảm xuống chạm hoặc thấp hơn ngưỡng đặt hàng tối thiểu của SKU (`available <= Threshold / MinStockLevel`). Làm rõ 10/11/2026: SKU hết hàng (`available <= 0`) luôn được cảnh báo mức `OutOfStock` kể cả khi chưa đặt ngưỡng (`ReorderPoint = 0`); SKU còn hàng chỉ cảnh báo khi đã đặt ngưỡng (`ReorderPoint > 0`). Áp dụng cho báo cáo cảnh báo tồn, số SKU tồn thấp ở Tổng quan, màn Kho và Trợ lý OISMS. | - Danh sách cảnh báo cập nhật tự động ngay khi tồn khả dụng giảm xuống dưới ngưỡng `MinStockLevel`. |
| **`FR-REP-04`** | **Inventory Period Report (Báo cáo tồn kho theo kỳ - NXT)** | Báo cáo Nhập - Xuất - Tồn theo kỳ (ngày bắt đầu / ngày kết thúc UTC+07 nửa mở, tối đa 366 ngày, không cho phép ngày tương lai). Hỗ trợ lọc theo Chi nhánh hoặc Toàn hệ thống (`branchId = null`), Danh mục, Tìm kiếm SKU. Phân trang SKU trước rồi mới tổng hợp sổ cái. Tách bạch nhóm Nhập (Mua hàng, Khách trả, Chuyển đến, Kiểm kê/Điều chỉnh) và Nhóm Xuất (Bán POS, TMĐT, Admin theo `Order.Channel`, Chuyển đi, Kiểm kê/Điều chỉnh). Khi xem toàn hệ thống: loại trừ chuyển kho nội bộ, theo dõi riêng cột Hàng đang chuyển đầu kỳ/cuối kỳ (`InTransit`). Công thức bảo toàn: $\Sigma$ Chi nhánh + Đang chuyển = Tổng hệ thống; Đầu + Nhập - Xuất = Cuối. | - Cân bằng tuyệt đối số lượng và giá trị.<br/>- Quyền truy cập: `Owner` xem đầy đủ số lượng và giá trị tồn/nhập/xuất; `Staff` chỉ xem số lượng, các trường giá trị trả về `null`; `Cashier` bị từ chối `403 Forbidden`.<br/>- Hỗ trợ xuất file Excel `.xlsx` dạng luồng với định dạng chuẩn và che cột tiền tương ứng quyền hạn.<br/>- Thời gian phản hồi trang `< 2 giây` với sổ kho quy mô lớn. |
| **`FR-REP-05`** | **Stock Card Report (Thẻ kho S12/S10)** | Thẻ kho chi tiết cho từng SKU theo Chi nhánh và Khoảng thời gian. Thống kê dòng Đầu kỳ, từng biến động sổ cái theo trình tự thời gian (`SequenceNo`), và dòng Cuối kỳ. Tích hợp liên kết chứng từ truy vết (`reference_id` / số phiếu: Đơn bán, Đơn mua, Phiếu trả, Phiếu chuyển kho, Phiếu kiểm kê). Phân định mẫu S12-DN (chỉ số lượng cho `Staff`) và mẫu S10-DN (số lượng + đơn giá/thành tiền + dòng điều chỉnh chênh lệch làm tròn WAC khi kỳ kéo dài tới hiện tại cho `Owner`). | - Hiển thị đúng số dư tồn sau từng giao dịch (`BalanceAfter`).<br/>- `Owner` có đầy đủ thông tin đơn giá và thành tiền; `Staff` các trường đơn giá/thành tiền là `null`.<br/>- Cho phép in trực tiếp theo mẫu kế toán và xuất Excel `.xlsx`. |

---

## 2. Quy Tắc Ngày Kinh Doanh UTC+07 & Vai Trò Bảng Tổng Hợp (`daily_sales_summaries`)

1. **Quy tắc ngày kinh doanh và khoảng lọc nửa mở (dùng chung cho báo cáo trực tiếp và tổng hợp ngày qua `RevenueProfitCalculationHelper`):**
   - Mọi mốc ngày lọc và gom nhóm sử dụng múi giờ kinh doanh Việt Nam **UTC+07** (`ReportTimeHelper.ToLocalDate` / `ReportTimeHelper.StartOfDayUtc`) với biên nửa mở `[StartDate, EndDate)`.
   - **Đơn bán hàng:** chỉ tính đơn có trạng thái `Confirmed` hoặc `Completed`; quy về ngày kinh doanh theo `orders.created_at` (không tính đơn `Draft`, `Reserved`, `Cancelled`). Số lượng quy đổi theo đơn vị gốc (`OrderItem.BaseQuantity = Quantity * FactorSnapshot`); giá vốn tính từ `OrderItem.CostPrice * BaseQuantity`.
   - **Hàng trả:** chỉ tính phiếu trả hàng có trạng thái `Confirmed`; quy về ngày kinh doanh theo `sales_returns.confirmed_at`. Số lượng hàng trả quy đổi đơn vị gốc (`SalesReturnItem.Quantity * OrderItem.FactorSnapshot`); tiền trả dùng `TotalRefundAmount` (POS/Admin) hoặc tổng `SL trả * đơn giá snapshot` (sàn TMĐT); giá vốn trả lại dùng `BaseQuantity * OriginalUnitCost`.
2. **Vai trò của bảng `daily_sales_summaries`:**
   - Bảng `daily_sales_summaries` lưu ảnh chụp tổng hợp sẵn theo khóa `(tenant_id, business_date, branch_id, channel, sku_id)`, trong đó `cost_of_goods_sold` là **giá vốn thuần** (`COGS bán - COGS trả`).
   - Dữ liệu được cập nhật tự động bởi Hangfire job `oisms-daily-sales-summary` (`00:30` UTC+07 mỗi ngày, quét 7 ngày từ hôm qua lùi về) hoặc theo yêu cầu qua `POST /api/reports/daily-summaries/rebuild`.
   - Khi tổng hợp một `(tenant, business_date)`, service mở transaction, giữ khóa `pg_advisory_xact_lock(tenant, date)`, xóa dữ liệu ngày cũ của tenant đó và ghi lại tập dòng mới (không ghi dòng toàn `0`).
   - Kết quả của `GET /api/reports/revenue-profit` giữ nguyên hành vi và số liệu trực tiếp không đổi; `GET /api/reports/daily-summaries` phục vụ `Owner` đối soát số tổng hợp với số liệu tính trực tiếp theo từng ngày (`Matched` / `Mismatch` / `NotSummarized`).
   - **Sai số làm tròn khi đối soát:** bảng tổng hợp lưu `numeric(20,2)` và làm tròn theo từng dòng, còn báo cáo trực tiếp cộng giá vốn WAC 6 chữ số lẻ rồi mới làm tròn. Một ngày được coi là `Matched` khi mọi chỉ số (doanh thu, hàng trả, giá vốn, lãi gộp) chênh không quá `0,01 × số dòng tổng hợp của ngày`; chênh lớn hơn là `Mismatch`.
