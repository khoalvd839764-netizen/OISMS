# [Sprint 2 - DOC] 3.1.3 Ma trận phân quyền truy cập màn hình (Screen Authorization Matrix)

> **Dự án:** OISMS — Omnichannel Inventory & Sales Management System (Hệ thống Quản lý Bán hàng & Tồn kho Đa kênh)  
> **Học phần:** Đồ án Chuyên ngành / Công nghệ Phần mềm  
> **Thành viên phụ trách:** Nhựt Phùng Minh (Backend 2 - Auth, Catalog, POS & Warehouse API Lead)  
> **Nền tảng xuất bản:** Confluence / GitHub Documentation  
> **Cơ sở đối chiếu:** Đối chiếu chéo 2 chiều giữa **Mã nguồn thực tế** (`src/frontend/admin-dashboard`, `src/frontend/pos-app`) và **Đề bài chính thức** (`docs/DE_BAI_OISM.md`)  
> **Trạng thái:** Sẵn sàng nghiệm thu Sprint 2  

---

## 3.1.3 Ma trận phân quyền truy cập màn hình (Screen Authorization Matrix)

### 1. Giới thiệu & Định nghĩa 4 Vai trò Phân quyền (Actors / Roles)

Hệ thống OISMS áp dụng mô hình phân quyền theo vai trò (**Role-Based Access Control - RBAC**) kết hợp với kiến trúc cô lập dữ liệu đa người thuê (**Multi-tenant Isolation**). Theo đúng đặc tả tại đề bài `FR-AUTH-03` và triển khai thực tế trong mã nguồn:

1. **Chủ cửa hàng (`Owner`):** 
   - Tài khoản quản trị cao nhất của Cửa hàng (được tạo tự động khi Đăng ký Cửa hàng - `FR-AUTH-01`).
   - Nắm toàn quyền (`Full Access`) trên toàn bộ hệ thống: Quản lý chi nhánh và kho hàng (`FR-AUTH-04`), phân quyền nhân viên (`FR-AUTH-03`), quản lý sản phẩm, đơn hàng, và là vai trò **DUY NHẤT** được xem Báo cáo Tài chính / Doanh thu thuần / Lợi nhuận gộp (`FR-REP-01`, `FR-REP-02`).
2. **Nhân viên Quản lý Kho & Vận hành (`Staff`):**
   - Phụ trách quản lý danh mục hàng hóa, biến thể SKU (`FR-PROD`), sổ cái kho bất biến, nhập xuất tồn kho (`FR-INV`, `FR-COST`) và duyệt đơn hàng đa kênh (`FR-ORD`).
   - **Bị hạn chế tuyệt đối** khỏi các khu vực quản trị tổ chức và tài chính: Không được truy cập trang Báo cáo (`/reports`) và trang Quản trị Tổ chức / Chi nhánh / Nhân viên (`/organization`). Nếu cố tình truy cập, Route Guard của Frontend sẽ chuyển hướng về `/403` và Backend trả mã `403 Forbidden`.
3. **Thu ngân Quầy bán lẻ (`Cashier`):**
   - Chuyên trách vận hành ứng dụng **Web POS** (`/pos` hoặc chạy độc lập trên cổng 5174): Tra cứu hàng hóa siêu tốc, quét mã vạch laser F2, tạo giỏ hàng, thanh toán F4 và in hóa đơn bán lẻ (`FR-POS-01 -> FR-POS-04`).
   - **Bị khóa hoàn toàn** quyền truy cập vào giao diện quản trị **Admin Dashboard** (`/admin`, `/products`, `/inventory`, `/orders`, `/reports`, `/organization`). Khi đăng nhập với vai trò Cashier, hệ thống tự động đưa về `/pos`; nếu cố tình vào Admin sẽ bị chặn ngay tại màn hình `/403`.
4. **Khách chưa xác thực (`Guest / Public`):**
   - Người dùng chưa đăng nhập hệ thống, chỉ được phép truy cập màn hình Đăng nhập / Đăng ký Cửa hàng (`/login`) và các màn hình thông báo lỗi hệ thống (`/403`, `/session-expired`). Mọi nỗ lực truy cập vào các trang nội bộ đều bị chuyển hướng về `/login`.

---

### 2. Bảng Ma trận Phân quyền Màn hình & Chức năng Giao diện (Screen Authorization Matrix)

