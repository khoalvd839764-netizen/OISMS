# Bàn Giao Triển Khai — S3-POS-B01 / v1.2 — Hóa Đơn và In Nhiệt (T02 + T05 Print)

Trạng thái: **READY_FOR_REVIEW v1.2**  
Ngày bàn giao: **27/10/2026** (Asia/Saigon)  
Người thực hiện: **Antigravity / Phương Lê — Frontend POS**  
Nhánh: `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`  
Git HEAD: `44317f3b5188c276fa3ef25c8faf30323e503079`  
Hash working tree chi tiết: [source-hashes.json](./evidence/v1.2/source-hashes.json)  
Tài liệu review liên quan: [v1.0-review-r1.md](./reviews/v1.0-review-r1.md), [v1.1-review-r2.md](./reviews/v1.1-review-r2.md), [v1.2-fix-prompt.md](./reviews/v1.2-fix-prompt.md)

---

## 1. Mục Tiêu và Tóm Tắt Vòng Fix v1.2 (Sau Review R2)

Sau đợt Review R2:
- **B01-F01 (P1 — Vùng in bị ẩn bởi CSS tổ tiên)**: Đã **CLOSED** ở v1.1.
- **B01-F03 (P2 — Cú pháp @page rơi về Letter)**: Đã **CLOSED** ở v1.1.
- **B01-F02 (P1 — Item rỗng [{}] & metadata sai kiểu, mất pending trước mapper)**: Đã **CLOSED** ở v1.1.
- **B01-F02 (P2 còn lại — Timestamp)**: Được xử lý triệt để trong vòng fix **v1.2**:

1. **Khắt khe hóa kiểm tra `createdAt` theo đúng hợp đồng Backend (`PosReceiptDto.CreatedAt: DateTimeOffset`)**:
   - `validatePosReceiptSnapshot` trong `api.ts`: Bắt buộc `dto.createdAt` phải tồn tại, có kiểu `string` không rỗng sau khi `trim()`, và phân tích cú pháp được thành ngày hợp lệ (`!isNaN(Date.parse(dto.createdAt))`).
   - Mọi trường hợp `createdAt` bị thiếu (`undefined`), `null`, rỗng (`""`), khoảng trắng (`"   "`), hoặc định dạng ngày sai đều bị từ chối ngay tại tầng API validation.
2. **Loại bỏ hoàn toàn fallback đồng hồ máy khách (`new Date()`) cho hóa đơn chính thức**:
   - Trong `mapServerReceiptToReceiptData`: Xóa bỏ triệt để đoạn fallback `new Date().toLocaleString('vi-VN')`. Nếu timestamp thiếu hoặc không thể phân tích cú pháp, mapper ném `Error` ngay lập tức.
   - Hóa đơn chính thức bắt buộc chỉ mang dấu thời gian xác nhận do chính máy chủ backend cấp.
3. **Bảo toàn pending checkout attempt cho đối soát / retry khi timestamp lỗi**:
   - Khi server trả HTTP 2xx nhưng thiếu hoặc sai `createdAt`, `checkoutPosOrder` trả về `{ success: false, isUnknown: true }`.
   - `PaymentModal.tsx` không gọi `onSuccessAndPrint`, giữ nguyên bản ghi pending trong `sessionStorage` ở trạng thái `'unknown'`.
   - Lần bấm thanh toán tiếp theo (retry) gửi lại chính xác cùng `requestKey` và toàn bộ snapshot payload ban đầu (đảm bảo tính lũy nghiệm Idempotency).
4. **Đường thành công & Replay giữ nguyên timestamp của server**:
   - Khi server trả `createdAt` hợp lệ (ví dụ `2026-10-27T07:00:00Z`), hóa đơn map chính xác theo thời gian này, độc lập hoàn toàn với đồng hồ máy khách.
   - Replay 200 giữ nguyên timestamp của server snapshot.
5. **Dọn dẹp định dạng**: `git diff --check` sạch 100%, không còn trailing whitespace.

---

## 2. Bảng Tiến Trình Đóng Các Finding (R1 & R2)

