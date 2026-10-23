# 04. THIẾT KẾ KIẾN TRÚC & KỸ THUẬT CHI TIẾT (ARCHITECTURE & DETAILED DESIGN)

| Thông tin kiểm soát thư mục | Chi tiết |
| :--- | :--- |
| **Mã phân hệ (Module ID)** | `MOD-ARC-04` |
| **Phiên bản hiện hành** | `v1.0.0` |
| **Ngày cập nhật gần nhất** | `2026-10-22` |
| **Nguồn đối chiếu gốc** | `De bai CNPM.pdf` — Mục 3.2.b, 3.2.d, 3.2.e (Trang 1, 4, 5) |

---

## Danh mục Tài liệu trong Phân hệ `04-architecture-and-design/`

| STT | Mã tài liệu | Tên file | Phiên bản | Nội dung chính |
| :---: | :---: | :--- | :---: | :--- |
| **1** | `DOC-ARC-01` | [`01-clean-architecture.md`](./01-clean-architecture.md) | `v1.0.0` | Kiến trúc phân tầng Clean Architecture 4 lớp (`Domain`, `Application`, `Infrastructure`, `WebAPI`). |
| **2** | `DOC-ARC-02` | [`02-multi-tenant-design.md`](./02-multi-tenant-design.md) | `v1.0.0` | Thiết kế SaaS Multi-tenancy, cơ chế phân giải `TenantId` từ JWT và bảo vệ đa lớp chống rò rỉ dữ liệu. |
| **3** | `DOC-ARC-03` | [`03-database-design.md`](./03-database-design.md) | `v1.0.0` | Sơ đồ thực thể liên kết (ERD), thiết kế bảng Sổ cái Append-Only và chiến lược Indexing quy mô lớn. |
| **4** | `DOC-ARC-04` | [`04-state-machines.md`](./04-state-machines.md) | `v1.0.0` | Sơ đồ máy trạng thái (State Machine) cho Đơn hàng đa kênh, Phiếu nhập kho và Chuyển kho 2 bước. |
| **5** | `DOC-SEQ-01` | [`sequence-diagrams/seq-pos-fast-checkout.md`](./sequence-diagrams/seq-pos-fast-checkout.md) | `v1.0.0` | Sơ đồ trình tự thanh toán nhanh nguyên tử tại quầy POS (`Reserve -> Confirm -> Deduct`). |
| **6** | `DOC-SEQ-02` | [`sequence-diagrams/seq-online-reservation.md`](./sequence-diagrams/seq-online-reservation.md) | `v1.0.0` | Sơ đồ trình tự giữ chỗ tồn kho, xử lý Idempotency và khóa CSDL chống bán vượt trong Flash Sale. |
| **7** | `DOC-SEQ-03` | [`sequence-diagrams/seq-warehouse-transfer.md`](./sequence-diagrams/seq-warehouse-transfer.md) | `v1.0.0` | Sơ đồ trình tự chuyển kho nội bộ 2 bước (`Out-bound transit -> In-bound confirmation`). |
| **8** | `DOC-SEQ-04` | [`sequence-diagrams/seq-import-and-wac.md`](./sequence-diagrams/seq-import-and-wac.md) | `v1.0.0` | Sơ đồ trình tự xác nhận phiếu nhập kho, ghi sổ cái Append-Only và tính lại giá vốn bình quân WAC. |
