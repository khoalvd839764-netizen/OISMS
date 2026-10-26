# S3-BE2-T05 — Automated core tests trên runtime thực

**Trạng thái:** PLANNED. **Ưu tiên:** P1. **Finding:** BE2-F07, BE2-F08; xác nhận lại F01/F02/F06 sau hợp nhất. **Phụ thuộc:** T04 VERIFIED.

## Phạm vi

BE2 chịu trách nhiệm automated core tests theo phân công v11. Dùng lại suite/fixture BE1 đã có; bổ sung khoảng trống, không copy suite để tăng số lượng. Thay/bỏ tuyên bố concurrency sai của batch tuần tự và cung cấp runner/test thực đi qua HTTP endpoint canonical. Nếu cần giữ batch API cho consumer hiện có, adapter phải dùng core chuẩn và được kiểm thử; không giữ engine riêng chỉ để chạy demo.

## Acceptance criteria

| AC | Điều kiện đạt / bằng chứng |
| --- | --- |
| AC-01 | Scenario 50 requests tranh 1 SKU có gate/barrier, đo được request overlap, context/connection riêng; không Task.WhenAll trên cùng DbContext. Dataset/run IDs riêng, fixture chỉ dùng database test có guard/marker. |
| AC-02 | Qua canonical reserve/webhook ingress đã chọn: đúng 1 thành công và 49 insufficient-stock 409, không 500/deadlock. HTTP 200/201 thành công phải khớp contract của endpoint, không sửa controller chỉ để khớp số cố định. DB cuối: OnHand=1, Reserved=1, Available=0; số order/reservation/ledger theo state đúng. |
| AC-03 | Replay cùng identity/payload giữ chỗ đúng một lần; shop khác độc lập; payload mismatch bị từ chối; batch chạy lại không trùng identity do chỉ dùng timestamp giây. |
| AC-04 | Có test insufficient stock, whole-cart rollback, confirm/cancel/complete retry, cost snapshot, TTL expired và fault/transaction failure. Invariant branch/warehouse/reservation/ledger không âm, không double-apply. |
| AC-05 | Expiry chạy qua Hangfire worker thật ở integration test phù hợp, xác nhận persisted state; không dùng InMemory/direct job call làm bằng chứng worker. Tái sử dụng test BE1 đủ coverage thay vì tạo job mới. |
| AC-06 | Phân biệt success/replay/409/validation/401/403/500/cancellation. Lỗi hệ thống và hủy không được cộng vào FailedConflictCount; lưu status/error code thực, không chỉ đếm exception message. |
| AC-07 | Ma trận FR/AC → test hiện có/test mới → runner result cho Orders, read APIs, reports, webhook, transfer, count, expiry; ghi provider và phạm vi. Không nhận ownership triển khai Simulator từ BE1 chỉ vì BE2 viết test. |

## Bàn giao

`handover.md`, lệnh tái chạy, fixture/database guard, dataset, status distribution, DB assertions và TRX/log trên cùng commit. Nếu môi trường thật không chạy được thì ghi chưa kiểm chứng; không chuyển thành pass bằng cách skip hoặc thay provider.
