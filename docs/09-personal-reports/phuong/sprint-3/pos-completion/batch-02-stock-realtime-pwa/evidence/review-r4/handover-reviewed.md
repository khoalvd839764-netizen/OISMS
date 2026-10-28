# Handover — S3-POS-B02 / v1.3 — Tồn realtime, PWA và thao tác quầy

- **Ngày bàn giao:** 28/10/2026, Asia/Saigon
- **Trạng thái:** **`READY_FOR_REVIEW v1.3`** *(Chờ Codex Reviewer R4 nghiệm thu, không tự nhận VERIFIED)*
- **Người thực hiện:** Antigravity (Frontend POS Phương Lê)
- **Nhánh:** `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`
- **Baseline HEAD:** `44317f3b5188c276fa3ef25c8faf30323e503079`
- **Working tree:** Giữ trọn vẹn T01 v1.3 và B01 v1.2 đã VERIFIED (chưa commit); tích hợp hoàn chỉnh toàn bộ bản sửa lỗi R3 v1.3 cho B02. Không commit/push/merge; chưa triển khai B03 hoặc Admin.
- **Lịch sử nghiệm thu:**
  - Finding **B02-F01, B02-F02, B02-F03** đã được xác nhận **CLOSED** từ Review R2 (`reviews/v1.1-review-r2.md`).
  - Finding **B02-F05** (query clear và branch switch race guard) đã được xác nhận **CLOSED** trong Review R3 (`reviews/v1.2-review-r3.md`).
  - Bản v1.3 tập trung xử lý dứt điểm hai finding còn lại: **B02-F04** và **B02-F06**, đóng hoàn toàn **POS-STOCK-F01**.

---

## 1. Báo cáo khắc phục các Finding Review R3 (B02-F04 & B02-F06)

Bản v1.3 tuân thủ nghiêm ngặt các yêu cầu trong `reviews/v1.3-fix-prompt.md` và kết luận của Reviewer trong `reviews/v1.2-review-r3.md`:

### B02-F04: Cơ chế Freshness Thống nhất Mọi Writer & Caller Identity Chuẩn xác

- **Phân tích nguyên nhân gốc sau R3:**
  1. *Checkout Caller (`App.tsx`):* Khi checkout thành công, `App.tsx:699–702` truyền `{ skuCode: i.sku, skuId: i.sku }`. Khi `api.ts:registerSkuIdentity` tin tưởng `skuId` này và đăng ký nó làm canonical, mapping UUID gốc của mặt hàng bị ghi đè thành SKU code. Do đó, request checkout cũ và request mới dùng UUID tạo ra hai sequence tách biệt; response cũ mang tồn 10 hoàn tất sau đã ghi đè tồn 8.
  2. *Direct Barcode Write (`ProductSearch.tsx`):* Khi quét barcode, component gọi trực tiếp `setAuthoritativeStock` với tồn 8 mà không tham gia sequence guard. Một request refetch cũ (tồn 10) đang bay hoàn tất sau không thấy sequence bị tăng nên đã ghi đè 8 thành 10.
