# Báo cáo sau nâng cấp · Mail chào mừng Chủ shop (Upgrade 11)

| Thông tin | Giá trị |
| :--- | :--- |
| Đặc tả | [11-mail-chao-mung-chu-shop.md](../Upgrade-recommended/Done/11-mail-chao-mung-chu-shop.md) |
| Ngày | 18/11/2026 |
| Baseline | `feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-1` tại `d8933a0` |
| Phạm vi | Backend, test, tài liệu. Không đổi schema (vẫn 48 bảng), không thêm biến môi trường, không đổi frontend |

## Đã làm

- **Mail chào mừng cho Chủ shop.** `POST /api/auth/register-tenant` thành công thì xếp một thư vào hàng đợi nền. Thư có tên Chủ shop, tên cửa hàng, email đăng nhập, nút đăng nhập Admin và ba bước bắt đầu.
- **Chỉ Owner nhận thư.** `POST /api/users` tạo `Staff` hoặc `Cashier` không gửi thư.
- **Gửi sau commit.** `AuthService.RegisterTenantAsync` gọi `IOwnerWelcomeNotifier` sau `ExecuteWithRetryAsync`, nên rollback không phát sinh thư và retry không gửi trùng.
- **Lỗi mail không ảnh hưởng đăng ký.** Thiếu SMTP, URL Admin sai, hàng đợi đầy hoặc SMTP lỗi đều vẫn trả `201`.
- **Rate limit đăng ký.** Policy `AuthRegister` giới hạn 10 lần/15 phút mỗi IP, cấu hình tại `RateLimiting:RegisterTenantPer15Minutes`, vượt hạn mức trả `429 TOO_MANY_REQUESTS`.

## Bảo mật và quy tắc dự án

| Quy tắc | Cách giữ |
| :--- | :--- |
| Không lộ mật khẩu | Thư không nhận mật khẩu làm tham số; integration test kiểm tra cả bản HTML và text. |
| Chèn nội dung | Tên người dùng, tên cửa hàng, email và link đều mã hóa HTML. Tiêu đề thư cố định. |
| Link không bị giả | Link đăng nhập chỉ lấy từ `PasswordReset:AdminBaseUrl`. |
| Log | Chỉ ghi `UserId`, không ghi email hay nội dung thư. |
| Transaction | Không thêm thao tác DB vào transaction đăng ký, không đổi thứ tự khóa. |

## Kiểm thử

Chạy ngày 18/11/2026 trên PostgreSQL 16 tạm thời (`postgres:16-alpine`), dùng cấu hình test như `.github/workflows/ci.yml`.

```bash
dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false
dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false
```

| Nhóm | Kết quả |
| :--- | :--- |
| Unit test backend | **PASS 428/428** |
| Toàn bộ integration test, gồm 8 request đăng ký đồng thời cùng email trên PostgreSQL thật | **PASS 456/456** (27 phút 26 giây) |
| Gửi thư thật qua Gmail SMTP | **NOT RUN** |

Test quên mật khẩu được sửa để lọc thư theo tiêu đề, vì Owner giờ nhận thêm mail chào mừng. Assertion của các test đó giữ nguyên.

## Giới hạn còn lại

- Hàng đợi nằm trong bộ nhớ, backend khởi động lại khi thư chưa gửi thì mất thư đó.
- Email chưa được xác minh khi đăng ký.
- `FR-AUTH-01` ghi đăng ký phải tạo Chi nhánh và Kho mặc định, nhưng code chỉ tạo Tenant và Owner. Nhóm cần chốt lại yêu cầu này.
