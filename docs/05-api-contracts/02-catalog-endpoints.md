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
| `PUT` | `/api/products/{pId}/variants/{vId}` | `Owner, Staff` | `FR-PROD-02..03` | Cập nhật biến thể SKU, bảo toàn kiểu dữ liệu JSON và `null` lồng nhau. Không đổi được giá, xem [mục 4](#4-bảng-giá-theo-thời-gian--phiếu-đổi-giá-oisms-47-u04). |
| `PUT` | `/api/products/{pId}/variants/{vId}/prices` | `Owner, Staff` | `FR-PROD-04` | **Đã đóng từ Upgrade 04:** luôn trả `410 DIRECT_PRICE_UPDATE_NOT_ALLOWED`. Đổi giá qua `POST /api/price-change-requests`. |
| `DELETE` | `/api/products/{pId}/variants/{vId}` | `Owner, Staff` | `FR-PROD-02` | Vô hiệu hóa mềm (`Soft delete: IsActive = false`) biến thể SKU. |

> Giá trong response lấy từ bảng giá tại thời điểm gọi: `retailPrice`, `wholesalePrice` là `null` khi chưa có giá hiệu lực. Xem [mục 4](#4-bảng-giá-theo-thời-gian--phiếu-đổi-giá-oisms-47-u04).

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
| `PUT` | `/api/products/{productId}/variants/{variantId}/units/{skuUnitId}` | `Owner, Staff` | Cập nhật quy cách, hệ số (tăng version và ghi lịch sử); không sửa hệ số đơn vị gốc (`400`). Không đổi được giá, xem [mục 4](#4-bảng-giá-theo-thời-gian--phiếu-đổi-giá-oisms-47-u04). |
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

---

## 4. Bảng Giá Theo Thời Gian & Phiếu Đổi Giá (`OISMS-47-U04`)

| Method | Endpoint | Quyền (RBAC) | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :--- |
| `GET` | `/api/price-lists` | `Owner, Staff, Cashier` | Danh sách bảng giá đang bật: `retail` (giá lẻ), `wholesale` (giá sỉ). |
| `POST` | `/api/price-change-requests` | `Owner, Staff` | Tạo phiếu đổi giá. Bắt buộc header `Idempotency-Key` hoặc `X-Request-Key`. Tạo mới trả `201`, gửi lại cùng khóa và cùng nội dung trả `200` với `isReplay = true`, khác nội dung trả `409 IDEMPOTENCY_PAYLOAD_MISMATCH`. Phiếu của `Owner` được duyệt ngay, phiếu của `Staff` ở trạng thái `Pending`. |
| `GET` | `/api/price-change-requests?status=&page=&pageSize=` | `Owner, Staff` | Danh sách phiếu, trả `{ items, totalCount, page, pageSize }`; `pageSize` tối đa 100. |
| `GET` | `/api/price-change-requests/{id}` | `Owner, Staff` | Chi tiết phiếu. |
| `POST` | `/api/price-change-requests/{id}/approve` | `Owner` | Duyệt phiếu `Pending`. `valid_from` = thời điểm muộn hơn giữa `requestedValidFrom` và lúc duyệt. Phiếu không còn `Pending` trả `409 INVALID_STATE_TRANSITION`. |
| `POST` | `/api/price-change-requests/{id}/reject` | `Owner` | Từ chối phiếu `Pending`, body `{ "note": "..." }` bắt buộc. |
| `POST` | `/api/sku-unit-prices/{id}/cancel` | `Owner` | Hủy giá chưa đến ngày áp dụng, body `{ "reason": "..." }` bắt buộc. Giá đã có hiệu lực trả `409 PRICE_ALREADY_EFFECTIVE`, đã hủy trả `409 INVALID_STATE_TRANSITION`. |
| `GET` | `/api/sku-unit-prices/effective?skuId=&priceListCode=&at=` | `Owner, Staff, Cashier` | Giá hiệu lực của SKU theo từng đơn vị, trả một danh sách. Bỏ trống `priceListCode` là `retail`, bỏ trống `at` là hiện tại. `Cashier` gửi `at` trả `403`. |
| `GET` | `/api/sku-unit-prices/history?skuId=&unitId=&priceListCode=` | `Owner, Staff` | Lịch sử giá, gồm dòng đã hủy và dòng tương lai; `validTo` suy ra từ dòng kế tiếp. |

**Body tạo phiếu:**

```json
{
  "reason": "Tăng giá theo nhà cung cấp",
  "requestedValidFrom": "2026-12-01T00:00:00+07:00",
  "items": [
    { "priceListCode": "retail", "skuId": "<guid>", "unitId": "<guid>", "newPrice": 150000 }
  ]
}
```

- Bỏ trống `requestedValidFrom` là áp dụng ngay khi duyệt; thời điểm trong quá khứ trả `400 PRICE_VALID_FROM_IN_PAST`.
- `reason` bắt buộc, tối đa 500 ký tự. Phiếu có 1–500 dòng, không trùng (bảng giá, SKU, đơn vị). `newPrice >= 0`, tối đa 2 chữ số lẻ.
- Đơn vị phải thuộc SKU, đang bật và cho phép bán, nếu không trả `422 UNIT_NOT_ALLOWED`. Mã bảng giá sai trả `404 PRICE_LIST_NOT_FOUND`. Trùng mốc hiệu lực trả `409 PRICE_VALID_FROM_CONFLICT`.
- Loại phiếu do server đặt: `Opening` cho giá đầu kỳ, `Initial` cho giá ban đầu, `Change` cho phiếu người dùng tạo. Client không gửi được.

**Quy tắc giá trên API sản phẩm và đơn vị:**

- **Tạo SKU hoặc đơn vị:** giá `> 0` sinh phiếu `Initial`. `Owner` có giá ngay; `Staff` chờ `Owner` duyệt và trong thời gian đó chưa bán được. Giá `0` nghĩa là chưa nhập, không sinh dòng giá.
- **Sửa SKU hoặc đơn vị:** các trường giá là tùy chọn và chỉ để đối chiếu. Không gửi, gửi trùng giá hiện hành, hoặc gửi `0` khi chưa có giá hiện hành thì bỏ qua; khác đi trả `409 DIRECT_PRICE_UPDATE_NOT_ALLOWED`. Khi đổi đơn vị gốc, giá gửi lên được đối chiếu với cả đơn vị gốc hiện tại và đơn vị đích.
- **Đổi đơn vị gốc:** không tự chép giá. Đơn vị mới chưa có giá thì chưa bán được.
- **Response:** `retailPrice`, `wholesalePrice`, `salePrice` có thể là `null`. Biến thể có thêm `baseUnitPriceMissing`, `true` khi đơn vị gốc chưa có giá lẻ hiệu lực. `salePrice` trong `units[]` của API sản phẩm luôn `null` với đơn vị gốc; API POS trả giá lẻ của mọi đơn vị.
