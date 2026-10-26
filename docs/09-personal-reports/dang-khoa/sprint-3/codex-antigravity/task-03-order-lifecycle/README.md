# Task 3: Online Order Lifecycle — Index & Trạng Thái Hiện Hành

| Thông tin kiểm soát Task 3 | Chi tiết |
| :--- | :--- |
| **Mã Task (Task ID)** | `S3-BE1-T03-ORDER-LIFECYCLE` |
| **Chủ đề** | Vòng đời đơn hàng trực tuyến ngoài POS (`Online Order Lifecycle`) |
| **Phiên bản hiện hành** | `v1.1` |
| **Trạng thái nghiệm thu** | **VERIFIED v1.1** *(Nghiệm thu bởi Codex vòng 2, ngày 24/10/2026)* |
| **Baseline** | Task 2 (`S3-BE1-T02-POS-CHECKOUT`) + Sprint 2 |
| **Người triển khai** | Lê Võ Đăng Khoa (Backend 1 Lead & Team Lead) |
| **Reviewer** | Codex (`codex-antigravity-workflow`) |
| **Ngày hoàn thành** | 24/10/2026 |

---

## 1. Mục tiêu & Phạm vi

Xây dựng máy trạng thái đơn hàng trực tuyến (`Order State Machine`) tách biệt khỏi luồng POS:
1. **Luồng trạng thái chuẩn:** `Draft` ➔ `Pending` ➔ `Reserved` ➔ `Confirmed` ➔ `Completed` / `Cancelled`.
2. **Cơ chế Giữ chỗ (Reservation Engine):** Khởi tạo `Reservation` khi đơn sang `Reserved`, giảm tồn khả dụng (`Available = OnHand - Reserved`).
3. **Duyệt đơn nguyên tử (`Confirm`):** Giảm `Reserved`, trừ `OnHand`, chốt `WAC snapshot` vào `OrderItem.CostPrice`, ghi `StockLedger` OUT.
4. **Hủy đơn an toàn (`Cancel`):** Giải phóng số lượng giữ chỗ (`Reserved = Reserved - Quantity`), hoàn trả toàn bộ tồn khả dụng, hủy phiếu `Reservation`.
5. **Chống race condition:** Đảm bảo cạnh tranh giữa Confirm và Cancel hoặc giữa nhiều luồng không làm lệch số dư hoặc double-release.

---

## 2. Bảng quản lý Findings (T3-F01 .. T3-F05)

| Finding ID | Độ ưu tiên | AC liên quan | Mô tả vấn đề & Giải pháp xử lý | Trạng thái |
| :---: | :---: | :---: | :--- | :---: |
| **T3-F01** | P1 | `AC-01`<br/>`AC-02` | **State Machine Transition Guard:** Chặn đứng các bước chuyển trạng thái bất hợp lệ (ví dụ: `Draft` nhảy thẳng lên `Completed`), trả `400 Bad Request`. | **CLOSED**<br/>*(Đóng tại v1.1)* |
| **T3-F02** | P1 | `AC-03`<br/>`AC-04` | **Atomic Confirm & Cancel:** Trừ `OnHand` và giải phóng `Reserved` trong cùng 1 transaction, không để rò rỉ tồn giữ chỗ. | **CLOSED**<br/>*(Đóng tại v1.1)* |
| **T3-F03** | P1 | `AC-05` | **WAC Snapshot on Confirm:** Chốt giá vốn tại thời điểm duyệt đơn, ghi nhận vào `OrderItem.CostPrice`. | **CLOSED**<br/>*(Đóng tại v1.1)* |
| **T3-F04** | P1 | `AC-06` | **Confirm vs Cancel Race Condition:** Áp dụng khóa bi quan (`Pessimistic Locking`) trên bản ghi Order để ngăn chặn đồng thời vừa duyệt vừa hủy. | **CLOSED**<br/>*(Đóng tại v1.1)* |
| **T3-F05** | P2 | `AC-07` | **DTO Contracts & BE2 Handover:** Chuẩn hóa toàn bộ schema DTO cho BE2 phục vụ các endpoint tra cứu và danh sách. | **CLOSED**<br/>*(Đóng tại v1.1)* |

---

## 3. Bằng chứng kiểm thử & Xác thực theo Provider

| Phân loại kiểm chứng | Lệnh / Kịch bản thực tế | Kết quả thực tế | Provider / Môi trường |
| :--- | :--- | :--- | :--- |
| **Unit Tests** | `dotnet test --filter Category=Unit` | **117/117 PASS** (100%) | .NET 8 / InMemory |
| **Order Lifecycle Tests** | `dotnet test --filter FullyQualifiedName~OnlineOrder` | **34/34 PASS** (100%) | PostgreSQL 16 Alpine thật |
| **Sprint 3 Integration** | PosCheckout + OnlineOrder tests | **55/55 PASS** (100%) | PostgreSQL 16 Alpine thật |
| **State Machine Guard** | Kiểm thử chuyển đổi trạng thái phủ định | **PASS**, mã lỗi 400 có kiểm soát | PostgreSQL 16 Alpine thật |

---

## 4. Danh mục tài liệu & Lịch sử các vòng

* **Handoff v1.0:** [`handoffs/v1.0-handoff.md`](./handoffs/v1.0-handoff.md) *(Mục 11: Báo cáo triển khai Task 3 v1.0)*
* **Review v1.0 (Codex R1):** [`reviews/v1.0-review-r1.md`](./reviews/v1.0-review-r1.md) *(Mục 12: Codex review vòng 1 — CHANGES_REQUESTED)*
* **Handoff v1.1:** [`handoffs/v1.1-handoff.md`](./handoffs/v1.1-handoff.md) *(Mục 13: Báo cáo xử lý review v1.1)*
* **Review v1.1 (Codex R2):** [`reviews/v1.1-review-r2.md`](./reviews/v1.1-review-r2.md) *(Mục 14: Kết luận nghiệm thu độc lập VERIFIED v1.1)*
