# Task 4A: Inter-Warehouse Stock Transfer — Index & Trạng Thái Hiện Hành

| Thông tin kiểm soát Task 4A | Chi tiết |
| :--- | :--- |
| **Mã Task (Task ID)** | `S3-BE1-T04A-STOCK-TRANSFER` |
| **Chủ đề** | Chuyển kho nội bộ 2 bước (`Two-Step Stock Transfer`) |
| **Phiên bản hiện hành** | `v1.2` |
| **Trạng thái nghiệm thu** | **VERIFIED v1.2** *(Nghiệm thu bởi Codex vòng 3, ngày 24/10/2026)* |
| **Baseline** | Task 3 (`S3-BE1-T03-ORDER-LIFECYCLE`) + Sprint 2 |
| **Người triển khai** | Lê Võ Đăng Khoa (Backend 1 Lead & Team Lead) |
| **Reviewer** | Codex (`codex-antigravity-workflow`) |
| **Ngày hoàn thành** | 24/10/2026 |

---

## 1. Mục tiêu & Phạm vi

Hiện thực hóa quy trình chuyển kho nội bộ hai bước (`FR-INV-03`) bảo toàn tính toàn vẹn số lượng và giá trị vốn:
1. **Bước 1 — Xuất điều chuyển (`Dispatch`):** Kiểm tra tồn khả dụng kho nguồn, trừ `OnHand` nguồn, đưa vào trạng thái `InTransit`, ghi bút toán `StockLedger` (Type `TRANSFER_OUT`) với giá vốn xuất kho theo WAC nguồn.
2. **Bước 2 — Nhập điều chuyển (`Receive`):** Nhận hàng tại kho đích, tăng `OnHand` đích, tính toán lại giá vốn bình quân gia quyền tại chi nhánh nhận (`WAC Recalculation`), ghi bút toán `StockLedger` (Type `TRANSFER_IN`).
3. **Từ chối / Hủy điều chuyển (`Reject / Cancel`):** Hoàn trả hàng về kho nguồn nếu kho đích từ chối hoặc hủy lệnh, ghi `TRANSFER_REVERSAL`.
4. **Phòng chống Deadlock:** Sắp xếp thứ tự khóa các SKU (`Ordered SKU Locking`) khi chuyển nhiều mặt hàng qua lại giữa các kho.
5. **Đảm bảo bảo mật đa chi nhánh & Tenant Isolation:** Chỉ người có quyền tại kho nguồn mới được Dispatch, người có quyền kho đích mới được Receive.

---

## 2. Bảng quản lý Findings (T4A-F01 .. T4A-F07)

| Finding ID | Độ ưu tiên | AC liên quan | Mô tả vấn đề & Giải pháp xử lý | Trạng thái |
| :---: | :---: | :---: | :--- | :---: |
| **T4A-F01** | P1 | `AC-01` | **Validation phiếu chuyển:** Chặn chuyển cùng 1 kho, số lượng <= 0, hoặc kho không cùng một tenant. | **CLOSED**<br/>*(Đóng tại v1.1)* |
| **T4A-F02** | P1 | `AC-02`<br/>`AC-03` | **Atomic Dispatch & InTransit:** Trừ OnHand nguồn và ghi nhận InTransit trong 1 transaction an toàn. | **CLOSED**<br/>*(Đóng tại v1.1)* |
| **T4A-F03** | P1 | `AC-04`<br/>`AC-05` | **WAC Recalculation on Receive:** Tính lại WAC đích theo công thức `(Tồn cũ * WAC cũ + Nhập mới * WAC nguồn) / Tổng tồn mới`. | **CLOSED**<br/>*(Đóng tại v1.2)* |
| **T4A-F04** | P1 | `AC-06` | **Reject / Cancel Reversal:** Hoàn trả chính xác số lượng về kho ban đầu, triệt tiêu trạng thái InTransit. | **CLOSED**<br/>*(Đóng tại v1.1)* |
| **T4A-F05** | P1 | `AC-07` | **Cross-Warehouse Deadlock Prevention:** Sắp xếp danh sách khóa theo `WarehouseId` và `SkuId` tăng dần trước khi acquire pessimistic lock. | **CLOSED**<br/>*(Đóng tại v1.2)* |
| **T4A-F06** | P2 | `AC-08` | **Idempotency & Double Receive Guard:** Chặn đứng việc gọi lặp Dispatch/Receive nhiều lần gây sai lệch sổ cái. | **CLOSED**<br/>*(Đóng tại v1.2)* |
| **T4A-F07** | P2 | `AC-09` | **Branch & Tenant Boundary Guard:** Áp dụng kiểm tra quyền chi nhánh nghiêm ngặt trước khi duyệt thao tác. | **CLOSED**<br/>*(Đóng tại v1.2)* |

---

## 3. Bằng chứng kiểm thử & Xác thực theo Provider

| Phân loại kiểm chứng | Lệnh / Kịch bản thực tế | Kết quả thực tế | Provider / Môi trường |
| :--- | :--- | :--- | :--- |
| **Build Solution** | `dotnet build src/backend/OISM.sln -c Release` | **PASS**, 0 warning, 0 error | .NET 8 SDK / Local |
| **StockTransfer Integration** | `dotnet test --filter FullyQualifiedName~StockTransfer` | **28/28 PASS** (100%) | PostgreSQL 16 Alpine thật |
| **Cross-flow Regression** | PosCheckout + OnlineOrder + StockTransfer | **168/169 PASS** *(chỉ B-01 fail)* | PostgreSQL 16 Alpine thật |
| **Unit Tests** | Kế thừa từ suite kiểm thử v1.1 | **117/117 PASS** (100%) | .NET 8 / InMemory |

---

## 4. Danh mục tài liệu & Lịch sử các vòng

* **Handoff v1.0:** [`handoffs/v1.0-handoff.md`](./handoffs/v1.0-handoff.md) *(Mục 15: Báo cáo triển khai Task 4A v1.0)*
* **Review v1.0 (Codex R1):** [`reviews/v1.0-review-r1.md`](./reviews/v1.0-review-r1.md) *(Mục 16: Codex review vòng 1 — CHANGES_REQUESTED)*
* **Handoff v1.1:** [`handoffs/v1.1-handoff.md`](./handoffs/v1.1-handoff.md) *(Mục 17: Báo cáo sửa review vòng 1 v1.1)*
* **Review v1.1 (Codex R2):** [`reviews/v1.1-review-r2.md`](./reviews/v1.1-review-r2.md) *(Mục 18: Codex review vòng 2 — CHANGES_REQUESTED)*
* **Handoff v1.2:** [`handoffs/v1.2-handoff.md`](./handoffs/v1.2-handoff.md) *(Mục 19: Báo cáo hoàn thiện kiểm chứng v1.2)*
* **Review v1.2 (Codex R3):** [`reviews/v1.2-review-r3.md`](./reviews/v1.2-review-r3.md) *(Mục 20: Kết luận nghiệm thu độc lập VERIFIED v1.2)*
