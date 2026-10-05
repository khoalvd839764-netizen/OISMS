# Báo Cáo Quét Lỗi Dự Án OISMS (Lần 1 - Đã Code)
Ngày quét: 04/10/2026

## 1. Phạm Vi Quét
Chỉ quét các file đã được code trong giai đoạn 1 (bỏ qua các thư mục trống `.gitkeep`):
- `docs/init_schema.sql`
- `docs/seed_data.sql`
- `backend/src/OISM.Domain/Common/BaseEntity.cs`
- `backend/src/OISM.Domain/Common/IMustHaveTenant.cs`
- `backend/src/OISM.Domain/Entities/Tenant.cs`
- `backend/src/OISM.Domain/Entities/User.cs`
- `backend/src/OISM.Domain/Entities/Role.cs`
- `backend/src/OISM.Domain/Entities/UserRole.cs`
- `backend/src/OISM.Domain/Entities/Branch.cs`
- `backend/OISM.sln`
- `backend/global.json`
- `backend/src/OISM.WebApi/Program.cs`
- `backend/src/OISM.WebApi/appsettings.json`
- `.gitignore`

**Lưu ý cho các lần quét sau:** KHÔNG cần quét lại các file trên để tìm lỗi cơ bản, trừ khi có sự thay đổi (commit mới) vào các file này.

## 2. Kết Quả Quét (Các vấn đề cần khắc phục)

### 2.1. Cấu trúc Solution & Build
- **Trạng thái:** Dự án build thành công (0 lỗi, 0 cảnh báo).
- **Cấu trúc:** Tốt, chuẩn Clean Architecture.

### 2.2. Lỗi Không Khớp Giữa C# Entity (OISM.Domain) và DB Schema
Phần Code C# của bạn Phúc (Sprint 1) thiếu một số thuộc tính so với Database (do bạn Khoa thiết kế):

1. **Entity `UserRole.cs` bị dư cột và sai kế thừa:**
   - **Thực tế:** `UserRole` đang kế thừa `BaseEntity`, điều này khiến Entity Framework sẽ tự động sinh ra cột `Id` (Guid), `CreatedAt`, `UpdatedAt`.
   - **Lỗi:** Trong database `init_schema.sql`, bảng `user_roles` **không có cột `id`** mà sử dụng khóa chính kép (Composite Key) là `(user_id, role_id)`. Nó cũng dùng cột `assigned_at` thay vì `created_at`.
   - **Cách sửa:** `UserRole` **KHÔNG** nên kế thừa `BaseEntity`. Chỉ cần chứa `UserId`, `RoleId`, và `AssignedAt` (nếu cần).

2. **Entity `User.cs` thiếu trường thông tin:**
   - **Thiếu:** Thiếu thuộc tính `PhoneNumber` (string?). Trong database đã có cột `phone_number`.

3. **Thiếu Navigation Properties (Khóa ngoại hướng đối tượng trong EF Core):**
   - Các Entity đang đứng rời rạc, chưa có thuộc tính liên kết (Navigation properties) để tiện cho truy vấn LINQ sau này (ví dụ `.Include(u => u.Tenant)`).
   - `Tenant.cs`: Thiếu `ICollection<User> Users` và `ICollection<Branch> Branches`.
   - `User.cs`: Thiếu liên kết tới `Tenant` và `ICollection<UserRole> UserRoles`.
   - `Role.cs`: Thiếu `ICollection<UserRole> UserRoles`.
   - `Branch.cs`: Thiếu liên kết tới `Tenant`.

## 3. Khuyến Nghị Cho Nhóm
Những lỗi trên là lỗi Logic / Mapping, sẽ không gây lỗi khi build project hiện tại (vì chưa code DbContext), nhưng **SẼ GÂY LỖI CRASH 100%** khi bạn số 2 (code DbContext) bắt đầu chạy Migration hoặc query database. 

Vui lòng nhờ Phúc (bạn 1) sửa lại các file Entity, hoặc bạn số 2 khi làm DbContext phải ghi đè bằng Fluent API thật cẩn thận để bỏ qua các cột dư thừa.
