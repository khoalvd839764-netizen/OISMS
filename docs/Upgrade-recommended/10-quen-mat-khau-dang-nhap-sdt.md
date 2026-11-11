# 10. Quên mật khẩu và đăng nhập bằng số điện thoại

> Trạng thái: kế hoạch của nhóm, soạn ngày 09/11/2026, **chưa triển khai, các quyết định chưa chốt**. Đây không phải đề xuất của GVHD. Thay thế nút "Quên mật khẩu? Coming soon" hiện có ở Admin và POS.

## Mục tiêu

1. Người dùng đăng nhập bằng **email hoặc số điện thoại** cùng mật khẩu.
2. Người dùng quên mật khẩu tự đặt lại qua **link gửi vào email**.
3. Để ngỏ đường mở rộng: tài khoản không có email, đặt lại mật khẩu bằng **mã OTP qua SMS**.

## Hiện trạng (kiểm tra 09/11/2026)

| Nội dung | Hiện trạng |
| :--- | :--- |
| Cột đăng nhập trong `users` | `email` và `phone` đều cho phép NULL nhưng CHECK `ck_users_login_identity` bắt buộc có ít nhất một. Cả hai **duy nhất toàn hệ thống** (`uq_users_2`, `uq_users_3`), nên tìm theo email hoặc SĐT không cần mã cửa hàng. |
| Chuẩn hóa | Email lưu chữ thường, đã trim (`ck_users_email_normalized`). SĐT lưu dạng E.164 (`ck_users_phone_normalized`), chuẩn hóa bằng `PhoneHelper.TryNormalizePhoneNumber` (`0901234567` → `+84901234567`). |
| Đăng nhập | `LoginRequest` chỉ có `Email`, `Password`, `TenantCode` (không dùng). `LoginValidator` bắt buộc định dạng email. `AuthService.LoginAsync` chỉ tìm theo email. |
| Tạo tài khoản | `RegisterTenantValidator` và `CreateUserValidator` bắt buộc email; SĐT tùy chọn, đã kiểm tra trùng (`PHONE_ALREADY_EXISTS`). Thực tế mọi tài khoản hiện có đều có email. |
| Frontend | Admin `AuthPage.tsx` và POS `LoginPage.tsx` có ô "Email". POS gọi `loginPos(email, password)` trong `posSession.ts`. Nút Quên mật khẩu bị tắt, nhãn Coming soon. |
| Gửi mail, SMS | Chưa có. Backend không có SMTP, SES hay nhà cung cấp SMS. |
| Chống dò mật khẩu | Chưa có rate limiting ở backend. |

## Quyết định cần chốt

| # | Câu hỏi | Đề xuất |
| :--- | :--- | :--- |
| Q1 | Gửi mail bằng gì trên bản demo EC2 | Gmail SMTP với App Password (miễn phí, đủ cho demo). Development chỉ ghi link ra log. |
| Q2 | Sau khi đặt lại mật khẩu, access token cũ còn hạn tối đa 60 phút | Chấp nhận. Chỉ thu hồi refresh token. Nếu muốn chặn ngay thì thêm cột `users.security_stamp` (giai đoạn sau). |
| Q3 | Owner đặt lại mật khẩu hộ nhân viên | Có, làm trong giai đoạn 2 vì ít việc và cần cho tài khoản không nhận được mail. |
| Q4 | Có cho tạo tài khoản không có email không (ví dụ Cashier chỉ có SĐT) | Chưa. Giữ email bắt buộc đến khi làm giai đoạn 3. |
| Q5 | Có làm OTP qua SMS không | Để giai đoạn 3, tùy chọn. Cần nhà cung cấp SMS trả phí (eSMS, SpeedSMS, Twilio). |

## Luồng

### Đăng nhập

