# 01. Hiệu Năng Phản Hồi & Xử Lý Chịu Tải Đồng Thời (Performance & Concurrency)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-NFR-01` |
| **Phiên bản (Version)** | `v1.2.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-19` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `NFR-PERF-01`, `NFR-PERF-02` (Trang 3, 4); `Upgrade 13 / OISMS-66` |
| **Phạm vi áp dụng** | Toàn bộ REST APIs, Database Locking & Flash Sale Concurrency |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả chỉ tiêu thời gian phản hồi API (`p95`) và kịch bản chịu tải đồng thời Flash Sale (`50 reqs / 1 SKU`). | `INIT-NFR-01` |
| `v1.1.0` | `2026-11-06` | Nhóm dự án OISMS | Ghi chú hiện thực: hệ thống dùng khóa bi quan, không dùng khóa lạc quan. | `OISMS-46` |
| `v1.2.0` | `2026-11-19` | Nhóm dự án OISMS | Bổ sung chỉ tiêu thời gian phản hồi cho Báo cáo Tồn kho theo kỳ và Thẻ kho (< 2 giây/trang với sổ kho > 100.000 dòng). Mô hình phân trang SKU trước khi tổng hợp sổ. | `OISMS-66-G2` |

---

## 1. Chỉ Tiêu Thời Gian Phản Hồi API (`NFR-PERF-01`)

| Nhóm API / Tác vụ | Ngưỡng độ trễ bắt buộc (SLA) | Điều kiện đo lường tiêu chuẩn |
| :--- | :---: | :--- |
| **1. Tìm kiếm SKU / Quét Barcode tại POS** (`GET /api/pos/search`, `GET /api/pos/barcode/{code}`) | **`p95 < 200 ms`** | Truy vấn theo chỉ mục `(TenantId, Barcode)` và `(TenantId, SkuCode)`. |
| **2. Tạo đơn hàng / Giao dịch ghi Sổ cái** (`POST /api/inventory/reserve`, `POST /api/pos/checkout`) | **`< 500 ms`** | Bao gồm thời gian mở Transaction, khóa dòng và ghi bản ghi `InventoryTransaction`. |
| **3. Báo cáo doanh thu & lợi nhuận tổng hợp** (`GET /api/reports/revenue-profit`) | **`< 2 giây`** | Trên tập dữ liệu dưới `100,000` bản ghi đơn hàng/sổ cái. |
| **4. Báo cáo Tồn kho theo kỳ (NXT) & Thẻ kho** (`GET /api/reports/inventory-period`, `GET /api/reports/stock-card`) | **`< 2 giây / trang`** | Tập dữ liệu trên `100,000` bản ghi sổ cái; phân trang 50 SKU trước rồi mới tổng hợp sổ cái thông qua chỉ mục `ix_inventory_transactions_sku_history`. |

---

## 2. Xử Lý Đồng Thời & Chống Bán Vượt Trong Flash Sale (`NFR-PERF-02`)

- **Kịch bản kiểm chứng bắt buộc:** `50 đơn hàng trực tuyến đồng thời (50 concurrent online orders)` cùng nhắm vào `1 đơn vị tồn khả dụng cuối cùng (1 remaining available SKU)`.
- **Cơ chế kỹ thuật bắt buộc:**
  - Áp dụng **Khóa bi quan mức cơ sở dữ liệu (`Pessimistic Locking: SELECT ... FOR UPDATE`)** hoặc **Khóa lạc quan (`Optimistic Locking: Version Check`)** kết hợp `PostgreSQL Advisory Lock` khi cập nhật `reserved` và `on_hand`.
  - **Kết quả bắt buộc:** Chỉ đúng **1** yêu cầu thành công (`HTTP 200/201`), **49** yêu cầu còn lại bị từ chối an toàn (`HTTP 409 Conflict`), số lượng `available` tuyệt đối không âm (`available == 0`).

