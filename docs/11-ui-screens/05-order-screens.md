# 05. ĐẶC TẢ TRUNG TÂM ĐƠN HÀNG ĐA KÊNH (OMNICHANNEL ORDERS SCREENS)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SCR-05` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực** | `2026-10-25` |
| **Đường dẫn URL Routes** | `/admin/orders`, `/orders` |
| **Quyền truy cập (RBAC)** | `Owner`, `Staff` |
| **Source code Frontend** | [`OrdersPage.tsx`](../../src/frontend/admin-dashboard/src/pages/OrdersPage.tsx) |
| **Controllers Backend** | [`OrdersController.cs`](../../src/backend/src/OISM.WebApi/Controllers/OrdersController.cs) |
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

---

## 6. Đặc Tả Giao Diện Quản Lý Trả Hàng & Hoàn Tiền (Sales Returns - OISMS-58-CS01)

| Thông tin màn hình | Chi tiết |
| :--- | :--- |
| **Mã màn hình** | `SCR-RET-01` (Danh sách), `SCR-RET-02` (Modal lập phiếu 3 bước), `SCR-RET-03` (Drawer chi tiết) |
| **Đường dẫn URL Routes** | `/returns`, `/admin/returns` |
| **Quyền truy cập (RBAC)** | `Owner`, `Staff` (Nhân viên thu ngân `Cashier` bị ẩn mục menu và trả về `403 Forbidden` khi truy cập) |
| **Source code Frontend** | [`SalesReturnsPage.tsx`](../../src/frontend/admin-dashboard/src/pages/SalesReturnsPage.tsx), [`CreateSalesReturnModal.tsx`](../../src/frontend/admin-dashboard/src/components/salesReturns/CreateSalesReturnModal.tsx), [`SalesReturnDetailDrawer.tsx`](../../src/frontend/admin-dashboard/src/components/salesReturns/SalesReturnDetailDrawer.tsx), [`ConfirmSalesReturnDialog.tsx`](../../src/frontend/admin-dashboard/src/components/salesReturns/ConfirmSalesReturnDialog.tsx) |
| **Services & Idempotency** | [`salesReturnService.ts`](../../src/frontend/admin-dashboard/src/services/salesReturnService.ts) (Idempotency pattern BF-004) |
| **Controllers Backend** | [`SalesReturnsController.cs`](../../src/backend/src/OISM.WebApi/Controllers/SalesReturnsController.cs) |

### 6.1 Bố Cục Giao Diện Wireframe (ASCII Wireframe)

#### A. Trang Danh Sách Phiếu Trả Hàng (`/returns`)
```text
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ BREADCRUMB: Quản trị bán hàng & đơn hàng / Phiếu trả hàng                                      │
│ Quản Lý Trả Hàng & Hoàn Tiền                                               [ + Tạo phiếu trả ] │
│ Ghi nhận hàng khách trả, nhập lại kho theo giá vốn gốc và hoàn tiền                            │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ THANH TÌM KIẾM & BỘ LỌC KHOẢNG NGÀY NỬA MỞ [StartDate, EndDate):                              │
│ [ 🔍 Tìm mã phiếu RT-..., mã đơn ORD-... ] [ Chi nhánh: Tất cả ▼ ] [ Trạng thái: Tất cả ▼ ]     │
│ [ Từ ngày: 2026-11-01 ] [ Đến ngày: 2026-11-10 ]                                   [ Lọc / Reset ]│
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ BẢNG DANH SÁCH PHIẾU TRẢ HÀNG:                                                                 │
│ Mã phiếu      | Mã đơn gốc  | Kho nhận         | Tiền hoàn      | Trạng thái | Ngày tạo / Thao tác │
│ ──────────────┼─────────────┼──────────────────┼────────────────┼────────────┼──────────────────── │
│ RT-20261109-01| ORD-20261109| Kho Tổng Quận 1  | 598,000 đ (Tiền| 🟢 Đã xác  | 09/11/2026 14:10    │
│               |             |                  | mặt tại quầy)  | nhận       | [ Chi tiết / Sổ IN] │
│ RT-20261109-02| SHOPEE-9901 | Kho Tổng Quận 1  | 0 đ (Ví sàn)   | 🟢 Đã xác  | 09/11/2026 14:35    │
│               |             |                  |                | nhận       | [ Chi tiết / Sổ IN] │
│ RT-20261109-03| ORD-20261108| Kho Tổng Quận 1  | 299,000 đ      | ⚪ Phiếu    | 09/11/2026 15:00    │
│               |             |                  |                | nháp       | [Xác nhận] [Hủy bỏ] │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ Phân trang: Hiển thị 1 - 3 của 3 phiếu trả hàng               [ < Trước ] [ 1 ] [ Sau > ]      │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
```

