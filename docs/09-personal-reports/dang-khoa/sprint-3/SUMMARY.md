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

---

## 1. Thông Tin Nhận Nhiệm Vụ & Ranh Giới Nghiệm Thu

### 1.1. Phạm vi được giao theo kế hoạch (Jira OISMS-40 / OISMS-34 & Sprint 3 README)
1. **Thiết kế nền tảng kiến trúc & Hợp đồng giao tiếp (Task 1):** Định nghĩa Entity, EF Core mapping, DbContext, Dual-Route Checkout, Service Interfaces cho toàn bộ phân hệ vận hành.
2. **Thanh toán nhanh quầy POS nguyên tử (Task 2 - `FR-POS-04`):** Endpoint thanh toán nhanh, trừ tồn OnHand lập tức, chốt giá vốn WAC snapshot, ghi sổ cái `StockLedger` OUT, chống trùng lặp Idempotency.
3. **Máy trạng thái đơn hàng & Vòng đời đơn online (Task 3 - `FR-ORD-01`, `FR-ORD-02`, `FR-COST-02`):** Luồng trạng thái chuẩn `Draft ➔ Pending ➔ Reserved ➔ Confirmed ➔ Completed / Cancelled`, động cơ giữ chỗ chống bán vượt, chốt giá vốn khi duyệt đơn.
4. **Chuyển kho nội bộ 2 bước (Task 4A - `FR-INV-03`):** Quy trình điều chuyển hàng giữa các kho `Dispatch ➔ InTransit ➔ Receive / Reject / Cancel`, tính toán lại WAC kho đích, phòng chống deadlock.
5. **Kiểm kê kho & Cân bằng tồn (Task 4B - `FR-INV-04`):** Khởi tạo đợt kiểm kê, snapshot số tồn sổ sách, ghi nhận số đếm thực tế, duyệt cân bằng tồn tự động tạo `StockAdjustment` và ghi sổ cái IN/OUT.
6. **Webhook đa kênh TMĐT & SignalR Realtime (Task 5 - `FR-ORD-01`, `FR-SIM-02`):** Tiếp nhận webhook Shopee, TikTok Shop, Lazada chuẩn hóa về `Canonical Order Model`, xử lý tranh chấp 50 reqs/SKU, đẩy thông báo thời gian thực qua `NotificationHub`.
7. **Hangfire Background Worker hủy giữ chỗ quá hạn (Task 6 - `FR-SIM-03`):** Quét định kỳ mỗi 1 phút, tự động hủy các đơn `Reserved` quá hạn 15 phút, hoàn trả tồn khả dụng nguyên tử.

### 1.2. Phạm vi thực sự đã nghiệm thu trong đợt này
Toàn bộ 6 task kỹ thuật lõi Backend 1 đã được thẩm định và nghiệm thu độc lập:
* **Task 1:** Đạt chuẩn thiết kế theo báo cáo kỹ thuật gốc Sprint 3.
* **Task 2, Task 3, Task 4A, Task 4B, Task 5, Task 6:** Đều đã được reviewer **Codex** thẩm định nghiêm ngặt qua nhiều vòng review-fix, chạy kiểm thử tự động trên PostgreSQL 16 thật và kết luận **VERIFIED**.

---

## 2. Bảng Kết Quả Nghiệm Thu Từng Task

