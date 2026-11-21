# Báo cáo kiểm chứng — OISMS-66-FIN-E (`NFR-SEC-01`, `NFR-USA-01`, `NFR-USA-02`, `BF-026`)

- **Ngày:** 2026-11-21
- **Commit nền:** `879a1ef`

## 1. Đã làm

- **`docker/caddy/Caddyfile` (`NFR-SEC-01` & `BF-026`):**
  - Thêm `tls { protocols tls1.3 }` cho cả 3 site (`{$ADMIN_DOMAIN}`, `{$POS_DOMAIN}`, `{$SIM_DOMAIN}`).
  - Chặn `/health/ready` từ bên ngoài (`respond 403`), chỉ forward `/health/live` tới `backend:8080`. Giữ nguyên `/health/ready` trong `Program.cs` cho healthcheck nội bộ.
- **`test-oisms/nfr/uptime-probe/` (`NFR-USA-01`):**
  - Viết `uptime-probe.mjs` (Node.js thuần, không thư viện ngoài) thăm dò `/health/live` và trang POS, lọc khung giờ `07:00–22:00 Asia/Ho_Chi_Minh`, xuất CSV + JSON tổng hợp.
- **`test-oisms/frontend/pos-app/test-live.mjs` (`NFR-USA-02`):**
  - Bổ sung kiểm chứng luồng `Barcode + Enter → F4 → Xác nhận` hoàn tất trong `2` bước (`< 3` bước); đồng thời sửa gọi `/api/purchase-orders/{id}/approve` truyền `version` theo contract hiện hành.

## 2. Kết quả kiểm chứng

| Mã | Môi trường | Kết quả | Trạng thái chính thức |
| --- | --- | --- | --- |
| `NFR-SEC-01` & `BF-026` | Caddy `2.10-alpine` local (`admin.localhost`, cổng `8443`) | `openssl -tls1_3` → `TLSv1.3 / TLS_AES_128_GCM_SHA256`; `-tls1_2` → bị từ chối (`tlsv1 alert protocol version`); `/health/live` → `200`; `/health/ready` qua Caddy → `403`; nội bộ `localhost:5000/health/ready` → `200 Healthy` | **ĐẠT local / CHƯA ĐO TRÊN DEPLOY** |
| `NFR-USA-01` | Local (`localhost:5000`, `localhost:5174`) | Smoke test 5 lần (2s/lần): `100%` trong khung `07:00–22:00 UTC+07` | **CHƯA ĐO TRÊN DEPLOY** (và chưa chạy soak 1 giờ trên local) |
| `NFR-USA-02` | `docker/pos-tests/compose.yml` + `PosPage.tsx` | `npm run test:live --prefix src/frontend/pos-app` → **PASS 8/8** (gồm check `NFR-USA-02: 2 bước < 3`) | **ĐẠT phần logic bước / chưa có ảnh 3 kích thước (máy dev thiếu Chromium) & chưa kiểm thiết bị cầm tay thật** |
