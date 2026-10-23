# KẾ HOẠCH & PHÂN CÔNG CHI TIẾT SPRINT 4 (23/10/2026 – 27/10/2026 | D13 – D18)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SPR-04` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày cập nhật** | `2026-10-22` |
| **Nguồn truy vết gốc** | `De bai CNPM.pdf` & Confluence Page ID `5275650` (Version 9) — Mục III.4 |
| **Chủ đề Sprint 4** | **Trí tuệ Nhân tạo (AI), Hoàn thiện & Tối ưu Hóa (Feature Freeze)** |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả nội dung & Chi tiết rà soát thay đổi (Change Check) | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | • Thiết lập kế hoạch và phân công Sprint 4 (`23/10/2026 – 27/10/2026 \| D13 – D18`).<br/>• **Check thay đổi so với bản nháp ban đầu khi đối chiếu Confluence (`Page ID: 5275650`):**<br/>  1. Chuyển `AI Forecasting gợi ý nhập hàng` (`FR-AI-01`) từ Sprint 5 về **Sprint 4** (Đăng Khoa làm lõi AI & đánh giá độ tin cậy, Nhựt làm API dữ liệu lịch sử & recommendation, Phúc Hữu làm UI hiển thị gợi ý AI).<br/>  2. Bổ sung nhiệm vụ xây dựng **màn hình demo concurrency** trên Web Admin, `Hangfire daily reports`, đo kiểm đạt toàn bộ chỉ tiêu `NFR` (`>= 80% coverage`, benchmark hiệu năng) và chốt **Feature Freeze (đóng toàn bộ tính năng mới)** cuối Sprint 4. | `INIT-SYNC-SPR-04` |

---

## 1. Mục Tiêu Trọng Tâm Sprint 4
Phát triển module **Dự báo AI gợi ý nhập hàng (`FR-AI`)** theo đề bài, hoàn thiện toàn diện hệ thống **Báo cáo & Biểu đồ phân tích (`FR-REP-01..03`)** và đạt tất cả các chỉ tiêu phi chức năng (`NFR`). **Đóng toàn bộ tính năng mới (Feature Freeze) vào cuối Sprint 4.**

---

## 2. Bảng Phân Công Chi Tiết 5 Thành Viên (Đồng bộ 100% Confluence)

| Thành viên | Vai trò | Nhiệm vụ cụ thể trong Sprint 4 | Tiêu chuẩn nghiệm thu |
| :--- | :--- | :--- | :--- |
| **Đăng Khoa** *(Trưởng nhóm)* | **Backend chính (BE1)** | • Thiết kế và hiện thực lõi **AI forecasting & evaluation** (`FR-AI-01`).<br/>• Tối ưu locking, queries, index và bảo mật để đạt toàn bộ chỉ tiêu `NFR`.<br/>• Cấu hình **Hangfire daily reports** (`FR-SIM-03`) và review toàn hệ thống.<br/>• Đảm bảo Backend automated tests đạt tối thiểu `80% coverage` phần lõi.<br/>• Hoàn thiện Docker, HTTPS, SignalR deployment và benchmark hiệu năng chính thức. | AI thật có đánh giá độ tin cậy; NFR có số đo bằng chứng; bản tích hợp sẵn sàng đóng tính năng (`Feature Freeze`). |
| **Nhựt (TV2)** | **Backend phụ (BE2)** | • Chuẩn bị và kiểm tra dữ liệu lịch sử; xây dựng API `recommendation / report` theo model AI của Khoa.<br/>• Hoàn thiện các bộ lọc filter, velocity, threshold cảnh báo tồn thấp (`FR-REP-01..03`).<br/>• Sửa lỗi CRUD, auth, query; hoàn thiện tài liệu backend. | API báo cáo và AI đầy đủ, dữ liệu truy vết được minh bạch; tài liệu Swagger đồng bộ. |
| **Phúc Hữu (TV3)** | **Frontend Admin** | • Hoàn thiện Report filters, biểu đồ doanh thu/giá vốn/lãi gộp, tồn kho, velocity (`FR-REP-01..02`).<br/>• Xây dựng giao diện **AI gợi ý nhập hàng** (hiển thị rõ khi thiếu dữ liệu) và cảnh báo tồn thấp (`FR-REP-03`).<br/>• Tối ưu giao diện thích ứng responsive.<br/>• Xây dựng **màn hình demo concurrency** lấy kết quả đo thật phục vụ hội đồng. | Admin đủ các view và trạng thái theo đề bài; số liệu hiển thị trực quan và đồng bộ. |
| **Phương (TV4)** | **Frontend POS** | • Tối ưu toàn diện POS PWA, in ấn, phím tắt, giao diện trên thiết bị di động/cảm ứng (`NFR-USA-02`).<br/>• Demo mã QR thanh toán rõ phạm vi; xử lý lỗi reconnect và lưu đơn tạm ngoại tuyến.<br/>• Tối ưu hiệu năng giao diện phản hồi `< 200ms` và khả năng tiếp cận. | POS hoạt động trơn tru trên thiết bị mục tiêu; thao tác bán hàng siêu tốc. |
| **Thành viên 5** | **Tester (QA)** | • Đo và lưu trữ chỉ số `NFR` theo kịch bản và môi trường cụ thể.<br/>• Swagger và UI kiểm thử hồi quy toàn diện; test role, tenant isolation.<br/>• Đối chiếu số liệu report và AI; thử nghiệm cài đặt trên máy sạch và log chỉ số uptime. | Báo cáo chất lượng, kết quả module AI, bằng chứng NFR và ma trận RTM đã được kiểm chứng. |
