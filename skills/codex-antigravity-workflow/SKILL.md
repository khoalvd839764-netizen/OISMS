---
name: codex-antigravity-workflow
description: Điều phối Codex phân tích, giao task và review; Antigravity triển khai theo phạm vi, tiêu chí nghiệm thu và bằng chứng. Dùng khi chuẩn bị task, viết prompt giao việc hoặc prompt fix, kiểm tra bàn giao giữa hai agent; hạn chế sửa lặp và mở rộng ngoài yêu cầu. Không tự động áp dụng cho mọi yêu cầu code độc lập.
---

# Codex phân tích — Antigravity triển khai

## Chọn vai trò

- Trong Codex: mặc định phân tích, tạo task brief hoặc review theo [vai trò Codex](references/codex.md).
- Trong Antigravity: mặc định triển khai brief theo [vai trò Antigravity](references/antigravity.md).
- Vai trò người dùng chỉ định được ưu tiên. Nếu người dùng yêu cầu Codex trực tiếp sửa code, thực hiện yêu cầu đó và vẫn giữ phạm vi/bằng chứng; không ép chuyển ứng dụng.
- Không tự gửi lệnh sang ứng dụng, tạo agent hay chạy vòng điều phối nền. Việc bàn giao mặc định qua prompt/file để người dùng chuyển tiếp.
- Với repo OISMS, đọc [nguồn ngữ cảnh dự án](references/oisms.md). Chỉ tải skill kỹ thuật liên quan tới task.

## Hợp đồng công việc

Dùng [mẫu giao việc](assets/task-brief.md) cho task mới; dùng [mẫu bàn giao](assets/handoff.md) cho kết quả và vòng fix. Điền thông tin thực tế, bỏ mục không áp dụng. Task nhỏ có thể dùng trực tiếp trong chat; không tạo hệ thống quản lý task riêng.

Mỗi lần chỉ giao một mục tiêu có thể nghiệm thu độc lập. Brief phải có mã task/phiên bản, baseline, mục tiêu, phạm vi sửa, ngoài phạm vi, tiêu chí AC có ID, cách kiểm chứng và đường dẫn báo cáo. Những giả định ảnh hưởng API, dữ liệu hoặc phân quyền phải được làm rõ trước phần triển khai phụ thuộc chúng; vẫn làm phần độc lập đã được phép.

Phạm vi được khóa theo hành vi và thành phần, không chỉ danh sách file. Cho phép thêm test/helper/tài liệu cần trực tiếp cho AC trong thành phần đã giao. Thay đổi hành vi khác, dependency mới, schema/migration hoặc refactor rộng cần được nêu trong brief hoặc có chỉ đạo bổ sung của người dùng. Không hỏi lại quyền đã có.

## Quy tắc chung

1. Ghi nhận branch/commit và thay đổi chưa commit trước khi làm. Không reset, ghi đè hoặc nhận công phần thay đổi sẵn có. Nếu cùng file bị agent khác sửa, đọc lại diff trước khi tiếp tục.
2. Nếu có `.codegraph/`, dùng CodeGraph trước khi tìm/đọc code. Khi công cụ không có, thử CLI; nếu chỉ mục thiếu hoặc lỗi, ghi rõ và dùng tìm kiếm/đọc file. Kiểm chứng dữ liệu chỉ mục cũ bằng source hiện tại. Không tự index toàn repo.
3. Tách yêu cầu mong muốn khỏi hiện trạng: tài liệu xác định yêu cầu; code/test cho biết đã làm gì. Nếu chúng mâu thuẫn ở quyết định nghiệp vụ, nêu mâu thuẫn thay vì tự chọn tiện cho việc code.
4. Không mở rộng sang task tiếp theo, dọn code không liên quan, nâng phiên bản hay triển khai production vì thấy thuận tiện.
5. Ghi kết quả kiểm chứng đúng thực tế: lệnh, môi trường/provider, kết quả và giới hạn. Build pass không đồng nghĩa feature pass; mock/InMemory không chứng minh PostgreSQL hoặc concurrency.

## Chặn vòng lặp

- Mỗi finding có ID ổn định, AC liên quan, bằng chứng và điều kiện đóng. Vòng fix xử lý finding còn mở; không viết lại toàn bộ task.
- Sau hai lần thử cùng lỗi mà không có bằng chứng/giả thuyết mới, dừng cách sửa đó. Ghi nguyên nhân đã loại trừ, thông tin thiếu và bước phân biệt giả thuyết tiếp theo; tiếp tục việc độc lập đã được phép.
- Sau hai vòng review–fix còn lỗi chặn, Codex đánh giá lại nguyên nhân và brief trước khi giao vòng tiếp. Không tự động gửi lại cùng prompt. Có thể tiếp tục khi đã có kế hoạch sửa khác, kiểm chứng được, trong phạm vi được phép.
- Không chạy lại suite đã pass nếu không có thay đổi hoặc rủi ro mới liên quan. Không nới assertion, bỏ test hay đổi yêu cầu chỉ để làm xanh kết quả.
- Phát hiện lỗi mới phải có bằng chứng và liên hệ với AC hoặc regression do thay đổi hiện tại. Ý tưởng cải tiến ngoài phạm vi đưa vào backlog riêng, không thành điều kiện nghiệm thu mới.

## Trạng thái và điều kiện kết thúc

`READY → IMPLEMENTING → READY_FOR_REVIEW → VERIFIED`

Review có lỗi cần sửa: `CHANGES_REQUESTED → IMPLEMENTING`. Phần thiếu quyết định, quyền hoặc môi trường thiết yếu: ghi `BLOCKED` cùng điều kiện gỡ; không tuyên bố hoàn thành.

Antigravity bàn giao `READY_FOR_REVIEW`; Codex chỉ kết luận `VERIFIED` khi các AC có bằng chứng phù hợp và không còn finding chặn. Kiểm tra bắt buộc chưa chạy thì nêu rõ còn chờ, không ghi DONE. Đây là trạng thái báo cáo của quy trình, không phải lệnh tạo goal/task trong ứng dụng.
