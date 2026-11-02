# Handover — S3-POS-B02 / v1.1 — Tồn realtime, PWA và thao tác quầy

- **Ngày bàn giao:** 28/10/2026, Asia/Saigon
- **Trạng thái:** **`READY_FOR_REVIEW v1.1`** *(Chờ Codex Reviewer nghiệm thu, không tự nhận VERIFIED)*
- **Người thực hiện:** Antigravity (Frontend POS Phương Lê)
- **Nhánh:** `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`
- **Baseline HEAD:** `44317f3b5188c276fa3ef25c8faf30323e503079`
- **Working tree:** Giữ trọn vẹn T01 v1.3 và B01 v1.2 đã VERIFIED (chưa commit), tích hợp hoàn chỉnh toàn bộ bản sửa lỗi R1 v1.1 cho B02.

---

## 1. Báo cáo khắc phục các Finding Review R1 (B02-F01 đến B02-F06)

Bản v1.1 giải quyết dứt điểm toàn bộ 6 finding được nêu trong `reviews/v1.0-review-r1.md` và tuân thủ nghiêm ngặt các điều kiện đóng trong `reviews/v1.1-fix-prompt.md`:

### B02-F01: Vòng đời SignalR Hub, huỷ bỏ start in-flight và cô lập đa phiên (Session Isolation)
- **Vấn đề v1.0:** Khi gọi `startRealtimeConnection` liên tục hoặc logout khi kết nối đang `start()` dở dang, kết nối cũ hoàn tất muộn sẽ tự ý đăng ký callbacks, phát event hoặc ghi đè trạng thái của phiên mới.
- **Khắc phục v1.1:**
  - Thêm biến đếm thế hệ phiên `currentGeneration` và con trỏ `activeStartingConnection` trong `src/services/realtimeService.ts`.
  - Mỗi lần gọi `startRealtimeConnection` hoặc `stopRealtimeConnection`, thế hệ `currentGeneration` tăng lên. Nếu có kết nối trước đó (kể cả đang ở trạng thái `Connecting`), hệ thống ngắt ngay lập tức (`conn.stop()`).
  - Tất cả các listener sự kiện (`OrderCreated`, `StockLevelChanged`, `LowStockWarning`), các hook vòng đời (`onreconnecting`, `onreconnected`, `onclose`) và callback hoàn tất của `start()` đều kiểm tra nghiêm ngặt `sessionGen === currentGeneration`. Nếu thế hệ đã cũ, kết nối lập tức bị đóng và không bao giờ emit sự kiện hoặc can thiệp vào UI.
- **Kiểm chứng:** Subtest `B02-F01-REGRESSION` mô phỏng connection A start trễ 10ms trong khi logout & start B ngay lập tức. Kết quả: Connection B hoạt động độc lập, connection A bị hủy và dừng hoàn toàn (`stopCount === 2`), không có bất kỳ event nào của A rò rỉ sang B (`received.length === 0`).

### B02-F02: Thống nhất kho tồn duy nhất và quét Barcode nhận tồn 8 (Đóng dứt điểm POS-STOCK-F01)
- **Vấn đề v1.0:** Tồn kho bị phân mảnh giữa `stockService.ts` và `api.ts`. Trong `ProductSearch.tsx`, sau khi checkout thành công (tồn giảm từ 10 xuống 8), quét barcode mới nhận API tồn 8 nhưng bị code fallback ghi đè bằng giá trị cũ trong `stockMap` (10), khiến thuộc tính `max` của numeric input trong giỏ hàng vẫn là 10 thay vì 8.
- **Khắc phục v1.1:**
  - Thống nhất `src/services/stockService.ts` trở thành proxy chuyển tiếp trực tiếp vào store duy nhất `stockStore` của `src/services/api.ts`.
  - Trong `ProductSearch.tsx`, tại hàm `handleKeyDown` (xử lý barcode), response trả về từ API ngay lập tức được ghi vào store chuẩn và chuyển nguyên vẹn `item.available` sang cho `onAddToCart`.
