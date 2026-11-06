# BF-022 — API tồn kho trả giá vốn cho Cashier

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ❌ Chưa sửa |
| **Ưu tiên** | P1 |
| **Khu vực** | Backend / Tồn kho / Phân quyền |
| **Phát hiện** | 06/11/2026, đối chiếu code với ma trận RBAC |

## Mô tả lỗi

**Yêu cầu:** `Cashier` không xem số dư chi tiết, không thấy giá vốn; chỉ cần tồn khả dụng tại POS. Quy tắc dự án: trường giá vốn phải kiểm tra quyền ở cả dữ liệu trả về.

**Thực tế:** `GET /api/inventory/balances` gắn `[Authorize(Roles = "Owner,Staff,Cashier")]` và DTO trả về có `averageCost` (WAC) cho mọi vai trò.

**Tái hiện:** đăng nhập `Cashier`, gọi `GET /api/inventory/balances?branchId=<id>`; mỗi dòng có `averageCost` khác 0 với SKU đã nhập kho.

**Ảnh hưởng:** thu ngân đọc được giá vốn từng SKU.

**Vị trí:** `src/backend/src/OISM.WebApi/Controllers/InventoryController.cs:48-49`; `src/backend/src/OISM.Application/Features/Inventory/Services/InventoryService.cs` (`GetBalancesAsync`, gán `AverageCost`); `Features/Inventory/Dtos/InventoryBalanceDtos.cs:34`.

## Điều kiện đóng

- Không trả `averageCost` (hoặc trả `null`) khi người gọi là `Cashier`; POS vẫn đọc được `onHand`, `reserved`, `available`.
- Kiểm tra cả `GET /api/inventory/balances/sku/{skuId}/branch/{branchId}` (cũng cho Cashier).
- Integration test theo 3 vai trò; chạy lại `npm test` của POS để chắc POS không dùng trường này.