#### B. Modal Tạo Phiếu Trả Hàng 3 Bước (`CreateSalesReturnModal`)
```text
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ TẠO PHIẾU TRẢ HÀNG & HOÀN TIỀN                                                      [ X ]      │
│ Bước 1: Chọn đơn hàng ──► [Bước 2: Chi tiết hàng trả & Hoàn tiền] ──► Bước 3: Xác nhận tóm tắt │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ ĐƠN GỐC: #ORD-20261109-01 (Kênh: POS - Khách lẻ)                       Trạng thái: 🟢 Completed│
│ KHO NHẬP HÀNG TRẢ VỀ: [ Kho Tổng Quận 1 ▼ ]                                                    │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ DANH SÁCH SẢN PHẨM CÓ THỂ TRẢ:                                                                 │
│ Sản phẩm / SKU               | Đã bán | Đã trả | Còn được trả | SL Trả       | Tiền hoàn dòng   │
│ ─────────────────────────────┼────────┼────────┼──────────────┼──────────────┼───────────────── │
│ Áo Polo Nam - Đen/M          | 5 cái  | 2 cái  | 3 cái        | [ 3 ] [Hết]  | 897,000 đ        │
│ POLO-NAM-DEN-M               |        |        |              | (max: 3)     |                  │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ TIỀN HOÀN & LÝ DO TRẢ HÀNG:                                                                    │
│ Tiền hoàn trả khách (tối đa: 897,000 đ):  [ 897,000 ] đ   (Có thể giảm nếu trừ phí trả hàng)   │
│ (*) Lý do trả hàng (bắt buộc):                                                                 │
│ [ Lỗi sản phẩm ]  [ Sai hàng / màu / size ]  [ Khách đổi ý ]  [ Khác ]                         │
│ Ghi chú nội bộ: [ Khách trả hàng nguyên tem mác, không rách ................................ ] │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ [ Hủy bỏ ]                                        [ < Quay lại ]  [ Lưu nháp ] [ Tiếp tục > ]  │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
```

### 6.2 Các Điểm Nối Tích Hợp Đa Phân Hệ (G5)

1. **Trang Kho & Sổ kho (`InventoryPage`)**:
   - Nút **"Ghi nhận trả hàng"** được kích hoạt chính thức (đã gỡ bỏ `ComingSoonBadge`).
   - Nhấp chuột trực tiếp mở `CreateSalesReturnModal` (Bước 1). Sau khi hoàn tất phiếu trả hàng thành công, tự động gọi `loadData()` để cập nhật số dư tồn kho tức thì.
2. **Drawer Chi Tiết Đơn Hàng (`OrderDetailDrawer`)**:
   - Bổ sung nút **"Trả hàng"** (màu đỏ cam `RotateCcw`) trên thanh hành động khi đơn hàng ở trạng thái `Confirmed` hoặc `Completed` và còn mặt hàng có `returnableQuantity > 0`. Bấm nút mở ngay Modal tạo phiếu trả ở Bước 2 với thông tin đơn gốc được điền sẵn.
   - Bảng sản phẩm bổ sung cột **"Đã trả"** cạnh cột "Số lượng".
   - Bổ sung khối **"Lịch sử phiếu trả hàng của đơn"** hiển thị mã phiếu, trạng thái, số lượng và tổng tiền hoàn.
3. **Modal Truy Vết Chứng Từ (`SalesInvoiceTraceModal`)**:
   - Bổ sung khối **"Phiếu Trả Hàng & Dòng Sổ Kho Nhập Lại (IN)"**: Hiển thị danh sách các phiếu trả hàng thuộc hóa đơn/đơn hàng, người xác nhận, ngày giờ xác nhận và chi tiết từng dòng sổ kho `IN` (mã SKU, số lượng nhập lại, giá vốn hoàn lại theo snapshot WAC gốc).