- **Kiểm chứng:**
  - Subtest `B02-F02-REGRESSION`: Cả `api.ts` và `stockService.ts` đều trả về đúng 8, `addedAvailable: 8`.
  - Browser Production Playwright (`test-b02-browser-production.mjs`): Chạy trên production build với Hub offline. Sau khi mua 2 sản phẩm (10 -> 8), đóng hóa đơn, quét barcode: ô input số lượng trong giỏ hàng có `max = "8"` chính xác (khắc phục điểm assertion fail trong `browser-stock.cjs`).

### B02-F03: Khớp chính xác SKU trong `invalidateAndRefetchStock`, loại bỏ fallback sai lệch
- **Vấn đề v1.0:** Hàm `invalidateAndRefetchStock` trong `api.ts` sử dụng fallback `|| searchHits[0]`. Khi SKU cần tìm không còn trong danh mục nhưng search trả về sản phẩm khác có cùng tiền tố, SKU đó bị gán nhầm số tồn của sản phẩm khác (ví dụ: gán 99 từ `SKU-A-OTHER`).
- **Khắc phục v1.1:**
  - Loại bỏ hoàn toàn fallback `|| searchHits[0]`.
  - Kiểm tra điều kiện khớp chính xác: `hit.variantId === target.skuId || hit.sku === target.skuCode`.
  - Nếu không tìm thấy sản phẩm khớp, giữ nguyên tồn cũ, đánh dấu `isStale: true` và ghi nhận `lastRefetchError`, không bao giờ gán tồn của sản phẩm khác.
- **Kiểm chứng:** Subtest `B02-F03-REGRESSION`: Tìm `SKU-A` khi kết quả chỉ có `SKU-A-OTHER` (tồn 99). Kết quả: `refetch.success === false`, SKU-A giữ nguyên tồn 10 (không bị đổi thành 99) và có `isStale: true`.

### B02-F04: Event SignalR kích hoạt API Refetch và Guard chống response đảo thứ tự
- **Vấn đề v1.0:** Các sự kiện SignalR trực tiếp ghi đè số dư từ event vào store; event cũ đến muộn có thể ghi đè số dư mới từ API GET.
- **Khắc phục v1.1:**
  - Handler của `StockLevelChanged` và `LowStockWarning` trong `realtimeService.ts` chuyển đổi việc nhận event thành kích hoạt `invalidateAndRefetchStock` qua REST API chuẩn của branch.
  - Thêm biến sequence guard `stockRefetchSeq` trong `api.ts`. Mỗi lần yêu cầu refetch được phát đi, số sequence tăng dần; các response đến sau nhưng mang số sequence cũ hơn sẽ bị hủy bỏ, ngăn chặn hoàn toàn race condition dữ liệu.
- **Kiểm chứng:** Subtest `B02-F04-REGRESSION`: Sau khi GET chốt tồn 8, phát event cũ có tồn 10 trễ 5ms. Event kích hoạt refetch API và kết quả tồn cuối cùng vẫn giữ nguyên là 8, không bị lùi về 10 và không bị trừ thêm thành 6.

### B02-F05: Guard yêu cầu Barcode in-flight khi chuyển chi nhánh và Unmount
- **Vấn đề v1.0:** Khi quét barcode ở Chi nhánh A và chuyển ngay sang Chi nhánh B trong khi request của A đang bay (in-flight), callback hoàn tất của A so sánh với `branchId` bị stale trong closure nên vẫn thêm sản phẩm của Chi nhánh A vào giỏ hàng Chi nhánh B.
- **Khắc phục v1.1:**
  - Sử dụng `currentBranchRef = useRef(branchId)` cập nhật liên tục qua mỗi render trong `ProductSearch.tsx`.
  - Trong `useEffect([branchId])`, tăng `latestRequestIdRef.current++` và gọi `abort()` trên tất cả các `AbortController` đang chạy.
  - Trong hàm barcode `handleKeyDown`, kiểm tra chặt chẽ `requestId === latestRequestIdRef.current` và `requestBranchId === currentBranchRef.current` ở cả 3 khối `success`, `catch` và `finally`.
  - Khi unmount component, tăng `latestRequestIdRef.current++` để hủy bỏ toàn bộ pending handlers.
- **Kiểm chứng:** Subtest `B02-F05-REGRESSION`: Khởi chạy barcode ở Chi nhánh A với độ trễ 10ms, chuyển ngay sang Chi nhánh B ở thời điểm 2ms. Khi response A trả về, request bị hủy bỏ hoàn toàn (`addedAfterSwitch.length === 0`).