```
Người dùng nhập "Email hoặc số điện thoại" + mật khẩu
  → POST /api/auth/login { identifier, password }
  → Có "@": chuẩn hóa email, tìm users.email
    Không có "@": PhoneHelper chuẩn hóa sang E.164, tìm users.phone
  → Không tìm thấy hoặc sai mật khẩu: cùng lỗi 401 INVALID_CREDENTIALS
  → Đúng: cấp access token + refresh token như hiện tại
```

### Quên mật khẩu qua email

```
① /forgot-password: nhập email hoặc SĐT
② POST /api/auth/forgot-password { identifier }
   → luôn trả 202 với cùng một câu thông báo
   → nếu tìm thấy người dùng đang hoạt động, có email, cửa hàng đang hoạt động:
       vô hiệu token cũ chưa dùng, tạo token mới (lưu hash, hạn 30 phút),
       gửi mail link {FrontendBaseUrl}/reset-password?token=...
③ Người dùng bấm link trong mail
④ POST /api/auth/reset-password { token, newPassword }
   → trong một transaction: khóa dòng token, kiểm tra còn hạn và chưa dùng,
     ghi hash mật khẩu mới, đánh dấu used_at, thu hồi mọi refresh token của người dùng
⑤ Quay lại đăng nhập bằng mật khẩu mới
```

Nhập SĐT ở bước ① vẫn được: hệ thống tìm theo SĐT rồi gửi vào **email của tài khoản đó**. Màn hình không hiện email đích để tránh lộ thông tin.

## Kế hoạch triển khai

### Giai đoạn 1: Đăng nhập bằng email hoặc SĐT

Không đổi schema.

**Backend**
- `LoginRequest`: thêm `Identifier`. Giữ `Email` một thời gian để client cũ và test cũ không vỡ: `Identifier` rỗng thì lấy `Email`.
- `LoginValidator`: bỏ rule `EmailAddress()`. Rule mới: có `@` thì phải là email hợp lệ, không có `@` thì `PhoneHelper.IsValidPhoneNumber` phải đúng.
- Thêm helper `LoginIdentifier.Resolve(raw)`, trả về `(Kind: Email|Phone, Normalized)`, dùng chung cho đăng nhập và quên mật khẩu.
- `AuthService.LoginAsync`: tìm theo `Email` hoặc `Phone` tùy loại. Giữ nguyên thông báo `INVALID_CREDENTIALS` cho mọi trường hợp sai.
- Thêm rate limiting (`Microsoft.AspNetCore.RateLimiting`) cho `/api/auth/login`: khoảng 10 lần/phút mỗi IP, trả 429 `TOO_MANY_REQUESTS`.

**Frontend**
- Admin `AuthPage.tsx` và POS `LoginPage.tsx`: đổi nhãn ô thành "Email hoặc số điện thoại", `type="text"`, `autocomplete="username"`. Gửi `identifier`.
- `posSession.ts`: `loginPos(identifier, password)`.

**Test**
- Unit: `LoginIdentifier.Resolve` với `0901234567`, `+84 90 123 4567`, `84901234567`, email in hoa, chuỗi rác.
- Integration: đăng nhập bằng email, bằng SĐT dạng nội địa, SĐT sai, SĐT không tồn tại (cùng mã lỗi với sai mật khẩu), request cũ chỉ có `email` vẫn chạy.

### Giai đoạn 2: Quên mật khẩu qua email

Thêm **1 bảng**, từ 44 lên 45 bảng.

**B2.1. Migration**

