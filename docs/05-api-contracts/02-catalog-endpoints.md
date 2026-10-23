# 02. Đặc Tả Hợp Đồng API Danh Mục, Sản Phẩm, SKU & Bảng Giá (Catalog & SKU Endpoints)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-API-02` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-PROD-01..04` (Trang 1, 2, 5) |
| **Phạm vi áp dụng** | `CategoriesController`, `BrandsController`, `ProductsController` |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả danh sách Endpoints quản lý cây danh mục, thương hiệu, sản phẩm, biến thể SKU, barcode và bảng giá. | `INIT-API-02` |

---

## 1. Danh Mục Endpoints Cây Danh Mục & Thương Hiệu (`FR-PROD-01`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `GET` | `/api/categories` | `Owner, Staff, Cashier` | `FR-PROD-01` | Lấy danh sách và cấu trúc cây danh mục phân cấp (`Parent/Children`). |
| `POST` | `/api/categories` | `Owner, Staff` | `FR-PROD-01` | Tạo danh mục mới, kiểm tra `ParentId` tồn tại trong cùng Tenant. |
| `PUT` | `/api/categories/{id}` | `Owner, Staff` | `FR-PROD-01` | Cập nhật danh mục; bảo vệ bằng `Advisory Lock` chống tạo chu trình vòng lặp (`400 Bad Request`). |
| `DELETE` | `/api/categories/{id}` | `Owner, Staff` | `FR-PROD-01` | Xóa/vô hiệu hóa danh mục. |
| `GET/POST/PUT/DELETE` | `/api/brands` | `Owner, Staff` | `FR-PROD-01` | Quản lý danh mục Thương hiệu (`Brand`) theo từng Tenant. |

---

## 2. Danh Mục Endpoints Sản Phẩm, Biến Thể SKU, Barcode & Giá (`FR-PROD-02..04`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `GET` | `/api/products` | `Owner, Staff, Cashier` | `FR-PROD-02` | Lấy danh sách sản phẩm kèm các biến thể SKU, mã vạch và giá bán. |
| `GET` | `/api/products/{id}` | `Owner, Staff, Cashier` | `FR-PROD-02` | Xem chi tiết sản phẩm và toàn bộ biến thể SKU. |
| `POST` | `/api/products` | `Owner, Staff` | `FR-PROD-02..04` | Tạo sản phẩm mới kèm danh sách biến thể SKU, tự sinh Barcode `EAN-13` nếu để trống. |
| `PUT` | `/api/products/{id}` | `Owner, Staff` | `FR-PROD-02` | Cập nhật thông tin sản phẩm gốc. |
| `POST` | `/api/products/{pId}/variants` | `Owner, Staff` | `FR-PROD-02..03` | Thêm biến thể SKU mới cho sản phẩm; kiểm tra trùng `SkuCode`, `Barcode` và tổ hợp thuộc tính JSON (`409 Conflict`). |
| `PUT` | `/api/products/{pId}/variants/{vId}` | `Owner, Staff` | `FR-PROD-02..03` | Cập nhật biến thể SKU, bảo toàn kiểu dữ liệu JSON và `null` lồng nhau. |
| `PUT` | `/api/products/{pId}/variants/{vId}/prices` | `Owner, Staff` | `FR-PROD-04` | Cập nhật Giá bán lẻ (`RetailPrice`) và Giá bán sỉ (`WholesalePrice`) độc lập với giá vốn WAC. |
| `DELETE` | `/api/products/{pId}/variants/{vId}` | `Owner, Staff` | `FR-PROD-02` | Vô hiệu hóa mềm (`Soft delete: IsActive = false`) biến thể SKU. |
