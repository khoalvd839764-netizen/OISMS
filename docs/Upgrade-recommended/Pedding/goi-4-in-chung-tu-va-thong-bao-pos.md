# Gói 4 — In chứng từ và thông báo tại POS

## Hiện trạng

POS đã có biên nhận và nút **In**. Admin nhận thông báo đơn mới qua SignalR, còn POS nhận sự kiện thay đổi tồn. In phiếu giao hàng và in báo cáo vẫn ở CS-06. [Biên nhận POS](../../../src/frontend/pos-app/src/components/pos/ReceiptPrintModal.tsx#L49) · [SignalR POS](../../../src/frontend/pos-app/src/services/realtimeService.ts#L63) · [Chi tiết đơn Admin](../../../src/frontend/admin-dashboard/src/components/orders/OrderDetailDrawer.tsx#L247)

## Việc cần làm

- [ ] **`FR-POS-03`.** Mở hộp thoại in sau khi checkout thành công và đã có biên nhận. Giữ nút in lại để thu ngân dùng khi trình duyệt chặn hộp thoại hoặc đã đóng hộp thoại.
- [ ] **`FR-ORD-03` / CS-06.** Cho in phiếu giao hàng từ đơn đang xem và in báo cáo theo bộ lọc đang chọn. Dữ liệu in dùng quyền của người thao tác và giữ quy tắc ẩn giá vốn của màn hình.
- [ ] **`FR-SIM-02`.** Bổ sung popup hoặc âm thanh đơn mới ở POS qua SignalR. Dùng tenant và phạm vi chi nhánh hiện hành, xử lý kết nối lại và đổi chi nhánh.

## Bằng chứng nghiệm thu

- [ ] Checkout thành công mở in với đúng biên nhận 58/80 mm. Checkout lỗi hoặc kết quả chưa xác định không kích hoạt in. In lại không tạo thêm đơn.
- [ ] Phiếu giao hàng và báo cáo có bản in đúng đơn/bộ lọc và không lộ số liệu ngoài quyền. Có bản in mẫu để đối chiếu.
- [ ] Đơn mới hiện trên Admin/POS thuộc đúng phạm vi. Kiểm tra lại kết nối, đổi chi nhánh và sự kiện gửi lặp. [Đặc tả POS](../../02-functional-requirements/fr-07-pos-checkout.md) · [Đặc tả SignalR](../../02-functional-requirements/fr-09-realtime-signalr.md)
