# Handover — S3-POS-B02 / v1.7 — Tồn realtime, PWA và thao tác quầy

- **Ngày bàn giao:** 28/10/2026, Asia/Saigon
- **Trạng thái:** **`READY_FOR_REVIEW v1.7`** *(Chờ Reviewer nghiệm thu, không tự nhận VERIFIED)*
- **Người thực hiện:** Antigravity (Frontend POS Phương Lê)
- **Nhánh:** `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`
- **Baseline HEAD:** `a20ebe9d8580a3426e764497ac306fe620942498`
- **Working tree:** Giữ trọn vẹn T01 v1.3 và B01 v1.2 đã VERIFIED; tích hợp hoàn chỉnh bản sửa lỗi R7 v1.7 cho B02. Không commit/push/merge/reset/clean/stash; không sửa Admin hay B03.
- **Lịch sử nghiệm thu:**
  - Finding **B02-F01, B02-F02, B02-F03** đã được xác nhận **CLOSED** từ Review R2 (`reviews/v1.1-review-r2.md`).
  - Finding **B02-F05** (query clear và branch switch race guard) đã được xác nhận **CLOSED** trong Review R3 (`reviews/v1.2-review-r3.md`).
  - Benchmark tra cứu tồn với strict payload validation (p95 = 16.653ms) đã được xác nhận **ĐẠT** trong Review R4 (`reviews/v1.3-review-r4.md`).
  - Finding **B02-F04** (thứ tự read→commit bao phủ cả failure/stale ordering và UI cart) đã được xác nhận **CLOSED** trong Review R6 (`tmp/pos-b02-review-r6/review-summary.json`).
  - Finding **B02-F06** được hoàn thiện trọn vẹn trong **v1.7** theo đúng Review R7 (`tmp/pos-b02-review-r7/review-summary.json`) và prompt:
    1. **Môi trường Test Database Độc lập (PostgreSQL 5433 + Backend 5001):** Khởi dựng môi trường test riêng biệt với schema hoàn chỉnh 31 bảng và marker, user role `oisms_test_app`. Loại bỏ hoàn toàn cờ bypass `POS_TEST_DB_ISOLATION_CONFIRMED=1`. Bảo vệ tuyệt đối database demo dùng chung (localhost:5000).
    2. **Ngắt Transport Thật bằng Network Reverse Proxy:** Xây dựng TCP/WebSocket reverse proxy trong test runner, ngắt kết nối vật lý thật ở tầng socket mạng (mã đóng 1006). Xóa bỏ hoàn toàn helper test hack `_disruptTransportForTesting` khỏi mã nguồn sản xuất `realtimeService.ts`. Không mock SDK, không can thiệp private API.
    3. **Khắc phục Triệt để False PASS:** Khẳng định chặt chẽ toàn bộ recovery workflow: nhận `OrderCreated` + `StockLevelChanged`, ngắt transport vật lý, tạo biến động tồn trong lúc offline, tự động reconnect, khẳng định rejoin chi nhánh thành công (`successfulJoinsAfterDrop > 0`), khẳng định có request GET stock sau reconnect (`stockReadsAfterDrop > 0`), và cache khôi phục chính xác số tồn cuối không stale.
    4. **Preflight Offline Harness 8 Kịch bản:** Kiểm chứng trọn vẹn cả luồng thành công, 2 chốt chặn bảo vệ (demo guard, isolation guard) và 5 probe phát hiện khuyết tật (thiếu sự kiện tồn, thiếu auto-reconnect, số tồn sai 123 !== 7, rejoin bị reject, bỏ qua GET refetch).

---

## 1. Báo cáo khắc phục Finding B02-F06 (Review R7)

Bản v1.7 giải quyết dứt điểm 3 điểm cốt lõi được nêu trong Review R7:

### 1.1. Môi trường Test Database Độc lập (Loại bỏ cờ bypass, xác minh quyền sở hữu)

#### A. Phân tích lỗi trên baseline v1.6:
- Trong v1.6, runner cho phép cờ môi trường `POS_TEST_DB_ISOLATION_CONFIRMED=1` để bỏ qua việc kiểm chứng quyền sở hữu database thật.
- Điều này tạo lỗ hổng cho phép chạy kiểm thử mutation trên cơ sở dữ liệu chưa được chứng minh cách ly thực sự.

