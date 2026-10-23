# 03. Tuân Thủ ACID & Tính Bất Biến Của Sổ Cái (ACID Compliance & Ledger Auditability)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-NFR-03` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `NFR-SEC-02`, `NFR-SEC-03` (Trang 4) |
| **Phạm vi áp dụng** | Quản lý Giao dịch (Transaction Management) & Bảng Sổ cái (`InventoryTransaction`) |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả nguyên tắc giao dịch nguyên tử ACID và tính bất biến không sửa/xóa của Sổ cái kho. | `INIT-NFR-03` |

---

## 1. Đặc Tả Chi Tiết (`NFR-SEC-02`, `NFR-SEC-03`)

| Mã NFR | Tên yêu cầu | Đặc tả kỹ thuật & Cơ chế bảo vệ |
| :---: | :--- | :--- |
| **`NFR-SEC-02`** | **ACID Compliance** (Tính nguyên tử & Toàn vẹn giao dịch) | - Chuỗi thao tác `Order Creation -> Reserve -> Deduct` (và luồng xác nhận Nhập kho + tính WAC) **BẮT BUỘC** thực thi bên trong một giao dịch cơ sở dữ liệu nguyên tử (`Atomic Database Transaction`).<br/>- Nếu xảy ra bất kỳ lỗi nào ở bước trung gian, toàn bộ thay đổi phải được **Rollback 100%**, không để lại dữ liệu rác hoặc lệch số dư giữa các bảng. |
| **`NFR-SEC-03`** | **Append-Only Ledger Auditability** (Khả năng kiểm toán Sổ cái chỉ ghi thêm) | - Bảng `InventoryLedger` (`inventory_transactions`) nghiêm ngặt chỉ cho phép ghi thêm (**No `UPDATE` or `DELETE` operations permitted**).<br/>- Mọi điều chỉnh sai sót số liệu bắt buộc phải thực hiện thông qua việc phát sinh **giao dịch sổ cái bù trừ (`Compensating ledger transactions`)**. |
