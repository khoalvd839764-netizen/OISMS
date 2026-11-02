# Evidence — B02 v1.3 review R4

Ngày 28/10/2026. [Review R4](../../reviews/v1.3-review-r4.md): CHANGES_REQUESTED v1.3, F04 OPEN; F06 benchmark đã đạt kiểm tra validator, live workflow OPEN/NOT RUN/BLOCKED. F01/F02/F03/F05 giữ CLOSED. Branch `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`, HEAD `44317f3b5188c276fa3ef25c8faf30323e503079` cộng working tree.

[Handover nguyên trạng](./handover-reviewed.md) giữ claim v1.3, link tương đối bên trong hiểu từ root B02, tương ứng [v1.3](../v1.3/). [Source delta9 file so R3](./source-delta-r3.json), [hash đầu](./source-hashes-start.json), [cuối](./source-hashes-end.json), [comparison](./hash-comparison.json):31 input không đổi trong lượt,27 reported hashes match. Không sửa product source/test, commit/push/merge hoặc backend/DB.

## Build và suites

```powershell
# D:/OISMS/src/frontend/pos-app
npm.cmd run build
# D:/OISMS
node --test src/frontend/pos-app/test-pos-e2e.mjs
node --test src/frontend/pos-app/test-b02-realtime-pwa.mjs
```

[Build](./build.log) PASS, [baseline](./baseline-tests.log)34/34 PASS, [B02](./b02-tests.log)19/19 PASS,0 skip. Những console fetch failures trong VM/offline tests không phải bằng chứng kết nối live; suites chưa bắt hai counterexamples F04 mới tái hiện.

## Reviewer runtime và benchmark

Từ repo root:

```powershell
node tmp/pos-b02-review-r4/reviewer-probes.cjs
node tmp/pos-b02-review-r4/benchmark-probes.cjs
```

[Runtime script](./reviewer-probes.cjs) cần [harness](./harness.cjs) cùng thư mục. [Results](./probe-results.json), [log](./probes.log): actual TS/TSX/callbacks/components, dependency-aware hooks, controlled HTTP/timers/storage/fake SignalR.6 ca đã đạt PASS,2 lỗi F04 tái hiện: older barcode sau newerGET8 ghi10; fresh search8 với cache10 thêm hàng10. Không dùng _simulateRealtimeEvent cho production handler. Exit0 nghĩa assert đúng cả PASS lẫn counterexample, không all-feature PASS.

[Benchmark script](./benchmark-probes.cjs), [results](./benchmark-results.json), [log](./benchmark-probes.log): nguyên benchmark source trong VM, fs chỉ lưu RAM, fixed clock và fixture fetch.5 ca payload lỗi reject, available0 PASS và duy nhất outputv1.3. Không gọi backend thật/ghi đè evidence/đo latency live; raw16.653ms là artifact của Anti, không phải số đo probe.

## Browser production

Từ repo root sau build:

```powershell
node tmp/pos-b02-review-r4/browser-search.cjs
node tmp/pos-b02-review-r4/browser-stock.cjs
```

Bản sao scripts ở đây chạy từ repo root được, xuất output cạnh script; dùng bundled Playwright/installed Edge theo đường dẫn trong script. Profile riêng edge-profile không được sao chép vào evidence/git. Browser/server đã đóng sau kiểm tra.

- [Search script](./browser-search.cjs), [results](./browser-search-results.json), [log](./browser-search.log), [screenshot](./browser-search-stale.png): actual production React/dist, REST fixture, Hub offline. GET search10 rồi server thay đổi8, GET search mới8 đã hoàn tất, chọn đúng dropdown; numeric max vẫn10.0 pageErrors, không POST/DB writes. Đây là evidence F04 search regression.
- [Lần thử đầu](./browser-search-first-attempt.log) probe chọn text Product A cũng có trong cart nên không chờ search mới, assertion có GET8 fail. Sửa riêng locator sang SKU ở dropdown và đợi response search, chạy lại mới xác nhận GET8/max10. Không dùng lỗi locator làm finding sản phẩm.
- [Checkout/barcode script](./browser-stock.cjs), [results](./browser-stock-results.json), [log](./browser-stock.log), [screenshot](./browser-stock-stale.png): Hub offline, fixture POST201 đổi10→8, GET refresh8/barcode8, numeric max8. Happy path vẫn PASS; tên PNG kế thừa, nội dung này đã đạt8.

Không chạy live Hub mutation/reconnect workflow trên demo DB, PostgreSQL transaction E2E hoặc hardware. Test endpoint/SDK connection smoke không được reviewer cộng thành live event/reconnect PASS. Source/query/alias checks theo AC hiện có, không audit thêm scope ngoài B02.
