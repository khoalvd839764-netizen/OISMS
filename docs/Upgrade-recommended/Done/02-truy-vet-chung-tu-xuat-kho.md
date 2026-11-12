# 02. Truy vết chứng từ xuất kho

> Trạng thái ngày 12/11/2026. Backend có truy vết chứng từ xuất và hóa đơn bán. Admin có màn hình xem, truy vết và in lại hóa đơn, POS hiển thị số hóa đơn trên biên nhận. Yêu cầu "xuất kèm hóa đơn" được xác nhận là **hóa đơn khi khách mua hàng**. Các mục ghi "đề xuất của nhóm" chưa có GVHD xác nhận. [Báo cáo triển khai](../../report-after-upgrade/hoa-don-truy-vet-xuat-kho.md) · [Nghiệm thu giao diện](../../report-after-upgrade/frontend-coming-soon-va-upgrade.md)

## Vấn đề cần giải quyết

Mỗi lần xuất kho phải kèm chứng từ đúng với loại xuất để đối chiếu được: xuất bán kèm hóa đơn của khách, chuyển kho kèm phiếu chuyển kho, kiểm kê thiếu kèm phiếu kiểm kê và phiếu điều chỉnh. Không dùng một loại hóa đơn cho mọi dòng `OUT`, và không tạo hóa đơn bán giả cho chuyển kho hay kiểm kê.

## Quyết định đã chốt

| Câu hỏi | Quyết định | Nguồn |
| :--- | :--- | :--- |
| "Hóa đơn mua hàng" khi xuất là gì? | Hóa đơn khi khách mua hàng, trong hệ thống là `sales_invoices`. Không phải hóa đơn của nhà cung cấp, không truy về lô nhập. | Người dùng, 03/11/2026 |
| "Hóa đơn chuyển kho" là gì? | Chính là phiếu chuyển kho `stock_transfers`. Không thêm bảng mới. | Người dùng, 03/11/2026 |
| Loại hóa đơn | Hóa đơn bán nội bộ, không phải hóa đơn điện tử theo quy trình phát hành riêng. | Đề xuất của nhóm |
| Thời điểm lập | Cùng giao dịch với dòng `OUT`: POS khi checkout, đơn online khi Confirm. | Đề xuất của nhóm |
| Đơn sàn TMĐT | Vẫn có hóa đơn nội bộ để mọi dòng xuất bán truy vết cùng một cách; đây không phải hóa đơn của sàn. | Đề xuất của nhóm |
| Đơn đã có trước nâng cấp | Không tạo bù hóa đơn. Vẫn truy vết được qua đơn hàng; API trả hóa đơn `null`. | Đề xuất của nhóm |
| Quyền xem giá vốn | Owner và Staff xem giá vốn từng dòng trong truy vết. Cashier chỉ in hóa đơn, không thấy giá vốn. Lãi gộp vẫn chỉ Owner xem qua báo cáo. | Đề xuất của nhóm |
| Cashier in lại hóa đơn | Được, trong phạm vi tenant. Hiện chưa có phân công chi nhánh cho người dùng; giới hạn theo chi nhánh để lại cho CS-02. | Đề xuất của nhóm |

## Hiện trạng trước khi nâng cấp (kiểm tra 03/11/2026)

| Nội dung | Hiện trạng |
| :--- | :--- |
| Một dòng sổ kho có đúng một nguồn | Đã có: `ck_inventory_transactions_one_source`, `ck_inventory_transactions_source_direction`. |
| Sổ kho trả chứng từ nguồn | Đã có: `InventoryLedgerDto` trả loại, mã, số chứng từ. Chưa có hóa đơn và người thực hiện. |
| Điểm ghi `OUT` xuất bán | Chỉ có 2 nơi: `CheckoutPosAsync` và `ConfirmOrderAsync`. `/process` đi qua Confirm. Luồng online xuất đủ một lần khi Confirm. |
| Hóa đơn bán | Chưa có thực thể. `PosReceiptDto.ReceiptNumber` đang bằng số đơn. Chưa có API in lại. |
| Giữ nguyên dữ liệu đã chụp | `order_items` đã khóa giá, số lượng, hệ số, đơn vị sau xác nhận; chưa khóa tên và mã SKU đã chụp. |
| Người xác nhận | Kiểm kê, phiếu điều chỉnh, phiếu nhập đã lưu `confirmed_by`. **Đơn hàng không lưu** (`confirmedByUserId` bị bỏ qua), **chuyển kho không lưu** người xuất. |
| Giá vốn POS | `OrderItem.CostPrice` làm tròn 4 chữ số còn dòng `OUT` ghi nguyên `AverageCost`, nên hai số có thể lệch. Luồng online dùng 6 chữ số. |

## Tham khảo Odoo và KiotViet

| Hệ thống | Cách xử lý được tài liệu chính thức mô tả | Điều rút ra |
| :--- | :--- | :--- |
| Odoo | Lập hóa đơn theo lượng đặt hoặc lượng đã giao. Với `Sales Order → Delivery Order → Invoice`, giao hàng giảm tồn trước, hóa đơn theo lượng thực giao. | Cần khi có giao từng phần. OISMS hiện xuất đủ một lần nên chưa cần. |
| KiotViet | Hoàn tất hóa đơn bán hàng làm hệ thống trừ tồn tại kho đã chọn. | Chứng từ bán và bút toán xuất có thể hình thành cùng một thao tác; OISMS theo hướng này. |