```sql
CREATE TABLE password_reset_tokens (
  id             uuid PRIMARY KEY,
  tenant_id      uuid NOT NULL,
  user_id        uuid NOT NULL,
  channel        varchar(10) NOT NULL DEFAULT 'Email',  -- 'Email' | 'Sms', để sẵn cho giai đoạn 3
  token_hash     varchar(128) NOT NULL,
  expires_at     timestamptz NOT NULL,
  used_at        timestamptz,
  attempt_count  integer NOT NULL DEFAULT 0,            -- dùng cho OTP ở giai đoạn 3
  created_at     timestamptz NOT NULL DEFAULT now(),
  requested_ip   varchar(45),
  CONSTRAINT uq_password_reset_tokens_1 UNIQUE (tenant_id, id),
  CONSTRAINT uq_password_reset_tokens_2 UNIQUE (token_hash),
  CONSTRAINT fk_password_reset_tokens_user FOREIGN KEY (tenant_id, user_id) REFERENCES users (tenant_id, id),
  CONSTRAINT ck_password_reset_tokens_channel CHECK (channel IN ('Email', 'Sms')),
  CONSTRAINT ck_password_reset_tokens_expiry CHECK (expires_at > created_at)
);
CREATE INDEX ix_password_reset_tokens_user ON password_reset_tokens (user_id, created_at DESC);
```

Thêm cột `channel` và `attempt_count` ngay từ đầu để giai đoạn 3 không phải chạy migration lần nữa.

Các chỗ phải sửa theo quy tắc schema của dự án:
1. File mới `docker/postgres/migrations/2026MMDD_password_reset_tokens.sql`, có `GRANT` cho `oisms_runtime`.
2. `docs/database/oisms_schema.sql` và `docs/database/README.md`.
3. Entity `PasswordResetToken`, cấu hình EF trong `OISM.Infrastructure/Persistence/Configurations`, query filter `tenant_id` trong `AppDbContext`, `DbSet` trong `IAppDbContext`.
4. `RequiredTables` trong `DatabaseMigrationService` (`Program.cs` không còn danh sách riêng).
5. Số bảng kiểm tra trong `docker/postgres/init-oisms.sh` (44 → 45).
6. Các tài liệu còn ghi "44 bảng", gồm `CLAUDE.md`.

**B2.2. Backend**
- Application `Common/Interfaces/IEmailSender.cs`: `SendAsync(to, subject, htmlBody, ct)`.
- Infrastructure:
  - `SmtpEmailSender` đọc `Email:Host`, `Email:Port`, `Email:User`, `Email:Password`, `Email:From`;
  - `LogEmailSender` cho Development, chỉ ghi nội dung ra log;
  - chọn bản cài đặt theo cấu hình trong `DependencyInjection.cs`.
- `ISqlLockService.LockPasswordResetTokenAsync(tokenHash)`: `SELECT … FOR UPDATE`.
- `Features/Auth/PasswordReset/`: `ForgotPasswordRequest`, `ResetPasswordRequest`, validator. Mật khẩu mới tối thiểu 6 ký tự, tối đa 100, giống `CreateUserValidator`.
- `AuthService.ForgotPasswordAsync`:
  - dùng `OpenPrivilegedScope` + `IgnoreQueryFilters` vì chưa có JWT;
  - token là 32 byte ngẫu nhiên dạng base64url, chỉ lưu SHA-256;
  - đánh dấu `used_at` cho các token cũ chưa dùng của người dùng đó;
  - gửi mail **sau khi commit**; gửi lỗi thì ghi log, vẫn trả 202.
- `AuthService.ResetPasswordAsync`: chạy trong `ExecuteWithRetryAsync` + transaction, lấy khóa sau khi mở transaction, theo mẫu `RefreshTokenAsync`.
- `PasswordReset:FrontendBaseUrl` và `PasswordReset:TokenLifetimeMinutes` (mặc định 30) lấy **từ cấu hình**, không dựng link từ header `Host`.
- `AuthController`:
  - `POST /api/auth/forgot-password`: `[IgnoreTenantClaim]`, rate limit khoảng 5 lần / 15 phút mỗi IP;
  - `POST /api/auth/reset-password`: `[IgnoreTenantClaim]`.
