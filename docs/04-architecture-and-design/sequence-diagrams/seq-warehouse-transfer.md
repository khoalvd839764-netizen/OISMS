# Sơ Đồ Trình Tự 03: Quy Trình Chuyển Kho 2 Bước Giữa Các Chi Nhánh (Two-Step Stock Transfer Sequence)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SEQ-03` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-INV-01`, `FR-INV-03` (Trang 2) |
| **Phạm vi áp dụng** | Điều chuyển hàng hóa nội bộ giữa Chi nhánh A (Kho xuất) và Chi nhánh B (Kho nhận) |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Thiết kế Sequence Diagram cho quy trình chuyển kho 2 bước (`Out-bound transit -> In-bound confirmation`). | `INIT-SEQ-03` |

---

## Sơ Đồ Trình Tự Chuyển Kho 2 Bước (`FR-INV-03`)

```mermaid
sequenceDiagram
    autonumber
    actor StaffA as Nhân viên Kho A (Xuất)
    actor StaffB as Nhân viên Kho B (Nhận)
    participant API as StockTransferService
    participant DB as PostgreSQL (Ledger & Balance)

    Note over StaffA, DB: BƯỚC 1: XUẤT KHO VẬN CHUYỂN (OUT-BOUND TRANSIT)
    StaffA->>API: Xác nhận xuất phiếu chuyển kho (From: Branch A, To: Branch B, Items[])
    API->>DB: BEGIN TRANSACTION
    API->>DB: Khóa số dư Kho A (FOR UPDATE) & Kiểm tra Available >= TransferQty
    API->>DB: Giảm OnHand tại Kho A & Ghi Sổ cái InventoryTransaction (Branch A, Type = OUT)
    API->>DB: Cập nhật trạng thái Phiếu chuyển -> InTransit
    API->>DB: COMMIT TRANSACTION
    API-->>StaffA: HTTP 200 OK (Hàng đang trên đường vận chuyển)

    Note over StaffB, DB: BƯỚC 2: XÁC NHẬN NHẬP TẠI KHO ĐẾN (IN-BOUND CONFIRMATION)
    StaffB->>API: Xác nhận đã nhận hàng tại Kho B
    API->>DB: BEGIN TRANSACTION
    API->>DB: Khóa số dư Kho B (FOR UPDATE)
    API->>DB: Tăng OnHand tại Kho B & Cập nhật WAC tại Kho B
    API->>DB: Ghi Sổ cái InventoryTransaction (Branch B, Type = IN, BalanceAfter)
    API->>DB: Cập nhật trạng thái Phiếu chuyển -> Completed
    API->>DB: COMMIT TRANSACTION
    API-->>StaffB: HTTP 200 OK (Hoàn tất chuyển kho 2 bước)
```
