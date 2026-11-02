# Handover — S3-POS-B02 / v1.4 — Tồn realtime, PWA và thao tác quầy

- **Ngày bàn giao:** 28/10/2026, Asia/Saigon
- **Trạng thái:** **`READY_FOR_REVIEW v1.4`** *(Chờ Codex Reviewer R5 nghiệm thu, không tự nhận VERIFIED)*
- **Người thực hiện:** Antigravity (Frontend POS Phương Lê)
- **Nhánh:** `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`
- **Baseline HEAD:** `44317f3b5188c276fa3ef25c8faf30323e503079`
- **Working tree:** Giữ trọn vẹn T01 v1.3 và B01 v1.2 đã VERIFIED (chưa commit); tích hợp hoàn chỉnh toàn bộ bản sửa lỗi R4 v1.4 cho B02. Không commit/push/merge; không sửa Admin hay B03.
- **Lịch sử nghiệm thu:**
  - Finding **B02-F01, B02-F02, B02-F03** đã được xác nhận **CLOSED** từ Review R2 (`reviews/v1.1-review-r2.md`).
  - Finding **B02-F05** (query clear và branch switch race guard) đã được xác nhận **CLOSED** trong Review R3 (`reviews/v1.2-review-r3.md`).
  - Benchmark tra cứu tồn với strict payload validation (p95 = 16.653ms) đã được xác nhận **ĐẠT** trong Review R4 (`reviews/v1.3-review-r4.md`).
  - Bản v1.4 hoàn thiện triệt để hai nội dung còn lại theo đúng `v1.4-fix-prompt.md`:
    1. **B02-F04**: Xử lý thứ tự cập nhật tồn (read→commit ticket) cho barcode cũ về sau refetch mới và search mới khi đã có cache sẵn.
    2. **B02-F06**: Hoàn thiện live workflow runner, phân định ranh giới giữa kiểm chứng controlled runtime và live DB mutation gate.

---

## 1. Báo cáo khắc phục các Finding Review R4 (B02-F04 & B02-F06)

Bản v1.4 tuân thủ nghiêm ngặt nguyên tắc "Sửa lỗi có bằng chứng": kiểm chứng FAIL trên baseline trước sửa, sửa code, và chứng minh PASS sau sửa trên cùng điều kiện và assertion.

### 1.1. B02-F04: Kiến trúc Read→Commit Ticket Thống nhất & Đồng bộ Giỏ hàng

#### A. Phân tích hai nguyên nhân gốc (đã tái hiện FAIL trên baseline):
1. **Barcode cũ về sau refetch mới:**
   - Trong v1.3, `scanPosBarcode` không giữ ticket khi bắt đầu gửi request. Khi response barcode mang số tồn cũ (10) trả về, `setAuthoritativeStock` tự động tăng phiên bản tại thời điểm commit, biến dữ liệu cũ thành phiên bản mới nhất và ghi đè số tồn 8 vừa được refetch.
   - Thêm vào đó, `ProductSearch` gọi `onAddToCart` với số tồn cũ 10.
   - **Baseline FAIL:** Chạy probe tái hiện `AssertionError: 10 !== 8` (lưu tại `evidence/v1.4/baseline-r4-failures.log`).
2. **Search mới bị bỏ qua khi cache đã tồn tại:**
   - Trong v1.3, `ProductSearch.tsx` có điều kiện `if (item.available !== undefined && !existing)`. Khi mặt hàng đã có sẵn trong cache (tồn 10), một lượt tìm kiếm mới nhận được tồn 8 từ API bị điều kiện `!existing` loại bỏ hoàn toàn.
   - Khi thu ngân click chọn mặt hàng từ dropdown, `handleSelectProduct` đọc `stockMap` vẫn là 10, đưa vào giỏ hàng với `max = 10` thay vì 8.
   - **Baseline FAIL:** Reviewer đã tái hiện trên Edge browser thật; cart input `max` bị kẹt ở `'10'` (lưu tại `evidence/v1.4/baseline-r4-failures.log`).

