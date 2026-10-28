# Sprint 3 — Frontend Admin / Phúc Hữu

Cập nhật 28/10/2026 theo chỉ đạo mới: **ưu tiên sản phẩm chạy luồng cơ bản, hoàn thiện lỗi/evidence sâu sau**. Trạng thái hiện tại **CORE_PLANNED**, chưa có smoke mới trong lượt lập kế hoạch.

- [Plan v2.0](./admin-completion/PLAN.md): một đợt C0–C5, không chờ đóng hết B01 rồi mới làm kho/báo cáo.
- [Prompt triển khai core v2.0](./admin-completion/PROMPT-START-B01.md): môi trường đúng source → phiên/danh mục → Orders → transfers/counts → reports/POS.
- [Backlog](./admin-completion/BACKLOG.md): giữ finding ID; hoãn print layout, evidence chuyên sâu và realtime nâng cao. Lỗi sai dữ liệu/quyền, fake success hoặc chặn core flow làm ngay.
- [Review B01 v1.4](./admin-completion/batch-01-contract-orders/reviews/v1.4-review.md) và [handover v1.4](./admin-completion/batch-01-contract-orders/handover.md): giữ lịch sử nghiệm thu đầy đủ; B01 chưa VERIFIED. Các prompt fix v1.x là lịch sử, không dùng để khóa đợt core theo thứ tự cũ.

Baseline tham chiếu `feature/OISMS-43-orders-transfers`, `f276eee` + working tree; pin lại khi triển khai, giữ mọi thay đổi skill/docs/test. Component 13/13 và sensitivity 2/2 được reviewer chạy PASS ở v1.4; HTTP PostgreSQL 3/3 PASS ở v1.3 có hash nguồn không đổi tại review v1.4. Chỉ tái sử dụng khi phần liên quan vẫn hợp lệ.

POS đã được tài liệu nhánh riêng ghi VERIFIED tại head tham chiếu `ee68bad`; chưa tự tích hợp vào Admin. Mốc core cần CORE-08 dùng cùng backend/test DB. Không tự merge nhánh hoặc nhận kết quả test rời thành E2E đã chạy.

Bàn giao core dùng checklist CORE-01..08 và một handoff có URL/cách chạy/fixture/kết quả. Codex xác nhận `CORE_READY_WITH_KNOWN_ISSUES` khi đủ luồng và an toàn; `PARTIAL_CORE` khi còn luồng thiếu. `VERIFIED` đầy đủ và DoD release giữ cho mốc sau.
