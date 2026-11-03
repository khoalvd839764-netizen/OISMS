# TỔNG HỢP TOÀN BỘ HỒ SƠ & BÁO CÁO SPRINT 2 (BACKEND 2 - NHỰT PHÙNG MINH)
## DỰ ÁN OISMS — ĐỒ ÁN CHUYÊN NGÀNH / CÔNG NGHỆ PHẦN MỀM (UTH)

> **Người thực hiện:** **Phùng Minh Nhựt (Backend 2 Lead)**  
> **Người phối hợp & Review:** **Lê Võ Đăng Khoa (Nhóm trưởng / Backend 1 Lead)**  
> **Nguồn đối chiếu:** GitHub, VS Code Workspace, Jira Software, Confluence Wiki  
> **Kết quả nghiệm thu kỹ thuật:** **188/188 Tests Pass 100%** (99 Unit Tests + 89 Integration Tests trên PostgreSQL 16 thật)

---

## MỤC LỤC TỔNG HỢP

1. [BẢNG TỔNG HỢP 4 NGUỒN MINH CHỨNG (GITHUB • JIRA • CONFLUENCE • VS CODE)](#1-bảng-tổng-hợp-4-nguồn-minh-chứng-github--jira--confluence--vs-code)
2. [CHI TIẾT TRÊN GITHUB (PULL REQUESTS & COMMITS)](#2-chi-tiết-trên-github-pull-requests--commits)
3. [CHI TIẾT TRÊN JIRA SOFTWARE (TASKS & SUBTASKS)](#3-chi-tiết-trên-jira-software-tasks--subtasks)
4. [CHI TIẾT TRÊN CONFLUENCE WIKI (PAGES & MATRICES)](#4-chi-tiết-trên-confluence-wiki-pages--matrices)
5. [CHI TIẾT MÃ NGUỒN TRONG VS CODE (9 NHÓM VIỆC BE2-01 ĐẾN BE2-09)](#5-chi-tiết-mã-nguồn-trong-vs-code-9-nhóm-việc-be2-01-đến-be2-09)
6. [9 LỖI REVIEW KINH ĐIỂN ĐÃ KHẮC PHỤC TRIỆT ĐỂ CÙNG KHOA](#6-9-lỗi-review-kinh-điển-đã-khắc-phục-triệt-để-cùng-khoa)
7. [KỊCH BẢN TRÌNH BÀY OVERVIEW SPRINT 2 TỐI NAY (DÀNH CHO NHỰT)](#7-kịch-bản-trình-bày-overview-sprint-2-tối-nay-dành-cho-nhựt)

---

# 1. BẢNG TỔNG HỢP 4 NGUỒN MINH CHỨNG

| Nền tảng | Mã định danh / Liên kết | Tiêu đề / Nội dung chính | Trạng thái |
| :--- | :--- | :--- | :---: |
| **GitHub** | **PR #1** (`feature/OISMS-01-...`) | Hoàn thiện API Đăng ký cửa hàng, Xác thực JWT và Quản lý Danh mục sản phẩm | **MERGED** |
| **GitHub** | **PR #4** (`feature/OISMS-27-...`) | Bổ sung API Quản lý Kho hàng, Chi nhánh, POS Search và Báo cáo Nghiệm thu Sprint 2 | **MERGED** |
| **GitHub** | **PR #14** (`docs/OISMS-31-...`) | Cập nhật Ma trận phân quyền truy cập màn hình 3.1.3 (39 màn hình) | **CLOSED / MERGED** |
| **GitHub** | **PR #16** (`develop -> main`) | Release Sprint 2: Nghiệm thu hoàn tất toàn bộ Sprint 2 đưa về nhánh `main` | **MERGED** |
| **Jira** | **`OISMS-27`** | Backend 2: Đăng ký cửa hàng, JWT Auth, Cây danh mục, Sản phẩm, Biến thể SKU, Mã vạch EAN-13, Bảng giá | **DONE** |
| **Jira** | **`OISMS-31`** | Đặc tả Ma trận phân quyền truy cập màn hình 3.1.3 (Screen Authorization Matrix) | **DONE** |
| **Confluence**| **Page ID `5275650`** | Kế hoạch Phân công Công việc Chi tiết các Sprint (OISMS) — Version 9 & 11 | **APPROVED** |
| **Confluence**| **Mục 3.1.3** | Ma trận phân quyền 39 màn hình đối chiếu quyền hạn `Owner`, `Staff`, `Cashier`, `Guest` | **APPROVED** |
| **VS Code** | `Check_list.md` (Gốc) | Checklist Nghiệm thu Đồ án Sprint 2 (Mục II.1, II.2, II.5 và Mục V ghi danh Nhựt) | **VERIFIED** |
| **VS Code** | `S2_BACKEND2_REPORT_...md` | Báo cáo cá nhân Sprint 2 của Nhựt chi tiết 275 dòng (188 tests pass 100%) | **VERIFIED** |
| **VS Code** | `SPRINT_2_BACKEND_2.md` | Bảng đặc tả phân rã 9 hạng mục nhiệm vụ BE2-01 đến BE2-09 | **VERIFIED** |

---

# 2. CHI TIẾT TRÊN GITHUB (PULL REQUESTS & COMMITS)

### 2.1. Pull Request #1: Nền tảng Xác thực & Danh mục ban đầu
- **Nhánh (Branch):** `feature/OISMS-01-dang-ky-va-xac-thuc-jwt`
- **Mã Task Jira:** `[OISMS-27]`
- **Mô tả:** Khởi tạo luồng đăng ký Tenant (`/api/auth/register-tenant`), tạo tài khoản Owner đầu tiên; Đăng nhập cấp cặp Access Token JWT (60 phút) + Refresh Token (7 ngày) băm mật khẩu BCrypt; CRUD Danh mục phân cấp đa tầng (`/api/categories`) và Thương hiệu (`/api/brands`).
- **Trạng thái:** **MERGED** vào `develop` ngày 16/10/2026.

### 2.2. Pull Request #4: Mở rộng SKU, Barcode EAN-13, Kho hàng & Nghiệm thu Sprint 2
- **Nhánh (Branch):** `feature/OISMS-27-bo-sung-kho-hang-va-bao-cao-nghiem-thu`
- **Mã Task Jira:** `[OISMS-27]`
- **Mô tả:** Bổ sung API Quản lý Chi nhánh (`/api/branches`), Kho hàng (`/api/warehouses`), Nhà cung cấp (`/api/suppliers`); Hoàn thiện CRUD biến thể SKU với thuộc tính động `attributes jsonb`, thuật toán sinh mã vạch chuẩn quốc tế **EAN-13** có Checksum Modulo 10; Xây dựng API Quầy POS (`/api/pos/search` và `/api/pos/barcode/{code}`) tích hợp tính tồn khả dụng theo chi nhánh `Available = OnHand - Reserved`. Đính kèm báo cáo nghiệm thu Sprint 2.
- **Trạng thái:** **MERGED** vào `develop` ngày 17/10/2026.

### 2.3. Pull Request #14: Ma trận Phân quyền Màn hình 3.1.3
- **Nhánh (Branch):** `docs/OISMS-31-ma-tran-phan-quyen-3-1-3`
- **Mã Task Jira:** `[OISMS-31]`
- **Mô tả:** Tài liệu hóa chi tiết quyền hạn truy cập của 4 vai trò trên 39 màn hình giao diện (Web Admin Dashboard và Web POS).
- **Trạng thái:** **MERGED / CLOSED** vào `develop` ngày 22/10/2026.

### 2.4. Pull Request #16: Release đóng gói hoàn tất Sprint 2
- **Nhánh (Branch):** Gộp từ `develop` vào `main`.
- **Mô tả:** Nghiệm thu toàn diện toàn bộ Sprint 2 của cả nhóm OISMS (Backend 1 của Khoa, Backend 2 của Nhựt, Admin của Phúc Hữu, POS của Phương).
- **Trạng thái:** **MERGED** vào `main` ngày 23/10/2026.

---

# 3. CHI TIẾT TRÊN JIRA SOFTWARE (TASKS & SUBTASKS)

### 3.1. Task Jira chính: `OISMS-27`
- **Tiêu đề:** `[Sprint 2 - Backend 2] Hoàn thiện API Đăng ký cửa hàng, Xác thực JWT, Quản lý Cây danh mục, Sản phẩm, Biến thể SKU, Barcode EAN-13 và Quầy POS`
- **Người thực hiện (Assignee):** **Nhựt Phùng Minh**
- **Đường dẫn Jira:** `https://khoalevodang301007.atlassian.net/browse/OISMS-27`
- **Trạng thái:** **DONE (Hoàn thành)**
- **Tiêu chuẩn nghiệm thu DoD:**
  - Đăng ký cửa hàng tạo đồng thời Tenant và Owner; không rò rỉ dữ liệu dở dang nếu lỗi.
  - JWT Token Rotation nguyên tử trên PostgreSQL, băm mật khẩu BCrypt.
  - Phân quyền RBAC 3 vai trò: `Owner`, `Staff`, `Cashier`. Cashier gọi API nhập kho nhận `403 Forbidden`.
  - Cây danh mục chống vòng lặp chu trình $A \rightarrow B \rightarrow A$ bằng PostgreSQL Advisory Lock.
  - SKU lưu thuộc tính tùy ý trong `attributes jsonb`, chặn trùng tổ hợp trả `409 Conflict`.
  - Sinh mã vạch EAN-13 chuẩn Modulo 10, POS tìm kiếm phản hồi $< 200\text{ms}$.
  - Toàn bộ bài kiểm thử tự động đạt 100%.

### 3.2. Subtask Jira: `OISMS-31`
- **Tiêu đề:** `Đặc tả Ma trận Phân quyền Truy cập Màn hình 3.1.3 (Screen Authorization Matrix)`
- **Người thực hiện:** **Nhựt Phùng Minh**
- **Đường dẫn Jira:** `https://khoalevodang301007.atlassian.net/browse/OISMS-31`
- **Trạng thái:** **DONE (Hoàn thành)**
- **Nội dung:** Lập ma trận đối chiếu 39 màn hình chức năng với 4 vai trò (`Owner`, `Staff`, `Cashier`, `Guest`), bảo vệ 2 tầng: React Route Guard ở Frontend và `[Authorize(Roles = "...")]` ở Backend.

---

# 4. CHI TIẾT TRÊN CONFLUENCE WIKI (PAGES & MATRICES)

### 4.1. Confluence Page ID: `5275650` (Version 9 & 11)
- **Tên trang:** *Kế hoạch Phân công Công việc Chi tiết các Sprint (OISMS)*
- **Nội dung đối chiếu Backend 2 (Nhựt):**
  - Giai đoạn: **Sprint 2** (13/10/2026 – 17/10/2026 | Ngày D1 đến D6).
  - Khối lượng công việc (Effort): Đảm nhiệm **25% tổng khối lượng Backend**, phụ trách toàn bộ lát cắt người dùng, bảo mật truy cập và danh mục sản phẩm (Auth & Product Catalog).
  - Đầu ra bàn giao: Toàn bộ API bàn giao qua các PR nhỏ, kèm Unit/Integration Tests và Swagger UI đầy đủ.

### 4.2. Confluence Mục 3.1.3: Ma trận phân quyền màn hình
- Phân định rõ ràng quyền truy cập:
  - **`Owner` (Chủ shop):** Toàn quyền truy cập 39/39 màn hình (kể cả Báo cáo tài chính, Lợi nhuận gộp, Cấu hình chi nhánh/kho).
  - **`Staff` (Nhân viên kho/quản lý đơn):** Xem và lập phiếu kho, xem sản phẩm, đơn hàng; **bị cấm xem Báo cáo Lợi nhuận gộp**.
  - **`Cashier` (Thu ngân quầy):** Chỉ được phép truy cập màn hình **Web POS** (`localhost:5174`), tìm kiếm sản phẩm và bán hàng; **bị chặn toàn bộ các màn hình quản trị Web Admin (`localhost:5173`)**.

---

# 5. CHI TIẾT MÃ NGUỒN TRONG VS CODE (9 NHÓM VIỆC BE2-01 ĐẾN BE2-09)

Dựa trên tài liệu nghiệm thu [**`docs/09-personal-reports/nhut/sprint-2/S2_BACKEND2_REPORT_2026-10-18.md`**](file:///Users/bo/OISMS/docs/09-personal-reports/nhut/sprint-2/S2_BACKEND2_REPORT_2026-10-18.md), toàn bộ 9 nhóm việc của bạn bao gồm:

| Mã việc | Tên phân hệ | File mã nguồn triển khai trong VS Code | Bài test kiểm chứng (Passed 100%) |
| :---: | :--- | :--- | :--- |
| **BE2-01** | Đăng ký cửa hàng | `AuthController.cs`, `RegisterTenantValidator.cs`, `PhoneHelper.cs` | `Postgres_Auth_RegisterTenant_PhoneNumber_...`, `TC_AUTH_01_RegisterTenant_...` |
| **BE2-02** | Xác thực JWT & Token Rotation | `AuthController.cs`, `JwtTokenService.cs`, `LoginValidator.cs` | `Postgres_RefreshToken_WhenTenantIsLocked_...`, `Postgres_ConcurrentRefreshToken_...` |
| **BE2-03** | Phân quyền RBAC 3 vai trò | `BranchController.cs`, `WarehousesController.cs`, `ProductsController.cs` | `Postgres_BranchAndWarehouse_Update_AndRbacCashierForbidden`, `TC_AUTH_02_CashierRole_...` |
| **BE2-04** | Cây Danh mục & Thương hiệu | `CategoriesController.cs`, `BrandsController.cs`, `CategoryDtos.cs`, `BrandDtos.cs` | `Postgres_CategoryTree_ConcurrentUpdates_...`, `Postgres_UpdateCategory_WithNonExistentParentId_...` |
| **BE2-05** | Sản phẩm, Biến thể SKU & JSON | `ProductsController.cs`, `ProductDtos.cs`, `SkuAttributeHelper.cs`, `ProductVariant.cs` | `Postgres_SkuBarcode_UniqueConstraint_...`, `Postgres_SkuAttributes_PreserveNestedNulls_...`, `Postgres_UpdateVariant_AttributeCombinationConflict_...` |
| **BE2-06** | Quản lý Giá bán sỉ & lẻ | `ProductsController.cs`, `ProductDtos.cs` | `Postgres_SkuCrud_UpdatePrices_...`, `UpdateVariantPricesValidator_...` |
| **BE2-07** | Mã vạch EAN-13 & Quầy POS | `PosCatalogController.cs`, `PosSearchDtos.cs`, `BarcodeHelper.cs` | `Postgres_PosCatalog_SearchAndBarcode_...`, `BarcodeHelper_ShouldGenerate_ValidEan13` |
| **BE2-08** | Chi nhánh, Kho hàng, Nhà cung cấp | `BranchController.cs`, `WarehousesController.cs`, `SuppliersController.cs` | `Postgres_Warehouse_ChangeBranch_WhenHasHistory_...`, `Branch_CreateAndToggleStatus_...` |
| **BE2-09** | Validation & Swagger API | Thư mục `Dtos/`, `Validators/`, `Program.cs`, `OISM.UnitTests/`, `OISM.IntegrationTests/` | **188/188 Tests Passed 100%** (99 Unit Tests + 89 Integration Tests) |

---

# 6. 9 LỖI REVIEW KINH ĐIỂN ĐÃ KHẮC PHỤC TRIỆT ĐỂ CÙNG KHOA

Trong quá trình nghiệm thu Sprint 2, Khoa và Nhựt đã tái hiện và khắc phục tận gốc 9 lỗi kỹ thuật phức tạp:

1. **Tenant bị khóa nhưng vẫn Refresh Token được:** Bổ sung kiểm tra `user.Tenant.IsActive` trong `AuthController.RefreshToken`, trả về `401 Unauthorized` ngay lập tức.
2. **Race condition khi gửi 2 request Refresh Token đồng thời:** Dùng khóa bi quan `SELECT id FROM refresh_tokens WHERE token_hash = @hash FOR UPDATE`. Chỉ 1 request được cấp token mới, request thứ 2 bị đá văng `401 Unauthorized`.
3. **Cập nhật Danh mục với `ParentId` không tồn tại văng lỗi 500:** Kiểm tra `parentExists` cùng Tenant trước khi duyệt cây, từ chối bằng lỗi `400 Bad Request` văn minh.
4. **Cập nhật SKU bị lọt tổ hợp thuộc tính trùng:** Xây dựng hàm `SkuAttributeHelper.ResolveAttributes` hợp nhất thuộc tính cũ, thuộc tính JSON và trường explicit, tính chữ ký Canonical và chặn trùng bằng mã **`409 Conflict`**. Khóa dòng Product bằng `FOR UPDATE`.
5. **Đổi chi nhánh của kho hàng đã có lịch sử nhập hàng văng lỗi 500:** Bọc trong Transaction khóa dòng kho hàng, kiểm tra nếu đã có chứng từ nhập kho hoặc sổ cái thì chặn đổi chi nhánh bằng mã **`409 Conflict`**, bảo vệ toàn vẹn khóa ngoại composite `fk_goods_receipts_warehouse_branch`.
6. **Mất kiểu JSON và mất `null` lồng nhau trong biến thể SKU:** Viết hàm chuyển đổi đệ quy bảo toàn 100% kiểu Object, Array, Number, Bool, String và giữ nguyên giá trị `null` lồng nhau (phân biệt `["S", null, "M"]` khác `["S", "M"]`).
7. **Số điện thoại đăng ký người dùng `"090 123 4567"` gây lỗi 500:** Viết `PhoneHelper` chuẩn hóa số điện thoại về chuẩn quốc tế E.164 (`"+84901234567"`), bảo vệ check constraint `ck_users_phone_normalized`.
8. **Mã vạch Barcode trùng gây lỗi 500:** Kiểm tra trùng barcode theo tenant, retry 10 lần sinh EAN-13, bắt lỗi vi phạm unique constraint `uq_skus_3` trả mã **`409 Conflict`** (cho phép trùng nếu khác tenant).
9. **Cập nhật Danh mục đồng thời tạo vòng lặp $A \rightarrow B \rightarrow A$:** Sử dụng **PostgreSQL Advisory Lock theo Tenant** (`pg_advisory_xact_lock`), xóa ChangeTracker (`_context.ChangeTracker.Clear()`) đọc dữ liệu mới nhất, duyệt ngược tổ tiên (`Ancestor Traversal`) phát hiện chu trình và trả lỗi **`400 Bad Request`**.

---

# 7. KỊCH BẢN TRÌNH BÀY OVERVIEW SPRINT 2 TỐI NAY (DÀNH CHO NHỰT)

Khi Khoa hoặc Thầy Cô bảo: *"Nhựt hãy overview toàn bộ những gì bạn đã làm trong Sprint 2!"*, bạn nhìn vào tài liệu này và tự tin nói rành rọt theo 4 ý sau:

> *"Dạ chào Khoa và cả nhóm! Trong Sprint 2 vừa qua, em đảm nhận vai trò **Backend 2 Lead**, phụ trách hoàn thiện 3 phân hệ cốt lõi:
> 
> 1. **Về Xác thực & Phân quyền (`FR-AUTH`):**  
>    - Em đã hoàn thành API đăng ký cửa hàng cấp `TenantId` riêng biệt, chuẩn hóa SĐT E.164.  
>    - Xây dựng cơ chế đăng nhập cấp cặp JWT Token, cài đặt **Token Rotation nguyên tử trên PostgreSQL** bằng khóa `FOR UPDATE` để chống tấn công Replay Attack.  
>    - Cấu hình phân quyền RBAC 3 vai trò: `Owner`, `Staff`, `Cashier` — chặn đứng thu ngân gọi các API quản trị kho bằng mã `403 Forbidden`.
> 
> 2. **Về Danh mục, Sản phẩm, Biến thể SKU & Barcode (`FR-PROD`):**  
>    - Xây dựng Cây danh mục đa tầng, áp dụng **PostgreSQL Advisory Lock** và thuật toán duyệt tổ tiên để triệt tiêu hoàn toàn lỗi tạo vòng lặp chu trình $A \rightarrow B \rightarrow A$.  
>    - Quản lý biến thể SKU với thuộc tính động linh hoạt `attributes jsonb`, viết hàm `SkuAttributeHelper` tính chữ ký Canonical chặn trùng thuộc tính bằng mã `409 Conflict`.  
>    - Viết thuật toán tự động sinh mã vạch quốc tế **EAN-13** chuẩn Checksum Modulo 10 (đầu số Việt Nam `893`), tích hợp API tìm kiếm và quét mã quầy POS với độ trễ phản hồi dưới 200ms.
> 
> 3. **Về Quản lý Chi nhánh & Kho hàng:**  
>    - Xây dựng API quản lý chi nhánh, kho hàng, nhà cung cấp, thiết lập khóa dòng bảo vệ không cho phép đổi chi nhánh của một kho đã có lịch sử nhập hàng hoặc phát sinh sổ cái.
> 
> 4. **Về Kết quả kiểm thử & Bàn giao:**  
>    - Toàn bộ phân hệ của em đã được nghiệm thu qua **PR #1 và PR #4 trên GitHub**, đối chiếu đồng bộ với Jira `OISMS-27` và Confluence 3.1.3.  
>    - Đạt **188/188 bài test PASS 100%** trên cơ sở dữ liệu PostgreSQL 16 thật.  
>    - Hiện tại toàn bộ 6 container Docker trên máy em đều đang chạy mượt mà, sẵn sàng 100% cho buổi nghiệm thu và vấn đáp ạ!"*
