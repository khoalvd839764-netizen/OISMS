# DANG KHOA REPORT - BACKEND 1 & TEAM LEAD

Directory of technical reports, testing guides, and acceptance handovers by **Dang Khoa (Backend 1 Lead & Team Lead)**.

## Sprint 3 Reports (`./sprint-3/`)

* **Báo cáo tổng kết chính thức Sprint 3 (BE1):** 👉 **[`SUMMARY.md`](./sprint-3/SUMMARY.md)** *(Đọc độc lập, đầy đủ 7 nội dung tổng kết & đối chiếu Jira)*
* **Mục lục điều phối Codex – Antigravity:** 👉 **[`codex-antigravity/README.md`](./sprint-3/codex-antigravity/README.md)** *(Quy ước giao tiếp & bảng mapping toàn diện)*
* **Index ngắn gọn Sprint 3:** [`sprint-3/README.md`](./sprint-3/README.md)

### Danh mục Phân hệ Kỹ thuật Sprint 3 (Backend 1 Core)
* **Task 1: API Contract & Persistence Foundation:** [`task-01-contract-foundation/`](./sprint-3/codex-antigravity/task-01-contract-foundation/README.md) *(DONE theo báo cáo gốc ngày 24/10/2026 — Chốt hợp đồng API, Dual-Route Checkout, Domain Entities, EF Mapping, DTOs & Service Interfaces)*
* **Task 2: Atomic POS Checkout (Fast Checkout Toàn Giỏ):** [`task-02-pos-checkout/`](./sprint-3/codex-antigravity/task-02-pos-checkout/README.md) *(VERIFIED bởi Codex ngày 24/10/2026, v1.2 — F-01..F-04 CLOSED; 117/117 unit tests và 21/21 integration tests Sprint 3 pass; B-01 và tích hợp frontend được quản lý riêng)*
* **Task 3: Online Order Lifecycle (State Machine):** [`task-03-order-lifecycle/`](./sprint-3/codex-antigravity/task-03-order-lifecycle/README.md) *(VERIFIED bởi Codex ngày 24/10/2026, v1.1 — T3-F01..T3-F05 CLOSED; 117/117 unit tests, 55/55 integration tests Sprint 3 pass và probe độc lập đạt)*
* **Task 4A: Inter-Warehouse Stock Transfer (Chuyển kho 2 bước):** [`task-04a-stock-transfer/`](./sprint-3/codex-antigravity/task-04a-stock-transfer/README.md) *(VERIFIED bởi Codex ngày 24/10/2026, v1.2 — T4A-F01..T4A-F07 CLOSED; build và 28/28 tests chuyển kho được kiểm chứng độc lập. Kế thừa unit/regression v1.1 vì DLL ứng dụng không đổi; B-01 riêng)*
* **Task 4B: Warehouse Stocktaking & Variance Reconciliation (Kiểm kê kho):** [`task-04b-stock-count/`](./sprint-3/codex-antigravity/task-04b-stock-count/README.md) *(VERIFIED v1.3 bởi Codex ngày 25/10/2026 — T4B-F01..T4B-F05 CLOSED. Build Release 0 warning/error và 51/51 tests PostgreSQL được reviewer thực chạy; kế thừa unit 117 và cross-flow 148 pass; B-01 riêng)*
* **Task 5: Webhook đa kênh & SignalR Realtime:** [`task-05-webhook-realtime/`](./sprint-3/codex-antigravity/task-05-webhook-realtime/README.md) *(VERIFIED v1.4 bởi Codex ngày 26/10/2026 — T5-F01..T5-F08 CLOSED. Observer nối đúng DbContext options, positive control 40P01=1/reset 0/race 0 PASS; rollback WAC/timestamp/notifier đạt; 26/26 tests liên quan pass: 14 Webhook PostgreSQL, 4 Hub, 8 Simulator. Kế thừa 129 unit và 249/250 full integration; B-01 riêng)*
* **Task 6: Hangfire Reservation Expiry Worker:** [`task-06-reservation-expiry/`](./sprint-3/codex-antigravity/task-06-reservation-expiry/README.md) *(VERIFIED v1.2 bởi Codex ngày 26/10/2026 — T6-F01..T6-F04 CLOSED. Stats scoped trên instance IOrderService; query candidate lỗi báo Failed; 4 điểm F03 đã kiểm source/runner: gated multi-SKU, snapshot rollback độc lập, cancellation đúng order, cùng SKU qua nhiều kho. Build Release 0 warning/error, 129 unit và full integration 260/261 PASS, fail duy nhất B-01)*
* **Task Docs & PR:** [`docs-pr-develop/`](./sprint-3/codex-antigravity/docs-pr-develop/README.md) *(Chuẩn hóa docs, tổng kết BE1 và mở PR develop)*

---

## Sprint 2 Reports (`./sprint-2/`)

### 1. Main Reports & Final Acceptance
* **Backend 1 Handover Report:** [S2_BACKEND1_REPORT_2026-10-19.md](./sprint-2/S2_BACKEND1_REPORT_2026-10-19.md)
* **Comprehensive Acceptance & Sign-off:** [S2_FINAL_ACCEPTANCE_2026-10-19.md](./sprint-2/S2_FINAL_ACCEPTANCE_2026-10-19.md)

### 2. Testing & Bug Fixes
* **Backend S2 Testing Guide:** [S2_BACKEND_TEST_GUIDE_2026-10-19.md](./sprint-2/S2_BACKEND_TEST_GUIDE_2026-10-19.md)
* **F2 (Idempotency) & F7 (SPA Nginx) Resolution:** [S2_FIX_F2_F7_2026-10-19.md](./sprint-2/S2_FIX_F2_F7_2026-10-19.md)

### 3. Reviews & Audits
* **Backend Handover Review (Oct 18):** [S2_BACKEND_REVIEW_2026-10-18.md](./sprint-2/S2_BACKEND_REVIEW_2026-10-18.md)
* **Admin Dashboard Integration Review (Oct 18):** [S2_ADMIN_INTEGRATION_2026-10-18.md](./sprint-2/S2_ADMIN_INTEGRATION_2026-10-18.md)
* **Admin & Backend Handover Audit (Oct 19, commit ab67d9e):** [S2_ADMIN_AUDIT_2026-10-19.md](./sprint-2/S2_ADMIN_AUDIT_2026-10-19.md)
* **Admin Frontend PR #10 Recheck (Oct 19, commit 043e9b5):** [S2_PR10_RECHECK_2026-10-19.md](./sprint-2/S2_PR10_RECHECK_2026-10-19.md)
