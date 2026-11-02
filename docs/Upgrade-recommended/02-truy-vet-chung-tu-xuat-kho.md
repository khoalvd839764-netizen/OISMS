# 02. Truy vết chứng từ xuất kho

> Trạng thái: đề xuất phân tích. Cần xác nhận rõ ý nghĩa “hóa đơn mua hàng” đối với chiều xuất.

## Vấn đề cần giải quyết

Mỗi bút toán xuất kho phải có nguồn nghiệp vụ rõ ràng để đối chiếu. Xuất bán, chuyển kho và điều chỉnh kiểm kê có chứng từ nguồn khác nhau; không thể dùng một loại hóa đơn cho mọi dòng `OUT`.

## Hiện trạng đã kiểm tra

- `InventoryTransaction` đã có các khóa nguồn như `OrderItemId`, `TransferItemId`, `StockCountItemId`, `ReceiptItemId`; đây là nền tảng tốt để truy vết. Xem [InventoryTransaction.cs](../../src/backend/src/OISM.Domain/Entities/InventoryTransaction.cs).
- Xác nhận đơn hàng ghi `OUT` và chụp WAC vào dòng đơn. Xem [OrderService.cs](../../src/backend/src/OISM.Application/Features/Orders/Services/OrderService.cs).
- POS trả `PosReceiptDto` để in hóa đơn/biên nhận từ dữ liệu đơn hàng. Mô hình dữ liệu hiện chưa có thực thể hóa đơn bán độc lập. Xem [OrderDtos.cs](../../src/backend/src/OISM.Application/Features/Orders/Dtos/OrderDtos.cs) và [AppDbContext.cs](../../src/backend/src/OISM.Infrastructure/Persistence/AppDbContext.cs).

## Tham khảo Odoo và KiotViet

| Hệ thống | Cách xử lý được tài liệu chính thức mô tả | Điều cần rút ra |
| :--- | :--- | :--- |
| Odoo | Có chính sách lập hóa đơn theo lượng đặt hoặc lượng đã giao. Với luồng `Sales Order → Delivery Order → Invoice`, giao hàng làm giảm tồn trước; hóa đơn lập theo lượng thực giao, kể cả giao từng phần. | Không thể luôn bắt hóa đơn **đã phát hành** tồn tại trước khi xuất nếu muốn hỗ trợ giao từng phần và tính tiền theo lượng giao. |
| KiotViet | Trong luồng bán lẻ được mô tả, hoàn tất hóa đơn bán hàng làm hệ thống trừ tồn tại kho đã chọn. | Với POS, chứng từ bán và bút toán xuất có thể hình thành cùng một thao tác. |

