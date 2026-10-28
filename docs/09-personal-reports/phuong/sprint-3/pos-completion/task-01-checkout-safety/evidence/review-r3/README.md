# Evidence R3 — T01 v1.2

Review ngày 27/10/2026, Asia/Saigon: [R3](../../reviews/v1.2-review-r3.md).

- HEAD `44317f3b5188c276fa3ef25c8faf30323e503079` cộng working tree v1.2. Bảy file source/test theo `source-hashes-start.json` và `source-hashes-end.json` khớp.
- `build.log`: PASS; `tests.log`: 24/24 PASS, không skip/cancel/fail.
- `reviewer-probes.cjs` và `harness.cjs` được copy từ evidence bàn giao v1.2, chạy lại độc lập trên source hiện tại, không sửa evidence của Anti: chín quan sát/control đã đạt, kết quả trong `probe-results.json`/`probes.log`.
- `recovery-probes.cjs` do reviewer bổ sung: gọi handler thật của App, LoginModal, PaymentModal và API, có hai counterexample trong `recovery-results.json`/`recovery-probes.log`. Không tự gán state/cart để giả kết quả callback. Assertion trong probe xác nhận trạng thái lỗi quan sát được; exit 0 không có nghĩa AC đạt.
- Node VM dùng hooks, storage, sự kiện và fetch stub. Không phải trình duyệt hoặc HTTP/PostgreSQL thật; token/password là fixture. Không có bằng chứng DB tạo đơn trùng ở hai ca recovery.

Từ repo root `D:\OISMS` (dependencies POS đã có):

```powershell
node docs/09-personal-reports/phuong/sprint-3/pos-completion/task-01-checkout-safety/evidence/review-r3/reviewer-probes.cjs
node docs/09-personal-reports/phuong/sprint-3/pos-completion/task-01-checkout-safety/evidence/review-r3/recovery-probes.cjs
```

Build/test chạy ở `src/frontend/pos-app`: `npm run build`, `npm test`. Không sửa source/test sản phẩm, commit/push hoặc merge trong review.
