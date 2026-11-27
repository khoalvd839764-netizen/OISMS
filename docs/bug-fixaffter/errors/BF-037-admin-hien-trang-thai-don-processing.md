# BF-037 — Admin hiển thị trạng thái đơn `Processing` không tồn tại và nút "Xử lý / Đóng gói" không có tác dụng

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa, kiểm chứng bằng build và rà soát mã |
| **Ưu tiên** | P3 (hiển thị, không ảnh hưởng dữ liệu) |
| **Khu vực** | Admin / Trung tâm đơn hàng |
| **Phát hiện** | 25/11/2026, khi rà báo cáo SRS đối chiếu state machine đơn hàng |
| **Nhánh sửa** | `claude/fix-order-processing-status-01b645` |

## Mô tả lỗi

Trên màn **Đơn hàng** của Admin:

- Bộ lọc trạng thái có mục **"Đang xử lý"** (`Processing`). Chọn mục này luôn ra danh sách rỗng vì backend không có trạng thái đó.
- Drawer chi tiết đơn `Confirmed` có nút **"Xử lý / Đóng gói"** cạnh **"Hoàn tất đơn"**. Bấm nút gọi `POST /api/orders/{id}/process`, API trả 200 nhưng đơn giữ nguyên `Confirmed`, người dùng tưởng đã chuyển bước.
- `types/domain.ts` khai báo thêm `"Pending"` trong `OrderStatus`, cũng không phải trạng thái đơn của backend.

## Nguyên nhân

- State machine chuẩn ([04-state-machines.md](../../04-architecture-and-design/04-state-machines.md)) chỉ có `Draft → Reserved → Confirmed → Completed` và `Draft/Reserved → Cancelled`. Backend khớp: `OISM.Domain.Common.OrderStatus` (5 hằng số), chuyển trạng thái trong `Order.cs`, CHECK của `orders.status` chỉ 5 giá trị.
- `src/frontend/admin-dashboard/src/types/ordersCenter.ts` được viết theo spec giao diện sớm, thêm `Processing` kèm `TODO: xác nhận với backend` nhưng chưa từng được dọn: `ORDER_STATUSES`, `ORDER_STATUS_LABEL`, `ORDER_ACTIONS` (`Confirmed: ['process','complete']`, `Processing: ['complete']`) và màu trong `OrdersTable`.
- `OrderService.ProcessOrderAsync` chỉ gọi lại luồng Confirm cho đơn `Reserved`; đơn đã `Confirmed` thì trả nguyên chi tiết. Admin chỉ hiện nút này ở đơn `Confirmed`, nên nút không bao giờ đổi gì.

## Cách sửa

Chỉ sửa frontend Admin, không đổi backend và API:

- `types/ordersCenter.ts`: bỏ `Processing` khỏi `OrderStatus`, `ORDER_STATUSES`, `ORDER_STATUS_LABEL`; bỏ action `process`; `ORDER_ACTIONS.Confirmed` chỉ còn `complete`. Thay TODO bằng ghi chú nguồn state machine.
- `components/orders/OrderDetailDrawer.tsx`: bỏ nút "Xử lý / Đóng gói" và nhánh gọi `processOrder`. Đơn `Reserved` còn **Duyệt đơn**, **Hủy đơn**; đơn `Confirmed` còn **Hoàn tất đơn**.
- `components/orders/OrdersTable.tsx`: bỏ màu `Processing`.
- `pages/OrdersPage.tsx`: khối in phiếu xuất kho 02-VT chỉ lọc đơn `Confirmed`/`Completed`.
- `services/orderService.ts`: bỏ `processOrder` (không còn caller).
- `types/domain.ts`: bỏ `"Pending"` khỏi `OrderStatus`; `pages/DashboardPage.tsx` bỏ khóa `Pending` trong `OrderBadge` (giá trị lạ vẫn rơi về nhãn mặc định như cũ).

Backend giữ nguyên `POST /api/orders/{id}/process` (có test `ProcessAndCompleteOrder_ShouldTransitionStateProperly` và có thể có client ngoài dùng). Từ bản sửa này Admin không gọi endpoint đó nữa.

### Tài liệu cập nhật

- `docs/11-ui-screens/05-order-screens.md`: sơ đồ trạng thái bỏ bước `Shipped` (cũng không tồn tại), ghi rõ không có trạng thái trung gian và nút theo từng trạng thái.
- `docs/05-api-contracts/04-order-endpoints.md`: dòng `/process` ghi là giữ để tương thích, Admin không gọi.

## File đã sửa

| File | Thay đổi |
| :--- | :--- |
| `src/frontend/admin-dashboard/src/types/ordersCenter.ts` | Bỏ `Processing`, action `process` |
| `src/frontend/admin-dashboard/src/components/orders/OrderDetailDrawer.tsx` | Bỏ nút "Xử lý / Đóng gói" |
| `src/frontend/admin-dashboard/src/components/orders/OrdersTable.tsx` | Bỏ màu `Processing` |
| `src/frontend/admin-dashboard/src/pages/OrdersPage.tsx` | Bỏ điều kiện `Processing` |
| `src/frontend/admin-dashboard/src/services/orderService.ts` | Bỏ `processOrder` |
| `src/frontend/admin-dashboard/src/types/domain.ts` | Bỏ `Pending` |
| `src/frontend/admin-dashboard/src/pages/DashboardPage.tsx` | Bỏ khóa `Pending` trong `OrderBadge` |
| `docs/11-ui-screens/05-order-screens.md`, `docs/05-api-contracts/04-order-endpoints.md` | Đồng bộ mô tả |

## Kiểm chứng

| Lệnh | Kết quả |
| :--- | :--- |
| `npm run build --prefix src/frontend/admin-dashboard` (`tsc -b && vite build`) | PASS. `Record<OrderStatus, …>` bắt buộc đủ khóa nên TypeScript xác nhận không còn chỗ nào dùng `Processing` |
| `grep -rnE "Processing\|processOrder\|'process'" src/frontend/admin-dashboard/src` | Chỉ còn ghi chú trong `ordersCenter.ts` và biến `processingId` không liên quan ở `PriceManagementModal.tsx` |
| Lint | NOT RUN: Admin không có script `lint` |
| Chụp màn bộ lọc và drawer đơn `Confirmed` trên dev server | NOT RUN: cần backend và dữ liệu đăng nhập |

Không cần test PostgreSQL: thay đổi không chạm backend, transaction, tồn kho hay dữ liệu.
