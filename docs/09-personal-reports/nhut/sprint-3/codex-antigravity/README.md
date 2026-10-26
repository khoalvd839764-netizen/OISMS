# Sprint 3 BE2 — Codex / Antigravity

Ngày lập: **2026-10-26**. Trạng thái: **CHANGES_REQUIRED / PLAN_READY**.

Code Nhựt đã được fetch và checkout tại `feature/OISMS-42-sprint-3-backend-2`, head `d7fa4bc8738a1f22053d9b313f8e90c423b0483d`, tương ứng [PR #20](https://github.com/khoalvd839764-netizen/OISMS/pull/20). Chưa sửa source nghiệp vụ, commit, push hay merge vào `develop` trong lần lập plan này.

## Bắt đầu ở đây

1. Đọc [PLAN.md](./PLAN.md) để biết baseline, thứ tự task và điều kiện được gộp.
2. Đọc [review ban đầu](./reviews/initial-review.md) và [bằng chứng](./evidence/README.md).
3. Antigravity triển khai **T01 trước**, bàn giao theo AC; Codex review trước khi chuyển task tiếp theo. Các task hiện chỉ ở trạng thái `PLANNED`.
4. Dùng [prompt fix BE2](./PROMPT-FIX-BE2.md) để giao T01: có kiểm tra baseline B-01, bảo toàn workspace và commit/push đúng nhánh Nhựt sau kiểm thử.

| Task | Nội dung | Phụ thuộc | Trạng thái |
| --- | --- | --- | --- |
| [S3-BE2-T01](./task-01-core-alignment/brief.md) | Hợp nhất baseline BE1, giữ một lõi ghi và một route webhook | Không | READY_FOR_REVIEW v1.0 |
| [S3-BE2-T02](./task-02-read-apis/brief.md) | Hoàn thiện API đọc đơn hàng / chuyển kho / kiểm kê | T01 | PLANNED |
| [S3-BE2-T03](./task-03-access-isolation/brief.md) | Sửa quyền báo cáo, kiểm chứng tenant và input | T02 | PLANNED |
| [S3-BE2-T04](./task-04-report-correctness/brief.md) | Sửa báo cáo, filter, velocity và truy vấn | T03 | PLANNED |
| [S3-BE2-T05](./task-05-core-tests/brief.md) | Automated core tests và concurrency thực | T04 | PLANNED |
| [S3-BE2-T06](./task-06-premerge-handover/brief.md) | Regression cuối, tổng kết và chuẩn bị PR | T05 | PLANNED |

## Quy ước bàn giao

- Làm trên đúng nhánh OISMS-42; không sửa trực tiếp `develop` hay nhánh BE1.
- Mỗi task có `brief.md`; sau triển khai thêm `handover.md`, evidence và `reviews/` trong thư mục task. Ghi commit thực tế, file thay đổi, AC → testcase → kết quả, giới hạn và finding còn mở.
- Anti dùng `READY_FOR_REVIEW v1.x` khi đã đủ bằng chứng; chỉ reviewer ghi `VERIFIED`. Một task chưa đạt phải sửa đúng finding ID, không mở rộng sang task khác.
- Nếu dùng MCP Jira/Confluence/GitHub để đối chiếu, ghi ID tài liệu, version, ngày truy cập và chênh lệch với baseline đã pin. Các Jira task chưa được tạo bởi plan này.
- Không đưa credentials, JWT hay connection string vào handover/log được commit.

## Prompt khởi động cho Antigravity

```text
Triển khai S3-BE2-T01 theo docs/09-personal-reports/nhut/sprint-3/codex-antigravity/task-01-core-alignment/brief.md.
Đọc README.md, PLAN.md và reviews/initial-review.md trong cùng gói trước khi sửa.
Làm ngay trên feature/OISMS-42-sprint-3-backend-2; kiểm tra HEAD và working tree, bảo toàn thay đổi ngoài phạm vi.
Đối chiếu MCP Jira/Confluence nếu có, đặc biệt Confluence 5275650 v11; ghi rõ nguồn thực sự đọc được.
Chỉ thực hiện T01 theo PROMPT-FIX-BE2.md, kiểm tra baseline BE1 có fix B-01 trước khi pin SHA; không viết lại engine tồn kho và không làm task T02–T06.
Sau khi kiểm thử, tạo handover.md với AC -> testcase -> kết quả, diff, commit/baseline và finding ID.
Bàn giao READY_FOR_REVIEW v1.0 cho Codex; commit/push đúng nhánh OISMS-42 sau kiểm thử T01 theo prompt, không tự tuyên bố VERIFIED và không merge vào develop.
```
