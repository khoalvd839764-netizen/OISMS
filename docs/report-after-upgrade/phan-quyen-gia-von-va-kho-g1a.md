# Báo cáo sau nâng cấp: Che giá vốn với Cashier và quyền ghi kho chỉ Owner (OISMS-59-G1A)

| Mục | Nội dung |
| --- | --- |
| Task | `OISMS-59-G1A` |
| Ngày kiểm tra | 14/11/2026 |
| Phạm vi | Backend (`InventoryController`, `InventoryService`, `InventoryBalanceDtos`, `WarehousesController`), Frontend (`admin-dashboard`), Integration Tests, Tài liệu RBAC / FR / API Contract |
| Trạng thái | Hoàn tất triển khai và chạy kiểm chứng tự động (chờ review) |

## Đã làm

1. **Che giá vốn (`averageCost`) đối với `Cashier` trên API tồn kho:**
   - `InventoryBalanceDto.AverageCost` chuyển từ `decimal` sang `decimal?` (`src/backend/src/OISM.Application/Features/Inventory/Dtos/InventoryBalanceDtos.cs`), giữ nguyên tên trường JSON `averageCost`.
   - `IInventoryService` và `InventoryService` (`src/backend/src/OISM.Application/Features/Inventory/Services/InventoryService.cs`) bổ sung overload nhận tham số `bool canViewCost` cho `GetBalancesAsync` (cả nhánh lọc theo kho và cấp chi nhánh) và `GetSkuBalanceAtBranchAsync`, đồng thời giữ nguyên overload mặc định (`canViewCost: true`) để tương thích các test gọi service trực tiếp. Khi `canViewCost == false`, `averageCost` trả `null`; các trường `onHand`, `reserved`, `available`, `reorderPoint` giữ nguyên.
   - `InventoryController` (`src/backend/src/OISM.WebApi/Controllers/InventoryController.cs`) tính `canViewCost = User.IsInRole("Owner") || User.IsInRole("Staff")` và truyền vào service, không đọc vai trò trong tầng `Application`.
2. **Siết quyền ghi kho chỉ còn `Owner`:**
   - `WarehousesController` (`src/backend/src/OISM.WebApi/Controllers/WarehousesController.cs`): đổi `[Authorize(Roles = "Owner,Staff")]` thành `[Authorize(Roles = "Owner")]` trên `POST /api/warehouses`, `PUT /api/warehouses/{id}` và `PUT /api/warehouses/{id}/toggle-status`. Hai endpoint `GET /api/warehouses` và `GET /api/warehouses/{id}` giữ nguyên `[Authorize]`.
3. **Cập nhật Admin Dashboard (`src/frontend/admin-dashboard`):**
   - `src/types/domain.ts`: đổi kiểu `averageCost` trên `BranchStockBreakdown` và `Sku` thành `number | null`, `totalInventoryValue` thành `number | null`.
   - `src/services/apiAdapter.ts`: giữ nguyên giá trị `null` cho `averageCost` và `totalInventoryValue` khi API trả `null`, không ép về `0`.
   - `src/pages/InventoryPage.tsx`: hiển thị `"—"` thay cho `0 ₫` ở cột `Giá vốn BQ (WAC)`, cột `Giá trị tồn kho` và trong modal giữ chỗ khi `averageCost` là `null`.
   - `src/pages/OrganizationPage.tsx`: chỉ render nút `+ Thêm kho`, nút `Bật`/`Tắt` kho và modal tạo kho khi `user?.role === "Owner"`.
4. **Cập nhật tài liệu:**
   - `docs/01-users-and-rbac/02-rbac-matrix.md` (v1.4.0): cập nhật mục 2 và 2.1 (`balances` và `kho` thành `Đã xử lý (14/11/2026)`; chốt yêu cầu xóa danh mục/thương hiệu/sản phẩm là `Chỉ Owner`).
   - `docs/02-functional-requirements/fr-01-auth-tenant.md` (v1.1.0): làm rõ `FR-AUTH-04` (`Staff` và `Cashier` chỉ xem danh sách chi nhánh/kho, `403` khi ghi kho).
   - `docs/05-api-contracts/03-inventory-endpoints.md` (v1.4.0): ghi rõ `averageCost` trả `null` với `Cashier`.
   - `docs/05-api-contracts/01-auth-endpoints.md` (v1.2.0): cập nhật quyền `POST /api/warehouses`, `PUT /api/warehouses/{id}`, `PUT /api/warehouses/{id}/toggle-status` thành `Owner`.

