# KẾ HOẠCH & PHÂN CÔNG CHI TIẾT SPRINT 5 (28/10/2026 – 01/11/2026 | D19 – D21)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SPR-05` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày cập nhật** | `2026-10-22` |
| **Nguồn truy vết gốc** | `De bai CNPM.pdf` & Confluence Page ID `5275650` (Version 9) — Mục III.5 |
| **Chủ đề Sprint 5** | **Rà soát (Review), Hồi quy & Chốt Bàn giao (Final Sign-off)** |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả nội dung & Chi tiết rà soát thay đổi (Change Check) | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | • Thiết lập kế hoạch và phân công Sprint 5 (`28/10/2026 – 01/11/2026 \| D19 – D21`).<br/>• **Check thay đổi so với bản nháp ban đầu khi đối chiếu Confluence (`Page ID: 5275650`):**<br/>  1. Loại bỏ việc phát triển tính năng `AI Forecasting` ở Sprint 5 (đã đưa về Sprint 4) và áp dụng quy tắc **Tuyệt đối không mở module mới**.<br/>  2. Tập trung 100% vào Code Review, sửa blocker bugs, kiểm thử hồi quy toàn diện, đóng gói Release Docker/CI-CD, hoàn thiện trọn bộ hồ sơ UML 2.0/RTM/User Guide và diễn tập phân vai bảo vệ đồ án cho cả 5 thành viên. | `INIT-SYNC-SPR-05` |

---

## 1. Mục Tiêu Trọng Tâm Sprint 5
**Tuyệt đối không mở module mới.** Tập trung rà soát toàn diện (`Review`), kiểm thử hồi quy (`Regression Testing`), sửa các lỗi còn tồn đọng, đóng gói bản phát hành và hoàn thiện trọn vẹn bộ hồ sơ nghiệm thu đồ án.

---

## 2. Bảng Phân Công Chi Tiết 5 Thành Viên & Toàn Nhóm (Đồng bộ 100% Confluence)

| Thành viên | Vai trò | Nhiệm vụ cụ thể trong Sprint 5 | Kết quả bàn giao |
| :--- | :--- | :--- | :--- |
| **Đăng Khoa** *(Trưởng nhóm)* | **Backend chính (BE1)** | • Review toàn bộ code và module lõi, sửa các lỗi chặn (`blocker bugs`).<br/>• Xác nhận automated tests, CI build trên GitHub Actions chạy đạt 100%.<br/>• Đóng gói bản phát hành (`Release tag`), kiểm tra Docker Compose, seed data, cấu hình và tài liệu API. | Bản phát hành chính thức trên Git; hướng dẫn kỹ thuật nhất quán; khởi chạy Docker một lệnh trên máy sạch. |
| **Nhựt (TV2)** | **Backend phụ (BE2)** | • Review các module mình phụ trách, sửa các bug còn lại.<br/>• Rà soát toàn bộ endpoint auth, catalog, report, AI; hoàn thiện tài liệu backend. | Tài liệu Backend/API docs hoàn chỉnh; các testcase liên quan được retest đạt. |
| **Phúc Hữu (TV3)** | **Frontend Admin** | • Kiểm thử hồi quy và sửa lỗi trên Web Admin.<br/>• Chốt toàn bộ ảnh chụp màn hình (`screenshot`) và hoàn thiện tài liệu hướng dẫn sử dụng (`User Guide`).<br/>• Xây dựng slide trình chiếu phần Web Admin. | Web Admin hoạt động ổn định; tài liệu User Guide và slide trình chiếu chu đáo. |
| **Phương (TV4)** | **Frontend POS** | • Kiểm thử hồi quy và sửa lỗi trên Web POS.<br/>• Kiểm tra in ấn, máy di động, cài đặt PWA trên thiết bị thật.<br/>• Hoàn thiện kịch bản trình diễn demo luồng bán hàng POS. | POS hoạt động ổn định tuyệt đối; kịch bản demo trơn tru, hóa đơn in ra chuẩn đẹp. |
| **Thành viên 5** | **Tester (QA)** | • Chạy lại các testcase, Swagger và UI quan trọng; retest các bản sửa lỗi.<br/>• Đối chiếu Ma trận truy vết yêu cầu (`RTM`) lần cuối.<br/>• Kiểm tra độc lập bộ cài đặt trên máy sạch và thu thập bằng chứng. | Báo cáo kiểm thử cuối cùng (`Test Report`) kèm danh sách các hạn chế còn lại; RTM đã kiểm chứng 100%. |
| **Toàn nhóm** | **All Members** | • Hoàn thiện trọn vẹn bộ hồ sơ nghiệm thu đồ án: `URS/RTM`, `Architecture Design`, `Detailed Design (UML Class, ERD, Sequence)`, `Software Testing Document`, `Installation & User Guide`, `Source Code`, `Docker`, `GitHub Actions CI/CD`.<br/>• Diễn tập phân vai bảo vệ đồ án: trình bày slide và trình diễn kịch bản demo. | Bộ hồ sơ nghiệm thu đồ án hoàn chỉnh 100%, sẵn sàng bảo vệ trước hội đồng chấm thi. |