#### B. Giải pháp xử lý triệt để trong v1.7:
1. **Thiết lập Cụm Container Test Độc lập:**
   - Container PostgreSQL: `oisms_test_pg_check` (port 5433 -> 5432) chạy trên image `postgres:16-alpine`.
   - Cơ sở dữ liệu: `oisms`, áp dụng toàn bộ schema chuẩn `docs/database/oisms_schema.sql` (30 bảng nghiệp vụ) + bảng marker `_oisms_test_fixture_marker` (tổng cộng 31 bảng).
   - Quyền người dùng: Tạo role chuyên biệt `oisms_test_app` với quyền `oisms_runtime` tối thiểu, không dùng siêu người dùng `postgres`.
   - Container Backend: `oisms_test_backend` (port 5001 -> 8080) kết nối trực tiếp vào `oisms_test_pg_check` với connection string của `oisms_test_app`.
2. **Loại bỏ Hoàn toàn Cờ Bypass:**
   - Trong `test-b02-hub-transport.mjs`, xóa bỏ hoàn toàn điều kiện `process.env.POS_TEST_DB_ISOLATION_CONFIRMED === '1'`.
   - Bắt buộc kiểm tra `/health/ready`: phải có `isHealthy === true` VÀ `currentUser === 'oisms_test_app'`.
   - Nếu không thỏa mãn, runner lập tức FAIL CLOSED với lỗi `[ISOLATION GUARD REJECTED]` trước bất kỳ thao tác nghiệp vụ nào.
3. **Demo Target Guard (Bảo vệ Port 5000):**
   - Runner kiểm tra `POS_TEST_BACKEND_URL`: nếu trỏ vào `localhost:5000` (shared demo container), lập tức từ chối với `[DEMO TARGET GUARD REJECTED]` và không gửi bất kỳ mutation nào.

---

### 1.2. Ngắt Transport Thật bằng Network Reverse Proxy (Loại bỏ Helper Test Hack khỏi Production)

#### A. Phân tích lỗi trên baseline v1.6:
- Trong v1.6, `realtimeService.ts` chứa helper `_disruptTransportForTesting()` can thiệp vào private handler `_connectionClosed` của SignalR SDK.
- Điều này vi phạm nguyên tắc bảo toàn mã nguồn sản xuất và không chứng minh được socket mạng thực sự bị ngắt kết nối.

#### B. Giải pháp xử lý triệt để trong v1.7:
1. **Làm sạch Mã nguồn Sản xuất (`realtimeService.ts`):**
   - Xóa bỏ hoàn toàn hàm `_disruptTransportForTesting()` khỏi `realtimeService.ts`.
   - Mã nguồn sản xuất 100% sạch sẽ, không có bất kỳ test hack hay phụ thuộc vào internal/private API của SignalR SDK.
2. **Cơ chế Reverse Proxy Điều khiển Gián đoạn Mạng (`test-b02-hub-transport.mjs`):**
   - Xây dựng `createNetworkProxy(targetUrl)` sử dụng module `http` và `net` của Node.js:
     * Chuyển tiếp toàn bộ HTTP request và WebSocket Upgrade (`server.on('upgrade')`) tới backend thật.
     * Khi test gọi `netControl.disrupt()`: proxy ngắt và hủy lập tức toàn bộ TCP sockets đang hoạt động (`socket.destroy()`). SignalR SDK client ghi nhận mã lỗi đóng kết nối `1006 (no reason given)` từ tầng mạng thật.
     * Khi test gọi `netControl.restore()`: proxy mở lại lưu lượng mạng, cho phép SignalR SDK tự động kích hoạt cơ chế `withAutomaticReconnect()` để kết nối lại máy chủ một cách tự nhiên.

---

### 1.3. Khắc phục Triệt để Hiện tượng False PASS trong Recovery Assertions

#### A. Phân tích lỗi trên baseline v1.6:
- Reviewer audit harness tái hiện runner v1.6 báo PASS kể cả khi:
  * Không có request GET tồn sau reconnect.
  * Rejoin nhóm chi nhánh bị server từ chối.
  * Cache cuối chứa số tồn sai khác (ví dụ: tồn = 123 thay vì 7).

#### B. Giải pháp xử lý triệt để trong v1.7:
Runner `test-b02-hub-transport.mjs` v1.7 bổ sung các chốt kiểm tra bắt buộc (strict assertions):
1. **Khẳng định Sự kiện Khởi tạo Nghiệp vụ Online:**
   - Gọi `POST /api/orders` (online order qua quyền Owner) để phát sinh đồng thời cả 2 sự kiện: `OrderCreated` và `StockLevelChanged`.
   - Assert nhận được `OrderCreated` đúng `orderNumber` vừa tạo.
   - Assert nhận được `StockLevelChanged` đúng `skuId` và `branchId`.
