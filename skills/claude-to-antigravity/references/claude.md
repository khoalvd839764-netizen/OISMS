# Vai trò Claude

Áp dụng luật trong [SKILL.md](../SKILL.md); file này chỉ thêm cách làm.

## Lập plan và viết prompt

1. Đọc sổ task nếu có (`tmp/claude-anti/<TASK-ID>.md`). Có rồi thì tiếp tục từ đó.
2. Xác định nguồn trong [oisms.md](oisms.md), đọc đúng mục cần. Dùng CodeGraph tìm mẫu code và blast radius.
3. Đối chiếu ngắn: yêu cầu / đã có trong code / còn thiếu. Không giao lại phần đã có.
4. Định tuyến GIAO / CHIA / TỰ LÀM. Báo người dùng một dòng; chỉ hỏi khi đề xuất Claude tự làm phần lớn hoặc thiếu quyết định nghiệp vụ.
5. Viết prompt theo [mẫu](../assets/prompt-implement.md), mọi đường dẫn đã xác minh tồn tại. Chỉ ra file mẫu để Anti bắt chước. Với CRITICAL, chốt gate và thứ tự lock trước khi giao, không để Anti tự thiết kế.
6. Đặt prompt trong một khối code để người dùng sao chép. Mỗi lần chỉ giao một prompt.

## Review một lượt

1. **Cổng evidence:** bàn giao thiếu mục bắt buộc hoặc NOT RUN không lý do thì trả lại ngay. Lần trả này không tính vòng sửa nhưng tính vào trần tổng.
2. **Cổng scope:** `git diff --stat` so với baseline; file lạ ngoài scope là finding.
3. **Đọc diff:** ưu tiên phần chạm invariant. Kiểm tra caller, thứ tự lock, nơi commit, tenant filter. Xem assertion có thực sự kiểm tra AC không.
4. **Gate CRITICAL:** tự chạy lại gate rủi ro trên PostgreSQL thật. Không có môi trường thì `BLOCKED`, không `VERIFIED`.
5. **Kết luận:** gom finding, cập nhật sổ task, chọn sửa tiếp hoặc thang leo.

Định dạng finding: `F-ID | P0–P3 | chặn/không | AC | file:dòng | kỳ vọng → thực tế | điều kiện đóng`.

- P0: mất dữ liệu, rò rỉ giữa tenant, lỗi bảo mật nghiêm trọng. P1: lỗi nghiêm trọng. P2: lỗi chức năng giới hạn, chặn khi vi phạm AC. P3: đề xuất, không chặn.
- Lỗi baseline chỉ chặn nếu trực tiếp ngăn AC. Đề xuất ngoài scope ghi backlog một dòng, không đưa vào prompt fix.
- Không gửi bản vá từ giả thuyết chưa chứng minh; giao kết quả cần đạt, gợi ý hướng khi đã có bằng chứng.

## Chọn bước sau review

| Tình huống | Bước tiếp |
| --- | --- |
| Không còn finding chặn, gate đủ | `VERIFIED`, hỏi người dùng về commit |
| Chỉ còn lỗi nhỏ ≤ ~20 dòng | Claude tự sửa, chạy lại test liên quan, không tốn một vòng Anti |
| Còn finding, chưa hết trần, số finding đang giảm | [Prompt fix](../assets/prompt-fix.md) gom một lượt |
| Hết trần, không hội tụ, hoặc lỗi trượt hai lần | Thang leo trong SKILL.md |

## Báo người dùng

Sau mỗi lượt: trạng thái, số vòng đã dùng, việc người dùng cần làm tiếp (dán prompt nào, quyết định gì). Không gửi nguyên diff. Báo trung thực phần chưa chạy và phần Claude tự sửa.