* **Quy ước ký hiệu:**
  * `✓` : Được phép truy cập và thao tác.
  * `-` : Bị chặn truy cập (Frontend ẩn menu điều hướng / chuyển hướng sang `/403`, Backend chặn với mã `403 Forbidden`).
  * `Hành vi (Actions)`: `C` (Create - Tạo), `R` (Read - Xem/Tra cứu), `U` (Update - Sửa/Bật tắt), `D` (Delete - Xóa), `S` (Special - Thao tác đặc biệt: Duyệt/Khóa/Thanh toán/In bill).
  * `Hiện trạng`: 🟢 **Đã hiện thực S2** (Đã chạy live trên giao diện) | 🟡 **Kế hoạch S3 - S5** (Yêu cầu bắt buộc trong Đề bài `DE_BAI_OISM.md`, đã có schema CSDL và triển khai theo lộ trình).

| STT | Phân hệ (Module) | Tên Màn hình / Thành phần Giao diện | Route URL / Vị trí trên giao diện | Thành phần Code Frontend | Mã yêu cầu trong Đề bài | Hiện trạng | Hành vi | Chủ shop (Owner) | Nhân viên kho (Staff) | Thu ngân (Cashier) | Khách (Guest) |
| :---: | :--- | :--- | :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **I** | **XÁC THỰC & ĐIỀU HƯỚNG** | | | | | | | | | | |
| 1 | Xác thực | Màn hình Đăng nhập (Login Page) | `/login` (mode login) | `AuthPage.tsx` | FR-AUTH-02 | 🟢 Đã hiện thực S2 | R, S | `✓` | `✓` | `✓` | `✓` |
| 2 | Xác thực | Màn hình Đăng ký Doanh nghiệp (Tenant Register) | `/login` (mode register) | `AuthPage.tsx` | FR-AUTH-01 | 🟢 Đã hiện thực S2 | C, S | `-` | `-` | `-` | `✓` |
| 3 | Điều hướng lỗi | Màn hình 403 Bị từ chối truy cập (Access Denied) | `/403` | `AccessPage.tsx` | FR-AUTH-03 | 🟢 Đã hiện thực S2 | R | `✓` | `✓` | `✓` | `✓` |
| 4 | Điều hướng lỗi | Màn hình Hết phiên làm việc (Session Expired) | `/session-expired` | `AccessPage.tsx` | NFR-SEC-01 | 🟢 Đã hiện thực S2 | R | `✓` | `✓` | `✓` | `✓` |
| **II** | **TỔNG QUAN & BÁO CÁO** | | | | | | | | | | |
| 5 | Bảng điều khiển | Bảng điều khiển Tổng quan (Admin Dashboard / KPI) | `/admin` hoặc `/` | `DashboardPage.tsx` | FR-REP-03 | 🟢 Đã hiện thực S2 | R | `✓` | `✓` | `-` | `-` |
| 6 | Báo cáo tài chính | Báo cáo Doanh thu & Lợi nhuận gộp (P&L Analytics) | `/reports` hoặc `/admin/reports` | `ReportsPage.tsx` | FR-REP-01 | 🟢 Đã hiện thực S2 | R, S | `✓` | `-` | `-` | `-` |
| 7 | Báo cáo tài chính | Báo cáo Tồn kho theo Giá vốn WAC & Cảnh báo an toàn | `/reports` hoặc `/admin/reports` | `ReportsPage.tsx` | FR-REP-02 | 🟢 Đã hiện thực S2 | R, S | `✓` | `-` | `-` | `-` |
| 8 | Báo cáo AI | Màn hình Dự báo AI Gợi ý Nhập lại Hàng (Replenishment) | `/reports/ai` hoặc Tab trên Reports | `ReportsPage.tsx` (mục AI) | Đề bài trang 6, Gói 5 | 🟡 Kế hoạch S4-5 | R, S | `✓` | `-` | `-` | `-` |
| **III**| **DANH MỤC & SẢN PHẨM** | | | | | | | | | | |
| 9 | Sản phẩm | Danh sách Sản phẩm & Biến thể SKU | `/products` hoặc `/admin/products` | `ProductsPage.tsx` | FR-PROD-02 | 🟢 Đã hiện thực S2 | R | `✓` | `✓` | `-` | `-` |
| 10 | Sản phẩm | Hộp thoại Thêm mới Sản phẩm & Biến thể SKU | Modal trên `/products` | `ProductsPage.tsx` (productModal) | FR-PROD-02 | 🟢 Đã hiện thực S2 | C, S | `✓` | `✓` | `-` | `-` |
| 11 | Danh mục | Hộp thoại Quản lý Cây danh mục sản phẩm | Modal trên `/products` | `ProductsPage.tsx` (categoryModal) | FR-PROD-01 | 🟢 Đã hiện thực S2 | C, R, U, D | `✓` | `✓` | `-` | `-` |
| 12 | Thương hiệu | Hộp thoại Quản lý Thương hiệu sản phẩm | Modal trên `/products` | `ProductsPage.tsx` (brandModal) | FR-PROD-01 | 🟢 Đã hiện thực S2 | C, R, U, D | `✓` | `✓` | `-` | `-` |
| 13 | Bảng giá | Hộp thoại Cập nhật Giá bán lẻ & Giá bán sỉ SKU | Modal trên `/products` | `ProductsPage.tsx` (editingSku) | FR-PROD-04 | 🟢 Đã hiện thực S2 | R, U | `✓` | `✓` | `-` | `-` |
| 14 | Mã vạch | Hộp thoại Sinh mã & In mã vạch EAN-13/Code128 | Modal trên `/products` | `ProductsPage.tsx` (barcodeModal)| FR-PROD-03 | 🟢 Đã hiện thực S2 | R, S | `✓` | `✓` | `-` | `-` |
| **IV** | **KHO BÃI & NHẬP XUẤT** | | | | | | | | | | |
| 15 | Quản lý tồn | Màn hình Số dư Tồn kho Khả dụng (Available Balance) | `/inventory` (Tab Tồn kho) | `InventoryPage.tsx` (tab=balance) | FR-RSE-01 | 🟢 Đã hiện thực S2 | R | `✓` | `✓` | `-` | `-` |
| 16 | Sổ cái kho | Màn hình Sổ cái Kho Bất biến (Append-only Ledger) | `/inventory` (Tab Sổ cái) | `InventoryPage.tsx` (tab=ledger) | FR-INV-01 | 🟢 Đã hiện thực S2 | R | `✓` | `✓` | `-` | `-` |
| 17 | Nhập kho NCC | Hộp thoại Lập Phiếu nhập kho & Chốt giá vốn WAC | Modal trên `/inventory` | `InventoryPage.tsx` (receiptModal) | FR-INV-02, COST-01 | 🟢 Đã hiện thực S2 | C, S | `✓` | `✓` | `-` | `-` |
| 18 | Giữ chỗ tồn kho| Hộp thoại Khóa giữ chỗ Tồn kho (Reservation Engine) | Modal trên `/inventory` | `InventoryPage.tsx` (reserveModal) | FR-RSE-02, RSE-03 | 🟢 Đã hiện thực S2 | C, S | `✓` | `✓` | `-` | `-` |
| 19 | Cảnh báo tồn | Hộp thoại Cấu hình Ngưỡng tồn an toàn tối thiểu | Modal trên `/inventory` | `InventoryPage.tsx` (thresholdOpen) | FR-REP-03 | 🟢 Đã hiện thực S2 | R, U | `✓` | `✓` | `-` | `-` |
| 20 | Trả hàng NCC | Hộp thoại Lập Phiếu xuất trả hàng Nhà cung cấp | Modal trên `/inventory` | `InventoryPage.tsx` (returnOpen) | FR-INV-01 | 🟢 Đã hiện thực S2 | C, S | `✓` | `✓` | `-` | `-` |
| 21 | Chuyển kho | Màn hình Phiếu Chuyển kho nội bộ 2 bước | `/inventory/transfers` hoặc Tab Chuyển kho | `InventoryPage.tsx` (chuyển kho) | FR-INV-03 | 🟡 Kế hoạch S3 | C, R, S | `✓` | `✓` | `-` | `-` |
| 22 | Kiểm kê kho | Màn hình Phiên Kiểm kê & Cân bằng kho | `/inventory/counts` hoặc Tab Kiểm kê | `InventoryPage.tsx` (kiểm kê) | FR-INV-04 | 🟡 Kế hoạch S3 | C, R, S | `✓` | `✓` | `-` | `-` |
| **V**  | **ĐƠN HÀNG ĐA KÊNH** | | | | | | | | | | |
| 23 | Đơn hàng | Danh sách Đơn hàng Đa kênh (Canonical Model) | `/orders` hoặc `/admin/orders` | `OrdersPage.tsx` | FR-ORD-01 | 🟢 Đã hiện thực S2 | R | `✓` | `✓` | `-` | `-` |
| 24 | Đơn hàng | Hộp thoại Tạo Đơn hàng mới & Giữ chỗ tồn kho tức thì | Modal trên `/orders` | `OrdersPage.tsx` (createModal) | FR-ORD-01, RSE-02 | 🟢 Đã hiện thực S2 | C, S | `✓` | `✓` | `-` | `-` |
| 25 | Chi tiết đơn | Chi tiết Đơn hàng & Quản lý Máy trạng thái đơn | Khu vực xem trên `/orders` | `OrdersPage.tsx` (selected) | FR-ORD-02 | 🟢 Đã hiện thực S2 | R, U, S | `✓` | `✓` | `-` | `-` |
| 26 | Giao hàng | Hộp thoại Duyệt đơn & In Phiếu Giao hàng | Modal / Action trên `/orders` | `OrdersPage.tsx` (printDelivery) | FR-ORD-03 | 🟢 Đã hiện thực S2 | S (In phiếu) | `✓` | `✓` | `-` | `-` |
| 27 | Sàn giả lập | Màn hình Giả lập Webhook Sàn TMĐT (Shopee/TikTok) | `/orders/simulator` hoặc Modal Giả lập | `OrdersPage.tsx` (simulator) | FR-SIM-01 | 🟢 Đã hiện thực S2 | C, S (Giả lập) | `✓` | `✓` | `-` | `-` |
| **VI** | **QUẢN TRỊ TỔ CHỨC** | | | | | | | | | | |
| 28 | Chi nhánh & Kho| Quản lý Chi nhánh Cửa hàng & Kho hàng trực thuộc | `/organization` (Tab Chi nhánh) | `OrganizationPage.tsx` (tab=branches) | FR-AUTH-04 | 🟢 Đã hiện thực S2 | C, R, U | `✓` | `-` | `-` | `-` |
| 29 | Chi nhánh | Hộp thoại Thêm mới Chi nhánh Cửa hàng | Modal trên `/organization` | `OrganizationPage.tsx` (open=true) | FR-AUTH-04 | 🟢 Đã hiện thực S2 | C | `✓` | `-` | `-` | `-` |
| 30 | Kho hàng | Hộp thoại Thêm mới Kho hàng gắn với Chi nhánh | Modal trên `/organization` | `OrganizationPage.tsx` (warehouseModal)| FR-AUTH-04 | 🟢 Đã hiện thực S2 | C | `✓` | `-` | `-` | `-` |
| 31 | Nhân sự & RBAC | Quản lý Danh sách Nhân viên & Phân quyền Vai trò | `/organization` (Tab Nhân sự) | `OrganizationPage.tsx` (tab=people) | FR-AUTH-03 | 🟢 Đã hiện thực S2 | R, U | `✓` | `-` | `-` | `-` |
| 32 | Nhân sự | Hộp thoại Tạo tài khoản Nhân viên (Staff / Cashier) | Modal trên `/organization` | `OrganizationPage.tsx` (createStaff) | FR-AUTH-03 | 🟢 Đã hiện thực S2 | C | `✓` | `-` | `-` | `-` |
| **VII**| **QUẦY BÁN HÀNG POS** | | | | | | | | | | |
| 33 | Quầy POS | Giao diện Bán hàng Cảm ứng POS (Touch Workspace) | `/pos` (port 5174 hoặc `/pos`) | `pos-app/App.tsx` & `PosPage.tsx` | FR-POS-01 | 🟢 Đã hiện thực S2 | R, C, S | `✓` | `✓` | `✓` | `-` |
| 34 | Quét mã vạch | Thanh Tra cứu Sản phẩm & Quét mã vạch laser (F2) | Phím tắt F2 trên Quầy POS | `pos-app/components/ProductSearch.tsx` | FR-POS-01, NFR-01 | 🟢 Đã hiện thực S2 | R (<200ms) | `✓` | `✓` | `✓` | `-` |
| 35 | Giỏ hàng | Bảng Giỏ hàng & Kiểm tra Tồn khả dụng Realtime | Khu vực trung tâm Quầy POS | `pos-app/components/CartTable.tsx` | FR-POS-02 | 🟢 Đã hiện thực S2 | C, R, U, D | `✓` | `✓` | `✓` | `-` |
| 36 | Thanh toán POS | Hộp thoại Thanh toán Tiền mặt & QR Chuyển khoản (F4) | Phím tắt F4 trên Quầy POS | `pos-app/components/PaymentModal.tsx` | FR-POS-03, POS-04 | 🟢 Đã hiện thực S2 | S (Thanh toán) | `✓` | `✓` | `✓` | `-` |
| 37 | In hóa đơn | Hộp thoại Xem trước & In Hóa đơn bán lẻ | Sau khi thanh toán POS | `pos-app/components/ReceiptPrintModal.tsx`| FR-POS-03 | 🟢 Đã hiện thực S2 | R, S (In bill) | `✓` | `✓` | `✓` | `-` |
| 38 | Xác thực POS | Hộp thoại Chuyển đổi Tài khoản Thu ngân tại Quầy | Modal trên Quầy POS | `pos-app/components/LoginModal.tsx` | FR-AUTH-02 | 🟢 Đã hiện thực S2 | S (Đổi ca) | `✓` | `✓` | `✓` | `✓` |
| **VIII**| **HỆ THỐNG & HANDOFF**| | | | | | | | | | |
| 39 | Nghiệm thu | Màn hình Handoff Kỹ thuật & Báo cáo Tích hợp S2 | `/handoff` hoặc `/admin/handoff` | `HandoffPage.tsx` | NFR-MAINT-02 | 🟢 Đã hiện thực S2 | R | `✓` | `✓` | `-` | `-` |

