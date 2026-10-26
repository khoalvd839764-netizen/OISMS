# 05. Đặc Tả Hợp Đồng API Bán Hàng Tại Quầy POS (POS Counter Endpoints)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-API-05` |
| **Phiên bản (Version)** | `v1.0.2` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-24` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-POS-01..04`, `NFR-PERF-01` (Trang 3) |
| **Hợp đồng chi tiết Sprint 3** | [`../08-sprints/sprint-3/01-api-contracts-sprint-3.md`](../08-sprints/sprint-3/01-api-contracts-sprint-3.md) |
| **Phạm vi áp dụng** | `PosCatalogController`, `OrdersController` & Ứng dụng `src/frontend/pos-app`, `src/frontend/admin-dashboard` |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.2` | `2026-10-24` | Đăng Khoa (Backend 1) | Tách biệt hoàn toàn dữ liệu phản hồi quầy thu ngân (`PosReceiptDto`), bảo đảm không để lộ `CostPrice` hay `COGS` cho vai trò `Cashier`. Cập nhật link tương đối. | `SEC-POS-RECEIPT` |
| `v1.0.1` | `2026-10-24` | Đăng Khoa (Backend 1) | Thống nhất cơ chế Dual-Route Mapping cho Checkout POS: Hỗ trợ đồng thời `POST /api/pos/checkout` (chuẩn canonical) và `POST /api/orders/checkout` (alias tương thích ngược cho cả `pos-app` và `admin-dashboard`). Liên kết tới `DOC-CTR-S3-01`. | `SYNC-CHECKOUT-ROUTING` |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả các Endpoints tìm kiếm nhanh, quét Barcode và thanh toán nguyên tử tại quầy POS. | `INIT-API-05` |

---

## 1. Danh Mục Endpoints Quầy Bán Hàng POS (`/api/pos`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `GET` | `/api/pos/search` | `Owner, Staff, Cashier` | `FR-POS-01`, `FR-POS-02` | Tìm kiếm nhanh sản phẩm/SKU theo từ khóa (`keyword`) và chi nhánh (`branchId`). Trả về thông tin SKU, Giá bán lẻ/sỉ và số lượng tồn khả dụng thực tế (`Available = Math.Max(0, OnHand - Reserved)`). Độ trễ `p95 < 200ms`. |
| `GET` | `/api/pos/barcode/{code}` | `Owner, Staff, Cashier` | `FR-POS-01`, `FR-POS-02` | Tra cứu chính xác SKU theo mã vạch `EAN-13`/`Code128` kèm tồn khả dụng tại chi nhánh. Độ trễ `p95 < 200ms`. |
| `POST` | `/api/pos/checkout`<br/>*(Alias: `/api/orders/checkout`)* | `Owner, Staff, Cashier` | `FR-POS-03`, `FR-POS-04`, `FR-COST-02` | **Thanh toán nhanh tại quầy POS:** Chạy chuỗi `Reserve -> Confirm -> Deduct` + Ghi Sổ cái `OUT` + Chụp `OrderItem.CostPrice` (WAC snapshot) trong **1 giao dịch CSDL nguyên tử duy nhất**. Hỗ trợ cả 2 đường dẫn tương thích 100%. Phản hồi trả về `PosReceiptDto` chuẩn bán lẻ, **tuyệt đối không để lộ giá vốn (CostPrice/COGS)**. Chi tiết xem [`DOC-CTR-S3-01`](../08-sprints/sprint-3/01-api-contracts-sprint-3.md). |