Nguồn: [Odoo — Invoicing policies](https://www.odoo.com/documentation/19.0/applications/sales/sales/invoicing/invoicing_policy.html), [Odoo — Invoicing processes](https://www.odoo.com/documentation/19.0/applications/finance/accounting/customer_invoices/overview.html), [KiotViet — Quản lý kho hàng](https://www.kiotviet.vn/huong-dan-su-dung-kiotviet/retail-thiet-lap/quan-ly-kho-hang/). Đối chiếu tài liệu ngày 29/10/2026.

## Ma trận chứng từ

| Biến động kho | Chứng từ kèm theo | Người chịu trách nhiệm | Việc của Upgrade 02 |
| :--- | :--- | :--- | :--- |
| Xuất bán POS, Admin, sàn | Dòng đơn hàng và hóa đơn của khách | `orders.confirmed_by` | Thêm hóa đơn, thêm người xác nhận |
| Xuất chuyển kho | Phiếu chuyển kho đã xuất đi; đối chiếu lần nhận ở kho đích | `stock_transfers.dispatched_by` | Thêm người xuất |
| Xuất do kiểm kê thiếu | Phiếu kiểm kê và phiếu điều chỉnh | `inventory_adjustments.confirmed_by` | Chỉ thêm vào API truy vết; bước duyệt thuộc [Upgrade 03](./03-quy-dinh-kiem-ke.md) |

Chuyển kho hiện không có bước duyệt riêng; "đã xuất đi" là trạng thái `InTransit`.

## Luồng xuất bán

1. POS checkout hoặc Confirm đơn online ghi `OUT` liên kết `OrderItemId`, chụp WAC, trừ tồn như hiện tại.
2. Trong **cùng giao dịch**, tạo đúng một `sales_invoices` cho đơn và lưu người xác nhận. Một bước thất bại thì không bước nào được ghi.
3. Hóa đơn in từ dữ liệu đã chụp trên hóa đơn và `order_items`, nên giữ nguyên dù bảng giá, tên SKU hay tên chi nhánh đổi sau đó.
4. Tra được hai chiều: hóa đơn → đơn → dòng sổ kho, và ngược lại.

Đơn đã xác nhận không hủy được theo state machine, nên hóa đơn không có trạng thái hủy. Trả hàng dùng chứng từ riêng của CS-01, nối qua `order_id` và `order_item_id`.

## Dữ liệu và API

- Bảng mới `sales_invoices`: số hóa đơn, đơn gốc, kênh, thông tin chi nhánh, khách, người lập, tổng tiền, phương thức thanh toán đã chụp, thời điểm lập. Mỗi đơn tối đa một hóa đơn, chỉ INSERT.
- Chưa tạo `sales_invoice_items`: một đơn một hóa đơn, dòng hàng dùng `order_items` đã khóa. Thêm khi có hóa đơn từng phần.
- Thêm `orders.confirmed_by`, `stock_transfers.dispatched_by`, cho phép trống với dữ liệu cũ.
- Không thay `OrderItemId` trên sổ kho bằng `InvoiceId`: dòng đơn mới là chứng từ gây biến động kho.
- API danh sách, chi tiết, in lại hóa đơn; API truy vết theo đơn, theo hóa đơn, theo dòng sổ kho. Dữ liệu in không chứa giá vốn.

## Tiêu chí nghiệm thu

1. Từ một dòng `OUT`, người có quyền truy được chứng từ nguồn hợp lệ và người xác nhận; đơn cũ hiển thị rõ là không có dữ liệu thay vì báo lỗi.
2. POS retry hoặc checkout đồng thời cùng key chỉ tạo một lần xuất và một hóa đơn; Confirm đồng thời cũng vậy.
3. Lỗi khi tạo hóa đơn làm hoàn tác toàn bộ: không đơn mới, không dòng `OUT`, tồn không đổi.
4. Chuyển kho và kiểm kê có chứng từ nguồn nhưng không phát sinh hóa đơn bán.
5. Hóa đơn in lại giữ nguyên giá, số lượng, đơn vị, tên hàng, chi nhánh, khách dù dữ liệu gốc đổi sau đó.
6. Giá vốn trên dòng đơn POS bằng đơn giá trên dòng `OUT` tương ứng.
7. Cashier in được hóa đơn nhưng không thấy giá vốn và không gọi được API truy vết; không xem được dữ liệu tenant khác.

## Ngoài phạm vi

| Nội dung | Lý do | Làm khi nào |
| :--- | :--- | :--- |
| Phiếu giao hàng, giao từng phần, trạng thái `Chờ lập hóa đơn` | Phải chuyển thời điểm trừ tồn khỏi Confirm, kéo theo giữ chỗ, WAC và báo cáo | Khi có yêu cầu giao từng phần |
| Truy về hóa đơn nhà cung cấp của đúng hàng xuất | Không phải yêu cầu; cần theo dõi lô/serial, WAC không xác định được | Không làm |
| Trả hàng cho nhà cung cấp | Chưa có luồng và chưa có cột nguồn trên sổ kho | Tính năng riêng |
| Trả hàng từ khách, hoàn tiền | Thuộc CS-01 | CS-01 |
| In phiếu giao và in báo cáo | Thuộc CS-06 | CS-06 |
| Hóa đơn điện tử | Có quy trình phát hành riêng | Không thuộc đồ án hiện tại |

## Quyết định còn mở

- GVHD có yêu cầu hạn chế gì khác với các mục "đề xuất của nhóm" hay không, nhất là thời điểm lập hóa đơn cho đơn online (hiện chọn lúc Confirm vì đó là lúc trừ tồn).
