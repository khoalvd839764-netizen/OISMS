# Báo cáo sau nâng cấp · Quên mật khẩu và đăng nhập bằng SĐT (Upgrade 10)

| Thông tin | Giá trị |
| :--- | :--- |
| Đặc tả | [10-quen-mat-khau-dang-nhap-sdt.md](../Upgrade-recommended/Done/10-quen-mat-khau-dang-nhap-sdt.md) (mục "Quyết định đã chốt") |
| Ngày | 15/11/2026 |
| Baseline | `feature/OISMS-59-hoan-thanh-nghiep-vu-con-lai` tại `f3b8a27` |
| Phạm vi | Backend, Admin, POS, cấu hình deploy, tài liệu. Không đổi schema (vẫn 48 bảng) |

## Đã làm

- **Đăng nhập bằng email hoặc SĐT.** `POST /api/auth/login` nhận `identifier`. Chuỗi có `@` là email, còn lại chuẩn hóa SĐT sang E.164 bằng `PhoneHelper` (`0901234567`, `090 123 4567`, `+84 90 123 4567` đều thành `+84901234567`). Trường `email` cũ vẫn nhận để client và test cũ không vỡ. Sai tài khoản và sai mật khẩu trả cùng `401` với cùng thông báo. Khi không tìm thấy người dùng, backend vẫn chạy BCrypt với một hash giả để thời gian phản hồi không lộ tài khoản.
- **Owner đặt lại mật khẩu hộ** Staff/Cashier cùng cửa hàng: `POST /api/users/{id}/reset-password`. Tài khoản Owner trả `403 CANNOT_RESET_OWNER_PASSWORD`; người dùng cửa hàng khác trả `404` nhờ query filter tenant. Ghi hash mới và thu hồi mọi refresh token trong cùng một `SaveChanges`. Admin có nút "Đặt lại mật khẩu" ở tab Nhân viên.
- **Quên mật khẩu qua link email.**
  - `POST /api/auth/forgot-password` luôn trả `202` với cùng thông báo.
  - Tài khoản và cửa hàng đang hoạt động, có email: mail chứa link `<AdminBaseUrl|PosBaseUrl>/reset-password#token=...` được xếp vào hàng đợi trong bộ nhớ, gửi ở nền (`EmailDispatchService`), nên API trả về ngay dù có gửi mail hay không.
  - Nhập SĐT vẫn được: mail gửi vào email của tài khoản đó.
  - `POST /api/auth/reset-password` mở transaction, khóa dòng `users` (`ISqlLockService.LockUserAsync`, `FOR UPDATE`), đọc lại người dùng, so dấu vân tay, ghi hash mới và thu hồi refresh token rồi commit.
  - Admin và POS mỗi app có trang `/forgot-password` và `/reset-password` riêng. Trang đặt lại đọc token sau dấu `#` rồi xóa khỏi thanh địa chỉ.
- **Rate limit theo IP** (sau `UseForwardedHeaders`): đăng nhập và đặt lại 10 lần/phút, quên mật khẩu 5 lần/15 phút, trả `429 TOO_MANY_REQUESTS`. Policy `AiChat` giữ nguyên mã `AI_RATE_LIMITED`.
- `GET /api/auth/password-reset/status` cho frontend biết tính năng có bật không. Khi tắt, nút Quên mật khẩu đổi thành dòng nhắc liên hệ chủ cửa hàng.

## Thiết kế token (không thêm bảng)

Token là base64url của `userId (16 byte) | hạn dùng unix (8) | dấu vân tay (16) | chữ ký HMAC-SHA256 (32)`.

- Dấu vân tay là HMAC của `password_hash` hiện tại. Đặt lại mật khẩu xong thì hash đổi, nên token vừa dùng và mọi token cũ đều hết hiệu lực. Không cần lưu token hay cột `used_at`.
- Hai request đặt lại cùng lúc: request sau chờ khóa dòng `users`, đọc lại hash mới, dấu vân tay không khớp, trả `400 RESET_TOKEN_INVALID`.
- Khóa ký `PasswordReset:SigningKey` tối thiểu 32 ký tự, khác `Jwt:Secret`. Development không đặt thì tự sinh khóa tạm theo tiến trình.
- Đánh đổi đã chấp nhận: gửi yêu cầu lần hai không vô hiệu link lần một cho đến khi có link được dùng hoặc hết 30 phút; không có lịch sử yêu cầu trong DB (chỉ có log). Khi làm OTP SMS cần đếm số lần nhập sai thì mới thêm bảng.

## Bảo mật và quy tắc dự án

