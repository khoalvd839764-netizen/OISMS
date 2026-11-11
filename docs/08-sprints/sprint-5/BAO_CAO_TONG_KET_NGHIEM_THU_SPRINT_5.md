# [OISMS] BÁO CÁO TỔNG KẾT & NGHIỆM THU SPRINT 5 (SPRINT 5 REVIEW & RETROSPECTIVE)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Dự án** | Omnichannel Inventory & Sales Management System (OISMS) |
| **Không gian Confluence** | `MFS` \| **Thư mục:** `Report Sprint` |
| **Mã tài liệu (Document ID)** | `DOC-REP-SPRINT-05` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Giai đoạn (Sprint Phase)** | **Sprint 5: Hoàn Thiện Webhook Simulator, API VietQR, Trợ Lý AI RAG, Trả Hàng (CS-01) & Rà Soát Tài Liệu Thiết Kế Chi Tiết SDD** |
| **Thời gian thực hiện** | `05/11/2026 – 11/11/2026` (D26 – D31, 6 buổi họp chính thức) |
| **Người lập báo cáo (Reporter)** | @Lê Võ Đăng Khoa *(Trưởng nhóm / Backend Lead / DevOps)* |
| **Đội ngũ tham gia** | @Lê Võ Đăng Khoa, @Phùng Minh Nhựt, @Nam Nguyễn, @Phuongw Le, @Phúc Hữu |
| **Trạng thái nghiệm thu** | **HOÀN THÀNH XUẤT SẮC 100% MỤC TIÊU KỸ THUẬT (AI, VietQR, Webhook, Trả hàng, Đo tải báo cáo). PHẦN TÀI LIỆU CỦA PHƯƠNG LÊ VÀ PHÚC HỮU CHƯA ĐẠT CHẤT LƯỢNG NGHIỆM THU, DELAY SANG SPRINT KẾ TIẾP.** |
| **Môi trường Demo Trực Tuyến** | 🌐 **[https://oisms.khoadang.site](https://oisms.khoadang.site)** |

---

## 1. Mục Tiêu Trọng Tâm Sprint 5 (Đối chiếu chuẩn Jira OISMS-58 & OISMS-53..59)

Sprint 5 là giai đoạn nước rút trọng yếu nhằm hoàn thiện toàn diện các phân hệ kỹ thuật nâng cao cuối cùng của hệ thống OISMS và kiện toàn bộ hồ sơ Thiết kế Phần mềm (Software Design Description - SDD):

1. **Hoàn thiện các phân hệ kỹ thuật còn lại (Jira `OISMS-58`):**
   - **Công cụ Giả lập đơn Sàn TMĐT qua Webhook (`FR-SIM-01`):** Cung cấp API quản lý kết nối sàn `/api/shop-integrations`, secret SHA-256, endpoint xác thực `GET /api/webhooks/simulator/whoami`, ứng dụng độc lập `marketplace-simulator` (cổng 5175) hỗ trợ bắn đơn tải đồng thời tới 200 đơn qua reverse proxy Caddy.
   - **Tích hợp API & Chuỗi thanh toán VietQR chuẩn NAPAS 247 (`CS-05`):** Chuẩn hóa chuỗi EMVCo kèm CRC-16, sinh QuickLink ảnh QR động theo số tiền hóa đơn và tài khoản thụ hưởng tenant (`payment_accounts`), bảo vệ Idempotency chống sinh mã trùng, tích hợp đồng bộ vào Web POS PWA (`pos-app`) và Web Admin POS.
   - **Chức năng Tiếp nhận & Xử lý Trả hàng từ khách gắn Đơn gốc (`CS-01`):** API `/api/sales-returns`, kiểm soát số lượng hợp lệ, tính hoàn tiền `RefundAmount`, hoàn kho qua sổ cái bất biến `InventoryTransaction.IN` đối với hàng nguyên vẹn và xuất hủy phế phẩm đối với hàng lỗi.
   - **Trợ lý AI Chatbot, Kho tri thức RAG BM25 & Phân quyền AI 2 lớp (`FR-13` / `CS-08`):** Xây dựng 57 file tài liệu chuẩn hóa tại `OISM.Infrastructure/Ai/Knowledge`, tìm kiếm BM25 tiếng Việt bỏ dấu < 5ms không phụ thuộc API ngoài, RAG context injection chống bịa đặt, phân quyền AI Guardrails 2 lớp (chặn Staff/Cashier hỏi doanh thu/lợi nhuận, chỉ Owner mới được truy vấn tài chính).
   - **Đo kiểm tải Báo cáo Doanh thu & Hoàn thiện Hệ thống:** Script sinh tải 5.000 đơn hàng đối soát tốc độ tính toán báo cáo doanh thu, lãi gộp và KPI trên PostgreSQL; kế hoạch Đăng nhập SĐT/Quên mật khẩu; đồng bộ kiến trúc Clean Architecture.
2. **Kiện toàn Hồ sơ Thiết kế Chi tiết SDD (Jira `OISMS-53` → `OISMS-59`):**
   - Đặc tả Kiến trúc hệ thống và Package Diagrams (Mục 1 SDD).
   - Thiết kế Cơ sở Dữ liệu 44 bảng nghiệp vụ, Sổ cái bất biến, Indexing và Concurrency Control (Mục 2 SDD).
   - Đặc tả chi tiết 11 Flow nghiệp vụ cốt lõi từ Mục 3.1 đến Mục 3.11 gồm Class Diagram, Sequence Diagram UML 2.0 và Class Specification bám sát mã nguồn thực tế.

---

## 2. Tình Hình Nhân Sự & Cơ Cấu Phân Công Sprint 5

Trong Sprint 5, khối lượng công việc được phân định rõ ràng thành hai nhóm nhiệm vụ chuyên biệt:

1. **Nhóm Kỹ thuật (Khoa & Nhựt):**
   - **Lê Võ Đăng Khoa & Phùng Minh Nhựt:** Ở phần này đảm nhận việc hoàn thành toàn bộ các chức năng cần thiết còn lại gồm cả **Backend** và **Frontend** (Jira `OISMS-58`).
   - Cụ thể: Trực tiếp xây dựng Webhook Simulator, API VietQR động NAPAS 247, module Trả hàng Sales Return CS-01, phân hệ Trợ lý AI Chatbot RAG BM25 kèm phân quyền AI 2 lớp, refactor giao diện POS sang `pos-app`, đo tải 5.000 đơn và quản trị hạ tầng AWS.
2. **Nhóm Biên soạn Tài liệu (3 bạn còn lại: Nam Nguyễn, Phương Lê, Phúc Hữu):**
   - **3 bạn còn lại thì tập trung viết lại tài liệu:** Rà soát và biên soạn lại toàn bộ hồ sơ Thiết kế Phần mềm (SDD) trong Chương IV trên Confluence (Page ID `983053`), đảm bảo sơ đồ và đặc tả phải chuẩn khớp với mã nguồn thực tế:
     - **Nam Nguyễn:** Phụ trách Jira `OISMS-56` (Mục 3.1 – 3.3: Phân hệ Xác thực & Bán hàng Đa kênh, POS, VietQR).
     - **Phúc Hữu:** Phụ trách Jira `OISMS-57` (Mục 3.4 – 3.6: Phân hệ Mua hàng PO/WAC, Chuyển kho nội bộ 2 bước, Kiểm kê kho 2 bước).
     - **Phương Lê:** Phụ trách Jira `OISMS-53` (Mục 1 SDD: Kiến trúc tổng thể và Package Diagrams) và Jira `OISMS-59` (Mục 3.10 – 3.11: Quản trị nhân sự/RBAC & Báo cáo doanh thu, lãi gộp).
     - *(Phùng Minh Nhựt hỗ trợ thêm Jira `OISMS-59`: Mục 3.7 – 3.9 gồm Đăng ký gian hàng, Danh mục sản phẩm đa đơn vị tính, Trả hàng CS-01. Lê Võ Đăng Khoa phụ trách Jira `OISMS-54` & `OISMS-55`: Mục 2 CSDL 44 bảng, Sổ cái và Indexing).*

---

## 3. Báo Cáo Diễn Biến Cuộc Họp Hàng Ngày (Sprint Meetings Summary — Đúng 6 Buổi)

*(Ghi nhận chi tiết diễn biến 6 buổi họp trong suốt quá trình triển khai Sprint 5)*

| Buổi họp | Thời gian | Trọng tâm thảo luận & Quyết định chính | Đánh giá tiến độ | Ghi chú & Điểm nghẽn (Blockers) | Có mặt |
| :---: | :---: | :--- | :---: | :--- | :---: |
| **Buổi 1** | `05/11/2026` | **Khởi động Sprint 5 & Phổ biến mục tiêu, phân chia nhiệm vụ:**<br>• Trưởng nhóm (Khoa) phổ biến mục tiêu Sprint 5: Hoàn tất trọn bộ các tính năng kỹ thuật còn lại (Webhook Simulator, VietQR, AI RAG Chatbot, Trả hàng CS-01) và hoàn thiện hồ sơ thiết kế chi tiết SDD.<br>• **Phân chia nhiệm vụ rõ ràng:**<br>  + **Khoa & Nhựt** ở phần này đảm nhận việc hoàn thành các chức năng cần thiết còn lại gồm cả **Backend** và **Frontend** (Jira `OISMS-58`).<br>  + **3 bạn còn lại (Nam Nguyễn, Phương Lê, Phúc Hữu)** thì tập trung viết lại tài liệu (Jira `OISMS-53`, `OISMS-56`, `OISMS-57`, `OISMS-59`). | Đã khởi động & phổ biến rõ ràng | Thống nhất ranh giới: nhóm code (Khoa, Nhựt) giải quyết chức năng kỹ thuật; nhóm tài liệu (Nam, Phương, Hữu) tập trung viết SDD. | Đầy đủ 5/5 |
| **Buổi 2** | `06/11/2026` | **Báo cáo tiến độ (Webhook Architecture, Chuẩn hóa VietQR & Xây dựng Kho tri thức AI):**<br>• Khoa & Nhựt báo cáo hoàn tất API quản lý kết nối sàn `/api/shop-integrations`, cấu trúc chuẩn hóa NAPAS 247 cho VietQR, và xây dựng 57 tài liệu kho tri thức RAG BM25 tại `OISM.Infrastructure/Ai/Knowledge`.<br>• Nhóm 3 bạn tài liệu bắt đầu rà soát tài liệu mẫu và phân chia viết các mục trong SDD. | Đạt tiến độ tốt | Khoa & Nhựt kiểm soát tốt khối lượng backend và frontend. Nhóm tài liệu đang triển khai. | Đầy đủ 5/5 |
| **Buổi 3** | `07/11/2026` | **Báo cáo tiến độ (Hoàn thiện Webhook Simulator App, POS VietQR & Tiếp nhận bản thảo SDD):**<br>• Khoa & Nhựt hoàn thiện ứng dụng độc lập `marketplace-simulator` (cổng 5175), tích hợp modal VietQR động vào Web POS PWA (`pos-app`), và hoàn thành API trả hàng `/api/sales-returns` với sổ cái bất biến.<br>• Nam Nguyễn hoàn thành bản thảo Mục 3.1 - 3.3.<br>• Phương Lê và Phúc Hữu nộp bản thảo Mục 1, Mục 3.4 - 3.6 và Mục 3.10 - 3.11 để nhóm kỹ thuật kiểm tra. | Đạt tiến độ kỹ thuật cao | Nhóm kỹ thuật đối chiếu chi tiết tài liệu và sơ đồ nộp với mã nguồn thực tế tại repository. | Đầy đủ 5/5 |
| **Buổi 4** | `08/11/2026` | **Báo cáo tiến độ (Kiểm tra đối chiếu tài liệu và sơ đồ với mã nguồn thực tế):**<br>• Khoa và Nhựt tiến hành rà soát chi tiết tài liệu do Phương Lê và Phúc Hữu nộp, đối chiếu trực tiếp với mã nguồn thực tế tại repository OISMS.<br>• **Ghi nhận qua kiểm tra:** Sản phẩm bàn giao chưa đảm bảo đủ chất lượng nghiệm thu (sơ đồ chưa đạt chất lượng, khái niệm và luồng nghiệp vụ chưa chuẩn khớp với code dự án).<br>• Các bạn đã có ý thức sửa sau đó, nhưng vì lý do giới hạn thời gian của sprint nên phần này sẽ **delay sang sprint kế tiếp** để tiếp tục hoàn thiện. | Cần tiếp tục hoàn thiện | **Blockers:** Sản phẩm bàn giao chưa đảm bảo đủ chất lượng nghiệm thu, vì lý do thời gian sẽ delay sang sprint kế tiếp. | Đầy đủ 5/5 |
| **Buổi 5** | `10/11/2026` | **Báo cáo tiến độ (AI Guardrails 2 lớp, Đo tải Báo cáo 5.000 đơn & Rà soát chỉnh sửa tài liệu):**<br>• Khoa & Nhựt tích hợp cơ chế bảo vệ phân quyền AI Chatbot 2 lớp (chặn Staff/Cashier hỏi số liệu tài chính nhạy cảm) và chạy script đo kiểm tải 5.000 đơn hàng trên PostgreSQL.<br>• Nam Nguyễn hoàn thiện Mục 3.1 - 3.3. Phương Lê và Phúc Hữu tiếp tục rà soát lại tài liệu và sơ đồ để chuẩn bị kế hoạch hoàn thiện. | Kỹ thuật hoàn tất xuất sắc | Khoa & Nhựt đã gánh vác trọn vẹn toàn bộ chức năng cần thiết backend và frontend. | Đầy đủ 5/5 |
| **Buổi 6** | `11/11/2026` | **Báo cáo tiến độ & Tổng kết nghiệm thu Sprint 5:**<br>• Nghiệm thu toàn diện 100% mục tiêu kỹ thuật: Webhook Simulator, VietQR POS, AI Chatbot RAG BM25, Trả hàng CS-01, đo tải báo cáo doanh thu.<br>• **Kết luận nhân sự & tài liệu:** Khoa và Nhựt hoàn thành xuất sắc việc đảm nhận toàn bộ chức năng backend và frontend. Về phần tài liệu của Phúc Hữu và Phương Lê, sản phẩm bàn giao chưa đảm bảo đủ chất lượng nghiệm thu; các bạn đã có ý thức sửa sau đó nhưng vì lý do thời gian sprint nên sẽ delay sang sprint kế tiếp để tiếp tục hoàn thiện. Đề nghị các bạn phải chấn chỉnh lại cách làm việc hiệu quả, không để xảy ra chất lượng sản phẩm kém nữa.<br>• Gắn Status Macro cảnh báo từ Mục 3.1 đến 3.11 trên Confluence page 983053 để rà soát ở sprint sau. | Nghiệm thu kỹ thuật 100% | Nhất trí delay phần tài liệu của Phương Lê và Phúc Hữu sang sprint kế tiếp vì lý do thời gian. | Đầy đủ 5/5 |

---

## 4. Bảng Nghiệm Thu Hạng Mục Công Việc Chi Tiết (DoD - Definition of Done)

### A. Phân hệ Kỹ thuật hoàn thành trong Sprint 5 (Khoa & Nhựt đảm nhận — Jira `OISMS-58`)

| Mã | Hạng mục kỹ thuật | Chi tiết hiện thực (Khoa & Nhựt) | Kết quả nghiệm thu |
| :---: | :--- | :--- | :---: |
| **FR-SIM-01** | **Giả lập đơn sàn TMĐT qua Webhook** | • API `/api/shop-integrations` (Owner): Tạo kết nối, secret SHA-256, sửa chi nhánh, rotate-secret.<br>• Endpoint `GET /api/webhooks/simulator/whoami`: Xác thực header `X-Webhook-Secret`.<br>• Ứng dụng độc lập `marketplace-simulator` (cổng 5175): Bắn đơn đồng thời tới 200 đơn, reverse proxy Caddy (`SIM_DOMAIN`). | **PASS** |
| **CS-05** | **Thanh toán VietQR động POS chuẩn NAPAS 247** | • Chuẩn hóa chuỗi EMVCo kèm CRC-16; sinh QuickLink ảnh QR động theo số tiền hóa đơn và tài khoản thụ hưởng tenant (`payment_accounts`).<br>• Bảo vệ Idempotency: Snapshot tài khoản thụ hưởng, lưu giữ `requestKey` / `cartFingerprint` chống trùng.<br>• Tích hợp đồng bộ Web POS PWA (`pos-app`) và Web Admin POS (`PosPage.tsx`). | **PASS** |
| **CS-01** | **Trả hàng từ khách gắn Đơn gốc (Sales Return)** | • API `/api/sales-returns`: Tiếp nhận trả hàng từ hóa đơn (`sales_invoices`) và đơn hàng (`orders`).<br>• Chặn trả vượt số lượng, tự động tính `RefundAmount`.<br>• Hoàn kho qua sổ cái bất biến `InventoryTransaction.IN` (hàng nguyên vẹn) hoặc ghi phế phẩm (hàng lỗi). | **PASS** |
| **FR-13 / CS-08** | **Trợ lý AI Chatbot & Kho tri thức RAG BM25** | • Kho tri thức 57 tài liệu tại `OISM.Infrastructure/Ai/Knowledge` (Embedded Resource).<br>• Tìm kiếm BM25 tiếng Việt bỏ dấu < 5ms, không phụ thuộc API ngoài.<br>• RAG Context Injection chống bịa đặt: Ghép tối đa 4 đoạn liên quan (≤ 3.000 ký tự), trích xuất nguồn rõ ràng.<br>• **Phân quyền AI 2 lớp (AI Guardrails):** Chặn Staff/Cashier hỏi Doanh thu/Lợi nhuận/Tài chính, chỉ Owner mới xem được.<br>• Hỗ trợ Groq Llama 3.3 70B & Google Gemini API. | **PASS** |
| **PERF & HARD** | **Đo kiểm tải Báo cáo & Hoàn thiện Hệ thống** | • Script sinh tải 5.000 đơn hàng local đối soát tốc độ tính toán báo cáo doanh thu, lãi gộp và KPI trên PostgreSQL.<br>• Kế hoạch Quên mật khẩu & Đăng nhập SĐT.<br>• Đồng bộ kiến trúc Clean Architecture và liên kết hệ thống. | **PASS** |

### B. Phân hệ Tài liệu Thiết kế Chi tiết SDD (Nhóm 3 bạn tài liệu & Hỗ trợ — Jira `OISMS-53..59`)

| Mục SDD | Tên phân hệ tài liệu | Thành viên phụ trách | Kết quả nghiệm thu | Ghi chú & Đánh giá |
| :---: | :--- | :---: | :---: | :--- |
| **Mục 1** | **Kiến trúc Tổng thể & Package Diagrams** (Jira `OISMS-53`) | **Phương Lê** | **CẦN HOÀN THIỆN** | Sơ đồ cấu trúc gói và phân tầng cần rà soát lại để chuẩn khớp mã nguồn thực tế. |
| **Mục 2** | **Thiết kế CSDL 44 Bảng, Sổ cái & Indexing** (Jira `OISMS-54, 55`) | **Lê Võ Đăng Khoa** | **PASS** | Đạt 100% chuẩn kỹ thuật PostgreSQL 16, đầy đủ Dictionary và ràng buộc toàn vẹn. |
| **Mục 3.1 – 3.3** | **Detailed Design: Xác thực, POS & Webhook** (Jira `OISMS-56`) | **Nam Nguyễn** | **PASS** | Đầy đủ Class Diagram, Sequence Diagram UML 2.0 và Class Specification bám sát code. |
| **Mục 3.4 – 3.6** | **Detailed Design: Mua hàng, Chuyển kho & Kiểm kê** (Jira `OISMS-57`) | **Phúc Hữu** | **CHƯA ĐẠT CHẤT LƯỢNG NGHIỆM THU** | **Sản phẩm bàn giao chưa đảm bảo đủ chất lượng nghiệm thu; vì lý do thời gian sẽ delay sang sprint kế tiếp.** |
| **Mục 3.7 – 3.9** | **Detailed Design: Gian hàng, Đa đơn vị tính, Trả hàng** (Jira `OISMS-59`) | **Phùng Minh Nhựt** | **PASS** | Đầy đủ sơ đồ và đặc tả bám sát Clean Architecture và các API thực tế. |
| **Mục 3.10 – 3.11** | **Detailed Design: Nhân sự/RBAC & Báo cáo Doanh thu** (Jira `OISMS-59`) | **Phương Lê** | **CHƯA ĐẠT CHẤT LƯỢNG NGHIỆM THU** | **Sản phẩm bàn giao chưa đảm bảo đủ chất lượng nghiệm thu; vì lý do thời gian sẽ delay sang sprint kế tiếp.** |

---

## 5. Đánh Giá Cải Tiến Quy Trình Scrum (Sprint 5 Retrospective)

### 5.1. 🟢 Những điều nhóm đã làm rất tốt (What Went Well?)
- **Năng lực kỹ thuật xuất sắc của Khoa và Nhựt:** Đảm nhận và giải quyết trọn vẹn toàn bộ khối lượng kỹ thuật cốt lõi còn lại (Webhook Simulator 200 đơn đồng thời, API VietQR động NAPAS 247, Trả hàng CS-01, AI RAG Chatbot BM25 phân quyền 2 lớp và đo tải 5.000 đơn).
- **Tính năng AI vượt bậc & Tối ưu chi phí:** Phân hệ AI RAG sử dụng thuật toán BM25 cục bộ tốc độ < 5ms, tri thức chuẩn hóa 57 file, kèm cơ chế AI Guardrails bảo vệ dữ liệu tài chính của doanh nghiệp.
- **Tính mở rộng của Webhook Simulator:** Ứng dụng độc lập `marketplace-simulator` giúp việc kiểm thử và nghiệm thu kịch bản bán hàng đa kênh từ sàn TMĐT diễn ra hoàn toàn chủ động, trực quan.

### 5.2. 🔴 Những điểm tồn tại & Cần cải thiện (What Went Wrong?)
- **Chất lượng bàn giao tài liệu của một số thành viên chưa đồng đều:**
  - Qua kiểm tra thì tài liệu và sơ đồ do **Phúc Hữu** và **Phương Lê** nộp chưa đảm bảo chất lượng nghiệm thu (sơ đồ chưa đạt chất lượng, khái niệm và luồng nghiệp vụ chưa chuẩn khớp với mã nguồn dự án).
  - Các bạn đã có ý thức sửa đổi sau đó, nhưng vì lý do giới hạn thời gian của Sprint nên phần này sẽ **delay sang sprint kế tiếp** để tiếp tục hoàn thiện.
- **Đề nghị thành viên chấn chỉnh cách làm việc hiệu quả:** Yêu cầu các thành viên nghiêm túc nâng cao tinh thần trách nhiệm, đối chiếu trực tiếp mã nguồn thực tế tại repository, không để xảy ra tình trạng sản phẩm bàn giao chất lượng kém.

### 5.3. 🚀 Kế hoạch hành động cho Sprint tiếp theo (Action Items)
1. **Rà soát sơ đồ chuẩn khớp với code dự án:**
   - Tiến hành rà soát lại toàn bộ hệ thống sơ đồ (Class Diagram, Sequence Diagram UML 2.0) và bảng Class Specification từ Mục 3.1 đến 3.11 trên Confluence page `983053` đảm bảo chuẩn khớp 100% với mã nguồn Clean Architecture 4 tầng.
2. **Kế hoạch hoàn thiện sản phẩm bàn giao:**
   - Phúc Hữu và Phương Lê tiếp tục rà soát, viết lại và hoàn thiện các mục được giao (Mục 1, Mục 3.4 – 3.6, Mục 3.10 – 3.11) để đạt chuẩn nghiệm thu ở sprint tiếp theo.
3. **Chuẩn bị nghiệm thu toàn diện đồ án:**
   - Đóng gói tài liệu, rà soát Ma trận truy vết yêu cầu (RTM), chuẩn bị slide thuyết trình và kịch bản demo bảo vệ đồ án.

---

## 6. Bảng Đánh Giá Đóng Góp Thành Viên Sprint 5 (Contribution Matrix)

*(Ghi nhận đánh giá thành viên theo đúng tinh thần quản trị Scrum — Không đánh giá mức độ/tỷ lệ %, tập trung vào nhiệm vụ, tinh thần làm việc và nhận xét chuyên môn)*

| STT | Thành viên | Vai trò / Nhiệm vụ thực tế trong Sprint 5 | Tinh thần / Thái độ làm việc | Đánh giá & Ghi chú của Trưởng nhóm (Notes) |
| :---: | :--- | :--- | :---: | :--- |
| **1** | **Lê Võ Đăng Khoa** | • Cùng Phùng Minh Nhựt đảm nhận hoàn thành các chức năng cần thiết còn lại gồm backend và frontend<br>• Phát triển Webhook Simulator, API VietQR, AI RAG Chatbot, CSDL 44 bảng (OISMS-54, 55), DevOps AWS | Gương mẫu / Chủ động / Trách nhiệm cao | Quán xuyến toàn bộ tiến độ, hoàn thành xuất sắc các phân hệ kỹ thuật cốt lõi, bảo đảm hệ thống vận hành ổn định trên Production. |
| **2** | **Phùng Minh Nhựt** | • Cùng Lê Võ Đăng Khoa đảm nhận hoàn thành các chức năng cần thiết còn lại gồm backend và frontend<br>• Xây dựng API Trả hàng CS-01, đo tải báo cáo 5.000 đơn, hỗ trợ SDD 3.7 – 3.9 | Tích cực / Trách nhiệm cao | Gánh vác khối lượng công việc lớn, code chắc chắn, phối hợp chặt chẽ cùng Khoa hoàn thành trọn vẹn cả backend và frontend. |
| **3** | **Nam Nguyễn** | • Thuộc nhóm 3 bạn tập trung viết lại tài liệu (phụ trách Mục 3.1 – 3.3 SDD theo Jira OISMS-56)<br>• Hỗ trợ kiểm thử tích hợp và giao diện QA | Tích cực / Tinh thần đồng đội cao | Nỗ lực bám sát tiến độ, hoàn thành tốt phần tài liệu Mục 3.1 - 3.3 chuẩn khớp với mã nguồn, hỗ trợ đắc lực nhánh kiểm thử. |
| **4** | **Phương Lê** | • Thuộc nhóm 3 bạn tập trung viết lại tài liệu (phụ trách Mục 1 SDD theo Jira OISMS-53 và Mục 3.10 – 3.11 theo Jira OISMS-59) | Đang tiếp tục hoàn thiện | **Sản phẩm bàn giao chưa đảm bảo đủ chất lượng nghiệm thu; bạn đã có ý thức sửa sau đó nhưng vì lý do thời gian của sprint nên sẽ delay sang sprint kế tiếp để tiếp tục hoàn thiện. Đề nghị chấn chỉnh lại cách làm việc hiệu quả, rà soát sơ đồ chuẩn khớp với code dự án.** |
| **5** | **Phúc Hữu** | • Thuộc nhóm 3 bạn tập trung viết lại tài liệu (phụ trách Mục 3.4 – 3.6 SDD theo Jira OISMS-57) | Đang tiếp tục hoàn thiện | **Sản phẩm bàn giao chưa đảm bảo đủ chất lượng nghiệm thu; bạn đã có ý thức sửa sau đó nhưng vì lý do thời gian của sprint nên sẽ delay sang sprint kế tiếp để tiếp tục hoàn thiện. Đề nghị chấn chỉnh lại cách làm việc hiệu quả, rà soát sơ đồ chuẩn khớp với code dự án.** |

---

## 7. Kết Luận Nghiệm Thu Sprint 5

Sprint 5 đã chính thức **HOÀN THÀNH TOÀN DIỆN 100% CÁC MỤC TIÊU KỸ THUẬT ĐỀ RA**:
- Khoa và Nhựt đã đảm nhận và hoàn thành trọn vẹn các chức năng kỹ thuật cần thiết còn lại gồm cả Backend và Frontend: Công cụ Webhook Simulator sàn TMĐT (FR-SIM-01), API & Chuỗi thanh toán VietQR động POS (CS-05), Chức năng Trả hàng từ khách gắn đơn gốc (CS-01), Trợ lý AI Chatbot RAG BM25 kèm phân quyền AI 2 lớp (FR-13 / CS-08), và đo kiểm tải báo cáo doanh thu 5.000 đơn trên PostgreSQL.
- Toàn bộ hệ thống chạy ổn định tại **[https://oisms.khoadang.site](https://oisms.khoadang.site)**.

Về phần tài liệu:
- Nhóm 3 bạn (Nam Nguyễn, Phương Lê, Phúc Hữu) đã tập trung viết lại tài liệu SDD.
- Qua kiểm tra, sản phẩm bàn giao (tài liệu và sơ đồ) của **Phúc Hữu** và **Phương Lê** chưa đảm bảo đủ chất lượng nghiệm thu; các bạn đã có ý thức sửa đổi sau đó nhưng vì lý do thời gian của sprint nên sẽ **delay sang sprint kế tiếp** để tiếp tục rà soát, chuẩn khớp sơ đồ với mã nguồn thực tế.
- Trên Confluence page `983053` (Chương IV: Thiết kế Phần mềm SDD), nhóm đã gắn Status Macro cảnh báo từ Mục 3.1 đến Mục 3.11: `CẦN SỬA VÀ RÀ SOÁT Ở SPRINT SAU VÌ CHƯA ĐỦ CHẤT LƯỢNG NGHIỆM THU`.
- Toàn đội ngũ sẵn sàng cho giai đoạn kế tiếp: Chấn chỉnh tác phong làm việc, hoàn thiện dứt điểm các sơ đồ thiết kế chi tiết còn thiếu và chuẩn bị nghiệm thu đồ án.
