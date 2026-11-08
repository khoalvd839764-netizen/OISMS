# Kế hoạch chuyển POS hiện tại từ Admin sang pos-app, loại bỏ POS tím cũ

Ngày khảo sát: 08/11/2026. Trạng thái thực hiện: **đã di chuyển POS, gỡ giao diện tím cũ và bổ sung realtime**. Chi tiết triển khai, kiểm thử và giới hạn tại [báo cáo kết quả](./tach-pos-khoi-admin-2026-11-08.md). Các mục dưới đây giữ nội dung kế hoạch đã được người dùng chỉnh và duyệt; cột “Hiện tại” mô tả mốc khảo sát trước triển khai.

## 1. Kiến trúc đích đã xác định

**Chuyển màn POS hiện tại từ Admin sang thư mục ứng dụng POS riêng `src/frontend/pos-app`, thay thế hoàn toàn giao diện POS tím cũ.** Admin tập trung quản trị và có liên kết mở POS.

Kế hoạch này thay thế phương án trước giữ POS bên trong Admin và xóa service POS. Thư mục `pos-app`, cổng 5174, service Docker và domain POS tiếp tục được dùng cho giao diện hiện tại sau khi chuyển.

| Thành phần | Hiện tại | Sau triển khai |
| --- | --- | --- |
| Admin | `admin-dashboard`, cổng 5173, chứa quầy `/pos` | Chỉ giữ quản trị; `/pos` là đường dẫn chuyển hướng tương thích sang ứng dụng POS |
| POS mới | `admin-dashboard/src/pages/PosPage.tsx` và component/service của nó | Chuyển sang `pos-app`; chạy ở `http://localhost:5174/` và `https://<POS_DOMAIN>/` |
| POS tím cũ | `pos-app/src/App.tsx`, component và style tím riêng | Xóa sau khi chuyển các test và thành phần hạ tầng còn cần |
| Simulator | `marketplace-simulator`, cổng 5175 | Giữ |
| Backend | API catalog, tồn, thanh toán, hóa đơn, xác thực | Giữ contract và nghiệp vụ đang dùng |
| Docker local | 7 service, 3 frontend | Vẫn 7 service, 3 frontend; service `pos-app` phục vụ POS hiện tại |

Không tạo thêm app/folder POS thứ tư. Trong plan này, “folder POS” là thư mục hiện có `src/frontend/pos-app`.

## 2. Bằng chứng và phạm vi di chuyển

Khảo sát đã dùng CodeGraph để đọc luồng route, màn hình, thanh toán và phụ thuộc, kết hợp kiểm tra cấu hình Docker/CI.

- POS cần giữ nằm ở `admin-dashboard/src/pages/PosPage.tsx`, `PosPage.css`; route `/pos` trong `src/App.tsx` cho Owner/Staff/Cashier.
- Header hiện tại dùng `.pos-retail-header`, CSS `.pos-workstation` với màu chủ đạo `#1769e0`.
- POS tím cũ nằm trong `pos-app`, header có gradient indigo/violet và nhãn “PWA React 19”.
- Hai component Admin `components/pos/CartTable.tsx`, `ProductSearch.tsx` chưa thấy caller trong khảo sát; màn mới dùng `ProductCatalog` và giỏ dựng trong `PosPage`. Đây là ứng viên xóa, không đưa sang app mới theo thói quen.
- `pos-app` có 66 file được Git quản lý gồm cả test, harness và cấu hình. Phải phân loại trước khi thay source.
- `PosPage` phụ thuộc `AuthContext`, React Router và các dịch vụ trong Admin; không thể chỉ chuyển JSX rồi chạy ngay.
- Admin hiện đăng nhập Cashier về `/pos`; cần sửa điều hướng khi tách ứng dụng.
- `pos-app` đang chứa test thật của POS Admin. Chúng phải được chuyển đường dẫn nạp sang implementation mới, không bị xóa cùng giao diện tím.

