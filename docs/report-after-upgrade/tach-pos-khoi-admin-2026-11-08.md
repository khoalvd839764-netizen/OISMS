# Tách POS hiện tại khỏi Admin — 08/11/2026

## Kết quả triển khai

Giao diện quầy hiện tại được chuyển từ `admin-dashboard` sang `pos-app`, thay thế POS tím cũ. Admin tại 5173 giữ quản trị; nút POS và bookmark `/pos` chuyển sang POS tại 5174. Simulator tiếp tục dùng 5175. Không đổi contract backend, schema hoặc dữ liệu chính.

POS có đăng nhập, refresh token, kiểm soát phiên và storage riêng; phiên Cashier trong Admin được thu hồi trước khi chuyển ứng dụng. Giỏ, đơn vị bán, giá, QR, tiền mặt và pending checkout giữ phạm vi tenant/user/chi nhánh cùng RequestKey khi thử lại.

SignalR cập nhật tồn theo chi nhánh, tải lại tồn từ server sau reconnect, loại sự kiện cũ khi đổi chi nhánh/phiên. Giỏ giữ số lượng và cảnh báo khi vượt tồn mới; backend quyết định kết quả checkout. Chưa coi toàn bộ FR-09 đã hoàn tất: popup/âm thanh LowStock và POSOrderCreated không thuộc đợt này.

Hóa đơn sau checkout lấy snapshot server; giao diện in hỗ trợ 58/80 mm. PWA dùng cache `v2.1`, cập nhật tài nguyên giao diện và không cache API, hub hoặc checkout. Không hỗ trợ checkout offline.

## Các mốc mã nguồn

- `2f570ca`: di chuyển giao diện, phiên POS độc lập, redirect Admin và loại bỏ POS tím cũ.
- `f46d26b`: realtime tồn theo phiên/chi nhánh, cùng kiểm chứng tích hợp riêng.
- `ae6069d`: snapshot hóa đơn, khổ in 58/80, cache PWA và kiểm chứng bổ sung.

Nhánh thực hiện: `codex/extract-current-pos`. Không push hoặc triển khai production.

## Kiểm chứng

| Phạm vi | Kết quả |
| --- | --- |
| POS unit/component/service | 88/88 đạt, gồm auth, idempotency, quy cách, QR, realtime, receipt và worker |
| Backend build và unit test | Build 0 warning/error; 268/268 test đạt |
| Admin/POS/Simulator | Cả ba build đạt; Simulator lint và 6/6 test đạt |
| POS lint | Không có error; còn 5 warning |
| PostgreSQL + SignalR thực | 7/7 kịch bản đạt trên DB `oisms_pos_migration_test` |
| Hai tab POS thật | Tab A checkout; tab B nhận tồn mới, cảnh báo giỏ vượt tồn và giữ lượng giỏ |
| Redirect/phân quyền | Admin `/pos` mở 5174; Cashier không giữ phiên Admin; Owner đăng nhập lại Admin bình thường |
| Nâng cấp PWA | Cùng origin đã mở POS tím qua cache cũ, nâng lên giao diện mới, sau đó mở lại giao diện mới khi server fixture dừng |
| Hóa đơn | Kiểm chứng snapshot/khổ giấy bằng test và preview; chưa in bằng máy in vật lý |

7 kịch bản thật gồm Cashier login/refresh/me; checkout cập nhật catalog và giới hạn giỏ của bên nhận; replay chỉ một order; replay chỉ một payment/invoice/OUT; cô lập chi nhánh; ngắt TCP thật rồi tự reconnect/rejoin/refetch; logout/login tenant khác không nhận sự kiện cũ.

Bằng chứng nằm trong `tmp/pos-migration-2026-11-08/`: `live-report.json`, `unit-tests.log`, `realtime-two-tabs-cart-warning.png`, `upgraded-pwa-offline.png`, `receipt-58-preview.png`. Đây là bằng chứng local, không thay cho benchmark NFR hoặc triển khai production. Checkout kiểm thử dùng DB cô lập; ảnh tài liệu chính dùng giỏ tạm và không gửi checkout.

## Docker và tài liệu

Theo yêu cầu cuối, đã xóa 4 container fixture/test, network test và 2 image test riêng. Build cache đã dọn 4.336 GB. Volume chính `oisms_postgres_data`, `oisms_redis_data` được giữ nguyên (ngày tạo vẫn là 07/11/2026).

Đã chạy `docker compose build --no-cache` thành công cho backend, Admin, POS và Simulator, sau đó `docker compose up -d --no-build --force-recreate`: cả 7 container chính chạy. Lỗi DNS Docker Hub ban đầu hết khi thử lại; không thay cấu hình DNS của máy. `/health/ready`, ba frontend 5173/5174/5175 và `sw.js` đều trả HTTP 200. HTML POS có `Cache-Control: no-cache`; worker có `no-store, must-revalidate, no-cache`.

Đã mở lại Admin và POS sau rebuild: Owner vẫn truy cập được quản trị; POS hiển thị tồn 110, realtime trực tiếp. Kiểm tra phiếu tạm tính 58 mm bằng giao diện mới, đóng phiếu và xóa giỏ minh họa mà không gửi checkout. Log build tại `tmp/pos-migration-2026-11-08/docker-rebuild.log`.

Bước dọn bổ sung bốn image chính cũ bị auto-review từ chối vì ngoài phạm vi xóa Docker test và ảnh hưởng rollback. Không xóa các image chính đó; việc xóa fixture/test và rebuild vẫn hoàn thành.

README, tài liệu chạy local/deploy/CI và các tham chiếu POS hiện hành được sửa theo kiến trúc mới; ghi chú lịch sử giữ mốc thời gian. Danh sách chuyển test nằm trong `src/frontend/pos-app/TEST-MIGRATION.md`.

Confluence hiện dùng trang chính 983053. Hai trang con cũ đã được người dùng hợp nhất và xóa. Ảnh `3.5.1-pos-current.png` đã được đính kèm trang chính; văn bản mới được cung cấp tại [nội dung để copy](./noi-dung-confluence-pos-2026-11-08.md), chờ người dùng dán. Connector từ chối cập nhật body vì trang vượt 100000 JSON node; không tạo thêm trang con.
