# Báo cáo hồi quy POS `BF-019` và `BF-020` (`OISMS-66-FIN-G`)

- **Ngày:** 2026-11-21
- **Commit nền:** `879a1ef`

## 1. Tái hiện trước sửa

- Tạo `test-oisms/frontend/pos-app/test-bf019-bf020.mjs` với 3 ca: `BF-019` (HTTP 200 body rỗng / thiếu mã đơn / GUID rỗng), `BF-020` (checkout A pending → đóng modal → đổi branch B, thêm hàng → A trả thành công), và `BF-003` (retry giữ `RequestKey`).
- Chạy `node --test test-oisms/frontend/pos-app/test-bf019-bf020.mjs` trước khi sửa:
  - `BF-019`: PASS
  - `BF-020`: **FAIL** (`1 !== 0` — `PaymentModal` gọi `onSuccessAndPrint` cho phản hồi muộn của branch A dù ngữ cảnh đã đổi sang branch B).
  - `BF-003`: PASS

## 2. Đã sửa

- `src/frontend/pos-app/src/components/pos/PaymentModal.tsx`:
  - Lưu `contextSignatureRef` gồm `tenantId:userId:branchId:items`.
  - Trước `await checkoutPosOrder(...)`, chụp `requestContextSignature`, `requestBranchId`, `requestUser`.
  - Sau khi `checkoutPosOrder` trả về: dọn/cập nhật `pendingCheckout` đúng `(requestUser, requestBranchId)`, và chỉ gọi `onSuccessAndPrint`, `onClose`, `setPendingAttempt`, `setApiNotice` khi `contextSignatureRef.current === requestContextSignature`.
- `src/frontend/pos-app/package.json`: thêm `test-bf019-bf020.mjs` vào script `"test"`.

## 3. Kiểm chứng sau sửa

- `npm run build --prefix src/frontend/pos-app` → Thành công (0 lỗi).
- `npm run lint --prefix src/frontend/pos-app` → 0 lỗi.
- `npm test --prefix src/frontend/pos-app` → **PASS 101/101**.