- Owner đặt lại hộ (Q3): `POST /api/users/{id}/reset-password { newPassword }` trong `UsersController` (`[Authorize(Roles = "Owner")]`), tìm người dùng qua query filter tenant, thu hồi refresh token của người đó.
- Mã lỗi mới: `RESET_TOKEN_INVALID` (400, dùng chung cho không tồn tại, hết hạn, đã dùng), `TOO_MANY_REQUESTS` (429). Thêm vào `docs/12-error-and-status-codes/`.

**B2.3. Frontend**
- Admin: bật nút Quên mật khẩu, thêm route `/forgot-password` và `/reset-password` (không cần đăng nhập). Trang đặt lại đọc `token` từ URL, rồi xóa khỏi thanh địa chỉ bằng `history.replaceState`.
- POS: nút Quên mật khẩu mở `<ADMIN_URL>/forgot-password`, để chỉ có một nơi xử lý.
- Trang Nhân viên: nút "Đặt lại mật khẩu" cho Owner.

**B2.4. Test**
- Unit: hash token; validator; `ForgotPasswordAsync` trả cùng một kết quả khi email có và không tồn tại.
- PostgreSQL thật:
  - hai request reset cùng một token chạy đồng thời, chỉ một request thành công;
  - token hết hạn hoặc đã dùng trả `RESET_TOKEN_INVALID`;
  - yêu cầu lần hai làm token lần một mất hiệu lực;
  - sau khi reset, refresh token cũ trả 401 và đăng nhập được bằng mật khẩu mới;
  - người dùng hoặc cửa hàng bị khóa thì không đặt lại được;
  - Owner cửa hàng A không đặt lại được mật khẩu người dùng cửa hàng B.

### Giai đoạn 3 (tùy chọn): Tài khoản không có email và OTP qua SMS

Chỉ làm khi chốt Q4 và Q5.

- Cho phép `CreateUserRequest.Email` rỗng nếu có `Phone`. Đổi `UserResponse.Email`, `UserProfileDto.Email` sang nullable. Frontend hiển thị `fullName` hoặc SĐT khi không có email (`PosAuthContext.normalize` đang dùng email làm tên dự phòng).
- `ISmsSender` trong Application, cài đặt theo nhà cung cấp trong Infrastructure; Development chỉ ghi log.
- Quên mật khẩu bằng SĐT của tài khoản không có email:
  - gửi OTP 6 chữ số, hạn 5 phút;
  - lưu hash vào `password_reset_tokens` với `channel = 'Sms'`;
  - nhập sai thì tăng `attempt_count`, quá 5 lần thì token mất hiệu lực.
- Thêm `POST /api/auth/verify-otp { phone, otp }`: trả một reset token ngắn hạn, rồi dùng tiếp `reset-password` như giai đoạn 2.
- Rate limit theo SĐT, ví dụ tối đa 3 SMS/giờ, vì mỗi SMS tốn tiền.

## Rủi ro và lưu ý

- Thay đổi chạm auth, tenant, migration và transaction đều là **rủi ro cao**. Phải có bằng chứng trên PostgreSQL thật, InMemory không đủ.
- Bản demo phải chạy migration giai đoạn 2 **trước** khi deploy backend mới. Nếu không, `/health/ready` trả 503 "schema is incomplete".
- Không ghi token, link đặt lại hay mật khẩu vào log ở Production. `LogEmailSender` chỉ bật ở Development.
- Thông tin SMTP đặt qua biến môi trường. Không commit vào `appsettings*.json` hay `.env`.
- Sau khi xong, cập nhật `FR-AUTH` trong `docs/02-functional-requirements/README.md`, `docs/11-ui-screens/01-auth-screens.md`, `docs/05-api-contracts/`, `docs/bug-fixaffter/CHECKLIST.md` và báo cáo frontend.

## Thứ tự đề xuất

Giai đoạn 1 → Giai đoạn 2 (B2.1 → B2.2 → test PostgreSQL → B2.3 → docs) → Giai đoạn 3 khi đã chốt.
