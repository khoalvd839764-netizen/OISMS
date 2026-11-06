# BF-003 — POS đổi request key khi thử lại thanh toán

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | 🟡 Đã sửa, cần kiểm chứng lại |
| **Ưu tiên** | P1 |
| **Khu vực** | POS |
| **Phát hiện** | 28–29/10/2026 (Tái hiện có kiểm soát) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

> **Hiện trạng 06/11/2026:** Bản sửa có trong POS Admin (`requestKey` theo chữ ký giỏ, lưu `sessionStorage`). POS riêng (`pos-app`) đang dùng code `develop` với cơ chế pending checkout riêng, chưa chạy lại kịch bản `pos-retry-key`.

## Mô tả lỗi (lúc phát hiện)

**Tái hiện:** Thanh toán tiền mặt → request có thể đã commit nhưng client nhận lỗi mất phản hồi → giữ nguyên giỏ rồi bấm thanh toán lại. `handleCheckout` tạo UUID mới mỗi lần.

**Ảnh hưởng:** backend không thể nhận lần sau là replay của cùng ý định; có thể tạo hai đơn và ghi xuất tồn hai lần nếu lần đầu đã commit. Chống double click trong lúc đang gửi không giải quyết retry sau lỗi.

**Vị trí:** `admin-dashboard/src/components/pos/PaymentModal.tsx:handleCheckout` và `pos-app/src/components/PaymentModal.tsx:handleCheckout`.

**Bằng chứng:** `pos-retry-key`: hai lần gửi cùng giỏ có hai key khác nhau. Chưa cố tình tạo đơn trùng trên DB đang dùng.

**Điều kiện đóng:** lưu snapshot giỏ/chi nhánh/actor/key trước POST; kết quả unknown giữ nguyên key/payload qua retry/reload; đối soát đơn trước khi tạo ý định mới. Test response lost after commit trên DB test: một đơn, một lần trừ tồn.

## Cách đã sửa

* **Thành phần:** `src/frontend/admin-dashboard/src/components/pos/PaymentModal.tsx`, `src/frontend/pos-app/src/components/PaymentModal.tsx`
* **Triệu chứng & Nguyên nhân:**
  * Khi bấm thanh toán đơn hàng, nếu xảy ra lỗi timeout hoặc mất kết nối mạng, người dùng bấm thử lại hoặc đóng/mở lại modal thanh toán cho cùng giỏ hàng. Mã cũ sinh UUID mới khiến backend coi đây là 2 giao dịch riêng biệt.
* **Giải pháp khắc phục:**
  * Tạo chữ ký `cartSignature` dựa trên chi nhánh, thu ngân và danh sách mặt hàng/số lượng/đơn giá trong giỏ.
  * Lưu trữ cặp `{ signature, key }` đồng thời trong `checkoutKeyRef` và `sessionStorage`. Mọi lần submit hoặc retry cùng một giỏ hàng (kể cả khi đóng/mở lại modal) đều tái sử dụng đúng `requestKey` đó. Khi giỏ hàng thay đổi hoặc sau khi thanh toán thành công, khóa cũ được xóa để cấp khóa mới cho giao dịch mới.
* **Kiểm thử xác thực:** Pass kịch bản `pos-retry-key`: hai lần submit liên tiếp khi retry gửi cùng một `requestKey` duy nhất lên API.
