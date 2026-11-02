# Evidence review R2 — T01 v1.1

Ngày 27/10/2026, Asia/Saigon. Review [v1.1 R2](../../reviews/v1.1-review-r2.md).

- Baseline HEAD `44317f3b5188c276fa3ef25c8faf30323e503079` cộng working tree v1.1. `source-hashes-start.json`/`source-hashes-end.json` khớp cho sáu file source/test theo dõi; không nhận HEAD một mình là phiên bản code đã test.
- `build.log`: build PASS; `tests.log`: 24/24 PASS, không fail/skip/cancel.
- `reviewer-probes.cjs` dùng `harness.cjs` để thực thi source TS/TSX production; `probe-results.json`/`probes.log` có chín quan sát. Năm control đạt và bốn quan sát tái hiện F01. Exit 0 của probe nghĩa các assertion kiểm chứng quan sát đã chạy xong, không có nghĩa tính năng đạt AC.
- React hooks, storage, event và fetch được stub. Request/body/token dùng fixture; không có token thật. Không chạy browser, HTTP backend hoặc PostgreSQL; `modeledOrders` chỉ là số đơn của mô hình.

Lệnh tái hiện từ root `D:\OISMS` (cần dependencies POS đã có):

```powershell
node docs/09-personal-reports/phuong/sprint-3/pos-completion/task-01-checkout-safety/evidence/review-r2/reviewer-probes.cjs
```

Build và suite chạy từ `D:\OISMS\src\frontend\pos-app`: `npm run build`, `npm test`. Không sửa test/source sản phẩm trong review. Source hiện tại thay đổi thì kết quả cần được xác minh lại.
