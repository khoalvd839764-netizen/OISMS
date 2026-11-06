# BF-011 — Sai ngày và biên thời gian báo cáo

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa |
| **Ưu tiên** | P1 |
| **Khu vực** | Báo cáo |
| **Phát hiện** | 28–29/10/2026 (Tái hiện có kiểm soát) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

## Mô tả lỗi (lúc phát hiện)

**Tái hiện 1:** 01:00 ngày 29/10/2026 ở Việt Nam → chọn “Hôm nay”. Component lấy `toISOString().split('T')[0]` và gửi ngày **28/10**.

**Tái hiện 2:** chọn ngày lịch 28/10 → gửi `2026-10-28T00:00:00Z`; nếu ngày nghiệp vụ là Asia/Saigon, mốc đầu ngày đúng phải là `2026-10-27T17:00:00Z`.

**Biên cuối ngày:** frontend gửi đến `23:59:59Z`, backend lọc `CreatedAt <= EndDate`. Giao dịch ở `23:59:59.500` bị bỏ sót. Đây là lỗi biên độc lập với lựa chọn timezone.

**Vị trí:** `ReportsPage.tsx:calculateDates/loadReports`; `Features/Reports/Services/ReportService.cs` lọc ngày và nhóm `dailyTrend`.

**Bằng chứng:** `report-today-vietnam`, `report-timezone`, đối chiếu predicate backend.

**Điều kiện đóng:** chốt timezone nghiệp vụ; ngày UI theo đúng người dùng/tenant; chuyển sang UTC một lần; dùng khoảng `[đầu ngày, đầu ngày kế tiếp)` cho lọc. Test 00:00–07:00 Việt Nam, đầu tháng, phần lẻ giây và dailyTrend cùng timezone.

## Cách đã sửa

* **Thành phần:**
  * Frontend: `src/frontend/admin-dashboard/src/pages/ReportsPage.tsx`
  * Backend: `src/backend/src/OISM.Application/Features/Reports/Services/ReportService.cs`
  * Unit Test: `src/backend/tests/OISM.UnitTests/ReportServiceTests.cs`
* **Triệu chứng & Nguyên nhân:**
  * Frontend: Dùng `new Date().toISOString().split('T')[0]` lấy theo giờ UTC khiến khoảng 00:00–06:59 sáng giờ Việt Nam bị lùi về ngày hôm trước.
  * Backend: Lọc theo điều kiện `CreatedAt <= EndDate` hoặc chỉ chuẩn hóa khi `Millisecond == 0`, làm sót các giao dịch phát sinh ở phần lẻ giây cuối ngày khi frontend gửi `23:59:59.999+07:00`. Đồng thời gom nhóm `dailyTrend` theo UTC bị lệch ngày so với giờ Việt Nam.
* **Giải pháp khắc phục:**
  * Frontend: Xây dựng tiện ích `getVietnamTodayStr()` chuẩn múi giờ `Asia/Saigon` (`Intl.DateTimeFormat`) và `toVietnamIsoStart`/`toVietnamIsoEnd` gắn đúng offset `+07:00`.
  * Backend: Chuyển đổi điều kiện lọc sang khoảng thời gian nửa mở `[StartDate, EndDateBoundary)` (`CreatedAt < endBoundary`). Chuẩn hóa mọi mốc `endBoundary.Second == 59` (bất kể mili-giây là `.000` hay `.999`) lên đầu giây tiếp theo (`00:00:00.000` ngày kế tiếp) để bao phủ trọn vẹn toàn bộ ngày kinh doanh. Gom nhóm biểu đồ ngày theo offset Việt Nam: `CreatedAt.ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-dd")`.
* **Kiểm thử xác thực:** Pass 2 kịch bản frontend (`report-today-vietnam`, `report-timezone`) và unit test backend `GetRevenueProfitReport_ShouldIncludeSubSecondEndOfDay_AndGroupDailyTrendInVietnamTime`.
