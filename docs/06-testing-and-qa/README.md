# 06. KIỂM THỬ & ĐẢM BẢO CHẤT LƯỢNG (TESTING & QUALITY ASSURANCE)

| Thông tin kiểm soát thư mục | Chi tiết |
| :--- | :--- |
| **Mã phân hệ (Module ID)** | `MOD-QA-06` |
| **Phiên bản hiện hành** | `v1.0.0` |
| **Ngày cập nhật gần nhất** | `2026-10-22` |
| **Nguồn đối chiếu gốc** | `De bai CNPM.pdf` — Mục `NFR-PERF-01..02`, `NFR-MAINT-02`, `3.2.e` (Trang 3, 4, 5) |

---

## Danh mục Tài liệu trong Phân hệ `06-testing-and-qa/`

| STT | Mã tài liệu | Tên file | Phiên bản | Nội dung chính |
| :---: | :---: | :--- | :---: | :--- |
| **1** | `DOC-QA-01` | [`01-requirements-matrix-rtm.md`](./01-requirements-matrix-rtm.md) | `v1.0.0` | Ma trận truy vết 100% yêu cầu chức năng (`FR-*`) và phi chức năng (`NFR-*`) sang mã nguồn, test case và Sprint. |
| **2** | `DOC-QA-02` | [`02-unit-and-integration-test.md`](./02-unit-and-integration-test.md) | `v1.0.0` | Kế hoạch và hướng dẫn chạy 194 bài Unit & Integration Tests trên PostgreSQL thật (Coverage `> 92%`). |
| **3** | `DOC-QA-03` | [`03-concurrency-stress-test.md`](./03-concurrency-stress-test.md) | `v1.0.0` | Kịch bản kiểm thử chịu tải đồng thời Flash Sale (`50 reqs / 1 SKU`) và kiểm thử Idempotency. |
| **4** | `DOC-QA-04` | [`04-acceptance-criteria-dod.md`](./04-acceptance-criteria-dod.md) | `v1.0.0` | Bộ tiêu chuẩn nghiệm thu kỹ thuật bắt buộc (Definition of Done) cho toàn dự án. |
