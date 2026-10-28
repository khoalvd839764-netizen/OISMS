# Evidence R3 — B01 v1.2

Ngày 27/10/2026, Asia/Saigon. [Review VERIFIED v1.2](../../reviews/v1.2-review-r3.md).

- HEAD `44317f3b5188c276fa3ef25c8faf30323e503079` cộng working tree v1.2. Hash 11 input files tại `source-hashes-start.json`/`source-hashes-end.json` khớp. So R2 chỉ API/test suite đổi; đầu vào in không đổi.
- `build.log`: PASS; `tests.log`: 34/34 PASS, 0 fail/skip/cancel. `git diff --check` PASS.
- `receipt-probes.cjs` + `harness.cjs`: actual validator/API/mapper/PaymentModal TS/TSX under Node VM với hooks/storage/fetch stub. `receipt-results.json`/log: 11 ca có assertion đạt, gồm 201, 200 replay sau mất response, hai clock client 2099/2100, các timestamp missing/null/empty/whitespace/wrong type/invalid và controls item/metadata/mapper exception.
- Cả request key lẫn toàn bộ payload được so khớp khi retry. Control mapper-throws có fault injection có chủ đích, các ca còn lại chạy mapper production. Số ca này là runtime frontend với stub, không phải browser/full-stack/DB.
- Không tạo lại PDF hoặc chạy lại browser suite vì component/CSS/generator in không đổi từ [evidence R2](../review-r2/README.md). Không sao chép kết quả browser cũ thành một lần chạy mới.
- Không chỉnh source/test sản phẩm trong review; chỉ lưu review/evidence/trạng thái. Không commit/push/merge hoặc dùng dữ liệu/tài khoản thật cho probe.

Chạy từ repo root `D:\OISMS`, dependencies POS đã có:

```powershell
node docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-01-receipt-print/evidence/review-r3/receipt-probes.cjs
```

Nên copy script/harness sang tmp trước chạy lại để không ghi đè archive. Build/test từ `src/frontend/pos-app`: `npm run build`, `npm test`.
