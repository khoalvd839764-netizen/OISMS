# Evidence — B02 v1.2 review R3

Ngày 28/10/2026. [Review R3](../../reviews/v1.2-review-r3.md): CHANGES_REQUESTED, F01/F02/F03/F05 CLOSED; F04/F06 OPEN. Branch `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`, HEAD `44317f3b5188c276fa3ef25c8faf30323e503079` cộng working tree. Không sửa source/test sản phẩm, không commit/push/merge hoặc backend/DB writes.

[Handover đã review](./handover-reviewed.md) là bản sao nguyên trạng; link tương đối bên trong vẫn hiểu từ root batch B02 (thư mục [v1.2](../v1.2/)). [31 hash đầu](./source-hashes-start.json), [cuối](./source-hashes-end.json), [so sánh](./hash-comparison.json): không đổi; [27 hash Anti báo cáo](./reported-hash-comparison.json) khớp. [Delta từ R2](./source-delta-r2.json) có7 file frontend/helper/test đổi.

## Build và test hiện có

Tại `D:/OISMS/src/frontend/pos-app`:

```powershell
npm.cmd run build
node --test test-pos-e2e.mjs
```

[Build](./build.log) PASS, [baseline](./baseline-tests.log)34/34 PASS. B02 chạy **tại repo root `D:/OISMS`**:

```powershell
node --test src/frontend/pos-app/test-b02-realtime-pwa.mjs
```

[Root log](./b02-tests-root.log):17/17 PASS,0 skip. Có output fetch/negotiation failures từ fixture/offline suite, không phải live Hub evidence. [Lần cwd pos-app](./b02-tests.log) FAIL vì harness tại evidence/review-r2 dùng root process.cwd, resolve sai đường typescript. Lượt root theo lệnh handover pass; hai log lưu đúng hoàn cảnh, không che failure CWD hoặc gọi toàn feature pass.

## Probes reviewer

Từ repo root:

```powershell
node tmp/pos-b02-review-r3/reviewer-probes.cjs
node tmp/pos-b02-review-r3/benchmark-probes.cjs
node tmp/pos-b02-review-r3/browser-stock.cjs
```

Bản sao [reviewer-probes](./reviewer-probes.cjs), [harness](./harness.cjs), [benchmark-probes](./benchmark-probes.cjs), [browser-stock](./browser-stock.cjs) cũng chạy từ repo root và xuất kết quả cạnh script. Browser cần production dist đã build, bundled Playwright/Edge theo đường dẫn trong script. Profile riêng cạnh script, không đưa edge-profile vào git/evidence. Browser/server đã đóng sau kiểm tra.

- [Runtime results](./probe-results.json), [log](./probes.log): actual TS/TSX/components/callback production, hook/fetch/storage/timer/fake transport.4 ca PASS (balance ID-contract với DTO object đúng, UUID helper alias race, reconnect GET, query clear).2 counterexamples8→10 (App code-as-ID và actual barcode trực tiếp ghi ngoài guard). Không dùng helper `_simulateRealtimeEvent` để chứng minh production handler.
- [Benchmark results](./benchmark-results.json), [log](./benchmark-probes.log): actual runner trong VM.404 throw/no output — đã sửa;200 sai identity/sku và available string vẫn xuất PASS — còn lỗi. Fs stub giữ output trong RAM; các đường v1.2/v1.1 trong results chỉ là vị trí runner dự định ghi, reviewer không ghi đè chúng. Đồng hồ fixture kiểm tra validator, không phải đo hiệu năng.
- [Browser results](./browser-stock-results.json), [log](./browser-stock.log), [screenshot](./browser-stock-stale.png): installed Edge thật/production dist, REST fixture, Hub abort/offline. Checkout2 từ10→API8, barcode8, numeric max8;0 pageErrors. Tên PNG kế thừa, kết quả happy path8 đã đạt.

Exit0 của reviewer runtime nghĩa là assert đúng cả ca sửa PASS lẫn lỗi dự kiến; không phải all-feature PASS. Không chạy live Hub runner trên localhost chưa xác định test DB; không chạy lại C# InMemory4 tests chỉ để thay bằng chứng frontend; không kiểm chứng transaction/PostgreSQL/hardware trong lượt này. Các giới hạn này không xóa kết quả helper/lookup đã đạt.
