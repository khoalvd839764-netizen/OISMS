# Task 6: Hangfire Reservation Expiry Worker — Index & Trạng Thái Hiện Hành

| Thông tin kiểm soát Task 6 | Chi tiết |
| :--- | :--- |
| **Mã Task (Task ID)** | `S3-BE1-T06-RESERVATION-EXPIRY` |
| **Chủ đề** | Tự động Hủy Phiếu Giữ Chỗ Quá Hạn bằng Hangfire Worker (`FR-SIM-03`) |
| **Phiên bản hiện hành** | `v1.2` |
| **Trạng thái nghiệm thu** | **VERIFIED v1.2** *(Nghiệm thu bởi Codex R3, ngày 26/10/2026)* |
| **Baseline** | Task 5 (`S3-BE1-T05-WEBHOOK-REALTIME`) + Sprint 2 |
| **Người triển khai** | Lê Võ Đăng Khoa (Backend 1 Lead & Team Lead) |
| **Reviewer** | Codex (`codex-antigravity-workflow`) |
| **Ngày hoàn thành** | 26/10/2026 |

---

## 1. Mục tiêu & Phạm vi

Xây dựng background worker chạy định kỳ tự động hủy các đơn giữ chỗ trực tuyến quá hạn (`FR-SIM-03`):
1. **Hangfire Recurring Job (`IReservationExpiryJob`):** Lên lịch quét mỗi 1 phút (`* * * * *`), phát hiện các đơn ở trạng thái `Reserved` có thời gian giữ chỗ vượt quá 15 phút (`Reservation.ExpiresAt < UtcNow`).
2. **Hủy đơn & Hoàn trả tồn nguyên tử:** Chuyển trạng thái đơn sang `Cancelled`, cập nhật phiếu giữ chỗ sang `Expired`, hoàn trả toàn bộ số lượng về tồn khả dụng (`Available`), giảm số dư `Reserved` tương ứng ở cả cấp kho (`Warehouse`) và chi nhánh (`Branch`).
3. **Phát sự kiện SignalR nhóm theo SKU:** Đẩy thông báo `StockChanged` tới các client đang kết nối, tự động gộp các biến động cùng SKU trong 1 đơn hàng để tối ưu băng thông.
4. **Cô lập Đa Tenant & Công bằng tài nguyên (Fairness):** Quét lần lượt từng tenant với batch size giới hạn (mặc định 50 đơn/lần), áp dụng con trỏ keyset pagination để không bỏ đói các tenant phía sau khi tenant đầu gặp lỗi.
5. **Cơ chế chịu lỗi & Báo cáo trạng thái chính xác:** Phân biệt rành mạch giữa `Succeeded` (no-op hoặc hoàn tất hợp lệ) và `Failed` (lỗi kết nối, lỗi transaction).

---

## 2. Bảng quản lý Findings (T6-F01 .. T6-F04)

