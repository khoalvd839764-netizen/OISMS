# BF-024 — Tìm kiếm toàn hệ thống: kết quả phiếu nhập không mở đúng phiếu

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa (`FIN-I` + `FIN-I-fix1`, review 21/11/2026) |
| **Ưu tiên** | P2 |
| **Khu vực** | Admin / Tìm kiếm (CS-04) |
| **Phát hiện** | 04/11/2026, nghiệm thu frontend ([báo cáo](../../report-after-upgrade/frontend-coming-soon-va-upgrade.md#chưa-làm)) |

## Mô tả lỗi

Ô tìm kiếm trên thanh đầu trang (`AdminShell.tsx`, gọi `GET /api/search`) trả được kết quả loại phiếu nhập, nhưng bấm vào kết quả không mở đúng phiếu nhập đó. Các loại kết quả khác (sản phẩm, đơn hàng) điều hướng bình thường.

**Vị trí cần xem:** `src/frontend/admin-dashboard/src/components/AdminShell.tsx` (xử lý chọn kết quả), `pages/InventoryPage.tsx` (đọc tham số mở phiếu nhập).

## Điều kiện đóng

- Bấm kết quả phiếu nhập mở đúng tab/phiếu theo ID; F5 tại URL đó vẫn mở đúng.
- Kiểm tra trên trình duyệt với tenant test có ít nhất 2 phiếu nhập.

## Cách sửa & Kiểm chứng (`OISMS-66-FIN-I`, 21/11/2026)

- **Nguyên nhân:** `AdminShell.tsx` điều hướng theo `g.url` (`/inventory` không kèm ID), đồng thời `InventoryPage.tsx` xóa ngay `?receiptId=` khỏi URL sau khi mở phiếu nên F5 mất trạng thái.
- **Cách sửa:**
  - `src/frontend/admin-dashboard/src/components/AdminShell.tsx`: bấm kết quả phiếu nhập điều hướng sang `/inventory?receiptId=${encodeURIComponent(g.id)}`.
  - `src/frontend/admin-dashboard/src/pages/InventoryPage.tsx`: giữ lại `receiptId` trong `searchParams` khi mở `openSavedReceipt(receiptParam)` để F5 tại URL đó vẫn mở đúng phiếu.
- **Kiểm chứng:** `npm run build --prefix src/frontend/admin-dashboard` thành công; kiểm tra trên trình duyệt tại `http://localhost:5173` (bấm phiếu nhập thứ hai, F5 giữ đúng phiếu).

## Vòng sửa FIN-I-fix1 (21/11/2026)

- **Lỗi phát hiện khi review:** URL giữ `receiptId` nhưng đóng phiếu không xóa, nên F5 sau khi đóng tự mở lại phiếu cũ.
- **Cách sửa:** `InventoryPage.tsx` xóa `receiptId` khỏi URL khi modal phiếu nhập chuyển từ mở sang đóng.
- **Kiểm chứng:** PASS 4/4 ca trên trình duyệt ([bằng chứng](../../06-testing-and-qa/evidence/CS-04/2026-11-21-FIN-I-fix1.md)).
