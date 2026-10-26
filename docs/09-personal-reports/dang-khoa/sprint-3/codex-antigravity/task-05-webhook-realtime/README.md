# Task 5: Webhook Đa Kênh & SignalR Realtime — Index & Trạng Thái Hiện Hành

| Thông tin kiểm soát Task 5 | Chi tiết |
| :--- | :--- |
| **Mã Task (Task ID)** | `S3-BE1-T05-WEBHOOK-REALTIME` |
| **Chủ đề** | Tiếp nhận Webhook Đa Kênh & Hạ tầng Thời gian thực SignalR |
| **Phiên bản hiện hành** | `v1.4` |
| **Trạng thái nghiệm thu** | **VERIFIED v1.4** *(Nghiệm thu bởi Codex vòng 5, ngày 26/10/2026)* |
| **Baseline** | Task 4B (`S3-BE1-T04B-STOCK-COUNT`) + Sprint 2 |
| **Người triển khai** | Lê Võ Đăng Khoa (Backend 1 Lead & Team Lead) |
| **Reviewer** | Codex (`codex-antigravity-workflow`) |
| **Ngày hoàn thành** | 26/10/2026 |

---

## 1. Mục tiêu & Phạm vi

Xây dựng cổng tiếp nhận đơn hàng đa sàn TMĐT và hạ tầng phát thông báo thời gian thực:
1. **Tiếp nhận Webhook 3 sàn (`FR-ORD-01`):** Cổng `POST /api/webhooks/{channel}` tiếp nhận webhook từ Shopee, TikTok Shop, Lazada, ánh xạ chuẩn hóa về `Canonical Order Model`.
2. **Khóa chống bán vượt & Idempotency:** Áp dụng khóa bi quan có thứ tự SKU, chặn đứng 50 requests đồng thời tranh chấp 1 SKU không gây âm kho. Xử lý idempotency theo `ExternalOrderId`.
3. **Hạ tầng truyền thông thời gian thực SignalR (`FR-SIM-02`):** Cấu hình `NotificationHub` (`/hubs/notifications`) đẩy thông báo tức thì `OrderCreated` và `StockChanged` tới Admin Dashboard và máy POS.
4. **Bảo mật & Phân quyền nhóm:** Phân lập kết nối theo `TenantId` và `BranchId`; ẩn giá vốn (`CostPrice`) đối với tài khoản nhân viên thu ngân POS.
5. **Bộ công cụ Simulator (`FR-SIM-01`):** Cung cấp controller giả lập payload 3 sàn và batch concurrency stress test.

---

## 2. Bảng quản lý Findings (T5-F01 .. T5-F08)

| Finding ID | Độ ưu tiên | AC liên quan | Mô tả vấn đề & Giải pháp xử lý | Trạng thái |
| :---: | :---: | :---: | :--- | :---: |
| **T5-F01** | P1 | `AC-01`<br/>`AC-02` | **Chuẩn hóa Canonical Order Model:** Mapping chính xác payload Shopee/TikTok/Lazada sang mô hình đơn chung, tự động tính tổng tiền & snapshot WAC. | **CLOSED**<br/>*(Đóng tại R3)* |
| **T5-F02** | P1 | `AC-03`<br/>`AC-04` | **Concurrency Stress Test 50 reqs/SKU:** Đúng các request hợp lệ giữ chỗ thành công (201 Created), các request vượt tồn nhận 409 INSUFFICIENT_STOCK có kiểm soát. | **CLOSED**<br/>*(Đóng tại R2)* |
| **T5-F03** | P1 | `AC-05` | **Xác thực Webhook kép:** Hỗ trợ xác thực qua `X-Webhook-Secret` hoặc JWT Bearer Token của Staff/Owner. | **CLOSED**<br/>*(Đóng tại R3)* |
| **T5-F04** | P2 | `AC-06` | **Chuẩn hóa mã lỗi RFC 7807:** Trả mã lỗi có cấu trúc (`code`, `message`, `details`) thay vì 500 unhandled. | **CLOSED**<br/>*(Đóng tại R2)* |
| **T5-F05** | P1 | `AC-07`<br/>`AC-08` | **SignalR NotificationHub & Privacy:** Cấu hình xác thực JWT token, phân nhóm theo Tenant/Branch, ẩn hoàn toàn giá vốn khỏi POS payload. | **CLOSED**<br/>*(Đóng tại R3)* |
| **T5-F06** | P1 | `AC-09`<br/>`AC-10` | **Sự kiện Realtime sau Commit:** Bọc phát thông báo sau khi SaveChanges và commit transaction thành công, không phát sự kiện rác khi rollback. | **CLOSED**<br/>*(Đóng tại R3)* |
| **T5-F07** | P1 | `AC-11`..`AC-14` | **Observer Wiring & Rollback Snapshot:** Nối `SqlStateDeadlockObserver` vào `AddInterceptors` của DbContext customFactory; positive control bắt lỗi 40P01; snapshot rollback đầy đủ WAC/timestamps; sửa 4 tên test SignalR Hub thật. | **CLOSED**<br/>*(Đóng tại R5)* |
| **T5-F08** | P2 | `AC-14` | **Simulator Controller:** Endpoint `/api/webhooks/simulator/{channel}` và `/batch` phục vụ kiểm thử tích hợp. | **CLOSED**<br/>*(Đóng tại R2)* |

