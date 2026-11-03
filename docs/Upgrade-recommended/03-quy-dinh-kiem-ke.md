# 03. Quy định kiểm kê giữa tháng, cuối tháng và kiểm kê đột xuất

> Trạng thái: đề xuất phân tích; ngày/giờ cụ thể và quyền duyệt cần được chốt theo vận hành thực tế.

## Vấn đề cần giải quyết

Hệ thống đã có thao tác tạo và chốt đợt kiểm kê, nhưng chưa định nghĩa **khi nào phải kiểm kê**. Nếu thời điểm chụp tồn, thời gian đếm và kỳ đối chiếu không rõ, hai người có thể kiểm kê cùng kho ở thời điểm khác nhau và hiểu sai chênh lệch.

## Hiện trạng đã kiểm tra

- `CreateStockCountRequestDto` có chi nhánh, kho, danh sách SKU và ghi chú; chưa có loại kỳ, ngày dự kiến hoặc thời điểm chốt. Xem [StockCountDtos.cs](../../src/backend/src/OISM.Application/Features/StockCounts/Dtos/StockCountDtos.cs).
- `StockCount` có trạng thái `Draft`, `Counting`, `Confirmed`, `Cancelled`, người tạo và người xác nhận; chưa có lịch/phiên duyệt riêng. Xem [StockCount.cs](../../src/backend/src/OISM.Domain/Entities/StockCount.cs).
- Dịch vụ hiện chụp số dư và sequence, chặn chốt khi có giao dịch xen giữa làm lệch sequence; đồng thời đối chiếu lượng giữ chỗ. Xem [StockCountService.cs](../../src/backend/src/OISM.Application/Features/StockCounts/Services/StockCountService.cs).

## Tham khảo Odoo và KiotViet

| Hệ thống | Cách xử lý được tài liệu chính thức mô tả | Điều cần rút ra |
| :--- | :--- | :--- |
| Odoo | Đếm chu kỳ theo vị trí kho với tần suất cấu hình bằng **số ngày**; lịch kiểm kê toàn bộ mặc định là 31/12 và có thể đổi. Có thể giao người đếm và ngày đếm dự kiến; khi tồn thay đổi từ lúc đếm đến lúc áp dụng điều chỉnh, hệ thống yêu cầu xác nhận thêm. | Lịch 15 và cuối tháng theo **ngày lịch** là yêu cầu riêng; không biểu diễn đúng bằng lặp mỗi 15 ngày. Cần xử lý giao dịch phát sinh trong lúc đếm. |
| KiotViet | Người dùng tạo phiếu kiểm kho, nhập số thực tế, lưu tạm hoặc hoàn thành để cân bằng tồn. Tài liệu khuyên kiểm định kỳ, nhưng phần hướng dẫn này không mô tả lịch tự tạo cố định ngày 15/cuối tháng. | Nên có phiếu và quy trình chốt như KiotViet, đồng thời thêm bộ lập lịch để đáp ứng chính sách của OISMS. |

