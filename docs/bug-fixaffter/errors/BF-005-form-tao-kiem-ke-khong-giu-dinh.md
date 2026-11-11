# BF-005 — Form tạo kiểm kê không giữ định danh retry

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa |
| **Ưu tiên** | P1 |
| **Khu vực** | Kiểm kê |
| **Phát hiện** | 28–29/10/2026 (Tái hiện có kiểm soát; PR26-F06) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

## Mô tả lỗi (lúc phát hiện)

**Tái hiện:** Tạo kiểm kê mất phản hồi rồi gửi lại. Service hỗ trợ `req.requestKey`, nhưng form không cung cấp/persist key, nên lần gọi sau sinh key mới.

**Ảnh hưởng:** nhiều phiên kiểm kê/snapshot cho cùng một ý định, người dùng khó xác định phiên nào cần tiếp tục chốt.

**Vị trí:** `services/stockCountService.ts:createStockCount`, `components/stockCounts/CreateStockCountModal.tsx:handleSubmit`.

**Bằng chứng:** `retry-count`.

**Điều kiện đóng:** form có snapshot/key theo actor; giữ qua unknown/retry/reload; xác minh bằng callback form thật và HTTP/PostgreSQL replay.

## Cách đã sửa

* **Thành phần:** `src/frontend/admin-dashboard/src/services/stockCountService.ts`, `src/frontend/admin-dashboard/src/components/stockCounts/CreateStockCountModal.tsx`
* **Triệu chứng & Nguyên nhân:**
  * Tương tự BF-004: Khi tạo phiếu kiểm kho bị lỗi mạng rồi gửi lại, service sinh key mới tạo ra nhiều đợt kiểm kê trùng lặp.
* **Giải pháp khắc phục:**
  * Thiết lập `intentState` gắn với chữ ký `(warehouseId, type, notes, skuIds)` trong `CreateStockCountModal.tsx`.
  * Tại `stockCountService.ts`, triển khai cache ý định `lastCountIntent` đồng bộ với `sessionStorage` theo chữ ký payload kiểm kê để tái sử dụng khóa định danh khi retry và cấp khóa mới khi thay đổi thông tin đợt kiểm kê.
* **Kiểm thử xác thực:** Pass kịch bản `retry-count`: retry tạo kiểm kê bảo toàn `requestKey`.