### B02-F06: Đo lường độ trễ thực tế trên Backend và Kiểm chứng WebSocket Transport thật
- **Vấn đề v1.0:** Thiếu dataset phân rã độ trễ mạng/backend/UI thật trên môi trường chạy; kiểm chứng Hub transport dùng UUID ngẫu nhiên thay vì dữ liệu fixture cross-tenant chuẩn của hệ thống.
- **Khắc phục v1.1:**
  - **Kiểm chứng WebSocket Transport thật (`test-b02-hub-transport.mjs`):**
    * Kết nối WebSocket thật tới backend container đang chạy tại `http://localhost:5000/hubs/notifications`.
    * Sử dụng JWT của Cashier A (Tenant A: `11111111-1111-1111-1111-111111111111`).
    * Tham gia thành công chi nhánh sở thuộc `11111111-0000-0000-0001-000000000001` (`JoinBranchGroup`).
    * Kiểm chứng cơ chế bảo mật cô lập đa khách thuê: Yêu cầu tham gia chi nhánh thật của Tenant B (`22222222-0000-0000-0001-000000000001`) bị backend từ chối với lỗi bảo mật xác thực: `Chi nhánh không tồn tại, đã ngừng hoạt động hoặc không thuộc Tenant của bạn`.
    * Kiểm chứng ngắt kết nối (`disconnect`), kết nối lại (`reconnect`) và tự động tham gia lại branch group.
    * Tích hợp thành công với production `realtimeService.ts`.
  - **Đo lường Benchmark độ trễ thực tế (`measure-pos-latency.mjs`):**
    * Thực hiện 50 yêu cầu barcode liên tục tới endpoint backend thật `http://localhost:5000/api/pos/barcode/8934187503554?branchId=11111111-0000-0000-0001-000000000001`.
    * Kết quả phân rã chi tiết được lưu tại [latency-benchmark.json](./evidence/v1.1/latency-benchmark.json):
      - **Mạng + Backend REST (p95):** **14.139 ms** (Min: 7.012 ms, Avg: 10.871 ms, Max: 17.323 ms)
      - **Tra cứu in-memory map client (p95):** **0.001 ms**
      - **Render UI estimate (p95):** **0.150 ms**
      - **Tổng thời gian xử lý tra cứu không kể debounce (p95):** **14.290 ms** *(Đạt xuất sắc mục tiêu brief < 200 ms)*.
  - **Ghi nhận minh bạch giới hạn thiết bị (Hardware Disclaimer):**
    * Kiểm thử thiết bị in vật lý (POS Thermal Printer 58mm/80mm) và máy quét mã vạch phần cứng USB/Bluetooth được ghi nhận trạng thái **NOT RUN** (môi trường agent không có phần cứng vật lý đính kèm; kiểm chứng trên Playwright Chromium/Edge Print Preview & giả lập phím Enter barcode).

---

## 2. Bảng đối chiếu mã SHA256 (Baseline vs Tested Working Tree v1.1)

Chi tiết đầy đủ được lưu tại file: [working-tree-hashes.json](./evidence/v1.1/working-tree-hashes.json).

