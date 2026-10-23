# KẾ HOẠCH & PHÂN CÔNG CHI TIẾT SPRINT 3 (18/10/2026 – 22/10/2026 | D7 – D12)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SPR-03` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày cập nhật** | `2026-10-22` |
| **Nguồn truy vết gốc** | `De bai CNPM.pdf` & Confluence Page ID `5275650` (Version 9) — Mục III.3 |
| **Chủ đề Sprint 3** | **Lõi Vận hành Hoàn chỉnh (Complete Operational Core)** |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả nội dung & Chi tiết rà soát thay đổi (Change Check) | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | • Thiết lập kế hoạch và phân công Sprint 3 (`18/10/2026 – 22/10/2026 \| D7 – D12`).<br/>• **Check thay đổi so với bản nháp ban đầu khi đối chiếu Confluence (`Page ID: 5275650`):**<br/>  1. Bổ sung vào Sprint 3: `Webhook Simulator 3 sàn` (`FR-SIM-01`), `SignalR realtime notification` (`FR-SIM-02`), `Hangfire hủy giữ chỗ quá hạn` (`FR-SIM-03`), `Truy vấn báo cáo & cảnh báo tồn thấp cơ bản` (`FR-REP-01..03`) và `Chốt thiết kế mô hình AI` (trước đó bản nháp để ở Sprint 4).<br/>  2. Phân rã chi tiết nhiệm vụ và tiêu chuẩn nghiệm thu của cả 5 thành viên (**Đăng Khoa, Nhựt, Phúc Hữu, Phương, TV5 - Tester**). | `INIT-SYNC-SPR-03` |

---

## 1. Mục Tiêu Trọng Tâm Sprint 3
Ưu tiên hoàn thành toàn bộ các yêu cầu chức năng (FR) lõi vận hành: **Order / POS / Reserve**, **Chuyển kho 2 bước & Kiểm kê kho**, **Trình giả lập Webhook 3 sàn TMĐT** và **Thông báo thời gian thực SignalR / Hangfire expire**. Các API báo cáo và cảnh báo tồn kho cơ bản phải bắt đầu trả dữ liệu thật trong Sprint này.

---

## 2. Bảng Phân Công Chi Tiết 5 Thành Viên (Đồng bộ 100% Confluence)

| Thành viên | Vai trò | Nhiệm vụ cụ thể trong Sprint 3 | Tiêu chuẩn nghiệm thu |
| :--- | :--- | :--- | :--- |
| **Đăng Khoa** *(Trưởng nhóm)* | **Backend chính (BE1)** | • Xây dựng máy trạng thái đơn hàng (`Order State Machine`), các thao tác `reserve / confirm / cancel / complete`.<br/>• Thiết lập cơ chế khóa dữ liệu (`Locking`); checkout toàn giỏ nguyên tử, `idempotency` và chốt giá vốn (`cost snapshot` — `FR-COST-02`).<br/>• Hiện thực **Chuyển kho nội bộ 2 bước (`FR-INV-03`)** và **Kiểm kê kho (`FR-INV-04`)**.<br/>• Lõi tiếp nhận Webhook đa kênh vào `Canonical Order Model` (`FR-ORD-01`); cấu hình `SignalR events` (`FR-SIM-02`) và `Hangfire` tự hủy giữ chỗ quá hạn (`FR-SIM-03`).<br/>• Chốt thiết kế mô hình AI gợi ý nhập hàng.<br/>• Viết automated core tests. | Lõi đơn và kho liên kênh nhất quán, không tồn tại hai engine trừ tồn; vượt qua test tranh tồn `50 reqs/1 SKU`, rollback và expire tự động. |
| **Nhựt (TV2)** | **Backend phụ (BE2)** | • Hoàn thiện auth, quyền và catalog còn thiếu.<br/>• Xây dựng API `list / detail / filter` đơn hàng và chứng từ kho theo thiết kế của Khoa.<br/>• Viết câu truy vấn báo cáo cơ bản: `net revenue`, `COGS`, `gross profit`, `inventory value`, `velocity`, `low-stock`, `threshold` (`FR-REP-01..03`).<br/>• Xây dựng công cụ **Simulator gửi payload 3 kênh (Shopee, TikTok, Lazada)** (`FR-SIM-01`) qua endpoint của Khoa. | API đọc và báo cáo cơ bản có số liệu thật, ngưỡng và cảnh báo hoạt động; simulator chạy tốt; test queries bảo đảm cô lập dữ liệu. |
| **Phúc Hữu (TV3)** | **Frontend Admin** | • Xây dựng form tạo đơn trên Admin, duyệt đơn online, in phiếu giao hàng và hủy đơn (`FR-ORD-03`).<br/>• Xây dựng giao diện **Chuyển kho 2 bước** và **Kiểm kê kho**.<br/>• Nối các báo cáo cơ bản vào Dashboard đúng session, filter và role.<br/>• Nhận **SignalR popup thông báo và âm thanh chuông** đón đơn mới (`FR-SIM-02`). | Admin quản lý thực tế đơn hàng, kho bãi và báo cáo; không sửa state local làm giả nghiệp vụ. |
| **Phương (TV4)** | **Frontend POS** | • Hoàn thiện nối **Checkout API**, thanh toán toàn giỏ hàng nguyên tử và in hóa đơn (`FR-POS-04`).<br/>• Xử lý triệt để thiếu tồn, lỗi mạng, kết quả chưa xác định và chống thao tác trùng (`idempotency`).<br/>• Tích hợp `SignalR`; tối ưu PWA, bàn phím phím tắt, cảm ứng và in ấn. | POS thanh toán thật, bảo vệ tồn giữ chỗ cho đơn online, in đúng dữ liệu hóa đơn thực tế. |
| **Thành viên 5** | **Tester (QA)** | • Swagger và UI test cho Order, POS, Inventory, Simulator, các ca phủ định, trùng lặp, expire, multi-tenant.<br/>• Chạy công cụ concurrency do backend cung cấp, ghi kết quả và bugs.<br/>• Cập nhật ma trận RTM kèm bằng chứng kiểm định. | Báo cáo luồng lõi hoàn chỉnh; RTM có testcase và evidence; danh sách lỗi được phân loại ưu tiên sửa chữa. |
