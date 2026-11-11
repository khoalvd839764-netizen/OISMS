# [OISMS] BÁO CÁO TIẾN ĐỘ & DIỄN BIẾN CUỘC HỌP SPRINT 5 (ĐẾN BUỔI 3)

**Dự án:** Omnichannel Inventory & Sales Management System (OISMS)  
**Không gian Confluence:** `OISMS` | **Thư mục:** `Report Sprint`  
**Mã tài liệu:** `DOC-REP-SPRINT-05-P3` | **Phiên bản:** `v1.0.0`  
**Giai đoạn:** **Sprint 5: Hoàn thành Dự án, Chuẩn hóa Hồ sơ SRS & Khắc phục Lỗi Tồn đọng**  
**Thời gian theo dõi:** `08/11/2026 – 09/11/2026` *(Ghi nhận tiến độ đến Buổi 3 — 09/11/2026)*  
**Người lập báo cáo (Reporter):** @Lê Võ Đăng Khoa *(Trưởng nhóm / System Architect)*  
**Đội ngũ tham gia:** @Lê Võ Đăng Khoa, @Phùng Minh Nhựt, @Nam Nguyễn, @Phuongw Le, @Phúc Hữu  
**Trạng thái hiện tại:** 🟡 **ĐANG TIẾN HÀNH (IN PROGRESS — ĐẾN BUỔI 3)**  

---

## 1. Mục Tiêu Trọng Tâm Giai Đoạn (Sprint Goals)

Giai đoạn này tập trung dồn toàn lực vào hai nhánh nhiệm vụ then chốt để chuẩn bị cho công tác nghiệm thu toàn diện dự án:

1. **Nhánh Tài liệu (Documentation Team):** Tập trung biên soạn, hoàn thiện và chuẩn hóa toàn bộ bộ tài liệu **Đặc tả Yêu cầu Phần mềm (SRS - Software Requirements Specification)** chi tiết, bao gồm 28 yêu cầu chức năng (FRs), 11 yêu cầu phi chức năng (NFRs), ma trận phân quyền RBAC và sơ đồ Use Cases bám sát mã nguồn thực tế.
2. **Nhánh Kỹ thuật (Technical & Code Review Team):** Rà soát toàn bộ source code (Clean Architecture 4 tầng, 22 module backend, 2 ứng dụng frontend Admin Dashboard & POS PWA), kiểm tra và sửa triệt để các lỗi logic, luồng dữ liệu và chức năng còn tồn đọng.

---

## 2. Bảng Phân Công & Nghiệm Thu Hạng Mục Bàn Giao (Definition of Done - DoD)

*(Ghi nhận kết quả thực tế đến Buổi 3 — Ngày 09/11/2026)*

| STT | Nội dung công việc / Bàn giao | Người phụ trách | Kết quả bàn giao thực tế (Đến Buổi 3) | Đánh giá | Ghi chú & Nhận xét tiến độ (Notes) |
| :---: | :--- | :--- | :--- | :---: | :--- |
| **1** | **Soạn thảo & Hoàn thiện Tài liệu SRS:** Viết chi tiết đặc tả 28 yêu cầu chức năng, ma trận phân quyền RBAC, sơ đồ ca sử dụng và các quy tắc nghiệp vụ hệ thống. | Phương Lê, Nam Nguyễn, Phúc Hữu | Phương Lê và Nam Nguyễn đang tích cực triển khai các chương đặc tả phân hệ. Phúc Hữu nộp bài đúng hạn/sớm nhưng nội dung sai khái niệm và sơ đồ vẽ sai lệch, chưa đạt yêu cầu. | *(Để trống)* | **Phúc Hữu nộp bài đúng deadline (thậm chí nộp sớm), nhưng làm việc kém hiệu quả, kết quả hời hợt, không đúng ý nghĩa, sai khái niệm, sơ đồ vẽ sai; không nắm bàn giao và không nắm được sản phẩm bàn giao.** |
| **2** | **Rà soát, Check & Sửa Code Còn Tồn Đọng:** Kiểm tra toàn diện mã nguồn, xử lý các chức năng còn tồn đọng, tối ưu hóa API, rà soát logic khóa chống bán lố, giá vốn WAC và các luồng giao diện POS/Admin. | Lê Võ Đăng Khoa, Phùng Minh Nhựt | Đã rà soát đồng bộ 22 module `OISM.Application`, chuẩn hóa các API endpoint, phân tách độc lập Web POS PWA và Web Admin, khắc phục các điểm nghẽn đồng bộ dữ liệu. | *(Để trống)* | Nhóm kỹ thuật duy trì tiến độ tốt, tiếp tục rà soát kiểm thử hồi quy và xử lý dứt điểm các lỗi phát sinh. |

