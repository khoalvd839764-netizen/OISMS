# 03. Thiết Kế Cơ Sở Dữ Liệu & Sơ Đồ Thực Thể Liên Kết (Database & ERD Design)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-ARC-03` |
| **Phiên bản (Version)** | `v1.1.1` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-08` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-INV-01`, `NFR-TENANT-02`, `NFR-SEC-03`, `3.2.e` (Trang 2, 4, 5) |
| **Phạm vi áp dụng** | PostgreSQL 16+ Schema (`docs/database/oisms_schema.sql` — 47 bảng) |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả sơ đồ ERD cốt lõi, quy tắc Sổ cái Append-Only và chiến lược đánh chỉ mục hiệu năng cao. | `INIT-ARC-03` |
| `v1.1.0` | `2026-11-06` | Nhóm dự án OISMS | Đồng bộ ERD và chỉ mục với `oisms_schema.sql` hiện hành (43 bảng): tên cột thật, kiểu `numeric(18,3)` cho số lượng, giá vốn nằm ở `inventory_balances.average_cost`. | `OISMS-46` |
| `v1.1.1` | `2026-11-08` | Nhóm dự án OISMS | Cập nhật số bảng hiện hành lên 44 sau khi bổ sung `payment_accounts`; giữ mốc 43 bảng trong lịch sử v1.1.0. | `CS-05` |

---

## 1. Sơ Đồ Thực Thể Liên Kết Cốt Lõi (Core Entity Relationship Diagram)

```mermaid
erDiagram
    TENANTS ||--o{ USERS : "sở hữu"
    TENANTS ||--o{ BRANCHES : "quản lý"
    TENANTS ||--o{ PRODUCTS : "danh mục"
    BRANCHES ||--o{ WAREHOUSES : "chứa"
    PRODUCTS ||--o{ SKUS : "biến thể"
    TENANTS ||--o{ UNITS : "danh mục đơn vị"
    UNITS ||--o{ SKUS : "đơn vị gốc"
    SKUS ||--o{ SKU_UNITS : "đơn vị quy đổi"
    UNITS ||--o{ SKU_UNITS : "dùng cho"
    SKU_UNITS ||--o{ SKU_UNIT_FACTOR_HISTORY : "lịch sử hệ số"
    SKUS ||--o{ INVENTORY_BALANCES : "tồn chi nhánh"
    SKUS ||--o{ WAREHOUSE_INVENTORY_BALANCES : "tồn kho"
    SKUS ||--o{ INVENTORY_TRANSACTIONS : "ghi sổ cái"
    SUPPLIERS ||--o{ PURCHASE_ORDERS : "nhận đơn mua"
    PURCHASE_ORDERS ||--o{ PURCHASE_ORDER_ITEMS : "chi tiết đặt"
    PURCHASE_ORDERS o|--o{ GOODS_RECEIPTS : "giao từng đợt"
    PURCHASE_ORDER_ITEMS o|--o{ GOODS_RECEIPT_ITEMS : "đối chiếu lượng nhận"
    GOODS_RECEIPTS ||--o{ GOODS_RECEIPT_ITEMS : "chi tiết nhập"
    SUPPLIERS ||--o{ SUPPLIER_INVOICES : "phát hành"
    GOODS_RECEIPTS ||--o{ SUPPLIER_INVOICES : "lịch sử hóa đơn"
    SUPPLIER_INVOICES ||--o{ SUPPLIER_INVOICE_ITEMS : "chi tiết hóa đơn"
    GOODS_RECEIPT_ITEMS ||--o{ SUPPLIER_INVOICE_ITEMS : "đối chiếu dòng"
    SUPPLIER_INVOICES ||--o| PURCHASE_VARIANCES : "chênh lệch"
    GOODS_RECEIPTS ||--o{ PURCHASE_VARIANCES : "chứng từ nhận hàng"
    ORDERS ||--o{ ORDER_ITEMS : "chi tiết đơn"
    ORDER_ITEMS ||--o{ RESERVATIONS : "giữ chỗ"
    ORDERS ||--o| SALES_INVOICES : "tối đa một hóa đơn bán"

    TENANTS {
        uuid id PK
        varchar name
        boolean is_active
    }
    SKUS {
        uuid id PK
        uuid tenant_id FK
        uuid product_id FK
        varchar code "duy nhất trong tenant"
        varchar barcode
        numeric retail_price "giá bán lẻ niêm yết"
        numeric wholesale_price
        uuid base_unit_id FK "đơn vị gốc"
    }
    SKU_UNITS {
        uuid id PK
        uuid tenant_id FK
        uuid sku_id FK
        uuid unit_id FK
        numeric factor_to_base "Hệ số về đơn vị gốc"
        numeric sale_price
        int version
    }
    INVENTORY_BALANCES {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid sku_id FK
        numeric on_hand "đơn vị gốc"
        numeric reserved "reserved <= on_hand"
        numeric average_cost "Giá vốn WAC tại chi nhánh"
        numeric reorder_point
        bigint last_sequence
    }
    INVENTORY_TRANSACTIONS {
        uuid id PK
        uuid tenant_id FK
        uuid branch_id FK
        uuid warehouse_id FK
        uuid sku_id FK
        varchar type "IN / OUT"
        numeric quantity "> 0"
        numeric unit_cost
        numeric balance_after
        bigint sequence_no
        uuid reference_id "cột sinh từ đúng 1 nguồn chứng từ"
        timestamptz created_at
    }
    SUPPLIER_INVOICES {
        uuid id PK
        uuid tenant_id FK
        uuid goods_receipt_id FK
        uuid supplier_id FK
        text invoice_series
        text invoice_number
        text status
        uuid request_key
    }
    SUPPLIER_INVOICE_ITEMS {
        uuid id PK
        uuid tenant_id FK
        uuid invoice_id FK
        uuid goods_receipt_item_id FK
        uuid sku_id FK
        numeric quantity
        numeric unit_price
    }
    PURCHASE_VARIANCES {
        uuid id PK
        uuid tenant_id FK
        uuid invoice_id FK,UK
        uuid goods_receipt_id FK
        text variance_type
        numeric total_amount_diff
        text status
    }
    SALES_INVOICES {
        uuid id PK
        uuid tenant_id FK
        varchar number UK
        uuid order_id FK
        uuid branch_id FK
        varchar channel "POS/Admin/Shopee/TikTok/Lazada"
        varchar branch_name_snapshot
        varchar customer_name_snapshot
        numeric total_amount
        varchar payment_method_snapshot
        timestamptz issued_at
    }
```

