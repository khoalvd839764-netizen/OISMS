# FR-05: Cơ Chế Giữ Chỗ Tồn Kho & Chống Bán Vượt (Anti-Oversell & Stock Reservation)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-05` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-RSE-01..03`, `NFR-PERF-02`, `NFR-SEC-02` (Trang 2, 3, 4) |
| **Gói công việc (Task Package)** | Task Package 3 — Centralized Order Hub & Anti-Oversell Engine |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả chi tiết cơ chế tính tồn khả dụng, giữ chỗ, trừ tồn/giải phóng tồn và khóa CSDL chống bán vượt. | `INIT-FR-05` |

---

## 1. Bảng Đặc Tả Chi Tiết Yêu Cầu (`FR-RSE`)

| Mã yêu cầu | Tên yêu cầu | Mô tả nghiệp vụ & Quy tắc kỹ thuật | Tiêu chí nghiệm thu (Acceptance Criteria) |
| :---: | :--- | :--- | :--- |
| **`FR-RSE-01`** | **Available Stock Calculation** | Duy trì động số lượng tồn khả dụng theo từng SKU tại mỗi chi nhánh/kho theo công thức: `available = on_hand - reserved`. | - Mọi API tra cứu tồn kho và POS đều hiển thị chính xác `Available = OnHand - Reserved`.<br/>- `Available` không bao giờ âm (`< 0`). |
| **`FR-RSE-02`** | **Stock Reservation Execution** | Tăng `reserved` khi đơn hàng online đi vào Order Hub ở trạng thái `Reserved`. Từ chối đơn hàng ngay lập tức nếu `available < Purchased Quantity`. | - Hỗ trợ cơ chế **Idempotency** (`Idempotency-Key` / `RequestKey` + `(OrderId, OrderItemId)`) chống giữ chỗ trùng lặp khi client retry.<br/>- Chặn giữ chỗ nếu Product/SKU/Branch/Warehouse đã ngừng hoạt động (`IsActive = false`). |
| **`FR-RSE-03`** | **Deduct/Release Execution** | - **Khi `Confirmed`:** Trừ `on_hand`, giảm `reserved` tương ứng và ghi nhận giao dịch Sổ cái (`OUT`).<br/>- **Khi `Cancelled`:** Giảm `reserved` để giải phóng hàng quay về quỹ `available`. | - Thực thi nguyên tử trong Database Transaction.<br/>- Vượt qua bài kiểm thử đồng thời 50 requests tranh mua 1 đơn vị tồn khả dụng cuối cùng (`NFR-PERF-02`). |