| Mã Task | Phân hệ kỹ thuật | Phiên bản | Trạng thái nghiệm thu | Reviewer | Biên bản nghiệm thu cuối cùng |
| :--- | :--- | :---: | :---: | :--- | :--- |
| **`S3-BE1-T01`** | API Contract & Persistence Foundation | `v1.0` | **DONE (theo báo cáo gốc)** | Báo cáo gốc | [`task-01-contract-foundation/`](./codex-antigravity/task-01-contract-foundation/README.md) |
| **`S3-BE1-T02`** | Atomic POS Checkout | `v1.2` | **VERIFIED v1.2** | Codex | [`reviews/v1.2-review-r3.md`](./codex-antigravity/task-02-pos-checkout/reviews/v1.2-review-r3.md) |
| **`S3-BE1-T03`** | Online Order Lifecycle | `v1.1` | **VERIFIED v1.1** | Codex | [`reviews/v1.1-review-r2.md`](./codex-antigravity/task-03-order-lifecycle/reviews/v1.1-review-r2.md) |
| **`S3-BE1-T04A`**| Two-Step Stock Transfer | `v1.2` | **VERIFIED v1.2** | Codex | [`reviews/v1.2-review-r3.md`](./codex-antigravity/task-04a-stock-transfer/reviews/v1.2-review-r3.md) |
| **`S3-BE1-T04B`**| Warehouse Stocktaking & Reconciliation | `v1.3` | **VERIFIED v1.3** | Codex | [`reviews/v1.3-review-r4.md`](./codex-antigravity/task-04b-stock-count/reviews/v1.3-review-r4.md) |
| **`S3-BE1-T05`** | Omnichannel Webhook & SignalR Realtime | `v1.4` | **VERIFIED v1.4** | Codex | [`reviews/v1.4-review-r5.md`](./codex-antigravity/task-05-webhook-realtime/reviews/v1.4-review-r5.md) |
| **`S3-BE1-T06`** | Hangfire Reservation Expiry Worker | `v1.2` | **VERIFIED v1.2** | Codex | [`reviews/v1.2-review-r3.md`](./codex-antigravity/task-06-reservation-expiry/reviews/v1.2-review-r3.md) |

*(Ghi chú: Task 1 giữ nguyên trạng thái `DONE (theo báo cáo gốc)`, không tự nâng thành VERIFIED vì không có biên bản Codex riêng).*

---

## 3. Bàn Giao Kỹ Thuật

### 3.1. Danh sách API bàn giao
| HTTP Verb | Endpoint | Phân hệ | Mô tả chức năng & Ràng buộc nghiệp vụ |
| :--- | :--- | :--- | :--- |
| `POST` | `/api/pos/checkout` | POS | Thanh toán nhanh quầy POS nguyên tử, trừ OnHand lập tức, chốt WAC snapshot, ghi Ledger OUT, yêu cầu `Idempotency-Key`. |
| `POST` | `/api/orders` | Orders | Tạo đơn hàng mới từ Admin/Online, tự động tính tổng tiền và chốt giá vốn tạm tính. |
| `POST` | `/api/orders/{id}/confirm` | Orders | Duyệt đơn online: trừ OnHand, giải phóng Reserved, ghi Ledger OUT. |
| `POST` | `/api/orders/{id}/cancel` | Orders | Hủy đơn online: hoàn trả số lượng giữ chỗ về tồn khả dụng. |
| `POST` | `/api/stock-transfers` | Transfers | Lập phiếu điều chuyển hàng liên kho. |
| `POST` | `/api/stock-transfers/{id}/dispatch`| Transfers | Xuất điều chuyển: trừ OnHand kho nguồn, ghi InTransit, ghi Ledger TRANSFER_OUT. |
| `POST` | `/api/stock-transfers/{id}/receive` | Transfers | Nhập điều chuyển: tăng OnHand kho đích, tính lại WAC chi nhánh đích, ghi Ledger TRANSFER_IN. |
| `POST` | `/api/stock-transfers/{id}/reject`  | Transfers | Từ chối nhận hàng: hoàn trả hàng về kho nguồn, ghi TRANSFER_REVERSAL. |
| `POST` | `/api/stock-counts` | Counts | Khởi tạo đợt kiểm kê kho (phạm vi ALL hoặc PARTIAL), snapshot số tồn sổ sách. |
| `POST` | `/api/stock-counts/{id}/record` | Counts | Ghi nhận số kiểm đếm thực tế của từng SKU. |
| `POST` | `/api/stock-counts/{id}/confirm`| Counts | Duyệt kiểm kê: tự động tạo `StockAdjustment`, ghi Ledger IN/OUT, cân bằng số dư OnHand. |
| `POST` | `/api/stock-counts/{id}/cancel` | Counts | Hủy bỏ đợt kiểm kê chưa duyệt. |
| `POST` | `/api/webhooks/{channel}` | Webhooks | Tiếp nhận webhook sàn TMĐT (`shopee`, `tiktok`, `lazada`), chuẩn hóa Canonical Model, giữ chỗ an toàn. |
| `POST` | `/api/webhooks/simulator/{channel}` | Simulator | Giả lập gửi payload từ 3 sàn phục vụ dev và testing. |
| `POST` | `/api/webhooks/simulator/batch` | Simulator | Bắn đồng thời 50 requests tranh chấp 1 SKU để kiểm chứng chống bán vượt. |
| `GET / WS` | `/hubs/notifications` | Realtime | SignalR Hub phát sự kiện `OrderCreated` và `StockChanged` đến client có quyền. |

