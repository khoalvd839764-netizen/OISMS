# 04. Đặc Tả Hợp Đồng API Đơn Hàng Đa Kênh, Webhook & Báo Cáo (Order, Webhook & Report Endpoints)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-API-04` |
| **Phiên bản (Version)** | `v1.0.2` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-24` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-ORD-01..03`, `FR-SIM-01`, `FR-REP-01..03`, `FR-AI` (Trang 2, 3, 6) |
| **Hợp đồng chi tiết Sprint 3** | [`../08-sprints/sprint-3/01-api-contracts-sprint-3.md`](../08-sprints/sprint-3/01-api-contracts-sprint-3.md) |
| **Phạm vi áp dụng** | Trung tâm Đơn hàng (`Orders`), Trình giả lập Webhook (`Simulator`), Báo cáo (`Reports`) & Dự báo AI |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.2` | `2026-10-24` | Đăng Khoa (Backend 1) | Bổ sung thao tác hoàn tất đơn hàng online `POST /api/orders/{id}/complete` (`Confirmed -> Completed`), cập nhật link tương đối và quy tắc bảo mật webhook/idempotency. | `SYNC-S3-COMPLETE-ORD` |
| `v1.0.1` | `2026-10-24` | Đăng Khoa (Backend 1) | Cập nhật hợp đồng chi tiết Sprint 3: Bổ sung endpoint alias `POST /api/orders/checkout`, chi tiết máy trạng thái `Order State Machine` và liên kết tới `DOC-CTR-S3-01`. | `SYNC-S3-ORDERS` |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả hợp đồng API cho Trung tâm Đơn hàng đa kênh, Webhook Simulator, Báo cáo lãi gộp và Gợi ý AI. | `INIT-API-04` |

---

## 1. Danh Mục Endpoints Đơn Hàng Đa Kênh & Webhook (`FR-ORD-01..03`, `FR-SIM-01`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `GET` | `/api/orders` | `Owner, Staff` | `FR-ORD-03` | Lấy danh sách đơn hàng hợp nhất (`Canonical Orders`), lọc theo Trạng thái, Kênh bán, Chi nhánh và Thời gian. |
| `GET` | `/api/orders/{id}` | `Owner, Staff` | `FR-ORD-03` | Lấy chi tiết đơn hàng kèm danh sách mặt hàng, giá vốn WAC snapshot và thông tin thanh toán. |
| `POST` | `/api/orders` | `Owner, Staff` | `FR-ORD-01..02` | Tạo đơn hàng thủ công trên Admin và thực hiện giữ chỗ tồn kho (`Draft -> Reserved`). Bắt buộc `RequestKey` hợp lệ do client cung cấp. |
| `POST` | `/api/orders/{id}/confirm` | `Owner, Staff` | `FR-ORD-02..03`, `FR-RSE-03`, `FR-COST-02` | Duyệt đơn hàng (`Reserved -> Confirmed`): Trừ `on_hand`, giảm `reserved`, ghi Sổ cái `OUT` và chụp `OrderItem.CostPrice`. |
| `POST` | `/api/orders/{id}/complete` | `Owner, Staff` | `FR-ORD-02..03` | **Hoàn tất đơn online (`Confirmed -> Completed`):** Ghi nhận khách đã nhận hàng thành công. **Tuyệt đối KHÔNG trừ tồn kho hay ghi ledger OUT lần thứ hai** (vì đã ghi khi Confirm). Gọi lặp lại trên đơn đã `Completed` là idempotent (trả về 200 OK). Đơn chưa Confirmed chuyển sang Completed trả `409 Conflict` (`INVALID_STATE_TRANSITION`). |
| `POST` | `/api/orders/{id}/cancel` | `Owner, Staff` | `FR-ORD-02..03`, `FR-RSE-03` | Hủy đơn hàng (`Reserved -> Cancelled`): Giải phóng `reserved` về lại quỹ `available`. |
| `POST` | `/api/orders/checkout`<br/>*(Canonical: `/api/pos/checkout`)* | `Owner, Staff, Cashier` | `FR-POS-03`, `FR-POS-04`, `FR-COST-02` | Endpoint alias tương thích ngược phục vụ Web POS PWA và Admin POS, trỏ vào cùng lõi `IOrderService.CheckoutPosAsync`. Trả về `PosReceiptDto` không lộ giá vốn. |
| `POST` | `/api/webhooks/simulator/{channel}` | `Owner, Staff / Webhook Key` | `FR-SIM-01`, `FR-ORD-01` | Tiếp nhận đơn TMĐT (Shopee, TikTok, Lazada). Xác thực qua Header/Secret (không tin body TenantId); ánh xạ shop sang chi nhánh fulfillment; khóa chống trùng `(tenant_id, channel, external_shop_id, external_order_id)`. |

---

## 2. Danh Mục Endpoints Báo Cáo & Dự Báo AI (`FR-REP-01..03`, `FR-AI-01`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `GET` | `/api/reports/revenue-profit` | `Owner` | `FR-REP-01` | Báo cáo Doanh thu thuần, Tổng giá vốn (`COGS`) và Lợi nhuận gộp (`Gross Profit = Revenue - COGS`). |
| `GET` | `/api/reports/inventory-velocity` | `Owner, Staff` | `FR-REP-02` | Báo cáo giá trị tồn kho theo giá vốn, top hàng bán chạy và hàng luân chuyển chậm. |
| `GET` | `/api/reports/stock-alerts` | `Owner, Staff` | `FR-REP-03` | Danh sách cảnh báo các SKU có `available <= MinStockLevel`. |
| `GET` | `/api/ai/reorder-recommendations` | `Owner, Staff` | `FR-AI-01` | Dự báo nhu cầu tiêu thụ và gợi ý số lượng đặt hàng nhập kho theo từng SKU. |
