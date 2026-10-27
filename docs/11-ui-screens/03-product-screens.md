# 03. ĐẶC TẢ MÀN HÌNH QUẢN LÝ SẢN PHẨM & DANH MỤC (PRODUCT CATALOG SCREENS)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SCR-03` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực** | `2026-10-25` |
| **Đường dẫn URL Routes** | `/admin/products`, `/products` |
| **Quyền truy cập (RBAC)** | `Owner`, `Staff` |
| **Source code Frontend** | [`ProductsPage.tsx`](file:///d:/OISMS/src/frontend/admin-dashboard/src/pages/ProductsPage.tsx) |
| **Controllers Backend** | [`ProductsController.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/ProductsController.cs), [`CategoriesController.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/CategoriesController.cs), [`BrandsController.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/BrandsController.cs) |

---

## 1. Mục Tiêu & Luồng Nghiệp Vụ

Màn hình **Quản lý Sản phẩm & Danh mục** phục vụ các nghiệp vụ quản trị hàng hóa đa kênh:
* Quản lý thông tin sản phẩm chuẩn (Master Product) và các biến thể chi tiết (SKU Variants) với các thuộc tính linh hoạt: Màu sắc (`Color`), Kích thước (`Size`) và `Attributes` JSON giữ nguyên kiểu dữ liệu.
* Tự động sinh mã vạch chuẩn `EAN-13` (Đầu số quốc gia `893` Việt Nam + 9 số ngẫu nhiên + 1 số kiểm tra Checksum) hoặc nhập thủ công `Code128`.
* Thiết lập và phân tách rõ ràng giữa **Giá bán lẻ (Retail Price)** và **Giá bán sỉ (Wholesale Price)**, hoàn toàn độc lập với giá vốn bình quân gia quyền (`WAC`).
* Quản lý Cây danh mục đa cấp (hỗ trợ kiểm tra chống lặp chu trình cha - con) và Thương hiệu sản phẩm.

---

## 2. Bố Cục Giao Diện Wireframe (ASCII Wireframe)

```text
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ BREADCRUMB: Quản lý danh mục & hàng hóa                                                        │
│ Danh Sách Sản Phẩm & Biến Thể SKU             [ Cây danh mục ] [ Thương hiệu ] [+ Thêm SP Mới] │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ THANH TÌM KIẾM & BỘ LỌC:                                                                       │
│ [ 🔍 Tìm tên SKU, Barcode... ] [ Danh mục: Tất cả ▼ ] [ Brand: Nike ▼ ] [ Trạng thái: Còn hàng ▼]│
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ BẢNG DỮ LIỆU SẢN PHẨM & SKU (PRODUCT DATA TABLE):                                              │
│ [✓] | Mã SKU        | Tên sản phẩm / Biến thể  | Mã vạch EAN-13| Giá lẻ     | Tồn kho | Trạng thái │
│ ────┼──────────────┼──────────────────────────┼───────────────┼────────────┼─────────┼─────────── │
│ [ ] | AO-POLO-TR-M | Áo Polo Cotton (Trắng, M)| 8935012345678 | 250,000 đ  | 45 cái  | 🟢 Đang bán│
│ [ ] | AO-POLO-TR-L | Áo Polo Cotton (Trắng, L)| 8935012345685 | 250,000 đ  | 12 cái  | 🟢 Đang bán│
│ [ ] | QN-JEAN-X-31 | Quần Jean Slim (Xanh, 31)| 8935098765432 | 480,000 đ  | 0 cái   | 🔴 Hết hàng│
│ [ ] | GY-SNK-D-42  | Giày Sneaker (Đen, 42)   | 8935044433221 | 850,000 đ  | 8 đôi   | ⚪ Ngừng bán│
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ PHÂN TRANG: Hiển thị 1 - 20 / Tổng số 142 mặt hàng                 [ < Trước ] Trang 1 [ Sau > ]│
└────────────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Modal Tạo/Sửa Sản Phẩm Đa Biến Thể

Khi bấm nút **`[+ Thêm Sản Phẩm Mới]`**, một Modal xuất hiện với các trường:
* **Thông tin chung:** Tên sản phẩm, Mã sản phẩm (Code), Danh mục cha, Thương hiệu.
* **Cấu hình biến thể (Variant Generator):**
  * Tùy chọn 1: *Sản phẩm đơn giản* (Chỉ có 1 mã SKU duy nhất).
  * Tùy chọn 2: *Sản phẩm đa biến thể* (Tự động sinh tổ hợp Ma trận: Màu sắc $\times$ Kích thước $\rightarrow$ Sinh danh sách dòng SKU tương ứng).
* **Mỗi dòng biến thể SKU:** Mã SKU (duy nhất theo Tenant), Barcode (có nút bấm `Sinh EAN-13`), Giá bán lẻ (`RetailPrice >= 0`), Giá bán sỉ (`WholesalePrice >= 0`), Định mức tồn tối thiểu (`MinStockThreshold`).

---

## 4. Bảng Hàm API Backend & Tài Liệu Tham Chiếu

| Method | Endpoint Backend | Hàm xử lý Controller / Service | Tài liệu Hợp đồng API tham chiếu | Mã lỗi liên quan |
| :---: | :--- | :--- | :--- | :--- |
| `GET` | `/api/products` | `ProductsController.GetProducts` | [`DOC-API-02 - 02-catalog-endpoints.md`](../05-api-contracts/02-catalog-endpoints.md#2-danh-mục-endpoints-sản-phẩm-biến-thể-sku--giá-bán-fr-prod-0204) | `AUTH_UNAUTHORIZED` (Xem [`DOC-ERR-02`](../12-error-and-status-codes/02-error-codes-catalog.md)) |
| `POST` | `/api/products` | `ProductsController.CreateProduct` | [`DOC-API-02 - 02-catalog-endpoints.md`](../05-api-contracts/02-catalog-endpoints.md#2-danh-mục-endpoints-sản-phẩm-biến-thể-sku--giá-bán-fr-prod-0204) | `VALIDATION_ERROR`, `BARCODE_DUPLICATE` |
| `PUT` | `/api/products/{pId}/variants/{vId}/prices` | `ProductsController.UpdatePrices` | [`DOC-API-02 - 02-catalog-endpoints.md`](../05-api-contracts/02-catalog-endpoints.md#2-danh-mục-endpoints-sản-phẩm-biến-thể-sku--giá-bán-fr-prod-0204) | `SKU_NOT_FOUND`, `PRICE_MISMATCH` |
| `GET` | `/api/categories` | `CategoriesController.GetCategories` | [`DOC-API-02 - 02-catalog-endpoints.md`](../05-api-contracts/02-catalog-endpoints.md#1-danh-mục-endpoints-cây-danh-mục--thương-hiệu-fr-prod-01) | `AUTH_UNAUTHORIZED` |
| `POST` | `/api/categories` | `CategoriesController.CreateCategory` | [`DOC-API-02 - 02-catalog-endpoints.md`](../05-api-contracts/02-catalog-endpoints.md#1-danh-mục-endpoints-cây-danh-mục--thương-hiệu-fr-prod-01) | `CATEGORY_CYCLIC_DEPENDENCY` |
| `GET` | `/api/brands` | `BrandsController.GetBrands` | [`DOC-API-02 - 02-catalog-endpoints.md`](../05-api-contracts/02-catalog-endpoints.md#1-danh-mục-endpoints-cây-danh-mục--thương-hiệu-fr-prod-01) | `AUTH_UNAUTHORIZED` |
| `POST` | `/api/brands` | `BrandsController.CreateBrand` | [`DOC-API-02 - 02-catalog-endpoints.md`](../05-api-contracts/02-catalog-endpoints.md#1-danh-mục-endpoints-cây-danh-mục--thương-hiệu-fr-prod-01) | `VALIDATION_ERROR` |

---

## 5. Quy Chuẩn Màu Sắc Trạng Thái Hiển Thị (Status Badges)

* Đang kinh doanh (`IsActive = true`): Sử dụng Huy hiệu Xanh lá `--badge--success` (`#e8f7f0` / `#0d543a`).
* Ngừng kinh doanh (`IsActive = false`): Sử dụng Huy hiệu Xám `--badge--neutral` (`#f0f2f5` / `#526075`).
* Hết hàng (`Available = 0`): Sử dụng Huy hiệu Đỏ `--badge--danger` (`#fff0f1` / `#8d3540`).
* Sắp hết hàng (`Available <= Threshold`): Sử dụng Huy hiệu Vàng cam `--badge--warning` (`#fff5dc` / `#7a4900`).
*(Chi tiết định nghĩa Design Tokens xem tại [`DOC-ERR-03`](../12-error-and-status-codes/03-color-system-tokens.md)).*
