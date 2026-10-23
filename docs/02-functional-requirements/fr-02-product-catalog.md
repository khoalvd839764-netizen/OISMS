# FR-02: Quản Lý Danh Mục, Sản Phẩm, Biến Thể SKU, Mã Vạch & Bảng Giá (Product & Variant Management)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-02` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-PROD-01..04` (Trang 1, 2) |
| **Gói công việc (Task Package)** | Task Package 2 — Product Catalog & Append-Only Inventory Ledger |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả chi tiết nhóm yêu cầu chức năng `FR-PROD-01` đến `FR-PROD-04`. | `INIT-FR-02` |

---

## 1. Bảng Đặc Tả Chi Tiết Yêu Cầu (`FR-PROD`)

| Mã yêu cầu | Tên yêu cầu | Mô tả nghiệp vụ & Quy tắc kỹ thuật | Tiêu chí nghiệm thu (Acceptance Criteria) |
| :---: | :--- | :--- | :--- |
| **`FR-PROD-01`** | **Category & Brand Management** | Quản lý cây danh mục sản phẩm phân cấp nhiều tầng (`ParentId`) và danh mục thương hiệu (`Brand`). | - Hỗ trợ truy vấn cây danh mục cha - con.<br/>- Chặn tuyệt đối lỗi tạo vòng lặp chu trình (`A -> B -> A`) bằng `PostgreSQL Advisory Lock` và kiểm tra tổ tiên (`400 Bad Request`). |
| **`FR-PROD-02`** | **Product & Variant/SKU Management** | Hỗ trợ sản phẩm đơn giản và sản phẩm có biến thể (Màu sắc, Kích thước, Thuộc tính JSON tùy chỉnh). Mỗi biến thể được gán mã `SKU` duy nhất trong cùng một Tenant. | - Ràng buộc Unique `(TenantId, SkuCode)` hoạt động chính xác (2 Tenant khác nhau được phép dùng trùng mã SKU).<br/>- Chặn tạo/cập nhật trùng tổ hợp thuộc tính biến thể trong cùng 1 sản phẩm (`409 Conflict`). |
| **`FR-PROD-03`** | **Barcode Management** | Tự động sinh hoặc cho phép nhập thủ công mã vạch chuẩn `EAN-13` / `Code128` cho từng SKU để phục vụ quét bán hàng POS và kiểm kê kho. | - Tự động sinh mã `EAN-13` hợp lệ có cơ chế retry chống trùng.<br/>- Ràng buộc Unique `(TenantId, Barcode)` trả về `409 Conflict` rõ ràng khi trùng. |
| **`FR-PROD-04`** | **Listed Price Management** | Thiết lập và cập nhật Giá bán lẻ (`RetailPrice`) và Giá bán sỉ (`WholesalePrice`) cho từng SKU, tách biệt hoàn toàn với giá vốn hàng bán (`COGS`). | - Chặn nhập giá âm (`400 Bad Request`).<br/>- Thay đổi giá bán niêm yết không làm ảnh hưởng đến cột giá vốn bình quân (`UnitCost`). |
