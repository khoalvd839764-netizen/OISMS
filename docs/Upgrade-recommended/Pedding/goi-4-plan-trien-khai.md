# Gói 4 — Plan triển khai

Plan cho các tiêu chí còn mở trong [Gói 4](./goi-4-in-chung-tu-va-thong-bao-pos.md). Lập ngày 15/11/2026, các quyết định chốt qua phiên chất vấn cùng ngày.

## Quyết định đã chốt

| # | Câu hỏi | Quyết định | Lý do |
| --- | --- | --- | --- |
| Q1 | POS báo loại đơn nào? | **Đơn online (Admin, Shopee, TikTok, Lazada) thuộc chi nhánh POS đang chọn.** Không báo đơn POS. Popup chỉ để xem, không có nút xử lý. | Mục đích: thu ngân biết hàng trên kệ vừa bị giữ chỗ (giải thích vì sao tồn khả dụng giảm) và biết cần soạn hàng. Cashier không duyệt/hủy đơn (`FR-ORD-03` thuộc Staff/Owner). |
| Q2 | Lọc chi nhánh ở đâu? | **Ở server.** Phát sự kiện mới `BranchOrderCreated` vào nhóm `tenant_{id}_branch_{id}`. `OrderCreated` vào nhóm tenant giữ nguyên cho Admin. `JoinBranchGroup` gọi `IBranchAccessService` để chặn Cashier vào chi nhánh không được phân công. | Lọc ở client vẫn để dữ liệu chi nhánh khác tới máy Cashier, trái CS-02. Tên sự kiện riêng tránh Admin đã vào nhóm chi nhánh nhận trùng. |
| Q2b | Payload gửi POS | `orderId`, `orderNumber`, `branchId`, `channel`, `createdAt` và danh sách dòng `skuId`, `skuCode`, `productName`, `quantity`, `unitName`. **Không** gửi tổng tiền, tên khách, giá vốn. | Thu ngân cần biết hàng nào bị giữ, không cần thông tin thanh toán của khách. |
| Q2c | Mất kết nối, đổi chi nhánh | **Không bù thông báo.** Khi trạng thái `reconnecting`/`disconnected`, POS hiện dòng "có thể đã bỏ lỡ thông báo". | Tồn đã được nạp lại sau khi nối lại (`subscribeReconnected`), popup chỉ là thông tin tức thời. Không thêm API. |
| Q2d | Giao diện POS | Toast góc màn hình tự ẩn sau vài giây, không che giỏ hàng, không chiếm phím tắt. Nút bật/tắt tiếng ở `PosHeader`, mặc định bật, lưu `localStorage`. Khử trùng theo `orderId`. | Không làm gián đoạn lúc đang tính tiền. Âm thanh chỉ phát được sau thao tác đầu tiên của người dùng (giới hạn autoplay). |
| Q3 | POS checkout có phát `OrderCreated`? | **Có**, sau commit, để đúng `fr-09` và để `OrdersPage` tự nạp lại. Admin hiện đơn kênh POS trong `NotificationCenter` nhưng **không phát âm thanh**. Replay theo cùng `RequestKey` không phát lại. Không phát `BranchOrderCreated` cho đơn POS (theo Q1). | Sửa lệch giữa `fr-09` ("đơn mới từ Webhook/POS") và code; tránh Admin kêu mỗi lần bán lẻ. |
| Q4 | Phiếu giao hàng | Khổ **A4 và 80 mm** (chọn được, như biên nhận POS). Có tên khách, SĐT, địa chỉ giao, dòng hàng, đơn giá, thành tiền, tổng thu (COD). **Không có giá vốn** với mọi vai trò. | Đúng CS-06; shipper cần số tiền thu hộ. |
| Q4b | Trạng thái được in | Chỉ `Confirmed` và `Completed`, chỉ đơn kênh online. Đơn POS dùng in lại hóa đơn bán đã có. | Đơn `Reserved` chưa duyệt, `Cancelled` không giao. |
| Q5 | In báo cáo | In **tab đang xem** bằng `window.print()` với CSS in A4. Tiêu đề in gồm tenant, khoảng ngày (UTC+07:00), chi nhánh, người in, giờ in. Không làm PDF ở server. | In đúng dữ liệu màn hình nên giữ nguyên quyền: Staff nhận `null` giá vốn/lãi gộp từ API. |
| Q6 | Tự in sau checkout | **Luôn tự mở hộp thoại in** khi checkout thành công, không có công tắc tắt. Phiếu tạm tính (`isDraft`) không bao giờ tự in. Giữ nút In lại. | Đúng nguyên văn `FR-POS-03`. |
| Q7 | Bằng chứng SignalR | Integration test với `HubConnection` thật, mở rộng `RealtimeHubIntegrationTests`. Phần replay POS checkout chạy trên PostgreSQL thật. | Chạm phân quyền; mock không chứng minh định tuyến nhóm. |