Nguồn: [Odoo — Invoicing policies](https://www.odoo.com/documentation/19.0/applications/sales/sales/invoicing/invoicing_policy.html), [Odoo — Invoicing processes](https://www.odoo.com/documentation/19.0/applications/finance/accounting/customer_invoices/overview.html), [KiotViet — Quản lý kho hàng](https://www.kiotviet.vn/huong-dan-su-dung-kiotviet/retail-thiet-lap/quan-ly-kho-hang/). Đối chiếu tài liệu ngày 29/10/2026.

**Đề xuất cho OISMS:** xuất bán phải đối chiếu với đơn bán và chứng từ bán. Tại POS, tạo biên nhận/hóa đơn bán nội bộ và dòng `OUT` trong cùng giao dịch; nếu một bước thất bại thì không chốt bước còn lại. Với đơn giao hàng, phiếu giao/xuất gắn đơn bán trước khi trừ tồn; hóa đơn bán lập theo lượng giao thực tế, có trạng thái `Chờ lập hóa đơn` và báo cáo các phiếu giao chưa đối chiếu. Nếu dự án yêu cầu phát hành hóa đơn trước giao, cấu hình riêng cho kênh đó. Hóa đơn bán nội bộ cần phân biệt với hóa đơn điện tử có quy trình phát hành riêng.

## Ma trận chứng từ nguồn đề xuất

| Biến động kho | Chứng từ bắt buộc trước khi ghi sổ | Chứng từ đối chiếu bổ sung |
| :--- | :--- | :--- |
| Nhập từ nhà cung cấp | Phiếu nhập gắn phiếu mua đã duyệt | Hóa đơn nhà cung cấp, có thể đến sau theo chính sách |
| Xuất bán POS/Admin | Dòng đơn hàng đã được xác nhận/thanh toán | Hóa đơn hoặc biên nhận bán hàng liên kết cùng đơn |
| Xuất chuyển kho | Phiếu chuyển kho đã duyệt | Phiếu nhận ở kho đích |
| Xuất do kiểm kê thiếu | Phiếu kiểm kê và chứng từ điều chỉnh đã duyệt | Biên bản chênh lệch |
| Trả hàng nhà cung cấp hoặc khách hàng | Chứng từ trả hàng có liên kết giao dịch gốc | Hóa đơn/đơn gốc khi áp dụng |

## Luồng xuất bán đề xuất

1. Đơn hàng chứa SKU, số lượng, đơn vị bán, giá bán và tham chiếu phiên bản bảng giá tại thời điểm chốt.
2. Khi sự kiện xuất bán hợp lệ xảy ra, backend ghi `InventoryTransaction.OUT` liên kết `OrderItemId`, chụp WAC và trừ tồn trong cùng giao dịch.
3. Sinh hoặc liên kết bản ghi hóa đơn/biên nhận bán hàng với đơn, số chứng từ, trạng thái và các dòng đã chụp. POS tạo cùng lúc với `OUT`; đơn giao hàng có thể lập hóa đơn sau khi biết lượng thực giao và phải được theo dõi đến khi đối chiếu xong.
4. Truy vấn từ hóa đơn → đơn hàng → dòng xuất kho và chiều ngược lại. Mỗi dòng `OUT` phải có đúng một nguồn nghiệp vụ; không được ghi xuất tự do.

Với chuyển kho và điều chỉnh kiểm kê, **không tạo hóa đơn bán hàng giả** để thỏa điều kiện truy vết. Cần loại chứng từ đúng với nghiệp vụ.

## Phân biệt hóa đơn mua với lô hàng xuất

Nếu yêu cầu là “một dòng xuất bán phải chỉ ra hóa đơn mua của đúng hàng đó”, cần theo dõi lô/serial và chính sách chọn lô. WAC chỉ tính giá bình quân của lượng tồn; nó không xác định đơn vị vừa bán xuất từ hóa đơn mua nào. Truy vết chứng từ mua ở cấp tổng tồn là khả thi, nhưng không chứng minh nguồn gốc từng đơn vị hàng khi các lô đã trộn.

## Dữ liệu và API dự kiến

- `sales_invoices` và `sales_invoice_items` nếu dự án yêu cầu hóa đơn bán hàng là thực thể độc lập, với `OrderId`, số chứng từ, trạng thái, thời điểm phát hành và snapshot dòng hàng.
- Không thay thế `OrderItemId` trên sổ kho bằng `InvoiceId`; lưu liên kết hóa đơn tại đơn/dòng đơn để giữ đúng chứng từ gây ra biến động kho.
- API tra cứu chuỗi chứng từ theo đơn, theo dòng sổ kho và theo số hóa đơn. Quyền xem giá vốn phải tách khỏi quyền in chứng từ cho khách.
- Ràng buộc duy nhất và idempotency để một lần xác nhận/checkout không tạo hai dòng xuất hoặc hai hóa đơn.

## Tiêu chí nghiệm thu đề xuất

1. Từ một dòng `OUT`, người có quyền truy được chứng từ nguồn hợp lệ và người xác nhận.
2. POS retry checkout không sinh thêm xuất kho hoặc hóa đơn trùng.
3. Chuyển kho/kiểm kê vẫn có chứng từ nguồn nhưng không bị ép dùng hóa đơn bán hàng.
4. Hóa đơn in lại giữ nguyên giá, số lượng và đơn vị đã chụp dù bảng giá sau đó thay đổi.

## Quyết định còn mở

- “Xuất phải có hóa đơn mua hàng” được hiểu là cần **hóa đơn bán hàng** cho xuất bán, hay cần truy ngược **hóa đơn mua của lô xuất**? Hai yêu cầu này dẫn tới thiết kế khác nhau.
- Phạm vi “hóa đơn” là biên nhận bán hàng nội bộ hay chứng từ hóa đơn có quy trình phát hành riêng.
- Thời điểm phát hành hóa đơn so với xác nhận xuất và cách xử lý hủy/trả hàng.
- Hạn phải hoàn tất đối chiếu hóa đơn cho đơn giao hàng và ai được xử lý đơn quá hạn.
