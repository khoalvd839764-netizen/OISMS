# DANG KHOA REPORT - BACKEND 1 & TEAM LEAD

Directory of technical reports, testing guides, and acceptance handovers by **Dang Khoa (Backend 1 Lead & Team Lead)**.

## Sprint 3 Reports (`./sprint-3/`)

### 1. Task Reports & Architecture Design
* **Task 1: API Contract & Persistence Foundation:** [README.md](./sprint-3/README.md#1-mục-tiêu--phạm-vi-objectives--scope) *(Hoàn thành 24/10/2026 — Chốt hợp đồng API, Dual-Route Checkout, Domain Entities, EF Mapping, DTOs & Service Interfaces)*
* **Task 2: Atomic POS Checkout (Fast Checkout Toàn Giỏ):** [README.md](./sprint-3/README.md#10-kết-luận-kiểm-chứng-độc-lập-của-codex--vòng-3--v12) *(VERIFIED bởi Codex ngày 24/10/2026, v1.2 — 117/117 unit tests và 21/21 integration tests Sprint 3 pass; B-01 và tích hợp frontend được quản lý riêng)*
* **Task 3: Online Order Lifecycle:** [README.md](./sprint-3/README.md) *(VERIFIED bởi Codex ngày 24/10/2026, v1.1 — T3-F01..T3-F05 đã đóng; 117/117 unit tests, 55/55 integration tests Sprint 3 pass và probe độc lập đạt; xem mục 14)*
* **Task 4A: Inter-Warehouse Stock Transfer (Chuyển kho 2 bước):** [README.md](./sprint-3/README.md) *(VERIFIED bởi Codex ngày 24/10/2026, review vòng 3 v1.2 — T4A-F01..T4A-F07 CLOSED; build và 28/28 tests chuyển kho được kiểm chứng độc lập. Kế thừa kết quả unit/regression v1.1 vì DLL ứng dụng không đổi; B-01 riêng. Xem mục 20.)*
* **Task 4B: Warehouse Stocktaking & Variance Reconciliation (Kiểm kê kho & Cân bằng tồn):** [README.md](./sprint-3/README.md#21-báo-cáo-triển-khai-task-4b-stock-count--v10) *(Sẵn sàng bàn giao review vòng 1 v1.0 — Hoàn tất trọn vẹn vòng đời Draft -> Counting -> Confirmed/Cancelled, Snapshot tồn & Sequence, Sequence Drift detection 409, WAC surplus/deficit, bảo vệ Active reservations 409, Advisory locks loại trừ race condition & deadlock, 35/35 integration tests pass độc lập trên PostgreSQL 16 thật. Xem mục 21.)*

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
