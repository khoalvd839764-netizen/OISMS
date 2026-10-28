# Chức năng chưa hoàn thiện — không gộp với lỗi đang hoạt động

Ngày: 28–29/10/2026. Các mục đã bị khóa bằng nhãn Coming soon ở lượt trước **vẫn chưa được xem là hoàn tất yêu cầu**.

| ID | Chức năng | Hiện trạng | Việc cần có để sử dụng thật |
| --- | --- | --- | --- |
| CS-01 | Nhận hàng trả từ khách | Nút Kho đang disabled. Có liên kết/schema sổ kho, chưa có luồng UI/API nhận trả hàng hoàn chỉnh. Liên quan FR-INV-01. | Chọn đơn gốc, kiểm tra lượng đã bán/đã trả, nhận đúng kho, xác nhận, ghi IN bù trừ và lưu lịch sử; chốt chính sách hoàn tiền/giá vốn. |
| CS-02 | Quản lý nhân viên, phân quyền và phân công chi nhánh | Tab chỉ hiển thị tài khoản đang đăng nhập, không phải danh sách nhân viên đầy đủ; thêm/sửa đã khóa. | API và UI danh sách/tạo/cập nhật theo tenant/quyền; validation; lưu bền và đọc lại phiên mới. |
| CS-03 | Lưu ngưỡng cảnh báo tồn | Cấu hình đã khóa. Trước đó chỉ thay state cục bộ, chưa lưu server. Cảnh báo Inventory đang dùng ngưỡng mặc định 5. | Nối cấu hình thật với `ReorderPoint`; thống nhất bảng Kho, báo cáo cảnh báo và realtime theo chi nhánh; không giả lưu thành công. |
| CS-04 | Tìm kiếm toàn hệ thống ở thanh đầu trang | Input disabled và có nhãn Coming soon. Tìm kiếm riêng ở sản phẩm/POS/đơn/kho là chức năng khác. | Contract tìm nhiều loại dữ liệu, điều hướng kết quả đúng, quyền và tenant, trạng thái loading/empty/error. |
| CS-05 | Thanh toán QR | Hai POS đã khóa lựa chọn QR; mã mô phỏng không phải cổng thanh toán thật. Liên quan FR-POS-03. | Chốt QR động hoặc xác nhận thủ công; ngân hàng/webhook/đối soát nếu tự động; idempotency và trạng thái thanh toán rõ. |
| CS-06 | In chứng từ giao hàng và in báo cáo | Nút Admin disabled; bố cục in riêng chưa hoàn thiện. B01-E02 vẫn còn mở. In hóa đơn POS là luồng riêng. | Cô lập đúng chứng từ; khổ A4/80mm theo loại; không in lẫn danh sách/bộ lọc; che giá vốn theo vai trò; kiểm tra mọi trang PDF. |
| CS-07 | Tổng hợp bán hàng và đơn gần đây trên Tổng quan | KPI doanh thu/số đơn/lợi nhuận, đơn gần đây và biểu đồ mẫu đã thay bằng Coming soon. Danh mục SKU/giá trị tồn vẫn đọc dữ liệu. | Nối DTO phân trang thật, tiêu chí trạng thái ghi nhận doanh thu và thời gian; không cộng cache/session như dữ liệu server đầy đủ; biểu đồ dùng số thực tế. |
| CS-08 | Dự báo/gợi ý nhập bằng AI | Có đặc tả FR-12; chưa thấy luồng frontend/API/model chạy được trong các thư mục source được quét. | Xác nhận phần được giao, dữ liệu đầu vào, baseline/phương pháp, endpoint, UI và cách đánh giá dự báo; chưa được nhận là đã có chỉ vì ghi trong tài liệu. |

## Các phần chưa phải bug sản phẩm đã xác nhận

- PWA/offline: Handoff hiện nói rõ chưa có service worker/bán offline. Cần chốt mức yêu cầu trước khi coi thiếu offline là lỗi; không tự thêm queue giao dịch tài chính.
- Kết nối sàn thật so với simulator: đối chiếu phạm vi đề bài và backend hiện tại; không mặc định một simulator đúng đề là bug chỉ vì chưa kết nối tài khoản sàn thật.
- Kiểm thử tải, phục hồi sự cố, mức uptime và CI/release chưa được thay thế bằng build hoặc unit test PASS.

Danh sách bug đang hoạt động: [BUG-LIST.md](BUG-LIST.md). Kế hoạch triển khai các mục này chưa được thực hiện trong lượt rà soát này.
