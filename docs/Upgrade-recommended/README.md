# Upgrade recommended

Đây là tập tài liệu phân tích **đề xuất nâng cấp từ GVHD Nguyễn Văn Chiến** cho OISMS. Các phương án chi tiết được nhóm dự án tiếp tục phân tích để chốt phạm vi và quy tắc nghiệp vụ; chưa phải mô tả chức năng đã triển khai.

## Mục lục

| Tài liệu | Nội dung |
| :--- | :--- |
| [01. Mua hàng, nhập kho và giá vốn](./01-mua-hang-nhap-kho-gia-von.md) | Chứng từ mua, đối chiếu hóa đơn, nguồn đơn giá và WAC |
| [02. Truy vết chứng từ xuất kho](./02-truy-vet-chung-tu-xuat-kho.md) | Đơn hàng, hóa đơn bán hàng và chứng từ nguồn của từng loại xuất. Backend đã xong, [báo cáo](../report-after-upgrade/hoa-don-truy-vet-xuat-kho.md); Admin có trang Hóa đơn bán (`/invoices`): xem chi tiết, truy vết, in lại |
| [03. Quy định kiểm kê](./03-quy-dinh-kiem-ke.md) | Lịch giữa tháng/cuối tháng, chốt số liệu, duyệt chênh lệch |
| [04. Bảng giá theo thời gian hiệu lực](./04-bang-gia-theo-thoi-gian.md) | Phiếu đổi giá, lịch sử giá theo `valid_from` và giá chụp vào đơn |
| [05. Nhiều đơn vị tính cho hàng hóa](./05-nhieu-don-vi-tinh.md) | Đơn vị gốc, quy đổi theo SKU, giá theo đơn vị và chứng từ |
| [06. Rà soát Clean Architecture](./06-ra-soat-clean-architecture.md) | Bằng chứng ban đầu, ý nghĩa các loại Context và kế hoạch kiểm tra/sắp xếp lại file |
| [07. Thanh toán QR bằng VietQR](./07-thanh-toan-qr-vietqr.md) | Kế hoạch của nhóm (không phải đề xuất GVHD) cho CS-05: QR ở POS, tài khoản nhận theo tenant, xác nhận tự động tùy chọn. Đã triển khai giai đoạn (a), (b) (07/11/2026); giai đoạn (c) webhook ngân hàng tạm gác. POS Admin trong kế hoạch đã gỡ, nay chỉ còn `pos-app` |
| [08. Trả hàng từ khách (CS-01)](./08-tra-hang-cs01.md) | Kế hoạch của nhóm theo mô hình KiotViet/Odoo: phiếu trả gắn đơn gốc, nhập `IN` theo giá vốn lúc bán, tính lại WAC, trừ doanh thu theo ngày trả. Đã triển khai backend và giao diện Admin (09/11/2026) |
| [09. Chatbot AI hỗ trợ cơ bản](./09-chatbot-ai-co-ban.md) | Trợ lý OISMS cho Owner và Staff: chỉ đọc, tóm tắt nhập/xuất kho, doanh thu, hàng sắp hết, gợi ý nhập và hướng dẫn sử dụng; chọn model Groq hoặc Gemini; không thêm bảng. Đã triển khai trên nhánh `feature/OISMS-58-chatbot-ai`. **Không có kinh phí nên API AI chỉ dùng gói miễn phí, hạn mức rất thấp.** |
| [10. Quên mật khẩu và đăng nhập bằng SĐT](./10-quen-mat-khau-dang-nhap-sdt.md) | Kế hoạch của nhóm: đăng nhập bằng email hoặc SĐT, đặt lại mật khẩu qua link email (thêm bảng `password_reset_tokens`), Owner đặt lại hộ, OTP SMS để giai đoạn sau. Chưa triển khai |

Hiện trạng triển khai giao diện: [báo cáo nghiệm thu frontend](../report-after-upgrade/frontend-coming-soon-va-upgrade.md).