| Nguồn từ Admin | Đích dự kiến trong pos-app | Cách xử lý |
| --- | --- | --- |
| `src/pages/PosPage.tsx`, `PosPage.css` | `src/pages/PosPage.tsx`, `PosPage.css` | Giữ giao diện và hành vi hiện tại, sửa import/auth/navigation |
| `components/pos/PosHeader.tsx`, `ProductCatalog.tsx`, `QuantityInput.tsx`, `PaymentModal.tsx`, `ReceiptPrintModal.tsx` | `src/components/pos/` | Một bộ component của POS hiện tại; thay liên kết quay về Admin bằng URL cấu hình |
| `services/posApi.ts`, `posCartUnits.ts`, `pendingCheckoutStore.ts`, `paymentAccountSource.ts`, `qrService.ts` | `src/services/` | Chuyển bản đang phục vụ màn mới, kết nối auth riêng của POS; giữ retry và QR snapshot |
| `types/pos.ts`, `types/qr.ts` | `src/types/` | Chuyển các kiểu POS; kiểm tra consumer Admin trước khi bỏ bản nguồn |
| CSS POS/in nằm trong `admin-dashboard/src/index.css` | CSS của POS | Chuyển selector còn cần cho màn hình, modal, receipt và khổ giấy 58/80 mm; tránh thiếu style khi tách. Các selector `.pos-shell`, `.pos-header`, `.pos-status` (`index.css:352–354`, dòng 106–108) có vẻ thuộc layout cũ: kiểm tra caller trước khi chuyển hay xóa |
| Phần xác thực cần cho POS | `src/context/PosAuthContext.tsx` và route guard/service phù hợp | Provider nhỏ chỉ phục vụ POS, có login/me/logout/refresh; **rút gọn từ `AuthContext` của Admin** (refresh token, xử lý 401, generation guard chống phiên cũ ghi đè), không dùng lại auth của app tím (`loginCashier`, `pos_access_token`, không có refresh). Không kéo toàn AdminShell và state đơn quản trị |
| Manifest dành cho quầy hiện tại | `pos-app/public/manifest.webmanifest` | Nhận diện giao diện mới, start URL trỏ POS; Admin có manifest riêng cho quản trị nếu vẫn cần |

Không import xuyên trực tiếp từ `pos-app` sang source `admin-dashboard`. Mỗi frontend phải build được bằng Docker build context riêng hiện có. Kiểu/hàm còn consumer quản trị, ví dụ cấu hình tài khoản nhận tiền, chỉ được bỏ sau khi thay consumer tương ứng; không xóa theo tên file chứa “pos” hay “qr”.

## 3. Các bước triển khai

### Bước 1 — Chốt mốc so sánh

1. Lưu diff/file đang thay đổi của người dùng. Không reset/restore toàn repo.
2. Chụp POS hiện tại ở 5173/pos và ghi nhận giỏ hàng, chọn đơn vị, thanh toán, in hóa đơn, phân quyền.
3. Ghi nhận build/test nền, tách lỗi có sẵn khỏi lỗi do di chuyển.
4. Lập bảng từng file ở `pos-app`: thay bằng bản hiện tại, giữ hạ tầng/helper cần dùng, chuyển test, hoặc xóa phần chỉ phục vụ giao diện tím.

Đã chạy nhóm `node --test test-admin-pos-checkout.mjs test-cart-units.mjs test-qr-parity.mjs` tại `src/frontend/pos-app`: **37/37 đạt**. Đây là mốc fixture/component/helper trước di chuyển, không phải kết quả của phiên bản đã tách.

### Bước 2 — Chuyển giao diện và logic hiện tại sang pos-app

1. Chuyển màn `PosPage`, các component còn dùng, types/services và CSS/in từ Admin sang đúng thư mục POS.
2. Thay entry `pos-app/src/App.tsx` bằng router/provider cho POS hiện tại; route chính `/`, login `/login`, có thể nhận `/pos` làm alias tương thích.
3. Giữ nguyên các luồng: chọn chi nhánh, tìm tên/SKU/barcode, quy cách, lượng lẻ, giá server, kiểm tra tồn, tiền mặt/VietQR, pending checkout, retry, hóa đơn và in.
4. Không ghép lại UI tím, không thêm realtime/offline trong bước này. Realtime làm ở Bước 2b, sau khi bản chuyển đạt mốc giữ nguyên hành vi. Helper cũ chỉ được giữ nếu còn cần cho hành vi đích, có test bảo vệ và không kéo lại implementation giao diện cũ.
5. **Chưa xóa source POS khỏi Admin ở bước này.** `test-admin-pos-checkout.mjs`, `test-cart-units.mjs`, `test-qr-parity.mjs` đang nạp source từ `admin-dashboard`; xóa trước khi chuyển test sẽ làm test và CI đỏ giữa chừng. Việc xóa chuyển sang Bước 5, sau khi Bước 4 đạt.