| Mã Finding | Mức độ | Vòng review | Nguyên nhân | Giải pháp kỹ thuật | Kiểm chứng & Evidence | Trạng thái |
| :--- | :---: | :---: | :--- | :--- | :--- | :---: |
| **B01-F01** | **P1** | R1 | `.no-print-bg` có `display: none !important` làm ẩn toàn bộ hóa đơn. | Đổi `.no-print-bg` và `.no-print-border` thành `display: block !important; background: transparent !important;`. | Test 32 PASS; Báo cáo [print-verification-report.json](./evidence/v1.1/print-verification-report.json) xác nhận `bgDisplay: 'block'`. | **CLOSED (v1.1)** |
| **B01-F03** | **P2** | R1 | `@page { size: 58mm auto; }` không hợp lệ bị Chromium drop về Letter. | Đổi sang `@page { size: 58mm/80mm 200mm; margin: 0; }`. Xuất PDF bằng Edge headless từ production component thật. | Test 31 & Test 34 PASS; 4 PDF MediaBox đo được 57.8mm và 80.1mm. | **CLOSED (v1.1)** |
| **B01-F02 (P1)** | **P1** | R1 | 1. Bỏ qua item `{}` và crash metadata `branchAddress: 42`.<br>2. Clear pending trước khi map receipt. | 1. Validate cấu trúc item và kiểu chuỗi metadata.<br>2. Đảo thứ tự: Map thành công mới clear pending; lỗi giữ pending `unknown`. | Test 33 PASS; Repro 1 & Repro 2 giữ nguyên pending attempt và `requestKey`. | **CLOSED (v1.1)** |
| **B01-F02 (P2)** | **P2** | R2 | `validatePosReceiptSnapshot` bỏ qua `createdAt` khi null/missing; mapper dùng `new Date()` client làm fallback in hóa đơn chính thức. | 1. Bắt buộc `createdAt` phải là chuỗi ngày hợp lệ, không rỗng trong `validatePosReceiptSnapshot`.<br>2. Xóa bỏ fallback `new Date()` trong `mapServerReceiptToReceiptData`.<br>3. Thiếu/null `createdAt` giữ pending `unknown`, retry cùng key/payload. | Test 33 (v1.2) PASS.<br>Probe độc lập 7 kịch bản đạt 100% ([receipt-results.json](./evidence/v1.2/receipt-results.json)). | **CLOSED (v1.2)** |

---

## 3. Ma Trận Nghiệm Thu Toàn Diện (S3-POS-B01 v1.2)

