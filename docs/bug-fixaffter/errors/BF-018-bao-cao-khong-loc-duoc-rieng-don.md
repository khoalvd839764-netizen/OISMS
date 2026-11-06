# BF-018 — Báo cáo không lọc được riêng đơn Admin

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa |
| **Ưu tiên** | P2 |
| **Khu vực** | Báo cáo |
| **Phát hiện** | 28–29/10/2026 (Tái hiện có kiểm soát + code) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

## Mô tả lỗi (lúc phát hiện)

**Tái hiện:** tạo/đã có đơn kênh `Admin`, vào báo cáo và tìm bộ lọc tương ứng.

**Thực tế:** không có option `Admin`; có `Web` với nhãn Website Online. Backend lọc chuỗi channel chính xác, nên `Web` không thay thế cho `Admin`. Không khẳng định các kênh thật khác không hoạt động.

**Vị trí:** `ReportsPage.tsx` dropdown kênh; `types/ordersCenter.ts` và `OrderService`/report predicate channel.

**Bằng chứng:** `report-admin-channel` xác nhận option; đối chiếu backend exact-match.

**Điều kiện đóng:** danh sách kênh thống nhất với contract; có Admin; dữ liệu fixture Admin hiển thị đúng khi chọn Admin và không lẫn POS/sàn.

## Cách đã sửa

* **Thành phần:** `src/frontend/admin-dashboard/src/pages/ReportsPage.tsx`
* **Triệu chứng & Nguyên nhân:**
  * Trong dropdown lọc kênh bán hàng của trang Báo cáo thiếu kênh `Admin` và lại hiển thị tùy chọn `Web` không nằm trong tập kênh hợp lệ của hệ thống (`POS`, `Admin`, `ECommerce`), khiến người dùng không thể lọc báo cáo riêng cho đơn Admin.
* **Giải pháp khắc phục:**
  * Bổ sung tùy chọn `<option value="Admin">Admin (Quản trị viên)</option>` và loại bỏ tùy chọn `Web` không hợp lệ khỏi dropdown kênh bán hàng trên trang Báo cáo.
* **Kiểm thử xác thực:** Pass kịch bản `report-admin-channel`: dropdown kênh hiển thị đúng các kênh hợp lệ gồm `Admin` và loại bỏ `Web`.
