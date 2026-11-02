# Evidence review R1 — B01 v1.0

Ngày 27/10/2026, Asia/Saigon. [Review](../../reviews/v1.0-review-r1.md).

- HEAD `44317f3b5188c276fa3ef25c8faf30323e503079` cộng working tree B01 v1.0. Hash 11 file source/test/script đầu/cuối khớp (`source-hashes-*.json`); không dùng HEAD một mình làm phiên bản code đã test.
- `build.log`: PASS. `tests.log`: 31/31 PASS, không fail/skip/cancel.
- `git diff --check` báo trailing whitespace tại `src/frontend/pos-app/src/services/api.ts:23` trong source bàn giao; reviewer không sửa source. Đây là ghi chú định dạng không chặn, không phải finding nghiệp vụ thứ tư. Probe syntax và hash source được kiểm tra riêng.
- `receipt-probes.cjs` + `harness.cjs`: actual API/mapper/PaymentModal transpile dưới Node VM với hook/storage/fetch stub. `receipt-results.json`/log có một positive server-snapshot control và hai counterexample. Exit 0 nghĩa các quan sát đã được assert, không có nghĩa feature đạt AC. Không chạy HTTP backend/PostgreSQL, không chứng minh đơn DB trùng.
- `browser-print.cjs`: actual ReceiptPrintModal render bằng ReactDOMServer, kèm nguyên source index.css, vào Edge headless qua bundled Playwright, emulate print và printToPDF. Không dùng template hóa đơn khác. Không load Tailwind CDN nên phép kiểm tập trung vào ancestor display:none và page-size CSSOM, không claim nghiệm thu toàn bộ layout/interaction app. `browser-results.json` có bốn case đều bị ẩn lúc print, CSS size bị bỏ qua.
- `actual-print-*.pdf`: sản phẩm kiểm thử reviewer, cả bốn trắng, 0 text chars theo `actual-pdf-analysis.json`; `actual-58mm-blank.png` đã được xem. `production-component-*.html` lưu markup SSR/CSS đã dùng.
- `submitted-pdf-analysis.json`: đọc cả bốn PDF bàn giao v1.0, mỗi trang Letter 215.9 × 279.4mm. `submitted-58mm.png` render và xem để đối chứng. Không sửa/copy đè PDF bàn giao.
- Edge launch trong sandbox thất bại do profile/temp; lần chạy thành công dùng profile thử nghiệm riêng `tmp/pos-b01-review-r1/edge-profile`, không dùng profile cá nhân. Profile không phải bằng chứng, không được copy vào tài liệu. Không sửa code sản phẩm hoặc commit/push/merge.

Từ root `D:\OISMS`:

```powershell
node docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-01-receipt-print/evidence/review-r1/receipt-probes.cjs
node docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-01-receipt-print/evidence/review-r1/browser-print.cjs
```

Browser script cần Edge tại đường dẫn đã ghi và bundled Playwright. Với review archive, nên copy script/harness vào `tmp/` trước chạy để không ghi đè bằng chứng cũ hoặc sinh browser profile trong docs. PDF analysis dùng pypdf, render PNG dùng Poppler pdftoppm; các file này là bằng chứng lỗi, không phải PDF giao cho người dùng sử dụng.