## Phase A — POS tự in sau checkout (`FR-POS-03`)

Chỉ frontend `pos-app`.

- `PosPage`: khi `onSuccessAndPrint` nhận biên nhận, lưu kèm cờ `autoPrint = true`. `onPreviewReceipt` (phiếu tạm tính) không đặt cờ.
- `ReceiptPrintModal`: nhận prop `autoPrint`. Khi `autoPrint && !receipt.isDraft`, gọi `window.print()` một lần sau khi modal render xong (`useEffect` + `requestAnimationFrame`), có `ref` chặn gọi lần hai khi render lại.
- Luồng lỗi và kết quả chưa rõ trong `PaymentModal` không đổi: các nhánh này không gọi `onSuccessAndPrint` nên không in.
- Test `npm test`: tách quyết định "có tự in không" thành hàm thuần (`shouldAutoPrint(receipt, autoPrint)`) để test: thành công → in; `isDraft` → không; không có biên nhận → không.

## Phase B — Phiếu giao hàng và in báo cáo (CS-06, `FR-ORD-03`)

Chỉ frontend `admin-dashboard`.

### B1. Phiếu giao hàng

- Component mới `components/orders/ShippingSlipPrintModal.tsx`, tham khảo `SalesInvoiceReprintModal`: chọn khổ A4/80 mm, CSS `@media print` chỉ in vùng phiếu.
- Dữ liệu lấy từ chi tiết đơn đang mở trong `OrderDetailDrawer` (API chi tiết đơn hiện có), không thêm endpoint. Không render trường giá vốn dù API có trả.
- Nút "In chứng từ" trong `OrderDetailDrawer` bỏ `ComingSoonBadge`, chỉ bật khi `channel !== 'POS'` và trạng thái `Confirmed`/`Completed`; trường hợp khác tắt kèm `title` nêu lý do.
- Số lượng in theo đơn vị đã bán kèm tên đơn vị (không cộng gộp khác đơn vị, tránh BF-027).

### B2. In báo cáo

- `ReportsPage`: bật nút "In báo cáo", gọi `window.print()`.
- Thêm khối tiêu đề chỉ hiện khi in (`hidden print:block`): tenant, khoảng ngày, chi nhánh, tab, người in, giờ in.
- CSS in: ẩn sidebar, thanh lọc, tab, nút; bảng không bị cắt ngang dòng; biểu đồ co theo khổ A4.
- Giá vốn/lãi gộp: in đúng giá trị màn hình đang hiện (Staff thấy `—` vì API trả `null`).

## Phase C — Thông báo đơn mới ở POS (`FR-SIM-02`)

### C1. Backend

- `IRealtimeNotifier.NotifyBranchOrderCreatedAsync(tenantId, BranchOrderRealtimeDto, ct)`; `RealtimeNotifier` gửi `BranchOrderCreated` vào `tenant_{tenantId}_branch_{branchId}`, nuốt và ghi log lỗi như các hàm hiện có.
- `BranchOrderRealtimeDto` trong `Features/Realtime/Dtos/RealtimeDtos.cs` theo Q2b, không có tiền và tên khách.
- `OrderIntakeService` (đơn Admin) và luồng đơn sàn: thêm `BranchOrderCreated` vào `pendingNotifications` cạnh `OrderCreated`, vẫn phát sau commit. Kiểm tra `MarketplaceOrderService` có đi qua cùng điểm phát hay cần thêm.
- `PosCheckoutService`: sau commit, phát `OrderCreated` (không phát `BranchOrderCreated`). Nhánh replay (`existingOrder != null`) không phát.
- `NotificationHub.JoinBranchGroup`: sau khi kiểm tra chi nhánh thuộc tenant, nếu người gọi là Cashier thì gọi `IBranchAccessService.EnsureCashierBranchAllowedAsync`; bị từ chối thì ném `HubException`. Giữ quy tắc hiện tại: Cashier chưa có dòng phân công nào được mọi chi nhánh.

### C2. Admin