2. **Khẳng định Mất Kết nối Thực sự:**
   - Sau khi gọi `netControl.disrupt()`, assert trạng thái của client chuyển sang mất kết nối (`realtimeStatus !== 'connected'`).
3. **Biến động Tồn trong lúc Offline:**
   - Thực hiện `POST /api/pos/checkout` trực tiếp lên backend trong lúc client đang rớt mạng để làm giảm tồn trên server mà client không nhận được broadcast trực tiếp.
4. **Khẳng định Tự động Reconnect:**
   - Khôi phục mạng, assert `reconnectedEvents.length > 0` và `realtimeStatus === 'connected'`.
5. **Khẳng định Rejoin Chi nhánh Thành công:**
   - Chặn đếm số lần `JoinBranchGroup` thành công sau khi mạng rớt: `assert.ok(successfulJoinsAfterDrop > 0)`.
   - Nếu rejoin bị reject -> Runner FAIL ngay lập tức.
6. **Khẳng định GET Refetch Phục hồi Tồn Bị Lỡ:**
   - Đếm số lượng request GET tồn (`/api/inventory/balances` hoặc `/api/pos/search` hoặc `/api/pos/barcode`): `assert.ok(stockReadsAfterDrop > 0)`.
   - Nếu không có request GET tồn sau reconnect -> Runner FAIL ngay lập tức.
7. **Khẳng định Giá trị Tồn Cuối trong Cache:**
   - Assert cache tồn kho được cập nhật: `assert.ok(recoveredStock)`.
   - Assert không bị đánh dấu stale: `assert.equal(recoveredStock.isStale, false)`.
   - Trong môi trường fixture/controlled: `assert.equal(recoveredStock.available, 7)` (khớp chính xác: 10 - 2 - 1 = 7).
   - Trong môi trường live: assert số tồn là số nguyên hợp lệ và được cập nhật đầy đủ.

---

### 1.4. Preflight Offline & Audit Harness 8 Kịch bản (`test-b02-hub-preflight.cjs`)

Bộ audit harness được mở rộng lên 8 kịch bản toàn diện:
- **Test 1:** Full Live Workflow trên Test Host độc lập -> **PASS 100%**.
- **Test 2:** Demo Target Guard fail closed trên port 5000 -> **PASS 100%** (0 request checkout).
- **Test 3:** Database Isolation Guard fail closed khi role không phải `oisms_test_app` -> **PASS 100%**.
- **Test 4 (Defect Probe):** Bắt lỗi khi backend thiếu sự kiện `StockLevelChanged` -> **BẮT LỖI THÀNH CÔNG (AssertionError)**.
- **Test 5 (Defect Probe):** Bắt lỗi khi SDK thiếu automatic reconnect -> **BẮT LỖI THÀNH CÔNG (AssertionError)**.
- **Test 6 (Defect Probe):** Bắt lỗi khi số tồn khôi phục sai (123 !== 7) -> **BẮT LỖI THÀNH CÔNG (AssertionError)**.
- **Test 7 (Defect Probe):** Bắt lỗi khi rejoin chi nhánh bị từ chối -> **BẮT LỖI THÀNH CÔNG (AssertionError)**.
- **Test 8 (Defect Probe):** Bắt lỗi khi bỏ qua request GET refetch sau reconnect -> **BẮT LỖI THÀNH CÔNG (AssertionError)**.

Kết quả kiểm chứng chi tiết: [`evidence/v1.7/preflight-runner.log`](./evidence/v1.7/preflight-runner.log) và [`evidence/v1.7/preflight-runner-results.json`](./evidence/v1.7/preflight-runner-results.json).

---

## 2. Bảng đối chiếu mã SHA-256 (Tested Working Tree v1.7)

Chi tiết được lưu tại: [working-tree-hashes.json](./evidence/v1.7/working-tree-hashes.json). Khớp 100% toàn bộ 21 tệp tin.

