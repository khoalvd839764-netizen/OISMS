# Mẫu prompt giao việc

Claude điền và trả trong khối code. Bỏ mục không áp dụng; ≤ ~60 dòng; dẫn đường dẫn, không dán nội dung docs.

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: {TASK-ID} — {tên}   | Mức: {FAST/STANDARD/CRITICAL} | Prompt {x}/3
BASELINE: branch {…}, commit {…}. Giữ nguyên thay đổi có sẵn: {…}

MỤC TIÊU: {1–2 câu}

NGUỒN CẦN ĐỌC (chỉ các mục này):
- {docs/…#mục}
- Mẫu code bắt chước: {đường dẫn}
- Skill kỹ thuật (nếu cần, tối đa 2): skills/{tên}/SKILL.md

ĐƯỢC SỬA: {file/thư mục}
KHÔNG ĐƯỢC SỬA: {phần ngoài scope; phần Claude sẽ làm}

ACCEPTANCE CRITERIA:
- AC-01 ({nguồn docs}): {hành vi quan sát được} → kiểm bằng {lệnh/test}
- AC-02 ...

INVARIANT PHẢI GIỮ: {chỉ các dòng liên quan từ oisms.md}

DOCS PHẢI CẬP NHẬT (bắt buộc khi đổi chức năng hiện có, API, quyền, trạng thái, kho/giá vốn, database; theo bảng "Thay đổi → docs" trong oisms.md):
- {file docs} — {mục cần thêm/sửa}
- Báo cáo: docs/report-after-upgrade/{tên}.md — đã làm, ảnh hưởng chức năng hiện có, thay đổi DB và cách nâng cấp, kiểm chứng, giới hạn

GATE (chạy thật và ghi kết quả):
- {lệnh, cwd, provider, điều kiện đạt}

GIỚI HẠN:
- Không commit/push. Không thêm dependency/migration ngoài danh sách trên.
- Một lỗi tối đa 2 lần thử; quá thì dừng, trả BLOCKED.
- Không nới assertion hay skip test.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```
