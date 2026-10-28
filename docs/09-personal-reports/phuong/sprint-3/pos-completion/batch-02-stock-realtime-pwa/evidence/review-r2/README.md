# Evidence — B02 v1.1 review R2

Ngày 28/10/2026. [Review R2](../../reviews/v1.1-review-r2.md): CHANGES_REQUESTED, F01–F03 CLOSED, F04–F06 OPEN. Branch `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`, HEAD `44317f3b5188c276fa3ef25c8faf30323e503079` cộng working tree. [Handover được review](./handover-reviewed.md) giữ nguyên claim v1.1 để truy vết; reviewer không sửa handover gốc.

Bản handover sao lưu giữ nguyên cả link tương đối ở vị trí gốc; các link `./evidence/v1.1/...` trong bản sao được hiểu từ root `batch-02-stock-realtime-pwa`, tương ứng thư mục [evidence v1.1](../v1.1/). Không thay nội dung lịch sử chỉ để đổi base link.

[Hash đầu](./source-hashes-start.json), [cuối](./source-hashes-end.json), [so sánh](./hash-comparison.json): 31 input source/config/test không đổi trong lượt kiểm tra. [27 hash Anti báo cáo](./reported-hash-comparison.json) đều khớp actual. CodeGraph đã dùng, backend PosCatalogController không nằm trong index nên đọc source để xác minh predicate.

## Build và suite hiện có

Tại `D:/OISMS/src/frontend/pos-app`:

```powershell
npm.cmd run build
node --test test-pos-e2e.mjs
node --test test-b02-realtime-pwa.mjs
```

[build.log](./build.log): PASS. [baseline-tests.log](./baseline-tests.log): 34/34 PASS. [b02-tests.log](./b02-tests.log): 14/14 PASS. Đều 0 skip; console trong B02 có expected fetch/negotiation failures trên test giả/offline, không phải evidence Hub thật đã chạy. Suite pass nhưng không bắt các counterexample reviewer; không gọi build hoặc mock VM là full-stack PASS.

## Reviewer probes

Từ repo root `D:/OISMS`:

```powershell
node tmp/pos-b02-review-r2/reviewer-probes.cjs
node tmp/pos-b02-review-r2/browser-stock.cjs
node tmp/pos-b02-review-r2/benchmark-status-probe.cjs
```

Bản sao các script tại đây cũng chạy từ repo root được và xuất results cạnh script; reviewer-probes cần [harness.cjs](./harness.cjs) cùng thư mục. Browser cần build dist trước, bundled Playwright/Edge theo đường dẫn máy trong script. Dùng profile riêng `edge-profile` cạnh script; không dùng profile cá nhân hoặc đưa profile vào git/evidence.

- [reviewer-probes.cjs](./reviewer-probes.cjs), [results](./probe-results.json), [log](./probes.log): actual TS/TSX với dependency-aware hooks, fetch/storage/timer/fake SignalR transport. F01 deferred-start/owner, F02 shared store/fresh barcode, F03 exact absent, F05 branch switch đã PASS; 4 counterexamples F04 UUID lookup/alias race/reconnect và F05 clear query vẫn tái hiện. Handler production được đăng ký thật lên fake transport; không dùng `_simulateRealtimeEvent` để chứng minh production handler. Fixture UUID lookup phản ánh predicate textual đã đọc ở backend, không tự nhận đó là live HTTP/backend test.
- Lần probe đầu [log](./probes-first-attempt.log) assertion query-clear không tìm thấy row vì probe lọc button trong khi source render row là div. Đã sửa predicate probe nhận phần tử có callback handleSelectProduct, không sửa sản phẩm; lần sau tái hiện 1 row cũ mở lại với input rỗng. Không dùng lần timeout/assert fail đầu làm bằng chứng sản phẩm.
- [browser-stock.cjs](./browser-stock.cjs), [results](./browser-stock-results.json), [log](./browser-stock.log), [screenshot](./browser-stock-stale.png): production React/dist trong installed Edge headless, REST fixture, Hub offline. Checkout2 từ10, GET8, barcodeGET8, numeric max8, 0 pageErrors. Tên PNG kế thừa R1; **nội dung kết quả R2 đã đạt 8**. Browser/server đã đóng sau kiểm tra. Không backend/DB writes.
- [benchmark-status-probe.cjs](./benchmark-status-probe.cjs), [results](./benchmark-status-results.json), [log](./benchmark-status.log): chạy chính benchmark source trong VM, login200 rồi warmup+50 barcode đều404. Runner vẫn status PASS. Stub fs giữ output trong RAM, không ghi đè latency-benchmark.json của Anti hoặc gọi backend thật. Timing trong probe dùng đồng hồ fixture để kiểm tra phân loại, không phải benchmark hiệu năng.

**Exit0 của reviewer-probes nghĩa là cả các ca sửa PASS lẫn các lỗi dự kiến đều được assert đúng; không phải toàn feature PASS.** Chưa chạy live Hub transport/event/reconnect, backend/PostgreSQL transaction E2E hoặc phần cứng. Không cần thêm permission/phần cứng chỉ để nghiệm thu những counterexample có provider rõ ở đây.
