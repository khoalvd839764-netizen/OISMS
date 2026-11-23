# 08. ĐẶC TẢ MÀN HÌNH BÁO CÁO DOANH THU & LÃI GỘP (FINANCIAL & PROFIT REPORTS)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SCR-08` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực** | `2026-10-25` |
| **Đường dẫn URL Routes** | `/admin/reports`, `/reports` |
| **Quyền truy cập (RBAC)** | **Chỉ dành cho Chủ sở hữu (`Owner`)** |
| **Source code Frontend** | [`ReportsPage.tsx`](../../src/frontend/admin-dashboard/src/pages/ReportsPage.tsx) |
| **Controllers Backend** | [`OrdersController.cs`](../../src/backend/src/OISM.WebApi/Controllers/OrdersController.cs) |

---

## 1. Mục Tiêu & Luồng Nghiệp Vụ

Màn hình **Báo Cáo Doanh Thu & Quản Trị Lãi Gộp (P&L Reports)** là phân hệ độc quyền dành cho Chủ doanh nghiệp (`Owner`):
1. **Minh bạch hóa Lợi nhuận gộp (P&L Analysis):** Đối chiếu trực tiếp Doanh thu bán hàng thực tế với Tổng giá vốn hàng bán (`COGS`), được chụp chính xác theo giá vốn bình quân gia quyền ($WAC$) tại thời điểm xác nhận từng đơn hàng:
   $$\text{Lợi nhuận gộp (Gross Profit)} = \text{Doanh thu thuần} - \text{Tổng giá vốn (COGS)}$$
   $$\text{Tỷ suất lợi nhuận gộp (Gross Margin)} = \left(\frac{\text{Lợi nhuận gộp}}{\text{Doanh thu thuần}}\right) \times 100\%$$
2. **Hiệu quả mặt hàng (SKU Performance Ranking):** Phân tích danh sách biến thể SKU bán chạy nhất (Top Sellers) và các mặt hàng ứ đọng vốn (Dead Stock / Slow-moving Inventory).
3. **Dự báo nhu cầu & Gợi ý nhập hàng AI (AI Reorder Recommendations):** Áp dụng mô hình dự báo tiêu thụ theo chuỗi thời gian để gợi ý số lượng đặt hàng tối ưu cho từng nhà cung cấp.

> **Trạng thái 10/11/2026:** giao diện gợi ý nhập hàng là tab Gợi ý nhập hàng trong `/inventory`, đã mở lại cho Owner và Staff (tạm ẩn từ 04/11/2026). API `GET /api/ai/reorder-recommendations` tính bằng công thức; Trợ lý OISMS dùng cùng dữ liệu để trả lời "nên nhập gì".

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
| `GET` | `/api/reports/revenue-profit` | `ReportsController.GetRevenueProfitReport` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#2-danh-mục-endpoints-báo-cáo-tìm-kiếm--dự-báo-ai-fr-rep-0103-fr-ai-01) | `FORBIDDEN_ACCESS` (Chặn nhân viên không phải Owner, xem [`DOC-ERR-02`](../12-error-and-status-codes/02-error-codes-catalog.md)) |
| `GET` | `/api/reports/daily-summaries` | `ReportsController.GetDailySummaries` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#2-danh-mục-endpoints-báo-cáo-tìm-kiếm--dự-báo-ai-fr-rep-0103-fr-ai-01) | `FORBIDDEN_ACCESS`, `VALIDATION_ERROR` (Chỉ `Owner`, `Staff`/`Cashier` nhận `403`) |
| `POST` | `/api/reports/daily-summaries/rebuild` | `ReportsController.RebuildDailySummaries` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#2-danh-mục-endpoints-báo-cáo-tìm-kiếm--dự-báo-ai-fr-rep-0103-fr-ai-01) | `FORBIDDEN_ACCESS`, `VALIDATION_ERROR` (Tối đa 31 ngày mỗi lần tính lại) |
| `GET` | `/api/reports/inventory-velocity` | `ReportsController.GetInventoryVelocityReport` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#2-danh-mục-endpoints-báo-cáo-tìm-kiếm--dự-báo-ai-fr-rep-0103-fr-ai-01) | `AUTH_UNAUTHORIZED` |
| `GET` | `/api/reports/stock-alerts` | `ReportsController.GetStockAlerts` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#2-danh-mục-endpoints-báo-cáo-tìm-kiếm--dự-báo-ai-fr-rep-0103-fr-ai-01) | `AUTH_UNAUTHORIZED` |
| `GET` | `/api/ai/reorder-recommendations` | `AiController.GetReorderRecommendations` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#2-danh-mục-endpoints-báo-cáo-tìm-kiếm--dự-báo-ai-fr-rep-0103-fr-ai-01) | `AUTH_UNAUTHORIZED` |

---

## 4. Tab Vận Tốc Kho: Tồn Theo Đơn Vị (Cập nhật 04/11/2026)

