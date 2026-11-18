# Gói 4 — In chứng từ và thông báo tại POS

> **Đã triển khai 15/11/2026** (commit `f3b8a27`, PR #47). Còn một bằng chứng thủ công: bản in mẫu trên trình duyệt. [Plan và quyết định đã chốt](./goi-4-plan-trien-khai.md) · [Báo cáo Gói 4](../../report-after-upgrade/in-chung-tu-va-thong-bao-pos-goi4.md)

## Việc đã làm

- [x] **`FR-POS-03`.** POS tự mở hộp thoại in đúng một lần khi checkout thành công; không tự in phiếu tạm tính, lỗi hoặc kết quả chưa rõ. Giữ nút In lại. [ReceiptPrintModal](../../../src/frontend/pos-app/src/components/pos/ReceiptPrintModal.tsx) · [Đặc tả POS](../../02-functional-requirements/fr-07-pos-checkout.md)
- [x] **`FR-ORD-03` / CS-06.** Phiếu giao hàng A4/80 mm cho đơn online `Confirmed`/`Completed`, có giá bán và tổng thu COD, không có giá vốn. In báo cáo theo tab đang xem, Staff thấy giá vốn/lãi gộp là "—". `PrintArea` cô lập vùng in. [Phiếu giao hàng](../../../src/frontend/admin-dashboard/src/components/orders/ShippingSlipPrintModal.tsx) · [PrintArea](../../../src/frontend/admin-dashboard/src/components/print/PrintArea.tsx) · [Báo cáo Admin](../../../src/frontend/admin-dashboard/src/pages/ReportsPage.tsx)
- [x] **`FR-SIM-02`.** Sự kiện `BranchOrderCreated` theo nhóm chi nhánh cho POS (không có tổng tiền, tên khách, giá vốn); `JoinBranchGroup` kiểm phân công Cashier; Cashier không vào nhóm toàn tenant. POS hiện toast, khử trùng theo `orderId`, nút tắt tiếng, cảnh báo khi mất kết nối, đổi chi nhánh xóa toast. Đơn POS phát `OrderCreated` cho Admin nhưng không kêu. [NotificationHub](../../../src/backend/src/OISM.WebApi/Hubs/NotificationHub.cs) · [SignalR POS](../../../src/frontend/pos-app/src/services/realtimeService.ts) · [Đặc tả SignalR](../../02-functional-requirements/fr-09-realtime-signalr.md)

## Bằng chứng nghiệm thu

- [x] Checkout thành công mới tự in; lỗi, kết quả chưa rõ và phiếu tạm tính không in; in lại không tạo đơn, gửi lại cùng `RequestKey` không phát sự kiện. `test-receipt.mjs` (`shouldAutoPrint`), `AC36_Postgres_Checkout_EmitsSingleOrderCreated_NoBranchOrderCreated_AndReplayEmitsNothing` (PostgreSQL).
- [ ] Phiếu giao hàng và báo cáo có bản in mẫu đúng đơn/bộ lọc, không lộ số liệu ngoài quyền. Code và build đạt; **chưa xem trước bản in trên trình duyệt** (biên nhận 58/80 mm, phiếu giao A4/80 mm, báo cáo Owner/Staff).
- [x] Đơn mới tới đúng chi nhánh và tenant, Cashier sai chi nhánh bị từ chối, không lộ tiền/tên khách, payload lỗi và sự kiện lặp bị bỏ. `RealtimeHubIntegrationTests` (AC35, `HubConnection` thật), `test-realtime.mjs`. Integration 85/85 PASS trên PostgreSQL 16 ngày 15/11/2026.
