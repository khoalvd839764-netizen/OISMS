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
| **Nhựt** | **Backend phụ (BE2)** | • Review các module mình phụ trách, sửa các bug còn lại.<br/>• Rà soát toàn bộ endpoint auth, catalog, report, AI; hoàn thiện tài liệu backend. | Tài liệu Backend/API docs hoàn chỉnh; các testcase liên quan được retest đạt. |
| **Phúc Hữu & Nam Nguyễn** | **Frontend Admin** | • Kiểm thử hồi quy và sửa lỗi trên Web Admin.<br/>• Chốt toàn bộ ảnh chụp màn hình (`screenshot`) và hoàn thiện tài liệu hướng dẫn sử dụng (`User Guide`).<br/>• Xây dựng slide trình chiếu phần Web Admin. | Web Admin hoạt động ổn định; tài liệu User Guide và slide trình chiếu chu đáo. |
| **Phương Lê** | **Frontend POS** | • Kiểm thử hồi quy và sửa lỗi trên Web POS.<br/>• Kiểm tra in ấn, máy di động, cài đặt PWA trên thiết bị thật.<br/>• Hoàn thiện kịch bản trình diễn demo luồng bán hàng POS. | POS hoạt động ổn định tuyệt đối; kịch bản demo trơn tru, hóa đơn in ra chuẩn đẹp. |
| **Cả nhóm** | **Kiểm thử (QA / Tester)** | • Chạy lại các testcase, Swagger và UI quan trọng; retest các bản sửa lỗi.<br/>• Đối chiếu Ma trận truy vết yêu cầu (`RTM`) lần cuối.<br/>• Kiểm tra độc lập bộ cài đặt trên máy sạch và thu thập bằng chứng. | Báo cáo kiểm thử cuối cùng (`Test Report`) kèm danh sách các hạn chế còn lại; RTM đã kiểm chứng 100%. |
| **Toàn nhóm** | **All Members** | • Hoàn thiện trọn vẹn bộ hồ sơ nghiệm thu đồ án: `URS/RTM`, `Architecture Design`, `Detailed Design (UML Class, ERD, Sequence)`, `Software Testing Document`, `Installation & User Guide`, `Source Code`, `Docker`, `GitHub Actions CI/CD`.<br/>• Diễn tập phân vai bảo vệ đồ án: trình bày slide và trình diễn kịch bản demo. | Bộ hồ sơ nghiệm thu đồ án hoàn chỉnh 100%, sẵn sàng bảo vệ trước hội đồng chấm thi. |

---

## 3. Bảng Đánh Giá Đóng Góp Thành Viên & Retrospective Sprint 5 (Đồng bộ Confluence Page ID: 18284569)

### 3.1. Bảng Đánh Giá Đóng Góp Thành Viên (Contribution Matrix)

| STT | Thành viên | Nhiệm vụ chính phụ trách | Đánh giá hoàn thành | Tinh thần làm việc | Đánh giá của Trưởng nhóm / Ghi chú (Notes) |
| :---: | :--- | :--- | :---: | :---: | :--- |
| **1** | **Lê Võ Đăng Khoa** | Rà soát, check, sửa code còn tồn đọng toàn hệ thống, kiến trúc hệ thống, điều phối | Đạt 100% | Rất tốt / Chủ động | Bao quát tốt tiến độ kỹ thuật, trực tiếp giải quyết các vấn đề cốt lõi. |
| **2** | **Phùng Minh Nhựt** | Rà soát, check, sửa code tồn đọng backend, chuẩn hóa APIs | Đạt 100% | Tích cực | Phối hợp nhịp nhàng với trưởng nhóm trong rà soát và kiểm thử mã nguồn. |
| **3** | **Phương Lê** | Viết và hoàn thiện tài liệu SRS / SDD, hỗ trợ giao diện | Chưa đạt / Cần làm lại | Thiếu nghiêm túc / Hời hợt | **Lạm dụng AI, không có kiểm chứng, làm bài hời hợt; nội dung sao chép máy móc từ công cụ AI mà không đối chiếu với kiến trúc thực tế và mã nguồn hệ thống OISMS.** |
| **4** | **Nam Nguyễn** | Viết và chuẩn hóa tài liệu SRS, rà soát ma trận yêu cầu | Đạt tiến độ | Tích cực | Nỗ lực bám sát tiến độ và đảm bảo chất lượng nội dung được phân công. |
| **5** | **Phúc Hữu** | Viết tài liệu SRS / SDD | Đúng hạn nhưng hời hợt / Kém hiệu quả | Đối phó / Hời hợt | **Nộp bài đúng deadline và nộp sớm nhưng làm việc kém hiệu quả, kết quả hời hợt, không đúng ý nghĩa, sai khái niệm, sơ đồ vẽ sai; không nắm bàn giao và không nắm được sản phẩm bàn giao.** |

### 3.2. Đánh Giá Cải Tiến Quy Trình Scrum (Sprint 5 Retrospective)

