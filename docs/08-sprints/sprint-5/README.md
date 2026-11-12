# KẾ HOẠCH & BÁO CÁO TỔNG KẾT NGHIỆM THU SPRINT 5 (05/11/2026 – 11/11/2026 | D26 – D31)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Dự án** | Omnichannel Inventory & Sales Management System (OISMS) |
| **Không gian Confluence** | `MFS` \| **Thư mục:** `Report Sprint` |
| **Mã tài liệu (Document ID)** | `DOC-REP-SPRINT-05` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Nguồn truy vết gốc** | Confluence Page ID `19824685` (`[Sprint 5] Báo Cáo Tổng Kết & Nghiệm Thu Sprint 5`) & Jira Sprint 5 (`OISMS-58`, `OISMS-53..59`) |
| **Chủ đề Sprint 5** | **Hoàn Thiện Webhook Simulator, API VietQR, Trợ Lý AI RAG, Trả Hàng (CS-01) & Rà Soát Tài Liệu Thiết Kế Chi Tiết SDD** |
| **Thời gian thực hiện** | `05/11/2026 – 11/11/2026` (D26 – D31, 6 buổi họp chính thức) |
| **Trạng thái nghiệm thu** | **HOÀN THÀNH XUẤT SẮC 100% MỤC TIÊU KỸ THUẬT (AI, VietQR, Webhook, Trả hàng, Đo tải báo cáo). PHẦN TÀI LIỆU CỦA PHƯƠNG LÊ VÀ PHÚC HỮU CHƯA ĐẠT CHẤT LƯỢNG NGHIỆM THU, DELAY SANG SPRINT KẾ TIẾP.** |
| **Môi trường Demo Trực Tuyến** | 🌐 **[https://oisms.khoadang.site](https://oisms.khoadang.site)** |

---

> [!NOTE]
> Báo cáo tổng kết nghiệm thu chi tiết Sprint 5 đã được đồng bộ 100% lên Confluence tại trang:  
> 🔗 **[Sprint 5 Báo Cáo Tổng Kết & Nghiệm Thu Sprint 5 (Confluence Page ID: 19824685)](https://khoalevodang301007.atlassian.net/wiki/spaces/MFS/pages/19824685)**  
> Xem chi tiết tại file: [`BAO_CAO_TONG_KET_NGHIEM_THU_SPRINT_5.md`](./BAO_CAO_TONG_KET_NGHIEM_THU_SPRINT_5.md).

---

## 1. Mục Tiêu Trọng Tâm Sprint 5 (Đối chiếu chuẩn Jira OISMS-58 & OISMS-53..59)

1. **Phát triển & hoàn thiện trọn bộ các tính năng kỹ thuật còn lại (Khoa & Nhựt đảm nhận — Jira `OISMS-58`):**
   - **Công cụ Giả lập đơn Sàn TMĐT qua Webhook (`FR-SIM-01`):** Cung cấp API quản lý kết nối sàn `/api/shop-integrations`, secret SHA-256, endpoint xác thực `GET /api/webhooks/simulator/whoami`, ứng dụng độc lập `marketplace-simulator` (cổng 5175) hỗ trợ bắn đơn tải đồng thời tới 200 đơn qua reverse proxy Caddy.
   - **Tích hợp API & Chuỗi thanh toán VietQR chuẩn NAPAS 247 (`CS-05`):** Chuẩn hóa chuỗi EMVCo kèm CRC-16, sinh QuickLink ảnh QR động theo số tiền hóa đơn và tài khoản thụ hưởng tenant (`payment_accounts`), bảo vệ Idempotency chống sinh mã trùng, tích hợp trong ứng dụng `pos-app`. Admin `/pos` chuyển hướng sang ứng dụng này.
   - **Chức năng Tiếp nhận & Xử lý Trả hàng từ khách gắn Đơn gốc (`CS-01`):** API `/api/sales-returns`, kiểm soát số lượng hợp lệ, tính hoàn tiền `RefundAmount`, hoàn kho qua sổ cái bất biến `InventoryTransaction.IN` đối với hàng nguyên vẹn và xuất hủy phế phẩm đối với hàng lỗi.
   - **Trợ lý AI Chatbot, Kho tri thức RAG BM25 & Phân quyền AI 2 lớp (`FR-AI-02`):** Xây dựng 57 file tài liệu chuẩn hóa tại `OISM.Infrastructure/Ai/Knowledge`, tìm kiếm BM25 tiếng Việt bỏ dấu < 5ms không phụ thuộc API ngoài, RAG context injection chống bịa đặt, phân quyền AI Guardrails 2 lớp (chặn Staff/Cashier hỏi doanh thu/lợi nhuận, chỉ Owner mới được truy vấn tài chính).
   - **Đo kiểm tải Báo cáo Doanh thu & Hoàn thiện Hệ thống:** Script sinh tải 5.000 đơn hàng đối soát tốc độ tính toán báo cáo doanh thu, lãi gộp và KPI trên PostgreSQL; kế hoạch Đăng nhập SĐT/Quên mật khẩu; đồng bộ kiến trúc Clean Architecture.
2. **Kiện toàn Hồ sơ Thiết kế Chi tiết SDD (3 bạn còn lại tập trung viết lại tài liệu — Jira `OISMS-53` → `OISMS-59`):**
   - Đặc tả Kiến trúc hệ thống và Package Diagrams (Mục 1 SDD).
   - Thiết kế Cơ sở Dữ liệu 44 bảng nghiệp vụ, Sổ cái bất biến, Indexing và Concurrency Control (Mục 2 SDD).
   - Đặc tả chi tiết 11 Flow nghiệp vụ cốt lõi từ Mục 3.1 đến Mục 3.11 gồm Class Diagram, Sequence Diagram UML 2.0 và Class Specification bám sát mã nguồn thực tế.

---

## 2. Tình Hình Nhân Sự & Cơ Cấu Phân Công Sprint 5

- **Nhóm Kỹ thuật (Khoa & Nhựt):** Ở phần này đảm nhận việc hoàn thành toàn bộ các chức năng cần thiết còn lại gồm cả **Backend** và **Frontend** (Jira `OISMS-58`).
- **Nhóm Biên soạn Tài liệu (3 bạn còn lại: Nam Nguyễn, Phương Lê, Phúc Hữu):** Tập trung viết lại tài liệu thiết kế chi tiết SDD (Mục 1 SDD và Mục 3.1 - 3.11) bám sát mã nguồn thực tế:
  - **Nam Nguyễn:** Phụ trách Jira `OISMS-56` (Mục 3.1 – 3.3).
  - **Phúc Hữu:** Phụ trách Jira `OISMS-57` (Mục 3.4 – 3.6).
  - **Phương Lê:** Phụ trách Jira `OISMS-53` (Mục 1 SDD) và Jira `OISMS-59` (Mục 3.10 – 3.11).
  - *(Phùng Minh Nhựt hỗ trợ thêm Jira `OISMS-59`: Mục 3.7 – 3.9. Lê Võ Đăng Khoa phụ trách Jira `OISMS-54` & `OISMS-55`: Mục 2 CSDL 44 bảng).*

---

## 3. Báo Cáo Diễn Biến Cuộc Họp Hàng Ngày (Sprint Meetings Summary — Đúng 6 Buổi)

| Buổi họp | Thời gian | Trọng tâm thảo luận & Quyết định chính | Đánh giá tiến độ | Ghi chú & Điểm nghẽn (Blockers) | Có mặt |
| :---: | :---: | :--- | :---: | :--- | :---: |
| **Buổi 1** | `05/11/2026` | **Khởi động Sprint 5 & Phổ biến mục tiêu, phân chia nhiệm vụ:**<br>• Trưởng nhóm (Khoa) phổ biến mục tiêu Sprint 5.<br>• Phân chia: Khoa & Nhựt ở phần này đảm nhận hoàn thành các chức năng cần thiết còn lại gồm backend và frontend. 3 bạn còn lại (Nam, Phương, Hữu) tập trung viết lại tài liệu. | Đã khởi động & phổ biến rõ ràng | Thống nhất ranh giới trách nhiệm rõ ràng giữa nhóm code và nhóm tài liệu. | Đầy đủ 5/5 |
| **Buổi 2** | `06/11/2026` | **Báo cáo tiến độ (Webhook Architecture, Chuẩn hóa VietQR & Xây dựng Kho tri thức AI):**<br>• Khoa & Nhựt hoàn tất API `/api/shop-integrations`, chuẩn NAPAS 247 VietQR, 57 file kho tri thức RAG BM25.<br>• Nhóm 3 bạn tài liệu bắt đầu rà soát tài liệu mẫu và phân chia viết các mục trong SDD. | Đạt tiến độ tốt | Nhóm kỹ thuật kiểm soát tốt tiến độ. Nhóm tài liệu đang triển khai. | Đầy đủ 5/5 |
| **Buổi 3** | `07/11/2026` | **Báo cáo tiến độ (Hoàn thiện Webhook Simulator App, POS VietQR & Tiếp nhận bản thảo SDD):**<br>• Khoa & Nhựt hoàn thiện app simulator 5175, modal VietQR POS, API trả hàng `/api/sales-returns`.<br>• Nam Nguyễn hoàn thành bản thảo 3.1 - 3.3. Phương Lê và Phúc Hữu nộp bản thảo Mục 1, 3.4 - 3.6 và 3.10 - 3.11. | Đạt tiến độ kỹ thuật cao | Nhóm kỹ thuật đối chiếu chi tiết tài liệu và sơ đồ nộp với mã nguồn repo OISMS. | Đầy đủ 5/5 |
| **Buổi 4** | `08/11/2026` | **Báo cáo tiến độ (Kiểm tra đối chiếu tài liệu và sơ đồ với mã nguồn thực tế):**<br>• Khoa và Nhựt rà soát chi tiết tài liệu do Phương Lê và Phúc Hữu nộp, đối chiếu với code repo OISMS.<br>• **Ghi nhận qua kiểm tra:** Sản phẩm bàn giao chưa đảm bảo đủ chất lượng nghiệm thu (sơ đồ chưa đạt chất lượng, khái niệm và luồng nghiệp vụ chưa chuẩn khớp với code dự án). Các bạn đã có ý thức sửa sau đó, nhưng vì lý do thời gian sprint nên phần này sẽ **delay sang sprint kế tiếp** để tiếp tục hoàn thiện. | Cần tiếp tục hoàn thiện | **Blockers:** Sản phẩm bàn giao chưa đảm bảo đủ chất lượng nghiệm thu, vì lý do thời gian sẽ delay sang sprint kế tiếp. | Đầy đủ 5/5 |
| **Buổi 5** | `10/11/2026` | **Báo cáo tiến độ (AI Guardrails 2 lớp, Đo tải Báo cáo 5.000 đơn & Rà soát chỉnh sửa tài liệu):**<br>• Khoa & Nhựt tích hợp AI Guardrails 2 lớp (chặn Staff xem tài chính), đo tải 5.000 đơn trên PostgreSQL.<br>• Nam Nguyễn hoàn thiện 3.1 - 3.3. Phương Lê và Phúc Hữu tiếp tục rà soát lại tài liệu và sơ đồ. | Kỹ thuật hoàn tất xuất sắc | Khoa & Nhựt đã gánh vác trọn vẹn toàn bộ chức năng cần thiết backend và frontend. | Đầy đủ 5/5 |
| **Buổi 6** | `11/11/2026` | **Báo cáo tiến độ & Tổng kết nghiệm thu Sprint 5:**<br>• Nghiệm thu 100% mục tiêu kỹ thuật: Webhook Simulator, VietQR POS, AI Chatbot RAG BM25, Trả hàng CS-01, đo tải báo cáo.<br>• **Kết luận nhân sự & tài liệu:** Khoa và Nhựt hoàn thành xuất sắc chức năng backend và frontend. Về phần tài liệu của Phúc Hữu và Phương Lê, sản phẩm bàn giao chưa đảm bảo đủ chất lượng nghiệm thu; các bạn đã có ý thức sửa sau đó nhưng vì lý do thời gian sprint nên sẽ delay sang sprint kế tiếp để tiếp tục hoàn thiện. Đề nghị chấn chỉnh lại cách làm việc hiệu quả, không để xảy ra chất lượng sản phẩm kém nữa.<br>• Gắn Status Macro cảnh báo từ Mục 3.1 đến 3.11 trên Confluence page 983053 để rà soát ở sprint sau. | Nghiệm thu kỹ thuật 100% | Nhất trí delay phần tài liệu của Phương Lê và Phúc Hữu sang sprint kế tiếp vì lý do thời gian. | Đầy đủ 5/5 |

---

## 4. Bảng Đánh Giá Đóng Góp Thành Viên Sprint 5 (Contribution Matrix)

*(Ghi nhận đánh giá thành viên theo đúng tinh thần quản trị Scrum — Không đánh giá mức độ/tỷ lệ %, tập trung vào nhiệm vụ, tinh thần làm việc và nhận xét chuyên môn)*

| STT | Thành viên | Vai trò / Nhiệm vụ thực tế trong Sprint 5 | Tinh thần / Thái độ làm việc | Đánh giá & Ghi chú của Trưởng nhóm (Notes) |
| :---: | :--- | :--- | :---: | :--- |
| **1** | **Lê Võ Đăng Khoa** | • Cùng Phùng Minh Nhựt đảm nhận hoàn thành các chức năng cần thiết còn lại gồm backend và frontend<br>• Phát triển Webhook Simulator, API VietQR, AI RAG Chatbot, CSDL 44 bảng (OISMS-54, 55), DevOps AWS | Gương mẫu / Chủ động / Trách nhiệm cao | Quán xuyến toàn bộ tiến độ, hoàn thành xuất sắc các phân hệ kỹ thuật cốt lõi, bảo đảm hệ thống vận hành ổn định trên Production. |
| **2** | **Phùng Minh Nhựt** | • Cùng Lê Võ Đăng Khoa đảm nhận hoàn thành các chức năng cần thiết còn lại gồm backend và frontend<br>• Xây dựng API Trả hàng CS-01, đo tải báo cáo 5.000 đơn, hỗ trợ SDD 3.7 – 3.9 | Tích cực / Trách nhiệm cao | Gánh vác khối lượng công việc lớn, code chắc chắn, phối hợp chặt chẽ cùng Khoa hoàn thành trọn vẹn cả backend và frontend. |
| **3** | **Nam Nguyễn** | • Thuộc nhóm 3 bạn tập trung viết lại tài liệu (phụ trách Mục 3.1 – 3.3 SDD theo Jira OISMS-56)<br>• Hỗ trợ kiểm thử tích hợp và giao diện QA | Tích cực / Tinh thần đồng đội cao | Nỗ lực bám sát tiến độ, hoàn thành tốt phần tài liệu Mục 3.1 - 3.3 chuẩn khớp với mã nguồn, hỗ trợ đắc lực nhánh kiểm thử. |
| **4** | **Phương Lê** | • Thuộc nhóm 3 bạn tập trung viết lại tài liệu (phụ trách Mục 1 SDD theo Jira OISMS-53 và Mục 3.10 – 3.11 theo Jira OISMS-59) | Đang tiếp tục hoàn thiện | **Sản phẩm bàn giao chưa đảm bảo đủ chất lượng nghiệm thu; bạn đã có ý thức sửa sau đó nhưng vì lý do thời gian của sprint nên sẽ delay sang sprint kế tiếp để tiếp tục hoàn thiện. Đề nghị chấn chỉnh lại cách làm việc hiệu quả, rà soát sơ đồ chuẩn khớp với code dự án.** |
| **5** | **Phúc Hữu** | • Thuộc nhóm 3 bạn tập trung viết lại tài liệu (phụ trách Mục 3.4 – 3.6 SDD theo Jira OISMS-57) | Đang tiếp tục hoàn thiện | **Sản phẩm bàn giao chưa đảm bảo đủ chất lượng nghiệm thu; bạn đã có ý thức sửa sau đó nhưng vì lý do thời gian của sprint nên sẽ delay sang sprint kế tiếp để tiếp tục hoàn thiện. Đề nghị chấn chỉnh lại cách làm việc hiệu quả, rà soát sơ đồ chuẩn khớp với code dự án.** |

---

## 5. Kết Luận Nghiệm Thu Sprint 5

Sprint 5 **HOÀN THÀNH TOÀN DIỆN 100% CÁC MỤC TIÊU KỸ THUẬT ĐỀ RA**:
- Khoa và Nhựt đã đảm nhận và hoàn thành trọn vẹn các chức năng kỹ thuật cần thiết còn lại gồm cả Backend và Frontend: Công cụ Webhook Simulator sàn TMĐT (FR-SIM-01), API & Chuỗi thanh toán VietQR động POS (CS-05), Chức năng Trả hàng từ khách gắn đơn gốc (CS-01), Trợ lý AI Chatbot RAG BM25 kèm phân quyền AI 2 lớp (FR-AI-02), và đo kiểm tải báo cáo doanh thu 5.000 đơn trên PostgreSQL. Toàn bộ hệ thống chạy ổn định tại [https://oisms.khoadang.site](https://oisms.khoadang.site).
- Về phần tài liệu: Nhóm 3 bạn tập trung viết lại tài liệu SDD; qua kiểm tra, sản phẩm bàn giao (tài liệu và sơ đồ) của **Phúc Hữu** và **Phương Lê** chưa đảm bảo đủ chất lượng nghiệm thu; các bạn đã có ý thức sửa đổi sau đó nhưng vì lý do thời gian của sprint nên sẽ **delay sang sprint kế tiếp** để tiếp tục rà soát, chuẩn khớp sơ đồ với mã nguồn thực tế.
- Trên Confluence page `983053` (Chương IV SDD), nhóm đã gắn Status Macro cảnh báo từ Mục 3.1 đến Mục 3.11: `CẦN SỬA VÀ RÀ SOÁT Ở SPRINT SAU VÌ CHƯA ĐỦ CHẤT LƯỢNG NGHIỆM THU`.