| Tệp tin | Trạng thái | SHA-256 Tested Working Tree v1.7 | Vai trò / Thay đổi trong v1.7 |
| :--- | :---: | :---: | :--- |
| `src/frontend/pos-app/src/App.tsx` | MODIFIED | `8922007fa7db85190627477eda05574ea60f8522b7a02859674bb416791bc2ba` | Cart đồng bộ `available` từ `subscribeBranchStock` & `handleAddToCart` |
| `src/frontend/pos-app/src/components/ProductSearch.tsx` | MODIFIED | `4c9af1c0848db0177d1f1cfcf1645a96688a2bd577ca57502bce619271311d62` | Guard stale cache khi barcode cũ về sau refresh lỗi |
| `src/frontend/pos-app/src/services/api.ts` | MODIFIED | `cc58d91457ce2ed6682aa52638a6e6589bbef4f6c513eaa64b093ad8d7f457c2` | `markStockStale` tích hợp readTicket và `recordCommittedStockVersion` cho failure |
| `src/frontend/pos-app/src/services/stockService.ts` | MODIFIED | `ead0a301f768d8d29b9a4f28fbef71385f5f46decf13f9095351df9e5fa367bd` | Re-export `markStockStale` và các hàm read→commit ticket |
| `src/frontend/pos-app/src/services/realtimeService.ts` | MODIFIED | `df89981ee934291af7a210c097672ce964a99c792d578e99b66231c4a93101f4` | Xóa bỏ hoàn toàn `_disruptTransportForTesting`, mã nguồn sạch 100% |
| `src/frontend/pos-app/src/components/PaymentModal.tsx` | **UNCHANGED** | `419b8ac9641ad904c4f9b037773b800f2b49f8b44a2fe5a3a77efaf7fe1affa3` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/components/PosHeader.tsx` | MODIFIED | `468a98ecca9d1df582fcb2bff1794e1be6dcf589063cde4b613af8c1316b1a52` | Realtime status badge, chime toggle |
| `src/frontend/pos-app/src/components/ReceiptPrintModal.tsx` | **UNCHANGED** | `941c4e448b6019bd624ebd30935fcb6cdd11a042138164b35efbba19b8bea9a1` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/components/LoginModal.tsx` | **UNCHANGED** | `c800f3daf92dddb6827fd2465180de8ae652cb21fa5e10b49bb2046473656fc7` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/types/pos.ts` | MODIFIED | `43f20da53c6840109dbc95a9233a155ea06aa5a33fd09a0a0b32c29eecfe3aea` | Bổ sung `skuId` và `barcode` trong `ReceiptItemData` |
| `src/frontend/pos-app/src/services/pendingCheckoutStore.ts` | **UNCHANGED** | `5066491e494561fd0933bacb0851b4556da760fda248b3e5b1e04c0168e9aa59` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/src/services/receiptMapper.ts` | **UNCHANGED** | `9f3ad439b32dc27e514677b42723f6d7c859c7e71c7d692a23805884988ec903` | Khớp 100% baseline B01 |
| `src/frontend/pos-app/public/sw.js` | MODIFIED | `a23f3be270d28ed0774e7c8680f32d783a653f81c1d605ae36a9243f44b48ed2` | Namespace `oisms-pos-v1.0`, pass-through API/Hub |
| `src/frontend/pos-app/test-b02-realtime-pwa.mjs` | MODIFIED | `ffd0f17fd05a78ff389fb9d7b4a06fb1ba0ead04d93d76a4af461f7b2c2b09f2` | 24 tests (bao phủ toàn bộ read ordering & failure/stale) |
| `src/frontend/pos-app/test-b02-hub-transport.mjs` | MODIFIED | `fcea9c8444d589e16b066f5cadffbf308199a1b641a5dd0f05d103519673c11d` | TCP reverse proxy, strict recovery assertions, role isolation check |
| `src/frontend/pos-app/test-b02-browser-production.mjs` | MODIFIED | `68d37c59e496a8164aca9546d140ac9ed9e4f85a58c0a3ec0d9825ac5cbac203` | Output evidence sang thư mục `v1.7` |
| `src/frontend/pos-app/test-pos-e2e.mjs` | **UNCHANGED** | `71901ba4f50ac22658935f395e7185e558a4236c01dcfb5f504af3ec7e5b626d` | Khớp 100% baseline B01 (34/34 tests PASS) |
| `src/frontend/pos-app/measure-pos-latency.mjs` | **UNCHANGED** | `dbf9cc2d9b9a36f7142bc90540da6cb387fa27299504452e92fe6396294e76aa` | Benchmark chuẩn p95 = 16.653ms giữ nguyên |
| `src/frontend/pos-app/test-probe-v15.cjs` | ADDED | `56f476d27a269a746ae5c4880f0cc75c1ee66627b1e7d6e2a839692bd8a59fd4` | Bộ probe 6 bài kiểm chứng failure ordering & retry |
| `src/frontend/pos-app/test-b02-hub-preflight.cjs` | ADDED | `4476b839ffee2fd922085e41cc70ec71b38419be56689b40933cf230c8bcb37a` | Harness offline 8 kịch bản: full workflow, demo/isolation guards, 5 defect probes |
| `src/frontend/pos-app/test-b02-browser-search-v16.cjs` | ADDED | `6859f0334e5f3eebb0fd087a1f815827441a16b40c91ab03d3e40f5dddbabd01` | Kiểm chứng Edge Browser thật cho fresh search cập nhật giỏ max = 8 |