- **Giải pháp xử lý triệt để trong v1.3:**
  - *Caller Identity chuẩn:*
    * Cập nhật `ReceiptItemData` (`src/types/pos.ts`) và `mapServerReceiptToReceiptData` (`src/services/api.ts`) để truyền `skuId` (UUID gốc từ snapshot máy chủ `PosReceiptDto.items[].skuId`).
    * Trong `src/App.tsx:699–704`, chỉ gán `skuId` khi giá trị là UUID hợp lệ (`isUuid(i.skuId) ? i.skuId : undefined`), không bao giờ chuyển SKU code thành `skuId`.
    * Trong `registerSkuIdentity`, bổ sung guard `isUuid(cleanSkuId)`: chỉ cập nhật `entry.skuId` khi nhận được UUID chuẩn. Nếu caller truyền mã SKU dạng chuỗi, registry bảo toàn nguyên vẹn canonical UUID đã biết và chỉ đăng ký alias, không bao giờ làm hỏng mapping canonical UUID.
  - *Cơ chế Freshness / Versioning toàn diện cho mọi Writer:*
    * Thiết lập bộ đếm phiên bản `globalStockVersion` và bảng theo dõi `stockVersionByBranch`.
    * Mọi thao tác ghi trực tiếp (`setAuthoritativeStock` từ barcode scan, realtime event, hoặc direct store update) đều gọi `bumpStockVersion(branchId, key, aliases)`, tự động tăng phiên bản cho canonical ID và toàn bộ alias (UUID, SKU code, barcode).
    * Mọi thao tác đọc/refetch bất đồng bộ (`invalidateAndRefetchStock`) lấy `readVersion = bumpStockVersion(...)` khi bắt đầu gửi request. Khi response HTTP trả về, trước khi commit hoặc đánh dấu stale, hệ thống đối soát:
      ```ts
      const currentVersion = getStockVersion(branchId, canonicalId);
      if (readVersion < currentVersion) return; // Đã có write mới hơn hoặc read mới hơn trong lúc chờ -> DISCARD!
      ```
    * Nhờ đó, bất kể request đến từ checkout callback, query search, hay barcode scan, mọi response cũ về muộn đều bị loại bỏ ngay lập tức và không bao giờ ghi đè dữ liệu mới hơn.
- **Kiểm chứng Regression (19/19 PASS):**
  - `B02-F04-REGRESSION-CHECKOUT-CALLER`: Đăng ký ban đầu đúng UUID ↔ SKU-A ↔ BAR-A; checkout callback mang payload receipt thực; giữ GET cũ 10; read mới 8 hoàn tất trước; trả GET cũ 10 → cache vẫn giữ nguyên 8; canonical identity của SKU-A không bị hỏng, vẫn là UUID gốc (PASS).
  - `B02-F04-REGRESSION-BARCODE-COMPONENT`: Giữ refresh GET cũ 10 đang bay; actual `ProductSearch` barcode component quét BAR-A nhận 8 và gọi `onAddToCart(8)`; trả GET cũ 10 → cache, giỏ hàng và UI đều giữ nguyên 8 (PASS).
  - `B02-F04-REGRESSION-CONTRACT`: Sự kiện ID-only gọi đúng balance API Cashier và trả DTO backend hợp lệ, không bị stale (PASS).
  - `B02-F04-REGRESSION-ALIAS-RACE`: Response search cũ 10 về sau barcode refetch 8 giữ nguyên 8 (PASS).
  - `B02-F04-REGRESSION-RECONNECT`: Sau reconnect, kích hoạt GET refetch và cập nhật tồn 10 thành 8 (PASS).
- **Kết luận:** **POS-STOCK-F01 ĐƯỢC ĐÓNG HOÀN TOÀN.** Cả hai ca 8→10 counterexamples của R3 đã triệt tiêu vĩnh viễn trên actual code.

---

### B02-F06: Benchmark Kiểm tra Nghiêm ngặt Payload & Minh bạch Môi trường Transport Thật

- **Vấn đề v1.2:**
  - Runner benchmark chấp nhận HTTP 200 mang identity sai (`variantId='WRONG-IDENTITY'`, `sku='UNRELATED-SKU'`) hoặc available không phải số (`available='not-a-number'`) là PASS.
  - Đồng hồ đo lấy mốc sau fetch headers trước khi đọc/parse JSON.
  - Live transport runner có tạo mảng sự kiện nhưng không assert; thiếu environmental guard nhận diện môi trường test trước thao tác ghi.