4. **Báo Cáo Doanh Thu & Lợi Nhuận (`ReportsPage`)**:
   - Thêm thẻ KPI **"Hàng trả lại"** (`returnAmount`) và **"Doanh thu thuần (Net)"** (`netRevenue = totalRevenue - returnAmount`).
   - Cập nhật 3 bảng phân rã (Theo kênh bán, Theo chi nhánh, Biểu đồ xu hướng theo ngày) hiển thị đầy đủ Doanh thu gộp, Tiền trả hàng và Doanh thu thuần.
5. **Trang Tổng Quan (`DashboardPage`)**:
   - Thẻ doanh thu tổng quan chuyển sang hiển thị **Doanh thu thuần (Net)** kèm ghi chú rõ ràng đã trừ hàng trả lại.

### 6.3 Cơ Chế An Toàn Idempotency & Chống Bấm Kép (BF-004)

- **Chữ ký định danh Payload (`RequestKey`)**:
  - Khóa sinh ngẫu nhiên UUID v4 khi người dùng bắt đầu lập phiếu trả.
  - Được lưu trữ tạm thời trong `sessionStorage` theo tiền tố `oism_sales_return_intent_${tenantId}` gắn liền với payload (`orderId`, `warehouseId`, `reason`, danh sách mặt hàng).
  - Khi người dùng gửi yêu cầu, `requestKey` được truyền qua cả 2 header: `Idempotency-Key` và `X-Request-Key`.
  - Nếu kết quả mạng chưa rõ ràng hoặc người dùng gửi lại đúng payload, hệ thống backend trả về kết quả replay `200 OK` mà không sinh ra phiếu thứ hai.
  - Sau khi nhận phản hồi thành công (`201 Created` hoặc `200 OK`), intent trong `sessionStorage` được dọn dẹp sạch sẽ.
- **Khóa nút Thao Tác (`busyRef` / `busyIdsRef`)**:
  - Toàn bộ các nút submit ("Lưu nháp", "Lưu và xác nhận", "Xác nhận phiếu", "Hủy phiếu") đều được bảo vệ bằng `useRef` chống click kép. Khi đang xử lý, nút hiển thị spinner `Loader2` và bị `disabled`.
- **Phục hồi Lỗi Khi "Lưu và xác nhận"**:
  - Nếu bước tạo phiếu Draft thành công nhưng bước xác nhận gặp sự cố, hệ thống **không xóa phiếu** mà giữ nguyên phiếu ở trạng thái `Draft` và mở ngay Drawer chi tiết để người dùng kiểm tra hoặc xác nhận lại thủ công.
- **Tự Động Làm Mới Khi Vượt Số Lượng (`RETURN_QUANTITY_EXCEEDED`)**:
  - Khi backend từ chối do mặt hàng đã bị đơn khác hoặc nhân viên khác trả trước, giao diện tự động gọi lại API `getReturnableItems` để đồng bộ lại số lượng còn được trả hiện thời mà không bắt người dùng nhập lại từ đầu.

### 6.4 Bảng Mã Lỗi Giao Diện Tiếng Việt Chuẩn Hóa

| Mã lỗi Backend (`errorCode`) | HTTP Status | Thông báo hiển thị giao diện tiếng Việt |
| :--- | :---: | :--- |
| `RETURN_QUANTITY_EXCEEDED` | `409 Conflict` | Số lượng trả vượt quá số lượng còn được phép trả của đơn hàng. Hệ thống đã làm mới lại số lượng tồn có thể trả. |
| `REFUND_AMOUNT_EXCEEDED` | `400 Bad Request` | Số tiền hoàn lại không được vượt quá giá trị thực thu của các mặt hàng được trả. |
| `ORDER_NOT_RETURNABLE` | `400 Bad Request` | Đơn hàng chưa xuất kho hoặc ở trạng thái không cho phép trả lại (chỉ áp dụng cho đơn Đã duyệt hoặc Hoàn tất). |
| `RETURN_WAREHOUSE_MISMATCH` | `400 Bad Request` | Kho nhập hàng trả phải thuộc cùng chi nhánh thực hiện đơn hàng gốc. |
| `INVALID_STATE_TRANSITION` | `409 Conflict` | Trạng thái phiếu trả hàng không hợp lệ cho thao tác này (Phiếu đã xác nhận hoặc đã bị hủy). |
| `IDEMPOTENCY_PAYLOAD_MISMATCH`| `409 Conflict` | Yêu cầu trùng khóa Idempotency-Key nhưng nội dung mặt hàng hoặc kho nhận bị thay đổi. |
| `FORBIDDEN` | `403 Forbidden` | Bạn không có quyền thực hiện thao tác trả hàng. Chỉ Chủ shop (Owner) và Quản lý kho (Staff) được cấp quyền. |

