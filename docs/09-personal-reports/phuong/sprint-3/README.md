# Sprint 3 — Frontend POS / Phương Lê

Trạng thái: **B01 VERIFIED v1.2 — review R3**, B01-F01/F02/F03 CLOSED trong phạm vi T02 + phần in T05. T01 VERIFIED v1.3 giữ nguyên. Còn B02–B03, chưa hoàn tất toàn bộ POS.

Cập nhật **28/10/2026 — review R4**: [B02 v1.3](./pos-completion/batch-02-stock-realtime-pwa/reviews/v1.3-review-r4.md) **CHANGES_REQUESTED**. Hai ca R3 và benchmark đã đạt; F01/F02/F03/F05 giữ CLOSED. F04 còn chiều barcode cũ→GET mới và searchAPI8/cache10; F06 còn live workflow OPEN/NOT RUN/BLOCKED. Build/34 baseline/19 B02 tests PASS, browser checkout→barcode8 PASS nhưng browser search mới vẫn dùng10. POS-STOCK-F01 chưa đóng toàn bộ. [Prompt fix v1.4](./pos-completion/batch-02-stock-realtime-pwa/reviews/v1.4-fix-prompt.md); B03 vẫn PLANNED.

| Nội dung | Tài liệu |
| --- | --- |
| Kế hoạch fix và hoàn thiện POS | [POS completion plan](./pos-completion/PLAN.md) |
| Task đã nghiệm thu | [S3-POS-T01 — An toàn checkout/retry/session](./pos-completion/task-01-checkout-safety/brief.md), [review R4 v1.3](./pos-completion/task-01-checkout-safety/reviews/v1.3-review-r4.md), [evidence R4](./pos-completion/task-01-checkout-safety/evidence/review-r4/README.md) |
| Đợt đã nghiệm thu | [B01 — Hóa đơn và in 58/80mm](./pos-completion/batch-01-receipt-print/brief.md): T02 + phần in T05; [review R3](./pos-completion/batch-01-receipt-print/reviews/v1.2-review-r3.md), [evidence R3](./pos-completion/batch-01-receipt-print/evidence/review-r3/README.md) |
| Đợt cần sửa sau review | [B02 — Tồn realtime/PWA/thao tác quầy](./pos-completion/batch-02-stock-realtime-pwa/reviews/v1.3-review-r4.md), CHANGES_REQUESTED v1.3 R4; F04 OPEN, F06 live workflow chưa đạt; B03 vẫn PLANNED |
| Lỗi tồn phải F5 sau checkout | POS-STOCK-F01 nằm ở [T03 — Làm mới tồn và SignalR](./pos-completion/PLAN.md#6-s3-pos-t03--làm-mới-tồn-sau-checkout-và-signalr), triển khai trong B02 và kiểm thử lại tại T06/B03 |
| Kết quả kiểm tra PR #24 | [Review ngày 27/10/2026](./PR24_REVIEW_2026-10-27.md) |
| Bằng chứng đối chứng head/base | [Evidence R1](./evidence/pr24-r1/README.md) |
| Báo cáo bàn giao ban đầu | [Báo cáo POS](./S3_POS_REPORT_2026-10-27.md) — các claim cần điều chỉnh theo review |

Nhánh thực hiện: `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`, head đầu vào `44317f3b5188c276fa3ef25c8faf30323e503079`. Giữ nguyên lịch sử và bằng chứng đã có. Kế hoạch này chưa sửa source, commit/push hay merge PR.