---

Mỗi phiếu nhập có thể lưu nhiều hóa đơn lịch sử và tối đa một hóa đơn còn hiệu lực. Chỉ mục unique `uq_supplier_invoices_active_receipt` loại `Cancelled` và `Rejected`. Dòng hóa đơn đối chiếu với dòng phiếu nhập rồi tới dòng phiếu mua. Các khóa ngoại nghiệp vụ ghép `tenant_id`.

## 2. Chiến Lược Đánh Chỉ Mục Quy Mô Lớn (`NFR-TENANT-02`)

| Bảng dữ liệu | Tên Index / Ràng buộc | Các cột cấu thành | Mục đích tối ưu hóa |
| :--- | :--- | :--- | :--- |
| `skus` | `uq_skus_2` | `(tenant_id, code)` | Mã SKU duy nhất trong cùng tenant. |
| `skus` | `uq_skus_3` | `(tenant_id, barcode)` | Quét barcode tại quầy POS (`NFR-PERF-01`). |
| `sku_units` | `uq_sku_units_2` | `(tenant_id, sku_id, unit_id)` | Mỗi SKU chỉ khai báo một lần cho mỗi đơn vị tính. |
| `inventory_balances` | `uq_inventory_balances_2` | `(tenant_id, branch_id, sku_id)` | Một dòng tồn cho mỗi SKU tại chi nhánh, là điểm khóa `FOR UPDATE`. |
| `inventory_transactions` | `uq_inventory_transactions_2` | `(tenant_id, branch_id, sku_id, sequence_no)` | Thứ tự bút toán liên tục theo SKU tại chi nhánh. |
| `inventory_transactions` | `ix_inventory_transactions_tenant_history` | `(tenant_id, created_at, id)` | Phân trang lịch sử sổ cái toàn tenant (`NFR-TENANT-02`). |
| `inventory_transactions` | `ix_inventory_transactions_sku_history` | `(tenant_id, branch_id, sku_id, created_at, id)` | Thẻ kho theo SKU tại chi nhánh. |
| `inventory_transactions` | `uq_inventory_transactions_3..8` | `(tenant_id, <nguồn chứng từ>)` | Mỗi dòng chứng từ chỉ ghi sổ một lần (chống ghi trùng khi retry). |
| `reservations` | `uq_reservations_2` | `(tenant_id, order_item_id, warehouse_id)` | Chống giữ chỗ lặp cho cùng một dòng đơn tại một kho. |
| `reservations` | `ix_reservations_expiry` | `(status, expires_at, tenant_id)` | Job quét giữ chỗ hết hạn. |
| `sales_invoices` | `uq_sales_invoices_2` | `(tenant_id, number)` | Số hóa đơn duy nhất trong tenant. |
| `sales_invoices` | `uq_sales_invoices_3` | `(tenant_id, order_id)` | Mỗi đơn có tối đa một hóa đơn bán. |
| `sales_invoices` | `ix_sales_invoices_time` | `(tenant_id, issued_at, id)` | Phân trang hóa đơn theo thời gian xuất. |

> **Tham chiếu chi tiết DDL:** Xem toàn bộ 47 bảng và trigger bảo vệ Sổ cái tại [`docs/database/oisms_schema.sql`](../database/oisms_schema.sql) và [`docs/database/README.md`](../database/README.md).

## 3. Quy Tắc Thay Đổi Schema

- Không dùng EF Core migrations. Mỗi thay đổi gồm: một file mới trong `docker/postgres/migrations/` (không sửa file cũ), cập nhật `docs/database/oisms_schema.sql`, thêm bảng vào `RequiredTables` trong `Program.cs` và sửa số bảng kiểm tra trong `docker/postgres/init-oisms.sh`.
- `inventory_transactions` chỉ được `INSERT`; trigger `ledger_append_only` chặn `UPDATE`/`DELETE`. Sửa sai bằng bút toán bù.
- `sales_returns`, `sales_return_items` đã có entity và API (trả hàng CS-01, PR #41). Hai bảng `daily_sales_summaries`, `replenishment_suggestions` có trong schema nhưng backend chưa dùng: báo cáo và gợi ý nhập hàng tính trực tiếp từ `inventory_transactions`, `orders` và `inventory_balances`. Trợ lý OISMS (FR-AI-02) không thêm bảng.
