# 05. Khả Năng Bảo Trì Kiến Trúc & Độ Phủ Kiểm Thử (Maintainability & Testability)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-NFR-05` |
| **Phiên bản (Version)** | `v1.1.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-14` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `NFR-MAINT-01`, `NFR-MAINT-02` (Trang 4) |
| **Phạm vi áp dụng** | Cấu trúc Solution `.NET 8` & Bộ kiểm thử tự động (`Unit & Integration Tests`) |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả nguyên tắc phân tầng Clean Architecture và chỉ tiêu độ phủ kiểm thử tự động tối thiểu 80%. | `INIT-NFR-05` |
| `v1.1.0` | `2026-11-14` | Nhóm dự án OISMS | Ghi nhận hiện trạng `NFR-MAINT-01`, chốt ngoại lệ có chủ đích `OISM.Application` dùng EF Core qua `IAppDbContext` và khóa kiểm tra tự động bằng `ArchitectureRulesTests`. | `OISMS-59-G1C` |

---

## 1. Đặc Tả Chi Tiết (`NFR-MAINT-01`, `NFR-MAINT-02`)

| Mã NFR | Tên yêu cầu | Đặc tả kỹ thuật bắt buộc |
| :---: | :--- | :--- |
| **`NFR-MAINT-01`** | **Clean Architecture** (Kiến trúc sạch 4 tầng) | Phân tách nghiêm ngặt trách nhiệm (`Strict separation of concerns`) qua 4 tầng dự án:<br/>1. **`Domain`:** Chứa thực thể và logic nghiệp vụ lõi (`COGS`, `Reserve`, `Ledger`). Không phụ thuộc bất kỳ thư viện ngoại vi nào.<br/>2. **`Application`:** Chứa các ca sử dụng (`Use cases`), DTOs, Validators và Interfaces.<br/>3. **`Infrastructure`:** Hiện thực kết nối `EF Core`, `PostgreSQL`, `SignalR`, `Hangfire`, `BCrypt`.<br/>4. **`Presentation`:** Tầng giao tiếp `Web API` (Controllers, Middlewares, Filters). |
| **`NFR-MAINT-02`** | **Automated Test Coverage** (Độ phủ kiểm thử tự động) | Các module nghiệp vụ cốt lõi bao gồm **Sổ cái tồn kho (`Inventory Ledger`)**, **Giữ chỗ chống bán vượt (`Anti-Oversell Reserve`)** và **Tính giá vốn (`COGS / WAC`)** bắt buộc phải duy trì **độ phủ Unit & Integration Test tối thiểu đạt `80%`** *(Thực tế Sprint 2 đã đạt `92.49% – 94.83%`)*. |

## 2. Hiện Trạng Tuân Thủ & Ngoại Lệ Đã Chốt (`NFR-MAINT-01`, 14/11/2026)

- **Chiều phụ thuộc bốn tầng:** `OISM.Domain` chỉ tham chiếu BCL; `OISM.Application` tham chiếu `OISM.Domain` (cấm `OISM.Infrastructure`, `OISM.WebApi`, `Npgsql*`, `Hangfire*`, `Microsoft.AspNetCore*`, `BCrypt*`); `OISM.Infrastructure` không tham chiếu `OISM.WebApi`; controller trong `OISM.WebApi` không tiêm hoặc giữ field kiểu `DbContext`, `AppDbContext`, `IAppDbContext`, `IUnitOfWork`, `ISqlLockService`.
- **Ngoại lệ có chủ đích (chốt ngày 14/11/2026):** `OISM.Application` được phép tham chiếu `Microsoft.EntityFrameworkCore` để truy vấn và lưu entity qua `IAppDbContext` (do hiện có 39 file và khoảng 645 lời gọi EF; việc refactor sang repository riêng mang rủi ro cao cho luồng transaction và reservation). Tuy nhiên, `OISM.Application` **cấm tuyệt đối** các lời gọi raw SQL của EF (`ExecuteSqlRaw`, `ExecuteSqlInterpolated`, `FromSqlRaw`, `FromSqlInterpolated`, `SqlQueryRaw`).
- **SQL đặc thù, khóa và transaction:** đặt tập trung ở `OISM.Infrastructure` sau các abstraction `ISqlLockService` và `IUnitOfWork`.
- **Khóa tự động:** được kiểm tra bằng 5 test trong [`ArchitectureRulesTests`](../../test-oisms/backend/OISM.IntegrationTests/ArchitectureRulesTests.cs); chi tiết đối chiếu xem [01-clean-architecture.md](../04-architecture-and-design/01-clean-architecture.md#4-đối-chiếu-phụ-thuộc-bốn-tầng-14112026).
