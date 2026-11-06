# BF-004 — Tạo phiếu chuyển kho đổi key khi retry

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa |
| **Ưu tiên** | P1 |
| **Khu vực** | Chuyển kho |
| **Phát hiện** | 28–29/10/2026 (Tái hiện có kiểm soát; PR26-F06) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

## Mô tả lỗi (lúc phát hiện)

**Tái hiện:** POST tạo phiếu mất phản hồi → bấm tạo lại với cùng form. Service luôn gọi `generateUuid()` mới; caller không có snapshot/key ổn định.

**Ảnh hưởng:** có thể sinh nhiều phiếu Draft cho cùng một yêu cầu. Không khẳng định đã trừ tồn hai lần ở bước tạo Draft; rủi ro xuất lặp xuất hiện nếu các phiếu trùng tiếp tục được xử lý.

**Vị trí:** `services/stockTransferService.ts:createTransfer`, `components/transfers/CreateTransferModal.tsx:handleSubmit`.

**Bằng chứng:** `retry-transfer`.

**Điều kiện đóng:** một ý định tạo phiếu giữ cùng key/payload, gồm timeout/reopen/reload; unknown có đối soát hoặc thao tác bỏ ý định rõ ràng. Test replay sau commit trả cùng transfer ID.

## Cách đã sửa

* **Thành phần:** `src/frontend/admin-dashboard/src/services/stockTransferService.ts`, `src/frontend/admin-dashboard/src/components/transfers/CreateTransferModal.tsx`
* **Triệu chứng & Nguyên nhân:**
  * Khi tạo phiếu chuyển kho bị timeout mạng, người dùng bấm thử lại. Service tự động sinh UUID mới nếu caller không truyền key hoặc khi người dùng mở lại form với cùng dữ liệu, dẫn đến tạo nhiều phiếu Draft chuyển kho trùng lặp.
* **Giải pháp khắc phục:**
  * Quản lý `intentState` gắn với chữ ký payload `(fromWarehouseId, toWarehouseId, items, notes)` trong `CreateTransferModal.tsx`.
  * Tại `stockTransferService.ts`, lưu trữ `lastTransferIntent` trong cả bộ nhớ và `sessionStorage` theo chữ ký payload để tái sử dụng đúng `requestKey` khi retry cùng một ý định chuyển kho, và tự động sinh key mới khi người dùng thay đổi nội dung phiếu.
* **Kiểm thử xác thực:** Pass kịch bản `retry-transfer`: retry tạo phiếu chuyển kho bảo toàn 100% `requestKey`.