Nguồn: [Odoo — Cycle counts](https://www.odoo.com/documentation/19.0/applications/inventory_and_mrp/inventory/warehouses_storage/inventory_management/cycle_counts.html), [Odoo — Inventory adjustments](https://www.odoo.com/documentation/19.0/applications/inventory_and_mrp/inventory/warehouses_storage/inventory_management/count_products.html), [KiotViet — Kiểm kho](https://www.kiotviet.vn/huong-dan-su-dung-kiotviet/retail-hang-hoa/kiem-kho/). Đối chiếu tài liệu ngày 29/10/2026.

**Đề xuất cho OISMS:** cấu hình hai mốc hàng tháng theo từng kho: `Giữa tháng` ngày 15 và `Cuối tháng` ngày cuối cùng của tháng, theo múi giờ cửa hàng. Tạo kế hoạch và nhắc người phụ trách; chỉ người được phân quyền mới duyệt chênh lệch và chốt. Kiểm đột xuất vẫn được tạo thủ công với lý do. Đây là chính sách đề xuất, không phải mặc định của Odoo hay KiotViet.

## Chính sách lịch đề xuất

| Loại | Gợi ý ban đầu | Phạm vi | Mục đích |
| :--- | :--- | :--- | :--- |
| Giữa tháng | Ngày 15 hàng tháng, giờ do cửa hàng cấu hình | Toàn kho hoặc nhóm SKU trọng yếu | Phát hiện sai lệch sớm |
| Cuối tháng | Ngày cuối cùng của tháng, giờ do cửa hàng cấu hình | Toàn kho | Chốt số liệu kỳ |
| Đột xuất | Theo quyết định của người có quyền, bắt buộc lý do | Kho/SKU chỉ định | Điều tra thất thoát hoặc thay đổi vận hành |

Ngày 15 và ngày cuối tháng chỉ là **giá trị mặc định đề xuất**, không phải quy định đã được GVHD phê duyệt. Lịch cần lưu theo múi giờ của tenant, hỗ trợ tháng 28/29/30/31 ngày, ngày nghỉ và đổi lịch có lưu vết.

## Vòng đời đề xuất

1. **Lập lịch**: hệ thống tạo kế hoạch cho từng kho và kỳ; có người phụ trách, phạm vi SKU, thời gian dự kiến và hạn hoàn tất.
2. **Bắt đầu đếm**: chụp tồn sổ sách, thời điểm và sequence của từng SKU. Hướng dẫn người đếm ghi số thực tế, không tự điều chỉnh tồn.
3. **Nộp kết quả**: khóa số lượng đếm, tính chênh lệch và yêu cầu lý do cho sai lệch vượt ngưỡng.
4. **Duyệt chênh lệch**: người có quyền kiểm tra, có thể yêu cầu đếm lại. Người duyệt nên khác người đếm đối với sai lệch đáng kể.
5. **Chốt**: backend kiểm tra lại sequence và lượng giữ chỗ, rồi ghi chứng từ điều chỉnh và sổ kho trong một giao dịch.
6. **Lưu hồ sơ**: trạng thái hoàn tất/đến hạn/quá hạn, kết quả, người đếm và người duyệt đều truy được.

Đề xuất thêm trạng thái `PendingApproval` hoặc thực thể phê duyệt riêng. Không nên tự động ghi chênh lệch vào kho chỉ vì đã nhập số đếm.

## Giao dịch phát sinh trong lúc đếm

Hai chính sách khả thi:

- **Đóng băng có giới hạn**: tạm ngưng giao dịch cho kho/SKU đang đếm trong khoảng ngắn; dễ đối chiếu nhưng ảnh hưởng bán hàng.
- **Vẫn giao dịch, kiểm tra drift**: tận dụng sequence hiện có; khi phát sinh giao dịch sau snapshot phải tính lại hoặc đếm lại SKU bị ảnh hưởng trước khi chốt.

Đề xuất bắt đầu bằng chính sách thứ hai vì backend đã có kiểm tra drift. Giao diện phải chỉ rõ SKU nào cần làm lại và không cho bỏ qua lỗi để chốt. Kỳ cuối tháng có thể cần cửa sổ chốt riêng nếu vận hành yêu cầu số liệu tại đúng mốc thời gian.

## Dữ liệu và API dự kiến

| Thành phần | Trường quan trọng |
| :--- | :--- |
| `stock_count_schedules` | Tenant, kho, loại kỳ, ngày/giờ theo múi giờ, phạm vi SKU, người phụ trách, trạng thái hiệu lực |
| `stock_counts` | Loại kỳ, schedule ID, thời điểm chốt snapshot, hạn hoàn tất, trạng thái duyệt, lý do đột xuất |
| `stock_count_approvals` hoặc audit log | Người nộp, người duyệt, thời điểm, quyết định, lý do, chênh lệch trước/sau duyệt |

Cần API cấu hình lịch, xem kỳ sắp tới/quá hạn, nộp kết quả, yêu cầu đếm lại và duyệt. Tác vụ tạo kỳ tự động phải idempotent để chạy lại không sinh hai đợt cho cùng kho và kỳ.

## Tiêu chí nghiệm thu đề xuất

1. Mỗi kho có lịch giữa tháng/cuối tháng rõ ngày giờ và múi giờ; tháng ngắn vẫn tạo đúng kỳ cuối tháng.
2. Chạy lại tác vụ lập lịch không tạo đợt trùng.
3. Đợt đột xuất có người yêu cầu và lý do.
4. Chênh lệch cần duyệt không ghi tồn trước khi được duyệt; giao dịch xen giữa bị phát hiện và yêu cầu xử lý.
5. Báo cáo thể hiện kỳ đã hoàn thành, quá hạn, người đếm, người duyệt và chênh lệch.

## Quyết định đã chốt

Người dùng đồng ý ngày 03/11/2026 để triển khai bản đầu. Đây là quyết định của nhóm dự án, chưa phải quy định được GVHD phê duyệt.

| Nội dung | Quyết định |
| :--- | :--- |
| Phạm vi lịch | Cấu hình theo từng kho, có thể tắt. Lịch định kỳ kiểm toàn kho; kiểm theo nhóm SKU dùng phiếu đột xuất |
| Múi giờ | Cố định Asia/Saigon (UTC+7), khớp quy tắc báo cáo; chưa có múi giờ theo tenant |
| Kế hoạch và phiếu | Tách riêng. Job chỉ tạo **kỳ**; phiếu và ảnh chụp số dư chỉ sinh khi người đếm bấm "Bắt đầu đếm", tránh lệch sequence trước ngày đếm. `created_by` của phiếu là người bấm |
| Luồng trạng thái | `Draft → Counting → PendingApproval → Confirmed`; duyệt có thể trả về `Counting` để đếm lại; được hủy từ `Draft`, `Counting`, `PendingApproval` |
| Quyền | Owner, Staff đếm và nộp. **Chỉ Owner duyệt và chốt**; Staff không còn tự chốt được |
| Người duyệt | Không được trùng người đếm. Tenant chỉ có một Owner đang hoạt động thì được tự duyệt nhưng bắt buộc ghi lý do |
| Ngưỡng duyệt | Bản đầu chưa có ngưỡng: mọi phiếu đều qua duyệt, kể cả phiếu không có chênh lệch |
| Giao dịch trong lúc đếm | Vẫn cho giao dịch. Lệch sequence phải báo đủ danh sách SKU và đếm lại đúng các SKU đó; không có cách bỏ qua để chốt |
| Đơn giữ chỗ, đơn chưa hoàn tất | Giữ quy tắc hiện có: số đếm không được thấp hơn lượng đang giữ chỗ |

## Quyết định còn mở

- Ngưỡng chênh lệch cần duyệt và trường hợp cho tự chốt phiếu không có chênh lệch.
- Cửa sổ chốt cuối tháng, xử lý ngày nghỉ.
- Lưu vết chi tiết khi đổi lịch: bản đầu chỉ lưu người sửa và thời điểm sửa gần nhất.
- Nhắc người phụ trách bằng thông báo đẩy: bản đầu chỉ có API xem kỳ sắp tới và quá hạn.
