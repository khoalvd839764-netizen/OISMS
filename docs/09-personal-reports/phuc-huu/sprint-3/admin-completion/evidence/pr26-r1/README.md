# Evidence PR26 review R1 — 28/10/2026

[Review](../../reviews/pr26-review-r1.md), [plan sau POS](../../PLAN.md). Kết luận CHANGES_REQUESTED, không phải nghiệm thu full-stack. Không sửa source sản phẩm, checkout branch, commit/push/merge hoặc ghi dữ liệu backend thật.

## Snapshot và GitHub

- [Metadata](./metadata.json): PR26 head `403431446d213bbbf7f27d01b9622672f2fa4653`, branch `feature/OISMS-43-orders-transfers`, base main, OPEN/unmerged/mergeable lúc kiểm tra. Hai checks completed/success, URL chi tiết ở metadata.
- [Compare develop](./compare-develop.json), [28 file feature](./changed-files-develop.json): develop `ecc0c85c7d6f81841d8138ff61ae40d33ef013d2`, ahead 3, behind 0. Diff main 284 files gồm nhiều thay đổi kế thừa; review code Admin dựa compare develop và backend contract cùng head.
- Tải metadata/check-runs/compare qua GitHub REST API read-only; tải ZIP từ codeload tại head trên, giải nén vào `D:/OISMS/tmp/pr26-review/snapshot/OISMS-403431446d213bbbf7f27d01b9622672f2fa4653`. Không dùng git checkout vì working tree POS đang có thay đổi cần giữ.
- [SHA256 source](./source-hashes.json), [so sánh sau kiểm tra](./hash-comparison.json): 28 file feature không đổi sau npm ci/build/probes. Không claim là tất cả file backend đã được test lại.

## Build

Tại `D:/OISMS/tmp/pr26-review/snapshot/OISMS-403431446d213bbbf7f27d01b9622672f2fa4653/src/frontend/admin-dashboard`:

```powershell
npm.cmd ci --ignore-scripts --no-audit --no-fund
npm.cmd run build
```

[npm-ci.log](./npm-ci.log): 91 package, PASS. [build.log](./build.log): TypeScript + Vite production PASS, JS bundle 686.27kB, có warning chunk lớn; không coi warning là build fail. CI frontend chỉ build Admin/POS, không tự chứng minh luồng kiểm kê/chuyển kho đúng.

## Reviewer probes

Từ `D:/OISMS`:

```powershell
node tmp/pr26-review/reviewer-probes.cjs
node tmp/pr26-review/browser-count.cjs
```

Script sao lưu tại [reviewer-probes.cjs](./reviewer-probes.cjs), [browser-count.cjs](./browser-count.cjs). Probe dùng đường dẫn snapshot nêu trên; chạy bản sao từ repo root vẫn đọc cùng source, output mới nằm cạnh script. Chuẩn bị đúng ZIP + npm ci/build trước khi tái chạy trên máy khác; browser runner cần Playwright bundled và Edge theo đường dẫn máy được ghi trong script.

- [Node results](./probe-results.json), [log](./probes.log): actual TS/TSX transpiled bằng TypeScript trong snapshot, controlled HTTP/storage/hooks/fake SignalR. 5 observations: transfer reject409 vẫn fake success; create count cùng payload đổi key; start A/token B vẫn connection auth A và event A được nhận; nhập count7 nhưng confirm chỉ gửi metadata, chưa record; so sánh ranh giới ngày local+07 với UTCZ trong source report.
- **Node exit 0 là counterexamples được assert đúng, không phải feature PASS.** Observation key là proof identity đổi qua hai call, chưa phải đo số chứng từ trùng trong DB. So sánh timezone là phép tính/source-risk cần đối chiếu policy ở A05, không gán thành finding đã đo báo cáo DB.
- [Browser results/request trace](./browser-count-results.json), [log](./browser-count.log), [screenshot](./browser-count.png): actual production React trong installed Edge headless, browser context tạm, REST fixture và Hub offline. Fixture persisted count10, nhập UI7, bấm Chốt; POST duy nhất /confirm body{}, không /record. UI chốt theo số fixture10, 0 pageErrors. Không backend/ledger/DB thật nào bị thay đổi.
- Lần chạy browser đầu [first-attempt.log](./browser-count-first-attempt.log) timeout vì locator text chọn option ẩn thay vì trạng thái hiển thị. Đã sửa **chỉ locator probe**, chờ nút Chốt ẩn sau success, chạy lại thành công; không sửa sản phẩm để làm xanh test. Browser/server đã đóng sau mỗi run.

Chưa chạy live backend/DB/Hub transport, automated transaction E2E, thiết bị vật lý hoặc test-auth.ps1. Các gate này có trong plan thực hiện sau POS; không ghi NOT RUN thành PASS.
