# Evidence review R2 — B01 v1.1

Ngày 27/10/2026, Asia/Saigon. [Review](../../reviews/v1.1-review-r2.md).

- HEAD `44317f3b5188c276fa3ef25c8faf30323e503079` cộng working tree v1.1, 11 file trong `source-hashes-start.json`/`source-hashes-end.json` khớp.
- `build.log`: PASS; `tests.log`: 34/34 PASS, 0 fail/skip/cancel.
- `git diff --check` còn ba trailing whitespace trong source bàn giao (`api.ts:109`, `test-pos-e2e.mjs:2912,2993`); ghi chú định dạng không chặn, không tạo finding thứ tư. Reviewer không sửa source; probe syntax/hash được kiểm tra riêng.
- `receipt-probes.cjs` + `harness.cjs`: production API/validator/mapper/PaymentModal chạy dưới Node VM với hooks/storage/fetch stub. Bảy quan sát trong `receipt-results.json`/log: năm control đạt, hai counterexample missing/null timestamp. Control `mapper-throws` inject exception để kiểm catch sau API validation. Exit 0 xác nhận quan sát theo assertion, không chứng minh toàn feature đạt.
- `browser-print.cjs`: Edge headless + bundled Playwright, production ReceiptPrintModal SSR và nguyên source index.css, bốn ca official/draft × 58/80mm. `browser-results.json` có rect >0, ancestor block, page-size hỗ trợ. SSR không load đầy đủ Tailwind CDN, vì vậy probe tập trung vào lỗi tổ tiên/CSS page size, không claim browser app lifecycle hoặc UX đầy đủ.
- `actual-pdf-analysis.json`: bốn PDF reviewer có text, trang rộng khoảng 57.83/80.09mm và cao 200.07mm. Đây là artifacts kiểm thử, không PDF sản phẩm giao dùng.
- `submitted-pdf-analysis.json`: đọc bốn PDF bàn giao v1.1, gồm text và MediaBox. `submitted-58mm.png`/`submitted-80mm.png` đã được xem để kiểm tra bản official; không sửa PDF bàn giao. Generator v1.1 render component thật với index.css và một tập utilities SSR hỗ trợ.
- Profile Edge thử nghiệm ở `tmp/pos-b01-review-r2/edge-profile`, không dùng profile cá nhân, không lưu vào evidence. Không chạy HTTP backend/PostgreSQL hoặc máy in vật lý; không sửa source/test sản phẩm, commit/push/merge.

Tái hiện từ repo root, nên copy script/harness vào tmp trước chạy để không ghi đè archive hoặc sinh browser profile trong docs:

```powershell
node tmp/pos-b01-review-r2/receipt-probes.cjs
node tmp/pos-b01-review-r2/browser-print.cjs
```

Browser script cần Edge tại đường dẫn trong script và Playwright bundled; PDF analysis dùng pypdf, render dùng pdftoppm. Không cần coi missing full-stack E2E là finding mới; gate đó đã ở B03/T06.
