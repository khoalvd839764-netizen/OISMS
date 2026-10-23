# 08. QUẢN LÝ KẾ HOẠCH & TÀI LIỆU CHUNG THEO SPRINT (SPRINT DOCUMENTATION)

| Thông tin kiểm soát thư mục | Chi tiết |
| :--- | :--- |
| **Mã phân hệ (Module ID)** | `MOD-SPR-08` |
| **Phiên bản hiện hành** | `v1.0.0` |
| **Ngày cập nhật gần nhất** | `2026-10-22` |
| **Nguồn đối chiếu gốc** | `De bai CNPM.pdf` & Confluence Page ID `5275650` (Version 9) — *Kế hoạch Phân công Công việc Chi tiết các Sprint (OISMS)* |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả nội dung & Chi tiết rà soát thay đổi (Change Check) | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | • Khởi tạo danh mục quản lý kế hoạch các Sprint.<br/>• **Check đối chiếu Confluence (`Page ID: 5275650`):** Cập nhật khung thời gian thực tế (`S2: 13/10–17/10`, `S3: 18/10–22/10`, `S4: 23/10–27/10`, `S5: 28/10–01/11`) và điều chỉnh lại ranh giới công việc: đưa `Webhook Simulator`, `SignalR`, `Hangfire expire` và `Report cơ bản` về **Sprint 3**; đưa `AI Forecasting`, `Report nâng cao` và `Feature Freeze` về **Sprint 4**; khóa **Sprint 5** chỉ dành cho Review, Regression Test & Đóng gói hồ sơ. | `INIT-SYNC-CONF-5275650` |

---

## Danh mục Các Giai Đoạn Sprint (Đồng bộ Confluence)

| Giai đoạn | Thư mục | Khung thời gian | Mục tiêu & Trọng tâm triển khai (Theo Confluence) |
| :---: | :--- | :---: | :--- |
| **Sprint 1** | *(Đã đóng)* | `09/10/2026 – 12/10/2026` | **Khởi tạo & Đặc tả yêu cầu:** Thu thập yêu cầu, lập URS & kế hoạch dự án, dựng khung sườn mã nguồn và Docker. |
| **Sprint 2** | [`./sprint-2/`](./sprint-2/README.md) | `13/10/2026 – 17/10/2026` (`D1 – D6`) *(Nghiệm thu 18–20/10)* | **Nền tảng & Luồng Dữ liệu Đầu tiên:** Hoàn thành CSDL nền tảng 29 bảng, Multi-tenant Isolation, JWT/RBAC, Danh mục/SKU/Barcode, Sổ cái Append-Only, Nhập kho nguyên tử, Tính giá vốn WAC, Lõi giữ chỗ chống bán vượt, Web Admin & Web POS PWA. |
| **Sprint 3** | [`./sprint-3/`](./sprint-3/README.md) | `18/10/2026 – 22/10/2026` (`D7 – D12`) | **Lõi Vận hành Hoàn chỉnh:** Hoàn thành `Order State Machine` (`reserve/confirm/cancel/complete`), Checkout POS toàn giỏ nguyên tử + Chốt giá vốn (`Cost Snapshot`), Chuyển kho 2 bước & Kiểm kê kho (`FR-INV-03..04`), Trình giả lập Webhook 3 sàn (`FR-SIM-01`), Thông báo `SignalR` (`FR-SIM-02`), `Hangfire` hủy giữ chỗ quá hạn (`FR-SIM-03`), và Báo cáo/cảnh báo cơ bản (`FR-REP-01..03` cơ bản). Chốt thiết kế AI. |
| **Sprint 4** | [`./sprint-4/`](./sprint-4/README.md) | `23/10/2026 – 27/10/2026` (`D13 – D18`) | **Trí tuệ Nhân tạo (AI), Hoàn thiện & Tối ưu Hóa (Feature Freeze):** Hiện thực lõi dự báo AI & gợi ý nhập hàng (`AI`), hoàn thiện biểu đồ & bộ lọc Báo cáo nâng cao (`FR-REP-01..03` đủ), `Hangfire` tổng hợp báo cáo hằng ngày, màn hình demo concurrency, đo kiểm đạt toàn bộ chỉ tiêu `NFR`. **Đóng toàn bộ tính năng mới (Feature Freeze) cuối Sprint 4.** |
| **Sprint 5** | [`./sprint-5/`](./sprint-5/README.md) | `28/10/2026 – 01/11/2026` (`D19 – D21`) | **Rà soát (Review), Hồi quy & Chốt Bàn giao:** **Không mở module mới.** Review toàn bộ code, sửa blocker bugs, kiểm thử hồi quy toàn diện, đóng gói Release Docker/CI-CD, hoàn thiện trọn bộ hồ sơ nghiệm thu đồ án và diễn tập bảo vệ. |
