# 05. ĐẶC TẢ TRUNG TÂM ĐƠN HÀNG ĐA KÊNH (OMNICHANNEL ORDERS SCREENS)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SCR-05` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực** | `2026-10-25` |
| **Đường dẫn URL Routes** | `/admin/orders`, `/orders` |
| **Quyền truy cập (RBAC)** | `Owner`, `Staff` |
| **Source code Frontend** | [`OrdersPage.tsx`](file:///d:/OISMS/src/frontend/admin-dashboard/src/pages/OrdersPage.tsx) |
| **Controllers Backend** | [`OrdersController.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/OrdersController.cs) |
| **Đặc tả Máy trạng thái** | [`../04-architecture-and-design/04-state-machines.md`](../04-architecture-and-design/04-state-machines.md) |

---

## 1. Mục Tiêu & Luồng Nghiệp Vụ

Trung tâm **Đơn hàng Đa kênh (Omnichannel Order Hub)** là nơi hội tụ toàn bộ đơn hàng phát sinh từ mọi điểm chạm bán lẻ của doanh nghiệp:
* Hợp nhất đơn hàng từ các kênh: Bán lẻ tại quầy (`POS`), Đặt hàng thủ công tại tổng bộ (`Admin`), và các sàn Thương mại điện tử (`Shopee`, `TikTok Shop`, `Lazada`).
* Quản lý vòng đời trạng thái đơn hàng nghiêm ngặt theo máy trạng thái hữu hạn (Finite State Machine):
  $$\text{Draft} \longrightarrow \text{Reserved (Giữ chỗ 15p)} \longrightarrow \text{Confirmed (Duyệt/Trừ kho)} \longrightarrow \text{Shipped} \longrightarrow \text{Completed}$$
  $$\text{Draft / Reserved} \longrightarrow \text{Cancelled (Hoàn tồn khả dụng)}$$
* Duyệt đơn và Hoàn tất đơn nguyên tử:
  * Khi bấm **`Duyệt đơn (Confirm)`**: Trừ `OnHand`, giảm `Reserved`, ghi Sổ cái `OUT`, và chụp giá vốn bình quân hiện hành vào `OrderItem.CostPrice`.
  * Khi bấm **`Hoàn tất (Complete)`**: Ghi nhận khách đã nhận hàng thành công. Tuyệt đối **không trừ tồn kho hay ghi sổ cái lần thứ hai**.

---

## 2. Bố Cục Giao Diện Wireframe (ASCII Wireframe)

```text
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ BREADCRUMB: Quản trị bán hàng & đơn hàng                                                       │
│ Trung Tâm Đơn Hàng Hợp Nhất Đa Kênh                    [ Giả lập Webhook Sàn ] [+ Tạo Đơn Admin]│
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ THANH TÌM KIẾM & BỘ LỌC KÊNH BÁN:                                                              │
│ [ 🔍 Mã đơn, Tên khách, SĐT... ] [ Kênh: Tất cả ▼ ] [ Trạng thái: Đang giữ chỗ ▼ ] [ Chi nhánh ▼]│
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ BẢNG DANH SÁCH ĐƠN HÀNG HỢP NHẤT:                                                              │
│ Mã đơn hàng   | Kênh bán | Khách hàng       | Thời gian     | Tổng tiền   | Trạng thái | Thao tác  │
│ ──────────────┼──────────┼──────────────────┼───────────────┼─────────────┼────────────┼────────── │
│ ORD-2026102501| Shopee   | tranvanb_hcm     | 25/10 14:15:20| 1,250,000 đ | 🟡 Reserved| [Xem/Duyệt]│
│ ORD-2026102502| POS      | Khách lẻ tại quầy| 25/10 14:22:05| 750,000 đ   | 🟢 Completed[In Bill]  │
│ ORD-2026102503| TikTok   | linh_danang      | 25/10 13:40:11| 480,000 đ   | 🔵 Confirmed[Giao vận] │
│ ORD-2026102504| Admin    | Cty May Mặc Á Châu| 25/10 11:10:00| 15,600,000 đ| 🔴 Cancelled[Chi tiết] │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ DRAWER CHI TIẾT ĐƠN HÀNG ĐANG CHỌN (SIDE PANEL):                                               │
│ ┌────────────────────────────────────────────────────────────────────────────────────────────┐ │
│ │ Chi Tiết Đơn Hàng: #ORD-2026102501 (Shopee)                         Trạng thái: 🟡 Reserved │ │
│ │ Khách hàng: Trần Văn B - 0912.345.678 - Q.Bình Thạnh, TP.HCM                               │ │
│ │ ────────────────────────────────────────────────────────────────────────────────────────── │ │
│ │ Danh sách sản phẩm mua:                                                                    │ │
│ │ • Áo Polo Trắng - L   | x2 | Đơn giá: 250,000đ | Tồn khả dụng: 12 | Giá vốn WAC: 120,000đ  │ │
│ │ • Quần Jean Slim - 31 | x1 | Đơn giá: 480,000đ | Tồn khả dụng: 5  | Giá vốn WAC: 210,000đ  │ │
│ │ ────────────────────────────────────────────────────────────────────────────────────────── │ │
│ │ Tổng tiền hàng: 980,000 đ  | Phí vận chuyển: 30,000 đ | Tổng thanh toán: 1,010,000 đ        │ │
│ │ Thời gian giữ chỗ: Còn 11 phút 30 giây (Tự động hủy nếu không duyệt)                       │ │
│ │ ────────────────────────────────────────────────────────────────────────────────────────── │ │
│ │ [ HỦY ĐƠN HÀNG (Hoàn tồn) ]             [ DUYỆT ĐƠN HÀNG (Xuất kho & Ghi sổ cái OUT) ]     │ │
│ └────────────────────────────────────────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Modal Tạo Đơn Hàng Admin Thủ Công

Khi bấm nút **`[+ Tạo Đơn Admin]`**, nhân viên có thể lập đơn bán buôn hoặc bán sỉ:
* **Chi nhánh & Kho:** Chọn Chi nhánh bán và Kho xuất hàng tương ứng.
* **Thông tin khách hàng:** Tên khách hàng, Số điện thoại (chuẩn hóa `E.164`), Địa chỉ giao hàng.
* **Danh sách mặt hàng:** Tìm SKU, hiển thị tồn khả dụng thực tế để tránh chọn vượt tồn.
* **Khóa chống gửi trùng (`RequestKey`):** Frontend tự động sinh GUID chuẩn gán vào payload và header `Idempotency-Key`.

---

## 4. Bảng Hàm API Backend & Tài Liệu Tham Chiếu

| Method | Endpoint Backend | Hàm xử lý Controller / Service | Tài liệu Hợp đồng API tham chiếu | Mã lỗi liên quan |
| :---: | :--- | :--- | :--- | :--- |
| `GET` | `/api/orders` | `OrdersController.GetOrders` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#1-danh-mục-endpoints-đơn-hàng-đa-kênh--webhook-fr-ord-0103-fr-sim-01) | `AUTH_UNAUTHORIZED` (Xem [`DOC-ERR-02`](../12-error-and-status-codes/02-error-codes-catalog.md)) |
| `GET` | `/api/orders/{id}` | `OrdersController.GetOrderById` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#1-danh-mục-endpoints-đơn-hàng-đa-kênh--webhook-fr-ord-0103-fr-sim-01) | `ORDER_NOT_FOUND` |
| `POST` | `/api/orders` | `OrdersController.CreateAdminOrder` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#1-danh-mục-endpoints-đơn-hàng-đa-kênh--webhook-fr-ord-0103-fr-sim-01) | `VALIDATION_ERROR`, `INSUFFICIENT_STOCK_FOR_RESERVATION` |
| `POST` | `/api/orders/{id}/confirm` | `OrdersController.ConfirmOrder` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#1-danh-mục-endpoints-đơn-hàng-đa-kênh--webhook-fr-ord-0103-fr-sim-01) | `INVALID_STATE_TRANSITION`, `RESERVATION_EXPIRED` |
| `POST` | `/api/orders/{id}/complete` | `OrdersController.CompleteOrder` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#1-danh-mục-endpoints-đơn-hàng-đa-kênh--webhook-fr-ord-0103-fr-sim-01) | `INVALID_STATE_TRANSITION` |
| `POST` | `/api/orders/{id}/cancel` | `OrdersController.CancelOrder` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#1-danh-mục-endpoints-đơn-hàng-đa-kênh--webhook-fr-ord-0103-fr-sim-01) | `INVALID_STATE_TRANSITION` |
| `POST` | `/api/webhooks/simulator/{channel}` | `OrdersController.SimulateWebhook` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#1-danh-mục-endpoints-đơn-hàng-đa-kênh--webhook-fr-ord-0103-fr-sim-01) | `VALIDATION_ERROR`, `BRANCH_INACTIVE_OR_INVALID` |

---

## 5. Quy Chuẩn Màu Sắc Trạng Thái Đơn Hàng (Status Badges)

* `Reserved` (Đang giữ chỗ tồn kho): Nền `--warning-soft: #fff5dc`, Viền `--warning: #b76e00`, Chữ `#7a4900`.
* `Confirmed` (Đã duyệt xuất kho): Nền `--info-soft: #edf3ff`, Viền `--info: #2768d8`, Chữ `#1e40af`.
* `Shipped` (Đang giao hàng): Nền `#e0f2fe`, Viền `#0284c7`, Chữ `#0369a1`.
* `Completed` (Hoàn tất thành công): Nền `--success-soft: #e8f7f0`, Viền `--success: #16845b`, Chữ `#0d543a`.
* `Cancelled` (Đã hủy / Hoàn tồn): Nền `--danger-soft: #fff0f1`, Viền `--danger: #c93c48`, Chữ `#8d3540`.
*(Chi tiết định nghĩa Design Tokens xem tại [`DOC-ERR-03`](../12-error-and-status-codes/03-color-system-tokens.md)).*
