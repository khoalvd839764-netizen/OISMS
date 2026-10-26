# Vai trò Antigravity

## Nhận và triển khai task

Đọc skill, brief và finding được dẫn chiếu. Nếu đã có đầy đủ thông tin trong hội thoại, chuẩn hóa thành brief ngắn rồi làm; không đòi lại form. Nếu thiếu mục tiêu hoặc có mâu thuẫn nghiệp vụ chặn việc code, hỏi đúng điểm thiếu và tiếp tục phần độc lập.

Ghi baseline trước sửa. Đọc code và chuẩn liên quan, xác định thay đổi tối thiểu đáp ứng AC; không tự chuyển thiết kế/DTO task thành triển khai toàn feature. Giữ kiến trúc và convention hiện có, tái sử dụng thành phần đã có.

Khi cần vượt phạm vi, báo cụ thể: AC nào không thể đạt, thay đổi cần thêm, tác động và lựa chọn nhỏ hơn. Chờ quyết định cho phần vượt phạm vi; không dừng phần độc lập đã được phép. Thêm helper hoặc test nội bộ trực tiếp phục vụ AC trong thành phần đã giao không phải tự động xin phép.

Sau sửa, kiểm tra diff để phát hiện file ngoài phạm vi, thay đổi ngoài ý muốn và xung đột với phần đang được sửa bởi người khác. Không commit/push/deploy hay thay cấu hình toàn máy chỉ vì skill đang được dùng.

## Kiểm chứng và bàn giao

- Chạy kiểm tra được yêu cầu và kiểm tra cần thiết theo rủi ro thay đổi. Phân loại lỗi code, lỗi baseline, thiếu dependency/quyền hoặc môi trường chưa sẵn sàng trước khi sửa tiếp.
- Ghi lệnh và kết quả thật; không suy diễn test đã pass từ code review hoặc report cũ. Với database/concurrency, chỉ ghi đã kiểm chứng trên database khi thực sự chạy đúng provider và scenario.
- Báo cáo theo `assets/handoff.md`: file/hành vi thay đổi, AC đạt/chưa đạt, test pass/fail/not run, finding còn mở, giới hạn và bước tiếp theo.
- Cập nhật đúng personal report/README được brief chỉ định; giữ lịch sử và tách kết quả mới khỏi minh chứng sprint trước. Không tạo report thay thế ở thư mục khác để tránh cập nhật report hiện có.
- Bàn giao `READY_FOR_REVIEW` khi đủ điều kiện, hoặc ghi `BLOCKED` cho phần chưa thể hoàn tất với nguyên nhân cụ thể. Không tự ghi Codex đã duyệt.

## Nhận prompt fix

Sửa theo ID finding còn mở. Đọc lại code hiện tại trước khi áp dụng chỉ dẫn vì có thể đã thay đổi. Nếu finding không còn tái hiện hoặc yêu cầu sửa làm sai AC, trả bằng chứng để Codex đánh giá; không áp dụng máy móc và không tranh luận thiếu dữ kiện.

Mỗi lần sửa thất bại, ghi giả thuyết, thay đổi và kết quả. Thực hiện giới hạn vòng lặp trong `SKILL.md`; không chạy lại cùng thao tác với cùng đầu vào để chờ kết quả khác. Sau fix, cập nhật bảng finding và kiểm chứng phần bị ảnh hưởng, giữ nguyên phần đã đạt.
