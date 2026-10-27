# SKILL-01: KIẾN TRÚC PHẦN MỀM CLEAN ARCHITECTURE & CÔ LẬP ĐA NGƯỜI THUÊ (MULTI-TENANCY)

> **Người thực hiện:** Lê Võ Đăng Khoa & Nhựt Phùng Minh  
> **Áp dụng tại:** Dự án OISMS — Hệ thống Quản lý Bán hàng & Tồn kho Đa kênh  
> **Cơ sở đối chiếu lý thuyết:** Ch14 - Designing Object-Oriented Software Architectures & Ch15 - Multi-tier Client/Server Architecture.

---

### 1. BÀI TOÁN THỰC TẾ & NỖI ĐAU HỆ THỐNG
Trong mô hình phần mềm dịch vụ (**SaaS - Software as a Service**), hàng trăm cửa hàng bán lẻ (Tenants) cùng sử dụng chung một hệ thống phần mềm và CSDL. Nếu kiến trúc thiết kế không chặt chẽ:
* Code logic nghiệp vụ sẽ bị dính chặt vào controller hoặc câu lệnh SQL, gây khó khăn cho việc bảo trì và kiểm thử.
* Rủi ro bảo mật nghiêm trọng nhất: Cửa hàng A có thể đọc hoặc sửa nhầm dữ liệu của Cửa hàng B nếu lập trình viên quên viết điều kiện `WHERE tenant_id = ...`.

---

### 2. GIẢI PHÁP KIẾN TRÚC ĐÃ ÁP DỤNG

#### 2.1. Clean Architecture phân tách 4 tầng ranh giới nghiêm ngặt
Hệ thống Backend .NET 8 của OISMS áp dụng mô hình Clean Architecture (Onion Architecture):
1. **`OISM.Domain` (Tầng Lõi):** Chứa các thực thể cốt lõi (`Tenant`, `User`, `Product`, `Sku`, `InventoryTransaction`), Enums trạng thái và Domain Events. Tầng này độc lập 100%, không phụ thuộc vào bất kỳ thư viện hay CSDL nào bên ngoài.
2. **`OISM.Application` (Tầng Logic Ứng dụng):** Chứa các Use-case nghiệp vụ, DTOs, hợp đồng Interfaces (`ICurrentTenantService`), bộ kiểm tra tính hợp lệ (`FluentValidation`) và các thuật toán chuyên sâu (WAC, Barcode, Cycle Detection).
3. **`OISM.Infrastructure` (Tầng Hạ tầng):** Chứa Entity Framework Core 8 (`AppDbContext`), cấu hình Fluent API, PostgreSQL migrations và các dịch vụ kỹ thuật.
4. **`OISM.WebApi` (Tầng Trình diễn / Giao tiếp ngoài):** Chứa 10 API Controllers, middleware giải mã JWT token, Swagger OpenAPI documentation.

#### 2.2. Cơ chế Cô lập Đa người thuê tự động (Multi-tenant Guard)
Hệ thống sử dụng mô hình **Shared Database, Separate Tenant ID**:
* Khi request gửi lên API, middleware trích xuất `tenantId` từ JWT Token và inject vào `ICurrentTenantService` có vòng đời `Scoped`.
* Tại tầng `AppDbContext`, EF Core áp dụng **Global Query Filter**:
  ```csharp
  modelBuilder.Entity<Product>().HasQueryFilter(p => p.TenantId == _currentTenantService.TenantId);
  modelBuilder.Entity<Sku>().HasQueryFilter(s => s.TenantId == _currentTenantService.TenantId);
  ```
* Mọi câu truy vấn `SELECT`, `UPDATE`, `DELETE` tự động được gắn thêm mệnh đề `WHERE tenant_id = @CurrentTenantId`, triệt tiêu hoàn toàn nguy cơ rò rỉ dữ liệu chéo giữa các cửa hàng.

---

### 3. BẰNG CHỨNG KIỂM CHỨNG TRONG MÃ NGUỒN
* Mã nguồn cài đặt: [`src/backend/src/OISM.Infrastructure/Data/AppDbContext.cs`](file:///Users/bo/OISMS/src/backend/src/OISM.Infrastructure/Data/AppDbContext.cs)
* Bộ kiểm thử hồi quy: `MultiTenantIsolationTests.cs` kiểm chứng: Khi Tenant A truy vấn danh sách sản phẩm, kết quả trả về không bao giờ chứa bất kỳ SKU nào thuộc Tenant B.
