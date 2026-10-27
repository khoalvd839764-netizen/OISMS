# 08. ĐẶC TẢ MÀN HÌNH BÁO CÁO DOANH THU & LÃI GỘP (FINANCIAL & PROFIT REPORTS)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SCR-08` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực** | `2026-10-25` |
| **Đường dẫn URL Routes** | `/admin/reports`, `/reports` |
| **Quyền truy cập (RBAC)** | **Chỉ dành cho Chủ sở hữu (`Owner`)** |
| **Source code Frontend** | [`ReportsPage.tsx`](file:///d:/OISMS/src/frontend/admin-dashboard/src/pages/ReportsPage.tsx) |
| **Controllers Backend** | [`OrdersController.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/OrdersController.cs) |

---

## 1. Mục Tiêu & Luồng Nghiệp Vụ

Màn hình **Báo Cáo Doanh Thu & Quản Trị Lãi Gộp (P&L Reports)** là phân hệ độc quyền dành cho Chủ doanh nghiệp (`Owner`):
1. **Minh bạch hóa Lợi nhuận gộp (P&L Analysis):** Đối chiếu trực tiếp Doanh thu bán hàng thực tế với Tổng giá vốn hàng bán (`COGS`), được chụp chính xác theo giá vốn bình quân gia quyền ($WAC$) tại thời điểm xác nhận từng đơn hàng:
   $$\text{Lợi nhuận gộp (Gross Profit)} = \text{Doanh thu thuần} - \text{Tổng giá vốn (COGS)}$$
   $$\text{Tỷ suất lợi nhuận gộp (Gross Margin)} = \left(\frac{\text{Lợi nhuận gộp}}{\text{Doanh thu thuần}}\right) \times 100\%$$
2. **Hiệu quả mặt hàng (SKU Performance Ranking):** Phân tích danh sách biến thể SKU bán chạy nhất (Top Sellers) và các mặt hàng ứ đọng vốn (Dead Stock / Slow-moving Inventory).
3. **Dự báo nhu cầu & Gợi ý nhập hàng AI (AI Reorder Recommendations):** Áp dụng mô hình dự báo tiêu thụ theo chuỗi thời gian để gợi ý số lượng đặt hàng tối ưu cho từng nhà cung cấp.

---

## 2. Bố Cục Giao Diện Wireframe (ASCII Wireframe)

```text
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ BREADCRUMB: Quản trị tài chính & Hiệu quả kinh doanh                                           │
│ Báo Cáo Doanh Thu & Lợi Nhuận Gộp (P&L)              [ Chọn kỳ: 30 ngày qua ▼ ] [ Xuất Excel ] │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ ┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐            │
│ │ TỔNG DOANH THU   │ │ TỔNG GIÁ VỐN     │ │ LỢI NHUẬN GỘP    │ │ TỶ SUẤT LÃI GỘP  │            │
│ │ 345,800,000 đ    │ │ 210,400,000 đ    │ │ 135,400,000 đ    │ │ 39.2%            │            │
│ │ Kênh: POS + TMĐT │ │ Snapshot WAC     │ │ Sau trừ giá vốn  │ │ 🟢 Đạt mục tiêu  │            │
│ └──────────────────┘ └──────────────────┘ └──────────────────┘ └──────────────────┘            │
├──────────────────────────────────────────────────┬─────────────────────────────────────────────┤
│ 🤖 GỢI Ý TÁI ĐẶT HÀNG THÔNG MINH (AI REORDER)     │ ⚠️ CẢNH BÁO TỒN ĐỌNG VỐN (DEAD STOCK)       │
│ • Áo Polo Trắng - L: Tốc độ bán 15 cái/ngày      │ 1. Giày Sneaker Da - 43: 0 đơn trong 45 ngày│
│   👉 Đề xuất nhập thêm: 150 cái từ NCC May Mặc Á │   (Giá trị vốn tồn: 12,500,000 đ)           │
│ • Quần Jean Slim - 31: Dự kiến hết hàng trong 2d │ 2. Áo Len Cổ Lọ - Đen: Tốc độ luân chuyển 0 │
│   👉 Đề xuất nhập thêm: 80 cái                   │   👉 Khuyến nghị: Giảm giá 20% xả hàng      │
├──────────────────────────────────────────────────┴─────────────────────────────────────────────┤
│ BẢNG PHÂN TÍCH HIỆU QUẢ TỪNG BIẾN THỂ SKU (SKU PERFORMANCE DATA TABLE):                        │
│ Mã SKU        | Tên sản phẩm / Biến thể  | Đã bán | Doanh thu   | Giá vốn WAC | Lãi gộp     | Tỷ suất│
│ ──────────────┼──────────────────────────┼────────┼─────────────┼─────────────┼─────────────┼─────── │
│ AO-POLO-TR-M  | Áo Polo Cotton (Trắng, M)| 120 cái| 30,000,000 đ| 14,400,000 đ| 15,600,000 đ| 52.0%  │
│ QN-JEAN-X-31  | Quần Jean Slim (Xanh, 31)| 65 cái | 31,200,000 đ| 13,650,000 đ| 17,550,000 đ| 56.2%  │
│ GY-SNK-D-42   | Giày Sneaker (Đen, 42)   | 18 đôi | 15,300,000 đ|  8,100,000 đ|  7,200,000 đ| 47.1%  │
└────────────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Bảng Hàm API Backend & Tài Liệu Tham Chiếu

| Method | Endpoint Backend | Hàm xử lý Controller / Service | Tài liệu Hợp đồng API tham chiếu | Mã lỗi liên quan |
| :---: | :--- | :--- | :--- | :--- |
| `GET` | `/api/reports/revenue-profit` | `OrdersController.GetRevenueProfitReport` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#2-danh-mục-endpoints-báo-cáo--dự-báo-ai-fr-rep-0103-fr-ai-01) | `FORBIDDEN_ACCESS` (Chặn nhân viên không phải Owner, xem [`DOC-ERR-02`](../12-error-and-status-codes/02-error-codes-catalog.md)) |
| `GET` | `/api/reports/inventory-velocity` | `OrdersController.GetInventoryVelocityReport` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#2-danh-mục-endpoints-báo-cáo--dự-báo-ai-fr-rep-0103-fr-ai-01) | `AUTH_UNAUTHORIZED` |
| `GET` | `/api/reports/stock-alerts` | `InventoryController.GetStockAlerts` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#2-danh-mục-endpoints-báo-cáo--dự-báo-ai-fr-rep-0103-fr-ai-01) | `AUTH_UNAUTHORIZED` |
| `GET` | `/api/ai/reorder-recommendations` | `OrdersController.GetAiReorderRecommendations` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#2-danh-mục-endpoints-báo-cáo--dự-báo-ai-fr-rep-0103-fr-ai-01) | `AUTH_UNAUTHORIZED` |