- `realtimeAdapter` / `NotificationCenter`: sự kiện `OrderCreated` có `channel === 'POS'` vẫn thêm vào danh sách và nạp lại `OrdersPage`, nhưng không gọi `playNotificationSound`.

### C3. POS

- `realtimeService.ts`: nghe `BranchOrderCreated`, áp cùng cơ chế chặn `generation`/`activeBranch` như `StockLevelChanged`, kiểm tra payload hợp lệ, phát cho `subscribeBranchOrderCreated`.
- Component toast mới trong `components/pos/`: khử trùng theo `orderId` (giữ tập id đã hiện trong phiên), tự ẩn, phát âm thanh khi bật tiếng.
- `PosHeader`: nút bật/tắt tiếng, hiện dòng "có thể đã bỏ lỡ thông báo" khi trạng thái realtime không phải `connected`.
- Đổi chi nhánh: xóa toast đang hiện của chi nhánh cũ.

## Kiểm thử

| Nhóm | Test | Môi trường |
| --- | --- | --- |
| Hub | Cashier được phân công chi nhánh A gọi `JoinBranchGroup(B)` bị từ chối; gọi `JoinBranchGroup(A)` thành công; Cashier chưa phân công vào được mọi chi nhánh. | `RealtimeHubIntegrationTests` (HubConnection thật) |
| Hub | Tạo đơn Admin ở chi nhánh A: POS ở A nhận `BranchOrderCreated` đúng payload Q2b (không có `totalAmount`, `customerName`, giá vốn); POS ở B không nhận; Admin nhận `OrderCreated`. | `RealtimeHubIntegrationTests` |
| Hub | Tenant khác không nhận sự kiện nào. | Mở rộng AC08 |
| POS checkout | Checkout thành công phát `OrderCreated` kênh POS, không phát `BranchOrderCreated`; gửi lại cùng `RequestKey` và payload không phát lần hai. | PostgreSQL thật (`Postgres*` hoặc fixture hiện có) |
| Frontend POS | `shouldAutoPrint`; khử trùng toast theo `orderId`; bỏ sự kiện sai chi nhánh. | `npm test --prefix src/frontend/pos-app` |
| Build | `dotnet build`, `npm run build` cho Admin và POS, `npm run lint` cho POS. | Local |
| Bản in mẫu | Biên nhận 58/80 mm sau checkout; phiếu giao hàng A4/80 mm; báo cáo in bằng Owner và Staff (Staff không thấy giá vốn/lãi gộp). Lưu ảnh/PDF vào báo cáo nghiệm thu. | Thủ công trên trình duyệt |

## Docs phải cập nhật

- `docs/02-functional-requirements/fr-09-realtime-signalr.md`: sự kiện `BranchOrderCreated`, phạm vi chi nhánh, đơn POS phát `OrderCreated` nhưng Admin không kêu.
- `docs/02-functional-requirements/fr-07-pos-checkout.md`: ghi chú hiện thực `FR-POS-03` (luôn tự in, không tự in phiếu tạm tính).
- `docs/02-functional-requirements/fr-06-omnichannel-orders.md`: phiếu giao hàng, trạng thái được in.
- `docs/05-api-contracts/`: hợp đồng sự kiện SignalR mới và lỗi `JoinBranchGroup`.
- `docs/01-users-and-rbac/02-rbac-matrix.md`: Cashier chỉ vào nhóm realtime của chi nhánh được phân công.
- `docs/bug-fixaffter/CHECKLIST.md`: đóng CS-06.
- [goi-4-in-chung-tu-va-thong-bao-pos.md](./goi-4-in-chung-tu-va-thong-bao-pos.md), [Pedding/README.md](./README.md), `checklist.md`: tick tiêu chí, chuyển Gói 4 sang `Done` khi đủ bằng chứng.

## Rủi ro

- `JoinBranchGroup` từ chối sẽ làm POS của Cashier chuyển `disconnected` (nhánh `catch` trong `switchBranch`). Hợp lý vì REST checkout cũng đã chặn chi nhánh không được phân công, nhưng cần kiểm tra danh sách chi nhánh trên POS đã lọc theo phân công để không xảy ra trong thao tác bình thường.
- `window.print()` tự gọi có thể bị trình duyệt chặn hoặc chặn luồng JS đến khi đóng hộp thoại; nút In lại là đường dự phòng.
- Âm thanh POS không phát trước thao tác đầu tiên (autoplay policy); chấp nhận.
