# 01. Hiệu Năng Phản Hồi & Xử Lý Chịu Tải Đồng Thời (Performance & Concurrency)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-NFR-01` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `NFR-PERF-01`, `NFR-PERF-02` (Trang 3, 4) |
| **Phạm vi áp dụng** | Toàn bộ REST APIs, Database Locking & Flash Sale Concurrency |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả chỉ tiêu thời gian phản hồi API (`p95`) và kịch bản chịu tải đồng thời Flash Sale (`50 reqs / 1 SKU`). | `INIT-NFR-01` |

---

## 1. Chỉ Tiêu Thời Gian Phản Hồi API (`NFR-PERF-01`)

| Nhóm API / Tác vụ | Ngưỡng độ trễ bắt buộc (SLA) | Điều kiện đo lường tiêu chuẩn |
| :--- | :---: | :--- |
| **1. Tìm kiếm SKU / Quét Barcode tại POS** (`GET /api/pos/search`, `GET /api/pos/barcode/{code}`) | **`p95 < 200 ms`** | Truy vấn theo chỉ mục `(TenantId, Barcode)` và `(TenantId, SkuCode)`. |
| **2. Tạo đơn hàng / Giao dịch ghi Sổ cái** (`POST /api/inventory/reserve`, `POST /api/pos/checkout`) | **`< 500 ms`** | Bao gồm thời gian mở Transaction, khóa dòng và ghi bản ghi `InventoryTransaction`. |
| **3. Báo cáo doanh thu & lợi nhuận tổng hợp** (`GET /api/reports/revenue-profit`) | **`< 2 giây`** | Trên tập dữ liệu dưới `100,000` bản ghi đơn hàng/sổ cái. |

---

## 2. Xử Lý Đồng Thời & Chống Bán Vượt Trong Flash Sale (`NFR-PERF-02`)

- **Kịch bản kiểm chứng bắt buộc:** `50 đơn hàng trực tuyến đồng thời (50 concurrent online orders)` cùng nhắm vào `1 đơn vị tồn khả dụng cuối cùng (1 remaining available SKU)`.
- **Cơ chế kỹ thuật bắt buộc:**
  - Áp dụng **Khóa bi quan mức cơ sở dữ liệu (`Pessimistic Locking: SELECT ... FOR UPDATE`)** hoặc **Khóa lạc quan (`Optimistic Locking: Version Check`)** kết hợp `PostgreSQL Advisory Lock` khi cập nhật `reserved` và `on_hand`.
  - **Kết quả bắt buộc:** Chỉ đúng **1** yêu cầu thành công (`HTTP 200/201`), **49** yêu cầu còn lại bị từ chối an toàn (`HTTP 409 Conflict`), số lượng `available` tuyệt đối không âm (`available == 0`).
