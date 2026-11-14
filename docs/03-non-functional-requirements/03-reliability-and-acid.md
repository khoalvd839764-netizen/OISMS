# 03. Tuân Thủ ACID & Tính Bất Biến Của Sổ Cái (ACID Compliance & Ledger Auditability)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-NFR-03` |
| **Phiên bản (Version)** | `v1.1.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-15` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `NFR-SEC-02`, `NFR-SEC-03` (Trang 4) |
| **Phạm vi áp dụng** | Quản lý Giao dịch (Transaction Management) & Bảng Sổ cái (`InventoryTransaction`) |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả nguyên tắc giao dịch nguyên tử ACID và tính bất biến không sửa/xóa của Sổ cái kho. | `INIT-NFR-03` |
| `v1.1.0` | `2026-11-15` | Nhóm dự án OISMS | Bổ sung cách diễn giải `NFR-SEC-02` đã chốt theo ranh giới giao dịch từng kênh bán. | `OISMS-59-G3A` |

---

## 1. Đặc Tả Chi Tiết (`NFR-SEC-02`, `NFR-SEC-03`)

| Mã NFR | Tên yêu cầu | Đặc tả kỹ thuật & Cơ chế bảo vệ |
| :---: | :--- | :--- |
| **`NFR-SEC-02`** | **ACID Compliance** (Tính nguyên tử & Toàn vẹn giao dịch) | - Chuỗi thao tác `Order Creation -> Reserve -> Deduct` (và luồng xác nhận Nhập kho + tính WAC) **BẮT BUỘC** thực thi bên trong một giao dịch cơ sở dữ liệu nguyên tử (`Atomic Database Transaction`).<br/>- Nếu xảy ra bất kỳ lỗi nào ở bước trung gian, toàn bộ thay đổi phải được **Rollback 100%**, không để lại dữ liệu rác hoặc lệch số dư giữa các bảng. |
| **`NFR-SEC-03`** | **Append-Only Ledger Auditability** (Khả năng kiểm toán Sổ cái chỉ ghi thêm) | - Bảng `InventoryLedger` (`inventory_transactions`) nghiêm ngặt chỉ cho phép ghi thêm (**No `UPDATE` or `DELETE` operations permitted**).<br/>- Mọi điều chỉnh sai sót số liệu bắt buộc phải thực hiện thông qua việc phát sinh **giao dịch sổ cái bù trừ (`Compensating ledger transactions`)**. |

---

## 2. Diễn Giải `NFR-SEC-02` Theo Ranh Giới Giao Dịch Từng Kênh

- **POS (`POST /api/pos/checkout`, `POST /api/orders/checkout`):** Toàn bộ chuỗi `Tạo đơn -> Giữ chỗ -> Trừ tồn -> Ghi sổ OUT -> Completed` (kèm chụp `CostPrice` và lập `SalesInvoice`) thực thi trong **một giao dịch cơ sở dữ liệu nguyên tử duy nhất**.
- **Đơn online (`Admin`, `Shopee`, `TikTok`, `Lazada`):** Không lưu trạng thái `Draft` riêng để tránh tách `tạo đơn` và `giữ chỗ` thành hai giao dịch, đồng thời không gộp bước xác nhận vào giao dịch tạo đơn vì `FR-ORD-03` yêu cầu nhân viên duyệt. Quy trình gồm hai giao dịch nguyên tử độc lập:
  1. **Giao dịch 1 (Tiếp nhận & giữ chỗ):** Khởi tạo đơn (`Draft -> Reserved` trong cùng giao dịch) + tăng `Reserved` và tạo `Reservation` (`Active`). Lỗi ở bất kỳ dòng hàng nào rollback toàn bộ đơn và giữ chỗ.
  2. **Giao dịch 2 (Xác nhận xuất kho):** Kiểm tra giữ chỗ `Active` còn hạn, trừ `OnHand`, giảm `Reserved`, ghi sổ `OUT`, chụp `OrderItem.CostPrice` (WAC), lập `SalesInvoice` và chuyển đơn sang `Confirmed`. Lỗi ở bất kỳ bước nào rollback 100%, không đổi `OnHand`, `Reserved`, `inventory_transactions` và `OrderItem.CostPrice`.
- **Hủy đơn & Hết hạn giữ chỗ (`Cancel` / `ReservationExpiryJob`):** Thực thi trong một giao dịch nguyên tử, chỉ giảm `Reserved`, chuyển giữ chỗ sang `Released`/`Expired` và đơn sang `Cancelled`, tuyệt đối không ghi bút toán `IN` bù vào sổ cái kho.
