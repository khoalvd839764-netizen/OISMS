# Task 4B: Warehouse Stocktaking & Variance Reconciliation — Index & Trạng Thái Hiện Hành

| Thông tin kiểm soát Task 4B | Chi tiết |
| :--- | :--- |
| **Mã Task (Task ID)** | `S3-BE1-T04B-STOCK-COUNT` |
| **Chủ đề** | Kiểm kê kho & Cân bằng tồn (`Warehouse Stocktaking & Variance Reconciliation`) |
| **Phiên bản hiện hành** | `v1.3` |
| **Trạng thái nghiệm thu** | **VERIFIED v1.3** *(Nghiệm thu bởi Codex vòng 4, ngày 25/10/2026)* |
| **Baseline** | Task 4A (`S3-BE1-T04A-STOCK-TRANSFER`) + Sprint 2 |
| **Người triển khai** | Lê Võ Đăng Khoa (Backend 1 Lead & Team Lead) |
| **Reviewer** | Codex (`codex-antigravity-workflow`) |
| **Ngày hoàn thành** | 25/10/2026 |

---

## 1. Mục tiêu & Phạm vi

Xây dựng phân hệ kiểm kê kho và cân bằng sai lệch tồn kho (`FR-INV-04`):
1. **Khởi tạo đợt kiểm kê (`Create`):** Tạo phiếu kiểm kê cho toàn bộ kho (`ALL`) hoặc nhóm SKU chọn lọc (`PARTIAL`), snapshot số tồn sổ sách hiện tại (`SystemQuantity`).
2. **Ghi nhận số lượng thực tế (`Record`):** Cho phép nhân viên nhập số kiểm đếm thực tế (`ActualQuantity`), tự động tính độ lệch (`VarianceQuantity = Actual - System`).
3. **Cân bằng tồn nguyên tử (`Confirm`):** Tự động sinh phiếu điều chỉnh (`StockAdjustment`), ghi nhận bút toán `StockLedger` (Type `STOCK_ADJUSTMENT_IN` nếu thừa, `STOCK_ADJUSTMENT_OUT` nếu thiếu), cập nhật lại số dư `OnHand` khớp số thực tế.
4. **Hủy đợt kiểm kê (`Cancel`):** Hủy bỏ đợt kiểm kê chưa duyệt mà không gây biến động tồn kho hoặc sổ cái.
5. **Khóa chống cạnh tranh & Xung đột:** Đảm bảo an toàn tuyệt đối khi vừa duyệt kiểm kê vừa có giao dịch bán lẻ POS, giữ chỗ online, hoặc chuyển kho.

---

## 2. Bảng quản lý Findings (T4B-F01 .. T4B-F05)

| Finding ID | Độ ưu tiên | AC liên quan | Mô tả vấn đề & Giải pháp xử lý | Trạng thái |
| :---: | :---: | :---: | :--- | :---: |
| **T4B-F01** | P1 | `AC-01`<br/>`AC-02` | **Snapshot số tồn & Validation:** Snapshot chính xác `SystemQuantity`, chặn tạo đợt trùng lặp trên cùng kho khi đợt cũ chưa hoàn tất. | **CLOSED**<br/>*(Đóng tại v1.1)* |
| **T4B-F02** | P1 | `AC-03` | **Record & Variance Tracking:** Lưu số lượng đếm, cho phép đếm lại nhiều lần trước khi chốt, tính sai lệch tức thời. | **CLOSED**<br/>*(Đóng tại v1.1)* |
| **T4B-F03** | P1 | `AC-04` | **Atomic Adjustment & Ledger:** Tự động tạo `StockAdjustment` và ghi `StockLedger` IN/OUT đúng giá vốn WAC trong 1 transaction. | **CLOSED**<br/>*(Đóng tại v1.1)* |
| **T4B-F04** | P1 | `AC-05`<br/>`AC-06` | **Cross-Flow Lock & Drift Detection:** Phát hiện biến động tồn kho sau khi snapshot, áp dụng pessimistic lock chống race condition. | **CLOSED**<br/>*(Đóng tại v1.2)* |
| **T4B-F05** | P1 | `AC-07`..`AC-12` | **5 kịch bản Integration Tests nâng cao:**<br/>1. SKU mới xuất hiện trong đợt ALL phải được khóa trước snapshot.<br/>2. Admin tạo giữ chỗ cạnh tranh confirm kiểm kê (POST /api/orders).<br/>3. Transfer đảo SKU: kết quả theo lịch hợp lệ.<br/>4. Hai counts cùng SKU chỉ điều chỉnh một lần.<br/>5. Fault create/record/confirm và clean retry. | **CLOSED**<br/>*(Đóng tại v1.3)* |

---

## 3. Bằng chứng kiểm thử & Xác thực theo Provider

| Phân loại kiểm chứng | Lệnh / Kịch bản thực tế | Kết quả thực tế | Provider / Môi trường |
| :--- | :--- | :--- | :--- |
| **Build Solution** | `dotnet build src/backend/OISM.sln -c Release` | **PASS**, 0 warning, 0 error (3.14s) | .NET 8 SDK / Local |
| **StockCount Integration** | `dotnet test --filter FullyQualifiedName~StockCount` | **51/51 PASS** (100%, 4.22m) | PostgreSQL 16 Alpine thật |
| **Unit Tests** | Kế thừa từ suite v1.2 | **117/117 PASS** (100%) | .NET 8 / InMemory |
| **Cross-Flow Tests** | Kế thừa cross-flow kiểm kê/POS/transfer/online | **148/148 PASS** (100%) | PostgreSQL 16 Alpine thật |

---

## 4. Danh mục tài liệu & Lịch sử các vòng

* **Handoff v1.0:** [`handoffs/v1.0-handoff.md`](./handoffs/v1.0-handoff.md) *(Mục 21: Báo cáo triển khai Task 4B v1.0)*
* **Review v1.0 (Codex R1):** [`reviews/v1.0-review-r1.md`](./reviews/v1.0-review-r1.md) *(Mục 22: Codex review vòng 1 — CHANGES_REQUESTED)*
* **Handoff v1.1:** [`handoffs/v1.1-handoff.md`](./handoffs/v1.1-handoff.md) *(Mục 23: Bàn giao fix review vòng 1 v1.1)*
* **Review v1.1 (Codex R2):** [`reviews/v1.1-review-r2.md`](./reviews/v1.1-review-r2.md) *(Mục 24: Codex review vòng 2 — CHANGES_REQUESTED)*
* **Handoff v1.2:** [`handoffs/v1.2-handoff.md`](./handoffs/v1.2-handoff.md) *(Mục 25: Bàn giao vòng 3 v1.2)*
* **Review v1.2 (Codex R3):** [`reviews/v1.2-review-r3.md`](./reviews/v1.2-review-r3.md) *(Mục 26: Codex review vòng 3 — CHANGES_REQUESTED)*
* **Handoff v1.3:** [`handoffs/v1.3-handoff.md`](./handoffs/v1.3-handoff.md) *(Mục 27: Bàn giao vòng 4 v1.3 hoàn thiện T4B-F05)*
* **Review v1.3 (Codex R4):** [`reviews/v1.3-review-r4.md`](./reviews/v1.3-review-r4.md) *(Mục 28: Kết luận nghiệm thu độc lập VERIFIED v1.3)*
