# Sprint 3 BE2 — Codex / Antigravity

Ngày lập: **2026-10-26**. Trạng thái hiện tại: **VERIFIED — T01 v1.3**, theo Codex review R4.

Code Nhựt đã được fetch và checkout tại `feature/OISMS-42-sprint-3-backend-2`, head `d7fa4bc8738a1f22053d9b313f8e90c423b0483d`, tương ứng [PR #20](https://github.com/khoalvd839764-netizen/OISMS/pull/20). Chưa sửa source nghiệp vụ, commit, push hay merge vào `develop` trong lần lập plan này.

## Bắt đầu ở đây

1. Đọc [PLAN.md](./PLAN.md) để biết baseline, thứ tự task và điều kiện được gộp.
2. Đọc [review ban đầu](./reviews/initial-review.md) và [bằng chứng](./evidence/README.md).
3. T01 v1.3 đã được [Codex review R4](./task-01-core-alignment/reviews/v1.3-review-r4.md) nghiệm thu tại `ca6b5ce`: BE2-F06 CLOSED; 6/6 reviewer probes, 146/146 unit và 44/44 targeted integration PASS; CI backend/frontend success.
4. Có thể giao [T02](./task-02-read-apis/brief.md). T02–T06 vẫn `PLANNED`; toàn bộ backend Nhựt chưa hoàn tất và PR #20 chưa được nghiệm thu để merge develop.

| Task | Nội dung | Phụ thuộc | Trạng thái |
| --- | --- | --- | --- |
| [S3-BE2-T01](./task-01-core-alignment/brief.md) | Hợp nhất baseline BE1, giữ một lõi ghi và một route webhook | Không | VERIFIED v1.3 — Codex R4 |
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

## Kết luận review hiện tại

Đã hoàn tất [fix v1.3 sau R3](./task-01-core-alignment/briefs/v1.3-fix-r3.md); không cần chạy lại prompt fix cũ. [Review R4](./task-01-core-alignment/reviews/v1.3-review-r4.md) và [evidence R4](./task-01-core-alignment/evidence/review-r4/README.md) là kết luận hiện hành. Các review/brief cũ được giữ để truy vết.

Bước tiếp theo theo PLAN là T02 — API đọc đơn hàng, chuyển kho và kiểm kê. Việc review này không triển khai T02, không commit/push hay merge PR.