> **Hiện thực (06/11/2026):** hệ thống chọn khóa bi quan (`pg_advisory_xact_lock` theo Tenant/Chi nhánh/SKU và `SELECT ... FOR UPDATE` trên số dư), lấy sau khi mở transaction và theo thứ tự SKU cố định. Không dùng khóa lạc quan `RowVersion`.
>
> **Bằng chứng đo local (21/11/2026 — `OISMS-66-FIN-D` & `OISMS-66-FIN-J`):**
> - `NFR-PERF-01`: đo trên DB riêng `oisms_perf` (`50.000` đơn / `100.000` `order_items`) tại [`evidence/goi-5/NFR-PERF-01/`](../06-testing-and-qa/evidence/goi-5/NFR-PERF-01/2026-11-21-FIN-D.md). `POST /api/orders` đo lại ở [FIN-D-fix1](../06-testing-and-qa/evidence/goi-5/NFR-PERF-01/2026-11-21-FIN-D-fix1.md): ĐẠT (p95 370.42 ms ở c=10).
> - **Tối ưu `revenue-profit` (`OISMS-66-FIN-J`, [`2026-11-21-FIN-J.md`](../06-testing-and-qa/evidence/goi-5/NFR-PERF-01/2026-11-21-FIN-J.md)):** tách projection cột cần dùng, giữ nguyên 100% số liệu (`0` diff JSON). Ca 30 ngày gần nhất là bộ lọc mặc định của màn Báo cáo (`ReportsPage`).
>
> | Ca đo `GET /api/reports/revenue-profit` | Đồng thời | Trước p95 (ms) | Sau p95 (ms) | Kết quả (`< 2000 ms`) |
> | --- | ---: | ---: | ---: | --- |
> | Không lọc | 1 | 2876.27 | 420.02 | **ĐẠT** |
> | Không lọc | 10 | 3537.79 | 863.20 | **ĐẠT** |
> | 30 ngày gần nhất (mặc định màn Báo cáo) | 1 | 1972.49 | 403.86 | **ĐẠT** |
> | 30 ngày gần nhất (mặc định màn Báo cáo) | 10 | 4203.08 | 799.72 | **ĐẠT** |
> | 1 SKU | 1 | 332.24 | 65.96 | **ĐẠT** |
> | 1 SKU | 10 | 280.62 | 132.49 | **ĐẠT** |
> | 20 SKU | 1 | 1888.87 | 367.56 | **ĐẠT** |
> | 20 SKU | 10 | 3514.87 | 5282.51 | **CHƯA ĐẠT** |
>
> **Review 21/11/2026:** ca 20 SKU c=10 chậm do container PostgreSQL chỉ có `/dev/shm` 64 MB: 10 truy vấn song song cùng lúc báo `could not resize shared memory segment`, EF tự retry sau 1–4 s. Tắt truy vấn song song để kiểm chứng thì 20 SKU c=10 đạt p95 1009 ms, 50/50 request ([review](../06-testing-and-qa/evidence/goi-5/NFR-PERF-01/2026-11-21-FIN-J-review.md)). Đã thêm `shm_size: 256mb` cho service `postgres` trong `docker-compose.yml` và đo lại: **15/16 ca ĐẠT** local, báo cáo 8/8 ca dưới 1,2 s (20 SKU c=10 p95 1161 ms, 50/50); còn checkout POS c=10 p95 527 ms do 10 request bán cùng 1 SKU xếp hàng chờ khóa tồn kho ([đo lại](../06-testing-and-qa/evidence/goi-5/NFR-PERF-01/2026-11-21-shm256.md)). Đo thêm 10 quầy bán **các SKU khác nhau**: p95 53–58 ms, **ĐẠT**; ca 10 quầy cùng 1 SKU p95 dao động 379–577 ms, ghi là giới hạn đã biết của khóa chống bán âm ([checkout nhiều SKU](../06-testing-and-qa/evidence/goi-5/NFR-PERF-01/2026-11-21-checkout-multisku.md)). Bản demo AWS cần tạo lại container `postgres` khi deploy để nhận cấu hình.
>
> - `NFR-PERF-02`: chạy lại `AC06_ConcurrencyStressTest_50CreateOrders` trên PostgreSQL 16 local **PASS 1/1** tại [`evidence/goi-5/NFR-PERF-02/`](../06-testing-and-qa/evidence/goi-5/NFR-PERF-02/2026-11-21-FIN-D.md). **CHƯA ĐO TRÊN DEPLOY**.
