# 03. Kịch Bản Kiểm Thử Chịu Tải Đồng Thời Flash Sale (Concurrency & Stress Testing)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-QA-03` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `NFR-PERF-02`, `FR-RSE-02`, `3.2.e` (Trang 2, 3, 4, 5) |
| **Phạm vi áp dụng** | Kiểm thử tranh chấp giữ chỗ tồn kho (`50 Concurrent Orders -> 1 Available SKU`) |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả kịch bản kiểm thử đồng thời chống bán vượt (Flash Sale Stress Test) và kiểm thử Idempotency. | `INIT-QA-03` |

---

## 1. Kịch Bản 1: 50 Đơn Hàng Đồng Thời Tranh 1 Đơn Vị Tồn Cuối Cùng (`NFR-PERF-02`)

- **Thiết lập ban đầu (Pre-condition):**
  - Khởi tạo 1 SKU có số lượng tồn vật lý `OnHand = 1`, `Reserved = 0` $\rightarrow$ `Available = 1`.
- **Thao tác thực thi (Action):**
  - Phóng đồng thời `50` luồng (`Task.WhenAll` với 50 `OrderId` khác nhau) cùng gọi vào `ReservationService.ReserveStockAsync` / `POST /api/inventory/reserve` với `Quantity = 1`.
- **Kết quả mong đợi (Expected Result):**
  1. Đúng **1** request nhận phản hồi thành công (`HTTP 200 OK`).
  2. Đúng **49** requests còn lại nhận mã từ chối nghiệp vụ (`HTTP 409 Conflict — Không đủ tồn kho khả dụng`).
  3. Không có bất kỳ request nào trả về lỗi `HTTP 500` hoặc gây Deadlock treo hệ thống.
  4. Sau khi 50 luồng kết thúc: `OnHand = 1`, `Reserved = 1`, `Available = 0` (Tuyệt đối không âm).

---

## 2. Kịch Bản 2: Gửi Lặp Cùng Một Yêu Cầu Giữ Chỗ Do Lỗi Mạng (Idempotent Retry)

- **Thao tác thực thi:** Gửi đồng thời hoặc liên tiếp nhiều request có cùng `Idempotency-Key` / `RequestKey` hoặc cùng cặp `(OrderId, OrderItemId, WarehouseId, SkuId, Quantity)`.
- **Kết quả mong đợi:** Hệ thống chỉ tăng `Reserved` đúng **1 lần duy nhất**, các lần gọi lặp trả về lại đúng `ReservationId` ban đầu với mã `HTTP 200 OK`.