---

### 6.5 Kết Quả Kiểm Thử Giao Diện & Bộ Ảnh Nghiệm Thu Thực Tế (AC-08)

Toàn bộ 9 kịch bản kiểm thử nghiệp vụ giao diện đã được thực thi và kiểm chứng trên môi trường thực tế (Local Backend API + PostgreSQL + React Admin Dashboard). Kết quả kiểm thử đạt **100% PASS**:

| STT | Kịch bản kiểm thử | Mô tả chi tiết & Kết quả thực tế | Trạng thái |
| :---: | :--- | :--- | :---: |
| **KB-01** | Bán POS đơn vị gốc $\rightarrow$ Trả hàng $\rightarrow$ Xác nhận | Bán đơn POS 5 áo Polo, lập phiếu trả 2 áo Draft, xác nhận hoàn tiền mặt. Sổ kho ghi nhận bút toán `IN` 2 áo đúng giá vốn gốc 50,000đ. | **PASS** |
| **KB-02** | Bán theo đơn vị quy đổi | Quy đổi chính xác theo `factorSnapshot` về đơn vị chuẩn `baseQuantity`. | **PASS** |
| **KB-03** | Đơn kênh Sàn TMĐT (`Shopee`) | Đơn hàng từ sàn TMĐT ẩn phần hoàn tiền tại quầy, lập phiếu trả với `refundAmount = 0` (sàn tự hoàn ví). | **PASS** |
| **KB-04** | Trả một phần rồi trả tiếp phần còn lại | Đơn bán 5 áo: lần 1 trả 2 áo (còn 3 áo); lần 2 trả nốt 3 áo (còn 0 áo). | **PASS** |
| **KB-05** | Chặn nhập vượt số lượng được trả | Nhập trả thêm khi số còn được trả $= 0 \rightarrow$ Backend trả về lỗi `RETURN_QUANTITY_EXCEEDED` (409), giao diện hiện thông báo tiếng Việt chuẩn. | **PASS** |
| **KB-06** | BF-004 Replay cùng `Idempotency-Key` | Gửi 2 lần cùng payload và `Idempotency-Key` $\rightarrow$ Lần 1 trả về 201 Created, lần 2 trả về 200 OK replay, trùng ID và số phiếu `RT-...`. | **PASS** |
| **KB-07** | Chặn chuyển trạng thái sai | Hủy phiếu đã `Confirmed` hoặc Xác nhận phiếu đã `Cancelled` $\rightarrow$ Chặn với mã lỗi `INVALID_STATE_TRANSITION` (409). | **PASS** |
| **KB-08** | Phân quyền RBAC Nhân viên thu ngân (`Cashier`) | Tài khoản `cashier@shopa.local` bị ẩn menu "Trả hàng" và bị trả về `403 Forbidden` khi truy cập trực tiếp. | **PASS** |
| **KB-09** | Cập nhật Báo cáo & Tổng quan số thuần | Báo cáo Doanh thu & Lợi nhuận và Tổng quan phản ánh chính xác `returnAmount`, `netRevenue` và `returnCogs`. | **PASS** |

#### Danh Mục Ảnh Chụp Màn Hình Nghiệm Thu Thực Tế (Screenshots)