| Mã / Yêu cầu | Điều kiện đạt | Kiểm chứng thực hiện | Kết quả | Evidence |
| --- | --- | --- | --- | --- |
| **PR24-F03** | Hóa đơn chính thức lấy 100% dữ liệu từ server snapshot (`PosReceiptDto`), không dùng giỏ local vá hóa đơn. | Test 26: Giỏ local giữ tên cũ, server trả tên mới; receipt hiển thị đúng tên server. | **PASS** | `test-pos-e2e.mjs` (Test 26) |
| **T02-AC-01** | Strongly-typed DTOs backend (`PosReceiptDto`, `PosReceiptItemDto`, `PosCheckoutResponseDto`), ánh xạ đầy đủ SKU, giá, thành tiền, chiết khấu, timestamp. | Module `mapServerReceiptToReceiptData` và handler production chạy với fixtures backend contract. | **PASS** | `test-pos-e2e.mjs` (Test 26), `receiptMapper.ts` |
| **T02-AC-02** | Giỏ giữ tên cũ, server trả tên mới: hóa đơn lấy tên server. Không dùng items/tổng local vá response. | Fixture cố tình khác biệt giữa giỏ và response backend. | **PASS** | `test-pos-e2e.mjs` (Test 26) |
| **T02-AC-03** | 2xx nhưng receipt hỏng/thiếu/mảng items rỗng/thiếu timestamp không được xóa pending và không in hóa đơn giả; giữ trạng thái `isUnknown: true`. | Test 27 & Test 33: Test negative (thiếu receipt, items rỗng, item `{}`, missing/null `createdAt`). Pending attempt được giữ nguyên trong storage với cùng `requestKey`. | **PASS** | `test-pos-e2e.mjs` (Test 27, 33), `api.ts` |
| **T02-AC-04** | Phiếu tạm tính có nhãn `CHƯA THANH TOÁN (PHIẾU TẠM TÍNH)`, không gọi API checkout, không xóa giỏ/key. Hỗ trợ in lại hóa đơn đã xác nhận mà không checkout lại. | Test 28: Preview gửi 0 request mạng; in lại từ `App.tsx` không phát sinh request thứ 2. | **PASS** | `test-pos-e2e.mjs` (Test 28), `App.tsx` |
| **T02-AC-05** | Nullable `branchAddress`, `branchPhone`, `customerName` hiển thị an toàn, không bịa đặt thông tin. Không chứa giá vốn/COGS trong receipt. Bắt buộc timestamp từ server, không lấy giờ client. | Test 29 & Test 33: Fixture các trường null; inspect DOM; kiểm tra timestamp server hợp lệ độc lập client clock. | **PASS** | `test-pos-e2e.mjs` (Test 29, 33) |
| **T05-AC-02** | Hỗ trợ 2 khổ in 58mm và 80mm theo lựa chọn thu ngân (lưu `localStorage`); cả tạm tính và chính thức không bị che/cắt cột số tiền lớn hoặc tên sản phẩm dài. | Test 30: Chuyển đổi khổ giấy, layout 2 dòng cho 58mm, bảng 4 cột cho 80mm, subtotal lớn `97.500.000 đ` không bị cắt số. | **PASS** | `test-pos-e2e.mjs` (Test 30) |
| **T05-AC-05** | Ghi nhận môi trường kiểm tra in (Microsoft Edge Chromium headless, viewport, khổ giấy CSS), sinh file PDF bằng chứng trực quan. | Test 31 & Test 34: 4 file PDF được tạo bằng Microsoft Edge headless + production component thật, đo MediaBox chuẩn xác ~58mm và ~80mm. | **PASS** | `evidence/v1.1/*.pdf`, `print-verification-report.json` |
| **B01-F01** | Giữ vùng in và các ancestor (`.no-print-bg`, `.no-print-border`) hiển thị khi in (`display: block !important`), chỉ ẩn interactive controls. | Test 32: Kiểm tra CSS stylesheet và DOM runtime, xác nhận layout không bị `display: none`. | **PASS** | `test-pos-e2e.mjs` (Test 32), `index.css` |
| **B01-F02 (P1+P2)** | Validate receipt snapshot toàn diện (loại trừ `{ items: [{}] }`, metadata sai kiểu, missing/null/empty `createdAt`). Map thành công TRƯỚC khi xóa pending. Không dùng `new Date()` client. Lỗi giữ nguyên `requestKey`/payload và set `unknown`. | Test 33 (v1.2): Unit validator, unit mapper, và 4 ca integration qua PaymentModal (empty item, missing date, null date, control valid timestamp). Tất cả negative giữ pending `unknown` và retry cùng key/body. | **PASS** | `test-pos-e2e.mjs` (Test 33), `receipt-results.json` |
| **B01-F03** | Dùng cú pháp `@page` hợp lệ theo CSS Paged Media (`size: 58mm/80mm 200mm; margin: 0;`), tạo PDF từ component/CSS thật, kiểm tra MediaBox và fonts. | Test 34: MediaBox 80mm = 80.1mm (227.04pt), 58mm = 57.8mm (163.92pt), file size > 70KB, chứa stream Font. | **PASS** | `test-pos-e2e.mjs` (Test 34) |

---

## 4. Bằng Chứng Thực Nghiệm v1.2

### 4.1. TypeScript & Vite Build
```text
> pos-app@0.0.0 build
> tsc -b && vite build

vite v8.3.4 building client environment for production...
transforming...
✓ 1914 modules transformed.
rendering chunks...
computing gzip size...
dist/index.html                   2.10 kB │ gzip:  0.92 kB
dist/assets/index-BtRpW0r5.css    1.86 kB │ gzip:  0.60 kB
dist/assets/index-BllcZmtL.js   306.41 kB │ gzip: 90.82 kB

✓ built in 557ms
```
*(Chi tiết: [build.log](./evidence/v1.2/build.log))*

### 4.2. Test Suite (34/34 PASS)
```text
> pos-app@0.0.0 test
> node --test test-pos-e2e.mjs

TAP version 13
# Subtest: 1. File Structure & TypeScript Artifacts Integrity (ok)
# ...
# Subtest: 32. B01-F01 Print Visibility Regression: Ancestor display and receipt-paper visibility (ok - 0.8ms)
# Subtest: 33. B01-F02 Backend Contract Validation & Pending Retention Regression (ok - 148.0ms)
# Subtest: 34. B01-F03 CSS Paged Media Syntax & MediaBox Measurement Regression (ok - 1.6ms)
1..34
# tests 34
# suites 0
# pass 34
# fail 0
# cancelled 0
# skipped 0
# todo 0
# duration_ms 1927.2847
```
*(Chi tiết: [tests.log](./evidence/v1.2/tests.log))*