| File | Trạng thái | SHA256 Tested Working Tree v1.1 | Ghi chú |
| --- | --- | --- | --- |
| `src/frontend/pos-app/src/App.tsx` | MODIFIED | `16FD4F76933B0BFDA156FA6BE59D45FFA3F1E9F8540F6E79C9CA1A4124E74E06` | Bảo toàn hook index 0-9; refresh stock sau checkout |
| `src/frontend/pos-app/src/components/LoginModal.tsx` | **UNCHANGED** | `C800F3DAF92DDDB6827FD2465180DE8AE652CB21FA5E10B49BB2046473656FC7` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/components/PaymentModal.tsx` | **UNCHANGED** | `419B8AC9641AD904C4F9B037773B800F2B49F8B44A2FE5A3A77EFAF7FE1AFFA3` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/components/ReceiptPrintModal.tsx` | **UNCHANGED** | `941C4E448B6019BD624EBD30935FCB6CDD11A042138164B35EFBBA19B8BEA9A1` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/components/CartTable.tsx` | **UNCHANGED** | `34F8C3603753163CDD9BEAE1CAE2A58C580C0C8EA119C61F08FD2347C6D67F44` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/components/ProductSearch.tsx` | MODIFIED | `B7EE959A170077491C0ED1B29AED9137403D8A8E06E6C29B0D4AD54956FBA801` | Sửa B02-F02 (fresh barcode stock 8) & B02-F05 (currentBranchRef guard) |
| `src/frontend/pos-app/src/components/PosHeader.tsx` | MODIFIED | `468A98ECCA9D1DF582FCB2BFF1794E1BE6DCF589063CDE4B613AF8C1316B1A52` | Realtime status badge, chime mute toggle |
| `src/frontend/pos-app/src/services/api.ts` | MODIFIED | `0504B780273C39C5103CD9B20C9E3343A1B176C59CA08C96092DCF70C848E26C` | Sửa B02-F03 (exact match) & B02-F04 (sequence guard refetchSeq) |
| `src/frontend/pos-app/src/services/stockService.ts` | MODIFIED | `9E8A6433420A181583C261F22E154E53886D863671708BB6BC30188B42A51B60` | Sửa B02-F02 (chuyển tiếp trực tiếp về api.ts stockStore) |
| `src/frontend/pos-app/src/services/realtimeService.ts` | MODIFIED | `C4399FB3D18C7DD279AB781FF2AB6DB93F414D5CF881A665CE2EEC08A415437B` | Sửa B02-F01 (generation check & abort in-flight start) & B02-F04 (API refetch) |
| `src/frontend/pos-app/src/services/receiptMapper.ts` | **UNCHANGED** | `9F3AD439B32DC27E514677B42723F6D7C859C7E71C7D692A23805884988EC903` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/services/pendingCheckoutStore.ts` | **UNCHANGED** | `5066491E494561FD0933BACB0851B4556DA760FDA248B3E5B1E04C0168E9AA59` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/types/pos.ts` | MODIFIED | `38C6F75864C6D7D8BF7B6CAA5DC771DCA003C0C0BCF2DE3AF3CF8B6B6878DD1A` | Realtime DTOs và Status types |
| `src/frontend/pos-app/src/index.css` | MODIFIED | `760EF1B50A530BF719328658967ADB50515B2010A15B57C59EC12F9370932A79` | Tailwind CSS v4 local compilation |
| `src/frontend/pos-app/src/main.tsx` | MODIFIED | `6C94C38CA9568F8F93A39A3D545848FB2F29628A7B62426E442246DC152DCD17` | Realtime adapter registration |
| `src/frontend/pos-app/index.html` | MODIFIED | `D0D8DDBEB82E709DDDA6B6006003397E01AC9F9309F566679969C2093CDCC8B9` | Loại bỏ runtime CDN, bảo toàn PWA |
| `src/frontend/pos-app/public/sw.js` | MODIFIED | `A23F3BE270D28ED0774E7C8680F32D783A653F81C1D605AE36A9243F44B48ED2` | Namespace `oisms-pos-v1.0`, pass-through `/api/` & `/hubs/` |
| `src/frontend/pos-app/public/manifest.webmanifest` | **UNCHANGED** | `3E7C3F1EA7456C697C008C6C7289B4980F31AF2AD3C07B007211FD1178E4828A` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/public/favicon.svg` | **UNCHANGED** | `61BC9A161DE58248288E6905425D7180F0624C2865007B97D763FDAC12043A66` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/package.json` | MODIFIED | `25B964B6C066372E0F170FDAA32103F81B70CCA2889F5A274704EE1B153E8861` | Khóa chính xác `@microsoft/signalr` & `@tailwindcss/vite` |
| `src/frontend/pos-app/package-lock.json` | MODIFIED | `15DBB95C34C0A0555A586704B4536671C6A9BF52E0DE2E59A2F2197A46F0F259` | Khóa chính xác dependencies |
| `src/frontend/pos-app/vite.config.ts` | MODIFIED | `0C03207436C9EB3F2F35EC9FF13D17C6AE7E4C8D46AD394A556578CF12C80D74` | Tailwind CSS Vite plugin |
| `src/frontend/pos-app/test-pos-e2e.mjs` | **UNCHANGED** | `71901BA4F50AC22658935F395E7185E558A4236C01DCFB5F504AF3EC7E5B626D` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/test-b02-realtime-pwa.mjs` | MODIFIED | `A2AA109ADDCBB83D4B6C0DD50CA0562B6A3E3FC644E33048B1DA0EF1DA68FF9C` | 14 testcases (9 AC + 5 regression subtests cho F01..F05) |
| `src/frontend/pos-app/test-b02-hub-transport.mjs` | MODIFIED | `598A70A0E1D7DCE0B68F94DE7BAA523CE1E4C65E386CC1585077E4EEB9955B19` | Test WebSocket thật, Tenant B cross-tenant rejection, reconnect |
| `src/frontend/pos-app/test-b02-browser-production.mjs` | MODIFIED | `63C78B480BF537D300722E7669D2B189AF8077DECEF6034D81CEF0DB569AE050` | Browser production test với barcode numeric input max = 8 check |
| `src/frontend/pos-app/measure-pos-latency.mjs` | **NEW** | `E506D7C67F86EA4A0DC313A1BB9502FBEA2CAEBA529D8C15985644800DE861B9` | Benchmark 50 requests đo latency thực backend/network/UI |

---

## 3. Ma trận nghiệm thu Acceptance Criteria (AC Matrix v1.1)

| Mã AC gốc | Tóm tắt điều kiện đạt | Runner / Testcase | Provider / Môi trường | Kết quả | Bằng chứng kiểm chứng |
| --- | --- | --- | --- | --- | --- |
| **T03-AC-01** | Hub dùng JWT hợp lệ; connect/stop/reconnect theo token/user/branch; join/leave đúng method; không trùng listener; hủy bỏ callback phiên cũ khi đổi generation. | `test-b02-realtime-pwa.mjs` (Subtest 1 & 10) | Node.js test runner | **PASS** | `B02-F01-REGRESSION`: in-flight start cũ dừng hoàn toàn, không rò rỉ sự kiện |
| **T03-AC-02** | Nhận `OrderCreated`, `StockLevelChanged`, `LowStockWarning` đúng DTO; chuông Web Audio tổng hợp an toàn kèm mute toggle; trạng thái mất mạng hiển thị rõ. | `test-b02-realtime-pwa.mjs` (Subtest 2) & `PosHeader.tsx` | Component UI & Web Audio mock | **PASS** | DTO camelCase/PascalCase pass, chime synthesis không crash khi chặn autoplay |
| **T03-AC-03** | Event SignalR refetch tồn đúng branch đang chọn; không local decrement; event lặp không trừ đúp; event branch khác bị bỏ qua. | `test-b02-realtime-pwa.mjs` (Subtest 3) | Unit / In-memory Stock Store | **PASS** | Mock event branch khác không ảnh hưởng branch active |
| **T03-AC-04** | Tồn API thấp hơn giỏ hàng: cảnh báo SKU/số lượng cụ thể trên UI; không tự ý cắt giảm quantity/giá; không sửa snapshot checkout pending. | `test-b02-realtime-pwa.mjs` (Subtest 4) & `App.tsx` | Unit + E2E Browser | **PASS** | Banner cảnh báo hiển thị rõ ràng, giỏ hàng nguyên vẹn |
| **T03-AC-05** | Reconnect refetch và đăng ký lại branch; đổi tenant/user hủy bỏ state cũ; kiểm chứng **Hub transport thật**. | `test-b02-hub-transport.mjs` (Subtest 1) | **Backend thật** (`http://localhost:5000/hubs/notifications`) | **PASS** | Kết nối WebSocket thật thành công, JoinBranchGroup hợp lệ, từ chối Shop B branch `22222222-0000-0000-0001-000000000001` |
| **T03-AC-06** | **POS-STOCK-F01:** Available 10 → checkout 2 → UI thành 8 không cần F5, không chờ Hub; quét barcode mới dùng tồn 8; replay/event đến sau vẫn 8; Hub offline vẫn làm mới. | `test-b02-realtime-pwa.mjs` (Subtest 5 & 11) & `test-b02-browser-production.mjs` (Scenario 1) | Unit + Production Browser (Edge Preview, Hub offline) | **PASS** | `02-desktop-stock-refreshed-8.png`, input `max = '8'`, không thể thêm vượt quá 8 |
| **T03-AC-07** | Search/barcode cũ không mở lại như đã đồng bộ sau checkout; refresh GET lỗi hiển thị badge stale và nút thử lại, không gán 99 từ SKU khác. | `test-b02-realtime-pwa.mjs` (Subtest 6 & 12) | Unit / In-memory Stock Store | **PASS** | `B02-F03-REGRESSION`: SKU-A không bị gán tồn 99 của SKU-A-OTHER, giữ stale + retry |
| **T05-AC-01** | F2 focus tìm kiếm; F4 mở thanh toán một lần khi hợp lệ (chặn khi submitting); Esc đóng an toàn. | `test-b02-realtime-pwa.mjs` (Subtest 7) & `test-b02-browser-production.mjs` (Scenario 1) | Production Browser (Playwright Edge) | **PASS** | F2, F4, Esc hoạt động chuẩn xác; [01-desktop-online-search-stock10.png](./evidence/v1.1/01-desktop-online-search-stock10.png) |
| **T05-AC-03** | Manifest/icons/SW chạy trên production preview; nạp lại shell offline từ SW; chỉ xóa cache thuộc namespace `oisms-pos-`, giữ nguyên cache khác origin. | `test-b02-realtime-pwa.mjs` (Subtest 8) & `test-b02-browser-production.mjs` (Scenario 2) | Production Browser (Playwright Edge) | **PASS** | SW kích hoạt, giữ `other-app-v1.0`, tải shell offline; [03-desktop-offline-pwa-shell.png](./evidence/v1.1/03-desktop-offline-pwa-shell.png) |
| **T05-AC-04** | Trạng thái Online/Offline hiển thị rõ trên header; bảo toàn giỏ hàng & pending theo T01; không cache public API/auth/Hub; không fake checkout hay queue POST khi offline. | `test-b02-realtime-pwa.mjs` (Subtest 8) & `test-b02-browser-production.mjs` (Scenario 2) | Production Browser (Playwright Edge) | **PASS** | Badge `Offline` hiển thị trên header khi mất mạng |
| **T05-AC-05** *(phần B02)* | Browser thật trên Desktop và Touch tablet viewport (768x1024): barcode/keyboard, offline/reconnect; phần cứng máy in/máy quét vật lý ghi nhận NOT RUN. | `test-b02-browser-production.mjs` (Scenario 3) | Production Browser (Playwright Edge) | **PASS** | [04-touch-viewport-tablet.png](./evidence/v1.1/04-touch-viewport-tablet.png); Máy in/máy quét vật lý: **NOT RUN** |
| **T05-AC-06** | Race condition guard khi gõ nhanh / đổi branch; sequential requestId + AbortController; phân rã số đo độ trễ p95 chi tiết từ backend container. | `test-b02-realtime-pwa.mjs` (Subtest 9 & 14) & `measure-pos-latency.mjs` | Live Backend REST + Node harness | **PASS** | Request chậm từ Branch A không ghi đè Branch B; p95 mạng+backend = 14.139ms (< 200ms) |

