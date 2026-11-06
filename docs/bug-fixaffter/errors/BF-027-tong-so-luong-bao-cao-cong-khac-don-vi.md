# BF-027 — Trường `total*Quantity` trong API báo cáo cộng gộp khác đơn vị

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ❌ Chưa sửa (giao diện đã tránh) |
| **Ưu tiên** | P3 |
| **Khu vực** | Backend / Báo cáo / Đa đơn vị tính |
| **Phát hiện** | 04/11/2026, nghiệm thu frontend ([báo cáo](../../report-after-upgrade/frontend-coming-soon-va-upgrade.md#ghi-chú-đối-chiếu)) |

## Mô tả lỗi

`GET /api/reports/inventory-velocity` vẫn trả các trường tổng `total*Quantity` để tương thích ngược. Các trường này cộng `onHand`/`available` của mọi SKU dù đơn vị gốc khác nhau (ví dụ 48 Cái + 26 hộp = 74), nên con số không có nghĩa. Giao diện đã chuyển sang dùng `quantityByUnit`, nhưng client khác gọi API vẫn có thể hiểu sai.

**Vị trí:** `src/backend/src/OISM.Application/Features/Reports/Services/ReportService.cs` (`GetInventoryVelocityReportAsync`), DTO báo cáo vận tốc kho.

## Điều kiện đóng

- Nhóm chốt: bỏ các trường tổng, đổi tên/ghi rõ là tổng thô, hoặc chỉ trả khi mọi SKU cùng đơn vị.
- Cập nhật [DOC-API-04](../../05-api-contracts/04-order-endpoints.md) và test báo cáo.