### 3.2. Cơ sở dữ liệu & Cấu hình Provisioning
* **PostgreSQL Schema:** Cập nhật bảng `ShopIntegration`, bổ sung trường `CostPrice` trên `OrderItem`, các chỉ mục phục vụ truy vấn tranh chấp (`IX_Orders_TenantId_ExternalOrderId_ShopIntegrationId`).
* **Hangfire Schema Provisioning:** Bổ sung script [`docs/database/hangfire_schema_provisioning.sql`](../../database/hangfire_schema_provisioning.sql) tạo schema `hangfire` độc lập trên PostgreSQL.
* **Cấu hình Worker Hangfire:**
  - Bật worker: Cấu hình trong `appsettings.json`: `"Hangfire": { "Enabled": true }`.
  - Tắt worker: Chuyển `"Enabled": false` (server WebApi không khởi chạy Hangfire Server, an toàn cho môi trường test/migrate).

### 3.3. Giới hạn kỹ thuật cần lưu ý
* **Realtime Best-Effort:** Sự kiện SignalR được đẩy sau khi commit transaction database thành công. Nếu client bị mất kết nối mạng, client phải refetch dữ liệu qua REST API khi reconnect (chưa có durable outbox/event store).
* **Bảo vệ ranh giới Tenant:** Toàn bộ API nghiệp vụ đều bắt buộc có `TenantId` từ JWT Token và có kiểm tra quyền truy cập chi nhánh (`BranchId`).
* **Không làm giả kết quả kiểm thử:** Các bài test sử dụng Mock hoặc InMemory chỉ dùng cho Unit Test và Hub Connection test; toàn bộ logic Concurrency, Deadlock, và Transaction đều được kiểm chứng trên **PostgreSQL 16 thật**.

---

## 4. Nguồn Kiểm Chứng Hiện Hành (S3-B01-FIX & Full Integration)

Lượt kiểm chứng toàn diện mới nhất sau khi khắc phục triệt để lỗi B-01 (Task `S3-B01-FIX`):
* **Build Solution:** `dotnet build src/backend/OISM.sln -c Release --no-restore` ➔ **PASS**, **0 warning, 0 error**.
* **Unit Tests:** `dotnet test src/backend/tests/OISM.UnitTests` ➔ **129/129 PASS (100%)**.
* **Full Integration Suite:** Chạy mới 1 lượt trọn vẹn toàn bộ integration tests của solution trên PostgreSQL thật:
  - **Kết quả:** **267/267 PASS, 0 FAIL, 0 skipped, thời gian chạy: 15 phút 30 giây**.
  - **Phân rã kiểm thử:**
    - Targeted B-01 test: 1/1 PASS (`Postgres_Warehouse_ChangeBranch_WhenHasHistory_ReturnsControlled409_AndCleanWarehouseSucceeds`).
    - Dedicated B-01 regression suite: 6/6 PASS (`WarehouseBranchReassignmentRegressionTests`).
    - 11/11 ReservationExpiry tests PASS; 14/14 Webhook tests PASS; 4/4 Realtime Hub tests PASS; 8/8 Simulator tests PASS.