---

## 4. Kết quả thực thi Test Suites

Tất cả các bài kiểm thử đã được chạy và đạt kết quả 100% xanh:

1. **Production Build Compilation:**
   ```powershell
   npm --prefix src/frontend/pos-app run build
   # Kết quả: Built thành công (0 errors, 1942 modules transformed)
   ```
2. **Bộ kiểm thử hồi quy Baseline (34 tests):**
   ```powershell
   node src/frontend/pos-app/test-pos-e2e.mjs
   # Kết quả: 34/34 tests PASS (Thời gian: 2.16s)
   ```
3. **Bộ kiểm thử chức năng B02 & Regression Findings F01-F05 (14 tests):**
   ```powershell
   node src/frontend/pos-app/test-b02-realtime-pwa.mjs
   # Kết quả: 14/14 tests PASS (Bao gồm 9 AC tests + 5 explicit regression tests)
   ```
4. **Kiểm chứng SignalR Hub Transport thật với Backend Container:**
   ```powershell
   node src/frontend/pos-app/test-b02-hub-transport.mjs
   # Kết quả: PASS 100%
   # - WebSocket connected to ws://localhost:5000/hubs/notifications
   # - Joined branch: 11111111-0000-0000-0001-000000000001
   # - Rejected cross-tenant branch: 22222222-0000-0000-0001-000000000001
   # - Disconnect, Reconnect & Rejoin: PASS
   # - RealtimeService production integration: PASS
   ```