| Quy tắc | Cách giữ |
| :--- | :--- |
| Tenant isolation | Luồng chưa có JWT (login, forgot, reset) dùng `OpenPrivilegedScope` + `IgnoreQueryFilters`, tìm theo email/SĐT duy nhất toàn hệ thống. Owner đặt hộ đi qua query filter tenant. |
| Transaction và khóa | Reset bằng token chạy trong `ExecuteWithRetryAsync` + transaction, khóa sau khi mở transaction. BCrypt chạy trước khi mở transaction để không giữ khóa lâu. |
| Không lộ tài khoản | Forgot luôn `202` cùng nội dung; mail gửi nền; login dùng hash giả khi không có người dùng. |
| Link không bị giả | URL frontend chỉ lấy từ cấu hình (`PasswordReset:AdminBaseUrl`, `PosBaseUrl`), client chỉ chọn `admin` hoặc `pos`. |
| Token không vào log | Token nằm sau `#` nên trình duyệt không gửi lên server hay Caddy. `LogEmailTransport` chỉ dùng ở Development. Lỗi gửi mail chỉ ghi tiêu đề, không ghi nội dung. |
| Phiên cũ | Thu hồi refresh token; access token cũ còn tối đa 60 phút (chấp nhận theo Q2). |

## Cấu hình và deploy

Không có migration. Bản demo chỉ cần thêm biến trong `.env.production` rồi build lại backend:

| Biến | Ghi chú |
| :--- | :--- |
| `PASSWORD_RESET_SIGNING_KEY` | `openssl rand -hex 32`, khác `JWT_SECRET` |
| `SMTP_HOST`, `SMTP_PORT` | `smtp.gmail.com`, `587` |
| `SMTP_USER`, `SMTP_FROM` | Gmail của hệ thống |
| `SMTP_PASSWORD` | Gmail App Password (tài khoản bật xác thực 2 bước). Không commit |

`docker-compose.prod.yml` tự đặt `PasswordReset__AdminBaseUrl` và `PosBaseUrl` theo `ADMIN_DOMAIN`, `POS_DOMAIN`. Thiếu khóa ký hoặc SMTP thì tính năng quên mật khẩu tắt, đăng nhập SĐT và Owner đặt hộ vẫn chạy.

## Kiểm thử

Chạy ngày 15/11/2026 trên máy dev, PostgreSQL 16 trong container `oism_postgres` (fixture tạo DB test riêng).

| Lệnh | Kết quả |
| :--- | :--- |
| `dotnet build src/backend/OISM.sln -p:UseAppHost=false` | PASS |
| `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj` | PASS 403/403 (có `PasswordResetUnitTests`: chuẩn hóa email/SĐT, validator, token đổi chữ ký hoặc khóa thì bị từ chối, tắt khi thiếu khóa ngoài Development) |
| `dotnet test ...OISM.IntegrationTests.csproj` (toàn bộ) | 442/446. 4 FAIL ở `InventoryAndWarehouseRbacIntegrationTests` do hồi quy: so email đăng nhập phân biệt hoa thường trong khi test nạp email chữ hoa thẳng vào DB. Đã sửa, trả lại so sánh không phân biệt hoa thường như trước |
| `dotnet test ... --filter "InventoryAndWarehouseRbac\|PasswordReset\|AuthIntegrationTests\|AuthStaleToken"` sau khi sửa | PASS 24/24 |
| `npm run build --prefix src/frontend/admin-dashboard` | PASS |
| `npm run build`, `npm run lint`, `npm test` (`pos-app`) | PASS, test 94/94; lint không có cảnh báo ở file mới |

Integration test Upgrade 10:

- `PasswordResetIntegrationTests` (InMemory, 8 test): đăng nhập bằng SĐT nội địa và có dấu cách, trường `email` cũ; SĐT không tồn tại và sai mật khẩu trả cùng nội dung; forgot với tài khoản không tồn tại trả `202` giống tài khoản có thật và không gửi mail; luồng POS (link về `PosBaseUrl`), reset xong refresh token cũ `401`, dùng lại token `400 RESET_TOKEN_INVALID`; token bị sửa; Owner đặt hộ Staff/Cashier, chặn Owner, chặn Staff gọi, chặn tenant khác (`404`); thiếu cấu hình trả `503`; quá giới hạn đăng nhập trả `429`.
- `PostgresPasswordResetIntegrationTests` (PostgreSQL thật, 4 test): 6 request đặt lại đồng thời cùng token chỉ 1 thành công, 5 trả `RESET_TOKEN_INVALID`, chỉ còn refresh token của lần đăng nhập mới; link yêu cầu lần hai dùng xong thì link lần một hết hiệu lực; tài khoản bị khóa trả `403 USER_INACTIVE` và không nhận mail; Owner đặt hộ thu hồi refresh token.

Kiểm tay trên trình duyệt (backend và DB tạm, đã xóa sau khi kiểm): Admin quên mật khẩu bằng SĐT, mail ghi ra log tới email của tài khoản, mở link thì token bị xóa khỏi thanh địa chỉ, đặt mật khẩu mới thành công; Owner đăng nhập bằng SĐT, đặt lại hộ Thu ngân; POS đăng nhập bằng SĐT với mật khẩu mới; POS gửi quên mật khẩu thì link trong mail trỏ về POS.

NOT RUN: gửi mail Gmail thật trên EC2; `npm run test:live` của POS.

## Chưa làm

- Giai đoạn 3: tài khoản không có email, OTP qua SMS (cần nhà cung cấp trả phí).
- Chưa thử gửi mail Gmail thật trên EC2; mới kiểm bằng kênh gửi giả trong test và ghi log ở Development.
- Thông báo mail tiếng Việt dạng đơn giản, chưa có template HTML theo nhận diện thương hiệu.
