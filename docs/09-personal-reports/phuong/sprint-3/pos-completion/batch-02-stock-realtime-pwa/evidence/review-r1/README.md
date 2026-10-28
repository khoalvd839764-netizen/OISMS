# Evidence — B02 v1.0 review R1

Ngày 28/10/2026. [Kết luận review](../../reviews/v1.0-review-r1.md): CHANGES_REQUESTED. Source tại branch `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`, HEAD `44317f3b5188c276fa3ef25c8faf30323e503079` cộng working tree. [Hash đầu](./source-hashes-start.json), [hash cuối](./source-hashes-end.json), [so sánh](./hash-comparison.json): 30 file không đổi.

## Các lệnh đã chạy

Từ `D:\OISMS\src\frontend\pos-app`:

```powershell
npm run build
node --test test-pos-e2e.mjs
node --test test-b02-realtime-pwa.mjs
```

Lưu tại [build.log](./build.log), [baseline-tests.log](./baseline-tests.log), [b02-tests.log](./b02-tests.log). PASS lần lượt build, 34 tests và 9 tests; không dùng kết quả này để đóng counterexample reviewer.

Từ `D:\OISMS`:

```powershell
node tmp/pos-b02-review-r1/reviewer-probes.cjs
node tmp/pos-b02-review-r1/browser-stock.cjs
```

Probe gốc và bản sao tại [reviewer-probes.cjs](./reviewer-probes.cjs), [browser-stock.cjs](./browser-stock.cjs). Bản sao cũng chạy từ repo root được; các output mới xuất cạnh script. Script browser cần production dist đã build, Playwright bundled và Edge theo đường dẫn máy này; dùng profile riêng cạnh script. Không dùng profile browser cá nhân. Không đưa profile `edge-profile` vào evidence/git.

## Provider và kết quả

- [Node observations](./probe-results.json), [console](./probes.log): 5 counterexamples chạy actual TS/TSX của api/realtimeService/stockService/ProductSearch, với storage, dependency-aware React hook harness, fetch/timer và fake SignalR transport điều khiển. Dùng callback production được đăng ký, không gọi helper giả lập event. **Exit 0 nghĩa là assert các lỗi đã tái hiện, không phải feature PASS.** Đủ chứng minh code path/race, không thay thế React browser/Hub/PostgreSQL thật.
- [Browser observations/request trace](./browser-stock-results.json), [console](./browser-stock.log), [screenshot](./browser-stock-stale.png): installed Edge thật, headless production dist qua ephemeral static server; REST fixture, Hub abort/offline. POST fixture chuyển tồn từ 10 sang 8, GET refresh và GET barcode đều trả 8, giỏ vẫn `max=10`. Screenshot cho ngữ cảnh UI; giá trị max và request trace nằm trong JSON. Không có pageErrors. Không phát sinh đơn trên backend/DB thật.
- Browser đã đóng và server đã dừng sau kiểm tra. Profile riêng không được sao chép vào thư mục evidence.
- Live Hub event/reconnect và latency backend thực **chưa được reviewer chạy/chứng minh**. Finding F06 dựa trên code runner và đối chiếu claim, không gán provider thật cho mô phỏng hoặc benchmark RAM.