* **Tổng cộng toàn bộ Solution:** **396/396 PASS (100%)**, không có bất kỳ test nào thất bại hoặc bị bỏ qua.

---

## 5. Trạng Thái Khắc Phục Lỗi Tồn Đọng Baseline (B-01) — ĐÃ GIẢI QUYẾT TRIỆT ĐỂ

Lỗi tồn đọng baseline B-01 (`Postgres_Warehouse_ChangeBranch_WhenHasHistory_ReturnsControlled409_AndCleanWarehouseSucceeds`) đã được **khắc phục triệt để và bàn giao READY_FOR_REVIEW**:
* **Nguyên nhân:** EF Core xem bộ ba `{ TenantId, Id, BranchId }` là Principal/Alternate Key (do cấu hình composite FK từ các bảng phụ thuộc), Change Tracker chặn việc sửa đổi key property trên tracked entity và ném `InvalidOperationException` (unhandled 500).
* **Giải pháp:** Cập nhật kho sạch thông qua parameterized SQL trực tiếp trong transaction (`ExecuteSqlInterpolatedAsync`), detach tracked entity khỏi Change Tracker, kiểm tra lịch sử trên 7 bảng phụ thuộc, khóa dòng bi quan `FOR UPDATE` và bắt lỗi PostgreSQL constraint `23503` trả về 409 Conflict.
* **Tài liệu bàn giao chi tiết:** Xem gói [`baseline-b01-fix/`](./codex-antigravity/baseline-b01-fix/README.md) và biên bản [`handover.md`](./codex-antigravity/baseline-b01-fix/handover.md).
* **Trạng thái:** `READY_FOR_REVIEW` (Antigravity nghiệm thu nội bộ 100% test pass; chờ Codex kiểm tra và cấp VERIFIED chính thức).

---

## 6. Đối Chiếu Kế Hoạch Dự Án & Phạm Vi Thực Tế

| Hạng mục trong Kế hoạch Sprint 3 | Phụ trách chính | Trạng thái thực tế trong đợt nghiệm thu này |
| :--- | :--- | :--- |
| **Lõi vận hành (Order, POS, Reserve, Chuyển kho, Kiểm kê)** | Đăng Khoa (BE1) | **HOÀN TẤT & VERIFIED 100%** (Task 1..4B) |
| **Tiếp nhận Webhook 3 sàn & SignalR realtime** | Đăng Khoa (BE1) | **HOÀN TẤT & VERIFIED 100%** (Task 5) |
| **Hangfire Worker tự hủy giữ chỗ quá hạn 15 phút** | Đăng Khoa (BE1) | **HOÀN TẤT & VERIFIED 100%** (Task 6) |
| **Khắc phục lỗi đổi chi nhánh kho sạch (B-01)** | Đăng Khoa (BE1) | **HOÀN TẤT & READY_FOR_REVIEW** (S3-B01-FIX) |
| **Trình giả lập Webhook Simulator cơ bản** | Đăng Khoa (BE1) | **HOÀN TẤT** (Endpoint nhận payload & batch concurrency test) |
| **Trình giả lập Simulator đầy đủ (Full UI / Mock sàn)** | Phối hợp / BE2 | Chưa thuộc phạm vi nghiệm thu BE1 Core |
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
* **Trạng thái PR:** `Draft / Open` (Head: `feature/OISMS-40-loi-van-hanh-kho-va-don-hang` ➔ Base: `develop`)
* **Trạng thái Merge thực tế:** **CHƯA MERGE VÀO DEVELOP** *(Code hiện nằm trên nhánh feature và PR #21 đang mở. Chưa merge do cần hoàn tất CI pipeline và chờ Codex/Team review theo quy trình repo; không tự ý bypass branch protection)*.
