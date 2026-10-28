# S3-BE2-T04 — Báo cáo đúng dữ liệu và filter

**Trạng thái:** PLANNED. **Ưu tiên:** P1. **Finding:** BE2-F04, BE2-F05, BE2-F10. **Phụ thuộc:** T03 VERIFIED.

## Phạm vi / hợp đồng phải chốt

Đối chiếu FR-REP-01..03 và dữ liệu BE1. Ghi rõ trạng thái tính doanh thu, timestamp ghi nhận, múi giờ/ngày, biên range, SKU filter, missing CostPrice, last-sale/never-sold và threshold=0. Nguồn hiện lưu yêu cầu đơn **Completed**; không mặc định mở rộng sang Confirmed chỉ để giữ test cũ pass. Đề xuất dùng CompletedAt cho thời điểm hoàn tất; nếu chọn khác phải giải thích bằng contract được chốt và test boundary.

Truy vấn chỉ đọc, không sửa lại CostPrice lịch sử, trạng thái đơn hay tồn kho. Không thêm AI, forecast hoặc dashboard ngoài phạm vi.

## Ghi nhận để fix sau — 27/10/2026

Theo yêu cầu lưu lỗi để xử lý sau, API `GET /api/reports/revenue-profit` đã có implementation nhưng chưa được nghiệm thu hoàn chỉnh. Đối chiếu source tại baseline `bfc83c0`; lượt ghi nhận này không sửa code, không chạy lại HTTP probe/test và không thay đổi thứ tự T01 → T06. Dùng lại finding ID gốc từ [review ban đầu](../reviews/initial-review.md), không tạo finding trùng.

| Finding / task xử lý | Hiện trạng và ảnh hưởng | Điều kiện đóng |
| --- | --- | --- |
| **BE2-F03 / T03 / P1** | `ReportsController.cs:14,28` cho phép Owner,Staff ở class và action revenue chưa thu hẹp quyền; Staff có thể nhận doanh thu, COGS, lợi nhuận. | Giới hạn action revenue về Owner theo contract; HTTP Owner 200, Staff/Cashier 403, anonymous 401. Không thu hẹp nhầm quyền inventory-velocity/stock-alerts. Xử lý tại [T03](../task-03-access-isolation/brief.md). |
| **BE2-F04 / T04 / P1** | `ReportService.cs:36` lấy cả Confirmed và Completed, làm ghi nhận doanh thu từ đơn chưa hoàn tất. | Chỉ tính Completed theo FR hiện hành; hai Completed tổng 30 và một Confirmed 100 cho revenue=30, orders=2. Các trạng thái chưa hoàn tất/hủy không được cộng. |
| **BE2-F04 / T04 / P1 — filter SKU** | `ReportDtos.cs:3–10` chưa có SKU filter; service hiện cộng `order.TotalAmount`, chưa có phép tính theo các dòng SKU được chọn. | Có contract/filter SKU; đơn nhiều SKU chỉ cộng revenue/COGS của các dòng khớp filter; totals và breakdown nhất quán, có PostgreSQL test. |

**Quyết định cần chốt khi triển khai:** hiện filter và daily breakdown dùng CreatedAt; T04 phải chốt timestamp ghi nhận (đề xuất CompletedAt), múi giờ và biên ngày, cùng quy tắc CostPrice thiếu. Đây là phần hợp đồng cần thống nhất/kiểm thử, không tự ghi thêm một finding thời gian độc lập khi chưa chốt contract. Giữ giá vốn lịch sử; không dùng WAC hiện tại để sửa kết quả đơn cũ.

**Trạng thái:** OPEN / PLANNED, chờ triển khai T03–T04. Chưa sửa trong lượt ghi nhận này.

## Acceptance criteria

| AC | Điều kiện đạt / bằng chứng |
| --- | --- |
| AC-01 | Revenue/COGS/profit từ đơn hoàn tất và CostPrice snapshot. WAC hiện tại thay đổi không làm đổi COGS lịch sử; missing CostPrice có quy tắc rõ, không âm thầm coi giá vốn thiếu là 0 hợp lệ. |
| AC-02 | Hỗ trợ branch/channel/time/**SKU**; khi lọc một SKU chỉ tính doanh thu/COGS các dòng phù hợp, không cộng toàn TotalAmount của đơn nhiều SKU. Tổng và breakdown khớp cùng filter/grain. |
| AC-03 | Test CreatedAt khác CompletedAt, biên ngày/múi giờ, ngày ngược, range rỗng, Draft/Reserved/Confirmed/Cancelled và Completed; semantics được ghi trong API docs. |
| AC-04 | Velocity không lọc branch: một dòng/SKU, tồn bằng tổng branch balances phù hợp; branch cụ thể: đúng branch. Sales filter Channel có hiệu lực. Không cộng đúp warehouse + branch balance. |
| AC-05 | DaysWithoutSale từ last-sale thật và clock xác định; SKU chưa bán, không có balance, nhiều branch và cùng quantity có quy tắc/ranking ổn định; không gán hằng 0/30. |
| AC-06 | Stock alerts đúng `available <= threshold`; có case bằng ngưỡng, vượt ngưỡng, reserved ảnh hưởng available, threshold=0 và cross-tenant. Nếu 0 là disabled, cần contract rõ và test chứng minh. |
| AC-07 | Ưu tiên aggregate/projection SQL thay vì load tất cả entities/items. Benchmark PostgreSQL dataset dưới 100.000 bản ghi, ghi số order/items, phân bố, index, môi trường, warmup và thời gian end-to-end; đạt FR-REP-01 `<2s` theo điều kiện công bố. |

Probe đối chứng tối thiểu: hai branch cùng SKU tồn 3 và 7 phải cho OnHand tổng 10; Shopee bán 1 và POS bán 2, lọc Shopee chỉ cho QuantitySold=1. Hai Completed tổng 30 và Confirmed 100 phải cho revenue=30 theo FR hiện hành.

## Bàn giao

`handover.md` có contract đã chốt, AC→testcase→result, dữ liệu kỳ vọng tính độc lập và benchmark. Test trên provider PostgreSQL thực để kiểm tra LINQ/SQL translation; không chỉ đổi expected theo implementation.