#### B. Giải pháp xử lý triệt để trong v1.4:
1. **Universal Read→Commit Ticket Architecture (`api.ts` & `stockService.ts`):**
   - `issueStockReadTicket(): number`: Monotonically tăng `globalStockTicket`, phát hành ticket duy nhất khi **bắt đầu một thao tác đọc** (read start).
   - `getCommittedStockVersion(branchId, key): number`: Trả về phiên bản cao nhất đã commit cho canonical ID và toàn bộ aliases.
   - `canCommitStockRead(branchId, key, readTicket): boolean`: Xác thực `readTicket >= getCommittedStockVersion(...)`.
   - `setAuthoritativeStock(branchId, key, available, metadata?, readTicket?)`:
     - Nếu có `readTicket`: Kiểm tra `canCommitStockRead`. Nếu ticket cũ hơn phiên bản đã commit (do có refetch/write mới hơn về trước), lập tức từ chối (`return false`). Nếu hợp lệ, ghi cache, ghi nhận `committedVersion = Math.max(current, readTicket)`, notify subscribers và `return true`.
     - Nếu không có `readTicket` (direct write như checkout deduction hoặc realtime event): Cấp ticket mới `++globalStockTicket`, ghi cache và `return true`.
2. **Caller `ProductSearch.tsx`:**
   - **Barcode scan:**
     - Trước khi `await scanPosBarcode`: phát hành `const barcodeTicket = issueStockReadTicket();`.
     - Khi response trả về: gọi `setAuthoritativeStock(..., freshAvailable, metadata, barcodeTicket)`.
     - Nếu commit thất bại (stale response): Đọc `latestCached = getCachedStock(...)` và lấy `effectiveAvailable = latestCached.available` (8).
     - Thêm vào giỏ với `available: effectiveAvailable` (8).
   - **Search query:**
     - Trước khi `await searchPosProducts`: phát hành `const searchTicket = issueStockReadTicket();`.
     - Khi response trả về: **Bỏ điều kiện `!existing`**, commit trực tiếp qua `setAuthoritativeStock(..., item.available, metadata, searchTicket)`.
     - Search ticket mới hơn cache cũ (ticket 2 >= version 1) -> Cache cập nhật thành 8. Nếu có write xảy ra giữa chừng (version 3 > ticket 2) -> Search cũ bị chặn.
     - Dropdown hiển thị `Khả dụng: 8`. Khi click chọn hàng, `handleSelectProduct` đọc `stockMap` (8), thêm vào giỏ với `available: 8`.
3. **Đồng bộ Giỏ hàng `App.tsx`:**
   - Trong `App.tsx`, `subscribeBranchStock` tự động cập nhật trường `available` cho toàn bộ các `item` đang có trong `cart` khi stockMap thay đổi.
   - Khi thêm hàng đã có trong giỏ, `handleAddToCart` cập nhật đồng thời `available: availableStock`.
   - Nhờ đó, trường `max` của `input[type=number]` trong `CartTable` lập tức chuyển từ 10 thành 8.

#### C. Bằng chứng kiểm chứng sau sửa (PASS 100%):
- **Probe Verification (`test-probe-v14.cjs`):**
  - Probe 1 (In-flight older barcode 10 returns after newer refetch 8): Cache giữ nguyên 8, cart added with 8. **PASS**.
  - Probe 2 (Fresh search 8 with existing cache 10): Cache cập nhật thành 8, cart added with 8. **PASS**.
  - Probe 3 (Old search 10 returns after direct write 8): Cache giữ nguyên 8. **PASS**.
  - Probe 4 (Old refetch 10 returns after direct write 8): Cache giữ nguyên 8. **PASS**.
  - Log xác nhận: `evidence/v1.4/probe-v14-verification.log`.
- **Actual Edge Browser Production Verification (`test-b02-browser-search-v14.cjs`):**
  - Chạy bản build production thật trên trình duyệt Microsoft Edge Chromium thật.
  - Sau khi server stock đổi từ 10 sang 8, fresh search trả về 8, click chọn hàng từ dropdown.
  - Cart numeric input `max` được xác thực: `assert.equal(max, '8')` **PASS** (trước sửa là `'10'`).
  - Ảnh chụp màn hình: `evidence/v1.4/browser-search-fixed-8.png`.
  - Kết quả JSON: `evidence/v1.4/browser-search-results.json`.