| Finding ID | Độ ưu tiên | AC liên quan | Mô tả vấn đề & Giải pháp xử lý | Trạng thái |
| :---: | :---: | :---: | :--- | :---: |
| **T6-F01** | P1 | `AC-01`<br/>`AC-06`<br/>`AC-07` | **Bounded Scan, Keyset Cursor & Tenant Fairness:** Giới hạn kích thước batch, tiến trình con trỏ keyset đảm bảo một nhóm đơn lỗi không gây nghẽn toàn bộ worker hoặc bỏ đói tenant khác. | **CLOSED**<br/>*(Đóng tại R2)* |
| **T6-F02** | P1 | `AC-02`<br/>`AC-05`<br/>`AC-09` | **Outcome Scoped & Failure Reporting:** Chuyển `LastExecutionStats` thành thuộc tính scoped trên `IOrderService` instance. Bỏ điều kiện `totalScanned > 0` giúp bắt lỗi query candidate báo `Failed` đúng, không nuốt lỗi thành job success. | **CLOSED**<br/>*(Đóng tại R3)* |
| **T6-F03** | P1 | `AC-03`<br/>`AC-08`<br/>`AC-09`<br/>`AC-11` | **4 Điểm Kiểm chứng Chuyên biệt:**<br/>1. *AC08:* Gated multi-SKU cross-flow lock overlap với PostgreSQL `wait_event=advisory`.<br/>2. *AC09:* Snapshot rollback độc lập tách biệt SKU lỗi và SKU hợp lệ.<br/>3. *AC11:* Cancellation token gắn đúng order, dọn sạch clock order phase 1.<br/>4. *AC03:* Phân bổ 1 SKU qua nhiều kho (active/inactive) và gộp notification theo SKU. | **CLOSED**<br/>*(Đóng tại R3)* |
| **T6-F04** | P2 | `AC-04`<br/>`AC-12` | **Audit ma trận test & Handoff trung thực:** Bảng đối soát 11/11 test methods thật khớp 100% source, đúng nhóm 12 AC brief v1.0, ghi nhận minh bạch provider và log. | **CLOSED**<br/>*(Đóng tại R2)* |

---

## 3. Bằng chứng kiểm thử & Xác thực theo Provider

| Phân loại kiểm chứng | Lệnh / Kịch bản thực tế | Kết quả thực tế | Provider / Môi trường |
| :--- | :--- | :--- | :--- |
| **Build Solution** | `dotnet build src/backend/OISM.sln -c Release` | **PASS**, 0 warning, 0 error (2.26s) | .NET 8 SDK / Local |
| **Unit Tests** | `dotnet test --filter Category=Unit` | **129/129 PASS** (100%, 151ms) | .NET 8 / InMemory |
| **Reservation Expiry Tests** | `FullyQualifiedName~ReservationExpiryIntegrationTests` | **11/11 PASS** (100%) | PostgreSQL 16 Alpine thật |
| **Full Integration Suite** | Toàn bộ 261 test tích hợp solution OISM | **260/261 PASS**, 1 FAIL *(chỉ B-01 fail)*, 0 skipped (13m48s) | PostgreSQL 16 Alpine thật |
| **Task 5 & Task 6 Suite** | 11 Expiry + 14 Webhook + 4 Hub + 8 Simulator | **37/37 PASS** (trích xuất từ lượt full) | PostgreSQL & InMemory |
| **Reviewer Probe Scenarios** | 4/4 kịch bản probe F02 và positive controls | **4/4 PASS** (Job Failed đúng, partial commit đúng) | Hangfire Worker / PostgreSQL |

---

## 4. Danh mục tài liệu & Lịch sử các vòng

* **Brief gốc:** [`briefs/v1.0-brief.md`](./briefs/v1.0-brief.md) *(Đặc tả yêu cầu Task 6 v1.0)*
* **Handoff v1.0:** [`handoffs/v1.0-handoff.md`](./handoffs/v1.0-handoff.md) *(Mục 39 cũ)*
* **Review v1.0 (Codex R1):** [`reviews/v1.0-review-r1.md`](./reviews/v1.0-review-r1.md) *(Mục 40 cũ — CHANGES_REQUESTED)*
* **Handoff v1.1:** [`handoffs/v1.1-handoff.md`](./handoffs/v1.1-handoff.md) *(Mục 41 cũ)*
* **Review v1.1 (Codex R2):** [`reviews/v1.1-review-r2.md`](./reviews/v1.1-review-r2.md) *(Mục 42 cũ — CHANGES_REQUESTED)*
* **Handoff v1.2:** [`handoffs/v1.2-handoff.md`](./handoffs/v1.2-handoff.md) *(Mục 43 cũ — Khắc phục hoàn toàn T6-F02 và T6-F03)*
* **Review v1.2 (Codex R3):** [`reviews/v1.2-review-r3.md`](./reviews/v1.2-review-r3.md) *(Mục 44 cũ — VERIFIED v1.2)*
