# 05. Đặc Tả Hợp Đồng API Bán Hàng Tại Quầy POS (POS Counter Endpoints)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-API-05` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-POS-01..04`, `NFR-PERF-01` (Trang 3) |
| **Phạm vi áp dụng** | `PosController` & Ứng dụng `src/frontend/pos-app` |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả các Endpoints tìm kiếm nhanh, quét Barcode và thanh toán nguyên tử tại quầy POS. | `INIT-API-05` |

---

## 1. Danh Mục Endpoints Quầy Bán Hàng POS (`/api/pos`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `GET` | `/api/pos/search` | `Owner, Staff, Cashier` | `FR-POS-01`, `FR-POS-02` | Tìm kiếm nhanh sản phẩm/SKU theo từ khóa (`keyword`) và chi nhánh (`branchId`). Trả về thông tin SKU, Giá bán lẻ/sỉ và số lượng tồn khả dụng thực tế (`Available = Math.Max(0, OnHand - Reserved)`). Độ trễ `p95 < 200ms`. |
| `GET` | `/api/pos/barcode/{code}` | `Owner, Staff, Cashier` | `FR-POS-01`, `FR-POS-02` | Tra cứu chính xác SKU theo mã vạch `EAN-13`/`Code128` kèm tồn khả dụng tại chi nhánh. Độ trễ `p95 < 200ms`. |
| `POST` | `/api/pos/checkout` | `Owner, Staff, Cashier` | `FR-POS-03`, `FR-POS-04`, `FR-COST-02` | Thực thi thanh toán nhanh tại quầy POS: Chạy chuỗi `Reserve -> Confirm -> Deduct` + Ghi Sổ cái `OUT` + Chụp `OrderItem.CostPrice` trong **1 giao dịch CSDL nguyên tử duy nhất**. |
