# Mẫu bàn giao của Antigravity

Trả ngay trong chat, ngắn gọn. Thiếu mục bắt buộc sẽ bị trả lại trước khi review.

```text
TASK: {TASK-ID} | Mức: {…} | Vòng: {x} | Trạng thái: {READY_FOR_REVIEW / VERIFIED (chỉ FAST) / BLOCKED}
BASELINE → HIỆN TẠI: {commit} + {thay đổi chưa commit}
FILE ĐÃ ĐỔI: {danh sách}

AC:
- AC-01: ĐẠT / CHƯA ĐẠT / CHƯA KIỂM → {test hoặc bằng chứng}

KIỂM TRA ĐÃ CHẠY:
- {lệnh} | cwd | provider (InMemory/PostgreSQL) | PASS x/y, FAIL, NOT RUN | giới hạn

FINDING (vòng fix):
- F-01: đã sửa chờ review / phản biện: {bằng chứng}

DOCS ĐÃ CẬP NHẬT: {file — mục đã sửa; mỗi file trong "DOCS PHẢI CẬP NHẬT" của prompt phải có mặt, chưa làm thì ghi lý do}
LỆCH SO VỚI DOCS HOẶC PROMPT: {không có / nêu cụ thể và lý do}
THỬ NGHIỆM THẤT BẠI: {giả thuyết → kết quả}
BLOCKER VÀ ĐIỀU KIỆN GỠ: {không có / nêu cụ thể}
NGOÀI SCOPE PHÁT HIỆN ĐƯỢC: {một dòng mỗi mục, chưa sửa}
```