### Bước 2b — Realtime tồn kho cho POS mới (FR-09)

Đã chốt: làm trong cùng đợt, **là pha riêng, commit riêng**, chỉ bắt đầu khi Bước 2 và Bước 4 đạt (test nền 37/37 và các ca QR đã chuyển nạp từ `pos-app/src`, cộng nghiệm thu thủ công giỏ/thanh toán/in giống mốc Bước 1). Mục đích: lỗi phát sinh phân biệt được do tách app hay do realtime, và hoàn tác được riêng.

Bối cảnh: POS trong Admin hiện không có SignalR; app tím có `realtimeService.ts` và `stockService.ts`. Nếu không làm, POS sau khi chuyển không đáp ứng `FR-SIM-02` (`docs/02-functional-requirements/fr-09-realtime-signalr.md:25`, đẩy biến động tồn tới POS).

Tái dùng:

- `realtimeService.ts` của app tím (kết nối `/hubs/notifications`, generation guard theo phiên B02-F01, `JoinBranchGroup`/`LeaveBranchGroup`, `switchBranch`, `stopRealtimeConnection`, sự kiện `StockLevelChanged`, `Reconnected`). Sửa để lấy token/user từ `PosAuthContext` mới, không đọc `pos_access_token`.
- Không mang nguyên cache tồn và "stock read ticket" trong `api.ts`/`stockService.ts` của app tím vì gắn với `ProductSearch` tím. Chỉ lấy lại quy tắc: số tồn đọc từ server sau một sự kiện mới hơn không bị phản hồi cũ ghi đè.

Phạm vi tối thiểu (4 điểm nối với màn mới):

1. `StockLevelChanged` đúng chi nhánh đang bán: cập nhật `available` trên `ProductCatalog` và giới hạn `maxQuantityForLine` của dòng giỏ liên quan. Giỏ đang vượt tồn mới thì báo cho thu ngân, không tự xóa dòng; checkout vẫn do backend kiểm tra cuối cùng.
2. `Reconnected`: tải lại tồn/catalog của chi nhánh từ server vì có thể đã lỡ sự kiện.
3. Đổi chi nhánh: rời nhóm cũ, vào nhóm mới; bỏ sự kiện đến muộn của chi nhánh cũ.
4. Đăng xuất, 401 hoặc đổi user/tenant: dừng kết nối, tăng generation, không áp sự kiện của phiên trước.

Ngoài phạm vi tối thiểu (tùy chọn, làm sau nếu còn thời gian): âm thanh/popup `OrderCreated`, `LowStockWarning`, chế độ offline.

Không đổi backend, hub, tên sự kiện hay contract. Sự kiện vẫn phát sau commit như hiện tại. Nâng `@microsoft/signalr` của `pos-app` (đang `^8.0.29`, Admin dùng `^10`) chỉ khi cần và phải chạy lại test hub.

### Bước 3 — Tách phiên đăng nhập và điều hướng

Hai ứng dụng ở 5173/5174 hoặc hai domain khác nhau không chia sẻ `localStorage`/`sessionStorage`.

