# 01. Ma Trận Truy Vết Yêu Cầu Toàn Diện (Requirements Traceability Matrix - RTM)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-QA-01` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` (Trang 1 – 5) & Confluence Page ID `5275650` (Version 9) |
| **Phạm vi áp dụng** | Truy vết từ Yêu cầu Đề bài $\leftrightarrow$ Thành viên phụ trách $\leftrightarrow$ Mã nguồn/Test $\leftrightarrow$ Giai đoạn Sprint |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả nội dung & Chi tiết rà soát thay đổi (Change Check) | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | • Thiết lập ma trận RTM đối chiếu toàn bộ FR/NFR với thành phần mã nguồn và bài kiểm thử.<br/>• **Check thay đổi khi đối chiếu Confluence (`Page ID: 5275650`):** Bổ sung Bảng đối chiếu tổng thể theo Mục I của Confluence, cập nhật chính xác cột Thành viên phụ trách và điều chỉnh giai đoạn thực hiện (`FR-SIM-01..03` bắt đầu từ Sprint 3, `FR-AI-01` thực hiện ở Sprint 4, Sprint 5 dành cho Regression Test & Hồ sơ). | `INIT-SYNC-QA-01` |

---

## 1. Ma Trận Đối Chiếu Tổng Thể Theo Confluence (Mục I — Page `5275650`)

| Mã yêu cầu | Tên nội dung yêu cầu | Thành viên phụ trách (Confluence) | Giai đoạn thực hiện (Confluence) | Trạng thái sau Sprint 2 |
| :---: | :--- | :--- | :--- | :---: |
| **`FR-AUTH (01-04)`** | Đăng ký cửa hàng, Đăng nhập bảo mật, Phân quyền RBAC và Quản lý chi nhánh | **Nhựt & Đăng Khoa** | **Sprint 2** *(bổ sung Sprint 3)* | **Đã hoàn thành & Pass 100% Test** |
| **`FR-PROD (01-04)`** | Danh mục, Thương hiệu, Sản phẩm, Biến thể SKU, Sinh mã vạch và Bảng giá | **Nhựt, Phúc Hữu & Nam Nguyễn** | **Sprint 2** *(bổ sung Sprint 3)* | **Đã hoàn thành & Pass 100% Test** |
| **`FR-INV (01-02)` & `FR-COST-01`** | Sổ cái kho bất biến chỉ thêm, Nhập kho NCC và Tự động tính giá vốn bình quân WAC | **Đăng Khoa, Phúc Hữu & Nam Nguyễn** | **Sprint 2** | **Đã hoàn thành & Pass 100% Test** |
| **`FR-INV (03-04)`** | Chuyển kho nội bộ 2 bước (xuất gửi $\rightarrow$ nhận) và Kiểm kê cân bằng kho | **Đăng Khoa, Phúc Hữu & Nam Nguyễn** | **Sprint 3** (`18/10 – 22/10`) | Đã có bảng DB, triển khai ở Sprint 3 |
| **`FR-COST-02` & `FR-ORD (01-03)`** | Chốt giá vốn vào đơn, Tiếp nhận đơn đa kênh Canonical Model, Máy trạng thái và Duyệt đơn | **Đăng Khoa, Nhựt, Phúc Hữu & Nam Nguyễn** | **Sprint 2 (nền), Sprint 3 (đủ)** | Đã xong nền DB, hoàn thiện ở Sprint 3 |
| **`FR-RSE (01-03)`** | Công thức tồn khả dụng động, Động cơ giữ chỗ và Khóa dữ liệu chống bán vượt | **Đăng Khoa (Trưởng nhóm)** | **Sprint 2 (nền), Sprint 3 (đủ)** | Đã xong API Reserve + Lock + Idempotency |
| **`FR-POS (01-04)`** | Giao diện bán nhanh cảm ứng, Quét mã vạch, Thanh toán giỏ nguyên tử và In hóa đơn | **Phương Lê & Đăng Khoa** | **Sprint 2 (nền), Sprint 3 (đủ)** | Đã xong UI POS, Search/Barcode, Print |
| **`FR-SIM (01-03)`** | Trình giả lập Webhook 3 sàn, Đẩy tin SignalR thời gian thực và Tác vụ nền Hangfire | **Đăng Khoa & Nhựt** *(FE nối ở Sprint 3)* | **Sprint 2, 3 & 4** *(Sprint 3: Simulator, SignalR, Hangfire expire; Sprint 4: Hangfire daily report)* | Triển khai trọng tâm ở Sprint 3 & 4 |
| **`FR-REP (01-03)`** | Báo cáo doanh thu thuần, giá vốn, lãi gộp; Tốc độ luân chuyển kho và Cảnh báo tồn thấp | **Nhựt, Phúc Hữu, Nam Nguyễn & Đăng Khoa** | **Sprint 3 (cơ bản), Sprint 4 (đủ)** | Triển khai ở Sprint 3 & 4 |
| **`AI` (Gói 5 theo đề)** | Dự báo nhu cầu và gợi ý nhập hàng tự động (`Stock Reorder Recommendations`) | **Đăng Khoa & Nhựt** *(Phúc Hữu & Nam Nguyễn làm UI)* | **Sprint 3 (chốt), Sprint 4 (chạy)** | Chốt ở Sprint 3, chạy thật ở Sprint 4 |
| **`NFR-PERF, TENANT, SEC, MAINT`** | Thử tải 50 đơn đồng thời Flash Sale, Cô lập đa người thuê, Bảo mật JWT/BCrypt, $\ge 80\%$ coverage | **Đăng Khoa & Cả nhóm cùng kiểm test** | **Sprint 2 – 3 (xây), Sprint 4 – 5 (đo & hồi quy)** | Đã đạt `194/194 Tests Pass`, Coverage `> 92%` |
