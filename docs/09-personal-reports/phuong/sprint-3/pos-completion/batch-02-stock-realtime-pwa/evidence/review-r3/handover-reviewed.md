# Handover — S3-POS-B02 / v1.2 — Tồn realtime, PWA và thao tác quầy

- **Ngày bàn giao:** 28/10/2026, Asia/Saigon
- **Trạng thái:** **`READY_FOR_REVIEW v1.2`** *(Chờ Codex Reviewer nghiệm thu, không tự nhận VERIFIED)*
- **Người thực hiện:** Antigravity (Frontend POS Phương Lê)
- **Nhánh:** `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`
- **Baseline HEAD:** `44317f3b5188c276fa3ef25c8faf30323e503079`
- **Working tree:** Giữ trọn vẹn T01 v1.3 và B01 v1.2 đã VERIFIED (chưa commit), tích hợp hoàn chỉnh toàn bộ bản sửa lỗi R2 v1.2 cho B02.
- **Lịch sử nghiệm thu:**
  - Finding **B02-F01, B02-F02, B02-F03** đã được Reviewer xác nhận **CLOSED** trong Review R2 (`v1.1-review-r2.md`).
  - Bản v1.2 tập trung giải quyết triệt để 3 finding còn lại: **B02-F04, B02-F05, B02-F06**.

---

## 1. Báo cáo khắc phục các Finding Review R2 (B02-F04, B02-F05, B02-F06)

Bản v1.2 giải quyết dứt điểm toàn bộ các điểm còn thiếu nêu trong `reviews/v1.1-review-r2.md` và tuân thủ nghiêm ngặt các điều kiện đóng trong `reviews/v1.2-fix-prompt.md`:

### B02-F04: Sửa toàn luồng Refetch theo Contract, Canonical Identity và Reconnect Recovery
- **Vấn đề v1.1:**
  1. *Contract mismatch:* Sự kiện `StockLevelChanged` chỉ mang `skuId` (UUID). Khi truyền `{ skuId }` vào `/api/pos/search`, backend `PosCatalogController.cs` chỉ lọc text theo SKU/tên/barcode nên trả về `[]`, khiến cache bị đánh dấu stale và giữ nguyên tồn 10 thay vì cập nhật 8.
  2. *Alias race condition:* `stockRefetchSeq` chia key theo alias lookupKey (SKU code vs UUID). Khi một request search SKU code cũ (trả tồn 10) về sau request barcode của cùng variant (trả tồn 8), response cũ vẫn ghi đè làm số tồn lùi từ 8 về 10.
  3. *Thiếu refetch sau reconnect:* Sau khi proxy hóa `stockService.ts`, không có subscriber nào đăng ký `subscribeReconnected`, khiến khi kết nối mạng phục hồi sau thời gian offline, ứng dụng chỉ join lại branch mà không GET refetch số tồn mới.
- **Khắc phục v1.2:**
  - *Contract balance endpoint:* Tích hợp hàm `getSkuBalanceAtBranch(skuId, branchId)` trong `src/services/api.ts` gọi trực tiếp endpoint chuẩn backend: `GET /api/inventory/balances/sku/{skuId}/branch/{branchId}` (hỗ trợ vai trò Cashier, trả về số dư tồn kho chính xác theo SKU ID mà không cần search text).
  - *Canonical identity registry & sequence guard:* Bổ sung `skuIdentityRegistry` và cơ chế phân giải `resolveCanonicalVariantId` trong `src/services/api.ts`. Sequence guard `stockRefetchSeq` được đồng bộ trên ID biến thể chuẩn (`branchId:canonicalVariantId`) cho mọi alias (SKU code, barcode, UUID). Khi response barcode mới đã chốt tồn 8, response search cũ mang tồn 10 bị phát hiện có `seq < latestSeq` và bị hủy bỏ lập tức.
  - *Tự động refetch khi reconnect:* Bổ sung hàm `refreshBranchStock(activeBranchId)` trong `api.ts` và tích hợp trực tiếp vào handler `conn.onreconnected` trong `src/services/realtimeService.ts`. Khi Hub kết nối lại, hệ thống tự động quét toàn bộ mặt hàng thuộc chi nhánh active và kích hoạt refetch API để cập nhật số dư mới nhất.
