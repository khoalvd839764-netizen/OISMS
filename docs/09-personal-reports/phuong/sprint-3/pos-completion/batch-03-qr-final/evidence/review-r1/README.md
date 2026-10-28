# Evidence — B03 parallel review R1

Ngày 28/10/2026, Asia/Saigon. HEAD `a20ebe9d8580a3426e764497ac306fe620942498` và handover B03 chưa commit. [Review](../../reviews/v1.0-parallel-review-r1.md) CHANGES_REQUESTED phần chuẩn bị độc lập; toàn B03 IMPLEMENTING.

Từ root `D:/OISMS`:

```powershell
node --test docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-03-qr-final/qr-module/test-qr-module.mjs
node docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-03-qr-final/evidence/review-r1/reviewer-probes.cjs
```

QR suite 9/9 PASS,0 skip. [Reviewer script](./reviewer-probes.cjs) thực thi actual TypeScript generator trong VM và actual guard runner trong subprocess; [raw results](./probe-results.json) chứa counterexamples, stdout guard, 11 protected hash comparison và7 source hashes. Probe exit0 nghĩa counterexample được assert đúng, không phải feature PASS. Bản sửa cần viết test expected đúng, giữ nguyên script/evidence R1 cho lịch sử.

- VM thiếu Buffer tái hiện ReferenceError, không phải một lần chạy browser QR thật.
- Parser TLV độc lập cho ASCII case100 ký tự; không gọi đó là decoder VietQR được chứng nhận.
- Hai full UUID khác nhau cùng prefix bị helper coi là cùng giao dịch.
- Cả5 subprocess guard cho qua và exit0, không có server hoặc DB mutation trong script hiện tại. Không tác động demo DB.
- Contract QuickLink đối chiếu từ [nhà cung cấp](https://www.vietqr.io/qr-dong): account19, amount13 chữ số, description50/không ký tự đặc biệt. Không gửi fixture tới provider, không tạo giao dịch ngân hàng.
- Không sửa source/test sản phẩm hoặc nội dung handover của Anti, không commit/push/merge trong lượt review.
