# BF-009 — Báo cáo bị phản hồi cũ ghi đè

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa |
| **Ưu tiên** | P1 |
| **Khu vực** | Báo cáo |
| **Phát hiện** | 28–29/10/2026 (Tái hiện có kiểm soát) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

## Mô tả lỗi (lúc phát hiện)

**Tái hiện:** Chọn chi nhánh A rồi B nhanh → B trả 9.900 trước → A trả 1.100 sau.

**Thực tế:** bộ lọc vẫn B nhưng số hiển thị thành 1.100 của A. `loadReports` không có request generation/cancellation/context check.

**Vị trí:** `pages/ReportsPage.tsx:loadReports` và effect tải theo bộ lọc.

**Bằng chứng:** `report-response-order`.

**Điều kiện đóng:** chỉ response của query/actor hiện tại được commit; kiểm tra đủ branch/channel/date/tab và stale loading/error, không chỉ helper service.

## Cách đã sửa

* **Thành phần:** `src/frontend/admin-dashboard/src/pages/ReportsPage.tsx`
* **Triệu chứng & Nguyên nhân:**
  * Người dùng chọn bộ lọc chi nhánh A rồi chuyển nhanh sang chi nhánh B. Phản hồi của B trả về trước (doanh thu 9.900), phản hồi chậm của A trả về sau (doanh thu 1.100) và ghi đè lên giao diện đang chọn bộ lọc B.
* **Giải pháp khắc phục:**
  * Sử dụng `activeReqRef = useRef(0)` làm biến đếm thế hệ truy vấn. Chỉ khi phản hồi API trả về với `currentReq === activeReqRef.current` thì mới được phép cập nhật dữ liệu vào React state; mọi phản hồi cũ hơn đều bị hủy bỏ an toàn.
* **Kiểm thử xác thực:** Pass kịch bản `report-response-order`: dữ liệu doanh thu của bộ lọc B được bảo toàn nguyên vẹn, phản hồi muộn của A bị loại bỏ.