- **Kiểm chứng:**
  - `B02-F04-REGRESSION-CONTRACT`: Event `StockLevelChanged` mang UUID kích hoạt refetch API chuẩn, cập nhật tồn thành 8 và `isStale: false` (PASS).
  - `B02-F04-REGRESSION-ALIAS-RACE`: Response search cũ (tồn 10) về muộn sau response barcode mới (tồn 8) bị hủy bỏ hoàn toàn, tồn giữ nguyên 8 (PASS).
  - `B02-F04-REGRESSION-RECONNECT`: Sau khi ngắt mạng và server giảm tồn 10 xuống 8, sự kiện reconnect kích hoạt GET refetch và cập nhật UI thành 8 (PASS).

### B02-F05: Vô hiệu hóa Callback ngay khi Query thay đổi hoặc bị xóa rỗng
- **Vấn đề v1.1:** Ca đổi chi nhánh đã đạt, nhưng khi người dùng nhập `SKU-A` (request fetch đang bay) rồi xóa trắng ô tìm kiếm (`searchTerm = ''`), effect chỉ xóa kết quả và return mà không tăng `latestRequestIdRef` hay hủy abort controller. Khi request `SKU-A` hoàn tất, callback vẫn coi request là hợp lệ và mở lại dropdown dù ô nhập liệu đang rỗng.
- **Khắc phục v1.2:**
  - Trong `src/components/ProductSearch.tsx`, ngay khi `searchTerm` thay đổi hoặc bị xóa trắng:
    1. Lập tức tăng `latestRequestIdRef.current++` để vô hiệu hóa ngay mọi callback đang chờ.
    2. Lập tức gọi `currentAbortControllerRef.current?.abort()` để hủy fetch trên mạng.
    3. Nếu query rỗng (`!trimmed`), lập tức dọn sạch `results = []`, `showDropdown = false`, `isSearching = false`.
  - Trong callback của timer debounce, kiểm tra `requestId !== latestRequestIdRef.current || abortController.signal.aborted` trước khi gửi fetch và trước khi cập nhật state.
  - Trong cleanup function của effect, hủy timer đồng thời gọi `abortController.abort()`.
- **Kiểm chứng:**
  - `B02-F05-REGRESSION-QUERY-CLEAR`: Nhập SKU-A, request fetch đang bay, xóa input về rỗng, request hoàn tất muộn. Kết quả: input rỗng, dropdown không bao giờ mở lại (`buttons.length === 0`, PASS).
  - `B02-F05-REGRESSION-BRANCH-SWITCH`: Request barcode Branch A đang bay, chuyển sang Branch B. Kết quả: không có sản phẩm nào của Branch A bị thêm vào giỏ Branch B (PASS).

### B02-F06: Hoàn thiện Bằng chứng Benchmark Trung thực và Kiểm chứng Transport thật
- **Vấn đề v1.1:** Runner benchmark chấp nhận HTTP 404 là kết quả thành công và cộng thời gian ước lượng render 0.15ms vào p95; kiểm chứng transport live Hub chưa đăng ký nhận event nghiệp vụ và chưa làm rõ giới hạn test container.
- **Khắc phục v1.2:**
  - *Benchmark trung thực (`measure-pos-latency.mjs`):*
    * Bổ sung assertion kiểm tra nghiêm ngặt `res.ok` (HTTP status 200), kiểm tra đối tượng trả về phải chứa đúng `variantId`, `sku` và trường số `available`. Nếu gặp 404 hoặc lỗi mạng, script lập tức throw Exception và fail (đã được đối chứng qua `benchmark-status-probe.cjs`).
    * Sửa thông tin metadata môi trường thành `.NET 8 WebApi + PostgreSQL Docker container`.
    * Tách bạch rõ ràng số đo thực: **Mạng + Backend REST p95 thực đo = 12.32 ms** (50/50 mẫu thành công, Min: 5.858 ms, Avg: 8.863 ms, Max: 13.901 ms), In-memory map lookup p95 = 0.000 ms. Thời gian ước lượng render 0.15ms được ghi riêng biệt, không cộng gộp giả làm số đo thực. Toàn bộ lưu tại [latency-benchmark.json](./evidence/v1.2/latency-benchmark.json).
  - *Kiểm chứng Transport thật (`test-b02-hub-transport.mjs`):*
    * Kết nối WebSocket thật tới backend `localhost:5000/hubs/notifications` bằng JWT của Cashier A.
    * Tham gia chi nhánh hợp lệ `11111111-0000-0000-0001-000000000001`.
    * Kiểm chứng server từ chối đích danh chi nhánh của Shop B `22222222-0000-0000-0001-000000000001` với thông báo bảo mật đa khách thuê.
    * Kiểm chứng ngắt kết nối, kết nối lại và tham gia lại branch.
    * Tích hợp production client `realtimeService.ts` đầy đủ với các subscriber: `OrderCreated`, `StockLevelChanged`, `LowStockWarning`, `Reconnected`.
    * **Ghi nhận minh bạch:** Backend container production không mở debug injection endpoint từ bên ngoài (tuân thủ bảo mật, không sửa backend ngoài brief). Việc nhận event nghiệp vụ và map DTO được bảo đảm và nghiệm thu 100% qua bộ kiểm thử backend `RealtimeHubIntegrationTests.cs` (4/4 PASS) cùng controlled transport suite `test-b02-realtime-pwa.mjs`.

