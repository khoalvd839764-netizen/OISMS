# 03. Đặc Tả Hợp Đồng API Tồn Kho, Sổ Cái & Nhập/Chuyển/Kiểm Kê (Inventory & Ledger Endpoints)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-API-03` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-INV-01..04`, `FR-COST-01`, `FR-RSE-01..03` (Trang 2, 3, 5) |
| **Phạm vi áp dụng** | `InventoryController`, `GoodsReceiptsController`, `StockTransfersController`, `StocktakesController` |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả danh sách Endpoints quản lý Sổ cái kho bất biến, Phiếu nhập kho, Giữ chỗ, Chuyển kho và Kiểm kê. | `INIT-API-03` |

---

## 1. Danh Mục Endpoints Sổ Cái, Tồn Kho & Phiếu Nhập (`FR-INV-01..02`, `FR-COST-01`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `GET` | `/api/inventory/balances` | `Owner, Staff` | `FR-INV-01`, `FR-RSE-01` | Tra cứu số dư tồn kho chi tiết (`OnHand`, `Reserved`, `Available = OnHand - Reserved`, `UnitCost` WAC) theo Chi nhánh/Kho/SKU. |
| `GET` | `/api/inventory/ledger` | `Owner, Staff` | `FR-INV-01` | Tra cứu lịch sử Sổ cái kho chỉ ghi thêm (`InventoryTransaction`), hỗ trợ lọc theo Chi nhánh, Kho, SKU, Loại giao dịch (`IN`/`OUT`) và Thời gian. |
| `POST` | `/api/inventory/goods-receipts` | `Owner, Staff` | `FR-INV-02` | Tạo Phiếu nhập hàng từ Nhà cung cấp ở trạng thái nháp (`Draft`) gồm nhiều dòng SKU. |
| `GET` | `/api/inventory/goods-receipts` | `Owner, Staff` | `FR-INV-02` | Lấy danh sách và chi tiết các phiếu nhập kho. |
| `POST` | `/api/inventory/goods-receipts/{id}/confirm` | `Owner, Staff` | `FR-INV-02`, `FR-COST-01` | Xác nhận phiếu nhập nguyên tử: Ghi Sổ cái `IN` + Tăng `OnHand` + Tính lại giá vốn bình quân `WAC`. Chặn xác nhận lặp (`409 Conflict`). |
| `POST` | `/api/inventory/reserve` | `Owner, Staff, Cashier` | `FR-RSE-02` | Giữ chỗ tồn kho chống bán vượt với khóa dòng CSDL và `Idempotency-Key` / `RequestKey`. |

---

## 2. Danh Mục Endpoints Chuyển Kho 2 Bước & Kiểm Kê (`FR-INV-03..04`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `POST` | `/api/inventory/transfers` | `Owner, Staff` | `FR-INV-03` | Tạo phiếu chuyển kho giữa Chi nhánh A và Chi nhánh B. |
| `POST` | `/api/inventory/transfers/{id}/dispatch` | `Owner, Staff` | `FR-INV-03` | Bước 1 — Xuất kho vận chuyển (`Out-bound transit`): Trừ tồn Kho A và ghi Sổ cái `OUT`. |
| `POST` | `/api/inventory/transfers/{id}/receive` | `Owner, Staff` | `FR-INV-03` | Bước 2 — Xác nhận nhập tại Kho B (`In-bound confirmation`): Tăng tồn Kho B và ghi Sổ cái `IN`. |
| `POST` | `/api/inventory/stocktakes` | `Owner, Staff` | `FR-INV-04` | Tạo phiên kiểm kê vật lý, đối chiếu tồn phần mềm và sinh bút toán điều chỉnh Sổ cái (`Adjustment Ledger entries`). |
