# 01. NGƯỜI DÙNG, VAI TRÒ & PHÂN QUYỀN (USERS & RBAC)

| Thông tin kiểm soát thư mục | Chi tiết |
| :--- | :--- |
| **Mã phân hệ (Module ID)** | `MOD-USR-01` |
| **Phiên bản hiện hành** | `v1.0.0` |
| **Ngày cập nhật gần nhất** | `2026-10-22` |
| **Nguồn đối chiếu gốc** | `De bai CNPM.pdf` — Mục `FR-AUTH-01..04`, `NFR-SEC-01`, `NFR-TENANT-01` |

---

## Danh mục Tài liệu trong Phân hệ `01-users-and-rbac/`

| STT | Mã tài liệu | Tên file | Phiên bản | Nội dung chính |
| :---: | :---: | :--- | :---: | :--- |
| **1** | `DOC-USR-01` | [`01-actors-and-personas.md`](./01-actors-and-personas.md) | `v1.0.0` | Đặc tả 5 nhóm tác nhân hệ thống: `Owner`, `Staff`, `Cashier`, `Webhook Simulator` và `Hangfire Background Worker`. |
| **2** | `DOC-USR-02` | [`02-rbac-matrix.md`](./02-rbac-matrix.md) | `v1.0.0` | Cấu trúc JWT Claims và Bảng ma trận phân quyền chi tiết cấp API & Màn hình giao diện cho 3 vai trò nội bộ. |
| **3** | `DOC-USR-03` | [`03-tenant-lifecycle.md`](./03-tenant-lifecycle.md) | `v1.0.0` | Quy trình khởi tạo Tenant nguyên tử, phân cấp Chi nhánh - Kho và quy tắc kiểm soát trạng thái hoạt động (`IsActive`). |
