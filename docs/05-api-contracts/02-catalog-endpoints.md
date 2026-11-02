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

> **Thay đổi dự kiến ở Upgrade 04 (chưa triển khai):** `PUT .../prices` sẽ bị đóng và thay bằng API phiếu đổi giá; API sửa biến thể và đơn vị không còn đổi được giá. Contract chi tiết sẽ được bổ sung khi triển khai. Xem [tài liệu 04](../Upgrade-recommended/04-bang-gia-theo-thoi-gian.md#thay-đổi-cần-làm).

---

## 3. Danh Mục Endpoints Đơn Vị Tính & Quy Cách SKU (`OISMS-47-U05`)

| Method | Endpoint | Quyền (RBAC) | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :--- |
| `GET` | `/api/units` | `Owner, Staff, Cashier` | Lấy danh sách đơn vị tính của Tenant; tự bổ sung các đơn vị thuộc bộ mặc định còn thiếu. |
| `GET` | `/api/units/{id}` | `Owner, Staff, Cashier` | Xem thông tin chi tiết một đơn vị tính theo Id. |
| `POST` | `/api/units` | `Owner, Staff` | Tạo mới đơn vị tính; kiểm tra trùng mã `code` trong cùng Tenant (`409 Conflict`). |
| `PUT` | `/api/units/{id}` | `Owner, Staff` | Cập nhật tên, ký hiệu, số lẻ, trạng thái; không được đổi `code` và `measure_group` nếu đã được SKU sử dụng (`409 UNIT_IN_USE`). |
| `GET` | `/api/products/{productId}/variants/{variantId}/units` | `Owner, Staff, Cashier` | Lấy danh sách các đơn vị tính và quy cách đã cấu hình cho biến thể SKU. |
| `POST` | `/api/products/{productId}/variants/{variantId}/units` | `Owner, Staff` | Thêm đơn vị quy đổi cho SKU; kiểm tra tương thích `measure_group` (`422`), trùng mã vạch trong tenant (`409`), hệ số > 0, tối đa 12 chữ số nguyên và 6 số lẻ (`400`). Đặt mặc định mới thì tự bỏ mặc định cũ. |
| `PUT` | `/api/products/{productId}/variants/{variantId}/units/{skuUnitId}` | `Owner, Staff` | Cập nhật quy cách, giá bán, hệ số (tăng version và ghi lịch sử); không sửa hệ số đơn vị gốc (`400`). |
| `DELETE` | `/api/products/{productId}/variants/{variantId}/units/{skuUnitId}` | `Owner, Staff` | Tắt mềm quy cách đơn vị cho SKU (`IsActive = false`); không được tắt đơn vị gốc (`400`). Tắt đơn vị đang mặc định thì mặc định chuyển về dòng gốc. |
| `GET` | `/api/products/{productId}/variants/{variantId}/units/{skuUnitId}/history` | `Owner, Staff` | Xem lịch sử thay đổi hệ số quy đổi của đơn vị quy cách SKU. |

**Quy tắc đơn vị trên API sản phẩm:**

- `POST /api/products`, `POST .../variants` nhận `baseUnitId` hoặc `unit` (mã đơn vị); không gửi thì dùng `piece`. `baseUnitId` không thuộc tenant trả `404 UNIT_NOT_FOUND`. SKU luôn có một dòng `sku_units` gốc hệ số 1.
- `PUT .../variants/{vId}` nhận `baseUnitId`; SKU đã có giao dịch, số dư, hoặc còn đơn vị quy đổi đang bật trả `409 BASE_UNIT_LOCKED`.
- Response biến thể có thêm `baseUnitId`, `baseUnitCode`, `baseUnitName`, `units[]`; biến thể trả về theo thứ tự tạo.
- `changed_by`, `updated_by` lấy từ JWT, client không gửi được.
- Bật lại đơn vị đã tắt (`isActive = true`) phải gửi kèm `factorToBase`, nếu không trả `400 INVALID_UNIT_FACTOR`. Đơn vị đang tắt không được đặt làm mặc định (`400 INVALID_OPERATION`).
- Đơn vị tắt trong danh mục không thêm được vào SKU, không làm đơn vị gốc được, không bán, nhập được (`422 UNIT_NOT_ALLOWED`). Đơn vị đang là gốc của SKU không tắt được (`409 UNIT_IN_USE`).
- Đơn hàng, POS, phiếu nhập nhận `unitId`: xem `03-inventory-endpoints.md`, `04-order-endpoints.md`, `05-pos-endpoints.md`.
