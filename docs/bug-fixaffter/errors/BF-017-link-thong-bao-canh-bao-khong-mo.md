# BF-017 — Link thông báo cảnh báo không mở đúng tab

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa |
| **Ưu tiên** | P2 |
| **Khu vực** | Thông báo/báo cáo |
| **Phát hiện** | 28–29/10/2026 (Tái hiện trên browser) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

## Mô tả lỗi (lúc phát hiện)

**Tái hiện:** mở `http://localhost:5173/reports?tab=alerts`.

**Kỳ vọng:** tab Cảnh báo tồn kho tối thiểu.

**Thực tế:** trang vẫn mở P&L. Đây chính là URL `LowStockWarning` tạo ra. `ReportsPage` luôn khởi tạo `activeTab='pnl'` và không đọc query string.

**Vị trí:** `realtimeAdapter.ts` tạo href và `ReportsPage.tsx` khởi tạo tab.

**Bằng chứng:** ảnh browser `evidence/reports-alert-link.jpg` và `evidence/reports-alert-link.json` (đã dọn khỏi repo).

**Điều kiện đóng:** URL/query tab là một nguồn trạng thái được kiểm tra hợp lệ; direct link, notification, reload và browser back/forward mở đúng tab.

## Cách đã sửa

* **Thành phần:** `src/frontend/admin-dashboard/src/pages/ReportsPage.tsx`, `src/frontend/admin-dashboard/src/components/NotificationCenter.tsx`
* **Triệu chứng & Nguyên nhân:**
  * Khi người dùng truy cập URL `/reports?tab=alerts` hoặc nhấp vào thông báo cảnh báo tồn kho tối thiểu trong `NotificationCenter` ngay khi đang đứng ở trang Báo cáo, trang báo cáo không chuyển sang tab "Cảnh báo tồn kho tối thiểu".
* **Giải pháp khắc phục:**
  * Đọc tham số query string `?tab=` khi khởi tạo `ReportsPage`, đồng bộ URL khi người dùng chuyển tab, và lắng nghe sự kiện `popstate` cùng custom event `oism:navigate` được phát từ `NotificationCenter` để chuyển ngay sang tab `alerts` cả khi mở mới lẫn khi đang ở ngay trên trang `/reports`.
* **Kiểm thử xác thực:** Pass kịch bản `BF-017`: truy cập URL `?tab=alerts` và điều hướng trực tiếp từ thông báo đều kích hoạt đúng tab "Cảnh báo tồn kho tối thiểu".
