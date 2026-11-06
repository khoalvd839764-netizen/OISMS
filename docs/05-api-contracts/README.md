# 05. ĐẶC TẢ HỢP ĐỒNG GIAO TIẾP API (API SPECIFICATIONS & CONTRACTS)

| Thông tin kiểm soát thư mục | Chi tiết |
| :--- | :--- |
| **Mã phân hệ (Module ID)** | `MOD-API-05` |
| **Phiên bản hiện hành** | `v1.1.0` |
| **Ngày cập nhật gần nhất** | `2026-11-06` |
| **Nguồn đối chiếu gốc** | `De bai CNPM.pdf` — Mục 3.2.c, 3.2.e, 3.2.f (Trang 1, 2, 3, 5) |

---

## Danh mục Tài liệu trong Phân hệ `05-api-contracts/`

| STT | Mã tài liệu | Tên file | Phiên bản | Nội dung chính |
| :---: | :---: | :--- | :---: | :--- |
| **1** | `DOC-API-01` | [`01-auth-endpoints.md`](./01-auth-endpoints.md) | `v1.x` | Đăng ký Tenant, Đăng nhập/Làm mới JWT, Chi nhánh, Kho, Nhà cung cấp và Người dùng. |
| **2** | `DOC-API-02` | [`02-catalog-endpoints.md`](./02-catalog-endpoints.md) | `v1.x` | Cây danh mục, Thương hiệu, Sản phẩm, Biến thể SKU, Đơn vị tính và Bảng giá theo thời gian. |
| **3** | `DOC-API-03` | [`03-inventory-endpoints.md`](./03-inventory-endpoints.md) | `v1.x` | Tồn kho, Sổ cái, Phiếu mua, Phiếu nhập, Giữ chỗ, Chuyển kho 2 bước, Lịch/Kỳ kiểm kê và duyệt chênh lệch. |
| **4** | `DOC-API-04` | [`04-order-endpoints.md`](./04-order-endpoints.md) | `v1.x` | Đơn hàng đa kênh, Webhook giả lập, Hóa đơn bán, Báo cáo, Tìm kiếm và Gợi ý AI. |
| **5** | `DOC-API-05` | [`05-pos-endpoints.md`](./05-pos-endpoints.md) | `v1.x` | Các API Tìm kiếm nhanh, Quét Barcode và Thanh toán nguyên tử tại quầy POS. |

> Nguồn sự thật là code: Swagger tại `http://localhost:5000/swagger` (chỉ Development) và controller trong `src/backend/src/OISM.WebApi/Controllers/`. Mã lỗi xem [`12-error-and-status-codes/`](../12-error-and-status-codes/README.md).