5. **Kiểm thử E2E Browser Production & Visual Evidence (Playwright Edge):**
   ```powershell
   node src/frontend/pos-app/test-b02-browser-production.mjs
   # Kết quả: PASS 100%
   # - Scenario 1 (Desktop Online, F2, F4, Esc, Checkout 10->8, Barcode Max=8 check): PASS
   # - Scenario 2 (Offline PWA Shell reload & Cache Isolation): PASS
   # - Scenario 3 (Touch Viewport 768x1024 tablet): PASS
   # - Đã xuất 4 ảnh chụp màn hình bằng chứng vào folder evidence/v1.1/
   ```
6. **Benchmark Đo lường Độ trễ Thực tế (50 mẫu truy vấn Backend thật):**
   ```powershell
   node src/frontend/pos-app/measure-pos-latency.mjs
   # Kết quả: p95 = 14.139ms (< 200ms) - Lưu tại evidence/v1.1/latency-benchmark.json
   ```
7. **Kiểm thử Backend Integration Test (`RealtimeHubIntegrationTests.cs`):**
   ```powershell
   dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj --filter FullyQualifiedName~RealtimeHubIntegrationTests
   # Kết quả: Passed! Failed: 0, Passed: 4, Skipped: 0, Total: 4 (net8.0)
   ```