---

## 3. Báo Cáo Diễn Biến Cuộc Họp Hàng Ngày (Sprint Meetings Summary — Đến Buổi 3)

*(Ghi nhận chi tiết diễn biến các buổi họp đến hôm nay — Buổi 3)*

| Buổi họp | Thời gian | Trọng tâm thảo luận & Quyết định chính | Đánh giá chất lượng | Điểm nghẽn (Blockers) & Cách xử lý | Có mặt |
| :---: | :---: | :--- | :---: | :--- | :---: |
| **Buổi 1** | `08/11/2026 (Sáng)` | **Khởi động Sprint 5 & Phân chia 2 nhánh nhiệm vụ:**<br/>• Nhánh tài liệu: Phương Lê, Nam Nguyễn, Phúc Hữu tập trung viết và hoàn thiện hồ sơ SRS.<br/>• Nhánh kỹ thuật: Khoa và Nhựt tiến hành rà soát, kiểm tra và sửa code các chức năng còn tồn đọng. | *(Để trống)* | Thống nhất cấu trúc các chương mục SRS cần chuẩn hóa và danh mục source code cần rà soát. | Đầy đủ 5/5 |
| **Buổi 2** | `08/11/2026 (Chiều)` | **Cập nhật tiến độ & Đồng bộ kỹ thuật:**<br/>• Khoa & Nhựt báo cáo kết quả rà soát backend Clean Architecture và giao diện Web POS PWA.<br/>• Phương Lê & Nam Nguyễn phân chia chi tiết các phân hệ cần viết trong SRS.<br/>• Phúc Hữu gửi bài sớm nhưng trưởng nhóm nhắc nhở cần chú trọng độ chính xác của khái niệm và sơ đồ. | *(Để trống)* | Nhánh code vận hành trơn tru. Nhánh tài liệu cần rà soát kỹ lưỡng chất lượng nội dung nộp. | Đầy đủ 5/5 |
| **Buổi 3** | `09/11/2026 (Hôm nay)` | **Kiểm tra tiến độ thực tế & Xử lý điểm nghẽn nhân sự:**<br/>• Khoa & Nhựt tiếp tục check và sửa code còn tồn đọng các chức năng còn lại, tối ưu API và cấu trúc gói.<br/>• Phương Lê & Nam Nguyễn tiếp tục hoàn thiện các phần đặc tả yêu cầu.<br/>• **Ghi nhận điểm nghẽn:** Phúc Hữu nộp bài đúng deadline (và nộp sớm) nhưng làm việc kém hiệu quả, kết quả hời hợt, không đúng ý nghĩa, sai khái niệm, sơ đồ vẽ sai; không nắm quy trình bàn giao và không nắm được sản phẩm bàn giao của nhóm. Trưởng nhóm trực tiếp chấn chỉnh và yêu cầu rà soát, làm lại toàn bộ phần sai lệch. | *(Để trống)* | **Blocker phát sinh:** Sản phẩm bàn giao của Phúc Hữu sai lệch khái niệm, sơ đồ vẽ sai, kết quả hời hợt, không có ý nghĩa thực tế.<br/>**Giải pháp:** Yêu cầu Phúc Hữu rà soát lại toàn bộ kiến trúc hệ thống và vẽ lại sơ đồ, chuẩn hóa lại khái niệm ngay trong ngày. | Đầy đủ 5/5 |

---

## 4. Đánh Giá Cải Tiến Quy Trình Scrum (Retrospective — Đến Buổi 3)

### 4.1. 🟢 Những Điều Nhóm Đã Làm Tốt (What Went Well?)
* **Tốc độ xử lý kỹ thuật cao:** Khoa và Nhựt chủ động rà soát sâu mã nguồn, bóc tách và giải quyết nhanh chóng các vấn đề tồn đọng trong backend Clean Architecture và giao diện POS PWA.
* **Tinh thần trách nhiệm của nhánh tài liệu chính:** Phương Lê và Nam Nguyễn nghiêm túc bám sát đề cương, nỗ lực triển khai nội dung đặc tả SRS đúng theo cấu trúc chuẩn.

