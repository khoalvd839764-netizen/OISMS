# FR-13. Trợ lý OISMS

| Mã tài liệu | Yêu cầu | Phiên bản | Ngày cập nhật |
| :--- | :--- | :--- | :--- |
| DOC-FR-13 | FR-AI-02 | v1.0.0 | 2026-11-10 |

Owner và Staff hỏi bằng tiếng Việt trong Admin để tìm hướng dẫn sử dụng và tóm tắt số liệu của cửa hàng. Backend tính số liệu và kiểm tra quyền. Mô hình diễn đạt kết quả từ các công cụ chỉ đọc.

| Phạm vi | Dữ liệu và quyền |
| :--- | :--- |
| Nhập/xuất kho | Tổng theo ngày nghiệp vụ, chi nhánh và đơn vị gốc, tách chuyển kho và trả hàng. |
| Doanh thu | Doanh thu thuần sau trả hàng, số đơn, phân theo ngày và kênh. Owner có thêm lãi gộp. |
| Tồn thấp | SKU chạm hoặc dưới ngưỡng đặt lại, tồn khả dụng và mức cảnh báo. |
| Gợi ý nhập | Tối đa 20 gợi ý ưu tiên theo ngày cạn hàng dự kiến, dùng dữ liệu FR-AI-01. |
| Hướng dẫn | Các thao tác trên Admin theo vai trò được xác thực. |

Vai trò và tenant lấy từ JWT. Chi nhánh được kiểm tra thuộc tenant trước khi đọc số liệu. Payload doanh thu cho Staff chỉ gồm các trường doanh thu thuần và số đơn. Cashier nhận HTTP 403.

Panel mặc định dùng tất cả chi nhánh và ô chọn chỉ liệt kê chi nhánh đang hoạt động. Tên chi nhánh được nhắc trong câu hỏi ưu tiên hơn lựa chọn hiện tại. Backend so tên của các chi nhánh đang hoạt động trong tenant, bỏ dấu và không phân biệt hoa thường. Khi không khớp duy nhất, trợ lý hiển thị tối đa 10 nút chọn. Bấm một nút sẽ gửi lại câu hỏi gần nhất theo chi nhánh đã xác nhận, giữ một bản của tin người dùng trong lịch sử.

Câu trả lời hiển thị đoạn văn, số in đậm, mã nội tuyến và danh sách. Dòng nguồn kèm phạm vi chi nhánh thực tế và khoảng ngày khi có.

Ngày nghiệp vụ theo UTC+07. Mỗi ngày được truy vấn từ 00:00 đến trước 00:00 ngày kế tiếp. Khoảng tra cứu tối đa 92 ngày. Trợ lý mặc định tắt. Lịch sử tồn tại trong bộ nhớ của panel theo phiên Admin.

[Thiết kế nâng cấp](../Upgrade-recommended/09-chatbot-ai-co-ban.md) · [Hợp đồng API](../05-api-contracts/04-order-endpoints.md#21-trợ-lý-oisms-fr-ai-02) · [Ma trận RBAC](../01-users-and-rbac/02-rbac-matrix.md)

## Lưu ý: giới hạn hạn mức API AI

Trợ lý OISMS đã được tích hợp và chạy được với Groq (`openai/gpt-oss-120b`) và Google Gemini (`gemini-3.5-flash`). **Nhóm không có kinh phí cho API AI**, nên bản demo chỉ dùng key gói miễn phí và hạn mức rất thấp:

| Nhà cung cấp | Hạn mức gói miễn phí (đo hoặc tra ngày 10/11/2026) | Ảnh hưởng thực tế |
| :--- | :--- | :--- |
| Groq | 30 request/phút, 8.000 token/phút, khoảng 200.000 token/ngày | Khoảng 40–60 câu hỏi số liệu mỗi ngày; "Tóm tắt hôm nay" có thể chạm giới hạn token/phút |
| Google Gemini (AI Studio Free Tier) | 20 request/ngày cho mỗi model | Khoảng 5–10 câu hỏi mỗi ngày, vì mỗi câu gọi model 2–4 lượt |

Hệ quả:
- Khi hết lượt, chatbot báo `AI_MODEL_QUOTA` ("Model … đã hết lượt của nhà cung cấp") và gợi ý hỏi lại bằng model còn lại. Đây là giới hạn của gói miễn phí, không phải lỗi hệ thống.
- Không dùng chatbot cho kiểm thử tải hay demo liên tục. Trước buổi demo nên chờ hạn mức ngày được làm mới (Groq theo giờ UTC, Gemini theo giờ Thái Bình Dương) và quay sẵn video dự phòng.
- Mọi chức năng không dùng AI vẫn chạy bình thường khi AI hết lượt hoặc tắt, kể cả tab Gợi ý nhập hàng (`FR-AI-01`, tính bằng công thức).
- Muốn tăng hạn mức: gắn tài khoản thanh toán cho key (Groq Developer tier, hoặc credit Google Cloud của gói Google AI Pro cho Gemini) rồi chỉ đổi biến môi trường, không cần sửa code.
- Gói miễn phí có thể dùng dữ liệu gửi lên để cải thiện model; chỉ dùng với dữ liệu demo.