- **Automated Regression Suite (`test-b02-realtime-pwa.mjs`):**
  - Tích hợp chính thức 2 bài test regression mới: `test 20` và `test 21`.
  - Toàn bộ **21/21 tests PASS 100%** (`evidence/v1.4/b02-realtime-pwa-21-tests.log`).

---

### 1.2. B02-F06: Benchmark Giữ Nguyên & Hoàn thiện Runner Live Transport

#### A. Benchmark Độ trễ Tra cứu Tồn kho:
- Reviewer R4 đã xác nhận runner benchmark đạt chuẩn:
  - Kiểm tra nghiêm ngặt HTTP 200, valid UUID, đúng SKU (`SKU-53318`), đúng barcode (`8934187503554`), `available` là số thực không âm; từ chối 404, sai identity và giá trị phi số.
  - Mốc thời gian đo đạc xuyên suốt toàn bộ vòng đời client: Network + Backend REST Controller + JSON Parse.
  - Kết quả thực tế: **p95 = 16.653ms** (< 200ms: ĐẠT XUẤT SẮC).
  - Báo cáo và dữ liệu đo đạc được lưu giữ nguyên vẹn tại: `evidence/v1.4/latency-benchmark.json`.

#### B. Hoàn thiện Live Event Runner & Environmental Guard:
- Trong `test-b02-hub-transport.mjs`, triển khai đầy đủ luồng thực thi khi có môi trường test độc lập (`POS_LIVE_TEST_ENV=1` cùng `POS_TEST_BACKEND_URL`):
  1. Đăng nhập Cashier trên test host.
  2. Kết nối production `realtimeService` với SignalR Hub.
  3. Đăng ký listener `OrderCreated` và `StockLevelChanged`.
  4. Gửi request checkout thực `POST /api/pos/checkout`.
  5. Await nhận sự kiện live broadcast, xác nhận `OrderNumber` và `newAvailableStock`.
  6. Gây mất kết nối transport, gửi mutation, kết nối lại, xác nhận tự động reconnect, auto-rejoin và `refreshBranchStock` lấy lại số tồn bị lỡ.
- **Bảo vệ CSDL Demo Dùng chung (`localhost:5000`):**
  - Khi chạy trên container demo mặc định, script chạy toàn bộ các kiểm thử WebSocket thật không gây mutation:
    * Kết nối WebSocket thật & JWT Handshake: **PASS**.
    * Join group chi nhánh hợp lệ của Shop A: **PASS**.
    * Server từ chối `Guid.Empty`: **PASS**.
    * Server từ chối chi nhánh thực tế của Shop B khác (cách ly đa khách thuê): **PASS**.
    * Tích hợp production client `realtimeService` (start / switchBranch / stop): **PASS**.
  - Phần phát sinh đơn hàng / checkout live trên CSDL demo được gắn gate an toàn:
    **`[GATE STATUS: LIVE EVENT BROADCAST & AUTO-RECONNECT WORKFLOW]: NOT RUN / BLOCKED (Isolated Test Database Required)`**.
  - **Lệnh unblock:**
    ```powershell
    # Khởi chạy test database container độc lập (ví dụ port 5433)
    docker run -d --name oisms-test-postgres -p 5433:5432 -e POSTGRES_PASSWORD=postgres postgres:16-alpine
    # Đặt biến môi trường và chạy live runner
    $env:POS_LIVE_TEST_ENV = "1"
    $env:POS_TEST_BACKEND_URL = "http://localhost:5001"
    node src/frontend/pos-app/test-b02-hub-transport.mjs
    ```

---

## 2. Bảng đối chiếu mã SHA-256 (Tested Working Tree v1.4)

Chi tiết được lưu tại: [working-tree-hashes.json](./evidence/v1.4/working-tree-hashes.json).