- Mặc định POS có phiên đăng nhập riêng qua backend hiện có, cho Owner/Staff/Cashier. Mở từ Admin mà POS chưa có phiên thì đăng nhập tại POS.
- Không truyền access token, refresh token hoặc mật khẩu qua URL để giả lập đăng nhập một lần. SSO xuyên ứng dụng là thay đổi riêng nếu sau này được yêu cầu.
- Provider/service POS dùng định danh tenant/user đã xác thực; giữ xử lý refresh, 401, logout và chống phản hồi phiên cũ ghi đè phiên mới.
- Pending/QR của POS mới phải thuộc đúng tenant + user + branch; không tự lấy trạng thái chưa rõ của người dùng khác.
- Thêm `VITE_POS_URL` cho Admin, mặc định local `http://localhost:5174`; production nhận `https://<POS_DOMAIN>`.
- Thêm `VITE_ADMIN_URL` cho POS, mặc định `http://localhost:5173`; production nhận `https://<ADMIN_DOMAIN>`. Owner/Staff quay lại Admin được; Cashier vẫn bị chặn route quản trị.
- Nút mở POS trong Admin dùng điều hướng ngoài ứng dụng. Route Admin `/pos` cũ chỉ chuyển hướng sang POS mới, không còn render/bundle bản quầy thứ hai.
- Cập nhật landing path Cashier trong luồng login/khôi phục phiên Admin, tránh vòng lặp giữa hai app.
- Khi Cashier đăng nhập ở Admin rồi bị chuyển sang POS, đăng xuất/xóa phiên Admin (`oism_access_token`, `oism_refresh_token`) trước khi chuyển hướng, để origin 5173 không còn token Cashier dùng được.
- POS dùng tên khóa storage riêng (ghi rõ trong code và docs), không dùng lại `pos_access_token` của app tím để tránh đọc nhầm token cũ còn sót trên origin 5174.

### Bước 4 — Chuyển test sang implementation POS mới

Giữ test trong `src/frontend/pos-app` theo cấu trúc `tests/` và `test-support/` nhất quán.

| Test hiện có | Thay đổi |
| --- | --- |
| `test-admin-pos-checkout.mjs` + `test-support/admin-pos-runtime.cjs` | Đổi tên bỏ “admin”, nạp `pos-app/src` mới; giữ 6 ca retry/RequestKey/QR/nguồn tài khoản |
| `test-cart-units.mjs` | Chỉ nạp helper của POS đích; giữ 6 ca của implementation đang dùng |
| `test-qr-parity.mjs` | Chuyển thành test hành vi QR của POS; giữ 9 ca của implementation hiện tại, bỏ so sánh hai bản POS |
| `test-b03-qr.mjs` + `test-support/qr-runtime.cjs` | Đang nạp `PaymentModal`/`qrService`/`paymentAccountSource` của app tím. Trỏ harness sang `PaymentModal` và service mới (logic CS-05); giữ 12 ca nếu còn áp dụng, ca nào bỏ phải ghi lý do |
| `test-b02-hub-preflight.cjs`, `test-b02-hub-transport.mjs`, `test-b02-realtime-pwa.mjs`, `test-b02-live-stock-regression.mjs` | Dùng cho Bước 2b: trỏ sang `realtimeService` và màn mới; ca không cần backend vào `npm test`, ca live chạy riêng trên backend/database cô lập |
| `test-pos-e2e.mjs`, các test B02/B03 khác | Rà từng ca; chuyển các quy tắc còn áp dụng, bỏ giả định UI/state index/đường dẫn của app tím; test nào chỉ áp dụng tính năng bị ngừng phải ghi lý do |
| Helper và test live | Chỉ giữ helper có caller; test ghi giao dịch tách khỏi lệnh mặc định, dùng backend/database cô lập |

Mốc tối thiểu: 21 ca nghiệp vụ lấy từ implementation hiện tại vẫn đạt (6 checkout + 6 đơn vị + 9 QR), cộng các ca của `test-b03-qr.mjs` còn áp dụng sau khi trỏ sang `PaymentModal` mới, cộng test login/guard/điều hướng giữa hai app và các ca còn phù hợp đã chuyển. Không giữ test nạp source POS từ Admin sau khi tách.

### Bước 5 — Dọn source cũ và cấu hình còn dư

1. Xóa component, style, assets, auth modal, service worker logic và service chỉ dùng cho giao diện tím sau khi kiểm tra caller.
2. Chỉ khi Bước 4 đạt (test nạp từ `pos-app/src`, tối thiểu 37/37 ca nền cộng các ca QR đã chuyển), xóa component POS trong Admin đã chuyển; xóa `CartTable`/`ProductSearch` dư nếu xác nhận không dùng. Gỡ import và CSS chết.
3. Bỏ ghi chú bắt buộc đồng bộ hai bản POS; giữ một implementation quầy tại `pos-app`.
4. Cập nhật manifest Admin không còn nhận diện/start URL của quầy; manifest POS nhận giao diện hiện tại.
5. Giữ backend POS, API/DTO, hóa đơn và cấu hình nhận tiền của Owner. Chỉ sửa comment trỏ sai source, không thay contract nghiệp vụ.
6. Không xóa CORS 5174 vì POS vẫn chạy ở đây. Kiểm tra origin/API hiện có đủ cho hai ứng dụng.
7. Không đổi/xóa hàng loạt pending checkout để “clear cache”.

