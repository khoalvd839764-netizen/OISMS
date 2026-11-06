# Giả lập đơn sàn TMĐT

Giao diện bắn đơn Shopee, TikTok Shop, Lazada vào OISMS qua `POST /api/webhooks/simulator/{channel}` (FR-SIM-01). App chỉ gọi đường webhook và xác thực bằng header `X-Webhook-Secret`, không đăng nhập.

## Chạy

```bash
npm install
npm run dev     # http://localhost:5175, Vite chuyển /api sang http://localhost:5000
npm test        # test hàm dựng payload và tính tổng tiền
```

Backend khác cổng thì đặt `VITE_DEV_API_TARGET`. Khi chạy bằng `docker compose`, nginx của app chuyển `/api/webhooks/*` sang `backend:8080`; khi deploy, Caddy làm việc này trên `SIM_DOMAIN`.

## Lấy secret

Owner vào Admin > **Kết nối sàn** > **Tạo kết nối** và copy secret trong hộp thoại (chỉ hiện một lần; mất thì bấm **Đổi secret**). Dán secret vào app, app gọi `GET /api/webhooks/simulator/whoami` để tự điền sàn và mã gian hàng. Có thể dán nhiều secret và chọn qua lại; danh sách chỉ giữ trong bộ nhớ tab.

Tài liệu đầy đủ: [`docs/14-webhook-simulator`](../../../docs/14-webhook-simulator/README.md).

## Kịch bản kiểm tra

| Thao tác | Kết quả mong đợi |
| --- | --- |
| Gửi đơn mới | 201, đơn `Reserved`, hiện ở Admin |
| Gửi lại (cùng mã đơn, cùng nội dung) | 200, trả kết quả lần trước, không tạo đơn mới |
| Đổi nội dung (cùng mã đơn, tăng số lượng dòng đầu) | 409 `WEBHOOK_PAYLOAD_MISMATCH` |
| Bắn N đơn cùng lúc tranh một SKU có ít tồn | Số đơn 201 không vượt tồn khả dụng, phần còn lại báo lỗi tồn kho |
| Bắn N đơn với chung một mã đơn | Đúng một đơn được tạo, các yêu cầu khác trả kết quả gửi lặp |
| Dán secret sai, đã đổi hoặc kết nối đã tắt | Báo lỗi ngay, không thêm kết nối |
