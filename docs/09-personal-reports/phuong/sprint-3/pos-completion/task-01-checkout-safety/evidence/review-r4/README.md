# Evidence R4 — T01 v1.3

Review ngày 27/10/2026, Asia/Saigon: [VERIFIED T01 v1.3](../../reviews/v1.3-review-r4.md).

- HEAD `44317f3b5188c276fa3ef25c8faf30323e503079` cộng working tree v1.3. `source-hashes-start.json`/`source-hashes-end.json` khớp cho bảy file source/test được review.
- `build.log`: PASS. `tests.log`: 25/25 PASS, 0 fail/skip/cancel.
- `reviewer-probes.cjs`/`harness.cjs` copy từ evidence v1.3, chạy lại trên source hiện tại ở thư mục riêng: chín control/quan sát PASS; `probe-results.json`/`probes.log`.
- `recovery-probes.cjs` copy từ v1.3, reviewer bổ sung assertion toàn bộ request payload khớp snapshot và record owner A còn nguyên. Hai ca recovery PASS, không chỉ so key/branch; kết quả tại `recovery-results.json`/`recovery-probes.log`.
- Provider: production TS/TSX transpile trong Node VM; React hooks, storage, events và fetch stub. Không phải browser/HTTP/PostgreSQL E2E. `modeledOrders` chỉ là số đơn mô hình, token/password đều fixture. Chưa kiểm chứng CI của working tree.

Từ root `D:\OISMS`, dependencies POS đã có:

```powershell
node docs/09-personal-reports/phuong/sprint-3/pos-completion/task-01-checkout-safety/evidence/review-r4/reviewer-probes.cjs
node docs/09-personal-reports/phuong/sprint-3/pos-completion/task-01-checkout-safety/evidence/review-r4/recovery-probes.cjs
```

Build/suite từ `D:\OISMS\src\frontend\pos-app`: `npm run build`, `npm test`. Không chỉnh source/test sản phẩm trong review; chỉ lưu bằng chứng và tài liệu kết luận. Kết quả ràng buộc với snapshot theo hash, không tự áp dụng cho code thay đổi sau đó.