---

## 3. Ma trận nghiệm thu Acceptance Criteria (AC Matrix v1.7)

| Mã AC | Tiêu chí tóm tắt | Trạng thái v1.7 | Bằng chứng kiểm chứng |
| :---: | :--- | :---: | :--- |
| **POS-STOCK-F01** | Tồn 10 -> Checkout 2 -> UI hiển thị 8 không cần F5. Replay/event không trừ đúp. Triệt tiêu hoàn toàn race condition thành công lẫn thất bại (read cũ không xóa stale/error của refresh mới). | **ĐẠT (CLOSED)** | `test-b02-realtime-pwa.mjs` (tests 5, 14, 15, 16, 20, 21, 22, 23, 24); `test-probe-v15.cjs`; `browser-search-fixed-8.png`. |
| **T03-AC-01** | Kết nối SignalR Hub thật với JWT token; kiểm tra trạng thái connected/disconnected. | **ĐẠT (CLOSED)** | `test-b02-hub-transport.mjs` (PASS); `test-b02-realtime-pwa.mjs` (test 1). |
| **T03-AC-02** | Nhận và chuẩn hóa DTO `OrderCreated`, `StockLevelChanged`, `LowStockWarning`. Âm thanh báo an toàn có nút Mute. Phát sinh sự kiện nghiệp vụ và kiểm chứng đầy đủ trên test backend độc lập. | **ĐẠT (CLOSED)** | `test-b02-hub-transport.mjs` (`b02-hub-transport-live.log`: PASS); `test-b02-hub-preflight.cjs` (PASS 100%). |
| **T03-AC-03** | Gia nhập/rời nhóm chi nhánh chính xác, chống listener trùng lặp, cách ly Tenant B. | **ĐẠT (CLOSED)** | `test-b02-hub-transport.mjs` (Shop B branch rejected); `test-b02-realtime-pwa.mjs` (tests 3, 10). |
| **T03-AC-04** | Cảnh báo quầy thu ngân khi tồn API nhỏ hơn số lượng trong giỏ hàng. | **ĐẠT (CLOSED)** | `test-b02-realtime-pwa.mjs` (test 4). |
| **T03-AC-05** | Tự động kết nối lại khi rớt mạng, rejoin đúng chi nhánh, phục hồi state và GET refetch tồn kho sau khi ngắt transport vật lý thật qua proxy. | **ĐẠT (CLOSED)** | `test-b02-hub-transport.mjs` (`b02-hub-transport-live.log`: PASS); `test-b02-realtime-pwa.mjs` (tests 17, 24); `test-b02-hub-preflight.cjs` (PASS 100%). |
| **T03-AC-06** | Làm mới tồn sau checkout tại quầy qua API chuẩn mà không cần F5. | **ĐẠT (CLOSED)** | `App.tsx` -> `invalidateAndRefetchStock` -> `test-b02-realtime-pwa.mjs` (tests 5, 15). |
| **T03-AC-07** | Vô hiệu hóa kết quả tìm kiếm cũ khi chi nhánh đổi hoặc query bị xóa rỗng; bảo vệ trạng thái stale khi GET refresh lỗi. | **ĐẠT (CLOSED)** | `test-b02-realtime-pwa.mjs` (tests 6, 18, 19, 22, 23, 24); `test-probe-v15.cjs`. |
| **T05-AC-01** | Phím tắt quầy thu ngân F2, F4, Esc, tối ưu touch viewport. | **ĐẠT (CLOSED)** | `test-b02-browser-production.mjs` (Desktop & Touch scenarios); `test-b02-realtime-pwa.mjs` (test 7). |
| **T05-AC-03** | PWA Web App Manifest đầy đủ standalone, theme_color, icons. | **ĐẠT (CLOSED)** | `manifest.webmanifest`; `index.html`. |
| **T05-AC-04** | Service Worker offline app shell, namespace `oisms-pos-v1.0`, pass-through API/Hub. | **ĐẠT (CLOSED)** | `test-b02-realtime-pwa.mjs` (test 8); `03-desktop-offline-pwa-shell.png`. |
| **T05-AC-06** | Đo đạc độ trễ tra cứu tồn kho thực tế p95 < 200ms với strict payload validation. | **ĐẠT (CLOSED)** | `latency-benchmark.json`: Full roundtrip REST p95 = 16.653ms (50/50 samples valid). |

