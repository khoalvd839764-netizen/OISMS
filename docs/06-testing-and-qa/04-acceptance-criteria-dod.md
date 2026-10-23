# 04. Tiêu Chuẩn Nghiệm Thu Kỹ Thuật (Definition of Done & Acceptance Criteria)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-QA-04` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Toàn văn Trang 1 – 6 |
| **Phạm vi áp dụng** | Quy chuẩn nghiệm thu cho từng Pull Request và từng đợt đóng Sprint |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Thiết lập bộ tiêu chí Definition of Done (DoD) bắt buộc cho Backend, Frontend và Database. | `INIT-QA-04` |

---

## 1. Năm Tiêu Chí Nghiệm Thu Bắt Buộc (Core Definition of Done)

1. **Cơ sở dữ liệu tái tạo được 100%:** Chạy migration/schema script và seed data trên môi trường PostgreSQL sạch là khởi tạo được toàn bộ hệ thống không lỗi.
2. **Cô lập tuyệt đối Đa người thuê (`Multi-tenant Isolation`):** Mọi bảng dữ liệu nghiệp vụ đều có `TenantId`, Global Query Filter chặn triệt để việc đọc/ghi chéo dữ liệu giữa các cửa hàng.
3. **Sổ cái kho bất biến (`Append-Only Ledger`):** Mọi biến động kho phải tạo bản ghi `InventoryTransaction` có `BalanceAfter`. Tuyệt đối cấm lệnh `UPDATE` hoặc `DELETE` trên bảng sổ cái; cấm cập nhật tùy tiện cột `on_hand`.
4. **Tính giá vốn bình quân (`WAC`) tự động & chính xác:** Tự động tính lại giá vốn đơn vị khi xác nhận phiếu nhập kho và chụp giá vốn vào `OrderItem.CostPrice` khi xuất bán.
5. **Nói không với dữ liệu giả (`Zero Mock/Hardcoded Data`):** Web Admin và Web POS phải kết nối API thật 100%, đọc/ghi dữ liệu từ PostgreSQL, phân quyền đúng theo JWT và xử lý chuẩn xác các mã trạng thái `200`, `400`, `401`, `403`, `409`.
