# BIÊN BẢN NGHIỆM THU & BÀN GIAO WEB ADMIN (REACT 19)
**Dự án:** OISMS - Omnichannel Inventory & Sales Management System  
**Phân hệ bàn giao:** `src/frontend/admin-dashboard` (Web Admin Dashboard - React 19 + TypeScript + Vite 8 + Tailwind v4)  
**Thành viên thực hiện:** Phúc Hữu (Frontend 1 - Web Admin Lead)  
**Trưởng nhóm nghiệm thu:** Đặng Khoa (Team Lead / Backend 1)  
**Ngày hoàn tất:** 17/10/2026  

---

## 1. TỔNG KẾT CÁC HẠNG MỤC ĐÃ HOÀN THÀNH (PHASE 1 → PHASE 6)

| Giai đoạn | Hạng mục triển khai | File mã nguồn chính | Trạng thái |
| :--- | :--- | :--- | :---: |
| **Phase 1** | Nền tảng UI Figma Prototype, Cấu hình Vite 8, Tailwind v4, HTTP Client & JWT Interceptor | `src/index.css`, `src/api/client.ts`, `src/components/common/ui.tsx` | **Hoàn tất 100%** |
| **Phase 2** | Xác thực JWT thật, Silent Refresh Queue, Điều hướng RBAC (`Owner`/`Staff`/`Cashier`), 403, Offline Banner, ErrorBoundary | `src/context/AuthContext.tsx`, `src/components/guards/ProtectedRoute.tsx`, `src/components/guards/RoleGuard.tsx`, `src/pages/AuthPage.tsx`, `src/components/feedback/ErrorBoundary.tsx`, `src/components/feedback/NetworkBanner.tsx` | **Hoàn tất 100%** |
| **Phase 3** | Quản lý Cây Danh mục Cha - Con (chống vòng lặp đệ quy) & Quản lý Thương hiệu (Brands) | `src/pages/ProductsPage.tsx`, `src/api/catalog.api.ts`, `src/types/catalog.types.ts` | **Hoàn tất 100%** |
| **Phase 4** | Quản lý Sản phẩm & Biến thể SKU đa dòng, Sinh mã vạch chuẩn GS1 EAN-13 (Modulo 10 Check Digit) | `src/pages/ProductsPage.tsx`, `src/api/catalog.api.ts` (`generateEan13Barcode`, `isValidEan13`) | **Hoàn tất 100%** |
| **Phase 5** | Phiếu nhập kho nhiều dòng (Multi-line Goods Receipt) + Dự phóng WAC, Bảng Tồn kho & Sổ kho bất biến (Read-Only Append-Only Ledger) | `src/pages/InventoryPage.tsx`, `src/api/inventory.api.ts`, `src/types/inventory.types.ts` | **Hoàn tất 100%** |
| **Phase 6** | Tối ưu hóa Code-Splitting (`React.lazy` + `Suspense`), kiểm tra `tsc -b`, `vite build`, `oxlint`, cấu hình Docker & Nginx Reverse Proxy | `src/App.tsx`, `Dockerfile`, `nginx.conf` | **Hoàn tất 100%** |
| **Phase 7** | Nối trực tiếp API Kho hàng Backend (`/api/inventory/*`) & Kiểm thử tích hợp End-to-End (Phiếu nhập nhiều dòng, WAC, Reorder Point, Sổ kho bất biến) | `InventoryController.cs`, `InventoryDtos.cs`, `AppDbContext.cs`, `InventoryIntegrationTests.cs` | **Hoàn tất 100%** |

---

## 2. KẾT QUẢ KIỂM TRA BIÊN DỊCH & KIỂM THỬ TÍCH HỢP (BUILD, CODE-SPLITTING & INTEGRATION TESTS)

1. **Kiểm tra Linter Frontend (`npm run lint` - `oxlint`)**:
   - Quét toàn bộ mã nguồn TypeScript/React: **0 errors**.
2. **Kiểm tra TypeScript & Đóng gói Production (`npm run build` - `tsc -b && vite build`)**:
   - Đã áp dụng `React.lazy()` và `<Suspense>` tách từng trang thành các chunk độc lập (on-demand loading), giảm kích thước bundle khởi tạo từ `456.83 kB` xuống còn `332.91 kB` và tối ưu tốc độ tải trang lần đầu.
3. **Kiểm tra Zero-Mock**:
   - Đã xóa bỏ hoàn toàn `DemoSessionContext.tsx`, `DemoToolsPage.tsx`, `mockData.ts`, `apiAdapter.ts`, `authAdapter.ts`, `realtimeAdapter.ts`, `simulatorAdapter.ts`.