### 4.3. Bằng Chứng Đối Soát Probe Reviewer (7/7 Kịch Bản Hoàn Hảo)
Thực thi kiểm chứng dựa trên mô hình probe độc lập của Reviewer (`receipt-probes.cjs`):

```json
{
  "provider": "Actual API/validator/mapper/PaymentModal TS/TSX in Node VM; hooks/storage/fetch stubs, mapping fault injection only in mapper-throws control. No backend/PostgreSQL.",
  "observations": [
    {
      "kind": "valid-snapshot",
      "printedCount": 1,
      "pendingKeptAfterFirst": false,
      "sourceCreatedAt": "2026-10-27T07:00:00Z",
      "mappedCreatedAt": "14:00:00 27/10/2026",
      "requestCount": 1
    },
    {
      "kind": "empty-item",
      "printedCount": 0,
      "pendingKeptAfterFirst": true,
      "sourceCreatedAt": "2026-10-27T07:00:00Z",
      "mappedCreatedAt": null,
      "requestCount": 2,
      "retrySameKey": true
    },
    {
      "kind": "bad-metadata",
      "printedCount": 0,
      "pendingKeptAfterFirst": true,
      "sourceCreatedAt": "2026-10-27T07:00:00Z",
      "mappedCreatedAt": null,
      "requestCount": 2,
      "retrySameKey": true
    },
    {
      "kind": "bad-date",
      "printedCount": 0,
      "pendingKeptAfterFirst": true,
      "sourceCreatedAt": "invalid-date",
      "mappedCreatedAt": null,
      "requestCount": 2,
      "retrySameKey": true
    },
    {
      "kind": "missing-date",
      "printedCount": 0,
      "pendingKeptAfterFirst": true,
      "sourceCreatedAt": null,
      "mappedCreatedAt": null,
      "requestCount": 2,
      "retrySameKey": true
    },
    {
      "kind": "null-date",
      "printedCount": 0,
      "pendingKeptAfterFirst": true,
      "sourceCreatedAt": null,
      "mappedCreatedAt": null,
      "requestCount": 2,
      "retrySameKey": true
    },
    {
      "kind": "mapper-throws",
      "printedCount": 0,
      "pendingKeptAfterFirst": true,
      "sourceCreatedAt": "2026-10-27T07:00:00Z",
      "mappedCreatedAt": null,
      "requestCount": 2,
      "retrySameKey": true
    }
  ]
}
```
*(Chi tiết: [receipt-results.json](./evidence/v1.2/receipt-results.json))*

---

## 5. Giới Hạn & Minh Bạch Kỹ Thuật

1. **Thiết bị in nhiệt vật lý**: Đã kiểm chứng chuẩn xác 100% kích thước MediaBox, CSS layout và typography qua engine Chromium/Edge; chưa gắn dây USB/LAN/Bluetooth tới thiết bị in nhiệt vật lý tại cửa hàng (phụ thuộc vào hardware driver tại quầy).
2. **VietQR động & Webhook ngân hàng**: Đang hiển thị mã mô phỏng kèm cảnh báo; thuộc phạm vi riêng của Đợt 3 (S3-POS-B03 / T04).
3. **Cập nhật tồn không cần F5 & Realtime SignalR (POS-STOCK-F01)**: Thuộc phạm vi riêng của Đợt 2 (S3-POS-B02 / T03).
4. **Kiểm thử E2E Full-stack (Browser → C# Backend → PostgreSQL thật)**: Thuộc phạm vi nghiệm thu cuối của Đợt 3 (S3-POS-B03 / T06).

---

## 6. Kết Luận

- Phiên bản **v1.2** đã đóng triệt để phần P2 còn lại của **B01-F02** (xử lý nghiêm ngặt timestamp `createdAt`, xóa bỏ hoàn toàn fallback `new Date()` client cho hóa đơn chính thức).
- Toàn bộ 4 finding của B01 (**B01-F01, B01-F02, B01-F03**) cùng **PR24-F03** đã được giải quyết trọn vẹn.
- Bộ test suite đạt **34/34 tests PASS**, probe 7 kịch bản đạt 100%.
- Bàn giao dừng ở trạng thái **`READY_FOR_REVIEW v1.2`**, không tự ý ghi `VERIFIED`, không commit, push hoặc merge.
