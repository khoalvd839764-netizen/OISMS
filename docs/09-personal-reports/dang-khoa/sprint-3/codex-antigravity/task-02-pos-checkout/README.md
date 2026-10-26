# Task 2: Atomic POS Checkout — Index & Trạng Thái Hiện Hành

| Thông tin kiểm soát Task 2 | Chi tiết |
| :--- | :--- |
| **Mã Task (Task ID)** | `S3-BE1-T02-POS-CHECKOUT` |
| **Chủ đề** | Thanh toán nhanh quầy POS toàn giỏ nguyên tử (`Atomic POS Checkout`) |
| **Phiên bản hiện hành** | `v1.2` |
| **Trạng thái nghiệm thu** | **VERIFIED v1.2** *(Nghiệm thu bởi Codex vòng 3, ngày 24/10/2026)* |
| **Baseline** | Task 1 (`S3-BE1-T01-CONTRACT-FOUNDATION`) + Sprint 2 |
| **Người triển khai** | Lê Võ Đăng Khoa (Backend 1 Lead & Team Lead) |
| **Reviewer** | Codex (`codex-antigravity-workflow`) |
| **Ngày hoàn thành** | 24/10/2026 |

---

## 1. Mục tiêu & Phạm vi

Xây dựng endpoint thanh toán quầy POS nhanh (`POST /api/pos/checkout`) thực hiện toàn bộ quy trình bán lẻ trong 1 transaction nguyên tử:
1. Trừ tồn kho thực tế (`OnHand`) lập tức theo thứ tự SKU ID cố định (chống deadlock).
2. Chốt giá vốn bình quân gia quyền (`WAC snapshot`) vào `OrderItem.CostPrice`.
3. Ghi bút toán xuất kho bán hàng (`StockLedger` OUT) và tạo bản ghi thanh toán (`Payment` status `Completed`).
4. Khóa chống thao tác trùng lặp (`Idempotency Key`) an toàn đa luồng.
5. Cạnh tranh tồn kho an toàn với đơn trực tuyến (`ReservationService`).

---

## 2. Bảng quản lý Findings (F-01 .. F-04)

| Finding ID | Độ ưu tiên | AC liên quan | Mô tả vấn đề & Giải pháp xử lý | Trạng thái |
| :---: | :---: | :---: | :--- | :---: |
| **F-01** | P1 | `AC-01`<br/>`AC-02` | **Idempotency Header & Transaction Boundary:** Header không hợp lệ trả `400 Bad Request`. Bọc transaction nguyên tử đảm bảo không tạo Order mồ côi khi lỗi. | **CLOSED**<br/>*(Đóng tại v1.1)* |
| **F-02** | P1 | `AC-06`<br/>`AC-07` | **WAC Snapshot & Ledger OUT:** Chốt giá vốn vào `CostPrice`, ghi nhận đúng `StockLedger` type `OUT` cho từng dòng sản phẩm. | **CLOSED**<br/>*(Đóng tại v1.2)* |
| **F-03** | P1 | `AC-04` | **Concurrent First Submission:** Phát đồng thời 10 requests cùng key mới; đúng 1 đơn thành công, các request còn lại nhận `200 OK` cùng `OrderId`, tồn kho chỉ trừ 1 lần. | **CLOSED**<br/>*(Đóng tại v1.2)* |
| **F-04** | P1 | `AC-05`<br/>`AC-08` | **POS tranh tồn với ReservationService:** Đồng bộ `startBarrier` tranh chấp tồn trên cùng SKU; đúng 1 bên thành công, bên kia nhận lỗi thiếu hàng có kiểm soát, không oversell, không số dư âm. | **CLOSED**<br/>*(Đóng tại v1.2)* |

---

## 3. Bằng chứng kiểm thử & Xác thực theo Provider

| Phân loại kiểm chứng | Lệnh / Kịch bản thực tế | Kết quả thực tế | Provider / Môi trường |
| :--- | :--- | :--- | :--- |
| **Unit Tests** | `dotnet test --filter Category=Unit` | **117/117 PASS** (100%) | .NET 8 / InMemory |
| **PosCheckout Integration** | `dotnet test --filter FullyQualifiedName~PosCheckoutIntegrationTests` | **21/21 PASS** (100%) | PostgreSQL 16 Alpine thật |
| **Concurrency 10 reqs** | Test kịch bản `AC04_Concurrent_First_Submission` | **PASS**, 1 Order, 1 Payment, 1 Ledger | PostgreSQL 16 Alpine thật |
| **POS vs Reserve Race** | Test kịch bản `AC05_POS_Competes_With_Reservation_Service` | **PASS**, không oversell, tồn không âm | PostgreSQL 16 Alpine thật |

---

## 4. Danh mục tài liệu & Lịch sử các vòng

* **Handoff v1.0:** [`handoffs/v1.0-handoff.md`](./handoffs/v1.0-handoff.md) *(Mục 8: Báo cáo kỹ thuật POS Checkout v1.0)*
* **Handoff v1.2:** [`handoffs/v1.2-handoff.md`](./handoffs/v1.2-handoff.md) *(Mục 9: Báo cáo fix và giải trình findings F-01..F-04)*
* **Review v1.2 (Codex R3):** [`reviews/v1.2-review-r3.md`](./reviews/v1.2-review-r3.md) *(Mục 10: Kết luận nghiệm thu độc lập VERIFIED v1.2)*