---

## 2. Bảng đối chiếu mã SHA256 (Baseline vs Tested Working Tree v1.2)

Chi tiết đầy đủ được lưu tại file: [working-tree-hashes.json](./evidence/v1.2/working-tree-hashes.json).

| File | Trạng thái | SHA256 Tested Working Tree v1.2 | Ghi chú |
| --- | --- | --- | --- |
| `src/frontend/pos-app/src/App.tsx` | MODIFIED | `16FD4F76933B0BFDA156FA6BE59D45FFA3F1E9F8540F6E79C9CA1A4124E74E06` | Bảo toàn hook index 0-9; refresh stock sau checkout |
| `src/frontend/pos-app/src/components/LoginModal.tsx` | **UNCHANGED** | `C800F3DAF92DDDB6827FD2465180DE8AE652CB21FA5E10B49BB2046473656FC7` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/components/PaymentModal.tsx` | **UNCHANGED** | `419B8AC9641AD904C4F9B037773B800F2B49F8B44A2FE5A3A77EFAF7FE1AFFA3` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/components/ReceiptPrintModal.tsx` | **UNCHANGED** | `941C4E448B6019BD624EBD30935FCB6CDD11A042138164B35EFBBA19B8BEA9A1` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/components/CartTable.tsx` | **UNCHANGED** | `34F8C3603753163CDD9BEAE1CAE2A58C580C0C8EA119C61F08FD2347C6D67F44` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/components/ProductSearch.tsx` | MODIFIED | `86CE53F22048C73C2930D0715A77E1C0D511B646E386CDA4F6146D2076A90948` | Sửa B02-F05 (vô hiệu ngay khi query clear/change, abort controller) |
| `src/frontend/pos-app/src/components/PosHeader.tsx` | MODIFIED | `468A98ECCA9D1DF582FCB2BFF1794E1BE6DCF589063CDE4B613AF8C1316B1A52` | Realtime status badge, chime mute toggle |
| `src/frontend/pos-app/src/services/api.ts` | MODIFIED | `AB5B4A10B990F9043C122FCDA04DB8878568893F72F63233936A426749A24496` | Sửa B02-F04 (getSkuBalanceAtBranch, canonical seq guard, refreshBranchStock) |
| `src/frontend/pos-app/src/services/stockService.ts` | MODIFIED | `9E8A6433420A181583C261F22E154E53886D863671708BB6BC30188B42A51B60` | Proxy chuyển tiếp trực tiếp về api.ts stockStore |
| `src/frontend/pos-app/src/services/realtimeService.ts` | MODIFIED | `5F4AEC88CDD9A224B3315A4E463DED8FE7844788D979A8E90F3AFEEEF8C70110` | Sửa B02-F04 (reconnect gọi refreshBranchStock, StockLevelChanged refetch) |
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
| `src/frontend/pos-app/test-b02-realtime-pwa.mjs` | MODIFIED | `34DE254851858380404001127A6896ADFACAE5E7186A73BEB24A6BDC8DC45040` | 17 testcases (9 AC + 8 explicit runtime regression tests cho F01..F05) |
| `src/frontend/pos-app/test-b02-hub-transport.mjs` | MODIFIED | `EA71E8AD5BACAEE0F6D7024773537B66BDB1CD4C828733AB4343F207E2F3D7A9` | Test WebSocket thật, Tenant B rejection, reconnect, production integration |
| `src/frontend/pos-app/test-b02-browser-production.mjs` | MODIFIED | `D3A79663C016DE40F246C6A09FB9217375B7211BD6A8D802F841366F3F0F9504` | Playwright E2E xuất evidence v1.2, barcode numeric input max = 8 check |
| `src/frontend/pos-app/measure-pos-latency.mjs` | MODIFIED | `03529B56A14BEFF59215CA75C002E305B2BC6CEB5768C40DBAB24A862847FAFF` | Benchmark 50 requests HTTP 200 verification, đo latency thực .NET 8 backend |

