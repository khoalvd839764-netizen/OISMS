# Biên bản bàn giao kỹ thuật — S3-POS-T01 / v1.3
## An toàn Checkout khi Retry, Mất mạng, Hết phiên và Recovery sau Reload (Đóng dứt điểm Recovery POS-T01-F01 sau Review R3)

- **Mã task:** `S3-POS-T01`
- **Phiên bản:** `v1.3`
- **Trạng thái bàn giao:** `READY_FOR_REVIEW v1.3` *(chưa đánh dấu VERIFIED; chờ Codex và Lead thẩm định; Task 2–6 vẫn giữ nguyên trạng thái PLANNED theo PLAN.md)*
- **Nhánh thực hiện:** `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`
- **Baseline SHA:** `44317f3b5188c276fa3ef25c8faf30323e503079` (PR #24) cộng working tree v1.3 chưa commit.
- **Môi trường thực thi & runner:** Node.js v22.14.0, Vite v8.3.4, TypeScript v6.0.3 trên Windows 11.
- **Ngày bàn giao:** 27/10/2026, Asia/Saigon.

---

## 1. Lịch sử Review & Mục tiêu Vòng Fix v1.3

### 1.1. Lịch sử các vòng thẩm định
- **Review R1 (v1.0):** Kết luận `CHANGES_REQUESTED` với 4 finding:
  - `POS-T01-F01`: Pending không xác định đúng user/tenant.
  - `POS-T01-F02`: Key bị mất khi kết quả chưa xác định (reset & HTTP 500).
  - `POS-T01-F03`: Reload không phục hồi branch/payload.
  - `POS-T01-F04`: 2xx không đọc được vẫn xóa pending và in hóa đơn chính thức.
- **Review R2 (v1.1):** Thẩm định xác nhận:
  - `POS-T01-F02`, `POS-T01-F03`, `POS-T01-F04`: **CLOSED** trong phạm vi finding.
  - `POS-T01-F01`: **CHANGES_REQUESTED (P1)** còn 2 đường tái hiện (LoginModal bỏ identity và PaymentModal giữ state cũ).
- **Review R3 (v1.2):** Thẩm định xác nhận:
  - Cả 2 đường P1 của R2 đã được sửa đúng: Identity xuyên suốt qua LoginModal và guard owner đồng bộ trong PaymentModal.
  - `POS-T01-F01` còn lại phần phục hồi mức **P2**: Trong `onLoginSuccess`, `App.tsx` tìm pending theo `currentUser` cũ (hoặc lấy `records[0]` khi `currentUser` null) thay vì người vừa xác thực (`user`). Khi quay lại tài khoản cũ (A -> B -> A) hoặc storage có nhiều owner (A rồi B), UI không hydrate đúng branch/cart của người vừa đăng nhập.

> [!IMPORTANT]
> **Về lỗi tồn kho POS-STOCK-F01:** Lỗi tồn kho sau checkout (phải F5 mới cập nhật) đã được xác nhận thuộc **Task 3 (S3-POS-T03)** theo [PLAN.md](../PLAN.md) mục 6. Không đưa thành điều kiện mới của Task 1 và không tạo Task 7.

---

## 2. Giải pháp Kỹ thuật v1.3 (Đóng dứt điểm Recovery POS-T01-F01)

### 2.1. Phục hồi theo Incoming User trong `onLoginSuccess`
- **Nguồn căn cứ phục hồi duy nhất:** Sử dụng chính người dùng vừa xác thực (`user`) để tìm kiếm bản ghi pending trong storage:
  ```typescript
  const incomingPending = getUserPendingCheckout(user);
  ```
- **Hành vi khi Incoming User có Pending Checkout (`unknown` hoặc `submitting`):**
  1. Chuyển trạng thái `submitting` (nếu có do ngắt quãng giữa chừng) sang `unknown` an toàn.
  2. Phục hồi `selectedBranchId` khớp chính xác `incomingPending.owner.branchId`, độc lập hoàn toàn với thứ tự danh sách chi nhánh trả về từ API `fetchBranches`.
  3. Hydrate toàn bộ giỏ hàng `cart` từ snapshot `incomingPending.requestPayload.items` (gồm SKU, tên, biến thể, số lượng, đơn giá).
  4. Đặt `printedReceipt = null`.
  5. Cập nhật danh sách chi nhánh qua `fetchBranches().then(setBranches)` mà **không ghi đè** `selectedBranchId` đã phục hồi.
  6. Mở `PaymentModal` sẽ ở trạng thái Retry, hiển thị đúng `requestKey` cũ và gửi lại đúng payload cũ.
- **Hành vi khi Incoming User KHÔNG có Pending Checkout:**
  - Nếu là cùng tài khoản (`currentUser` trước đó khớp `user`): Giữ nguyên giỏ hàng đang soạn dở (ví dụ hết phiên khi đang chọn sản phẩm trước thanh toán).
  - Nếu là khác tài khoản (hoặc đăng nhập mới sau logout): Bắt đầu phiên sạch (`cart = []`, `setPrintedReceipt(null)`), chọn chi nhánh mặc định từ API (`data[0].id`).
  - **Bảo toàn đa chủ sở hữu:** Bản ghi pending của các owner khác trong storage **tuyệt đối không bị xóa hoặc ghi đè**, sẵn sàng để các owner tương ứng phục hồi khi họ đăng nhập lại.

### 2.2. Nới lỏng kiểu tham số `owner` trong `isSameUserAndTenant`
- Trong [`pendingCheckoutStore.ts`](file:///d:/OISMS/src/frontend/pos-app/src/services/pendingCheckoutStore.ts) và [`api.ts`](file:///d:/OISMS/src/frontend/pos-app/src/services/api.ts): Cập nhật tham số `owner: Partial<CheckoutOwner> | null | undefined` để chấp nhận linh hoạt các đối tượng identity mà vẫn đảm bảo cơ chế **fail-closed** nghiêm ngặt (bắt buộc khớp `tenantId`/`tenantCode` và `userId`/`email`, không dùng tên hiển thị).

---

## 3. Danh sách File Thay đổi trong Lượt Fix v1.3

| File | Thay đổi (+/-) | Mô tả thay đổi cụ thể |
| :--- | :---: | :--- |
| `src/frontend/pos-app/src/App.tsx` | +25 / -20 | Cập nhật `onLoginSuccess`: Tìm pending bằng `getUserPendingCheckout(user)`; phục hồi đúng branch và giỏ từ snapshot payload; bảo toàn phiên sạch cho user không có pending; không xóa pending của owner khác. |
| `src/frontend/pos-app/src/services/pendingCheckoutStore.ts` | +2 / -2 | Cho phép `owner: Partial<CheckoutOwner> | null | undefined` trong `isSameUserAndTenant`. |
| `src/frontend/pos-app/src/services/api.ts` | +2 / -2 | Đồng bộ `isSameUserAndTenant` chấp nhận `Partial<CheckoutOwner>`. |
| `src/frontend/pos-app/test-pos-e2e.mjs` | +255 / -2 | Thêm `Test 25` phủ kín 3 ca: (1) Quay lại tài khoản A sau phiên B, (2) Storage chứa pending A rồi B, đăng nhập lại B, (3) Chuyển sang user C không có pending. Toàn bộ 25 tests pass. |
| `docs/.../task-01-checkout-safety/evidence/v1.3/` | *(Thư mục mới)* | Lưu trữ `build.log`, `tests.log`, `harness.cjs`, `reviewer-probes.cjs`, `recovery-probes.cjs`, `recovery-results.json`, `probe-results.json`, `source-hashes.json`. |

---

## 4. Bằng chứng Thực thi (Execution Evidence)

### 4.1. Lệnh Kiểm thử
Chạy trực tiếp từ thư mục gốc `D:\OISMS`:
```powershell
# Chạy recovery probe v1.3
node docs/09-personal-reports/phuong/sprint-3/pos-completion/task-01-checkout-safety/evidence/v1.3/recovery-probes.cjs

# Chạy full probe suite v1.3
node docs/09-personal-reports/phuong/sprint-3/pos-completion/task-01-checkout-safety/evidence/v1.3/reviewer-probes.cjs

# Chạy build & test suite POS
cd src/frontend/pos-app
npm run build
npm test
```

### 4.2. Nhật ký `npm run build`
Trích từ `docs/.../evidence/v1.3/build.log`:
```text
> pos-app@0.0.0 build
> tsc -b && vite build

vite v8.3.4 building client environment for production...
transforming...
✓ 1914 modules transformed.
rendering chunks...
computing gzip size...
dist/index.html                   2.10 kB │ gzip:  0.93 kB
dist/assets/index-D6aUoRps.css    1.08 kB │ gzip:  0.48 kB
dist/assets/index-Jvh33cEW.js   297.20 kB │ gzip: 88.59 kB

✓ built in 564ms
```

### 4.3. Nhật ký `npm test` (25/25 PASS)
Trích từ `docs/.../evidence/v1.3/tests.log`:
```text
> pos-app@0.0.0 test
> node --test test-pos-e2e.mjs

TAP version 13
# Subtest: 1. File Structure & TypeScript Artifacts Integrity
ok 1 - 1. File Structure & TypeScript Artifacts Integrity
# Subtest: 2. api.ts Real Endpoints & Strict No-Demo Contract
ok 2 - 2. api.ts Real Endpoints & Strict No-Demo Contract
# Subtest: 3. Search & Barcode Matching Simulation with Real API Logic
ok 3 - 3. Search & Barcode Matching Simulation with Real API Logic
# Subtest: 4. Cart Operations & Stock Constraint Validation
ok 4 - 4. Cart Operations & Stock Constraint Validation
# Subtest: 5. Cash Payment & Quick Cash Auto Change Calculations
ok 5 - 5. Cash Payment & Quick Cash Auto Change Calculations
# Subtest: 6. 80mm Thermal Receipt Layout & Draft Watermark Contract
ok 6 - 6. 80mm Thermal Receipt Layout & Draft Watermark Contract
# Subtest: 7. Checkout Contract & Draft Preview Decoupling
ok 7 - 7. Checkout Contract & Draft Preview Decoupling
# Subtest: 8. Negative Case: Barcode Miss does NOT add results[0]
ok 8 - 8. Negative Case: Barcode Miss does NOT add results[0]
# Subtest: 9. Negative Case: Branch Change & Logout Clears Cart
ok 9 - 9. Negative Case: Branch Change & Logout Clears Cart
# Subtest: 10. Negative Case: Available stock boundary enforcement
ok 10 - 10. Negative Case: Available stock boundary enforcement
# Subtest: 11. PWA Baseline Configuration
ok 11 - 11. PWA Baseline Configuration
# Subtest: 12. Direct Runtime API Logic & Status Code Handling
ok 12 - 12. Direct Runtime API Logic & Status Code Handling
# Subtest: 13. Workflow: Failed Checkout & Preview Receipt preserves Cart
ok 13 - 13. Workflow: Failed Checkout & Preview Receipt preserves Cart
# Subtest: 14. S3-POS-T01 AC-01 & AC-06: UUID RFC 4122 Compliance & Fallback Without crypto.randomUUID
ok 14 - 14. S3-POS-T01 AC-01 & AC-06: UUID RFC 4122 Compliance & Fallback Without crypto.randomUUID
# Subtest: 15. S3-POS-T01 AC-01 & AC-02: Response Lost after Server Commit -> Retry Preserves Key and Payload Snapshot (Closes PR24-F01)
ok 15 - 15. S3-POS-T01 AC-01 & AC-02: Response Lost after Server Commit -> Retry Preserves Key and Payload Snapshot (Closes PR24-F01)
# Subtest: 16. S3-POS-T01 AC-01: Pending Checkout Preserved across Modal Close/Open & Reload
ok 16 - 16. S3-POS-T01 AC-01: Pending Checkout Preserved across Modal Close/Open & Reload
# Subtest: 17. S3-POS-T01 AC-03: Checkout 401 preserves Cart; Same-User restores, Cross-User clears (Closes PR24-F02)
ok 17 - 17. S3-POS-T01 AC-03: Checkout 401 preserves Cart; Same-User restores, Cross-User clears (Closes PR24-F02)
# Subtest: 18. S3-POS-T01 AC-04: Concurrency Control (Parallel Submit Blocked & Cart Protection while Pending)
ok 18 - 18. S3-POS-T01 AC-04: Concurrency Control (Parallel Submit Blocked & Cart Protection while Pending)
# Subtest: 19. S3-POS-T01 AC-05: RFC 7807 ProblemDetails Error Classification & Canonical/Alias Route Fallback
ok 19 - 19. S3-POS-T01 AC-05: RFC 7807 ProblemDetails Error Classification & Canonical/Alias Route Fallback
# Subtest: 20. S3-POS-T01 AC-06: Confirmed Success Single-Clear & Database E2E Gate Transparency
ok 20 - 20. S3-POS-T01 AC-06: Confirmed Success Single-Clear & Database E2E Gate Transparency
# Subtest: 21. S3-POS-T01-F01: Authenticated identity lifecycle across LoginModal, App & persistent PaymentModal
ok 21 - 21. S3-POS-T01-F01: Authenticated identity lifecycle across LoginModal, App & persistent PaymentModal
# Subtest: 22. S3-POS-T01-F02: Reset control & HTTP 500 retain unknown pending attempt, retry keeps same key
ok 22 - 22. S3-POS-T01-F02: Reset control & HTTP 500 retain unknown pending attempt, retry keeps same key
# Subtest: 23. S3-POS-T01-F03: Reload recovery restores branch & payload snapshot without API order dependency
ok 23 - 23. S3-POS-T01-F03: Reload recovery restores branch & payload snapshot without API order dependency
# Subtest: 24. S3-POS-T01-F04: Safe success confirmation, unreadable JSON or missing minimal order confirmation retains pending for verification
ok 24 - 24. S3-POS-T01-F04: Safe success confirmation, unreadable JSON or missing minimal order confirmation retains pending for verification
# Subtest: 25. S3-POS-T01-F01 v1.3: Accurate recovery by incoming user across multi-owner storage and account switching
ok 25 - 25. S3-POS-T01-F01 v1.3: Accurate recovery by incoming user across multi-owner storage and account switching
1..25
# tests 25
# suites 0
# pass 25
# fail 0
# cancelled 0
# skipped 0
# todo 0
# duration_ms 1071.8802
```

### 4.4. Đối chiếu Chi tiết 2 Kịch bản Recovery Probes (Review R3 vs Fix v1.3)

Trích từ `docs/.../evidence/v1.3/recovery-results.json`:

| Kịch bản Recovery Probe | Kết quả Review R3 v1.2 | Kết quả Fix v1.3 | Đánh giá |
| :--- | :--- | :--- | :---: |
| **1. expiry-switch-back**<br/>(A pending ở branch A -> logout -> login B -> B hết phiên -> login A. API trả branch B trước A) | `actualBranch: branchB`<br/>`actualCartRows: 0`<br/>`pendingVisibleForSelectedBranch: false`<br/>`addNewItemAllowed: true`<br/>`sentNewKeyInsteadOfRecovering: true` *(Bug không về branch/giỏ A)* | `actualBranch: branchA`<br/>`actualCartRows: 1`<br/>`pendingVisibleForSelectedBranch: true`<br/>`addNewItemBlocked: true`<br/>`reusedOriginalKey: true`<br/>`retryBranch: branchA` | **RESOLVED (Đóng POS-T01-F01 Recovery Ca 1)** |
| **2. two-owners-logout-relogin**<br/>(Storage chứa pending A rồi pending B -> B logout -> login B) | `actualCartRows: 0`<br/>`ownPendingRetained: true` *(Bug lấy nhầm record A đầu list làm căn cứ, xóa giỏ B)* | `actualBranch: branchB`<br/>`actualCartRows: 1 (SKU-B)`<br/>`pendingVisibleForSelectedBranch: true`<br/>`addNewItemBlocked: true`<br/>`reusedOriginalKey: true (reqKeyB)`<br/>`retryBranch: branchB` | **RESOLVED (Đóng POS-T01-F01 Recovery Ca 2)** |

### 4.5. Đối chiếu Toàn bộ 9 Kịch bản Control & Lifecycle Probes (v1.3)
Trích từ `docs/.../evidence/v1.3/probe-results.json`:
- `lost: reset then retry`: `pendingKept: true`, `sameKey: true`, `modeledOrders: 1` (**PASS**)
- `500: reset then retry`: `pendingKept: true`, `sameKey: true`, `modeledOrders: 1` (**PASS**)
- `parse-error` (200 JSON lỗi): `printedOfficialReceipt: 0`, `pendingKept: true` (**PASS**)
- `missing-order` (200 thiếu mã): `printedOfficialReceipt: 0`, `pendingKept: true` (**PASS**)
- `Reload with submitting & B first`: `branchRecovered: branchA`, `cartRows: 1`, `status: unknown` (**PASS**)
- `Persistent modal different-user`: `oldKeyDisplayed: false`, `newKeyGeneratedForUserB: true`, `userARecordPreserved: true` (**PASS**)
- `Persistent modal different-tenant`: `oldKeyDisplayed: false`, `newKeyGeneratedForUserB: true`, `userARecordPreserved: true` (**PASS**)
- `Same-user reauth actual LoginModal`: `tenantId: tenant-a`, `cartRowsAfterLogin: 1`, `pendingAccessibleForUi: true` (**PASS**)
- `Retry attempt after real relogin`: `reusedOriginalKey: true`, `sparseOwnerCreated: false`, `retryLookupForUi: true` (**PASS**)

---

## 5. Minh bạch Môi trường Kiểm thử và Giới hạn

1. **Phân biệt Môi trường Stub / Node VM với PostgreSQL thật:**
   - Các kết quả `modeledOrders: 1` là kết quả của **mô hình persistence stub** trong runtime Node VM phục vụ việc kiểm tra contract, lifecycle và hành vi của module frontend thực tế.
   - **Tuyệt đối không dùng số đơn của mô hình stub làm bằng chứng cơ sở dữ liệu.**
   - Kiểm thử E2E trên trình duyệt thật (Chrome/Edge PWA) nối với Backend HTTP và PostgreSQL database thật: **Gated tại Task 6 (S3-POS-T06)** theo kế hoạch.
2. **Kế thừa và Chuyển tiếp các Task tiếp theo:**
   - **S3-POS-T02:** Map và in hóa đơn từ Server Receipt snapshot (`PosReceiptDto`, không dùng dữ liệu giỏ hàng để thay thế snapshot server).
   - **S3-POS-T03:** Làm mới tồn ngay sau checkout không cần F5; Tích hợp client SignalR (`/hubs/notifications`) cho cập nhật đa thiết bị; Nghiệm thu `POS-STOCK-F01`.
   - **S3-POS-T04:** Quyết định phạm vi VietQR và bằng chứng thanh toán.
   - **S3-POS-T05:** Kiểm chứng PWA shell, khổ in 58mm/80mm và tương tác bàn phím quầy thu ngân.
   - **S3-POS-T06:** Kiểm thử tích hợp toàn diện E2E, Docker PostgreSQL evidence và hoàn tất PR #24.

---

## 6. Kết luận Bàn giao

Phiên bản **S3-POS-T01 v1.3** đã:
- Sửa triệt để toàn bộ các khía cạnh còn lại của finding `POS-T01-F01` (chọn nguồn phục hồi theo incoming user; bảo toàn branch/cart/key/payload của đúng owner; cách ly an toàn khi đổi tài khoản và lưu trữ đa chủ sở hữu).
- Giữ nguyên vẹn các sửa chữa đã đạt của `POS-T01-F02`, `POS-T01-F03`, `POS-T01-F04`.
- Vượt qua 100% build production (`npm run build`) và 25/25 tests (`npm test`) trong `src/frontend/pos-app`.
- Vượt qua 11/11 kịch bản runtime probes thực tế (9 control/lifecycle probes + 2 recovery probes).
- Giữ nguyên lịch sử review v1.0, v1.1, v1.2; không commit, push hay merge trong lượt này.

**Chính thức bàn giao ở trạng thái: `READY_FOR_REVIEW v1.3`.**
