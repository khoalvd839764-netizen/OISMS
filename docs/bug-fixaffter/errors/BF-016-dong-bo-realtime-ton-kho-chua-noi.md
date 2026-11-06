# BF-016 — Đồng bộ realtime tồn kho chưa nối đủ

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa |
| **Ưu tiên** | P2 |
| **Khu vực** | Realtime |
| **Phát hiện** | 28–29/10/2026 (Xác nhận bằng code; PR26-F07) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

## Mô tả lỗi (lúc phát hiện)

**Thực tế:** server gửi `StockLevelChanged` vào nhóm chi nhánh. Trong source Admin đã quét không có caller `joinBranch/leaveBranch`. Adapter phát DOM event `oism:stock-changed`, nhưng các màn kho không đăng ký sự kiện đó. `onreconnected` không refetch dữ liệu bỏ lỡ. Kiểu `reservation.expired` tồn tại nhưng không có handler transport tương ứng.

**Ảnh hưởng:** nhãn SignalR “Trực tuyến” không bảo đảm bảng tồn/trạng thái đơn tự cập nhật. Cần làm mới hoặc focus lại tab Kho; đây là đường dùng tạm hiện có, không phải realtime đầy đủ.

**Vị trí:** `services/realtimeAdapter.ts`, `components/NotificationCenter.tsx`, `pages/InventoryPage.tsx`; `WebApi/Services/RealtimeNotifier.cs`, `Hubs/NotificationHub.cs`.

**Điều kiện đóng:** join/leave theo branch và actor, listener có cleanup, reconnect refetch, chuẩn hóa event expiry; chứng minh bằng Hub thật và dữ liệu test. Giữ xử lý BF-002 trước khi mở rộng realtime.

## Cách đã sửa

* **Thành phần:** `src/frontend/admin-dashboard/src/services/realtimeAdapter.ts`, `src/frontend/admin-dashboard/src/pages/InventoryPage.tsx`, `src/frontend/admin-dashboard/src/pages/OrdersPage.tsx`
* **Triệu chứng & Nguyên nhân:**
  * Màn hình quản lý kho (`InventoryPage`) không thực hiện gọi `joinBranch` vào nhóm chi nhánh trên SignalR Hub, không lắng nghe sự kiện `oism:stock-changed` và `oism:reservation-expired` để tự động làm mới số tồn, và không đồng bộ lại dữ liệu khi kết nối mạng phục hồi (`reconnected`).
* **Giải pháp khắc phục:**
  * Trong `realtimeAdapter.ts`, hỗ trợ quản lý tập hợp đa chi nhánh `joinedBranchIds`, tự động join lại toàn bộ các chi nhánh đã đăng ký khi `onreconnected` và phát sự kiện `oism:realtime-reconnected`, đồng thời lắng nghe cả `reservation.expired` / `ReservationExpired` để phát `oism:reservation-expired`.
  * Trong `InventoryPage.tsx` và `OrdersPage.tsx`, tự động join nhóm chi nhánh đang lọc (hoặc toàn bộ chi nhánh đang hoạt động khi chọn `"all"`) và lắng nghe `oism:stock-changed`, `oism:reservation-expired`, `oism:realtime-reconnected` để tải lại số dư tồn kho và danh sách đơn tức thì.
* **Kiểm thử xác thực:** Pass kịch bản kiểm thử `BF-016` trong `scratch/test_bugfix_after_verification.cjs`: xác nhận gọi `joinBranch` và tự động nạp lại dữ liệu kho khi nhận sự kiện `oism:stock-changed`, `oism:reservation-expired` và `oism:realtime-reconnected`.
