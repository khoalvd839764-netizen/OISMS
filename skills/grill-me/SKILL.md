---
name: grill-me
description: Phỏng vấn người dùng liên tục về một kế hoạch, đặc tả hoặc thiết kế OISMS cho đến khi chốt hết các nhánh quyết định, mỗi lần một câu hỏi trắc nghiệm. Dùng khi người dùng muốn "grill me", "hỏi vặn", "chất vấn" một plan, hoặc trước khi triển khai mục còn mở trong docs/Upgrade-recommended/Pedding (ví dụ Upgrade 01-B, Upgrade 10, Gói 1–5).
---

# Grill me — chất vấn plan OISMS

Phỏng vấn người dùng về mọi khía cạnh của kế hoạch cho đến khi hai bên hiểu như nhau. Đi qua từng nhánh của cây quyết định, giải quyết các quyết định phụ thuộc nhau theo thứ tự: quyết định nào làm thay đổi câu hỏi sau thì hỏi trước.

Dựa trên skill [grill-me của RobMitt](https://github.com/RobMitt/grill-me-skill), điều chỉnh cho OISMS.

## Cách hỏi

- Mọi câu hỏi đều dùng **AskUserQuestion**, không hỏi bằng văn bản thường.
- **Mỗi lần một câu hỏi.** Chờ trả lời rồi mới hỏi tiếp.
- Mỗi câu có 2–4 lựa chọn cụ thể, là các hướng người dùng thực sự có thể chọn. Tránh "Có/Không" trừ khi câu hỏi thật sự chỉ có hai nhánh. Người dùng luôn có ô "Other" để tự ghi.
- Nếu có hướng nên chọn, đặt lên đầu và thêm "(Recommended)", mô tả ngắn lý do trong `description`.
- Câu hỏi phải tự đứng được: nêu ngắn bối cảnh đã tìm thấy (file, mã FR, mã lỗi) ngay trong câu hỏi.
- Viết tiếng Việt, xưng "tôi" và "bạn".

## Tự tìm trước khi hỏi

Câu nào trả lời được bằng code hoặc docs thì tự tra, không hỏi người dùng:

1. Code: dùng CodeGraph trước (`codegraph_explore` hoặc `codegraph explore "<tên>"`), sau đó mới đọc file.
2. Docs là **yêu cầu**, code và test là **hiện trạng**. Xem bảng "Đọc thêm khi cần" trong `CLAUDE.md` ở gốc repo.
3. Docs và code mâu thuẫn về nghiệp vụ thì **đưa thành câu hỏi** cho người dùng quyết định, không tự đổi yêu cầu.

## Các nhánh cần đi qua

Bỏ nhánh không liên quan đến plan. Với nhánh liên quan, hỏi đến khi có câu trả lời cụ thể.

1. **Mục tiêu và phạm vi:** vấn đề cần giải, mã FR/NFR/CS/Upgrade liên quan, phần làm ngay và phần để sau, ai yêu cầu (GVHD hay nhóm).
2. **Nghiệp vụ và trạng thái:** luồng chính, ca lỗi, trạng thái chứng từ và chuyển trạng thái được phép, đối chiếu `docs/04-architecture-and-design/04-state-machines.md`.
3. **Phân quyền:** Owner, Staff, Cashier được làm gì; trường nhạy cảm (giá vốn, lãi gộp) có bị che ở dữ liệu trả về không; có phạm vi chi nhánh không.
4. **Tenant:** bảng mới có `tenant_id` và query filter; luồng chưa có JWT (đăng nhập, webhook) có cần `OpenPrivilegedScope` không.
5. **Kho, sổ cái và giá vốn:** có chạm `on_hand`, `Reserved`, sổ `inventory_transactions`, WAC, đơn vị gốc và `FactorSnapshot` không. Sửa sai bằng bút toán bù hay chứng từ điều chỉnh có người duyệt.
6. **Giao dịch, khóa và idempotency:** bước nào phải nằm trong cùng một transaction, khóa gì và theo thứ tự nào, có cần `Idempotency-Key`, retry trả gì, sự kiện SignalR phát sau commit.
7. **Dữ liệu và migration:** bảng/cột mới, script trong `docker/postgres/migrations/`, cập nhật `oisms_schema.sql`, `RequiredTables`, `init-oisms.sh`; dữ liệu cũ xử lý thế nào.
8. **API và lỗi:** endpoint, mã lỗi mới cần thêm vào `docs/12-error-and-status-codes/`, hợp đồng trong `docs/05-api-contracts/`.
9. **Giao diện:** Admin, POS hay cả hai; màn hình nào; trạng thái rỗng, lỗi, đang tải.
10. **Báo cáo và thời gian:** ngày nghiệp vụ UTC+07:00, khoảng nửa mở, ảnh hưởng tới báo cáo doanh thu/COGS.
11. **Kiểm thử và nghiệm thu:** unit test, integration test trên PostgreSQL thật cho phần transaction/concurrency, bằng chứng cần lưu để đóng mục checklist.
12. **Tài liệu:** file docs nào phải cập nhật (đặc tả, RTM, checklist, Pedding/Done).

## Luồng

1. Đọc plan hoặc tài liệu người dùng đưa, tự tra code/docs phần cần thiết, rồi liệt kê ngầm các nhánh còn mở.
2. Hỏi câu đầu tiên bằng AskUserQuestion.
3. Sau mỗi câu trả lời: xác nhận quyết định trong 1–2 câu, rồi hỏi ngay câu tiếp theo.
4. Nếu câu trả lời phá một quy tắc trong mục "Quy tắc không được phá" của `CLAUDE.md`, nói rõ quy tắc bị chạm và hỏi lại trước khi đi tiếp.
5. Tiếp tục cho đến khi mọi nhánh liên quan đã chốt.
6. Kết thúc bằng bản tóm tắt ngắn: các quyết định đã chốt, câu hỏi còn treo (nếu người dùng muốn để sau), rủi ro và việc tiếp theo.
7. Chỉ ghi bản tóm tắt vào docs khi người dùng yêu cầu; hỏi đường dẫn nếu chưa rõ. Không tự sửa code hoặc commit trong skill này.
