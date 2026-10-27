# BỘ PHAO VẤN ĐÁP CỐT LÕI: PHẦN 00, 01, 02 (DỰ ÁN OISMS)

> **Dành cho:** Nhựt Phùng Minh (TV2 - Backend 2 Lead)  
> **Mục đích:** Ôn tập nhanh bản chất hệ thống trước buổi kiểm tra của nhóm và giảng viên.

---

## PHẦN 00: TỔNG QUAN & NGUYÊN TẮC CỐT LÕI (CORE CONCEPTS)

### Câu 1: Hệ thống OISMS là gì và giải quyết bài toán gì?
- **Trả lời:** OISMS là hệ thống quản lý bán hàng và tồn kho đa kênh (Omnichannel) theo mô hình SaaS (cho nhiều chủ shop cùng thuê). Nó giúp chủ shop bán hàng cùng lúc ở **Quầy POS** và trên **sàn TMĐT (Shopee, TikTok, Lazada)** mà không bị lệch tồn kho và tính được chính xác lãi/lỗ.

### Câu 2: Ba nỗi đau lớn nhất của chủ shop mà OISMS giải quyết là gì?
- **Trả lời:**
  1. **Bán vượt tồn kho (Stock Overselling):** Hết hàng trên thực tế nhưng trên Shopee/TikTok vẫn nổ đơn $\rightarrow$ bị sàn phạt tiền và hủy shop.
  2. **Sai lệch giá vốn hàng bán (Inaccurate COGS):** Nhập nhiều đợt với giá khác nhau, tính nhẩm bằng tay dẫn đến tính sai tiền lãi thực tế.
  3. **Thất thoát & mất dấu kho:** Nhân viên tự ý vào sửa số lượng tồn trong phần mềm mà không để lại dấu vết kiểm toán (Audit Trail).

### Câu 3: Sáu cơ chế sống còn của hệ thống là gì? *(Trọng tâm)*
1. **Sổ cái kho bất biến (Append-Only Inventory Ledger):** Cấm tuyệt đối lệnh `UPDATE` hoặc `DELETE` số tồn kho cũ. Mọi biến động nhập/xuất đều phải thêm 1 dòng mới như sao kê ngân hàng để chống gian lận.
2. **Công thức tồn khả dụng:**
   $$\text{Available (Tồn khả dụng)} = \text{On-Hand (Tồn kho thực tế)} - \text{Reserved (Đang giữ chỗ cho khách)}$$
3. **Giá vốn bình quân gia quyền (WAC):** Cứ mỗi lần nhập hàng mới là tự động tính lại giá vốn trung bình, và khi xuất bán đơn nào thì "chụp lại" (snapshot) giá vốn đơn đó để tính lãi gộp chính xác.
4. **Máy trạng thái đơn hàng (Order State Machine):** Đi tuần tự `Draft/Pending` $\rightarrow$ `Reserved` $\rightarrow$ `Confirmed` $\rightarrow$ `Completed` (hoặc `Cancelled`), cấm nhảy cóc.
5. **Bảo mật đa khách thuê (Multi-Tenancy):** Dữ liệu của các shop cô lập hoàn toàn bằng `TenantId` qua EF Core Global Query Filter, shop này không bao giờ xem được dữ liệu của shop kia.
6. **Mã vạch chuẩn quốc tế EAN-13:** Mỗi biến thể (màu, size) có mã vạch 13 số riêng (tính check digit theo Modulo 10) để máy quét quầy POS nhận diện dưới 0.2 giây.

---

## PHẦN 01: NGƯỜI DÙNG & PHÂN QUYỀN (USERS & RBAC)

### Câu 4: Hệ thống có những ai tham gia và quyền hạn ra sao?
- Có **3 con người** và **2 tác nhân tự động**:
  - **1. Chủ shop (`Owner`):** Toàn quyền hệ thống, xem được mọi báo cáo mật (Doanh thu, Giá vốn, Lợi nhuận gộp), tạo chi nhánh và quản lý nhân viên.
  - **2. Nhân viên kho (`Staff`):** Nhập hàng, kiểm kho, chuyển kho, duyệt đơn giao hàng. **CẤM xem Báo cáo Lợi nhuận gộp và Doanh thu** (bí mật kinh doanh của chủ).
  - **3. Thu ngân (`Cashier`):** Chỉ được dùng giao diện bán hàng POS tại quầy, quét mã vạch và in bill. **BỊ CẤM vào trang quản trị Web Admin, cấm sửa giá bán, cấm xem giá vốn**.
  - **4. Webhook Simulator:** Đóng vai Shopee, TikTok để tự động bắn đơn hàng online về.
  - **5. Hangfire Worker:** Robot chạy ngầm tự hủy các đơn giữ chỗ quá 15 phút không trả tiền và tổng kết báo cáo doanh thu lúc nửa đêm.