| Thành phần | Quy tắc hiển thị |
| :--- | :--- |
| Thẻ Tổng giá trị tồn (WAC) | Một số tiền duy nhất, định giá theo giá vốn bình quân. |
| Thẻ Tồn thực tế, Giữ chỗ, Khả dụng | Mỗi đơn vị gốc một dòng, lấy từ `quantityByUnit`. Không cộng gộp khác đơn vị. |
| Nhiều đơn vị | Hiện 4 dòng đầu; nút `Xem đủ N đơn vị` mở cùng lúc cho cả ba thẻ. |
| SKU chưa có đơn vị gốc | Gom vào nhóm `Chưa xác định`. |
| Cột SL bán và Tồn hiện tại | Ghi kèm đơn vị gốc. Tồn hiện tại cộng trên mọi chi nhánh, hoặc chỉ chi nhánh đang lọc; mỗi SKU một dòng. |

---

## 5. Bộ Lọc Nhiều SKU Ở Tab P&L (`OISMS-66-FIN-B`, 21/11/2026)

- **Bộ chọn SKU (`SkuMultiSelect`):** cho phép tìm theo mã SKU/tên sản phẩm, hiển thị chip SKU đã chọn, nút bỏ từng SKU hoặc "Bỏ lọc SKU", chặn chọn quá **20** SKU kèm thông báo.
- **Đồng bộ URL & Chống race condition:** đồng bộ tham số `?skus=id1,id2` hai chiều trên URL (F5 giữ nguyên lựa chọn); mỗi lần đổi lựa chọn tăng `activeReqRef.current` để bỏ qua phản hồi cũ.
- **Hiển thị khi đang lọc SKU:**
  - Truyền `skuIds` dạng query lặp (`?skuIds=...&skuIds=...`) lên `GET /api/reports/revenue-profit`.
  - Hiện bảng **Chi tiết doanh thu & lãi gộp theo SKU đang lọc (`skuBreakdown`)**, số lượng bán tách theo từng đơn vị (`quantityByUnit`), không cộng khác đơn vị.
  - **Ẩn khối đối soát `daily_sales_summaries`** (vì bảng tổng hợp ngày không lưu theo SKU, Q4).
  - Bản in (`window.print`) hiển thị danh sách SKU đang lọc ở phần tiêu đề.
- **Khi bỏ hết SKU:** giao diện trở về nguyên trạng ban đầu và hiện lại khối đối soát `daily_sales_summaries`.

---

## 5. Tab Lợi Nhuận: Khu Vực "Tổng Hợp Theo Ngày" (Cập nhật 15/11/2026)

| Thành phần | Quy tắc hiển thị & Tương tác |
| :--- | :--- |
| Vị trí | Nằm ngay dưới bảng *Biến động doanh thu & lãi gộp theo ngày* trong tab Doanh thu & Lợi nhuận (`Owner` only). |
| Bảng đối soát | Gồm các cột: `Ngày`, `Doanh thu (TH / TT)`, `Hàng trả (TH / TT)`, `Giá vốn (TH / TT)`, `Lãi gộp (TH / TT)`, `Đối soát`, `Cập nhật lúc` (`Asia/Ho_Chi_Minh`). Khi `summary == null` (chưa tổng hợp), cột tổng hợp hiển thị `—`. |
| Nhãn trạng thái đối soát | - `Khớp` (`Matched`): số tổng hợp bằng số tính trực tiếp, chỉ chênh do làm tròn (tối đa `0,01 đ` × số dòng tổng hợp của ngày).<br/>- `Lệch` (`Mismatch`): phát sinh đơn/trả mới sau lần tổng hợp gần nhất.<br/>- `Chưa tổng hợp` (`NotSummarized`): có phát sinh trong ngày nhưng chưa có dòng trong `daily_sales_summaries`. |
| Nút **Tính lại** | Gọi `POST /api/reports/daily-summaries/rebuild` theo khoảng `startDate..endDate` đang chọn (tối đa 31 ngày). Khóa nút và hiển thị trạng thái xoay khi đang chạy hoặc đang tải báo cáo; tự nạp lại bảng đối soát sau khi hoàn tất; lỗi (ví dụ > 31 ngày) hiển thị rõ theo `ProblemDetails` + `errorCode`. Không dùng mock data. |

---

## 6. In Báo Cáo A4 (`OISMS-59-G4`, 15/11/2026)

- Nút **In báo cáo** trên `ReportsPage` kích hoạt `window.print()` cho tab đang xem (`P&L`, `Vận tốc kho` hoặc `Cảnh báo tồn tối thiểu`).
- Khối tiêu đề chỉ hiện khi in (`hidden print:block`): tên tenant, tab đang xem, khoảng ngày (`UTC+07:00`), chi nhánh, người in và giờ in.
- CSS `@media print` khổ A4 ẩn sidebar, thanh lọc, tab và nút bấm; ngăn cắt ngang dòng bảng (`break-inside: avoid`) và co biểu đồ vừa khổ giấy.
- Các trường giá vốn và lãi gộp khi nhận giá trị `null` (như với vai trò `Staff`) hiển thị và in ra ký hiệu `—`.