| Tệp tin | Trạng thái | SHA-256 Tested Working Tree v1.4 | Vai trò / Thay đổi trong v1.4 |
| :--- | :---: | :---: | :--- |
| `src/frontend/pos-app/src/App.tsx` | MODIFIED | `8922007fa7db85190627477eda05574ea60f8522b7a02859674bb416791bc2ba` | Cart đồng bộ `available` từ `subscribeBranchStock` & `handleAddToCart` |
| `src/frontend/pos-app/src/components/ProductSearch.tsx` | MODIFIED | `102cbe12b5069c302418b49764e283a5961a83ddd0ca41118d652e161c720aec` | Ticket phát hành tại read start; bỏ `!existing`; stale barcode đọc cache |
| `src/frontend/pos-app/src/services/api.ts` | MODIFIED | `dcb32ddf7fc375823ff5cb5974d5ebe5876e492d10679cffe7250278896e83e6` | Universal read→commit ticket (`issueStockReadTicket`, `canCommitStockRead`, `setAuthoritativeStock`) |
| `src/frontend/pos-app/src/services/stockService.ts` | MODIFIED | `ead0a301f768d8d29b9a4f28fbef71385f5f46decf13f9095351df9e5fa367bd` | Re-export `issueStockReadTicket`, `getCommittedStockVersion`, `canCommitStockRead` |
| `src/frontend/pos-app/src/services/realtimeService.ts` | MODIFIED | `5f4aec88cdd9a224b3315a4e463ded8fe7844788d979a8e90f3afeeef8c70110` | Realtime client SignalR, reconnect auto-refresh |
| `src/frontend/pos-app/src/components/PaymentModal.tsx` | **UNCHANGED** | `419b8ac9641ad904c4f9b037773b800f2b49f8b44a2fe5a3a77efaf7fe1affa3` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/components/PosHeader.tsx` | MODIFIED | `468a98ecca9d1df582fcb2bff1794e1be6dcf589063cde4b613af8c1316b1a52` | Realtime status badge, chime toggle |
| `src/frontend/pos-app/src/components/ReceiptPrintModal.tsx` | **UNCHANGED** | `941c4e448b6019bd624ebd30935fcb6cdd11a042138164b35efbba19b8bea9a1` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/components/LoginModal.tsx` | **UNCHANGED** | `c800f3daf92dddb6827fd2465180de8ae652cb21fa5e10b49bb2046473656fc7` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/types/pos.ts` | MODIFIED | `43f20da53c6840109dbc95a9233a155ea06aa5a33fd09a0a0b32c29eecfe3aea` | Bổ sung `skuId` và `barcode` trong `ReceiptItemData` |
| `src/frontend/pos-app/src/services/pendingCheckoutStore.ts` | **UNCHANGED** | `5066491e494561fd0933bacb0851b4556da760fda248b3e5b1e04c0168e9aa59` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/services/receiptMapper.ts` | **UNCHANGED** | `9f3ad439b32dc27e514677b42723f6d7c859c7e71c7d692a23805884988ec903` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/public/sw.js` | MODIFIED | `a23f3be270d28ed0774e7c8680f32d783a653f81c1d605ae36a9243f44b48ed2` | Namespace `oisms-pos-v1.0`, pass-through API/Hub |
| `src/frontend/pos-app/test-b02-realtime-pwa.mjs` | MODIFIED | `b15f0e93e58f0a680beed8bef277072d000c0f1f8a3a9d21536a5eb94f517dd5` | 21 tests (bổ sung Test 20 & Test 21 cho F04) |
| `src/frontend/pos-app/test-b02-hub-transport.mjs` | MODIFIED | `6ee7e7ca9b34b89092a497495bf6067ed2caea99a9308933eed0c40acb86290b` | Live WebSocket test + hoàn thiện live runner logic |
| `src/frontend/pos-app/test-b02-browser-production.mjs` | MODIFIED | `41539bf1214c1fa51ae2a77869dee2506afa32d69ec7fbc9d6c5ec40261acfd4` | Output evidence sang thư mục `v1.4` |
| `src/frontend/pos-app/test-pos-e2e.mjs` | **UNCHANGED** | `71901ba4f50ac22658935f395e7185e558a4236c01dcfb5f504af3ec7e5b626d` | Khớp 100% baseline B01 (34/34 tests PASS) |
| `src/frontend/pos-app/measure-pos-latency.mjs` | **UNCHANGED** | `dbf9cc2d9b9a36f7142bc90540da6cb387fa27299504452e92fe6396294e76aa` | Benchmark chuẩn p95 = 16.653ms giữ nguyên |

---

## 3. Ma trận nghiệm thu Acceptance Criteria (AC Matrix v1.4)

| Mã AC | Tiêu chí tóm tắt | Trạng thái v1.4 | Bằng chứng kiểm chứng |
| :---: | :--- | :---: | :--- |
| **POS-STOCK-F01** | Tồn 10 -> Checkout 2 -> UI hiển thị 8 không cần F5 (kể cả Hub offline). Replay/event đến sau không trừ thêm thành 6. Triệt tiêu hoàn toàn race condition khi response cũ về muộn hoặc search mới có cache sẵn. | **ĐẠT (CLOSED)** | `test-b02-realtime-pwa.mjs` (tests 5, 14, 15, 16, 20, 21); Edge Browser: `browser-search-fixed-8.png`, `02-desktop-stock-refreshed-8.png`. |
| **T03-AC-01** | Kết nối SignalR Hub thật với JWT token; kiểm tra trạng thái connected/disconnected. | **ĐẠT (CLOSED)** | `test-b02-hub-transport.mjs` (PASS); `test-b02-realtime-pwa.mjs` (test 1). |
| **T03-AC-02** | Nhận và chuẩn hóa DTO `OrderCreated`, `StockLevelChanged`, `LowStockWarning`. Âm thanh báo an toàn có nút Mute.<br>*(1) Controlled runtime: ĐẠT<br>(2) Live event mutation trên CSDL thật: BLOCKED* | **ĐẠT (Controlled Runtime: CLOSED)**<br>**NOT RUN / BLOCKED (Live DB Mutation)** | `test-b02-realtime-pwa.mjs` (test 2); PosHeader chime toggle.<br>Live DB mutation: Chờ isolated test DB. |
| **T03-AC-03** | Gia nhập/rời nhóm chi nhánh chính xác, chống listener trùng lặp, cách ly Tenant B. | **ĐẠT (CLOSED)** | `test-b02-hub-transport.mjs` (Shop B branch rejected); `test-b02-realtime-pwa.mjs` (tests 3, 10). |
| **T03-AC-04** | Cảnh báo quầy thu ngân khi tồn API nhỏ hơn số lượng trong giỏ hàng. | **ĐẠT (CLOSED)** | `test-b02-realtime-pwa.mjs` (test 4). |
| **T03-AC-05** | Tự động kết nối lại khi rớt mạng, rejoin đúng chi nhánh, phục hồi state và GET refetch.<br>*(1) Controlled runtime: ĐẠT<br>(2) Live network loss & DB recovery: BLOCKED* | **ĐẠT (Controlled Runtime: CLOSED)**<br>**NOT RUN / BLOCKED (Live Recovery)** | `test-b02-realtime-pwa.mjs` (test 17); `test-b02-hub-transport.mjs`.<br>Live recovery: Chờ isolated test DB. |
| **T03-AC-06** | Làm mới tồn sau checkout tại quầy qua API chuẩn mà không cần F5. | **ĐẠT (CLOSED)** | `App.tsx` -> `invalidateAndRefetchStock` -> `test-b02-realtime-pwa.mjs` (test 5, 15). |
| **T03-AC-07** | Vô hiệu hóa kết quả tìm kiếm cũ khi chi nhánh đổi hoặc query bị xóa rỗng. | **ĐẠT (CLOSED)** | `test-b02-realtime-pwa.mjs` (tests 6, 18, 19). |
| **T05-AC-01** | Phím tắt quầy thu ngân F2, F4, Esc, tối ưu touch viewport. | **ĐẠT (CLOSED)** | `test-b02-browser-production.mjs` (Desktop & Touch scenarios); `test-b02-realtime-pwa.mjs` (test 7). |
| **T05-AC-03** | PWA Web App Manifest đầy đủ standalone, theme_color, icons. | **ĐẠT (CLOSED)** | `manifest.webmanifest`; `index.html`. |
| **T05-AC-04** | Service Worker offline app shell, namespace `oisms-pos-v1.0`, pass-through API/Hub. | **ĐẠT (CLOSED)** | `test-b02-realtime-pwa.mjs` (test 8); `03-desktop-offline-pwa-shell.png`. |
| **T05-AC-06** | Đo đạc độ trễ tra cứu tồn kho thực tế p95 < 200ms với strict payload validation. | **ĐẠT (CLOSED)** | `latency-benchmark.json`: Full roundtrip REST p95 = 16.653ms (50/50 samples valid). |

---

## 4. Hướng dẫn Tái kiểm chứng v1.4

Thực thi tuần tự từ thư mục gốc của repository: `D:\OISMS`:

```powershell
# 1. Build production TypeScript & Vite
npm.cmd --prefix src/frontend/pos-app run build

