# 11. Mail chào mừng Chủ shop

> Trạng thái: đề xuất của nhóm, **đã triển khai ngày 18/11/2026**. Đây không phải đề xuất của GVHD. [Báo cáo triển khai](../../report-after-upgrade/mail-chao-mung-chu-shop-u11.md)

## Mục tiêu

Chủ shop vừa đăng ký cửa hàng nhận một email xác nhận cửa hàng đã tạo xong, có link đăng nhập và các bước bắt đầu.

## Quyết định đã chốt (18/11/2026)

| # | Quyết định |
| :--- | :--- |
| Người nhận | **Chỉ tài khoản `Owner` tự đăng ký** qua `POST /api/auth/register-tenant`. `Staff` và `Cashier` do Owner tạo tại `POST /api/users` không nhận thư. Dữ liệu seed demo không phát sinh thư. |
| Nội dung | Tên Chủ shop, tên cửa hàng, email đăng nhập, nút đăng nhập Admin, ba bước bắt đầu. **Không bao giờ chứa mật khẩu.** |
| Mã cửa hàng | Không đưa vào thư. `Tenant.Code` không được lưu xuống DB (`builder.Ignore(t => t.Code)`, bảng `tenants` không có cột `code`) và không dùng để đăng nhập. |
| Ba bước bắt đầu | Hướng dẫn Chủ shop tự tạo chi nhánh và kho đầu tiên, vì đăng ký hiện chỉ tạo Tenant và Owner. |
| Thời điểm gửi | Sau khi transaction đăng ký commit và ngoài vòng `ExecuteWithRetryAsync`: rollback không phát sinh thư, retry không gửi trùng. |
| Khi lỗi | Thiếu SMTP, URL Admin sai, hàng đợi đầy hoặc SMTP lỗi đều không làm hỏng đăng ký, API vẫn trả `201`. Log chỉ ghi `UserId`. |
| Hạ tầng | Dùng lại hàng đợi `IEmailSender`, `EmailDispatchService` và nhận diện `EmailBranding` của Upgrade 10. Không thêm bảng, không thêm biến môi trường. |
| Link đăng nhập | `<PasswordReset:AdminBaseUrl>/login`, chỉ lấy từ cấu hình, không dựng từ header hay dữ liệu client. |
| Chống lạm dụng | Rate limit `register-tenant` 10 lần/15 phút mỗi IP (`RateLimiting:RegisterTenantPer15Minutes`), vượt hạn mức trả `429 TOO_MANY_REQUESTS`. |

## Luồng

```text
POST /api/auth/register-tenant
  → Kiểm tra dữ liệu, email và SĐT trùng
  → Transaction: tạo Tenant + Owner → COMMIT
  → IOwnerWelcomeNotifier.Notify: xếp thư vào hàng đợi trong bộ nhớ
  → Trả 201 ngay
        ↓ (nền)
  EmailDispatchService gửi qua SMTP, Development không có SMTP thì ghi log
```

## Thành phần

| Thành phần | Vai trò |
| :--- | :--- |
| `Features/Auth/Welcome/OwnerWelcomeEmail.cs` | Mẫu thư HTML và bản text, mã hóa HTML mọi dữ liệu người dùng. |
| `Features/Auth/Welcome/OwnerWelcomeNotifier.cs` | Kiểm tra cấu hình, dựng link đăng nhập, xếp thư, không ném lỗi. |
| `AuthService.RegisterTenantAsync` | Gọi notifier sau khi commit. |
| `RateLimitPolicies.AuthRegister` | Giới hạn số lần đăng ký theo IP. |

## Giới hạn đã chấp nhận

- Hàng đợi nằm trong bộ nhớ: backend khởi động lại khi thư chưa gửi thì mất thư đó, không gửi bù.
- Email chưa được xác minh khi đăng ký, nên thư có dòng nhắc người nhận bỏ qua nếu không phải họ đăng ký.
- Thư dùng chung hàng đợi 200 thư và hạn mức Gmail với mail đặt lại mật khẩu.

## Điểm docs còn lệch

`FR-AUTH-01` ghi đăng ký phải tạo Chi nhánh và Kho mặc định, trong khi code và `docs/05-api-contracts/01-auth-endpoints.md` chỉ tạo Tenant và Owner. Upgrade này giữ nguyên hành vi hiện tại, nhóm cần chốt lại yêu cầu.