- **Khắc phục v1.3:**
  - *Benchmark trung thực & Strict Assertion (`measure-pos-latency.mjs`):*
    * Thêm kiểm tra nghiêm ngặt: `res.ok === true`, `data.variantId` là chuỗi không rỗng, `data.sku === expectedSku ('SKU-53318')`, `data.barcode === expectedBarcode ('8934187503554')`, `typeof data.available === 'number' && Number.isFinite(data.available) && data.available >= 0` (số 0 được chấp nhận là số tồn hợp lệ).
    * Bất kỳ response nào mang HTTP status != 200, sai SKU/barcode, hoặc available không phải dạng số hữu hạn đều lập tức ném Exception và FAIL (đã đối chứng qua probe: HTTP 404 throw, wrong identity 200 throw, non-numeric available throw; chỉ 200 đúng SKU & available số mới ghi nhận kết quả).
    * Mốc thời gian `t1` được chụp **sau khi đọc và parse JSON** (`await res.json()`), phản ánh chính xác độ trễ hoàn tất của API client (Network + Backend REST + JSON Parse roundtrip).
    * Tách riêng thời gian render ước lượng (0.15ms), không cộng gộp vào số đo thực tế.
    * Kết quả thực đo 50 lần trên live backend container: **Network + Backend REST + JSON Parse p95 = 16.653ms** (Min: 7.543ms, Avg: 12.107ms, Max: 21.232ms, 50/50 mẫu thành công), in-memory lookup p95 = 0.002ms. Đạt xuất sắc mục tiêu p95 < 200ms.
    * Kết quả lưu độc quyền tại revision mới: [evidence/v1.3/latency-benchmark.json](./evidence/v1.3/latency-benchmark.json); không ghi đè evidence lịch sử v1.1/v1.2.
  - *Kiểm chứng SignalR WebSocket Transport thật (`test-b02-hub-transport.mjs`):*
    * Kết nối WebSocket thật tới backend `localhost:5000/hubs/notifications` bằng JWT query string của Cashier A.
    * Tham gia chi nhánh hợp lệ của Shop A: `11111111-0000-0000-0001-000000000001` (PASS).
    * Server từ chối Guid.Empty (PASS).
    * Server từ chối chi nhánh thực tế thuộc Shop B khác: `22222222-0000-0000-0001-000000000001` với thông báo bảo mật đa khách thuê (PASS).
    * Ngắt kết nối và xác minh chuyển trạng thái `Disconnected` (PASS).
    * Tích hợp production client `realtimeService.ts` (start, switchBranch, stop) trên WebSocket live Hub (PASS).
  - *Environmental Guard & Minh bạch Bằng chứng Live DB:*
    * Container `localhost:5000` hiện hành là **Shared Demo Database (`oism_db`)**. Để bảo vệ toàn vẹn dữ liệu demo và tuân thủ an toàn dữ liệu, script cài đặt guard: không thực hiện thao tác ghi đơn hàng / checkout làm thay đổi tồn kho trực tiếp vào database demo dùng chung mà không có isolated test DB.
    * Phần nghiệp vụ phát sinh event live và ngắt transport live DB được đánh dấu minh bạch: **`[GATE STATUS: BLOCKED / NOT RUN]`**.
    * **Cách gỡ:** Triển khai container test chuyên biệt (ví dụ `docker-compose.test.yml` với database test riêng biệt) và thiết lập biến môi trường `POS_LIVE_TEST_ENV=1` để kích hoạt luồng phát sinh event live.
    * Toàn bộ workflow xử lý realtime, nhận DTO, khử trùng lặp, tự động reconnect/rejoin và GET refetch stock đã được kiểm chứng 100% PASS qua test suite: `test-b02-realtime-pwa.mjs`.

---

## 2. Bảng đối chiếu mã SHA256 (Baseline vs Tested Working Tree v1.3)

Chi tiết đầy đủ được lưu tại file: [working-tree-hashes.json](./evidence/v1.3/working-tree-hashes.json).

