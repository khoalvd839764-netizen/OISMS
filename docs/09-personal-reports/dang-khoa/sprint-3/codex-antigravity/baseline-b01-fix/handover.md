# BIÊN BẢN BÀN GIAO SỬA LỖI B-01 (TASK S3-B01-FIX)

| Thuộc tính | Chi tiết |
| :--- | :--- |
| **Mã nhiệm vụ** | `S3-B01-FIX` — Khắc phục lỗi đổi chi nhánh kho sạch |
| **Vai trò thực hiện** | Antigravity |
| **Trạng thái bàn giao** | **`READY_FOR_REVIEW`** *(chờ Codex review/verify, không tự ý ghi VERIFIED)* |
| **Nhánh Git** | `feature/OISMS-40-loi-van-hanh-kho-va-don-hang` |
| **Baseline Head SHA** | `4206cc4b12f0128ee08927dc83550c9e8db5cd6e` |
| **Pull Request** | [PR #21](https://github.com/khoalvd839764-netizen/OISMS/pull/21) |
| **Môi trường kiểm thử** | PostgreSQL 16 (Local Test Container), .NET 8.0 Release mode |

---

## 1. Ma Trận Nghiệm Thu Acceptance Criteria (AC-01 .. AC-07)

| Mã AC | Tiêu chí nghiệm thu | Tên Test Case kiểm chứng thực tế | Kết quả | Ghi chú & Bằng chứng |
| :--- | :--- | :--- | :---: | :--- |
| **AC-01** | Kho sạch đổi sang branch hợp lệ cùng tenant ➔ 200; GET và DB context mới xác nhận BranchId, tên/trạng thái đúng, WarehouseId không đổi. | `WarehouseBranchReassignmentRegressionTests.AC01_CleanWarehouse_ReassignBranch_Returns200_PreservesWarehouseId_UpdatesDbDirectly`<br>`PostgresBackend2RegressionTests.Postgres_Warehouse_ChangeBranch_WhenHasHistory_ReturnsControlled409_AndCleanWarehouseSucceeds` | **PASS** | HTTP 200; `WarehouseId` giữ nguyên; truy vấn DB trực tiếp xác nhận `BranchId` mới và `UpdatedAt` có giá trị. |
| **AC-02** | Kho có lịch sử/chứng từ/số dư liên quan ➔ 409; dữ liệu không thay đổi. Bao gồm lịch sử khi tồn hiện tại bằng 0. | `WarehouseBranchReassignmentRegressionTests.AC02_WarehouseWithHistory_Returns409_AndDataUnchanged_IncludingZeroStock` | **PASS** | Kiểm tra 7 bảng lịch sử phụ thuộc; draft receipt (tồn 0) bị từ chối 409; DB giữ nguyên branch cũ. |
| **AC-03** | Không đổi branch, chỉ sửa tên/trạng thái ➔ vẫn 200. | `WarehouseBranchReassignmentRegressionTests.AC03_SameBranch_UpdateNameAndStatus_Returns200` | **PASS** | EF Core `SaveChangesAsync` chuẩn; Name và IsActive cập nhật thành công 200 OK. |
| **AC-04** | Branch không tồn tại hoặc khác tenant bị từ chối 400; không phát sinh cập nhật chéo tenant. | `WarehouseBranchReassignmentRegressionTests.AC04_TargetBranchDoesNotExist_OrDifferentTenant_Returns400_NoCrossTenantUpdate` | **PASS** | Branch ảo ➔ 400; Branch thuộc Tenant B khi gọi từ Tenant A ➔ 400; DB không bị cập nhật chéo. |
| **AC-05** | Request tới kho thuộc tenant khác không đọc hoặc sửa được dữ liệu kho đó (404 Not Found). | `WarehouseBranchReassignmentRegressionTests.AC05_RequestToWarehouseOfDifferentTenant_Returns404_CannotReadOrModify` | **PASS** | Tenant B gọi GET, PUT, ToggleStatus kho của Tenant A đều nhận 404 Not Found; dữ liệu Tenant A cô lập 100%. |
| **AC-06** | Transaction failure hoặc race với thao tác tạo dữ liệu phụ thuộc không gây cập nhật dở dang, mất lịch sử hoặc orphan FK. | `WarehouseBranchReassignmentRegressionTests.AC06_ConcurrentCreationRace_WithDependentHistory_GuaranteesAtomicityAndIntegrity` | **PASS** | Khóa bi quan `FOR UPDATE` + bắt SQLSTATE 23503; toàn vẹn khóa ngoại (tenant, warehouse, branch) trên PostgreSQL được bảo đảm tuyệt đối. |
| **AC-07** | Test B-01 gốc và toàn bộ suite đạt trên PostgreSQL thật. Không skip test, không hạ assertion. | Toàn bộ suite Unit & Integration Tests trên PostgreSQL thật | **PASS** | targeted test: **1/1 PASS**<br>regression test: **6/6 PASS**<br>full suite: **396/396 PASS (100%)** |

---

## 2. Kết Quả Kiểm Thử Thực Tế Từ Runner / TRX

### 2.1. Targeted Test (Bài test B-01 gốc)
- **Lệnh chạy:**
  ```powershell
  dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --no-build --filter "FullyQualifiedName~Postgres_Warehouse_ChangeBranch_WhenHasHistory_ReturnsControlled409_AndCleanWarehouseSucceeds" --logger trx --results-directory tmp/b01-fix/targeted
  ```
- **File TRX:** `D:\OISMS-b01\tmp\b01-fix\targeted\Jacki_Khoa_KHOA_LE_2026-10-26_16_34_55_net8.0.trx`
- **Kết quả:** `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 5 s`

### 2.2. Dedicated Regression Suite (Bộ kiểm thử AC-01 .. AC-06)
- **Lệnh chạy:**
  ```powershell
  dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --no-build --filter "FullyQualifiedName~WarehouseBranchReassignmentRegressionTests" --logger trx --results-directory tmp/b01-fix/regression
  ```
- **File TRX:** `D:\OISMS-b01\tmp\b01-fix\regression\Jacki_Khoa_KHOA_LE_2026-10-26_16_37_57_net8.0.trx`
- **Kết quả:** `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6, Duration: 22 s`

### 2.3. Full Solution Suite (Toàn bộ Unit & Integration Tests)
- **Lệnh chạy:**
  ```powershell
  dotnet test src/backend/OISM.sln -c Release --no-build --logger trx --results-directory tmp/b01-fix/full
  ```
- **Files TRX:**
  - Unit Tests: `D:\OISMS-b01\tmp\b01-fix\full\Jacki_Khoa_KHOA_LE_2026-10-26_16_38_45_net8.0.trx`
    - `Passed! - Failed: 0, Passed: 129, Skipped: 0, Total: 129, Duration: 900 ms`
  - Integration Tests: `D:\OISMS-b01\tmp\b01-fix\full\Jacki_Khoa_KHOA_LE_2026-10-26_16_38_51_net8.0.trx`
    - `Passed! - Failed: 0, Passed: 267, Skipped: 0, Total: 267, Duration: 15 m 30 s`
- **Tổng cộng:** **396/396 PASS (100%)**, 0 FAILED, 0 SKIPPED.

---

## 3. Danh Sách Tệp Thay Đổi

1. `src/backend/src/OISM.WebApi/Controllers/WarehousesController.cs`:
   - Bổ sung `ICurrentTenantService` vào constructor (fallback `User` claims).
   - Mở rộng kiểm tra dữ liệu phụ thuộc trên cả 7 bảng lịch sử (`GoodsReceipts`, `WarehouseInventoryBalances`, `InventoryTransactions`, `Reservations`, `StockTransfers`, `StockCounts`, `InventoryAdjustments`).
   - Khóa dòng bi quan PostgreSQL: `SELECT id FROM warehouses WHERE id = {id} AND tenant_id = {tenantId} FOR UPDATE`.
   - Cập nhật kho sạch qua parameterized SQL trực tiếp: `UPDATE warehouses SET branch_id = ..., name = ..., is_active = ..., updated_at = ... WHERE id = ... AND tenant_id = ...`.
   - Detach tracked entity khỏi Change Tracker để chống xung đột key.
   - Bắt cụ thể `Npgsql.PostgresException` và `DbUpdateException` mã `23503` trả về `409 Conflict`.
   - Giữ nguyên luồng cập nhật thường (EF Core) khi không đổi `BranchId`.
2. `src/backend/tests/OISM.IntegrationTests/WarehouseBranchReassignmentRegressionTests.cs`:
   - 6 bài test độc lập bao phủ toàn bộ AC-01 .. AC-06 trên PostgreSQL thật.
3. `docs/09-personal-reports/dang-khoa/sprint-3/codex-antigravity/baseline-b01-fix/README.md`:
   - Báo cáo phân tích nguyên nhân gốc và giải pháp khắc phục.
4. `docs/09-personal-reports/dang-khoa/sprint-3/codex-antigravity/baseline-b01-fix/handover.md`:
   - Biên bản bàn giao, ma trận AC, kết quả runner thực tế.
5. `docs/09-personal-reports/dang-khoa/sprint-3/SUMMARY.md`:
   - Cập nhật mục 4 và mục 5 xác nhận B-01 đã được giải quyết hoàn toàn, 396/396 test PASS.

---

## 4. Giới Hạn & Cam Kết Kỹ Thuật

- **Bảo toàn schema:** Không sửa đổi, xóa bỏ hay làm lỏng các ràng buộc khóa ngoại (FK) hay alternate key trên PostgreSQL.
- **Bảo toàn tenant guard:** Toàn bộ câu lệnh SQL và EF query đều bắt buộc có `tenant_id` scope.
- **Không tự ý merge:** Chỉ push lên nhánh `feature/OISMS-40-loi-van-hanh-kho-va-don-hang` để cập nhật PR #21; không merge vào `develop`.
- **Bảo toàn worktree khác:** Thư mục chính `D:/OISMS` (nhánh Nhựt `feature/OISMS-42-sprint-3-backend-2`) được bảo vệ nguyên vẹn 100%, không bị ảnh hưởng.