### Bước 6 — Docker, CI và script

| File/phạm vi | Kế hoạch đúng sau khi tách |
| --- | --- |
| `docker-compose.yml` | Giữ service `pos-app`, container `oism_pos_app`, port 5174; truyền `VITE_POS_URL` cho Admin và `VITE_ADMIN_URL` cho POS lúc build |
| `docker-compose.prod.yml` | Giữ override POS, `POS_DOMAIN`, dependency Caddy; cấu hình URL Admin/POS đối ứng từ domain |
| `docker/caddy/Caddyfile` | Giữ `POS_DOMAIN` → `pos-app:80`, Admin → `admin-dashboard:80`; giữ API/hub proxy cùng origin |
| Dockerfile hai frontend | Bổ sung build args/env tương ứng vì Vite nhúng URL lúc build; không chỉ đặt environment ở container runtime. Bước build trong CI cũng phải có giá trị mặc định cho `VITE_POS_URL`/`VITE_ADMIN_URL` |
| `.env.example`, `.env.production.example` | Giữ `POS_PORT`, `POS_DOMAIN`; bổ sung ví dụ URL nếu cần, không đưa bí mật vào docs/log |
| `pos-app/vite.config.ts` | Cố định dev port 5174 và strict port để không tự rơi về 5173 gây nhầm ứng dụng |
| `.github/workflows/ci.yml` | Giữ build Admin/POS/Simulator; POS regression tests trỏ source mới, không còn phụ thuộc implementation Admin đã gỡ |
| `scripts/run-pos.ps1` | Tiếp tục chạy `pos-app` trên 5174 |
| `scripts/build-all.ps1`, `start-docker.ps1` | Giữ build/chạy cả hai ứng dụng; chạy test POS và báo lỗi đúng khi bước build/test thất bại |

Tái dùng cấu hình nginx SPA fallback, xác minh refresh trực tiếp route login/POS và asset thiếu trả 404. Rebuild đúng hai frontend sau di chuyển; không `down -v`, không prune volume hoặc xóa dữ liệu.

### Bước 7 — Cache và trạng thái chuyển tiếp

- POS cũ và POS mới dùng chung origin 5174/domain POS. Đã kiểm tra `pos-app/public/sw.js` (đăng ký trong `index.html`, cache `oisms-pos-v1.0`): request trang (navigate) theo **network-first**, chỉ trả `index.html` trong cache khi mất mạng; asset tĩnh cache-first nhưng tên file Vite có hash nên bundle mới không trùng asset cũ; không can thiệp `/api`, `/hubs`, request khác GET. Vì vậy khi online, trình duyệt từng dùng app tím vẫn nhận shell mới; rủi ro chỉ còn ở lần mở offline trước khi worker cập nhật.
- Phương án mặc định: **giữ `sw.js`**, tăng `CACHE_VERSION` (ví dụ `v2.0`) để `activate` xóa cache `oisms-pos-v1.0` cũ, giữ precache `/`, `/index.html`, manifest và icon của giao diện mới.
- Nginx của `pos-app` trả `Cache-Control: no-cache` cho `/sw.js` (và `index.html`) để trình duyệt luôn kiểm tra worker mới.
- Chỉ dùng worker chuyển tiếp để unregister nếu quyết định bỏ PWA cho POS; khi đó vẫn phục vụ `/sw.js` hợp lệ, không để 404.
- Kiểm tra nâng cấp trên trình duyệt đã từng mở/cài app tím (online và offline), không chỉ tab sạch.
- Pending checkout chưa rõ trong Admin thuộc origin 5173; không tự chuyển sang 5174 bằng URL hoặc tự gửi lại. Hoàn tất/đối soát các giao dịch này trước chuyển đổi, giữ dữ liệu gốc cho đến khi rõ kết quả.
- Không xóa cache/storage toàn bộ Admin hoặc tự tạo giao dịch mới để thay thế yêu cầu chưa rõ kết quả.

### Bước 8 — Đồng bộ docs và nghiệm thu