---

## 3. Ma trận nghiệm thu Acceptance Criteria (AC Matrix v1.2)

| Mã AC gốc | Tóm tắt điều kiện đạt | Runner / Testcase | Provider / Môi trường | Kết quả | Bằng chứng kiểm chứng |
| --- | --- | --- | --- | --- | --- |
| **T03-AC-01** | Hub dùng JWT hợp lệ; connect/stop/reconnect theo token/user/branch; join/leave đúng method; không trùng listener; hủy bỏ callback phiên cũ khi đổi generation. | `test-b02-realtime-pwa.mjs` (Subtest 1 & 10) | Node.js test runner | **PASS** | `B02-F01-REGRESSION`: in-flight start cũ dừng hoàn toàn 2 lần, không rò rỉ sự kiện |
| **T03-AC-02** | Nhận `OrderCreated`, `StockLevelChanged`, `LowStockWarning` đúng DTO; chuông Web Audio tổng hợp an toàn kèm mute toggle; trạng thái mất mạng hiển thị rõ. | `test-b02-realtime-pwa.mjs` (Subtest 2) & `PosHeader.tsx` | Component UI & Web Audio mock | **PASS** | DTO camelCase/PascalCase pass, chime synthesis không crash khi chặn autoplay |
| **T03-AC-03** | Event SignalR refetch tồn đúng branch đang chọn; không local decrement; event lặp không trừ đúp; event branch khác bị bỏ qua. | `test-b02-realtime-pwa.mjs` (Subtest 3) | Unit / In-memory Stock Store | **PASS** | Mock event branch khác không ảnh hưởng branch active |
| **T03-AC-04** | Tồn API thấp hơn giỏ hàng: cảnh báo SKU/số lượng cụ thể trên UI; không tự ý cắt giảm quantity/giá; không sửa snapshot checkout pending. | `test-b02-realtime-pwa.mjs` (Subtest 4) & `App.tsx` | Unit + E2E Browser | **PASS** | Banner cảnh báo hiển thị rõ ràng, giỏ hàng nguyên vẹn |
| **T03-AC-05** | Reconnect refetch và đăng ký lại branch; đổi tenant/user hủy bỏ state cũ; kiểm chứng **Hub transport thật**. | `test-b02-hub-transport.mjs` (Subtest 1) | **Backend thật** (`http://localhost:5000/hubs/notifications`) | **PASS** | Kết nối WebSocket thật thành công, JoinBranchGroup hợp lệ, từ chối Shop B branch `22222222-0000-0000-0001-000000000001` |
| **T03-AC-06** | **POS-STOCK-F01:** Available 10 → checkout 2 → UI thành 8 không cần F5, không chờ Hub; quét barcode mới dùng tồn 8; replay/event đến sau vẫn 8; response search cũ không đè tồn 8 thành 10. | `test-b02-realtime-pwa.mjs` (Subtest 5, 11, 14) & `test-b02-browser-production.mjs` (Scenario 1) | Unit + Production Browser (Edge Preview, Hub offline) | **PASS** | `02-desktop-stock-refreshed-8.png`, input `max = '8'`, response cũ không thể đè ngược về 10 |
| **T03-AC-07** | Search/barcode cũ không mở lại như đã đồng bộ sau checkout; refresh GET lỗi hiển thị badge stale và nút thử lại, không gán 99 từ SKU khác. | `test-b02-realtime-pwa.mjs` (Subtest 6, 12, 13) | Unit / In-memory Stock Store | **PASS** | `B02-F03-REGRESSION`: SKU-A không bị gán tồn 99 của SKU-A-OTHER, giữ stale + retry |
| **T05-AC-01** | F2 focus tìm kiếm; F4 mở thanh toán một lần khi hợp lệ (chặn khi submitting); Esc đóng an toàn. | `test-b02-realtime-pwa.mjs` (Subtest 7) & `test-b02-browser-production.mjs` (Scenario 1) | Production Browser (Playwright Edge) | **PASS** | F2, F4, Esc hoạt động chuẩn xác; [01-desktop-online-search-stock10.png](./evidence/v1.2/01-desktop-online-search-stock10.png) |
| **T05-AC-03** | Manifest/icons/SW chạy trên production preview; nạp lại shell offline từ SW; chỉ xóa cache thuộc namespace `oisms-pos-`, giữ nguyên cache khác origin. | `test-b02-realtime-pwa.mjs` (Subtest 8) & `test-b02-browser-production.mjs` (Scenario 2) | Production Browser (Playwright Edge) | **PASS** | SW kích hoạt, giữ `other-app-v1.0`, tải shell offline; [03-desktop-offline-pwa-shell.png](./evidence/v1.2/03-desktop-offline-pwa-shell.png) |
| **T05-AC-04** | Trạng thái Online/Offline hiển thị rõ trên header; bảo toàn giỏ hàng & pending theo T01; không cache public API/auth/Hub; không fake checkout hay queue POST khi offline. | `test-b02-realtime-pwa.mjs` (Subtest 8) & `test-b02-browser-production.mjs` (Scenario 2) | Production Browser (Playwright Edge) | **PASS** | Badge `Offline` hiển thị trên header khi mất mạng |
| **T05-AC-05** *(phần B02)* | Browser thật trên Desktop và Touch tablet viewport (768x1024): barcode/keyboard, offline/reconnect; phần cứng máy in/máy quét vật lý ghi nhận NOT RUN. | `test-b02-browser-production.mjs` (Scenario 3) | Production Browser (Playwright Edge) | **PASS** | [04-touch-viewport-tablet.png](./evidence/v1.2/04-touch-viewport-tablet.png); Máy in/máy quét vật lý: **NOT RUN** |
| **T05-AC-06** | Race condition guard khi gõ nhanh / đổi branch / xóa query; sequential requestId + AbortController; phân rã số đo độ trễ p95 chi tiết từ backend container. | `test-b02-realtime-pwa.mjs` (Subtest 9, 16, 17) & `measure-pos-latency.mjs` | Live Backend REST + Node harness | **PASS** | Xóa query không mở lại dropdown cũ; p95 mạng+backend = 12.32ms (< 200ms) |

