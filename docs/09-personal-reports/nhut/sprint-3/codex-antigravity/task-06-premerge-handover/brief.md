# S3-BE2-T06 — Regression, bàn giao và chuẩn bị PR develop

**Trạng thái:** PLANNED. **Ưu tiên:** P1. **Phụ thuộc:** T01–T05 VERIFIED.

## Phạm vi

Chốt gói BE2 trên OISMS-42 trước khi đề nghị gộp PR #20. Tạo tổng kết cá nhân Nhựt và tài liệu giao tiếp Codex/Anti dựa trên code/evidence cuối cùng. Giữ báo cáo gốc làm lịch sử, ghi rõ bản nào thay thế kết luận cũ.

## Acceptance criteria

| AC | Điều kiện đạt / bằng chứng |
| --- | --- |
| AC-01 | Fetch lại develop và feature heads; ghi SHA kiểm thử cuối cùng, dependency BE1 và diff thực. Không rebase/force-push hoặc reset các thay đổi khác để làm sạch diff. |
| AC-02 | Release build, toàn bộ unit và PostgreSQL integration suite sau hợp nhất được chạy; có kết quả targeted concurrency/worker/RBAC/report benchmark. Count lấy từ TRX/runner, không giữ cứng 114 hay số test BE1 trước đây. |
| AC-03 | F01–F10 có bảng trạng thái và evidence closure. Regression mới không bị bỏ qua; B-01 được phân loại riêng theo PLAN.md. Nếu còn fail, ghi BLOCKED cùng task sở hữu; không tuyên bố all-green. |
| AC-04 | Tổng kết cá nhân phân biệt phần Nhựt làm, phần dùng lại từ BE1 và phần test bổ sung; liên kết từng task/commit/test. README dẫn đến brief/handover/review/evidence theo task, không để claim 100% cũ thành trạng thái hiện hành. |
| AC-05 | Nội dung PR mô tả vấn đề/hành vi sau sửa, endpoints/contracts ảnh hưởng, dependency BE1, validation thực và giới hạn; base `develop`, head OISMS-42. Tuân thủ template/branch protection thực tế của repo; không bịa checklist đã hoàn thành. |
| AC-06 | Nếu PR #20 vẫn mở, chuẩn bị cập nhật PR đó; nếu đã đóng/merge thì xác minh trạng thái và bàn giao phương án phù hợp, không tự tạo PR trùng. Chỉ đưa file trong phạm vi vào commit; bảo toàn hai thư mục tài liệu untracked đã nêu. |
| AC-07 | Gói bàn giao READY_FOR_REVIEW có evidence đầy đủ; reviewer quyết định VERIFIED. Chưa merge, chưa bật auto-merge; người dùng quyết định sau review/CI. |

## Bàn giao

`handover.md` và bản tổng kết có thể dùng để cập nhật PR. Chỉ chuẩn bị nội dung GitHub trong phạm vi task này; việc lập plan hiện tại chưa cập nhật PR, commit hay push code. Khi có yêu cầu xuất bản riêng, sử dụng công cụ/CLI GitHub khả dụng và gắn PR vào chat.