4. **Kiểm tra Backend & Integration Tests (`dotnet build` & `dotnet test`)**:
   - Đã hoàn thiện `InventoryController.cs` (`/api/inventory/balances`, `/api/inventory/balances/reorder-point`, `/api/inventory/receipts`, `/api/inventory/receipts/{id}/confirm`, `/api/inventory/ledger`) tuân thủ nghiêm ngặt các ràng buộc trigger PostgreSQL v0.5 (`oisms_check_stock`, `ledger_append_only`) và Global Query Filter `IMustHaveTenant`.
   - Toàn bộ kịch bản kiểm thử tích hợp (`InventoryIntegrationTests`, `CatalogAndProductIntegrationTests`, `BranchAndSupplierIntegrationTests`, `AuthIntegrationTests`) đều **PASS 100%**.

---

## 3. HƯỚNG DẪN CHẠY & KIỂM THỬ NGHIỆM THU (E2E VERIFICATION GUIDE)

### Cách 1: Chạy trực tiếp trên máy phát triển (Local Development)
```bash
cd src/frontend/admin-dashboard
npm install
npm run dev
```
- Ứng dụng chạy tại: `http://localhost:5173`
- Kết nối tới Backend Web API tại: `http://localhost:5000/api` (cấu hình trong `.env`).

### Cách 2: Chạy bằng Docker Compose toàn hệ thống
```bash
docker compose up --build -d
```
- Container `oism_admin_dashboard` phục vụ trên cổng `http://localhost:5173` qua Nginx (hỗ trợ cả SPA fallback `try_files $uri $uri/ /index.html` và reverse proxy `/api/` sang `http://backend:8080/api/`).

### Kịch bản kiểm thử nghiệm thu chuẩn (Test Cases):
1. **Kiểm thử Đăng ký Tenant & Đăng nhập (`/login`)**:
   - Chuyển sang chế độ **"Đăng ký doanh nghiệp mới"**, nhập Tên doanh nghiệp, Mã Tenant, Họ tên Owner, Email, Mật khẩu -> Hệ thống gọi `POST /api/auth/register-tenant`, tự động lưu JWT và điều hướng vào Dashboard.
   - Thử nhập sai mật khẩu -> Hệ thống hiển thị hộp cảnh báo đỏ và Toast báo lỗi xác thực từ Backend.
2. **Kiểm thử Phân quyền RBAC (`/organization`, `/reports`, `/403`)**:
   - Với tài khoản `Owner`: Thấy đầy đủ menu **Báo cáo doanh thu** và **Nhân viên & Chi nhánh**. Tạo mới được Chi nhánh (`/api/branches`), Kho vật lý (`/api/warehouses`), Nhà cung cấp (`/api/suppliers`).
   - Với tài khoản `Staff`: Menu tự động ẩn mục **Nhân viên & Chi nhánh** và **Báo cáo**. Nếu gõ trực tiếp URL `/organization` hoặc `/reports` trên thanh địa chỉ, `RoleGuard` lập tức chặn và chuyển sang màn hình `/403` (`Bạn không có quyền truy cập`).
   - Với tài khoản `Cashier`: Tự động điều hướng sang màn hình `/pos`.
3. **Kiểm thử Danh mục Cha-Con, Thương hiệu & Sản phẩm đa biến thể (`/products`)**:
   - Mở tab **Danh mục Cha - Con**: Tạo danh mục gốc (Root) và danh mục con (`parentId`). Khi bấm Sửa danh mục gốc, danh sách chọn Danh mục cha tự động loại bỏ chính nó và các danh mục con của nó (chống vòng lặp).
   - Mở tab **Thương hiệu**: Tạo mới và cập nhật thương hiệu.
   - Mở tab **Sản phẩm & SKU**: Nhấn **"Thêm sản phẩm & SKU"**, nhập thông tin chung và bấm **"+ Thêm dòng biến thể SKU"** để thêm nhiều SKU cùng lúc. Nhấn biểu tượng Barcode để tự động sinh mã **EAN-13** chuẩn 13 số (đầu số `893` + Check Digit Modulo 10).
4. **Kiểm thử Phiếu nhập kho nhiều dòng, Giá vốn WAC & Sổ kho bất biến (`/inventory`)**:
   - Nhấn **"Tạo phiếu nhập kho nhiều dòng"**: Chọn Kho vật lý, chọn Nhà cung cấp (hoặc bấm **"+ Mới"** tạo nhà cung cấp ngay tại chỗ), thêm nhiều dòng SKU với Số lượng và Đơn giá nhập -> Kiểm tra cột **Thành tiền**, **Tổng giá trị phiếu nhập** và **Dự kiến WAC mới** tự động tính toán.
   - Mở tab **Sổ kho chỉ đọc (Append-Only Ledger)**: Kiểm tra bộ lọc theo Kho, Chiều (`IN`/`OUT`), Nghiệp vụ và xác nhận **không có bất kỳ nút Sửa/Xóa nào** trên bảng sổ kho.
