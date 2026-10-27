# 09. ĐẶC TẢ MÀN HÌNH QUẢN LÝ CƠ CẤU TỔ CHỨC & CHI NHÁNH (ORGANIZATION & BRANCH SCREENS)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SCR-09` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực** | `2026-10-25` |
| **Đường dẫn URL Routes** | `/admin/organization`, `/organization` |
| **Quyền truy cập (RBAC)** | **Chỉ dành cho Chủ sở hữu (`Owner`)** |
| **Source code Frontend** | [`OrganizationPage.tsx`](file:///d:/OISMS/src/frontend/admin-dashboard/src/pages/OrganizationPage.tsx) |
| **Controllers Backend** | [`BranchController.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/BranchController.cs), [`WarehousesController.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/WarehousesController.cs), [`SuppliersController.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/SuppliersController.cs) |

---

## 1. Mục Tiêu & Luồng Nghiệp Vụ

Màn hình **Quản trị Cơ cấu Tổ chức & Chi nhánh** cung cấp quyền thiết lập hạ tầng cốt lõi cho Chủ doanh nghiệp (`Owner`):
1. **Quản lý Mạng lưới Chi nhánh (Branches):** Tạo mới, chỉnh sửa thông tin liên hệ, địa chỉ và bật/tắt trạng thái hoạt động (`IsActive`) của từng cửa hàng.
2. **Quản lý Kho hàng trực thuộc (Warehouses):** Thiết lập quan hệ $1 - n$ (Một chi nhánh có thể có một hoặc nhiều kho hàng: Kho bán lẻ tại quầy, Kho tổng, Kho dự trữ).
   * **Ràng buộc an toàn dữ liệu:** Hệ thống nghiêm cấm hành vi chuyển đổi Chi nhánh cha của một Kho hàng nếu kho đó đã phát sinh bất kỳ giao dịch nào trong Sổ cái tồn kho (`409 Conflict`).
3. **Quản lý Nhân sự & Phân quyền RBAC:** Cấp tài khoản nhân viên, gán vai trò (`Staff` hoặc `Cashier`) và quản lý phân quyền phạm vi truy cập theo từng chi nhánh cụ thể.
4. **Danh mục Nhà cung cấp (Suppliers Directory):** Quản lý đối tác nhập hàng, mã số thuế, số điện thoại chuẩn `E.164` và người liên hệ.

---

## 2. Bố Cục Giao Diện Wireframe (ASCII Wireframe)

```text
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ BREADCRUMB: Quản trị hệ thống & Cơ cấu tổ chức                                                 │
│ Quản Lý Cơ Cấu Tổ Chức & Chi Nhánh                     [+ Thêm Nhân Viên] [+ Thêm Chi Nhánh]   │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ CHỌN TAB QUẢN TRỊ:                                                                             │
│ ┌───────────────────────────────┬───────────────────────────────┐                              │
│ │ [✓] CHI NHÁNH & KHO HÀNG      │ [ ] NHÂN VIÊN & PHÂN QUYỀN    │                              │
│ └───────────────────────────────┴───────────────────────────────┘                              │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ TAB 1: DANH SÁCH CHI NHÁNH & KHO TRỰC THUỘC                                                    │
│ ┌────────────────────────────────────────────────────────────────────────────────────────────┐ │
│ │ 🏢 Chi Nhánh Quận 1 (Cửa hàng Flagship)                      🟢 Đang hoạt động [ Sửa ] [ ⚙ ]│ │
│ │    Địa chỉ: 123 Nguyễn Huệ, P.Bến Nghé, Q.1, TP.HCM | SĐT: 028.3822.9999                   │ │
│ │    Kho hàng trực thuộc:                                                                    │ │
│ │    • Kho Bán Lẻ Q1   (Mã: WH-Q1-POS) | Tồn kho: 1,450 SP | 🟢 Đang dùng [ Đổi tên ]        │ │
│ │    • Kho Lưu Trữ Tầng 2 (Mã: WH-Q1-ST) | Tồn kho: 3,200 SP | 🟢 Đang dùng [ Đổi tên ]        │ │
│ │    [ + Thêm kho mới cho chi nhánh này ]                                                    │ │
│ └────────────────────────────────────────────────────────────────────────────────────────────┘ │
│ ┌────────────────────────────────────────────────────────────────────────────────────────────┐ │
│ │ 🏢 Chi Nhánh Cầu Giấy (Hà Nội)                               🟢 Đang hoạt động [ Sửa ] [ ⚙ ]│ │
│ │    Địa chỉ: 45 Xuân Thủy, Dịch Vọng Hậu, Cầu Giấy, Hà Nội | SĐT: 024.3768.8888              │ │
│ │    Kho hàng trực thuộc:                                                                    │ │
│ │    • Kho Bán Hàng CG (Mã: WH-HN-01)  | Tồn kho: 890 SP   | 🟢 Đang dùng [ Đổi tên ]        │ │
│ └────────────────────────────────────────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Modal Thêm/Sửa Chi Nhánh & Kho Hàng

### 3.1. Modal Thêm Chi Nhánh Mới
* Tên chi nhánh (Bắt buộc, không trùng lặp trong Tenant).
* Địa chỉ chi nhánh & Số điện thoại hotline.
* Tự động khởi tạo 1 Kho mặc định mang tên `Kho Bán Lẻ [Tên Chi Nhánh]`.

### 3.2. Modal Thêm Kho Hàng Trực Thuộc
* Tên kho hàng (vd: `Kho Tổng`, `Kho Lưu Trữ Tầng Hầm`).
* Mã kho hàng (viết hoa không dấu, vd: `WH-Q1-MAIN`).
* Chi nhánh sở hữu: Cố định theo chi nhánh đang chọn.

---

## 4. Bảng Hàm API Backend & Tài Liệu Tham Chiếu

| Method | Endpoint Backend | Hàm xử lý Controller / Service | Tài liệu Hợp đồng API tham chiếu | Mã lỗi liên quan |
| :---: | :--- | :--- | :--- | :--- |
| `GET` | `/api/branches` | `BranchController.GetBranches` | [`DOC-API-01 - 01-auth-endpoints.md`](../05-api-contracts/01-auth-endpoints.md#3-endpoints-quản-lý-chi-nhánh-kho-hàng--nhà-cung-cấp-fr-auth-04) | `AUTH_UNAUTHORIZED` (Xem [`DOC-ERR-02`](../12-error-and-status-codes/02-error-codes-catalog.md)) |
| `POST` | `/api/branches` | `BranchController.CreateBranch` | [`DOC-API-01 - 01-auth-endpoints.md`](../05-api-contracts/01-auth-endpoints.md#3-endpoints-quản-lý-chi-nhánh-kho-hàng--nhà-cung-cấp-fr-auth-04) | `VALIDATION_ERROR`, `FORBIDDEN_ACCESS` |
| `GET` | `/api/warehouses` | `WarehousesController.GetWarehouses` | [`DOC-API-01 - 01-auth-endpoints.md`](../05-api-contracts/01-auth-endpoints.md#3-endpoints-quản-lý-chi-nhánh-kho-hàng--nhà-cung-cấp-fr-auth-04) | `AUTH_UNAUTHORIZED` |
| `POST` | `/api/warehouses` | `WarehousesController.CreateWarehouse` | [`DOC-API-01 - 01-auth-endpoints.md`](../05-api-contracts/01-auth-endpoints.md#3-endpoints-quản-lý-chi-nhánh-kho-hàng--nhà-cung-cấp-fr-auth-04) | `VALIDATION_ERROR`, `BRANCH_INACTIVE_OR_INVALID` |
| `GET` | `/api/suppliers` | `SuppliersController.GetSuppliers` | [`DOC-API-01 - 01-auth-endpoints.md`](../05-api-contracts/01-auth-endpoints.md#3-endpoints-quản-lý-chi-nhánh-kho-hàng--nhà-cung-cấp-fr-auth-04) | `AUTH_UNAUTHORIZED` |
| `POST` | `/api/suppliers` | `SuppliersController.CreateSupplier` | [`DOC-API-01 - 01-auth-endpoints.md`](../05-api-contracts/01-auth-endpoints.md#3-endpoints-quản-lý-chi-nhánh-kho-hàng--nhà-cung-cấp-fr-auth-04) | `VALIDATION_ERROR` |
