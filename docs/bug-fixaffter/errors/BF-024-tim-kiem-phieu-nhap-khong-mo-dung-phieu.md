# BF-024 — Tìm kiếm toàn hệ thống: kết quả phiếu nhập không mở đúng phiếu

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ❌ Chưa sửa |
| **Ưu tiên** | P2 |
| **Khu vực** | Admin / Tìm kiếm (CS-04) |
| **Phát hiện** | 04/11/2026, nghiệm thu frontend ([báo cáo](../../report-after-upgrade/frontend-coming-soon-va-upgrade.md#chưa-làm)) |

## Mô tả lỗi

Ô tìm kiếm trên thanh đầu trang (`AdminShell.tsx`, gọi `GET /api/search`) trả được kết quả loại phiếu nhập, nhưng bấm vào kết quả không mở đúng phiếu nhập đó. Các loại kết quả khác (sản phẩm, đơn hàng) điều hướng bình thường.

**Vị trí cần xem:** `src/frontend/admin-dashboard/src/components/AdminShell.tsx` (xử lý chọn kết quả), `pages/InventoryPage.tsx` (đọc tham số mở phiếu nhập).

## Điều kiện đóng

- Bấm kết quả phiếu nhập mở đúng tab/phiếu theo ID; F5 tại URL đó vẫn mở đúng.
- Kiểm tra trên trình duyệt với tenant test có ít nhất 2 phiếu nhập.