---

## 4. Kết quả thực thi Test Suites v1.2

Tất cả các bài kiểm thử đã được chạy và đạt kết quả 100% xanh:

1. **Production Build Compilation:**
   ```powershell
   npm --prefix src/frontend/pos-app run build
   # Kết quả: Built thành công (0 errors, 1942 modules transformed)
   ```
2. **Bộ kiểm thử hồi quy Baseline (34 tests):**
   ```powershell
   node src/frontend/pos-app/test-pos-e2e.mjs
   # Kết quả: 34/34 tests PASS (Thời gian: 2.02s)
   ```
3. **Bộ kiểm thử chức năng B02 & Regression Findings F01-F05 (17 tests):**
   ```powershell
   node src/frontend/pos-app/test-b02-realtime-pwa.mjs
   # Kết quả: 17/17 tests PASS (Bao gồm 9 AC tests + 8 runtime regression tests)
   # - B02-F01-REGRESSION: PASS (Hub lifecycle, session isolation)
   # - B02-F02-REGRESSION: PASS (Unified stock store, fresh barcode 8)
   # - B02-F03-REGRESSION: PASS (Exact SKU match, no adoption of unrelated SKU)
   # - B02-F04-REGRESSION-CONTRACT: PASS (UUID balance API lookup without stale)
   # - B02-F04-REGRESSION-ALIAS-RACE: PASS (Older response does not overwrite 8)
   # - B02-F04-REGRESSION-RECONNECT: PASS (Reconnect triggers GET and updates to 8)
   # - B02-F05-REGRESSION-BRANCH-SWITCH: PASS (Discard barcode from old branch)
   # - B02-F05-REGRESSION-QUERY-CLEAR: PASS (Clearing input prevents dropdown reopen)
   ```
