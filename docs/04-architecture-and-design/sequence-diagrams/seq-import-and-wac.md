# Sơ Đồ Trình Tự 04: Xác Nhận Nhập Kho & Tính Tự Động Giá Vốn Bình Quân WAC (Purchase Receipt & WAC Sequence)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SEQ-04` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-INV-01`, `FR-INV-02`, `FR-COST-01`, `NFR-SEC-02..03` (Trang 2, 4) |
| **Phạm vi áp dụng** | `GoodsReceiptService.cs`, `InventoryLedgerService.cs` & `POST /api/inventory/goods-receipts/{id}/confirm` |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Thiết kế Sequence Diagram cho luồng xác nhận phiếu nhập kho nguyên tử, ghi sổ cái Append-Only và tính lại giá vốn WAC. | `INIT-SEQ-04` |

---

## Sơ Đồ Trình Tự Xác Nhận Nhập Kho & Cập Nhật WAC (`FR-INV-02`, `FR-COST-01`)

```mermaid
sequenceDiagram
    autonumber
    actor Staff as Owner / Staff
    participant UI as Web Admin Dashboard
    participant API as GoodsReceiptsController
    participant Svc as GoodsReceiptService
    participant DB as PostgreSQL (ACID Transaction)

    Staff->>UI: Bấm "Xác nhận nhập kho" trên phiếu Draft
    UI->>API: POST /api/inventory/goods-receipts/{id}/confirm
    API->>Svc: ConfirmGoodsReceiptAsync(id)
    Svc->>DB: BEGIN TRANSACTION
    Svc->>DB: SELECT * FROM goods_receipts WHERE id = @id FOR UPDATE
    alt Phiếu không ở trạng thái Draft (Đã Confirmed hoặc Cancelled)
        DB-->>Svc: Trạng thái không hợp lệ
        Svc->>DB: ROLLBACK TRANSACTION
        Svc-->>UI: HTTP 409 Conflict (Chặn xác nhận lặp 2 lần)
    else Phiếu hợp lệ (Status == Draft)
        loop Duyệt từng dòng sản phẩm (GoodsReceiptItem: SkuId, ImportQty, ImportPrice)
            Svc->>DB: Khóa dòng tồn kho (SELECT ... FOR UPDATE)
            Svc->>Svc: Tính WAC mới: NewCost = (OldStock*OldCost + ImportQty*ImportPrice) / (OldStock + ImportQty)
            Svc->>DB: Cập nhật OnHand = OldStock + ImportQty & UnitCost = NewCost
            Svc->>DB: INSERT INTO inventory_transactions (Type = IN, Quantity = ImportQty, BalanceAfter = NewOnHand)
        end
        Svc->>DB: Cập nhật GoodsReceipt.Status = Confirmed, ConfirmedAt = UtcNow
        Svc->>DB: COMMIT TRANSACTION
        Svc-->>UI: HTTP 200 OK (Trả về phiếu đã xác nhận & giá vốn WAC mới)
    end
```