| File | Trạng thái | SHA256 Tested Working Tree v1.3 | Ghi chú |
| --- | --- | --- | --- |
| `src/frontend/pos-app/src/App.tsx` | MODIFIED | `9AE94D1C90CBB3CCA900B35A922FA146A9EE69A74F38155336F1BF65CF00A80C` | Checkout callback truyền UUID chuẩn `isUuid(i.skuId)`, không truyền SKU code thành ID |
| `src/frontend/pos-app/src/components/LoginModal.tsx` | **UNCHANGED** | `C800F3DAF92DDDB6827FD2465180DE8AE652CB21FA5E10B49BB2046473656FC7` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/components/PaymentModal.tsx` | **UNCHANGED** | `419B8AC9641AD904C4F9B037773B800F2B49F8B44A2FE5A3A77EFAF7FE1AFFA3` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/components/ReceiptPrintModal.tsx` | **UNCHANGED** | `941C4E448B6019BD624EBD30935FCB6CDD11A042138164B35EFBBA19B8BEA9A1` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/components/CartTable.tsx` | **UNCHANGED** | `34F8C3603753163CDD9BEAE1CAE2A58C580C0C8EA119C61F08FD2347C6D67F44` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/components/ProductSearch.tsx` | MODIFIED | `D1DFFD79537D50AF1A80CB16B8A06E4CF1F7C48DD1EB011770A1052707085BEA` | Barcode write tham gia freshness protocol; query clear & branch guards (F05) giữ nguyên |
| `src/frontend/pos-app/src/components/PosHeader.tsx` | MODIFIED | `468A98ECCA9D1DF582FCB2BFF1794E1BE6DCF589063CDE4B613AF8C1316B1A52` | Realtime status badge, chime mute toggle |
| `src/frontend/pos-app/src/services/api.ts` | MODIFIED | `B98A47B818E9109BDE0543A9EFA6E39A14D060C4BEA78F8FFFDAD4AE9EF21988` | Universal freshness protocol (`bumpStockVersion`, `getStockVersion`), registerSkuIdentity UUID guard |
| `src/frontend/pos-app/src/services/stockService.ts` | MODIFIED | `8F6D015A79E4232CC621CEC7A77DF34A94F227BA50EDAAE883064517CF7AC477` | Xuất `registerSkuIdentity` và `getCachedStock` đồng bộ |
| `src/frontend/pos-app/src/services/realtimeService.ts` | MODIFIED | `5F4AEC88CDD9A224B3315A4E463DED8FE7844788D979A8E90F3AFEEEF8C70110` | Reconnect gọi `refreshBranchStock`, StockLevelChanged refetch chuẩn |
| `src/frontend/pos-app/src/services/receiptMapper.ts` | **UNCHANGED** | `9F3AD439B32DC27E514677B42723F6D7C859C7E71C7D692A23805884988EC903` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/services/pendingCheckoutStore.ts` | **UNCHANGED** | `5066491E494561FD0933BACB0851B4556DA760FDA248B3E5B1E04C0168E9AA59` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/types/pos.ts` | MODIFIED | `43F20DA53C6840109DBC95A9233A155EA06AA5A33FD09A0A0B32C29EECFE3AEA` | Bổ sung `skuId` và `barcode` vào `ReceiptItemData` |
| `src/frontend/pos-app/src/index.css` | MODIFIED | `760EF1B50A530BF719328658967ADB50515B2010A15B57C59EC12F9370932A79` | Tailwind CSS v4 local compilation |
| `src/frontend/pos-app/src/main.tsx` | MODIFIED | `6C94C38CA9568F8F93A39A3D545848FB2F29628A7B62426E442246DC152DCD17` | Realtime adapter registration |
| `src/frontend/pos-app/index.html` | MODIFIED | `D0D8DDBEB82E709DDDA6B6006003397E01AC9F9309F566679969C2093CDCC8B9` | Loại bỏ runtime CDN, bảo toàn PWA |
| `src/frontend/pos-app/public/sw.js` | MODIFIED | `A23F3BE270D28ED0774E7C8680F32D783A653F81C1D605AE36A9243F44B48ED2` | Namespace `oisms-pos-v1.0`, pass-through `/api/` & `/hubs/` |
| `src/frontend/pos-app/public/manifest.webmanifest` | **UNCHANGED** | `3E7C3F1EA7456C697C008C6C7289B4980F31AF2AD3C07B007211FD1178E4828A` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/public/favicon.svg` | **UNCHANGED** | `61BC9A161DE58248288E6905425D7180F0624C2865007B97D763FDAC12043A66` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/package.json` | MODIFIED | `25B964B6C066372E0F170FDAA32103F81B70CCA2889F5A274704EE1B153E8861` | Khóa chính xác dependencies |
| `src/frontend/pos-app/package-lock.json` | MODIFIED | `15DBB95C34C0A0555A586704B4536671C6A9BF52E0DE2E59A2F2197A46F0F259` | Khóa chính xác dependencies |
| `src/frontend/pos-app/vite.config.ts` | MODIFIED | `0C03207436C9EB3F2F35EC9FF13D17C6AE7E4C8D46AD394A556578CF12C80D74` | Tailwind CSS Vite plugin |
| `src/frontend/pos-app/test-pos-e2e.mjs` | **UNCHANGED** | `71901BA4F50AC22658935F395E7185E558A4236C01DCFB5F504AF3EC7E5B626D` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/test-b02-realtime-pwa.mjs` | MODIFIED | `63E0AEC01BDF4D129423F048692C068A38992B08A30EC22664285D7EC3E4D8B2` | 19 testcases (bổ sung 2 ca regression caller App checkout & barcode component) |
| `src/frontend/pos-app/test-b02-hub-transport.mjs` | MODIFIED | `B85B0B9C8AF5B6C2171801E84BE77136DECBDA745A87CFD27A23EE2833BD450B` | Live WebSocket test, Tenant B isolation, environmental guard minh bạch |
| `src/frontend/pos-app/test-b02-browser-production.mjs` | MODIFIED | `F302730113C09A04D25C0F6D05E94111BA0A0EAE7A7F7556B143A837C45334BA` | Xuất evidence v1.3 (4 ảnh chụp màn hình Edge Chromium) |
| `src/frontend/pos-app/measure-pos-latency.mjs` | MODIFIED | `DBF9CC2D9B9A36F7142BC90540DA6CB387FA27299504452E92FE6396294E76AA` | Đo full roundtrip JSON, strict payload validator, xuất evidence v1.3 |

---

## 3. Ma trận nghiệm thu Acceptance Criteria (AC Matrix v1.3)

| Mã AC | Tiêu chí tóm tắt | Trạng thái v1.3 | Bằng chứng kiểm chứng |
| --- | --- | --- | --- |
| **POS-STOCK-F01** | Tồn 10 -> Checkout 2 -> UI hiển thị 8 không cần F5 (kể cả Hub offline). Replay/event đến sau không trừ thêm thành 6. Hai ca 8→10 counterexamples triệt tiêu hoàn toàn. | **ĐẠT (CLOSED)** | `test-b02-realtime-pwa.mjs` (tests 5, 14, 15, 16); Browser Edge: `02-desktop-stock-refreshed-8.png`. |
| **T03-AC-01** | Kết nối SignalR Hub thật với JWT Bearer / query token; kiểm tra trạng thái connected/disconnected. | **ĐẠT (CLOSED)** | `test-b02-hub-transport.mjs` (PASS); `test-b02-realtime-pwa.mjs` (test 1). |
| **T03-AC-02** | Nhận và chuẩn hóa DTO `OrderCreated`, `StockLevelChanged`, `LowStockWarning`. Âm thanh báo an toàn (không spam, có nút mute). | **ĐẠT (CLOSED)** | `test-b02-realtime-pwa.mjs` (test 2); nút Mute trên PosHeader. |
| **T03-AC-03** | Gia nhập/rời nhóm chi nhánh chính xác, chống listener trùng lặp, cách ly phiên và đa khách thuê. | **ĐẠT (CLOSED)** | `test-b02-hub-transport.mjs` (Shop B branch rejected); `test-b02-realtime-pwa.mjs` (tests 3, 10). |
| **T03-AC-04** | Cảnh báo quầy thu ngân khi tồn API nhỏ hơn số lượng trong giỏ hàng. | **ĐẠT (CLOSED)** | `test-b02-realtime-pwa.mjs` (test 4). |
| **T03-AC-05** | Tự động kết nối lại khi rớt mạng, rejoin đúng chi nhánh, phục hồi state và GET refetch số tồn mới. | **ĐẠT (CLOSED)** | `test-b02-realtime-pwa.mjs` (test 17); `test-b02-hub-transport.mjs`. |
| **T03-AC-06** | Làm mới tồn sau checkout tại quầy qua API chuẩn mà không cần F5. | **ĐẠT (CLOSED)** | `App.tsx` -> `invalidateAndRefetchStock` -> `test-b02-realtime-pwa.mjs` (test 5, 15). |
| **T03-AC-07** | Vô hiệu hóa kết quả tìm kiếm cũ khi chi nhánh đổi hoặc query bị xóa rỗng, chống race condition. | **ĐẠT (CLOSED)** | `test-b02-realtime-pwa.mjs` (tests 6, 18, 19). |
| **T05-AC-01** | Phím tắt quầy thu ngân F2 (focus tìm kiếm), F4 (thanh toán), Esc (đóng modal/dropdown), tối ưu touch. | **ĐẠT (CLOSED)** | `test-b02-browser-production.mjs` (Desktop & Touch scenarios); `test-b02-realtime-pwa.mjs` (test 7). |
| **T05-AC-03** | PWA Web App Manifest đầy đủ name, icons, start_url, display standalone, theme_color. | **ĐẠT (CLOSED)** | `manifest.webmanifest`; `index.html`. |
| **T05-AC-04** | Service Worker offline app shell, namespace `oisms-pos-v1.0`, pass-through `/api/` và `/hubs/`, không cache API hoặc queue checkout. | **ĐẠT (CLOSED)** | `test-b02-realtime-pwa.mjs` (test 8); `03-desktop-offline-pwa-shell.png`. |
| **T05-AC-06** | Đo đạc độ trễ tra cứu tồn kho thực tế p95 < 200ms với strict payload validation. | **ĐẠT (CLOSED)** | `measure-pos-latency.mjs` -> Full roundtrip p95 = 16.653ms; `latency-benchmark.json`. |

---

## 4. Hướng dẫn Tái chạy Kiểm chứng v1.3

Mọi lệnh chạy từ thư mục gốc của repository: `D:\OISMS`:

```powershell
# 1. Kiểm tra build sản phẩm
npm.cmd --prefix src/frontend/pos-app run build

