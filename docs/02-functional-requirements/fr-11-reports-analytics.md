# FR-11: Báo Cáo Doanh Thu, Lợi Nhuận Gộp & Cảnh Báo Tồn Kho (Reports & Alerts)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-11` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-REP-01..03`, `NFR-PERF-01` (Trang 3, 6) |
| **Gói công việc (Task Package)** | Task Package 5 — Reporting, AI Integration, Testing & Deployment |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả chi tiết nhóm yêu cầu báo cáo tài chính, báo cáo giá trị tồn kho, tốc độ luân chuyển và cảnh báo tồn tối thiểu. | `INIT-FR-11` |

---

## 1. Bảng Đặc Tả Chi Tiết Yêu Cầu (`FR-REP`)

| Mã yêu cầu | Tên yêu cầu | Mô tả nghiệp vụ & Quy tắc kỹ thuật | Tiêu chí nghiệm thu (Acceptance Criteria) |
| :---: | :--- | :--- | :--- |
| **`FR-REP-01`** | **Revenue & Gross Profit Report** | Tổng hợp Doanh thu thuần (`Net Revenue`), Tổng giá vốn hàng bán (`Total COGS` tính từ `OrderItem.CostPrice`), và Lợi nhuận gộp (`Gross Profit = Revenue - COGS`). Hỗ trợ bộ lọc theo Khoảng thời gian (`Timeframe`), Chi nhánh (`Branch`), Kênh bán (`Channel`) và `SKU`. | - Số liệu tính toán chính xác dựa trên giá vốn đã chụp (`CostPrice`) của các đơn đã hoàn tất.<br/>- Thời gian phản hồi báo cáo `< 2 giây` với tập dữ liệu dưới `100,000` bản ghi (`NFR-PERF-01`). |
| **`FR-REP-02`** | **Inventory & Velocity Report** | Theo dõi tổng giá trị tồn kho hiện tại dựa trên giá vốn bình quân (`OnHand * UnitCost`), thống kê danh sách mặt hàng bán chạy nhất (`Best-sellers`) và hàng luân chuyển chậm (`Slow-moving items`). | - Hiển thị trực quan trên Admin Dashboard cho `Owner`. |
| **`FR-REP-03`** | **Stock Alert** (Cảnh báo tồn kho tối thiểu) | Tự động hiển thị thông báo cảnh báo trên Dashboard khi số lượng tồn khả dụng giảm xuống chạm hoặc thấp hơn ngưỡng đặt hàng tối thiểu của SKU (`available <= Threshold / MinStockLevel`). | - Danh sách cảnh báo cập nhật tự động ngay khi tồn khả dụng giảm xuống dưới ngưỡng `MinStockLevel`. |
