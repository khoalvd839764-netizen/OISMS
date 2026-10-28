# KHẮC PHỤC LỖI B-01: ĐỔI CHI NHÁNH KHO SẠCH (WAREHOUSE BRANCH REASSIGNMENT)

## 1. Bối Cảnh & Hiện Tượng Lỗi

Trong quá trình chạy kiểm thử hồi quy Backend 2 trên PostgreSQL thật:
- **Tên test lỗi:** `PostgresBackend2RegressionTests.Postgres_Warehouse_ChangeBranch_WhenHasHistory_ReturnsControlled409_AndCleanWarehouseSucceeds`
- **Vị trí lỗi:** `src/backend/tests/OISM.IntegrationTests/PostgresBackend2RegressionTests.cs:line 790`
- **Hiện tượng:** Khi gọi `PUT /api/warehouses/{id}` để chuyển một kho hàng hoàn toàn sạch (chưa phát sinh bất kỳ chứng từ hay số dư nào) sang chi nhánh mới cùng Tenant, kỳ vọng trả về `200 OK` và cập nhật `BranchId`, nhưng thực tế API ném `500 Internal Server Error`.
- **Thông điệp Exception:**
  ```text
  System.InvalidOperationException:
  The property 'Warehouse.BranchId' is part of a key and so cannot be modified or marked as modified.
  ```

---

## 2. Phân Tích Nguyên Nhân Gốc (Root Cause Analysis)

1. **Khóa phụ trợ Alternate Key trên EF Core:**
   - Trong schema PostgreSQL v0.5, bảng `warehouses` có ràng buộc unique composite `uq_warehouses_2 ("tenant_id", "id", "branch_id")`.
   - Để ánh xạ các quan hệ khóa ngoại composite từ `StockTransferConfiguration`, `StockCountConfiguration`, `InventoryAdjustmentConfiguration` về `Warehouse`, EF Core đã đăng ký bộ ba `{ TenantId, Id, BranchId }` làm **Principal Key / Alternate Key** trên thực thể `Warehouse`.
2. **Cơ chế Change Tracking của EF Core:**
   - Khi `WarehousesController.UpdateWarehouse` thực hiện `warehouse.BranchId = request.BranchId;` trên thực thể đang được Change Tracker theo dõi, EF Core phát hiện một thuộc tính tham gia vào khóa chính / khóa phụ đang bị sửa đổi.
   - Khi gọi `_context.SaveChangesAsync()`, Change Tracker chủ động ném `System.InvalidOperationException` để ngăn chặn việc sửa đổi key property trên tracked entity.
3. **Bắt ngoại lệ chưa bao quát:**
   - Khối `try-catch` tại `UpdateWarehouse` trước đây chỉ bắt `DbUpdateException` (ngoại lệ phát sinh từ phía database provider khi vi phạm constraint), hoàn toàn không xử lý `InvalidOperationException` nội bộ của EF Core change tracker, dẫn đến unhandled 500.

---

## 3. Chiến Lược Sửa Lỗi Tận Gốc (Root Fix Strategy)

Để kho sạch đổi `BranchId` thành công, giữ nguyên `WarehouseId`, tuân thủ toàn bộ AC-01..AC-07 mà không phá vỡ schema database hoặc xóa bỏ các ràng buộc khóa ngoại:

1. **Kiểm tra lịch sử phụ thuộc toàn diện trên cả 7 bảng:**
   Trước khi cho phép đổi chi nhánh, kiểm tra toàn bộ các bảng tham chiếu tới `WarehouseId`:
   - `GoodsReceipts` (`WarehouseId == id`)
   - `WarehouseInventoryBalances` (`WarehouseId == id`)
   - `InventoryTransactions` (`WarehouseId == id`)
   - `Reservations` (`WarehouseId == id`)
   - `StockTransfers` (`FromWarehouseId == id || ToWarehouseId == id`)
   - `StockCounts` (`WarehouseId == id`)
   - `InventoryAdjustments` (`WarehouseId == id`)
   Nếu bất kỳ bảng nào có dữ liệu (kể cả khi tồn kho hiện tại bằng 0) ➔ lập tức rollback và trả về `409 Conflict`.

2. **Xác minh chi nhánh đích nghiêm ngặt theo Tenant:**
   - Chi nhánh đích `BranchId` phải tồn tại và thuộc quyền sở hữu của chính tenant đang đăng nhập (`b.TenantId == tenantId.Value`).
   - Nếu chi nhánh không tồn tại hoặc thuộc tenant khác ➔ trả về `400 Bad Request`.

3. **Cập nhật bằng Parameterized SQL trong Transaction:**
   - Thay vì sửa thuộc tính `BranchId` trên tracked entity rồi gọi `SaveChangesAsync()`, controller sử dụng parameterized SQL trực tiếp:
     ```csharp
     var affectedRows = await _context.Database.ExecuteSqlInterpolatedAsync(
         $"UPDATE warehouses SET branch_id = {request.BranchId}, name = {trimmedName}, is_active = {isActive}, updated_at = {now} WHERE id = {id} AND tenant_id = {tenantId.Value}");
     ```
   - Thao tác này cập nhật trực tiếp tại tầng database mà không kích hoạt kiểm tra key-mutation của EF Core change tracker.
   - Bắt buộc kiểm tra `affectedRows > 0` và lọc chặt chẽ theo cả `id` lẫn `tenant_id`.
   - Detach thực thể cũ khỏi change tracker (`_context.Entry(warehouse).State = EntityState.Detached`) để bảo đảm tính nhất quán của DbContext.

4. **Bảo toàn luồng cập nhật không đổi Branch:**
   - Nếu `request.BranchId == warehouse.BranchId`: chỉ cập nhật `Name` và `IsActive` thông qua EF Core `SaveChangesAsync()` thông thường, không phát sinh direct SQL không cần thiết.

5. **Đồng thời & Khóa bi quan (Concurrency & Pessimistic Locking):**
   - Áp dụng `SELECT id FROM warehouses WHERE id = {id} AND tenant_id = {tenantId} FOR UPDATE` trên PostgreSQL ngay khi mở transaction.
   - Bắt chính xác cả `Npgsql.PostgresException` và `DbUpdateException` với mã lỗi SQLSTATE `23503` (Foreign Key Constraint Violation) để trả về `409 Conflict` nếu có race condition tạo dữ liệu đồng thời.

---

## 4. Tệp Tin Thay Đổi

1. `src/backend/src/OISM.WebApi/Controllers/WarehousesController.cs`: Triển khai logic sửa lỗi B-01.
2. `src/backend/tests/OISM.IntegrationTests/WarehouseBranchReassignmentRegressionTests.cs`: Bộ kiểm thử hồi quy độc lập cho AC-01..AC-06 trên PostgreSQL thật.
