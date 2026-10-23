# 02. ĐẶC TẢ YÊU CẦU CHỨC NĂNG (FUNCTIONAL REQUIREMENTS)

| Thông tin kiểm soát thư mục | Chi tiết |
| :--- | :--- |
| **Mã phân hệ (Module ID)** | `MOD-FR-02` |
| **Phiên bản hiện hành** | `v1.0.0` |
| **Ngày cập nhật gần nhất** | `2026-10-22` |
| **Nguồn đối chiếu gốc** | `De bai CNPM.pdf` — Mục 3.2.c & 3.2.g (Trang 1, 2, 3, 5, 6) |

---

## Danh mục 12 Module Yêu Cầu Chức Năng

| STT | Mã tài liệu | Tên file | Mã FR bao phủ | Phiên bản | Nội dung chính |
| :---: | :---: | :--- | :---: | :---: | :--- |
| **1** | `DOC-FR-01` | [`fr-01-auth-tenant.md`](./fr-01-auth-tenant.md) | `FR-AUTH-01..04` | `v1.0.0` | Đăng ký Tenant, Xác thực JWT, Phân quyền RBAC và Quản lý Chi nhánh/Kho. |
| **2** | `DOC-FR-02` | [`fr-02-product-catalog.md`](./fr-02-product-catalog.md) | `FR-PROD-01..04` | `v1.0.0` | Danh mục phân cấp, Thương hiệu, Sản phẩm/Biến thể SKU, Barcode EAN-13 và Bảng giá sỉ/lẻ. |
| **3** | `DOC-FR-03` | [`fr-03-inventory-ledger.md`](./fr-03-inventory-ledger.md) | `FR-INV-01..04` | `v1.0.0` | Sổ cái kho bất biến Append-Only, Nhập kho NCC, Chuyển kho 2 bước và Kiểm kê kho. |
| **4** | `DOC-FR-04` | [`fr-04-cost-wac.md`](./fr-04-cost-wac.md) | `FR-COST-01..02` | `v1.0.0` | Tính Giá vốn bình quân tự động (WAC) khi nhập kho và Chụp giá vốn lịch sử vào dòng đơn hàng. |
| **5** | `DOC-FR-05` | [`fr-05-stock-reservation.md`](./fr-05-stock-reservation.md) | `FR-RSE-01..03` | `v1.0.0` | Tồn khả dụng động (`available = on_hand - reserved`), Giữ chỗ, Trừ/Hoàn tồn và Khóa DB chống bán vượt. |
| **6** | `DOC-FR-06` | [`fr-06-omnichannel-orders.md`](./fr-06-omnichannel-orders.md) | `FR-ORD-01..03` | `v1.0.0` | Tiếp nhận đơn đa kênh vào Canonical Order Model, Order State Machine và Xử lý/In phiếu giao. |
| **7** | `DOC-FR-07` | [`fr-07-pos-checkout.md`](./fr-07-pos-checkout.md) | `FR-POS-01..04` | `v1.0.0` | Giao diện PWA POS, Kiểm soát tồn tại quầy, In hóa đơn nhiệt và Luồng Fast Checkout nguyên tử. |
| **8** | `DOC-FR-08` | [`fr-08-marketplace-sync.md`](./fr-08-marketplace-sync.md) | `FR-SIM-01` | `v1.0.0` | Công cụ giả lập gửi Webhook đơn hàng từ Shopee, TikTok Shop, Lazada. |
| **9** | `DOC-FR-09` | [`fr-09-realtime-signalr.md`](./fr-09-realtime-signalr.md) | `FR-SIM-02` | `v1.0.0` | Đẩy cảnh báo âm thanh/popup đơn hàng mới thời gian thực qua SignalR. |
| **10** | `DOC-FR-10` | [`fr-10-background-hangfire.md`](./fr-10-background-hangfire.md) | `FR-SIM-03` | `v1.0.0` | Xử lý tác vụ nền Hangfire: tự động hủy đơn giữ chỗ quá hạn và tổng hợp báo cáo hằng ngày. |
| **11** | `DOC-FR-11` | [`fr-11-reports-analytics.md`](./fr-11-reports-analytics.md) | `FR-REP-01..03` | `v1.0.0` | Báo cáo Doanh thu, COGS, Lợi nhuận gộp, Giá trị tồn kho và Cảnh báo tồn kho tối thiểu. |
| **12** | `DOC-FR-12` | [`fr-12-ai-forecasting.md`](./fr-12-ai-forecasting.md) | `FR-AI-01` | `v1.0.0` | Dự báo nhu cầu tiêu thụ bằng AI và Gợi ý kế hoạch nhập lại hàng hóa. |
