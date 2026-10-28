# Quy trình Codex và Antigravity

Skill dùng chung nằm tại [`skills/codex-antigravity-workflow/SKILL.md`](../../skills/codex-antigravity-workflow/SKILL.md). Codex đọc yêu cầu, chuẩn, report và code để giao một task rõ phạm vi, sau đó review. Antigravity triển khai, kiểm tra và cập nhật personal report theo brief.

## Sử dụng

Trong Codex, yêu cầu:

```text
Đọc skills/codex-antigravity-workflow/SKILL.md, vai trò Codex.
Đối chiếu chuẩn, kế hoạch Sprint 3, report Sprint 2 của Đăng Khoa và code hiện tại.
Tạo prompt cho Antigravity thực hiện task backend tiếp theo còn thiếu của Đăng Khoa.
Chốt phạm vi, AC, cách kiểm chứng và report cần cập nhật. Chưa sửa code.
```

Chuyển brief Codex tạo sang Antigravity cùng lời dẫn:

```text
Đọc skills/codex-antigravity-workflow/SKILL.md, vai trò Antigravity.
Thực hiện brief dưới đây, bảo toàn thay đổi sẵn có, kiểm tra đúng phạm vi,
cập nhật personal report được chỉ định và bàn giao bằng chứng theo skill.

[Dán task brief của Codex tại đây]
```

Sau khi Antigravity bàn giao, dùng trong Codex:

```text
Áp dụng skills/codex-antigravity-workflow/SKILL.md, vai trò Codex.
Review diff và handoff theo đúng brief đã giao. Xác minh AC và finding còn mở.
Nếu còn lỗi, tạo prompt fix theo ID finding, giữ phần đã đạt; không mở rộng task.
Nếu đủ bằng chứng, kết luận VERIFIED và nêu kiểm tra thực tế đã chạy.
```

## File hỗ trợ

- [Hướng dẫn Codex](../../skills/codex-antigravity-workflow/references/codex.md): phân tích, giao việc, review và prompt fix.
- [Hướng dẫn Antigravity](../../skills/codex-antigravity-workflow/references/antigravity.md): code, kiểm chứng và bàn giao.
- [Ngữ cảnh OISMS](../../skills/codex-antigravity-workflow/references/oisms.md): sử dụng report Sprint 2 và ghi kết quả mới đúng sprint.
- [Mẫu giao việc](../../skills/codex-antigravity-workflow/assets/task-brief.md) và [mẫu bàn giao](../../skills/codex-antigravity-workflow/assets/handoff.md).

## Discovery và giới hạn

Thư mục `skills/` giữ bản gốc để quản lý trong Git. Điểm discovery dùng chung là `.agents/skills/codex-antigravity-workflow`, liên kết tới thư mục skill gốc tại workspace hiện tại; `.agents/` đang được repo bỏ qua trong Git nên máy clone mới cần thiết lập lại liên kết hoặc đọc skill bằng đường dẫn như các prompt trên. OpenAI mô tả cách đặt skill theo repo tại `.agents/skills/` trong [hướng dẫn tổ chức workflow bằng skill](https://developers.openai.com/blog/skills-agents-sdk).

Antigravity công bố hỗ trợ workspace skill tại `.agents/skills/`, bao gồm IDE; xem [tài liệu Agent skills](https://antigravity.google/docs/skills?app=antigravity-ide). Có thể kiểm tra skill trong Customizations của IDE. Việc tồn tại file/liên kết chưa chứng minh ứng dụng đang chạy đã nạp skill; nếu chưa thấy, tải lại phiên làm việc hoặc dùng prompt đọc đường dẫn trực tiếp.

Skill đặt quy tắc hành vi cho agent, không phải sandbox cưỡng chế. Việc chuyển prompt giữa hai ứng dụng do người dùng thực hiện; bộ skill không tự gửi lệnh hay chạy vòng sửa nền. Giới hạn hai lần thử không có giả thuyết mới và đánh giá lại sau hai vòng review giúp phát hiện bế tắc, không có nghĩa được bỏ AC hoặc báo hoàn thành khi chưa đạt.