---

## 4. Hướng dẫn Tái kiểm chứng v1.7

Thực thi tuần tự từ thư mục gốc của repository: `D:\OISMS`:

```powershell
# 1. Build production TypeScript & Vite (PASS 100%)
npm.cmd --prefix src/frontend/pos-app run build

# 2. Chạy 34 bài kiểm thử hồi quy baseline (PASS 100%)
node --test src/frontend/pos-app/test-pos-e2e.mjs

# 3. Chạy toàn bộ 24 bài kiểm thử B02 (PASS 100%)
node --test src/frontend/pos-app/test-b02-realtime-pwa.mjs

# 4. Chạy kiểm chứng probe cho failure ordering & retry (PASS 100%)
node src/frontend/pos-app/test-probe-v15.cjs

# 5. Chạy offline preflight & audit harness 8 kịch bản cho F06 (PASS 100%)
node src/frontend/pos-app/test-b02-hub-preflight.cjs

# 6. Kiểm tra kết nối SignalR Hub WebSocket thật & Demo Guard trên Live Demo 5000 (Non-mutating: PASS 100%)
node --test src/frontend/pos-app/test-b02-hub-transport.mjs

# 7. Chạy Live Transport Disruption & Event Recovery Workflow trên Isolated Test Backend 5001 (PASS 100%)
$env:POS_LIVE_TEST_ENV = '1'
$env:POS_TEST_BACKEND_URL = 'http://localhost:5001'
node --test src/frontend/pos-app/test-b02-hub-transport.mjs
```

### Danh mục Bằng chứng v1.7 (`evidence/v1.7/`):
- `build.log`: Log build production thành công 0 lỗi (876ms).
- `baseline-34-tests.log`: Log 34/34 bài test baseline PASS (2.4s).
- `b02-realtime-pwa-24-tests.log`: Log 24/24 bài test B02 PASS (1.8s).
- `probe-v15-verification.log`: Log 6 probe kiểm chứng cơ chế failure ordering và retry thành công PASS 100%.
- `preflight-runner.log`: Log offline preflight & audit harness 8 kịch bản: full workflow, demo/isolation guards, 5 defect catch probes PASS 100%.
- `preflight-runner-results.json`: Chi tiết cấu trúc kết quả preflight runner 8/8 kịch bản PASS.
- `b02-hub-transport-default.log`: Log kết nối Hub WebSocket thật, cách ly Shop B và guard an toàn môi trường trên port 5000 (Non-mutating).
- `b02-hub-transport-live.log`: Log toàn bộ live workflow: nhận OrderCreated & StockLevelChanged, ngắt transport vật lý thật qua proxy, offline checkout mutation, auto-reconnect, rejoin thành công, GET refetch tồn và khôi phục cache không stale trên Isolated Test Backend 5001 (PASS).
- `browser-search-results.json`: Kết quả đo đạc trên Edge Chromium sản xuất (cart numeric input max = 8).
- `browser-search-fixed-8.png`: Ảnh chụp quầy thu ngân Edge production sau khi search tồn 8 cập nhật giỏ hàng.
- `latency-benchmark.json`: Báo cáo độ trễ p95 chi tiết (16.653ms).
- `working-tree-hashes.json`: Bảng băm SHA-256 chính xác 21 tệp tin trong working tree.
- `01-desktop-online-search-stock10.png`: Ảnh chụp quầy thu ngân với tồn khả dụng ban đầu = 10.
- `02-desktop-stock-refreshed-8.png`: Ảnh chụp quầy thu ngân tự động cập nhật tồn = 8 sau khi mua 2 không cần F5.
- `03-desktop-offline-pwa-shell.png`: Ảnh chụp PWA tải lại từ Service Worker trong chế độ hoàn toàn Offline.
- `04-touch-viewport-tablet.png`: Ảnh chụp giao diện quầy thu ngân chế độ cảm ứng máy tính bảng.
