**Dự án:** Omnichannel Inventory & Sales Management System (OISMS)

**Không gian Confluence:** `OISMS` | **Thư mục:** `Report Sprint`

**Mã tài liệu:** `DOC-REP-SPRINT-04` | **Phiên bản:** `v1.0.0`

**Giai đoạn:** **Sprint 4: Hoàn Thiện Nghiệp Vụ, Triển Khai Thực Nghiệm (AWS)**

**Thời gian thực hiện:** `23/10/2026 – 04/11/2026` (D13 – D25)

**Trạng thái nghiệm thu:** **HOÀN THÀNH TOÀN DIỆN (100% DoD) & DEPLOY PRODUCTION DEMO**

**Môi trường Demo Trực Tuyến AWS:** [**https://oisms.khoadang.site**](https://oisms.khoadang.site)

---

1. Mục Tiêu Trọng Tâm Sprint 4
------------------------------

Sprint 4 là giai đoạn bản lề của dự án OISMS nhằm chuyển giao từ hệ thống lõi vận hành sang một nền tảng bán lẻ đa kênh toàn diện, đạt độ tin cậy cấp doanh nghiệp:

* **Hoàn thiện các tính năng còn thiếu (Coming Soon CS-01 → CS-08):** Đưa toàn bộ các phân hệ phụ trợ (RBAC, ReorderPoint, Global Search, Dashboard KPI, VietQR POS, In ấn và Gợi ý nhập hàng AI `FR-AI-01`) vào hoạt động với dữ liệu thật.
* **Hiện thực 5 gói nâng cấp nghiệp vụ chuyên sâu theo góp ý của GVHD:** Mua hàng từ phiếu mua (Upgrade 01), Hóa đơn bán và truy vết xuất kho (Upgrade 02), Lịch kiểm kê định kỳ và duyệt chênh lệch (Upgrade 03), Bảng giá theo thời gian hiệu lực (Upgrade 04), Đa đơn vị tính quy cách (Upgrade 05) và Chuẩn hóa Clean Architecture (Upgrade 06).
* **Khắc phục triệt để lỗi hệ thống & Kiểm thử hồi quy:** Giải quyết toàn bộ 20 lỗi hệ thống (`BF-001` → `BF-020`), đạt 212/212 Backend Unit Tests, 364/365 Integration Tests, 63/63 Frontend Tests.
* **Đóng gói & Triển khai thực nghiệm (Production Deployment) lên AWS:** Đưa hệ thống lên môi trường đám mây AWS EC2 với Docker Compose, cấu hình tên miền chính thức `oisms.khoadang.site` kèm chứng chỉ bảo mật HTTPS/SSL.

---

2. Tình Hình Nhân Sự & Cơ Cấu Phân Công Sprint 4
------------------------------------------------

### 2.1. Bối cảnh điều chỉnh phân công

Trong giai đoạn Sprint 4, do hai thành viên **Phúc Hữu** và **Phương Lê** gặp việc cá nhân và học tập đột xuất không thể thu xếp thời gian tham gia trọn vẹn, đội ngũ nòng cốt gồm **Lê Võ Đăng Khoa**, **Phùng Minh Nhựt** và **Nam Nguyễn** đã chủ động đứng ra đảm nhận chính toàn bộ khối lượng công việc của Sprint nhằm bảo đảm tiến độ dự án.

*Ghi chú cam kết:* Hai thành viên **Phúc Hữu** và **Phương Lê** đã cam kết sẽ **nhận làm bù khối lượng công việc tương ứng ở Sprint sau (Sprint 5)** để cân bằng tổng đóng góp toàn dự án.

### 2.2. Phân công vai trò cụ thể trong Sprint 4

| Nhóm chuyên trách | Thành viên đảm nhận chính | Phạm vi trách nhiệm thực tế |
| --- | --- | --- |
| **Backend Core & Architecture** | **Lê Võ Đăng Khoa** (Lead) **Phùng Minh Nhựt** | • Thiết kế schema 43 bảng PostgreSQL, Clean Architecture (Upgrade 06).  • Hiện thực lõi Upgrade 01-A (Đơn mua & Nhập kho), Upgrade 04 (Bảng giá), Upgrade 05 (Đa đơn vị tính & WAC), Upgrade 02 (Truy vết hóa đơn & sổ cái), Upgrade 03 (Lịch kiểm kê).  • Xây dựng API CS-01 (Trả hàng), CS-02 (RBAC), CS-03 (ReorderPoint), CS-04 (Search), CS-07 (Dashboard KPI), CS-08 (Thuật toán gợi ý nhập hàng AI).  • Fix 20 lỗi hệ thống (`BF-001`..`BF-020`), Hangfire background jobs, NFR & Docker deployment. |
| **Frontend Applications** *(Admin & POS)* | **Lê Võ Đăng Khoa** **Phùng Minh Nhựt** **Nam Nguyễn** | • **Web Admin Dashboard (cổng 5173):** Xây dựng và ghép nối API cho toàn bộ màn hình Đơn mua hàng, Phiếu đổi giá, Lịch kiểm kê, Đa đơn vị tính, Tổ chức nhân viên RBAC, Dashboard KPI, Tìm kiếm toàn hệ thống, In ấn A4.  • **Web POS App (cổng 5174 PWA):** Tích hợp chọn đơn vị tính quy cách, quét mã vạch theo đơn vị, VietQR động (CS-05), in hóa đơn nhiệt 80mm có mã `INV-`, chống bấm đúp Idempotency.  • Fix toàn bộ 19 lỗi giao diện, SignalR realtime re-join branch, lưu trạng thái giỏ hàng khi token hết hạn. |
| **Kiểm thử, DevOps & Hạ tầng AWS** | **Lê Võ Đăng Khoa** **Phùng Minh Nhựt** **Nam Nguyễn** | • Thiết lập CI/CD pipeline, chạy bộ kiểm thử tự động (Unit & Integration tests).  • Cấu hình máy chủ AWS EC2, Docker Compose production, Nginx Reverse Proxy, Let's Encrypt SSL tự động gia hạn.  • Trỏ tên miền `oisms.khoadang.site` và kiểm tra toàn diện kịch bản E2E trên môi trường thực tế. |

---

3. Bảng Nghiệm Thu Hạng Mục Công Việc Chi Tiết (DoD - Definition of Done)
-------------------------------------------------------------------------

### A. Nhóm chức năng còn thiếu (Coming Soon CS-01 → CS-08)

| Mã | Tên chức năng | Chi tiết hiện thực | Kết quả nghiệm thu |
| --- | --- | --- | --- |
| **CS-01** | **Nhận hàng trả từ khách** | Cho phép chọn đơn hàng gốc, kiểm tra số lượng đã bán/đã trả, nhập kho hoàn tồn tự động quy đổi về đơn vị gốc `BaseQuantity` kèm hệ số `FactorSnapshot`. | **PASS** |
| **CS-02** | **Quản trị Nhân viên & RBAC** | Màn hình `OrganizationPage.tsx` hỗ trợ tạo mới, chỉnh sửa thông tin, phân quyền vai trò (Owner, Staff) và khóa tài khoản nhân viên. | **PASS** |
| **CS-03** | **Ngưỡng tồn an toàn trên Server** | Cấu hình `ReorderPoint` được lưu trữ tập trung tại server, đồng bộ chính xác trên Kho, Sản phẩm, Báo cáo và Tổng quan. | **PASS** |
| **CS-04** | **Tìm kiếm toàn hệ thống** | Thanh tìm kiếm Global Search trên Header (`AdminShell.tsx`), tra cứu tức thời SKU, Đơn hàng, Phiếu kho và điều hướng trực tiếp đúng quyền. | **PASS** |
| **CS-05** | **Thanh toán VietQR động POS** | Tích hợp mã VietQR động theo đơn hàng trong `PaymentModal.tsx` trên cả POS Admin và POS PWA, hỗ trợ xác nhận thanh toán an toàn. | **PASS** |
| **CS-06** | **In ấn chuyên nghiệp** | In biên nhận POS khổ 80mm/58mm với mã hóa đơn chuẩn `INV-`, in phiếu giao hàng và báo cáo thống kê khổ A4 chuẩn hóa. | **PASS** |
| **CS-07** | **Dashboard KPI Tổng quan** | Bảng số liệu kinh doanh thật: Doanh thu thuần, Giá vốn, Lợi nhuận gộp, Số lượng đơn, biểu đồ xu hướng 7 ngày và danh sách đơn hàng gần đây. | **PASS** |
| **CS-08** | **AI Gợi ý nhập hàng (FR-AI-01)** | Thuật toán AI phân tích lịch sử bán hàng, tốc độ xuất kho và ngưỡng tồn an toàn để đưa ra khuyến nghị nhập hàng tự động kèm lý do trực quan. | **PASS** |

### B. Nhóm chức năng nâng cấp nghiệp vụ lõi theo gợi ý của GVHD (Upgrade 01 → 06)

| Mã | Gói nâng cấp | Chi tiết kỹ thuật & Nghiệp vụ | Kết quả nghiệm thu |
| --- | --- | --- | --- |
| **U01-A** | **Mua hàng & Nhập kho NCC** | Thêm bảng `purchase_orders`, `purchase_order_items`. Bắt buộc phiếu nhập kho NCC phải gắn với đơn mua đã được phê duyệt; khóa đơn giá và hệ số quy đổi; chặn nhập vượt số lượng (`PURCHASE_ORDER_OVER_RECEIPT`). | **PASS** |
| **U02** | **Hóa đơn & Truy vết xuất kho** | Thêm bảng `sales_invoices`. Liên kết trực tiếp mọi dòng sổ cái kho `OUT` với chứng từ nguồn và hóa đơn bán; hỗ trợ in lại hóa đơn và tra cứu lịch sử xuất bán. | **PASS** |
| **U03** | **Lịch kiểm kê & Duyệt chênh lệch** | Bảng `stock_count_schedules` hỗ trợ lịch định kỳ (ngày 15, cuối tháng, đột xuất); quy trình nộp và phê duyệt chênh lệch 2 bước (`PendingApproval`); chống ghi đè khi kho biến động giữa chừng. | **PASS** |
| **U04** | **Bảng giá theo thời gian hiệu lực** | Bảng `price_lists`, `sku_unit_prices` quản lý hiệu lực theo `valid_from`; quy trình lập & duyệt Phiếu đổi giá (`price_change_requests`); chụp snapshot giá vào đơn và đóng API sửa giá trực tiếp trên SKU. | **PASS** |
| **U05** | **Đa đơn vị tính (Multi-Unit)** | Bảng `units`, `sku_units`. Cho phép cấu hình đơn vị gốc và các đơn vị quy đổi (thùng, hộp, cái); quy đổi `BaseQuantity = Quantity × FactorSnapshot`; tính tồn, giữ chỗ, WAC theo đơn vị gốc; hỗ trợ quét barcode quy cách tại POS. | **PASS** |
| **U06** | **Clean Architecture** | Tách triệt để toàn bộ logic nghiệp vụ từ Controller về `OISM.Application`; cô lập hạ tầng tại `OISM.Infrastructure`; tách `AddApplicationServices` và `AddInfrastructureServices`. | **PASS** |

### C. Khắc phục lỗi hệ thống & Đo kiểm chất lượng (Hardening)

| Tiêu chí đo kiểm | Kết quả thực tế | Trạng thái |
| --- | --- | --- |
| **Fix 20 lỗi hệ thống (**`BF-001` **→** `BF-020`**)** | Hoàn tất 100% (Phiên, SignalR, Idempotency, múi giờ Việt Nam, khóa form). | **PASS** |
| **Backend Unit Tests** | **212 / 212 tests PASS** (100%) | **PASS** |
| **Backend Integration Tests** | **364 / 365 tests PASS** (99.7% — 1 test khác biệt CRLF/LF trên Windows) | **PASS** |
| **Frontend POS Tests** | **63 / 63 tests PASS** (bao gồm test giỏ hàng đa đơn vị tính) | **PASS** |
| **Build Web Admin Dashboard** | `npm run build` thành công, 0 lỗi, 0 warning chặn | **PASS** |
| **Build Web POS App (PWA)** | `npm run build` thành công, 0 lỗi | **PASS** |
| **Kiểm tra độ sẵn sàng hệ thống** | Endpoint `/health/ready` trả về HTTP 200, kiểm tra đủ 43 bảng database | **PASS** |

---

4. Triển Khai Thực Nghiệm Môi Trường AWS (AWS Cloud Deployment)
---------------------------------------------------------------

Hệ thống OISMS đã được đóng gói container hóa và triển khai thành công lên hạ tầng đám mây Amazon Web Services (AWS):

* **Hạ tầng máy chủ:** AWS EC2 Instance (Ubuntu Linux 24.04 LTS).
* **Cơ chế điều phối:** Docker & Docker Compose (`docker-compose.prod.yml`).
* **Hệ sinh thái dịch vụ triển khai:**

  + `oism-postgres`: PostgreSQL 16 chứa đầy đủ 43 bảng nghiệp vụ đã chạy migration chuẩn xác.
  + `oism-redis`: Redis 7 phục vụ bộ nhớ đệm và phân tán khóa.
  + `oism-backend`: .NET 8 Web API Clean Architecture, Hangfire Background Server, SignalR Hub.
  + `oism-admin`: Web Admin Dashboard (Nginx tĩnh phục vụ SPA React 19).
  + `oism-pos`: Web POS PWA (Nginx tĩnh phục vụ Service Worker PWA).
  + `nginx-gateway`: Nginx Reverse Proxy xử lý định tuyến subdomain, websocket SignalR và SSL.
* **Chứng chỉ bảo mật & Tên miền:** Let's Encrypt SSL/TLS tự động gia hạn, giao thức HTTPS 100%.
* **Đường dẫn truy cập chính thức:** [**https://oisms.khoadang.site**](https://oisms.khoadang.site)

---

5. Đánh Giá Cải Tiến Quy Trình Scrum (Sprint 4 Retrospective)
-------------------------------------------------------------

### 5.1. Những điều nhóm đã làm rất tốt (What Went Well?)

* **Khả năng ứng biến & Tinh thần đồng đội cao:** Khi có sự biến động về nhân sự, ba thành viên Khoa, Nhựt và Nam Nguyễn đã lập tức phân bổ lại công việc, gánh vác toàn bộ cả hai nhánh Backend lẫn Frontend mà không làm trễ hạn tiến độ chung.
* **Chất lượng kiến trúc và mã nguồn vượt bậc:** Việc hoàn thiện Clean Architecture (U06), quy trình mua hàng (U01-A), đa đơn vị tính (U05) và bảng giá hiệu lực (U04) nâng tầm hệ thống lên tiêu chuẩn ERP thu nhỏ hoàn chỉnh.
* **Đạt mốc triển khai Production thật:** Không chỉ dừng lại ở môi trường local, việc đưa thành công hệ thống lên AWS với domain thật `oisms.khoadang.site` giúp toàn đội ngũ và GVHD có thể nghiệm thu trực quan mọi lúc mọi nơi.

### 5.2. Những điểm tồn tại & Cần cải thiện (What Went Wrong?)

* **Mức độ phụ thuộc nhân sự trong giai đoạn cao điểm:** Việc thiếu vắng 2 thành viên tạo áp lực làm việc rất lớn lên 3 thành viên còn lại để kịp mốc Feature Freeze.
* **Môi trường phát triển đa nền tảng (Windows vs Linux):** Vấn đề ký tự xuống dòng (CRLF trên Windows vs LF trên Linux) gây sai lệch nhỏ trong một bài kiểm thử tích hợp so sánh thân hàm SQL. Cần chuẩn hóa `.gitattributes` triệt để.

### 5.3. Kế hoạch hành động cho Sprint 5 (Action Items for Sprint 5)

1. **Phúc Hữu và Phương Lê làm bù khối lượng công việc:**

   * Hoàn thiện tài liệu Hướng dẫn sử dụng chi tiết (User Manual cho Admin và Thu ngân POS).
   * Thực hiện kiểm thử chấp nhận người dùng (UAT), kiểm thử tải nặng (Stress test Flash Sale 50 reqs/SKU) và ghi nhận số liệu hoàn thiện Báo cáo nghiệm thu tổng thể đồ án.
   * Hoàn thiện các cải tiến giao diện tinh chỉnh nhỏ và chuẩn bị slide thuyết trình bảo vệ đồ án.
2. **Feature Freeze & Ổn định hệ thống:** Tuyệt đối không thêm tính năng mới trong Sprint 5; tập trung 100% vào đo kiểm chỉ tiêu NFR, tối ưu hiệu năng và đóng gói tài liệu bàn giao.

---

6. Bảng Đánh Giá Đóng Góp Thành Viên Sprint 4 (Contribution Matrix)
-------------------------------------------------------------------

| STT | Thành viên | Vai trò thực tế trong Sprint 4 | Tỷ lệ hoàn thành | Đánh giá của Trưởng nhóm |
| --- | --- | --- | --- | --- |
| **1** | **Lê Võ Đăng Khoa** | • Trưởng nhóm / Backend Lead / DevOps • Chủ trì kiến trúc 5 gói Upgrade, lõi Backend, DevOps AWS, hỗ trợ Frontend | **100%** | Xuất sắc. Quán xuyến toàn bộ tiến độ, kiến trúc lõi, deploy AWS thành công và hoàn thành vượt mức cam kết. |
| **2** | **Phùng Minh Nhựt** | • Backend 02 & Hỗ trợ Frontend • Xây dựng các API CS-01..08, kiểm thử tự động, hỗ trợ màn hình Admin | **100%** | Rất tốt. Chủ động gánh vác khối lượng công việc lớn, code chắc chắn, phối hợp nhịp nhàng. |
| **3** | **Nam Nguyễn** | • Frontend Developer & QA / Deployment • Đảm nhận chính hoàn thiện giao diện Web Admin Dashboard, Web POS, kiểm thử hồi quy | **100%** | Rất tốt. Tích cực nhận thêm phần việc Frontend khi nhóm thiếu người, kiểm thử cẩn thận và đóng góp lớn cho giao diện. |
| **4** | **Phúc Hữu** | • Frontend (Tạm nghỉ có phép) • Bận việc học và gia đình đột xuất | *Làm bù Sprint 5* | Có tinh thần trách nhiệm, đã báo trước với nhóm và cam kết làm bù khối lượng ở Sprint 5. |
| **5** | **Phương Lê** | • Frontend POS (Tạm nghỉ có phép) • Bận việc học và gia đình đột xuất | *Làm bù Sprint 5* | Có tinh thần trách nhiệm, đã báo trước với nhóm và cam kết làm bù khối lượng ở Sprint 5. |

---

7. Kết Luận Nghiệm Thu Sprint 4
-------------------------------

Sprint 4 đã chính thức **HOÀN THÀNH TOÀN BỘ 100% KHỐI LƯỢNG MỤC TIÊU ĐỀ RA**. Toàn bộ các tính năng còn thiếu (Coming Soon), 5 gói nâng cấp nghiệp vụ lõi của GVHD và 20 lỗi hệ thống đã được xử lý triệt để, kiểm thử đạt chuẩn và triển khai thực tế lên đám mây AWS tại [**https://oisms.khoadang.site**](https://oisms.khoadang.site). Nhóm sẵn sàng bước vào Sprint 5 phát triển các chức năng cuối và hoàn thiện đến chương 4 báo cáo thiết kế bắt đầu kiểm thử.