#### 🔴 Những Điểm Tồn Tại & Thách Thức (What Went Wrong?)
- **Lạm dụng AI và chất lượng bàn giao hời hợt:** Thành viên **Phương Lê** lạm dụng công cụ AI để sinh nội dung tài liệu mà không đối chiếu, kiểm chứng với mã nguồn thực tế và kiến trúc hệ thống của OISMS; làm bài hời hợt, nội dung thiếu chính xác, sai lệch bối cảnh so với thiết kế thực tế của dự án.
- **Hiệu suất làm việc kém hiệu quả:** Thành viên **Phúc Hữu** dù nộp bài sớm nhưng kết quả hời hợt, không đúng ý nghĩa, sai khái niệm chuyên môn, sơ đồ vẽ sai; không nắm được quy trình bàn giao và sản phẩm bàn giao của dự án.

#### 🚀 Kế Hoạch Hành Động Khắc Phục (Action Items)
- **Yêu cầu Phương Lê nghiêm túc kiểm điểm thái độ làm việc, chấm dứt việc lạm dụng AI tạo nội dung hời hợt không qua kiểm chứng; phải trực tiếp đối chiếu mã nguồn thực tế tại repository OISMS để viết lại và hoàn thiện các phần được giao.**
- **Yêu cầu Phúc Hữu nghiêm túc chấn chỉnh thái độ làm việc, không làm đối phó/hời hợt, phải chủ động nắm bắt sản phẩm bàn giao để sửa lại các khái niệm và sơ đồ bị sai, không làm ảnh hưởng đến chất lượng chung của toàn đội.**

---

## 4. Tình Trạng Thẩm Định Tài Liệu Thiết Kế Chi Tiết SDD (Mục 3.1 → 3.11)

> 🚨 **CẢNH BÁO TIẾN ĐỘ & KIỂM ĐỊNH TỪ TRƯỞNG NHÓM (SPRINT 5):**  
> **TOÀN BỘ CÁC PHÂN HỆ TỪ MỤC 3.1 (XÁC THỰC) ĐẾN MỤC 3.11 ĐỀU Ở TRẠNG THÁI: 🔴 CHƯA ĐẠT YÊU CẦU — CHƯA ĐỐI CHIẾU CODE THỰC TẾ.**  
> *(Nghiêm cấm lạm dụng AI tạo tài liệu đối phó/hời hợt. Toàn bộ sơ đồ Class Diagram, Sequence Diagram UML 2.0 và Bảng Class Specification bắt buộc phải được đối chiếu 100% từng class, method, attribute, entity và controller/service thực tế trong repository trước khi nghiệm thu.)*

| Phân hệ Nghiệp vụ SDD | Mã Flow | Người phụ trách | Trạng thái Thẩm định | Đánh giá & Yêu cầu |
| :--- | :---: | :---: | :---: | :--- |
| **3.1. Xác thực & Đăng nhập Đa người thuê** | `Flow 3.1` | Nam Nguyễn | 🔴 **CHƯA ĐẠT YÊU CẦU** | Chưa đối chiếu code thực tế |
| **3.2. Bán hàng Quầy POS & Thanh toán VietQR** | `Flow 3.2` | Nam Nguyễn | 🔴 **CHƯA ĐẠT YÊU CẦU** | Chưa đối chiếu code thực tế |
| **3.3. Webhook Đơn sàn TMĐT & Giữ chỗ Online** | `Flow 3.3` | Nam Nguyễn | 🔴 **CHƯA ĐẠT YÊU CẦU** | Chưa đối chiếu code thực tế |
| **3.4. Mua hàng, Nhập kho & Tính Giá vốn WAC** | `Flow 3.4` | Phúc Hữu | 🔴 **CHƯA ĐẠT YÊU CẦU** | Chưa đối chiếu code thực tế (Sai khái niệm/sơ đồ) |
| **3.5. Chuyển kho Nội bộ 2 Bước** | `Flow 3.5` | Phúc Hữu | 🔴 **CHƯA ĐẠT YÊU CẦU** | Chưa đối chiếu code thực tế (Sai khái niệm/sơ đồ) |
| **3.6. Kiểm kê Kho Định kỳ 2 Bước & Duyệt Chênh lệch** | `Flow 3.6` | Phúc Hữu | 🔴 **CHƯA ĐẠT YÊU CẦU** | Chưa đối chiếu code thực tế (Sai khái niệm/sơ đồ) |
| **3.7. Đăng ký Gian hàng SaaS & Onboarding** | `Flow 3.7` | Nhựt Phùng Minh | 🔴 **CHƯA ĐẠT YÊU CẦU** | Chưa đối chiếu code thực tế |
| **3.8. Quản trị Nhân viên & Phân quyền RBAC** | `Flow 3.8` | Phương Lê | 🔴 **CHƯA ĐẠT YÊU CẦU** | Chưa đối chiếu code thực tế (Lạm dụng AI) |
| **3.9. Danh mục Sản phẩm & Đa đơn vị tính** | `Flow 3.9` | Nhựt Phùng Minh | 🔴 **CHƯA ĐẠT YÊU CẦU** | Chưa đối chiếu code thực tế |
| **3.10. Quản lý Đổi trả Hàng Bán lẻ (CS-01)** | `Flow 3.10` | Nhựt Phùng Minh | 🔴 **CHƯA ĐẠT YÊU CẦU** | Chưa đối chiếu code thực tế |
| **3.11. Báo cáo Doanh thu, Lãi gộp & Cảnh báo Tồn** | `Flow 3.11` | Phương Lê | 🔴 **CHƯA ĐẠT YÊU CẦU** | Chưa đối chiếu code thực tế (Lạm dụng AI) |


