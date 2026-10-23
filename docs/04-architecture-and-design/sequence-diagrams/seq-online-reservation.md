# Sơ Đồ Trình Tự 02: Giữ Chỗ Tồn Kho & Chống Bán Vượt Đơn Online (Stock Reservation & Anti-Oversell Sequence)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SEQ-02` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-RSE-01..03`, `NFR-PERF-02`, `3.2.e` (Trang 2, 3, 4, 5) |
| **Phạm vi áp dụng** | `ReservationService.cs`, `POST /api/inventory/reserve` & Webhook Ingestion |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Thiết kế Sequence Diagram cho cơ chế khóa CSDL chống bán vượt và bảo vệ Idempotency khi giữ chỗ. | `INIT-SEQ-02` |

---

## Sơ Đồ Trình Tự Giữ Chỗ Chịu Tải Cao (Flash Sale 50 Concurrent Requests)

```mermaid
sequenceDiagram
    autonumber
    participant Client as Webhook / Admin / POS
    participant API as InventoryController
    participant Svc as ReservationService
    participant DB as PostgreSQL (Advisory & Row Lock)

    Client->>API: POST /api/inventory/reserve (SkuId, WarehouseId, Quantity, OrderId, RequestKey)
    API->>Svc: ReserveStockAsync(request)
    Svc->>DB: BEGIN TRANSACTION
    Svc->>DB: SELECT pg_advisory_xact_lock(hash(TenantId + RequestKey / OrderItemId))
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
            Svc->>DB: INSERT INTO reservations (Status = Active, ExpiresAt = +15 mins)
            Svc->>DB: COMMIT TRANSACTION
            Svc-->>Client: HTTP 200 OK (Trả về ReservationId & Available mới)
        end
    end
```