### 4.2. 🔴 Những Điểm Tồn Tại & Thách Thức (What Went Wrong?)
* **Hiệu suất làm việc và chất lượng bàn giao không đạt yêu cầu:** Thành viên Phúc Hữu dù hoàn thành đúng hạn và nộp sớm deadline nhưng làm việc kém hiệu quả, kết quả hời hợt, không đúng ý nghĩa, sai khái niệm chuyên môn, sơ đồ vẽ sai; không nắm được quy trình bàn giao và sản phẩm bàn giao của dự án.
* Khối lượng công việc tài liệu SRS rất lớn, đòi hỏi sự tập trung, nghiên cứu kỹ mã nguồn và nắm vững kiến trúc hệ thống của toàn bộ thành viên được phân công.

### 4.3. 🚀 Kế Hoạch Hành Động Tiếp Theo (Action Items)
* Trưởng nhóm tăng cường kiểm soát trực tiếp chất lượng đầu ra từng buổi đối với nhánh viết tài liệu SRS, đặc biệt là tính chuẩn xác của các khái niệm nghiệp vụ và sơ đồ thiết kế.
* Yêu cầu Phúc Hữu nghiêm túc chấn chỉnh thái độ làm việc, không làm đối phó/hời hợt, phải chủ động nắm bắt sản phẩm bàn giao để sửa lại các khái niệm và sơ đồ bị sai, không làm ảnh hưởng đến chất lượng chung của toàn đội.

---

## 5. Bảng Đánh Giá Đóng Góp Thành Viên (Contribution Matrix — Đến Buổi 3)

| STT | Thành viên | Nhiệm vụ chính phụ trách | Đánh giá hoàn thành (Đến Buổi 3) | Tinh thần làm việc | Đánh giá của Trưởng nhóm / Ghi chú (Notes) |
| :---: | :--- | :--- | :---: | :--- | :--- |
| **1** | **Lê Võ Đăng Khoa** | Rà soát, check, sửa code còn tồn đọng toàn hệ thống, kiến trúc hệ thống, điều phối | *(Để trống)* | Rất tốt / Chủ động | Bao quát tốt tiến độ kỹ thuật, trực tiếp giải quyết các vấn đề cốt lõi. |
| **2** | **Phùng Minh Nhựt** | Rà soát, check, sửa code tồn đọng backend, chuẩn hóa APIs | *(Để trống)* | Tích cực | Phối hợp nhịp nhàng với trưởng nhóm trong rà soát và kiểm thử mã nguồn. |
| **3** | **Phương Lê** | Viết và hoàn thiện tài liệu SRS | *(Để trống)* | Tích cực | Có tinh thần trách nhiệm, tích cực biên soạn đặc tả các phân hệ nghiệp vụ. |
| **4** | **Nam Nguyễn** | Viết và chuẩn hóa tài liệu SRS, rà soát ma trận yêu cầu | *(Để trống)* | Tích cực | Nỗ lực bám sát tiến độ và đảm bảo chất lượng nội dung được phân công. |
| **5** | **Phúc Hữu** | Viết tài liệu SRS | *(Để trống)* | Đúng hạn nhưng hời hợt / Kém hiệu quả | **Nộp bài đúng deadline và nộp sớm nhưng làm việc kém hiệu quả, kết quả hời hợt, không đúng ý nghĩa, sai khái niệm, sơ đồ vẽ sai; không nắm bàn giao và không nắm được sản phẩm bàn giao.** |

---

## 6. Kết Luận & Kế Hoạch Tiếp Theo

Tính đến thời điểm hiện tại (**hết Buổi 3 — 09/11/2026** của Sprint 5):
* Nhánh kỹ thuật (Khoa, Nhựt) đang kiểm soát tốt tiến độ rà soát và khắc phục các lỗi code tồn đọng.
* Nhánh tài liệu (Phương Lê, Nam Nguyễn, Phúc Hữu) cần tăng tốc và chấn chỉnh triệt để sự phối hợp của Phúc Hữu (yêu cầu vẽ lại sơ đồ và đính chính các khái niệm sai) để đảm bảo hoàn thiện hồ sơ SRS đúng thời hạn nghiệm thu Sprint 5.
