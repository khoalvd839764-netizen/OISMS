# BF-020 — POS riêng chưa khóa giỏ và ngữ cảnh khi checkout pending

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | 🟡 Cần kiểm chứng lại |
| **Ưu tiên** | P1 |
| **Khu vực** | POS riêng 5174 |
| **Phát hiện** | 28–29/10/2026 (Tái hiện có kiểm soát) |
| **Sửa** | 30/10/2026 trên code POS cũ; bản sửa không được đưa vào `develop` |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

> **Hiện trạng 06/11/2026:** Bản sửa cũ (khóa giỏ bằng `cartVersion`, `currentBranchIdRef`, `onBusyChange`) **không** có trên nhánh hiện tại vì xung đột với POS mới của `develop`. `pos-app/src/App.tsx` hiện có cơ chế pending checkout theo người dùng và chi nhánh (`getPendingCheckout`), nhưng chưa chạy lại kịch bản `separate-pos-old-checkout` trên code này.

## Mô tả lỗi (lúc phát hiện)

**Kịch bản đã chạy:** POS riêng cổng 5174 bắt đầu checkout A → request chưa trả → bấm Escape để đóng modal → chuyển sang chi nhánh B, thêm hàng vào giỏ mới → request A trả thành công → giỏ B bị xóa.

**Thực tế:** busy chỉ nằm trong PaymentModal, không truyền lên App. Header, branch/logout và các callback giỏ không có context lock/generation tương ứng; callback `onSuccessAndPrint` gọi `setCart([])` không kiểm tra cart/actor đã thay đổi hay chưa.

**Ảnh hưởng:** phản hồi của giao dịch cũ có thể xóa giỏ hoặc hiển thị hóa đơn trong ngữ cảnh mới. POS tích hợp đã có `checkoutBusy`; không đồng nhất hai implementation.

**Vị trí:** `src/frontend/pos-app/src/App.tsx` thao tác giỏ/chi nhánh/logout và callback `onSuccessAndPrint`; `components/PaymentModal.tsx`.

**Bằng chứng:** `separate-pos-old-checkout`, thực thi App và PaymentModal thật; chỉ catalog entry và HTTP được thay bằng fixture.

**Điều kiện đóng:** snapshot theo cart/branch/actor, khóa hoặc tách giỏ pending; response cũ không làm mất giỏ mới. Test deferred checkout → sửa/chuyển phiên → trả kết quả, cùng retry của BF-003.

## Cách đã sửa (bản 30/10/2026, không còn trên nhánh hiện tại)

* **Thành phần:** `src/frontend/pos-app/src/App.tsx`, `src/frontend/pos-app/src/components/PaymentModal.tsx`
* **Triệu chứng & Nguyên nhân:**
  * Tại POS app độc lập: Khi request thanh toán ở chi nhánh A đang chạy hoặc chưa rõ kết quả, nếu người dùng đóng modal, đổi sang chi nhánh B và tạo giỏ hàng mới, phản hồi muộn của chi nhánh A hoàn tất sẽ xóa trắng giỏ hàng mới của chi nhánh B và hiển thị đè hóa đơn cũ.
* **Giải pháp khắc phục:**
  * Kết nối callback `onBusyChange={setCheckoutBusy}` để khóa thao tác đổi ngữ cảnh chi nhánh/sửa giỏ hàng khi giao dịch thanh toán đang in-flight.
  * Sử dụng `currentBranchIdRef` và `cartVersionRef` truyền `checkoutBranchId` và `checkoutVersion` vào `PaymentModal`. Trong `onSuccessAndPrint`, kiểm tra đối soát: nếu `checkoutBranchId !== currentBranchIdRef.current` hoặc `checkoutVersion !== cartVersionRef.current`, tuyệt đối không xóa giỏ hàng mới và không ghi đè `printedReceipt` của ngữ cảnh mới.
* **Kiểm thử xác thực:** Pass kịch bản `separate-pos-old-checkout`: khi request cũ của chi nhánh A trả về muộn, giỏ hàng mới trên chi nhánh B được bảo toàn 100%.
