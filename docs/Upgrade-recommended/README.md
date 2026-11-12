# Upgrade recommended

Thư mục này lưu đề xuất nâng cấp OISMS của GVHD Nguyễn Văn Chiến và của nhóm dự án. [Done](./Done/) lưu tài liệu đã triển khai theo phạm vi ghi trong bảng. [Công việc còn mở theo đề bài](./Pedding/README.md) lưu kế hoạch Gói 5 và các tiêu chí chi tiết còn mở của Gói 1–4.

## Mục lục

| Tài liệu | Trạng thái và dẫn chứng |
| :--- | :--- |
| [01. Mua hàng, nhập kho và giá vốn](./Done/01-mua-hang-nhap-kho-gia-von.md) | **Đã làm.** 01-A: phiếu mua, nhập kho và WAC. 01-B: hóa đơn nhà cung cấp, đối chiếu ba bên, chênh lệch có duyệt; không gồm công nợ NCC. [Báo cáo 01-A](../report-after-upgrade/mua-hang-nhap-kho.md) · [Báo cáo 01-B](../report-after-upgrade/hoa-don-nha-cung-cap-u01b.md) |
| [02. Truy vết chứng từ xuất kho](./Done/02-truy-vet-chung-tu-xuat-kho.md) | **Đã làm.** Có chứng từ nguồn, hóa đơn bán, truy vết và in lại trong Admin. [Báo cáo 02](../report-after-upgrade/hoa-don-truy-vet-xuat-kho.md) |
| [03. Quy định kiểm kê](./Done/03-quy-dinh-kiem-ke.md) | **Đã làm.** Có lịch giữa tháng, cuối tháng, kiểm kê đột xuất và duyệt chênh lệch. [Báo cáo 03](../report-after-upgrade/lich-kiem-ke-duyet-chenh-lech.md) |
| [04. Bảng giá theo thời gian hiệu lực](./Done/04-bang-gia-theo-thoi-gian.md) | **Đã làm.** Có phiếu đổi giá, phê duyệt, lịch sử và giá chụp vào đơn. [Báo cáo 04](../report-after-upgrade/bang-gia-theo-thoi-gian.md) |
| [05. Nhiều đơn vị tính cho hàng hóa](./Done/05-nhieu-don-vi-tinh.md) | **Đã làm.** Có đơn vị gốc, quy đổi theo SKU, chọn đơn vị trên chứng từ và POS. [Báo cáo frontend](../report-after-upgrade/frontend-coming-soon-va-upgrade.md) |
| [06. Rà soát Clean Architecture](./Done/06-ra-soat-clean-architecture.md) | **Đã làm phạm vi backend.** Hoàn tất các đợt refactor theo báo cáo. [Báo cáo 06](../report-after-upgrade/clean-architecture.md) |
| [07. Thanh toán QR bằng VietQR](./Done/07-thanh-toan-qr-vietqr.md) | **Đã làm giai đoạn A và B.** POS tạo VietQR, thu ngân xác nhận thủ công. Webhook ngân hàng là giai đoạn C tùy chọn và đang tạm gác. [Nghiệm thu VietQR](../report-after-upgrade/vietqr-cs05-review-pr39.md) |
| [08. Trả hàng từ khách (CS-01)](./Done/08-tra-hang-cs01.md) | **Đã làm.** Có phiếu trả gắn đơn gốc, cập nhật tồn/WAC và báo cáo doanh thu. [Kiểm thử PostgreSQL](../../test-oisms/backend/OISM.IntegrationTests/PostgresSalesReturnIntegrationTests.cs) |
| [09. Chatbot AI hỗ trợ cơ bản](./Done/09-chatbot-ai-co-ban.md) | **Đã làm.** Trợ lý OISMS hỏi đáp chỉ đọc cho Owner và Staff. Bản demo dùng API gói miễn phí. [Đặc tả FR-AI-02](../02-functional-requirements/fr-13-ai-assistant.md) |
| [10. Quên mật khẩu và đăng nhập bằng SĐT](./Pedding/10-quen-mat-khau-dang-nhap-sdt.md) | **Chưa làm.** Đăng nhập hiện dùng email. Đặt lại mật khẩu và đăng nhập SĐT còn mở. [AuthService](../../src/backend/src/OISM.Application/Features/Auth/Services/AuthService.cs#L144) |

Kiểm tra ngày 12/11/2026 đạt 6/6 integration test chọn lọc trên PostgreSQL cho các mục 02–05, 07–08 và 19/19 unit test dịch vụ chatbot cho mục 09. [Checklist nâng cấp](../../checklist.md#62-nâng-cấp-nghiệp-vụ-và-kiến-trúc)