| Mã ảnh | Tên file | Mô tả nội dung giao diện | Đường dẫn file ảnh |
| :---: | :--- | :--- | :--- |
| **SCR-01** | `01-sales-returns-list.png` | **Trang danh sách phiếu trả hàng (`/returns`)**: Thanh tìm kiếm, lọc chi nhánh, lọc trạng thái, lọc ngày nửa mở, nút "+ Tạo phiếu trả", bảng phiếu trả với badge màu sắc và phân trang. | [01-sales-returns-list.png](./screenshots/sales-returns/01-sales-returns-list.png) |
| **SCR-02** | `02-create-return-modal.png` | **Modal tạo phiếu trả hàng 3 bước (`CreateSalesReturnModal`)**: Bước 2 hiển thị thông tin đơn gốc, bảng mặt hàng có thể trả (Bán / Đã trả / Còn được trả / Ô SL trả / Nút "Trả hết"), tiền hoàn tự tính, chọn kho nhận, quick buttons lý do trả hàng. | [02-create-return-modal.png](./screenshots/sales-returns/02-create-return-modal.png) |
| **SCR-03** | `03-sales-return-detail.png` | **Drawer chi tiết phiếu trả hàng (`SalesReturnDetailDrawer`)**: Header hiển thị badge trạng thái `Confirmed`, chi nhánh & kho nhận, người xác nhận, bảng danh sách sản phẩm nhập lại kèm cột giá vốn WAC (chỉ hiện cho Owner/Staff), liên kết sang sổ kho và truy vết. | [03-sales-return-detail.png](./screenshots/sales-returns/03-sales-return-detail.png) |
| **SCR-04** | `04-order-detail-with-return.png` | **Drawer chi tiết đơn hàng (`OrderDetailDrawer`)**: Nút hành động "Trả hàng" (màu đỏ cam `RotateCcw`), cột "Đã trả" trong bảng sản phẩm, và khối "Lịch sử phiếu trả hàng của đơn" liệt kê các phiếu trả liên quan. | [04-order-detail-with-return.png](./screenshots/sales-returns/04-order-detail-with-return.png) |
| **SCR-05** | `05-invoice-trace-with-return.png` | **Modal truy vết chứng từ (`SalesInvoiceTraceModal`)**: Khối "Phiếu Trả Hàng & Dòng Sổ Kho Nhập Lại (IN)" hiển thị chi tiết số phiếu trả, ngày xác nhận và các dòng bút toán sổ kho `IN` hoàn trả giá vốn. | [05-invoice-trace-with-return.png](./screenshots/sales-returns/05-invoice-trace-with-return.png) |
| **SCR-06** | `06-reports-net-revenue.png` | **Trang Báo cáo Doanh thu & Lợi nhuận (`/reports`)**: Thẻ KPI "Hàng trả lại" (`returnAmount`) và "Doanh thu thuần (Net)" (`netRevenue`), cùng các bảng phân rã kênh bán và xu hướng theo ngày có trừ hàng trả. | [06-reports-net-revenue.png](./screenshots/sales-returns/06-reports-net-revenue.png) |


---

## 7. Đặc Tả Màn Hình Hóa Đơn Bán Hàng (`/invoices`, Upgrade 02)

*Bổ sung 11/11/2026 theo code hiện tại.*

| Thông tin | Chi tiết |
| :--- | :--- |
| **Route** | `/invoices`, `/admin/invoices` |
| **Quyền** | Owner, Staff. Cashier không vào Admin (đăng nhập Admin sẽ được chuyển sang POS); Cashier chỉ in lại biên lai qua `GET /api/sales-invoices/receipt` |
| **Source** | `src/frontend/admin-dashboard/src/pages/SalesInvoicesPage.tsx`, `components/salesInvoices/SalesInvoiceDetailModal.tsx`, `SalesInvoiceReprintModal.tsx`, `SalesInvoiceTraceModal.tsx`, `services/salesInvoiceService.ts` |
| **API** | `GET /api/sales-invoices`, `GET /api/sales-invoices/{id}`, `GET /api/sales-invoices/{id}/receipt`, `GET /api/sales-invoices/{id}/trace` |
| **Yêu cầu** | [Upgrade 02](../Upgrade-recommended/Done/02-truy-vet-chung-tu-xuat-kho.md) |

| Khu vực | Nội dung |
| :--- | :--- |
| Tiêu đề | "Hóa Đơn Bán Hàng & Truy Vết", nút **Tải lại** |
| Bộ lọc | Số hóa đơn (`INV-...`), Chi nhánh, Từ ngày, Đến ngày |
| Bảng | Số hóa đơn, Đơn hàng gốc, Chi nhánh, Khách hàng, Người lập, Thời gian lập, Tổng tiền, Thao tác |
| Thao tác mỗi dòng | **Xem chi tiết**; **In lại** (khổ 80mm hoặc 58mm, dùng dữ liệu đã chụp lúc lập hóa đơn); **Truy vết** |
| Modal truy vết | Chuỗi Hóa đơn → Đơn hàng → Dòng sổ kho `OUT` (bảng chi tiết bút toán); nếu đơn có trả hàng thì thêm khối Phiếu trả hàng và dòng sổ `IN` (CS-01) |

Giá vốn trong truy vết do backend lọc theo vai trò; trang không tự ẩn trường. In chứng từ giao hàng từ đơn hàng (CS-06) chưa làm, nút trong drawer đơn hàng còn nhãn Coming soon.
