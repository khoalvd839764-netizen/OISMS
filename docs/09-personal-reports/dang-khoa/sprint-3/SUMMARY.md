# BÁO CÁO TỔNG KẾT BACKEND 1 — SPRINT 3 (LÊ VÕ ĐĂNG KHOA)

| Thông tin kiểm soát tổng kết | Chi tiết |
| :--- | :--- |
| **Người thực hiện** | **Lê Võ Đăng Khoa** — Trưởng nhóm / Backend 1 Lead |
| **Giai đoạn dự án** | **Sprint 3: Lõi Vận hành Hoàn chỉnh (18/10/2026 – 22/10/2026)** |
| **Mã Task Jira (MCP)** | [`OISMS-40`](https://khoalevodang301007.atlassian.net/browse/OISMS-40) *(Subtask thuộc Task [`OISMS-34`](https://khoalevodang301007.atlassian.net/browse/OISMS-34) trên Sprint 3 Board)* |
| **Trạng thái Jira** | `In Progress` *(Chưa chuyển In Review trên Jira Cloud, giữ nguyên thực tế)* |
| **Người chịu trách nhiệm** | Lê Võ Đăng Khoa (Assignee) |
| **Hạn chót (Deadline Sprint 3)**| 22/10/2026 - 23:59:59 (Theo cấu hình Sprint 7 trên Jira) |
| **Nhánh Git (Branch)** | `feature/OISMS-40-loi-van-hanh-kho-va-don-hang` |
| **Đích bàn giao (Base PR)** | `develop` |
| **Ngày cập nhật checklist** | 27/10/2026 — đối chiếu hồ sơ từng task, schema và lịch sử Git hiện có |

---

## 1. Thông Tin Nhận Nhiệm Vụ & Ranh Giới Nghiệm Thu

### 1.1. Phạm vi được giao theo kế hoạch (Jira OISMS-40 / OISMS-34 & Sprint 3 README)
1. **Thiết kế nền tảng kiến trúc & Hợp đồng giao tiếp (Task 1):** Định nghĩa Entity, EF Core mapping, DbContext, Dual-Route Checkout, Service Interfaces cho toàn bộ phân hệ vận hành.
2. **Thanh toán nhanh quầy POS nguyên tử (Task 2 - `FR-POS-04`):** Endpoint thanh toán nhanh, trừ tồn OnHand lập tức, chốt giá vốn WAC snapshot, ghi sổ cái `StockLedger` OUT, chống trùng lặp Idempotency.
3. **Máy trạng thái đơn hàng & Vòng đời đơn online (Task 3 - `FR-ORD-01`, `FR-ORD-02`, `FR-COST-02`):** Tạo đơn giữ chỗ `Reserved`, xác nhận `Confirmed`, hoàn tất `Completed`, hủy đơn đủ điều kiện sang `Cancelled`; xử lý tương thích Draft legacy và chốt giá vốn khi xác nhận xuất hàng.
4. **Chuyển kho nội bộ 2 bước (Task 4A - `FR-INV-03`):** Lập phiếu, xuất chuyển sang `InTransit`, nhận hàng sang `Received`; hủy phiếu Draft theo contract, cập nhật giá vốn chi nhánh đích và phối hợp khóa với các luồng bán/giữ hàng.
5. **Kiểm kê kho & Cân bằng tồn (Task 4B - `FR-INV-04`):** Khởi tạo đợt kiểm kê, snapshot số tồn sổ sách, ghi nhận số đếm thực tế, duyệt cân bằng tồn tự động tạo `StockAdjustment` và ghi sổ cái IN/OUT.
6. **Webhook đa kênh TMĐT & SignalR Realtime (Task 5 - `FR-ORD-01`, `FR-SIM-02`):** Tiếp nhận webhook Shopee, TikTok Shop, Lazada chuẩn hóa về `Canonical Order Model`, xử lý tranh chấp 50 reqs/SKU, đẩy thông báo thời gian thực qua `NotificationHub`.
7. **Hangfire Background Worker hủy giữ chỗ quá hạn (Task 6 - `FR-SIM-03`):** Quét định kỳ mỗi 1 phút, tự động hủy các đơn `Reserved` quá hạn 15 phút, hoàn trả tồn khả dụng nguyên tử.

### 1.2. Phạm vi thực sự đã nghiệm thu trong đợt này
Kết quả được ghi theo từng gói công việc; Task 4 tách thành 4A và 4B:
* **Task 1:** Đạt chuẩn thiết kế theo báo cáo kỹ thuật gốc Sprint 3.
* **Task 2, Task 3, Task 4A, Task 4B, Task 5, Task 6:** Có biên bản nghiệm thu **VERIFIED** cùng bằng chứng kiểm thử tương ứng. Transaction/concurrency được kiểm chứng trên PostgreSQL; các ca Hub transport dùng DB InMemory được ghi riêng trong hồ sơ.

---

## 2. Bảng Kết Quả Nghiệm Thu Từng Task

| Mã Task | Phân hệ kỹ thuật | Phiên bản | Trạng thái nghiệm thu | Biên bản nghiệm thu cuối cùng |
| :--- | :--- | :---: | :---: | :--- |
| **`S3-BE1-T01`** | API Contract & Persistence Foundation | `v1.0` | **DONE (theo báo cáo gốc)** | [Hồ sơ Task 1](./codex-antigravity/task-01-contract-foundation/README.md) |
| **`S3-BE1-T02`** | Atomic POS Checkout | `v1.2` | **VERIFIED v1.2** | [Biên bản Task 2](./codex-antigravity/task-02-pos-checkout/reviews/v1.2-review-r3.md) |
| **`S3-BE1-T03`** | Online Order Lifecycle | `v1.1` | **VERIFIED v1.1** | [Biên bản Task 3](./codex-antigravity/task-03-order-lifecycle/reviews/v1.1-review-r2.md) |
| **`S3-BE1-T04A`**| Two-Step Stock Transfer | `v1.2` | **VERIFIED v1.2** | [Biên bản Task 4A](./codex-antigravity/task-04a-stock-transfer/reviews/v1.2-review-r3.md) |
| **`S3-BE1-T04B`**| Warehouse Stocktaking & Reconciliation | `v1.3` | **VERIFIED v1.3** | [Biên bản Task 4B](./codex-antigravity/task-04b-stock-count/reviews/v1.3-review-r4.md) |
| **`S3-BE1-T05`** | Omnichannel Webhook & SignalR Realtime | `v1.4` | **VERIFIED v1.4** | [Biên bản Task 5](./codex-antigravity/task-05-webhook-realtime/reviews/v1.4-review-r5.md) |
| **`S3-BE1-T06`** | Hangfire Reservation Expiry Worker | `v1.2` | **VERIFIED v1.2** | [Biên bản Task 6](./codex-antigravity/task-06-reservation-expiry/reviews/v1.2-review-r3.md) |

Task 1 giữ trạng thái `DONE (theo báo cáo gốc)` vì chưa có biên bản nghiệm thu độc lập riêng. Dấu `[x]` dưới đây ghi nhận hạng mục đã triển khai theo hồ sơ tương ứng; không thay thế trạng thái nghiệm thu của từng task.

### 2.1. Checklist công việc đã thực hiện

**Task 1 — Nền tảng dữ liệu và hợp đồng API**

- [x] Bổ sung Domain entities, DbSet và EF Core mapping phục vụ thanh toán, chuyển kho, kiểm kê và điều chỉnh tồn.
- [x] Chuẩn hóa DTO và service interface cho POS, đơn online, chuyển kho, kiểm kê, webhook và realtime.
- [x] Thống nhất checkout chuẩn `/api/pos/checkout` và alias `/api/orders/checkout` dùng cùng lõi xử lý.
- [x] Quy định RequestKey, thứ tự lấy khóa, tenant scope và dữ liệu trả về theo quyền.
- [x] Bổ sung bảng `shop_integrations`, sáu cột trên các bảng hiện có và các constraint/quyền liên quan; tái sử dụng các bảng nghiệp vụ có sẵn từ Sprint 2.

**Task 2 — Thanh toán POS nguyên tử toàn giỏ**

- [x] Xử lý kiểm tra tồn, tạo đơn, thanh toán, trừ tồn và ghi ledger OUT trong cùng một transaction.
- [x] Kiểm tra tồn khả dụng sau khi lấy khóa; bảo vệ hàng đang giữ cho đơn online.
- [x] Chốt giá vốn WAC vào từng `OrderItem.CostPrice` và ghi giá vốn xuất kho.
- [x] Chống thanh toán trùng: cùng RequestKey/nội dung trả kết quả cũ; đổi nội dung bị từ chối.
- [x] Kiểm chứng thiếu tồn, rollback toàn giỏ, request đồng thời và POS tranh tồn với reservation.
- [x] Trả dữ liệu hóa đơn POS không chứa giá vốn/COGS cho thu ngân.

**Task 3 — Vòng đời đơn online**

- [x] Tạo đơn giữ chỗ, reservation và phân bổ tồn theo kho; cập nhật Reserved ở hai cấp kho/chi nhánh.
- [x] Xác nhận đơn: giảm Reserved, trừ OnHand, chốt giá vốn và ghi OUT nguyên tử.
- [x] Hủy đơn đủ điều kiện: giải phóng giữ chỗ đúng một lần, giữ tương thích Draft legacy chưa thanh toán.
- [x] Hoàn tất đơn Confirmed mà không trừ tồn hoặc ghi OUT lần hai.
- [x] Kiểm chứng chuyển trạng thái sai, thiếu allocation, retry, confirm/cancel đồng thời, rollback và cách ly tenant.

**Task 4A — Chuyển kho hai bước**

- [x] Tạo phiếu và các dòng hàng, chống tạo phiếu trùng bằng RequestKey/payload signature.
- [x] Dispatch: kiểm tra tồn khả dụng, trừ kho nguồn, lưu giá vốn xuất và chuyển phiếu sang InTransit.
- [x] Receive: tăng tồn kho đích, ghi ledger IN và cập nhật giá vốn chi nhánh theo contract.
- [x] Hủy phiếu Draft; kiểm soát tenant, quan hệ kho/chi nhánh và trạng thái chứng từ.
- [x] Kiểm chứng dispatch/receive lặp, tranh tồn với bán/giữ hàng, nhập xen giữa và rollback.

**Task 4B — Kiểm kê và cân bằng tồn**

- [x] Khởi tạo kiểm kê, chụp số tồn và sequence tại thời điểm snapshot.
- [x] Ghi nhận số đếm thực tế và tính chênh lệch từng SKU.
- [x] Khi chốt, tạo chứng từ `InventoryAdjustment` liên kết; dùng chứng từ đó ghi IN/OUT để tránh ghi đúp.
- [x] Phát hiện giao dịch kho xen giữa bằng sequence drift; kiểm tra giá vốn điều chỉnh cho hàng thừa khi chưa có WAC hợp lệ.
- [x] Kiểm chứng chốt lặp, concurrent confirm, tenant isolation và transaction failure.

**Task 5 — Tiếp nhận webhook và thông báo realtime**

- [x] Tiếp nhận payload canonical Shopee, TikTok và Lazada qua `/api/webhooks/simulator/{channel}`.
- [x] Xác thực nguồn gửi, ánh xạ shop về doanh nghiệp/chi nhánh và kiểm tra dữ liệu đầu vào.
- [x] Chống nhận trùng theo tenant/kênh/shop/mã đơn và canonical payload signature.
- [x] Tạo đơn Reserved bằng lõi chung; giữ hàng thật, thiếu tồn không tạo đơn giữ chỗ giả.
- [x] Phát SignalR sau commit; kiểm tra JWT, nhóm tenant/chi nhánh và không lộ dữ liệu giá vốn/bí mật.
- [x] Kiểm chứng HTTP/PostgreSQL cho replay, tranh tồn, rollback và các ca xác thực; phân biệt provider của test Hub.

**Task 6 — Tự hủy giữ chỗ quá hạn**

- [x] Tích hợp Hangfire PostgreSQL storage trong schema riêng và chế độ bật/tắt worker.
- [x] Lên lịch quét định kỳ theo `Reservation.ExpiresAt`, giữ TTL 15 phút đã lưu trên reservation.
- [x] Hủy toàn đơn đủ điều kiện, giải phóng mọi reservation còn hoạt động và cập nhật Reserved hai cấp trong cùng transaction.
- [x] Giữ nguyên OnHand, WAC và ledger khi chỉ giải phóng giữ chỗ; không xử lý lại đơn đã xác nhận/thanh toán thành công.
- [x] Kiểm chứng worker thật, retry/no-op, confirm/expiry cạnh tranh, cancellation và rollback.
- [x] Bổ sung scan có giới hạn/cursor, thống kê theo service instance và báo failure theo policy để xử lý lỗi không bị che mất.

**Sửa lỗi nền tảng và bàn giao**

- [x] Sửa B-01: kho sạch đổi chi nhánh hợp lệ; kho có lịch sử bị chặn có kiểm soát, dữ liệu được bảo toàn.
- [x] Bổ sung regression B-01 và kiểm chứng lại trên PostgreSQL theo hồ sơ liên kết tại mục 4–5.
- [x] Tách tài liệu theo task, lưu hợp đồng, bàn giao, biên bản và bằng chứng kiểm thử.
- [x] Đóng gói BE1 qua PR #21 và đưa code vào develop tại merge commit `4dbfd8e`, chứa fix B-01 `b5ba71e`.

### 2.2. Checklist phần chưa được xác nhận hoàn tất trong báo cáo này

- [ ] Công cụ giả lập sàn đầy đủ với giao diện riêng và sender HTTP đồng thời được nghiệm thu end-to-end; batch tuần tự gọi service nội bộ chưa đáp ứng mục này.
- [ ] Tích hợp và nghiệm thu UI E2E toàn bộ luồng Admin/POS/realtime với frontend.
- [ ] Chốt và bàn giao thiết kế AI gợi ý nhập hàng theo đầu mục nghiên cứu riêng của Sprint 3.
- [ ] Hoàn thiện toàn bộ API đọc/báo cáo và automated core tests của gói BE2; đây là phạm vi phối hợp, không ghi nhận là phần triển khai BE1.
- [ ] Triển khai production, bật/provision Hangfire trên môi trường đích và kiểm chứng vận hành sau triển khai.

Các mục chưa đánh dấu chỉ phản ánh giới hạn bằng chứng của báo cáo này, không khẳng định người phụ trách chưa làm ở nơi khác.

---

## 3. Bàn Giao Kỹ Thuật

### 3.1. Danh sách API bàn giao
| HTTP Verb | Endpoint | Phân hệ | Mô tả chức năng & Ràng buộc nghiệp vụ |
| :--- | :--- | :--- | :--- |
| `POST` | `/api/pos/checkout` và `/api/orders/checkout` | POS | Thanh toán toàn giỏ nguyên tử, trừ OnHand, chốt WAC snapshot, ghi OUT; dùng RequestKey ổn định theo contract. |
| `POST` | `/api/orders` | Orders | Tạo đơn Admin và giữ hàng nguyên tử; chưa chốt CostPrice khi chỉ Reserved. |
| `POST` | `/api/orders/{id}/confirm` | Orders | Duyệt đơn online: trừ OnHand, giải phóng Reserved, ghi Ledger OUT. |
| `POST` | `/api/orders/{id}/cancel` | Orders | Hủy đơn online: hoàn trả số lượng giữ chỗ về tồn khả dụng. |
| `POST` | `/api/orders/{id}/complete` | Orders | Hoàn tất đơn Confirmed; không xuất kho hoặc ghi OUT lần hai. |
| `POST` | `/api/stock-transfers` | Transfers | Lập phiếu điều chuyển hàng liên kho. |
| `POST` | `/api/stock-transfers/{id}/dispatch`| Transfers | Xuất điều chuyển: trừ tồn nguồn, chuyển InTransit, ghi ledger OUT với giá vốn snapshot. |
| `POST` | `/api/stock-transfers/{id}/receive` | Transfers | Nhận điều chuyển: tăng tồn đích, cập nhật giá vốn theo contract và ghi ledger IN. |
| `POST` | `/api/stock-transfers/{id}/cancel` | Transfers | Hủy phiếu Draft theo contract; không phải API hoàn hàng đang vận chuyển. |
| `POST` | `/api/stock-counts` | Counts | Khởi tạo đợt kiểm kê kho (phạm vi ALL hoặc PARTIAL), snapshot số tồn sổ sách. |
| `POST` | `/api/stock-counts/{id}/record` | Counts | Ghi nhận số kiểm đếm thực tế của từng SKU. |
| `POST` | `/api/stock-counts/{id}/confirm`| Counts | Chốt kiểm kê: tạo `InventoryAdjustment` liên kết, ghi IN/OUT qua chứng từ điều chỉnh và cập nhật tồn. |
| `POST` | `/api/stock-counts/{id}/cancel` | Counts | Hủy bỏ đợt kiểm kê chưa duyệt. |
| `POST` | `/api/webhooks/simulator/{channel}` | Webhooks | Endpoint nhận payload canonical ba kênh, xác thực nguồn gửi, chống trùng và giữ hàng; không phải công cụ sender độc lập. |
| `POST` | `/api/webhooks/simulator/batch` | Simulator | Có trên nhánh tích hợp OISMS-42; sinh đơn tuần tự và gọi service nội bộ. Không dùng làm bằng chứng 50 HTTP requests đồng thời hoặc ghi nhận là triển khai riêng của BE1. |
| SignalR | `/hubs/notifications` | Realtime | Các sự kiện `OrderCreated`, `StockLevelChanged`, `LowStockWarning` tới kết nối có quyền. |

### 3.2. Cơ sở dữ liệu & Cấu hình Provisioning
* **PostgreSQL Schema:** Thêm bảng `shop_integrations`; thêm `request_key`, `payload_signature` vào `stock_transfers`; thêm `request_key`, `payload_signature`, `generated_adjustment_id` vào `stock_counts`; thêm `webhook_payload_signature` vào `orders`; bổ sung unique/FK/quyền tương ứng. Cột `order_items.cost_price` và các bảng thanh toán/chuyển kho/kiểm kê đã có trong schema Sprint 2, được bổ sung mapping và logic sử dụng.
* **Hangfire Schema Provisioning:** Bổ sung [SQL provisioning](../../../database/hangfire_schema_provisioning.sql) cho schema `hangfire` độc lập. Khi bật worker, Hangfire dùng storage này để tạo/lưu metadata job.
* **Cấu hình Worker Hangfire:**
  - Bật worker: Cấu hình trong `appsettings.json`: `"Hangfire": { "Enabled": true }`.
  - Tắt worker: Chuyển `"Enabled": false` (server WebApi không khởi chạy Hangfire Server, an toàn cho môi trường test/migrate).

### 3.3. Giới hạn kỹ thuật cần lưu ý
* **Realtime Best-Effort:** Sự kiện SignalR được đẩy sau khi commit transaction database thành công. Nếu client bị mất kết nối mạng, client phải refetch dữ liệu qua REST API khi reconnect (chưa có durable outbox/event store).
* **Bảo vệ ranh giới Tenant:** API người dùng lấy tenant từ JWT; webhook xác định tenant/shop từ cơ chế xác thực và cấu hình integration tin cậy. Không nhận tenant tùy ý từ request body.
* **Không làm giả kết quả kiểm thử:** Các bài test sử dụng Mock hoặc InMemory chỉ dùng cho Unit Test và Hub Connection test; toàn bộ logic Concurrency, Deadlock, và Transaction đều được kiểm chứng trên **PostgreSQL 16 thật**.

---

## 4. Nguồn Kiểm Chứng Hiện Hành (S3-B01-FIX & Full Integration)

Kết quả trong [bàn giao B-01](./codex-antigravity/baseline-b01-fix/handover.md), tương ứng baseline sửa lỗi BE1; đây là số liệu của lượt chạy được lưu trong hồ sơ, không phải kết quả chạy mới ở lần sửa tài liệu ngày 27/10/2026:
* **Build Solution:** `dotnet build src/backend/OISM.sln -c Release --no-restore` ➔ **PASS**, **0 warning, 0 error**.
* **Unit Tests:** `dotnet test src/backend/tests/OISM.UnitTests` ➔ **129/129 PASS (100%)**.
* **Full Integration Suite:** Chạy mới 1 lượt trọn vẹn toàn bộ integration tests của solution trên PostgreSQL thật:
  - **Kết quả:** **267/267 PASS, 0 FAIL, 0 skipped, thời gian chạy: 15 phút 30 giây**.
  - **Phân rã kiểm thử:**
    - Targeted B-01 test: 1/1 PASS (`Postgres_Warehouse_ChangeBranch_WhenHasHistory_ReturnsControlled409_AndCleanWarehouseSucceeds`).
    - Dedicated B-01 regression suite: 6/6 PASS (`WarehouseBranchReassignmentRegressionTests`).
    - 11/11 ReservationExpiry tests PASS; 14/14 Webhook tests PASS; 4/4 Realtime Hub tests PASS; 8/8 Simulator tests PASS.
* **Tổng cộng tại baseline bàn giao B-01:** **396/396 PASS** theo runner được trích trong bàn giao (129 unit + 267 integration). Không cộng các nhóm subset vào tổng lần nữa.
* **Đối chứng sau tích hợp:** [Biên bản T01 BE2 v1.3](../../nhut/sprint-3/codex-antigravity/task-01-core-alignment/reviews/v1.3-review-r4.md) ghi 44/44 integration liên quan PASS trên `ca6b5ce`, gồm 6 warehouse regression và 1 B-01 gốc. Kết quả này xác nhận lại phần B-01 trên nhánh tích hợp; không thay con số full suite của baseline BE1.

---

## 5. Khắc Phục Lỗi Nền Tảng B-01

Lỗi B-01 (`Postgres_Warehouse_ChangeBranch_WhenHasHistory_ReturnsControlled409_AndCleanWarehouseSucceeds`) đã có fix tại `b5ba71e`, nằm trong merge BE1 vào develop và được chạy lại ở nhóm regression nêu tại mục 4:
* **Nguyên nhân:** EF Core xem bộ ba `{ TenantId, Id, BranchId }` là Principal/Alternate Key (do cấu hình composite FK từ các bảng phụ thuộc), Change Tracker chặn việc sửa đổi key property trên tracked entity và ném `InvalidOperationException` (unhandled 500).
* **Giải pháp:** Cập nhật kho sạch thông qua parameterized SQL trực tiếp trong transaction (`ExecuteSqlInterpolatedAsync`), detach tracked entity khỏi Change Tracker, kiểm tra lịch sử trên 7 bảng phụ thuộc, khóa dòng bi quan `FOR UPDATE` và bắt lỗi PostgreSQL constraint `23503` trả về 409 Conflict.
* **Tài liệu bàn giao chi tiết:** Xem gói [`baseline-b01-fix/`](./codex-antigravity/baseline-b01-fix/README.md) và biên bản [`handover.md`](./codex-antigravity/baseline-b01-fix/handover.md).
* **Trạng thái hiện tại trong báo cáo:** đã sửa, có regression PASS và fix đã vào develop. Trạng thái `READY_FOR_REVIEW` trong handover cũ được giữ làm lịch sử.

---

## 6. Đối Chiếu Kế Hoạch Dự Án & Phạm Vi Thực Tế

| Hạng mục trong Kế hoạch Sprint 3 | Phụ trách chính | Trạng thái thực tế trong đợt nghiệm thu này |
| :--- | :--- | :--- |
| **Lõi vận hành (Order, POS, Reserve, Chuyển kho, Kiểm kê)** | Đăng Khoa (BE1) | T01 DONE theo báo cáo gốc; T02/T03/T04A/T04B VERIFIED theo từng biên bản |
| **Tiếp nhận Webhook 3 sàn & SignalR realtime** | Đăng Khoa (BE1) | **HOÀN TẤT & VERIFIED 100%** (Task 5) |
| **Hangfire Worker tự hủy giữ chỗ quá hạn 15 phút** | Đăng Khoa (BE1) | **HOÀN TẤT & VERIFIED 100%** (Task 6) |
| **Khắc phục lỗi đổi chi nhánh kho sạch (B-01)** | Đăng Khoa (BE1) | Đã sửa và đưa vào develop; regression PASS theo mục 4–5 |
| **Tiếp nhận đơn giả lập ba kênh** | Đăng Khoa (BE1) | Đã nghiệm thu endpoint nhận và lõi xử lý webhook; batch tuần tự là phần có trên nhánh tích hợp |
| **Simulator đầy đủ (UI / sender HTTP đồng thời)** | Đăng Khoa phụ trách Simulator theo phân công; phối hợp kiểm thử với BE2 | Chưa có bằng chứng nghiệm thu đầy đủ trong báo cáo này; không coi batch tuần tự là runner concurrent |
| **Thiết kế AI gợi ý nhập hàng** | Nghiên cứu riêng | Chưa thuộc gói nghiệm thu mã nguồn backend này |
| **Daily Aggregation & API Báo cáo tài chính (COGS, Revenue)** | Nhựt (BE2 - OISMS-42) | Nằm trong phạm vi PR riêng của BE2 ([PR #20](https://github.com/khoalvd839764-netizen/OISMS/pull/20)) |
| **Giao diện Web Admin & POS PWA** | Phúc Hữu (Admin), Phương (POS) | Thuộc phạm vi Frontend, nhận bàn giao API từ BE1 |
| **Triển khai Production & Khóa hạ tầng** | DevOps | Chưa thực hiện trong đợt nghiệm thu Sprint 3 local |

---

## 7. Thông Tin Đóng Gói Git & Pull Request

* **Kho lưu trữ (Repository):** `https://github.com/khoalvd839764-netizen/OISMS.git`
* **Nhánh nguồn (Head):** `feature/OISMS-40-loi-van-hanh-kho-va-don-hang`
* **Nhánh đích (Base):** `develop`
* **Pull Request chính thức:** 👉 **[PR #21](https://github.com/khoalvd839764-netizen/OISMS/pull/21)** (`[OISMS-40] Hoàn thiện lõi Backend 1 Sprint 3 và chuẩn hóa tài liệu bàn giao`)
* **Trạng thái Merge theo lịch sử Git đã đối chiếu:** **ĐÃ MERGE VÀO DEVELOP**, commit `4dbfd8eef0c2acea48171c56d406a3897aa45df8` có thông điệp `Merge pull request #21`; commit sửa B-01 `b5ba71ef7ac2bf055f4ee8963e159d4f27c69aae` là tổ tiên của merge này.
* Báo cáo này tổng kết phần BE1. Nhánh đang dùng để cập nhật tài liệu là OISMS-42 và không làm thay đổi kết luận nghiệm thu của các task BE2 còn lại. Không cập nhật trạng thái Jira hoặc triển khai production trong lần sửa tài liệu này.
