# Sơ Đồ Trình Tự 02: Giữ Chỗ Tồn Kho & Chống Bán Vượt Đơn Online (Stock Reservation & Anti-Oversell Sequence)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SEQ-02` |
| **Phiên bản (Version)** | `v1.2.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-19` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-RSE-01..03`, `NFR-PERF-02`, `3.2.e` (Trang 2, 3, 4, 5) |
| **Phạm vi áp dụng** | `ReservationService.cs`, `OrderService.ConfirmOrderAsync`, `POST /api/inventory/reserve` & Webhook Ingestion |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Thiết kế Sequence Diagram cho cơ chế khóa CSDL chống bán vượt và bảo vệ Idempotency khi giữ chỗ. | `INIT-SEQ-02` |
| `v1.1.0` | `2026-11-06` | Nhóm dự án OISMS | Đồng bộ khóa advisory theo (Tenant, Branch, SKU) và theo RequestKey; thời hạn giữ chỗ mặc định 15 phút do `DurationMinutes`. | `OISMS-46` |
| `v1.2.0` | `2026-11-19` | Nhóm dự án OISMS | Bổ sung luồng xác nhận đơn online (`Reserved -> Confirmed`): tiêu thụ giữ chỗ, ghi sổ `OUT`, lập `SalesInvoice` và lập Phiếu xuất kho 02-VT (`GoodsIssue` + `GoodsIssueItem` theo từng kho xuất) trong cùng transaction. | `OISMS-66-L2-G3` |
---

## Sơ Đồ Trình Tự Giữ Chỗ Chịu Tải Cao (Flash Sale 50 Concurrent Requests)

```mermaid
sequenceDiagram
    autonumber
    participant Client as Webhook / Admin / POS
    participant API as InventoryController
    participant Svc as ReservationService
    participant DB as PostgreSQL (Advisory & Row Lock)

    Client->>API: POST /api/inventory/reserve (OrderItemId, SkuId, BranchId, WarehouseId, Quantity, DurationMinutes, RequestKey)
    API->>Svc: ReserveStockAsync(request)
    Svc->>DB: BEGIN TRANSACTION
    Svc->>DB: SELECT pg_advisory_xact_lock(hashtextextended(Tenant/Branch/SKU)) và khóa theo RequestKey
    Svc->>DB: Kiểm tra Idempotency: Đã có Reservation Active cho cùng RequestKey / OrderItemId?
    alt Đã tồn tại Reservation khớp thông số (Retry do mạng)
        DB-->>Svc: Trả về bản ghi Reservation cũ
        Svc->>DB: COMMIT TRANSACTION
        Svc-->>Client: HTTP 200 OK (Idempotent Response - Không cộng dồn Reserved lần 2)
    else Yêu cầu mới hoàn toàn
        Svc->>DB: Kiểm tra IsActive của Branch, Warehouse, Product, SKU
        Svc->>DB: SELECT * FROM warehouse_inventory_balances & inventory_balances FOR UPDATE
        alt Available (OnHand - Reserved) < Quantity
            DB-->>Svc: Tồn khả dụng không đủ
            Svc->>DB: ROLLBACK TRANSACTION
            Svc-->>Client: HTTP 409 Conflict (Chặn bán vượt thành công)
        else Available >= Quantity
            Svc->>DB: Tăng Reserved ở cả 2 cấp (Warehouse & Branch)
            Svc->>DB: INSERT INTO reservations (Status = Active, ExpiresAt = now + DurationMinutes, mặc định 15)
            Svc->>DB: COMMIT TRANSACTION
            Svc-->>Client: HTTP 200 OK (Trả về ReservationId & Available mới)
        end
    end
```

## Sơ Đồ Trình Tự Xác Nhận Đơn Hàng Online & Lập Phiếu Xuất Kho 02-VT (`Reserved -> Confirmed`)

```mermaid
sequenceDiagram
    autonumber
    actor Staff as Nhân viên / Owner
    participant Admin as Admin Dashboard
    participant API as OrdersController (/api/orders/{id}/confirm)
    participant Svc as OrderService
    participant DB as PostgreSQL (ACID Transaction)

    Staff->>Admin: Bấm Xác nhận đơn hàng đang Reserved
    Admin->>API: POST /api/orders/{id}/confirm
    API->>Svc: ConfirmOrderAsync(orderId)
    Svc->>DB: BEGIN TRANSACTION + Khóa tồn kho theo thứ tự chuẩn (không thêm khóa mới)
    Svc->>DB: 1. Chuyển Reservations từ Active -> Consumed, giảm Reserved và trừ OnHand
    Svc->>DB: 2. Chụp OrderItem.CostPrice = WAC hiện tại, chuyển Order -> Confirmed
    Svc->>DB: 3. INSERT InventoryTransaction (Type = OUT, Quantity = BaseQuantity, UnitCost = WAC, OrderItemId)
    Svc->>DB: 4. INSERT SalesInvoice (số INV-...)
    Svc->>DB: 5. Nhóm dòng OUT theo WarehouseId tăng dần -> INSERT GoodsIssue (PXK-<số HĐ> hoặc PXK-<số HĐ>-1, -2...) & GoodsIssueItem (1-1 với dòng OUT)
    Svc->>DB: COMMIT TRANSACTION, phát SignalR sau commit
    API-->>Admin: HTTP 200 OK (OrderDto kèm thông tin hóa đơn và phiếu xuất kho)
```
