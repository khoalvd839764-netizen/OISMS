# Mẫu prompt fix

Một prompt gom mọi finding chặn; ≤ ~30 dòng. Trước khi gửi, kiểm tra: còn trong trần vòng, số finding đang giảm, không có finding nào đã trượt hai lần, và prompt khác lần trước.

```text
Đọc skills/claude-to-antigravity/references/antigravity.md, mục "Khi nhận prompt fix".

TASK: {TASK-ID} | Vòng {x}/{trần} | Prompt {y}/3 | Diff đã review: {commit/dấu vết}

CHỈ SỬA CÁC FINDING SAU:
F-01 | P{n} | AC-{n} | {file:dòng}
  Kỳ vọng: {…}  Thực tế: {…}
  Điều kiện đóng: {lệnh/test và kết quả cần có}
{Đã trượt 1 lần thì thêm: Lần trước đã thử {…}, không hiệu quả vì {…}.}

GIỮ NGUYÊN (đã đạt): {AC, test}
KHÔNG LÀM: refactor, sửa finding khác, thêm tính năng, commit/push.

CHẠY LẠI: {chỉ các lệnh bị ảnh hưởng}

Không đồng ý finding nào thì phản biện một lần kèm bằng chứng. Đây là vòng cuối nếu x = trần.
BÀN GIAO: mục tối thiểu của handoff.md, finding ghi "đã sửa chờ review".
```