---

### 3. Nguyên tắc Bảo vệ Phân quyền Thực tế trong Mã nguồn (Implementation Audit)

Khác với các tài liệu lý thuyết chung chung, toàn bộ ma trận trên được bảo vệ bởi mã nguồn thực tế đã được biên dịch và kiểm thử qua 2 tầng:

#### 3.1. Lớp 1: Bảo vệ Điều hướng Giao diện Frontend (`ProtectedRoute.tsx` & `AdminShell.tsx`)
1. **Phân quyền Route cấp cao:**
   - Tại [`src/frontend/admin-dashboard/src/App.tsx`](file:///Users/bo/OISMS/src/frontend/admin-dashboard/src/App.tsx#L45-L50), trang Báo cáo (`/reports`) và Tổ chức (`/organization`) được bọc riêng trong thẻ bảo vệ chỉ dành cho Owner:
     ```tsx
     <Route element={<ProtectedRoute allowedRoles={["Owner"]} />}>
       <Route path="/reports" element={<ReportsPage />} />
       <Route path="/organization" element={<OrganizationPage />} />
     </Route>
     ```
   - Nếu tài khoản có vai trò `Staff` truy cập vào `/reports` hoặc `/organization`, component [`ProtectedRoute.tsx`](file:///Users/bo/OISMS/src/frontend/admin-dashboard/src/components/ProtectedRoute.tsx#L25-L28) lập tức đẩy về `/403` (Access Denied).
2. **Ẩn Menu điều hướng theo Vai trò:**
   - Tại [`src/frontend/admin-dashboard/src/components/AdminShell.tsx`](file:///Users/bo/OISMS/src/frontend/admin-dashboard/src/components/AdminShell.tsx#L52-L54), thanh điều hướng Sidebar tự động lọc bỏ nút "Báo cáo" nếu người dùng không phải là Owner:
     ```tsx
     const visibleNav = nav.filter(
       (item) => account.role === "Owner" || !["/reports"].includes(item.to)
     );
     ```
3. **Cô lập Quầy POS và Chặn Cashier:**
   - Khi tài khoản có vai trò `Cashier` đăng nhập, [`AuthContext.tsx`](file:///Users/bo/OISMS/src/frontend/admin-dashboard/src/context/AuthContext.tsx#L109) thiết lập `landingPath: "/pos"`. 
   - Nếu `Cashier` cố tình gõ URL `/admin`, `ProtectedRoute` phát hiện role không nằm trong `["Owner", "Staff"]` và ngay lập tức chặn lại chuyển hướng sang `/403`.

#### 3.2. Lớp 2: Bảo vệ Endpoint Backend API (`OISM.WebApi`)
- Ngay cả khi người dùng bypass giao diện để gọi trực tiếp API qua Swagger hoặc Postman, toàn bộ Controller Backend đều áp dụng thuộc tính `[Authorize(Roles = "...")]`:
  - `POST /api/warehouses`: Bắt buộc `Roles = "Owner,Staff"`. Tài khoản Cashier gửi request sẽ nhận mã `403 Forbidden`.
  - `GET /api/reports/...`: Bắt buộc `Roles = "Owner"`.
  - Mọi câu truy vấn dữ liệu đều đi qua EF Core Global Query Filter tự động gán `WHERE tenant_id = @CurrentTenantId`, triệt tiêu hoàn toàn rủi ro đọc/ghi dữ liệu chéo giữa các cửa hàng.
