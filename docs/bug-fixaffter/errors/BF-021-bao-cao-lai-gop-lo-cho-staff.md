# BF-021 — Báo cáo lãi gộp lộ giá vốn và lãi gộp cho Staff

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ❌ Chưa sửa |
| **Ưu tiên** | P1 |
| **Khu vực** | Backend / Báo cáo / Phân quyền |
| **Phát hiện** | 06/11/2026, đối chiếu code với ma trận RBAC (cũng là BE2-F03 trong hồ sơ BE2) |

## Mô tả lỗi

**Yêu cầu:** chỉ `Owner` được xem tổng giá vốn (COGS) và lãi gộp ([ma trận RBAC](../../01-users-and-rbac/02-rbac-matrix.md), `FR-REP-01`).

**Thực tế:** `ReportsController` gắn `[Authorize(Roles = "Owner,Staff")]` ở cấp class, nên `Staff` gọi được `GET /api/reports/revenue-profit`. `ReportService.GetRevenueProfitReportAsync` không nhận vai trò và trả đủ `costOfGoodsSold`, `grossProfit` (tổng, theo kênh, theo chi nhánh, theo ngày).

**Tái hiện:** đăng nhập bằng tài khoản `Staff`, gọi `GET /api/reports/revenue-profit` bằng token đó (Postman hoặc DevTools).

**Ảnh hưởng:** lộ dữ liệu tài chính. Giao diện chỉ mở `/reports` cho `Owner` nên người dùng thường không thấy, nhưng gọi API trực tiếp thì lấy được.

**Vị trí:** `src/backend/src/OISM.WebApi/Controllers/ReportsController.cs:14`, `:28`; `src/backend/src/OISM.Application/Features/Reports/Services/ReportService.cs:31`.

**Đối chiếu:** `GET /api/reports/dashboard-summary` đã làm đúng: truyền `isOwner` và trả `null`/`0` cho trường giá vốn, lãi gộp khi không phải `Owner`.

## Điều kiện đóng

- Hoặc chặn `Staff` ở endpoint (`Roles = "Owner"`), hoặc service ẩn `costOfGoodsSold`, `grossProfit` theo vai trò như `dashboard-summary`. Cần nhóm chốt cách nào theo yêu cầu.
- Có integration test: Owner nhận đủ trường; Staff nhận 403 hoặc trường đã ẩn; Cashier nhận 403.