## Ảnh hưởng chức năng hiện có

- **Vai trò `Staff`:** mất quyền tạo mới (`POST /api/warehouses`), cập nhật (`PUT /api/warehouses/{id}`) và bật/tắt trạng thái kho (`PUT /api/warehouses/{id}/toggle-status`) — các API này trả `403 Forbidden` và các nút thao tác kho trên `OrganizationPage` bị ẩn khi đăng nhập với `Staff`. `Staff` vẫn xem được danh sách/chi tiết kho (`GET`), xem và thao tác tồn kho, nhập/chuyển/kiểm kê kho, và vẫn thấy `averageCost` trên API tồn kho.
- **Vai trò `Cashier`:** vẫn tra cứu được `onHand`, `reserved`, `available`, `reorderPoint` trên `GET /api/inventory/balances` và `GET /api/inventory/balances/sku/{skuId}/branch/{branchId}` phục vụ POS, nhưng trường `averageCost` luôn là `null`.
- **Test hiện có đã cập nhật:** `Postgres_WebApi_E2E_FullFlow_CashierBlocked_AdminReadRealBalances` trong `test-oisms/backend/OISM.IntegrationTests/InventoryAndGoodsReceiptIntegrationTests.cs` trước đây kiểm tra `b.AverageCost == 120000m` bằng token `Cashier`; đã sửa thành kiểm tra `b.AverageCost == null` cho `Cashier` và `b.AverageCost == 120000m` cho `Owner`.

## Thay đổi cơ sở dữ liệu

- **Không đổi DB:** không thêm bảng, cột, ràng buộc hay file migration nào.

## Kiểm chứng

| Lệnh | Phạm vi / Provider | Kết quả |
| --- | --- | --- |
| `dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal` | Toàn bộ solution backend | PASS (0 warning, 0 error) |
| `dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --no-build --filter 'FullyQualifiedName~InventoryAndWarehouseRbacIntegrationTests\|FullyQualifiedName~BranchAndSupplierIntegrationTests\|FullyQualifiedName~Postgres_WebApi_E2E_FullFlow_CashierBlocked_AdminReadRealBalances\|FullyQualifiedName~WarehouseBranchReassignmentRegressionTests\|FullyQualifiedName~Postgres_BranchAndWarehouse_Update_AndRbacCashierForbidden'` | `InventoryAndWarehouseRbacIntegrationTests` (InMemory), `BranchAndSupplierIntegrationTests` (InMemory), `Postgres_WebApi_E2E_FullFlow_CashierBlocked_AdminReadRealBalances` (PostgreSQL), `WarehouseBranchReassignmentRegressionTests` (PostgreSQL), `Postgres_BranchAndWarehouse_Update_AndRbacCashierForbidden` (PostgreSQL) | PASS 16/16 (Failed: 0, Skipped: 0) |
| `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false --no-build` | Toàn bộ UnitTests | PASS 380/380 (Failed: 0, Skipped: 0) |
| `npm run build --prefix src/frontend/admin-dashboard` | `tsc -b && vite build` | PASS |

## Giới hạn

- Các test RBAC mới trong `InventoryAndWarehouseRbacIntegrationTests` chạy qua `CustomWebApplicationFactory` (EF Core InMemory); luồng kiểm tra toàn bộ `OISM.IntegrationTests` chưa chạy hết trong đợt này.
- UI `admin-dashboard` chưa chạy kiểm thử giao diện tự động (chỉ kiểm chứng build TypeScript/Vite); hành vi ẩn nút và hiển thị `"—"` cần kiểm tra thêm khi review giao diện hoặc tích hợp.
- Ngoài scope đợt này: `src/frontend/admin-dashboard/src/pages/ProductsPage.tsx` (dòng 583, 1246) gọi `.toLocaleString("vi-VN")` trực tiếp trên `averageCost`; hiện `tsconfig.app.json` không bật `strictNullChecks` và chỉ `Owner`/`Staff` (luôn nhận giá trị số từ backend) truy cập trang sản phẩm nên build không lỗi, nhưng nên bổ sung kiểm tra null ở đợt dọn dẹp tiếp theo.
