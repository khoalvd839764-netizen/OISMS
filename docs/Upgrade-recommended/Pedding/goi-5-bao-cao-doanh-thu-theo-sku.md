# Gói 5 — Lọc báo cáo doanh thu theo SKU

## Hiện trạng

`FR-REP-01` đã có doanh thu, COGS, lãi gộp và bộ lọc thời gian, chi nhánh, kênh. `ReportFilterDto` và màn Báo cáo chưa có lọc theo SKU. Báo cáo tính đơn `Confirmed` và `Completed` theo ngày tạo đơn, rồi khấu trừ hàng trả theo ngày xác nhận trả. [Đặc tả báo cáo](../../02-functional-requirements/fr-11-reports-analytics.md) · [Bộ lọc hiện tại](../../../src/backend/src/OISM.Application/Features/Reports/Dtos/ReportDtos.cs#L3) · [Màn Báo cáo](../../../src/frontend/admin-dashboard/src/pages/ReportsPage.tsx)

## Việc cần làm

- [ ] Thêm bộ lọc SKU tùy chọn cho báo cáo doanh thu ở API và Admin. Kết hợp với ngày, chi nhánh và kênh hiện có. Khi bỏ chọn SKU, trả kết quả tổng hợp hiện hành.
- [ ] Áp điều kiện SKU ở cấp dòng bán và dòng trả trước khi tính doanh thu thuần, COGS và lãi gộp. Dùng giá vốn chụp ở giao dịch và giữ quy tắc ngày nghiệp vụ UTC+7. Chốt cách phân bổ nếu một khoản điều chỉnh chỉ có ở cấp phiếu.
- [ ] Dùng cùng tập dữ liệu đã lọc cho KPI, biểu đồ và bảng chi tiết trên báo cáo. Giữ quy tắc phân quyền và che giá vốn/lãi gộp theo API hiện tại.

## Bằng chứng nghiệm thu

- [ ] Đơn chứa SKU A và B, có phiếu trả riêng cho A. Chọn A chỉ cộng dòng A và khấu trừ đúng phần trả A. Chọn B không bị ảnh hưởng bởi phiếu trả A. Kiểm thử cả trường hợp trả vào ngày khác ngày bán.
- [ ] Có ca lọc kết hợp SKU, thời gian, chi nhánh và kênh, SKU không có giao dịch, và SKU ngoài tenant. Kiểm tra bỏ lọc cho kết quả như trước.
- [ ] Đối chiếu UI với API và test phân quyền. Đo lại độ trễ báo cáo có và không có bộ lọc SKU theo `NFR-PERF-01`. [Dịch vụ báo cáo](../../../src/backend/src/OISM.Application/Features/Reports/Services/ReportService.cs) · [Kế hoạch đo](./goi-5-kiem-chung-nfr.md)
