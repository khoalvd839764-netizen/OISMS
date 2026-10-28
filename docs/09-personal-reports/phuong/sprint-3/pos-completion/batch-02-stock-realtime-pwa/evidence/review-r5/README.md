# Evidence — B02 v1.4 review R5

Ngày 28/10/2026, Asia/Saigon. [Review](../../reviews/v1.4-review-r5.md) CHANGES_REQUESTED v1.4, F04/F06 OPEN; F01/F02/F03/F05 giữ CLOSED. HEAD `a20ebe9` + working tree v1.4, 18/18 reported hashes match trong [results](./probe-results.json).

Từ root `D:/OISMS`, reviewer đã chạy:

```powershell
npm --prefix src/frontend/pos-app run build
node --test src/frontend/pos-app/test-pos-e2e.mjs
node --test src/frontend/pos-app/test-b02-realtime-pwa.mjs
node docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-02-stock-realtime-pwa/evidence/review-r5/reviewer-probes.cjs
node docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-02-stock-realtime-pwa/evidence/review-r5/live-runner-probe.cjs
node docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-02-stock-realtime-pwa/evidence/review-r5/browser-search.cjs
```

- Build PASS, baseline34/34, B02 suite21/21 PASS,0 skip. Console network warnings trong offline tests không phải evidence live.
- [Runtime probes](./reviewer-probes.cjs) dùng harness R4 nguyên trạng, đọc actual TS/TSX trên đĩa, controlled HTTP/hooks/fake SDK. [Results](./probe-results.json): ba ca success race PASS, hai ca newer refresh error bị older completion xóa stale/error tái hiện. Seed cả UUID và SKU-code aliases10.
- [Runner contract probe](./live-runner-probe.cjs) chạy source runner và actual production loaders, fixture health/login5000/5001 và fake SDK, không network thật. Chỉ đổi tên binding ESM require lúc transpile để tránh shadow CJS import loader, không đổi hành vi test. [Results](./live-runner-results.json): TypeError subscribeOrder, test loader vẫn inject demo token/base. Lần dựng harness đầu lỗi require temporal-dead-zone do transpile; đã sửa riêng harness trước chạy probe, không coi đó là lỗi sản phẩm.
- [Browser](./browser-search.cjs) lấy script R4, giữ fixture/thao tác và đổi assertion expected max10→8, screenshot filename mới. Real production dist/Edge, REST fixture và Hub offline, không POST hay DB. [Results](./browser-search-results.json): GET10→GET8/max8,0 pageErrors; [screenshot](./browser-search-fixed.png).
- Probe exit0 nghĩa assert được cả PASS lẫn trạng thái sai được tái hiện, không phải all-feature PASS. Giữ R5 script/history; acceptance mới cần assert đúng trạng thái stale và chạy FAIL→PASS sau fix.
- Không chạy live mutation vào demo hoặc gọi stop/start đó là automatic recovery. Không sửa source/test sản phẩm hoặc commit/push/merge. Browser profile bị .gitignore bỏ qua.
