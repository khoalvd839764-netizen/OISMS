# Sprint 3 BE2 — Codex / Antigravity

Ngày lập: **2026-10-26**. Trạng thái hiện tại: **READY_FOR_REVIEW v1.1 — T01 fix sau review R1**.

Code Nhựt đã được fetch và checkout tại `feature/OISMS-42-sprint-3-backend-2`, head `d7fa4bc8738a1f22053d9b313f8e90c423b0483d`, tương ứng [PR #20](https://github.com/khoalvd839764-netizen/OISMS/pull/20). Chưa sửa source nghiệp vụ, commit, push hay merge vào `develop` trong lần lập plan này.

## Bắt đầu ở đây

1. Đọc [PLAN.md](./PLAN.md) để biết baseline, thứ tự task và điều kiện được gộp.
2. Đọc [review ban đầu](./reviews/initial-review.md) và [bằng chứng](./evidence/README.md).
3. T01 đã được Anti triển khai/bàn giao v1.0; [Codex review R1](./task-01-core-alignment/reviews/v1.0-review-r1.md) còn hai finding chặn. Xử lý [fix v1.1](./task-01-core-alignment/briefs/v1.1-fix-r1.md) trước khi chuyển T02; T02–T06 vẫn `PLANNED`.
4. [Prompt BE2 ban đầu](./PROMPT-FIX-BE2.md) được giữ làm lịch sử giao việc. Lượt tiếp theo dùng brief fix v1.1 và prompt R1 bên dưới, không triển khai lại toàn bộ T01 từ đầu.

| Task | Nội dung | Phụ thuộc | Trạng thái |
| --- | --- | --- | --- |
| [S3-BE2-T01](./task-01-core-alignment/brief.md) | Hợp nhất baseline BE1, giữ một lõi ghi và một route webhook | Không | READY_FOR_REVIEW v1.1 |
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

## Prompt fix sau review R1 cho Antigravity

```text
Sửa S3-BE2-T01 v1.1 theo docs/09-personal-reports/nhut/sprint-3/codex-antigravity/task-01-core-alignment/briefs/v1.1-fix-r1.md.
Đọc reviews/v1.0-review-r1.md và evidence/review-r1/ trong thư mục task trước khi sửa.
Làm trên feature/OISMS-42-sprint-3-backend-2; kiểm tra HEAD/working tree và giữ phần T01 đã đạt cùng baseline B-01.
Chỉ đóng BE2-T01-F01 (process/transaction/schema) và BE2-F06 (adapter bypass/canonical signature); không làm T02–T06.
Hai reviewer probes PostgreSQL đã tái hiện lỗi; thêm regression vào suite CI và kiểm chứng persisted state, không chỉ dùng InMemory.
Cập nhật handover v1.1 và AC -> testcase -> kết quả; test đạt rồi commit/push đúng nhánh OISMS-42, ghi CI thực tế.
Bàn giao READY_FOR_REVIEW v1.1 cho Codex, không tự ghi VERIFIED và không merge develop.
```