4. **Kiểm chứng SignalR Hub Transport thật với Backend Container:**
   ```powershell
   node src/frontend/pos-app/test-b02-hub-transport.mjs
   # Kết quả: PASS 100%
   # - WebSocket connected to ws://localhost:5000/hubs/notifications
   # - Joined branch: 11111111-0000-0000-0001-000000000001
   # - Rejected cross-tenant branch: 22222222-0000-0000-0001-000000000001
   # - Disconnect, Reconnect & Rejoin: PASS
   # - Production realtimeService Integration: PASS
   ```
5. **Kiểm thử E2E Browser Production & Visual Evidence (Playwright Edge):**
   ```powershell
   node src/frontend/pos-app/test-b02-browser-production.mjs
   # Kết quả: PASS 100%
   # - Scenario 1 (Desktop Online, F2, F4, Esc, Checkout 10->8, Barcode Max=8 check): PASS
   # - Scenario 2 (Offline PWA Shell reload & Cache Isolation): PASS
   # - Scenario 3 (Touch Viewport 768x1024 tablet): PASS
   # - Đã xuất 4 ảnh chụp màn hình bằng chứng vào folder evidence/v1.2/
   ```
6. **Benchmark Đo lường Độ trễ Thực tế (50 mẫu truy vấn Backend thật):**
   ```powershell
   node src/frontend/pos-app/measure-pos-latency.mjs
   # Kết quả: 50/50 mẫu HTTP 200 hợp lệ, p95 mạng+backend = 12.32ms (< 200ms) - Lưu tại evidence/v1.2/latency-benchmark.json
   ```
7. **Kiểm thử Backend Integration Test (`RealtimeHubIntegrationTests.cs`):**
   ```powershell
   dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj --filter FullyQualifiedName~RealtimeHubIntegrationTests
   # Kết quả: Passed! Failed: 0, Passed: 4, Skipped: 0, Total: 4 (net8.0)
   ```

---

## 5. Thư mục Bằng chứng (Evidence Artifacts v1.2)

Toàn bộ tài liệu và hình ảnh bằng chứng nghiệm thu v1.2 được lưu trữ tại:
`docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-02-stock-realtime-pwa/evidence/v1.2/`

- [01-desktop-online-search-stock10.png](./evidence/v1.2/01-desktop-online-search-stock10.png): Tìm kiếm sản phẩm trực tuyến trên Desktop, hiển thị tồn ban đầu là 10.
- [02-desktop-stock-refreshed-8.png](./evidence/v1.2/02-desktop-stock-refreshed-8.png): Sau khi checkout mua 2 sản phẩm (khi Hub offline), tồn khả dụng tự động làm mới thành 8 mà không cần F5; quét barcode mới nhận tồn 8 và giỏ hàng khóa max = 8.
- [03-desktop-offline-pwa-shell.png](./evidence/v1.2/03-desktop-offline-pwa-shell.png): PWA Shell tải lại mượt mà khi hoàn toàn OFFLINE từ Service Worker cache, hiển thị huy hiệu `Offline` trên header.
- [04-touch-viewport-tablet.png](./evidence/v1.2/04-touch-viewport-tablet.png): Giao diện POS responsive tối ưu cho thiết bị cảm ứng Tablet/iPad (768x1024).
- [latency-benchmark.json](./evidence/v1.2/latency-benchmark.json): Bộ dữ liệu thô 50 mẫu đo lường độ trễ mạng và backend REST với HTTP 200 validation.
- [working-tree-hashes.json](./evidence/v1.2/working-tree-hashes.json): Danh mục mã băm SHA256 của toàn bộ các file trong working tree v1.2.

---

## 6. Kết luận và Bàn giao

- Phạm vi **S3-POS-B02 v1.2** đã được khắc phục hoàn chỉnh:
  - F01, F02, F03 đã CLOSED từ R2.
  - F04, F05, F06 đã được sửa và chứng minh qua 100% runtime tests.
  - Lỗi **POS-STOCK-F01** đã được đóng toàn diện trên cả luồng trực tiếp và các luồng phản hồi bất đồng bộ đảo thứ tự.
- Toàn bộ working tree từ T01 v1.3 và B01 v1.2 được bảo toàn tuyệt đối, không có bất kỳ xung đột nào.
- Tuân thủ quy định: Chưa thực hiện commit, push, merge, reset, clean; chưa triển khai B03.
- Trạng thái hiện tại: **`READY_FOR_REVIEW v1.2`**. Dừng công việc và chuyển giao cho Reviewer kiểm tra, đánh giá nghiệm thu.
