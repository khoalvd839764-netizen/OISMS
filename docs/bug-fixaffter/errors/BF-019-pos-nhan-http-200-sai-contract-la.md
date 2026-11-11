# BF-019 — POS nhận HTTP 200 sai contract là thành công

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | 🟡 Đã sửa, chưa có test riêng |
| **Ưu tiên** | P2 |
| **Khu vực** | POS |
| **Phát hiện** | 28–29/10/2026 (Tái hiện có kiểm soát) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

> **Hiện trạng 06/11/2026:** Bản sửa có trong POS Admin (chặn thiếu mã đơn hoặc GUID rỗng). POS riêng chưa kiểm chứng lại.
>
> **Cập nhật 08/11/2026 (tách POS khỏi Admin):** bản sửa của POS Admin đã chuyển sang `pos-app/src/services/posApi.ts` (`checkoutPosOrder` từ chối thiếu mã đơn hoặc GUID rỗng) và là POS duy nhất. Các đường dẫn `admin-dashboard/.../posApi.ts`, `pos-app/services/api.ts` và hai `PaymentModal.tsx` nêu bên dưới là hiện trạng lúc sửa, đã bị xóa hoặc gộp còn `pos-app/src/components/pos/PaymentModal.tsx`. Test: `test-checkout.mjs`.

## Mô tả lỗi (lúc phát hiện)

**Tái hiện:** checkout nhận HTTP 200 với `{unexpected:true}` hoặc body không đọc được.

**Thực tế:** helper vẫn trả `success:true` khi thiếu định danh/mã đơn server; PaymentModal có thể dùng mã tự sinh và gọi callback xóa giỏ như thanh toán thành công.

**Ảnh hưởng:** người dùng không có chứng từ server để đối soát trong trường hợp proxy/API trả response sai contract. Chưa quan sát response sai này trên backend thật.

**Vị trí:** `posApi.ts:checkoutPosOrder`, `pos-app/services/api.ts`, hai `PaymentModal.tsx` nhánh success/fallback orderNumber.

**Bằng chứng:** `pos-malformed-success`.

**Điều kiện đóng:** validate response thành công theo contract; không giả định thành công khi thiếu identity; giữ giỏ/snapshot ở trạng thái cần đối soát, không tự tạo mã server thay thế.

## Cách đã sửa

* **Thành phần:** `src/frontend/admin-dashboard/src/services/posApi.ts`, `src/frontend/pos-app/src/services/api.ts`, và hai modal `PaymentModal.tsx`
* **Triệu chứng & Nguyên nhân:**
  * Khi gửi checkout đơn POS, nếu API trả về HTTP 200 kèm body rỗng, thiếu mã đơn hàng hoặc chứa GUID rỗng `00000000-0000-0000-0000-000000000000`, helper cũ vẫn trả `success: true`, khiến giao diện xóa giỏ hàng trong khi đơn chưa được tạo hợp lệ.
* **Giải pháp khắc phục:**
  * Validate chặt chẽ phản hồi HTTP 200: Chỉ chấp nhận thành công khi server trả về định danh đơn hàng hợp lệ (`orderNumber`, `number` hoặc `id` khác chuỗi rỗng và khác GUID rỗng `00000000-0000-0000-0000-000000000000`).
  * Nếu phản hồi sai contract, coi giao dịch là chưa xác nhận thành công, giữ nguyên giỏ hàng và `requestKey` để thu ngân đối soát hoặc thử lại an toàn.
* **Kiểm thử xác thực:** Pass kịch bản `pos-malformed-success`: phản hồi HTTP 200 thiếu định danh đơn hàng bị từ chối và báo lỗi xác thực contract.

## Kiểm chứng sau khi gộp POS (11/11/2026)

Code chặn nằm trong `src/frontend/pos-app/src/services/posApi.ts`: HTTP 200 thiếu mã đơn hoặc mã là GUID rỗng trả `isUnknown`, giữ giỏ để đối soát. Chưa có test nào trong `test-oisms/frontend/pos-app/` cho trường hợp này, nên giữ trạng thái cần kiểm chứng.