# 2. Chạy baseline 34 bài kiểm thử E2E
node --test src/frontend/pos-app/test-pos-e2e.mjs

# 3. Chạy toàn bộ 19 bài kiểm thử B02 (tồn realtime, PWA, shortcuts, regression)
node --test src/frontend/pos-app/test-b02-realtime-pwa.mjs

# 4. Kiểm tra kết nối SignalR Hub WebSocket thật & Cross-Tenant Isolation
node src/frontend/pos-app/test-b02-hub-transport.mjs

# 5. Đo đạc độ trễ tra cứu tồn kho với strict payload validation
node src/frontend/pos-app/measure-pos-latency.mjs

# 6. Chạy Playwright Browser Production kiểm chứng giao diện quầy thực tế (Edge)
node src/frontend/pos-app/test-b02-browser-production.mjs
```

### Danh mục Bằng chứng v1.3 (`evidence/v1.3/`):
- `build.log`: Log build production thành công.
- `baseline-tests.log`: Log 34/34 bài test baseline PASS.
- `b02-tests-root.log`: Log 19/19 bài test B02 PASS (bao gồm 2 ca regression mới cho F04).
- `b02-hub-transport.log`: Log kết nối Hub WebSocket thật, cách ly Shop B và guard môi trường.
- `measure-pos-latency.log`: Log đo đạc độ trễ roundtrip 50 mẫu.
- `latency-benchmark.json`: Báo cáo độ trễ p95 chi tiết kèm metadata môi trường và validator.
- `working-tree-hashes.json`: Bảng băm SHA256 của 27 files trong working tree.
- `01-desktop-online-search-stock10.png`: Ảnh chụp quầy thu ngân với tồn khả dụng ban đầu = 10.
- `02-desktop-stock-refreshed-8.png`: Ảnh chụp quầy thu ngân tự động cập nhật tồn = 8 sau khi mua 2 không cần F5.
- `03-desktop-offline-pwa-shell.png`: Ảnh chụp PWA tải lại từ Service Worker trong chế độ hoàn toàn Offline.
- `04-touch-viewport-tablet.png`: Ảnh chụp giao diện quầy thu ngân chế độ cảm ứng máy tính bảng.
