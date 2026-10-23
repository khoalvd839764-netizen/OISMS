# Sơ Đồ Trình Tự 01: Thanh Toán Nhanh Nguyên Tử Tại Quầy POS (Fast POS Checkout Sequence)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SEQ-01` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-POS-04`, `FR-COST-02`, `NFR-SEC-02`, `3.2.e` (Trang 2, 3, 4, 5) |
| **Phạm vi áp dụng** | Luồng thanh toán đơn hàng tại quầy POS (`Reserve -> Confirm -> Deduct`) |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Thiết kế Sequence Diagram chuẩn UML 2.0 cho luồng thanh toán nhanh nguyên tử tại quầy POS. | `INIT-SEQ-01` |

---

## Sơ Đồ Trình Tự (`Reserve -> Confirm -> Deduct` in Single Atomic Transaction)

```mermaid
sequenceDiagram
    autonumber
    actor Cashier as Thu ngân (Cashier)
    participant POS as React PWA POS
    participant API as PosCheckoutController
    participant Svc as Order & Inventory Service
    participant DB as PostgreSQL (ACID Transaction)

    Cashier->>POS: Quét Barcode / Chọn SKU & Bấm Thanh toán (Cash/QR)
    POS->>API: POST /api/pos/checkout (BranchId, Items[], PaymentMethod, IdempotencyKey)
    API->>DB: BEGIN TRANSACTION
    API->>Svc: Thực thi chuỗi nguyên tử: Create Order -> Reserve -> Confirm -> Deduct
    Svc->>DB: SELECT * FROM inventory_balances WHERE sku_id IN (...) FOR UPDATE (Khóa dòng bi quan)
    alt Có bất kỳ SKU nào có available (on_hand - reserved) < Quantity
        DB-->>API: Không đủ tồn khả dụng
        API->>DB: ROLLBACK TRANSACTION
        API-->>POS: HTTP 409 Conflict (Thông báo rõ SKU thiếu tồn)
    else Tất cả SKU đều đủ tồn khả dụng
        Svc->>DB: 1. INSERT Order (Status = Confirmed/Completed) & OrderItems (Snapshot CostPrice = current WAC)
        Svc->>DB: 2. Thực hiện Reserve -> Confirm -> Trừ trực tiếp on_hand thông qua cơ chế Ledger
        Svc->>DB: 3. INSERT InventoryTransaction (Type = OUT, Quantity, BalanceAfter, ReferenceId = OrderId)
        API->>DB: COMMIT TRANSACTION
        API-->>POS: HTTP 200 OK (Trả về thông tin Hóa đơn & Số dư mới)
        POS->>Cashier: Kích hoạt Browser Print API (window.print() khổ 80mm/58mm)
    end
```
