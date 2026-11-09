# OISMS POS

Quầy hiện tại chuyển từ Admin vào `src/pages/PosPage.tsx`, thay thế giao diện tím cũ. Admin chỉ quản trị và mở POS qua liên kết; chỉ duy trì một implementation quầy.

## Chạy và kiểm tra

`npm ci`, `npm run dev`: cổng 5174 cố định. Chạy `npm run build`, `npm test`, `npm run lint` để kiểm tra. Từ gốc repo: `docker compose up -d --build admin-dashboard pos-app`.

## URL và phiên đăng nhập

- `VITE_API_BASE_URL`: mặc định `http://localhost:5000`, không có đuôi `/api`.
- `VITE_ADMIN_URL`: mặc định `http://localhost:5173`; Admin dùng `VITE_POS_URL=http://localhost:5174`.
- Vite nhúng URL lúc build; Dockerfile/Compose truyền qua build args. Production dùng domain tương ứng.
- POS có login/me/refresh/logout riêng, khóa `oisms_pos_v2_access`, `oisms_pos_v2_refresh`, `oisms_pos_v2_tenant`, `oisms_pos_v2_identity`. Không đọc token app tím hay chuyển JWT qua URL từ Admin.
- Pending `pos_pending_checkout_v1` giữ snapshot theo tenant/user/branch trong sessionStorage. Retry dùng nguyên RequestKey/payload. Các tab đồng bộ thay đổi định danh; bỏ response/event của phiên cũ.

## Realtime, hóa đơn và PWA

`realtimeService.ts` nối hub notifications và quản lý nhóm chi nhánh; `branchStock.ts` đọc số tồn từ API, loại GET cũ. Reconnect tải lại tồn, không tự xóa dòng giỏ vượt tồn. Popup/âm thanh đơn mới nằm ngoài pha này.

Hóa đơn chính thức dùng snapshot server; response lỗi giữ pending. Hỗ trợ chọn giấy 58/80 mm. VietQR xác nhận tay, chưa có webhook ngân hàng.

Worker v2.1 dọn cache POS cũ, không cache API/hub/checkout. Cache hỗ trợ shell offline; không hỗ trợ bán/ghi giao dịch offline.

## Kiểm thử live cô lập

Từ gốc repo, chạy `docker compose -f docker/pos-tests/compose.yml up -d --build`, rồi `npm run test:live --prefix src/frontend/pos-app`.

Guard kiểm chứng container/port/database `oisms_pos_migration_test` trước POST. Backend 55001, PostgreSQL 55433, UI test `http://localhost:55174`; không trỏ runner vào demo 5000. Fixture credentials chỉ dành cho test.

Xem [ma trận chuyển test](../../../test-oisms/frontend/pos-app/TEST-MIGRATION.md) và [báo cáo kiểm chứng](../../../docs/report-after-upgrade/tach-pos-khoi-admin-2026-11-08.md).