Tài liệu hiện hành phải phản ánh: Admin 5173, **POS mới 5174 nhưng dùng giao diện vừa chuyển từ Admin**, Simulator 5175; vẫn 3 frontend/7 service local.

Cập nhật `CLAUDE.md`: dòng mô tả `pos-app` (POS hiện tại chuyển từ Admin, không còn `/pos` trong Admin), dòng tiến độ CS-05 ("VietQR trên cả POS Admin và POS PWA" → một POS ở `pos-app`), dòng POS đa đơn vị (`posCartUnits.ts` chỉ còn một bản, bỏ "sửa thì sửa cả hai"), cấu trúc `admin-dashboard` và lệnh test POS nếu đổi tên/đường dẫn.

Cập nhật README gốc và docs kiến trúc, UI, API, CI/DevOps/deploy, hướng dẫn test và đa đơn vị/VietQR. Source quầy trỏ về `pos-app/src/pages/PosPage.tsx`. Ảnh giao diện POS hiện tại vẫn là ảnh chuẩn, chụp lại ở origin mới nếu chú thích/URL bị ảnh hưởng. Confluence sửa trên trang đã thống nhất, không tạo thêm trang chỉ cho đợt này. Giữ báo cáo/evidence lịch sử theo thời điểm.

Tiêu chí nghiệm thu:

- Admin/POS/Simulator build độc lập; test POS mới chạy từ `pos-app` và CI đạt.
- Chỉ còn một implementation giao diện quầy trong `pos-app`; Admin không bundle POS hay import xuyên source hai ứng dụng.
- Mở 5174 thấy đúng giao diện hiện tại, không còn POS tím; kiểm tra cả trình duyệt từng cài PWA/cache cũ.
- Admin mở POS đúng URL; bookmark Admin `/pos` chuyển hướng đúng; POS chưa có phiên yêu cầu login, không lặp redirect.
- Login Owner/Staff/Cashier, refresh/F5, logout/401, đổi tenant/user đều đúng; Cashier không truy cập quản trị.
- Giỏ/quy cách/giá/tồn/QR/tiền mặt/in 58–80 mm giữ hành vi hiện tại; retry vẫn đúng RequestKey/payload/QR snapshot.
- Realtime (Bước 2b), có bằng chứng trên backend + PostgreSQL thật, không chỉ mock: tab A checkout thì tab B cùng chi nhánh thấy tồn giảm và giới hạn giỏ cập nhật; đổi chi nhánh không nhận sự kiện chi nhánh cũ; đăng xuất rồi đăng nhập user/tenant khác không nhận sự kiện phiên trước; mất kết nối rồi kết nối lại thì tồn được tải lại đúng.
- Sau khi Bước 2b đạt, cập nhật trạng thái FR-09 phần POS trong `docs/02-functional-requirements/README.md`.
- Checkout thử trên môi trường cô lập chỉ tạo một đơn, payment, invoice và ledger/tồn đúng; không ghi vào dữ liệu demo chụp tài liệu.
- Compose local/production vẫn có upstream POS hợp lệ; 5173 và 5174 đều chạy đúng ứng dụng, dữ liệu DB/Redis không mất.
- Không còn test/source/script/docs hiện hành trỏ tới implementation POS trong Admin đã gỡ. Tham chiếu “POS tím” chỉ còn ở ghi chú migration/lịch sử có chủ đích.
- Review diff không làm mất các thay đổi README/docs đã có của người dùng.

## 4. Khôi phục và giới hạn kiểm chứng

Lưu mốc code/image trước di chuyển; nếu có hồi quy, khôi phục riêng diff/image của đợt tách. Nếu lỗi chỉ thuộc realtime, hoàn tác riêng commit Bước 2b, giữ bản chuyển đã đạt. Không cần migration hay xóa volume DB. Khôi phục phải tính cả bundle/worker cache và các pending checkout ở origin cũ.

Tại mốc khảo sát trước triển khai mới có nhóm test nền 37/37 và lỗi quyền truy cập Docker named pipe. Các giới hạn tại mốc đó đã được kiểm tra lại trong đợt triển khai; xem kết quả build, 88 test POS, 7 kịch bản tích hợp thật và nâng cấp PWA trong báo cáo liên kết ở đầu tài liệu. Chưa triển khai production hoặc in bằng máy in vật lý.
