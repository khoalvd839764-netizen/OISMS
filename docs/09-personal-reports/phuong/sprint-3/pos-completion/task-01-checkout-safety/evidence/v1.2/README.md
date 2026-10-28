# Evidence bàn giao v1.2 — S3-POS-T01

Ngày 27/10/2026, Asia/Saigon. Bàn giao READY_FOR_REVIEW v1.2 sau khi khắc phục triệt để `POS-T01-F01` theo [v1.2-fix-prompt.md](../../reviews/v1.2-fix-prompt.md) và [v1.1-review-r2.md](../../reviews/v1.1-review-r2.md).

## Thành phần bằng chứng
1. `build.log`: `npm run build` (`tsc -b && vite build`) PASS với exit code 0.
2. `tests.log`: `npm test` (`node --test test-pos-e2e.mjs`) 24/24 PASS không lỗi, không skip/cancel.
3. `source-hashes.json`: SHA-256 hash của 7 file source và test sản phẩm liên quan.
4. `harness.cjs` & `reviewer-probes.cjs`: Probe runner thực thi trực tiếp source TS/TSX production (`App.tsx`, `PaymentModal.tsx`, `LoginModal.tsx`, `api.ts`, `pendingCheckoutStore.ts`).
5. `probe-results.json` & `probes.log`: 9 kịch bản kiểm chứng thực tế, toàn bộ đạt kết quả mong muốn (5 controls + 4 kịch bản đối soát finding F01).

## Hướng dẫn tái hiện độc lập
Từ thư mục gốc `D:\OISMS`:

```powershell
# Chạy probe v1.2
node docs/09-personal-reports/phuong/sprint-3/pos-completion/task-01-checkout-safety/evidence/v1.2/reviewer-probes.cjs

# Chạy build & test suite POS
cd src/frontend/pos-app
npm run build
npm test
```

## Giới hạn môi trường
- Test runner và reviewer probe chạy trên môi trường Node.js VM với React hooks, storage, event bus và fetch transport được stub theo contract.
- Không có browser rendering engine, mạng HTTP thật hay cơ sở dữ liệu PostgreSQL thực. Số lượng đơn `modeledOrders` là số đơn trong mô hình stub, không phải bản ghi database thực tế.
- E2E toàn diện với PostgreSQL và backend container được khóa tại Gate T06 (`S3-POS-T06`).
