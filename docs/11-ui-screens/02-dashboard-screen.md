# 02. ĐẶC TẢ MÀN HÌNH DASHBOARD TỔNG QUAN (OPERATIONAL DASHBOARD)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SCR-02` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực** | `2026-10-25` |
| **Đường dẫn URL Routes** | `/admin`, `/` |
| **Quyền truy cập (RBAC)** | `Owner`, `Staff` (Chặn `Cashier`) |
| **Source code Frontend** | [`DashboardPage.tsx`](file:///d:/OISMS/src/frontend/admin-dashboard/src/pages/DashboardPage.tsx), [`StatCard`](file:///d:/OISMS/src/frontend/admin-dashboard/src/components/ui.tsx#L29-L31) |
| **Controllers Backend** | [`OrdersController.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/OrdersController.cs), [`InventoryController.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/InventoryController.cs) |

---

## 1. Mục Tiêu & Luồng Nghiệp Vụ

Màn hình **Dashboard Tổng quan** là trung tâm chỉ huy điều hành kinh doanh của doanh nghiệp trong ngày:
* Cung cấp các thẻ chỉ số đo lường hiệu suất then chốt (KPI Cards): Doanh thu bán hàng, Lợi nhuận gộp & Tỷ suất lãi gộp (Gross Margin %), Tổng giá trị vốn tồn kho toàn hệ thống (tính chuẩn xác theo $WAC \times OnHand$), và Số đơn hàng đang xử lý.
* Giám sát danh sách các mặt hàng rơi vào vùng nguy hiểm tồn kho (`Available <= MinStockThreshold`) để kịp thời lập phiếu đặt hàng nhà cung cấp.
* Theo dõi danh sách đơn hàng đang ở trạng thái Giữ chỗ (`Reserved`) cần duyệt hoặc sắp hết hạn.

---

## 2. Bố Cục Giao Diện Wireframe (ASCII Wireframe)

```text
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ BREADCRUMB: Tổng quan vận hành OISM                                                            │
│ Chào mừng trở lại, Nguyễn Văn Quản Trị                                  [ Làm mới ] [ Tạo đơn] │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ ┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐            │
│ │ DOANH THU THỰC   │ │ LÃI GỘP THỰC TẾ  │ │ GIÁ TRỊ TỒN KHO  │ │ ĐƠN ĐANG GIỮ CHỖ │            │
│ │ 128,450,000 đ    │ │ 44,950,000 đ     │ │ 845,200,000 đ    │ │ 14 đơn hàng      │            │
│ │ +12.5% so hôm qua│ │ Tỷ suất: 35.0%   │ │ Định giá theo WAC│ │ 3 đơn sắp hết hạn│            │
│ └──────────────────┘ └──────────────────┘ └──────────────────┘ └──────────────────┘            │
├──────────────────────────────────────────────────┬─────────────────────────────────────────────┤
│ 📊 BIỂU ĐỒ DOANH THU THEO GIỜ                    │ ⚠️ CẢNH BÁO TỒN KHO DƯỚI ĐỊNH MỨC (LOW STOCK)│
│ [ ▂▃▅▆▇█▇▆▅▄▃  - Cập nhật thời gian thực ]       │ 1. Áo Polo Trắng - L   (Còn 2 / Định mức 10)│
│                                                  │ 2. Giày Sneaker - 41   (Còn 0 / HẾT HÀNG)   │
│ Doanh số tập trung cao điểm từ 11:00 - 14:00     │ 3. Quần Kaki Be - 30   (Còn 4 / Định mức 15)│
│                                                  │ [ Xem toàn bộ cảnh báo kho -> ]             │
├──────────────────────────────────────────────────┴─────────────────────────────────────────────┤
│ 🛒 DANH SÁCH ĐƠN HÀNG MỚI ĐANG GIỮ CHỖ TỒN KHO (PENDING / RESERVED)                             │
│ Mã đơn       | Kênh bán | Khách hàng       | Tổng tiền   | Hết hạn giữ chỗ | Thao tác      │
│ ORD-20261025 | POS      | Khách lẻ tại quầy| 750,000đ    | Còn 12 phút     | [ Duyệt ngay ]│
│ ORD-20261024 | Shopee   | tranvanb_hcm     | 1,200,000đ  | Còn 8 phút      | [ Chi tiết ]  │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Danh Sách Trường Dữ Liệu & Chỉ Số Tính Toán

| Thẻ chỉ số (Metric) | Công thức / Nguồn tính toán | Ý nghĩa điều hành | Màu hiển thị áp dụng |
| :--- | :--- | :--- | :--- |
| **Doanh thu thuần** | $\sum \text{Order.Amount}$ (với các đơn khác `Cancelled`) | Doanh số bán thực tế phát sinh | `--brand: #1769e0` (Xem [`DOC-ERR-03`](../12-error-and-status-codes/03-color-system-tokens.md)) |
| **Lợi nhuận gộp** | $\text{DoanhThu} - \sum \text{Order.COGS}$ | Lãi thực sau khi trừ giá vốn bình quân | `--success: #16845b` |
| **Tỷ suất lãi gộp** | $(\text{LợiNhuậnGộp} / \text{DoanhThu}) \times 100\%$ | Hiệu quả sinh lời của danh mục hàng | `--success-text: #0d543a` |
| **Giá trị tồn kho** | $\sum (\text{OnHand}_{\text{branch}} \times \text{AverageCost}_{\text{branch}})$ | Tổng tài sản vốn đang lưu tại các kho | `--ink: #172033` |
| **Cảnh báo kho** | $\text{OnHand} - \text{Reserved} \le \text{Threshold}$ | Hàng tồn khả dụng đã chạm ngưỡng an toàn | Nền `--warning-soft: #fff5dc`, Viền `--warning: #b76e00` |

---

## 4. Bảng Hàm API Backend & Tài Liệu Tham Chiếu

| Method | Endpoint Backend | Hàm xử lý Controller / Service | Tài liệu Hợp đồng API tham chiếu | Mã lỗi liên quan |
| :---: | :--- | :--- | :--- | :--- |
| `GET` | `/api/orders` | `OrdersController.GetOrders` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#1-danh-mục-endpoints-đơn-hàng-đa-kênh--webhook-fr-ord-0103-fr-sim-01) | `AUTH_UNAUTHORIZED`, `DATABASE_CONNECTION_ERROR` (Xem [`DOC-ERR-02`](../12-error-and-status-codes/02-error-codes-catalog.md)) |
| `GET` | `/api/inventory/balances` | `InventoryController.GetBalances` | [`DOC-API-03 - 03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md#1-danh-mục-endpoints-sổ-cái-tồn-kho--phiếu-nhập-fr-inv-0102-fr-cost-01) | `NO_ACTIVE_WAREHOUSE`, `DATABASE_CONNECTION_ERROR` |
| `GET` | `/api/reports/revenue-profit` | `OrdersController.GetRevenueProfitReport` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#2-danh-mục-endpoints-báo-cáo--dự-báo-ai-fr-rep-0103-fr-ai-01) | `FORBIDDEN_ACCESS` (Chỉ dành cho Owner) |
| `GET` | `/api/reports/stock-alerts` | `InventoryController.GetStockAlerts` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#2-danh-mục-endpoints-báo-cáo--dự-báo-ai-fr-rep-0103-fr-ai-01) | `AUTH_UNAUTHORIZED` |
