# Gói 3 — Vòng đời đơn và job tổng hợp báo cáo

## Vòng đời đơn và tính nguyên tử

`FR-ORD-02` mô tả `Draft → Reserved → Confirmed → Completed`. Code tạo đơn Admin và webhook trực tiếp ở `Reserved`. `NFR-SEC-02` yêu cầu giao dịch nguyên tử, trong khi đơn online hiện giữ chỗ và xác nhận xuất ở hai giao dịch riêng. [Đặc tả vòng đời](../../02-functional-requirements/fr-06-omnichannel-orders.md) · [Code nhận đơn](../../../src/backend/src/OISM.Application/Features/Orders/Services/OrderIntakeService.cs#L651) · [Đặc tả ACID](../../03-non-functional-requirements/03-reliability-and-acid.md)

- [ ] Chốt kênh nào cần lưu `Draft` và cách chuyển từ nháp sang giữ chỗ. Ghi rõ bước nào phải nằm trong cùng giao dịch đối với đơn online và POS.
- [ ] Nếu thêm nháp, kiểm tra nháp chưa giữ tồn, chuyển sang `Reserved` chỉ giữ một lần, hết tồn trả lỗi và hủy/hết hạn giải phóng đúng số lượng. Kiểm thử cạnh tranh và rollback quanh bước giữ chỗ, xác nhận.

## Tổng hợp báo cáo hằng ngày

`FR-SIM-03` đã có job hết hạn giữ chỗ và sinh kỳ kiểm kê. Phần còn mở là job Hangfire tổng hợp doanh thu, giá vốn và lãi gộp theo ngày cho từng tenant. Schema có sẵn `daily_sales_summaries` với khóa tenant, ngày nghiệp vụ, chi nhánh, kênh và SKU. [Đặc tả Hangfire](../../02-functional-requirements/fr-10-background-hangfire.md) · [Schema bảng tổng hợp](../../database/oisms_schema.sql#L901)

- [ ] Đăng ký job theo cấu hình Hangfire hiện có. Chốt giờ chạy, phạm vi tính lại khi đơn xác nhận muộn hoặc hàng trả phát sinh và cách chạy lại ngày lỗi.
- [ ] Dùng cùng quy tắc ngày, giá vốn và hàng trả với báo cáo trực tiếp. Chạy lại cùng tenant/ngày phải cho cùng số liệu, kể cả khi retry hoặc hai lượt chạy chồng nhau. [Quy tắc báo cáo](../../02-functional-requirements/fr-11-reports-analytics.md) · [Lọc báo cáo theo SKU](./goi-5-bao-cao-doanh-thu-theo-sku.md)

## Bằng chứng nghiệm thu

- [ ] Có test chuyển trạng thái, lỗi giữa giao dịch và đối soát sổ kho theo quyết định đã chốt.
- [ ] Có kết quả chạy job và test đối soát với báo cáo trực tiếp. Dữ liệu thử gồm nhiều tenant, đơn xác nhận muộn, hàng trả khác ngày và chạy lại cùng ngày.
