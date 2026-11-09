# BF-015 — POS xử lý thiếu lỗi hết phiên và ProblemDetails

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | 🟡 Đã sửa, cần kiểm chứng lại |
| **Ưu tiên** | P2 |
| **Khu vực** | POS |
| **Phát hiện** | 28–29/10/2026 (Tái hiện có kiểm soát) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

> **Hiện trạng 06/11/2026:** Bản sửa có trong `admin-dashboard/src/services/posApi.ts`. `pos-app/src/services/api.ts` có xử lý 401 và phát `pos-auth-expired`, nhưng chưa chạy lại kịch bản trên code mới.
>
> **Cập nhật 08/11/2026 (tách POS khỏi Admin):** chỉ còn một POS tại `src/frontend/pos-app`. Bản sửa nằm ở `pos-app/src/services/posApi.ts` (mọi nhánh 401 phát `pos-auth-expired`) cùng `posSession.ts` (refresh token, generation guard). `admin-dashboard/src/services/posApi.ts` và `pos-app/src/services/api.ts` nêu bên dưới đã bị xóa; phần "Cách đã sửa" giữ theo thời điểm sửa. Test: `test-auth.mjs`, `test-checkout.mjs`.

## Mô tả lỗi (lúc phát hiện)

**Tái hiện:** Checkout trả HTTP 401 với `detail` mô tả hết phiên.

**Thực tế:** helper trả lỗi chung theo HTTP, giữ token hết hạn và không phát `pos-auth-expired`. Các lỗi nghiệp vụ cũng chỉ đọc `.message`, bỏ `.detail/errorCode` của backend. Nhánh search/barcode có xử lý 401, nhưng checkout không có.

**Vị trí:** `admin-dashboard/src/services/posApi.ts:checkoutPosOrder`; `pos-app/src/services/api.ts:checkoutPosOrder`.

**Bằng chứng:** `pos-checkout-401` trên helper Admin; source POS riêng có cùng cấu trúc xử lý.

**Điều kiện đóng:** phân biệt 400/401/403/409/network, hiển thị đúng detail/errorCode; reauth không mất snapshot cần đối soát và không replay bằng actor khác. Test cả hai POS.

## Cách đã sửa

* **Thành phần:** `src/frontend/admin-dashboard/src/services/posApi.ts`, `src/frontend/pos-app/src/services/api.ts`, `src/frontend/pos-app/src/App.tsx`
* **Triệu chứng & Nguyên nhân:**
  * Trong cả hai module POS, hàm `checkoutPosOrder` khi gặp lỗi HTTP 401 chỉ quăng lỗi chung chung, không xóa token hết hạn, không phát sự kiện `pos-auth-expired`, bỏ qua `detail`/`errorCode` trong `ProblemDetails`, và tại POS app độc lập có nguy cơ làm mất giỏ hàng của thu ngân khi phiên hết hạn.
* **Giải pháp khắc phục:**
  * Khi gặp HTTP 401, xóa token hết hạn và phát sự kiện `window.dispatchEvent(new CustomEvent('pos-auth-expired'))`, đồng thời tại `pos-app/src/App.tsx` giữ nguyên `cart` khi hết phiên để thu ngân đăng nhập lại tiếp tục thanh toán.
  * Trích xuất đầy đủ `detail`, `message` và `errorCode` từ cấu trúc chuẩn RFC 7807 `ProblemDetails`.
* **Kiểm thử xác thực:** Pass kịch bản `pos-checkout-401`: token hết hạn bị xóa sạch, sự kiện hết phiên được phát, giỏ hàng được bảo toàn và thông báo chi tiết của backend được hiển thị.
