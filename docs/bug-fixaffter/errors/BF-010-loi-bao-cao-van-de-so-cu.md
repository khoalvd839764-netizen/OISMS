# BF-010 — Lỗi báo cáo vẫn để số cũ dưới bộ lọc mới

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa |
| **Ưu tiên** | P2 |
| **Khu vực** | Báo cáo |
| **Phát hiện** | 28–29/10/2026 (Tái hiện có kiểm soát) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

## Mô tả lỗi (lúc phát hiện)

**Tái hiện:** Có báo cáo A → đổi sang C → API C thất bại.

**Thực tế:** có thông báo lỗi nhưng vẫn render số của lần trước dưới bộ lọc C; không gắn nhãn dữ liệu cũ hoặc ẩn số.

**Vị trí:** `ReportsPage.tsx:loadReports/catch` và các khối render `pnlData`, `velocityData`, `alertsData`.

**Bằng chứng:** `report-stale-on-error`.

**Điều kiện đóng:** dữ liệu gắn với bộ lọc đã tải thành công; đổi query phải reset hoặc thể hiện stale cùng query cũ rõ ràng. Test HTTP error, 401/403 và chuyển tab.

## Cách đã sửa

* **Thành phần:** `src/frontend/admin-dashboard/src/pages/ReportsPage.tsx`
* **Triệu chứng & Nguyên nhân:**
  * Đang xem báo cáo thành công ở bộ lọc A, người dùng đổi sang bộ lọc C nhưng API của C bị lỗi mạng/500. Giao diện hiển thị thông báo lỗi nhưng bên dưới vẫn giữ nguyên các con số biểu đồ của bộ lọc A.
* **Giải pháp khắc phục:**
  * Trong khối `catch` xử lý lỗi của hàm `loadReports`, chủ động làm sạch toàn bộ dữ liệu báo cáo cũ: `setPnlData(null)`, `setVelocityData(null)`, `setAlertsData([])`.
* **Kiểm thử xác thực:** Pass kịch bản `report-stale-on-error`: khi truy vấn lỗi, dữ liệu cũ biến mất và chỉ hiển thị thông báo lỗi rõ ràng.
