# BF-036 — Đăng nhập lỗi 500 khi trình duyệt còn token của tenant khác

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa và kiểm chứng trên PostgreSQL |
| **Ưu tiên** | P1 |
| **Khu vực** | Backend / Auth / Tenant isolation; Admin / HTTP |
| **Phát hiện** | 07/11/2026, stack `docker compose` local |
| **Nhánh sửa** | `feature/OISMS-48-shop-integrations-api` (PR 39) |

## Mô tả lỗi

Trình duyệt còn access token còn hạn của tenant B (ví dụ vừa đăng ký shop mới), người dùng đăng nhập tài khoản `owner@shopa.local` của tenant A với mật khẩu đúng. Giao diện báo "Đăng nhập thất bại", API trả 500. Log backend:

```
System.Security.SecurityException: Cross-tenant reference violation: RefreshToken references User
with TenantId '<tenant A>' while authenticated as Tenant '<tenant B>'.
```

`POST /api/auth/refresh-token` có cùng lỗi khi request mang token của tenant khác.

## Nguyên nhân

- Admin (`src/frontend/admin-dashboard/src/services/httpClient.ts`) gắn `Authorization: Bearer <token cũ>` vào mọi request, kể cả `/api/auth/login`.
- JwtBearer vẫn xác thực token cũ vì token còn hạn, nên `CurrentTenantService` trả tenant B ngay cả ở endpoint ẩn danh.
- `AuthService.LoginAsync` lưu refresh token trong `OpenPrivilegedScope()`, nhưng bước kiểm tra navigation của `AppDbContext.EnforceTenantGuardsAndAudit()` chạy theo tenant đã xác thực (B) và chặn `RefreshToken → User` của tenant A.

## Cách sửa

- Thêm `IgnoreTenantClaimAttribute` (`OISM.Application.Common.Attributes`), gắn lên `register-tenant`, `login`, `refresh-token`, `logout` trong `AuthController`. Trên các endpoint này `CurrentTenantService` không lấy tenant từ access token gửi kèm. Tenant của luồng do service xác định từ email/mật khẩu hoặc refresh token đã kiểm tra.
- Không nới guard của `AppDbContext`, không mở thêm `OpenPrivilegedScope`/`IgnoreQueryFilters`. Endpoint nghiệp vụ vẫn lấy tenant từ JWT như cũ; `SetTenant` (webhook, job, hub) vẫn được ưu tiên.
- Admin: interceptor request bỏ header `Authorization` khi gọi login/register/refresh/logout; `AuthContext.login` và `authAdapter.login` gọi `clearAuthSession()` trước khi gửi đăng nhập.
- POS PWA (`src/frontend/pos-app/src/services/api.ts`): `loginCashier` đã gọi `fetch` không kèm `Authorization`, không cần sửa.

## Kiểm chứng

`src/backend/tests/OISM.IntegrationTests/AuthStaleTokenIntegrationTests.cs` (PostgreSQL thật qua `WebApplicationFactoryWithPostgres`):

- Đăng nhập tenant A kèm `Bearer <token B>` trả 200, claim `tenantId` và `user.tenantId` là A, `/api/auth/me` trả email A; token B vẫn chỉ thấy B; mọi dòng `refresh_tokens` có `tenant_id` trùng tenant của user.
- Sai mật khẩu kèm token B vẫn trả 401, không ghi refresh token.
- Refresh token của A, đăng ký tenant mới và đăng xuất khi mang token B đều thành công và ghi đúng tenant.
- Endpoint nghiệp vụ (`GET /api/branches`) vẫn dùng tenant của JWT và trả 401 khi không có token.

Khi bỏ phần sửa ở `CurrentTenantService`, hai ca login và refresh trả 500 đúng như lỗi gốc.

## Cách tạm khi chưa có bản sửa

Xóa `oism_access_token` trong localStorage hoặc dùng cửa sổ ẩn danh.
