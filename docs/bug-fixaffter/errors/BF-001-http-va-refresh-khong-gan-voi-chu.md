# BF-001 — HTTP và refresh không gắn với chủ sở hữu phiên

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa |
| **Ưu tiên** | P1 |
| **Khu vực** | Đăng nhập/HTTP |
| **Phát hiện** | 28–29/10/2026 (Tái hiện có kiểm soát) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

## Mô tả lỗi (lúc phát hiện)

**Kịch bản 1:** Phiên A đang refresh token. Người dùng đăng nhập B trước khi refresh A trả về. Phản hồi A vẫn được ghi vào localStorage và header mặc định, thay token B bằng token A.

**Kịch bản 2:** Một POST của A trả 401 muộn sau khi B đã đăng nhập. Interceptor thấy token hiện tại khác token của request, tự gửi lại POST bằng token B, không xác minh đó có còn là cùng phiên/người dùng hay không.

**Ảnh hưởng:** UI có thể hiện B nhưng HTTP sử dụng A; hoặc một ý định của A được thực hiện trong ngữ cảnh B. Backend kiểm tra tenant không thay thế được kiểm tra chủ sở hữu ý định phía client.

**Bằng chứng:** `auth-refresh-owner`, `auth-old-401-replay`. Probe chỉ dùng token fixture và HTTP điều khiển; không thực hiện giao dịch chéo tenant trên hệ thống đang chạy.

**Vị trí:** `src/frontend/admin-dashboard/src/services/httpClient.ts`, các hàm `refreshAccessToken`, response interceptor 401; liên quan `context/AuthContext.tsx` login/logout.

**Điều kiện đóng:** bind generation/actor với refresh, queue và từng request; logout/login vô hiệu kết quả cũ; không tự replay mutation của actor cũ bằng token mới. Có kiểm thử A refresh → logout → B login → A trả muộn và A POST → B login → A 401.

## Cách đã sửa

* **Thành phần:** `src/frontend/admin-dashboard/src/services/httpClient.ts`, `src/frontend/admin-dashboard/src/context/AuthContext.tsx`
* **Triệu chứng & Nguyên nhân:**
  * Kịch bản 1: Khi phiên A đang gửi request refresh token ngầm, người dùng đăng xuất và đăng nhập tài khoản B. Khi refresh của A trả về muộn (thành công hoặc thất bại), nó có thể ghi đè token A vào `localStorage` hoặc gọi `clearAuthSession()` làm mất phiên mới của B.
  * Kịch bản 2: Request POST mutation của user A bị 401 trả về muộn sau khi user B đã đăng nhập. Interceptor 401 thấy token hiện tại khác với token của request nên tự động gửi lại (replay) mutation của A bằng token của B.
* **Giải pháp khắc phục:**
  * Bổ sung biến đếm thế hệ `currentAuthGeneration` và tập hợp `sessionPreviousTokens` theo dõi chuỗi token của từng phiên; gọi `clearAuthSession()` khi `login()` thiết lập phiên mới.
  * Gắn `generation` vào từng item trong `failedQueue`. Khi `refreshAccessToken()` kết thúc ở một thế hệ cũ, ném lỗi có cờ `isStaleSession = true` và trong khối `catch` của interceptor 401 tuyệt đối không gọi `clearAuthSession()` nếu lỗi thuộc về phiên cũ.
  * Trong interceptor 401: Chỉ cho phép replay với các phương thức đọc an toàn (`GET`, `HEAD`, `OPTIONS`) hoặc khi token cũ nằm trong chuỗi kế thừa `sessionPreviousTokens` của chính phiên hiện tại.
* **Kiểm thử xác thực:** Pass kịch bản 1 (`auth-refresh-owner`) và kịch bản 2 (`auth-old-401-replay`).
