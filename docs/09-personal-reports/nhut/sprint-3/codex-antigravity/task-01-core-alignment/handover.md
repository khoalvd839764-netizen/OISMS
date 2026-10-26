# Bàn Giao S3-BE2-T01 — Hợp Nhất Baseline và Một Lõi Ghi Tồn

**Phiên bản:** v1.3 (Sửa theo Codex Review R3)  
**Ngày:** 2026-10-26  
**Vai trò thực hiện:** Antigravity  
**Trạng thái bàn giao:** `READY_FOR_REVIEW v1.3` (Bàn giao Codex review, không tự ghi VERIFIED, không merge develop)  
**Nhánh công tác:** `feature/OISMS-42-sprint-3-backend-2`  
**PR mục tiêu:** [#20](https://github.com/khoalvd839764-netizen/OISMS/pull/20)  

---

## 1. Thông Tin Baseline & Lịch Sử Review

- **Reviewed Code Commit (Review R3):** `db889057940d4ca43fd400b6ce424db6401d4c4a`
- **Reviewed Documentation Head:** `f9db45a1808f2d25ec8ea7bf7aae515491e14d50`
- **Tài liệu tham chiếu:**
  - Brief gốc: [brief.md](brief.md)
  - Đánh giá Codex Review R1: [reviews/v1.0-review-r1.md](reviews/v1.0-review-r1.md)
  - Brief sửa v1.1: [briefs/v1.1-fix-r1.md](briefs/v1.1-fix-r1.md)
  - Đánh giá Codex Review R2: [reviews/v1.1-review-r2.md](reviews/v1.1-review-r2.md)
  - Brief sửa v1.2: [briefs/v1.2-fix-r2.md](briefs/v1.2-fix-r2.md)
  - Đánh giá Codex Review R3: [reviews/v1.2-review-r3.md](reviews/v1.2-review-r3.md)
  - Brief sửa v1.3: [briefs/v1.3-fix-r3.md](briefs/v1.3-fix-r3.md)
  - Bằng chứng Reviewer R1: [evidence/review-r1/README.md](evidence/review-r1/README.md)
  - Bằng chứng Reviewer R2: [evidence/review-r2/README.md](evidence/review-r2/README.md)
  - Bằng chứng Reviewer R3: [evidence/review-r3/README.md](evidence/review-r3/README.md), [probe-results.json](evidence/review-r3/probe-results.json), [ReviewerProbes.cs](evidence/review-r3/ReviewerProbes.cs)
- **Bảo toàn workspace ngoài phạm vi:**
  - `docs/11-ui-screens/` và `docs/12-error-and-status-codes/` giữ nguyên trạng thái untracked, tuyệt đối không stage hay commit.
  - Baseline BE1 có fix B-01 (`WarehouseBranchReassignmentRegressionTests`) được bảo toàn 100%.
  - Giữ nguyên các phần đã đạt:
    - **BE2-T01-F01 (Process order lifecycle):** CLOSED. Dùng lõi confirm của BE1 (`Reserved` -> `Confirmed`, không có `Processing`, không đổi schema DB, advisory lock an toàn).
    - **BE2-T01-F02 (Batch auto-provision credentials):** CLOSED. Không tự động cấp secret ngầm; thiếu `ShopIntegration` trả 404 `INTEGRATION_NOT_FOUND` có kiểm soát; secret cố định cũ trả 401; credential riêng hợp lệ trả 201.
    - **Same-total changed-items payload mismatch:** Trả 409 `WEBHOOK_PAYLOAD_MISMATCH` khi đổi cấu trúc giỏ hàng dù cùng tổng tiền.

---

## 2. Nguyên Nhân Gốc & Giải Pháp Kỹ Thuật (Fix v1.3)

Chỉ xử lý finding còn mở duy nhất: **BE2-F06** với hai nhánh lỗi liên quan đến mapping và validation đầu vào:

### 2.1. Nhánh 1: Thiếu Timestamp & Retry Bằng DTO Mới
- **Nguyên nhân gốc:**
  - Trong `SimulatorService.IngestWebhookOrderAsync`, dòng `payload.OrderTimestamp ??= DateTimeOffset.UtcNow;` đã trực tiếp gán giá trị thời gian hiện tại vào object `payload` truyền vào nếu nó mang giá trị `null`.
  - Hậu quả:
    1. Khi gọi Ingest lần đầu với payload thiếu timestamp, adapter tự động gán $T_1$ và lưu order thành công. Nếu caller retry cùng dữ liệu bằng một instance DTO mới (ví dụ parse lại từ JSON), instance mới chưa có timestamp sẽ được gán $T_2 \neq T_1$.
    2. Vì `OrderTimestamp` nằm trong chuỗi băm Canonical Signature SHA-256 (`WebhookCanonicalSignatureHelper`), chữ ký tại $T_2$ khác $T_1$, dẫn đến adapter ném lỗi sai lệch `WEBHOOK_PAYLOAD_MISMATCH` (409) thay vì nhận diện đây là lỗi input không hợp lệ ngay từ đầu.
    3. Trong khi đó, Ingress canonical qua HTTP `/api/webhooks/simulator/Shopee` khi nhận timestamp thiếu hoặc null thì trả ngay 400 Bad Request `VALIDATION_ERROR`, tạo ra sự bất nhất giữa Adapter và Canonical Ingress.
- **Giải pháp khắc phục:**
  - Gỡ bỏ hoàn toàn dòng lệnh tự gán/mutate `payload.OrderTimestamp ??= DateTimeOffset.UtcNow;`.
  - Truyền trực tiếp `payload.OrderTimestamp` sang `canonicalDto.OrderTimestamp`.
  - Bộ kiểm tra dùng chung `WebhookCanonicalValidator.ValidateAndCalculateTotal` sẽ từ chối mọi timestamp mang giá trị null, default hoặc rỗng bằng mã lỗi chuẩn hóa `VALIDATION_ERROR` (400) trước khi có bất kỳ truy vấn DB hay ghi nhận đơn/giữ chỗ nào.
  - Các caller mô phỏng (như `RunConcurrencySimulationAsync`) chủ động truyền timestamp tường minh từ caller một lần; retry sự kiện giữ nguyên timestamp đó.
  - Cập nhật các fixture kiểm thử hợp lệ cung cấp `OrderTimestamp` tường minh; giữ nguyên các test missing/default timestamp để chứng minh bị từ chối 400.

### 2.2. Nhánh 2: Xử Lý An Toàn `Items` Chứa Phần Tử `null` Trước Validator
- **Nguyên nhân gốc:**
  - Trong `SimulatorService.IngestWebhookOrderAsync`, trước khi gọi `WebhookCanonicalValidator`, code thực hiện:
    `TotalAmount = payload.Items != null ? payload.Items.Sum(i => i.Quantity * i.UnitPrice) : null`
    và `Items = payload.Items?.Select(i => new CanonicalWebhookItemDto { SkuCode = i.SkuCode... }).ToList()`.
  - Nếu `payload.Items` có chứa phần tử `null` (ví dụ `Items = [null]`), việc dereference `i.Quantity` và `i.SkuCode` gây ra `NullReferenceException` (HTTP 500) trước khi dữ liệu kịp đến `WebhookCanonicalValidator`.
- **Giải pháp khắc phục:**
  - Kiểm tra `payload.Items.Any(i => i == null)` trước khi tính tổng hoặc duyệt dòng hàng:
    - Nếu có item null, gán `totalAmount = 0m` và map an toàn `i == null ? (CanonicalWebhookItemDto)null! : ...` mà không dereference thuộc tính của phần tử null.
    - Khi đó, `canonicalDto.Items` chứa phần tử null được chuyển an toàn sang `WebhookCanonicalValidator.ValidateAndCalculateTotal`.
    - `WebhookCanonicalValidator` phát hiện `dto.Items.Any(i => i == null)` và ném lỗi có kiểm soát:
      `OismException("VALIDATION_ERROR", "Dòng sản phẩm trong đơn hàng không được mang giá trị null.", 400);`
    - Đảm bảo 100% không phát sinh `NullReferenceException`, không lọc bỏ item lỗi ngầm, không tạo order/giữ chỗ dở dang, đồng nhất tuyệt đối với Canonical Ingress.
  - Thêm khối `checked` khi tính `totalAmount` để phòng ngừa lỗi tràn số `OverflowException`.

---

## 3. Ma Trận Nghiệm Thu (Acceptance Criteria) Fix v1.3

| AC | Nội dung điều kiện đạt | Trạng thái | Bằng chứng kiểm thử / Test case |
| :--- | :--- | :--- | :--- |
| **R3-AC-01** | Adapter và canonical ingress với timestamp missing/default đều trả validation 400 theo contract; không order/reservation mới, không mutate timestamp đầu vào. | **PASS** | `OrderProcessAndSimulatorRegressionTests.R3_AC01_AC03_SimulatorAdapter_MissingTimestamp_TwoIndependentPayloads_BothRejected400_NoPersistOrMismatch`<br>`ReviewerProbes.SimulatorAdapter_MissingTimestamp_NewPayloadInstances_MustMatchCanonicalRejection`: PASS 400, `payload.OrderTimestamp` vẫn null. |
| **R3-AC-02** | Hai DTO mới deserialize từ cùng body có timestamp hợp lệ, cùng identity: replay trả cùng order, giữ chỗ một lần. Không chỉ test gọi lại cùng object. | **PASS** | `OrderProcessAndSimulatorRegressionTests.R3_AC02_SimulatorAdapter_TwoNewInstances_SameValidTimestamp_IdempotentReplayReturnsSameOrder`: 2 instance DTO độc lập cùng trả Order ID cũ, `Reserved = 2` (không bị nhân đôi). |
| **R3-AC-03** | Hai body tương đương cùng thiếu timestamp đều bị từ chối 400; không persist lần đầu rồi báo mismatch 409 ở retry. | **PASS** | `OrderProcessAndSimulatorRegressionTests.R3_AC01_AC03_...`: Cả lần 1 và lần 2 đều bị từ chối 400 `VALIDATION_ERROR`, 0 order được lưu vào database. |
| **R3-AC-04** | Null list/empty list/null item/SKU rỗng xử lý có kiểm soát qua validation phù hợp; đặc biệt null item không gây NullReferenceException, không side effect. | **PASS** | `OrderProcessAndSimulatorRegressionTests.R3_AC04_SimulatorAdapter_NullItemInList_ThrowsControlledValidationError_NoNullReferenceExceptionOrSideEffect`<br>`ReviewerProbes.SimulatorAdapter_NullItem_MustReachCanonicalValidation`: Cả Adapter và Ingress ném `VALIDATION_ERROR` (400), 0 order persist, không có `NullReferenceException`. |
| **R3-AC-05** | Giữ process/core, same-total mismatch 409, batch thiếu config 404/no provisioning, secret cố định 401 và credential riêng hợp lệ; không mở lại F01/F02. | **PASS** | `ProcessEndpoint_OnRealPostgres_MustNotReturn500OrPartiallyConfirm`: PASS 200.<br>`SimulatorAdapter_SameTotalChangedItems_MustRejectPayloadMismatch`: PASS 409.<br>`Batch_MustNotProvisionIntegrationWithPredictableAnonymousCredential`: PASS 404 & 401 & 201.<br>`SimulatorAdapter_DefaultTimestamp_MustApplyCanonicalValidation`: PASS 400. |

---

## 4. Kết Quả Kiểm Thử Thực Tế

### 4.1. Reviewer Probes Đối Chứng (PostgreSQL Thật - Toàn Bộ 6/6 PASS)
```powershell
dotnet test tmp/codex-be2-t01-review-r3/probe/ReviewerProbes.csproj -c Release -p:BuildProjectReferences=false --logger trx --results-directory tmp/codex-be2-t01-review-r3/probe-results --verbosity minimal
```
- **Kết quả:** **6/6 test cases PASSED (100%)**, 0 Failed, 0 Aborted (Thời lượng: 23s):
  1. `ProcessEndpoint_OnRealPostgres_MustNotReturn500OrPartiallyConfirm`: **PASSED** (HTTP 200, Confirmed, OnHand=8, Reserved=0, 1 OUT row).
  2. `SimulatorAdapter_SameTotalChangedItems_MustRejectPayloadMismatch`: **PASSED** (`WEBHOOK_PAYLOAD_MISMATCH`, 409).
  3. `Batch_MustNotProvisionIntegrationWithPredictableAnonymousCredential`: **PASSED** (404 khi thiếu cấu hình, 401 với secret cũ, 201 với secret riêng hợp lệ).
  4. `SimulatorAdapter_DefaultTimestamp_MustApplyCanonicalValidation`: **PASSED** (400 `VALIDATION_ERROR`, 0 đơn được tạo).
  5. `SimulatorAdapter_MissingTimestamp_NewPayloadInstances_MustMatchCanonicalRejection`: **PASSED** (Cả 2 instance DTO mới đều ném 400 `VALIDATION_ERROR`, `OrderTimestamp` không bị inject ngầm, Ingress trả 400, 0 đơn trong DB).
  6. `SimulatorAdapter_NullItem_MustReachCanonicalValidation`: **PASSED** (Adapter ném 400 `VALIDATION_ERROR`, không văng `NullReferenceException`, Ingress trả 400, 0 đơn trong DB).

### 4.2. Bộ Kiểm Thử Hồi Quy Mở Rộng (15 Test Cases Trên PostgreSQL Thật)
```powershell
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --no-build --filter "FullyQualifiedName~OrderProcessAndSimulatorRegressionTests"
```
- **Kết quả:** **15/15 PASSED (100%)**, 0 Failed, 0 Skipped (Thời lượng: 53s).

### 4.3. Unit Tests Suite
```powershell
dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj -c Release --no-build
```
- **Kết quả:** **146/146 PASSED (100%)**, 0 Failed, 0 Skipped (Thời lượng: 2s).

### 4.4. Targeted Integration Suites
```powershell
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~OrderProcessAndSimulatorRegressionTests|FullyQualifiedName~Webhook|FullyQualifiedName~WarehouseBranchReassignment|FullyQualifiedName~Postgres_Warehouse_ChangeBranch"
```
- **Kết quả:** **44/44 PASSED (100%)**, 0 Failed, 0 Skipped (Thời lượng: 2m 52s).

### 4.5. Toàn Bộ Integration Tests Suite (PostgreSQL Thật)
```powershell
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --no-build --no-restore
```
- **Kết quả:** **282/282 PASSED (100%)**, 0 Failed, 0 Skipped (Thời lượng: 21m 38s).
- **Tổng kiểm thử giải pháp:** **428/428 tests PASSED (100%)** (146 Unit Tests + 282 Integration Tests).

---

## 5. Giới Hạn Phạm Vi & Kế Hoạch Tiếp Theo

1. **Phạm vi hoàn tất:** Đã đóng hoàn toàn nhánh cuối cùng của **BE2-F06** theo đúng tiêu chuẩn nghiệm thu R3 trong Brief Fix v1.3.
2. **Tuân thủ quy ước:**
   - Không tự ý tuyên bố `VERIFIED`. Bàn giao với trạng thái `READY_FOR_REVIEW v1.3` cho Codex thẩm tra độc lập.
   - Không merge vào nhánh `develop`.
   - Giữ nguyên các thư mục untracked ngoài phạm vi: `docs/11-ui-screens/` và `docs/12-error-and-status-codes/`.
3. **Các task tiếp theo:**
   - **T02 (Read/Report Endpoints):** Bắt đầu triển khai sau khi Codex nghiệm thu T01 v1.3.
   - **T03–T06:** Giữ trạng thái `PLANNED` theo kế hoạch sprint.