### Câu 5: Điểm quan trọng nhất về phân quyền cần lưu ý là gì?
- **Trả lời:** Phân quyền ở cả 2 đầu: **Frontend** (Route Guard ẩn menu, chuyển hướng về `/403`) và **Backend** (Attributes `[Authorize(Roles = "...")]` trả về lỗi HTTP `403 Forbidden` nếu Staff hoặc Cashier cố tình gọi API lấy báo cáo lợi nhuận).

---

## PHẦN 02: 12 PHÂN HỆ NGHIỆP VỤ THỰC TẾ

1. **FR-01 (Auth & Multi-tenant):** Đăng ký shop, cấp `TenantId`, đăng nhập JWT Token Rotation.
2. **FR-02 (Catalog & Barcode EAN-13):** Danh mục đa tầng, biến thể SKU, sinh mã vạch EAN-13.
3. **FR-03 (Inventory Ledger):** Sổ cái kho Append-Only, nhập kho, kiểm kho, chuyển kho 2 bước.
4. **FR-04 (Cost WAC):** Tự động tính giá vốn bình quân gia quyền và chốt snapshot vào đơn bán.
5. **FR-05 (Stock Reservation):** Cơ chế giữ chỗ tồn kho và khóa CSDL chống bán vượt khi Flash Sale.
6. **FR-06 (Omnichannel Orders):** Chuẩn hóa đơn về Canonical Order Model và Order State Machine.
7. **FR-07 (POS Checkout):** Giao diện bán lẻ POS PWA cảm ứng, quét mã nhanh và in bill nhiệt.
8. **FR-08 (Marketplace Sync):** Webhook Simulator giả lập nhận đơn tự động từ Shopee/TikTok/Lazada.
9. **FR-09 (SignalR Realtime):** Chuông reo và popup thông báo đơn hàng mới đổ về tức thời.
10. **FR-10 (Hangfire Background):** Tác vụ nền dọn dẹp đơn giữ chỗ quá hạn và tổng hợp báo cáo ngày.
11. **FR-11 (Reports & Analytics):** Báo cáo Doanh thu, Lợi nhuận gộp, Vận tốc kho và Cảnh báo tồn thấp.
12. **FR-12 (AI Forecasting):** Dự báo nhu cầu bán hàng và gợi ý điểm đặt hàng lại (Reorder Point).

---

## PHẦN TỰ TIN TRẢ LỜI CỦA BẠN (BACKEND 2 - NHỰT)

Khi Khoa hoặc Giảng viên hỏi: *"Nhựt phụ trách những gì và kết quả thế nào?"* $\rightarrow$ **Trả lời:**

> *"Em phụ trách **Backend 2**, hoàn thành 3 phân hệ lớn trong Sprint 3:
> 1. **Đơn hàng đa kênh (FR-06):** Toàn bộ API CRUD, bộ lọc theo kênh/chi nhánh/trạng thái và máy trạng thái đơn hàng (Order State Machine).
> 2. **Báo cáo Tài chính & Tồn kho (FR-11):** API Doanh thu thuần, Giá vốn COGS, Lợi nhuận gộp (`Gross Profit = Revenue - COGS`), Tốc độ luân chuyển kho và Cảnh báo hàng tồn thấp. Phân quyền chặt chẽ chỉ `Owner` mới được xem lợi nhuận.
> 3. **Trình giả lập Webhook sàn TMĐT (FR-08):** API giả lập bắn đơn từ Shopee, TikTok Shop về hệ thống (đơn lẻ và hàng loạt).
> 
> Toàn bộ mã nguồn đạt **114/114 Unit Tests pass 100%**, **24/24 Integration Tests pass**, đã tạo nhánh `feature/OISMS-42-sprint-3-backend-2` và mở **Pull Request #20** trên GitHub sẵn sàng để merge!"*