---

## 5. Thư mục Bằng chứng (Evidence Artifacts)

Toàn bộ tài liệu và hình ảnh bằng chứng nghiệm thu v1.1 được lưu trữ tại:
`docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-02-stock-realtime-pwa/evidence/v1.1/`

- [01-desktop-online-search-stock10.png](./evidence/v1.1/01-desktop-online-search-stock10.png): Tìm kiếm sản phẩm trực tuyến trên Desktop, hiển thị tồn ban đầu là 10.
- [02-desktop-stock-refreshed-8.png](./evidence/v1.1/02-desktop-stock-refreshed-8.png): Sau khi checkout mua 2 sản phẩm (khi Hub offline), tồn khả dụng tự động làm mới thành 8 mà không cần F5; quét barcode mới nhận tồn 8 và giỏ hàng khóa max = 8.
- [03-desktop-offline-pwa-shell.png](./evidence/v1.1/03-desktop-offline-pwa-shell.png): PWA Shell tải lại mượt mà khi hoàn toàn OFFLINE từ Service Worker cache, hiển thị huy hiệu `Offline` trên header.
- [04-touch-viewport-tablet.png](./evidence/v1.1/04-touch-viewport-tablet.png): Giao diện POS responsive tối ưu cho thiết bị cảm ứng Tablet/iPad (768x1024).
- [latency-benchmark.json](./evidence/v1.1/latency-benchmark.json): Bộ dữ liệu thô 50 mẫu đo lường độ trễ mạng và backend REST.
- [working-tree-hashes.json](./evidence/v1.1/working-tree-hashes.json): Danh mục mã băm SHA256 của toàn bộ các file trong working tree v1.1.

---

## 6. Kết luận và Bàn giao

- Phạm vi **S3-POS-B02 v1.1** đã được khắc phục hoàn chỉnh, đóng dứt điểm lỗi **POS-STOCK-F01** và toàn bộ 6 finding **B02-F01..F06**.
- Toàn bộ working tree từ T01 v1.3 và B01 v1.2 được bảo toàn tuyệt đối, không có bất kỳ xung đột nào.
- Tuân thủ quy định: Chưa thực hiện commit, push, merge, reset, clean; chưa triển khai B03.
- Trạng thái hiện tại: **`READY_FOR_REVIEW v1.1`**. Dừng công việc và chuyển giao cho Reviewer kiểm tra, đánh giá nghiệm thu.
