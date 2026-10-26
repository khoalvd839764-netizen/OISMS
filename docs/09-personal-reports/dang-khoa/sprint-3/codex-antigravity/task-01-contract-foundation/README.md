# Task 1: API Contract & Persistence Foundation — Index & Trạng Thái Hiện Hành

| Thông tin kiểm soát Task 1 | Chi tiết |
| :--- | :--- |
| **Mã Task (Task ID)** | `S3-BE1-T01-CONTRACT-FOUNDATION` |
| **Chủ đề** | API Contract & Persistence Foundation Sprint 3 |
| **Phiên bản hiện hành** | `v1.0` |
| **Trạng thái nghiệm thu** | **DONE (theo báo cáo gốc)** *(Không tự đổi thành VERIFIED nếu thiếu biên bản Codex độc lập)* |
| **Baseline** | Sprint 2 (commit `ab67d9e` / `043e9b5`) |
| **Người triển khai** | Lê Võ Đăng Khoa (Backend 1 Lead & Team Lead) |
| **Reviewer / Nguồn nghiệm thu** | Báo cáo kỹ thuật gốc Sprint 3 (Mục 1..7) |
| **Ngày hoàn thành** | 24/10/2026 |

---

## 1. Mục tiêu & Phạm vi

Thiết lập toàn diện nền tảng kiến trúc, mô hình lưu trữ (Persistence Model) và hợp đồng giao tiếp (API Contracts) cho toàn bộ các chức năng lõi của Sprint 3:
1. **Domain Entities & EF Mapping:** Thiết lập `Order`, `OrderItem`, `Payment`, `Reservation`, `StockTransfer`, `StockTransferItem`, `StockCount`, `StockCountItem`, `StockAdjustment`, `StockAdjustmentItem`, `ShopIntegration`.
2. **Persistence:** Mở rộng `AppDbContext`, đăng ký `DbSet`, cấu hình khóa ngoại, indexes, concurrency tokens và tenant isolation.
3. **API Contracts & DTOs:** Thiết lập hợp đồng DTO cho Dual-Route Checkout (POS vs Online), Stock Transfer, Stock Count, Webhook Canonical Model và SignalR Realtime.
4. **Service Interfaces:** Định nghĩa `IOrderService`, `IReservationService`, `IStockTransferService`, `IStockCountService`, `IWebhookService`.

---

## 2. Bằng chứng kiểm thử & Xác thực theo Provider

| Phân loại kiểm chứng | Lệnh / Cơ chế thực hiện | Kết quả thực tế | Provider / Môi trường |
| :--- | :--- | :--- | :--- |
| **Build Solution** | `dotnet build src/backend/OISM.sln -c Release` | **PASS**, 0 warning, 0 error | .NET 8 SDK / Local |
| **Schema & Migrations** | `DatabaseMigrationService` / Initial baseline | **PASS**, bảng và index khởi tạo đầy đủ | PostgreSQL 16 Alpine |
| **Unit Tests** | `dotnet test --filter Category=Unit` | **100% PASS** (114/114 tests) | xUnit / .NET 8 |

---

## 3. Lịch sử bàn giao & Tài liệu chi tiết

* **Handoff v1.0:** [`handoffs/v1.0-handoff.md`](./handoffs/v1.0-handoff.md) — Chi tiết Mục 1 đến Mục 7 trích xuất từ báo cáo gốc:
  - *Mục 1:* Mục tiêu & Phạm vi chi tiết
  - *Mục 2:* Kế thừa nền tảng Sprint 2
  - *Mục 3:* Các quyết định kỹ thuật & Lý do (Dual-Route Checkout, Pessimistic Locking, Idempotency, SignalR Hub, Hangfire Expiry)
  - *Mục 4:* Danh sách file thay đổi & bổ sung (Changelog)
  - *Mục 5:* Bảng bàn giao cho các thành viên (FE Admin, FE POS, BE2, Tester)
  - *Mục 6:* Lệnh kiểm chứng & Kết quả thực tế
  - *Mục 7:* Checklist nghiệm thu Task 1 & Kế hoạch tiếp theo
