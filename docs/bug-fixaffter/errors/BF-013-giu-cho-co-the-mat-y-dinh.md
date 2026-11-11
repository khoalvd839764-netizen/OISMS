# BF-013 — Giữ chỗ có thể mất ý định chưa rõ kết quả khi sửa form

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa |
| **Ưu tiên** | P1 |
| **Khu vực** | Giữ chỗ kho |
| **Phát hiện** | 28–29/10/2026 (Xác nhận bằng code) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

## Mô tả lỗi (lúc phát hiện)

**Kịch bản:** POST reserve timeout sau khi có thể đã commit → UI ở trạng thái retry → người dùng đổi chi nhánh/kho/SKU/số lượng.

**Thực tế:** `handleReserveFormChange` tự xóa pending reservation và sinh key mới, không yêu cầu bỏ/đối soát ý định cũ. Trong khi đó nút “Bỏ thao tác cũ” riêng lại có confirm, nên guard có thể bị đi vòng bằng cách sửa input.

**Ảnh hưởng:** lần giữ chỗ đầu còn tồn tại nhưng UI mất theo dõi; thao tác tiếp theo có key khác và có thể giữ chỗ thêm.

**Vị trí:** `pages/InventoryPage.tsx:handleReserveFormChange/handleDiscardPendingReservation/handleReserveStock`.

**Điều kiện đóng:** khóa snapshot khi unknown hoặc yêu cầu quyết định rõ trước khi đổi; không mất pending/key chỉ do gõ input. Test timeout-after-commit, đổi trường, reload, retry và số reserved cuối trên DB test.

## Cách đã sửa

* **Thành phần:** `src/frontend/admin-dashboard/src/pages/InventoryPage.tsx`
* **Triệu chứng & Nguyên nhân:**
  * Khi gửi yêu cầu giữ chỗ tồn kho (reservation) bị timeout (kết quả chưa rõ ràng trên server), giao diện lưu trạng thái pending retry. Tuy nhiên nếu người dùng chỉnh sửa các trường trong form, mã cũ tự động xóa pending reservation và sinh key mới mà không cảnh báo, làm mất dấu vết giao dịch cũ đang xử lý trên backend.
* **Giải pháp khắc phục:**
  * Khóa toàn bộ các input trong form giữ chỗ khi đang gửi hoặc đang có ý định giữ chỗ chưa rõ kết quả (`isRetryingReserve` / `pendingReserveSnapshot`).
  * Hiển thị hộp thoại xác nhận bắt buộc trước khi người dùng muốn hủy ý định giữ chỗ đang chờ đối soát để nhập yêu cầu mới, và bảo toàn nguyên vẹn `idempotencyKey` khi bấm thử lại.
* **Kiểm thử xác thực:** Pass kịch bản kiểm thử `BF-013` trong `scratch/test_bugfix_after_verification.cjs`: các trường input bị khóa hoàn toàn khi ở trạng thái unknown và lần bấm retry gửi lại đúng `idempotencyKey` ban đầu.