# 2. Chạy 34 bài kiểm thử hồi quy baseline (PASS 100%)
node --test src/frontend/pos-app/test-pos-e2e.mjs

# 3. Chạy toàn bộ 21 bài kiểm thử B02 (PASS 100%)
node --test src/frontend/pos-app/test-b02-realtime-pwa.mjs

# 4. Chạy kiểm chứng probe R4 trước/sau sửa (PASS 100%)
node src/frontend/pos-app/test-probe-v14.cjs

# 5. Chạy kiểm thử Edge Browser Production thực tế cho F04 (PASS 100%)
node src/frontend/pos-app/test-b02-browser-search-v14.cjs

# 6. Kiểm tra kết nối SignalR Hub WebSocket thật & Cách ly Shop B
node src/frontend/pos-app/test-b02-hub-transport.mjs

# 7. Chạy Playwright Browser Production scenarios (Desktop, Touch, PWA Offline)
node src/frontend/pos-app/test-b02-browser-production.mjs
```

### Danh mục Bằng chứng v1.4 (`evidence/v1.4/`):
- `build.log`: Log build production thành công 0 lỗi.
- `baseline-34-tests.log`: Log 34/34 bài test baseline PASS.
- `b02-realtime-pwa-21-tests.log`: Log 21/21 bài test B02 PASS (gồm 2 ca regression mới cho F04).
- `probe-v14-verification.log`: Log 4 probe kiểm chứng cơ chế read→commit ticket PASS 100%.
- `browser-search-results.json`: Kết quả đo đạc trên Edge Chromium sản xuất (cart numeric input max = 8).
- `browser-search-fixed-8.png`: Ảnh chụp quầy thu ngân Edge production sau khi search tồn 8 cập nhật giỏ hàng.
- `b02-hub-transport.log`: Log kết nối Hub WebSocket thật, cách ly Shop B và guard môi trường.
- `latency-benchmark.json`: Báo cáo độ trễ p95 chi tiết (16.653ms).
- `working-tree-hashes.json`: Bảng băm SHA-256 các tệp tin trong working tree.
- `01-desktop-online-search-stock10.png`: Ảnh chụp quầy thu ngân với tồn khả dụng ban đầu = 10.
- `02-desktop-stock-refreshed-8.png`: Ảnh chụp quầy thu ngân tự động cập nhật tồn = 8 sau khi mua 2 không cần F5.
- `03-desktop-offline-pwa-shell.png`: Ảnh chụp PWA tải lại từ Service Worker trong chế độ hoàn toàn Offline.
- `04-touch-viewport-tablet.png`: Ảnh chụp giao diện quầy thu ngân chế độ cảm ứng máy tính bảng.
- `baseline-r4-failures.log`: Log ghi nhận lỗi trước sửa trên baseline R4.