---

## 3. Bằng chứng kiểm thử & Xác thực theo Provider

| Phân loại kiểm chứng | Lệnh / Kịch bản thực tế | Kết quả thực tế | Provider / Môi trường |
| :--- | :--- | :--- | :--- |
| **Build Solution** | `dotnet build src/backend/OISM.sln -c Release` | **PASS**, 0 warning, 0 error (1.82s) | .NET 8 SDK / Local |
| **Webhook Integration** | `FullyQualifiedName~WebhookIntegrationTests` | **14/14 PASS** (100%, 48s) | PostgreSQL 16 Alpine thật |
| **SignalR Hub Tests** | `FullyQualifiedName~RealtimeHubIntegrationTests` | **4/4 PASS** (100%, 9s) | SignalR LongPolling / InMemory |
| **Simulator Tests** | `FullyQualifiedName~WebhookSimulatorControllerTests` | **8/8 PASS** (100%, 7s) | HTTP Controller / InMemory |
| **Unit Tests** | `WebhookAndRealtimeUnitTests` & toàn bộ suite | **129/129 PASS** (100%) | .NET 8 / InMemory |
| **Regression Kế thừa** | Kế thừa từ Codex R3 | **249/250 PASS** *(chỉ B-01 fail)* | PostgreSQL 16 Alpine thật |

---

## 4. Danh mục tài liệu & Lịch sử các vòng

* **Brief gốc:** [`briefs/v1.0-brief.md`](./briefs/v1.0-brief.md) *(Đặc tả yêu cầu Task 5 v1.0)*
* **Handoff v1.0:** [`handoffs/v1.0-handoff.md`](./handoffs/v1.0-handoff.md) *(Mục 29 cũ)*
* **Review v1.0 (Codex R1):** [`reviews/v1.0-review-r1.md`](./reviews/v1.0-review-r1.md) *(Mục 30 cũ — CHANGES_REQUESTED)*
* **Handoff v1.1:** [`handoffs/v1.1-handoff.md`](./handoffs/v1.1-handoff.md) *(Mục 31 cũ)*
* **Review v1.1 (Codex R2):** [`reviews/v1.1-review-r2.md`](./reviews/v1.1-review-r2.md) *(Mục 32 cũ — CHANGES_REQUESTED)*
* **Handoff v1.2:** [`handoffs/v1.2-handoff.md`](./handoffs/v1.2-handoff.md) *(Mục 33 cũ)*
* **Review v1.2 (Codex R3):** [`reviews/v1.2-review-r3.md`](./reviews/v1.2-review-r3.md) *(Mục 34 cũ — CHANGES_REQUESTED)*
* **Handoff v1.3:** [`handoffs/v1.3-handoff.md`](./handoffs/v1.3-handoff.md) *(Mục 35 cũ)*
* **Review v1.3 (Codex R4):** [`reviews/v1.3-review-r4.md`](./reviews/v1.3-review-r4.md) *(Mục 36 cũ — CHANGES_REQUESTED)*
* **Handoff v1.4:** [`handoffs/v1.4-handoff.md`](./handoffs/v1.4-handoff.md) *(Mục 37 cũ — Hoàn thiện triệt để T5-F07)*
* **Review v1.4 (Codex R5):** [`reviews/v1.4-review-r5.md`](./reviews/v1.4-review-r5.md) *(Mục 38 cũ — VERIFIED v1.4)*
