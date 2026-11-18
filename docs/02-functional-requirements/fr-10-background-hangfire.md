# FR-10: Xử Lý Tác Vụ Nền Với Hangfire (Background Processing)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-10` |
| **Phiên bản (Version)** | `v1.4.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-15` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-SIM-03`, `FR-RSE-03` (Trang 3, 6) |
| **Gói công việc (Task Package)** | Task Package 3 — Centralized Order Hub & Anti-Oversell Engine |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả các tác vụ nền bất đồng bộ sử dụng Hangfire: tự động hủy đơn giữ chỗ quá hạn và tổng hợp báo cáo hằng ngày. | `INIT-FR-10` |
| `v1.1.0` | `2026-10-26` | Đăng Khoa (BE1) | Đính chính boundary auto-expiry thành `ExpiresAt <= cutoffUtc` (đồng bộ Confirm guard), phạm vi Task 6 chuyên biệt cho auto-cancel/release. | `S3-BE1-T06` |
| `v1.2.0` | `2026-11-06` | Nhóm dự án OISMS | Ghi hiện trạng: Hangfire mặc định tắt; có job hết hạn giữ chỗ và job sinh kỳ kiểm kê; chưa có job tổng hợp báo cáo ngày. | `OISMS-46` |
| `v1.3.0` | `2026-11-08` | Nhóm dự án OISMS | Bật được qua `HANGFIRE_ENABLED`; Hangfire dùng chung kết nối `Database:*`; schema `hangfire` qua migration; ghi kết quả kiểm chứng hết hạn giữ chỗ trên Docker. | `OISMS-46` |
| `v1.4.0` | `2026-11-15` | Nhóm dự án OISMS | Triển khai job tổng hợp báo cáo ngày `oisms-daily-sales-summary` (`00:30` UTC+07, cửa sổ 7 ngày từ hôm qua), API đối soát & tính lại cho `Owner`, và ghi nhận kết quả kiểm chứng Hangfire trên Docker. | `OISMS-59-G3B` |

---

## 1. Bảng Đặc Tả Chi Tiết Yêu Cầu (`FR-SIM-03`)

| Mã yêu cầu | Tên yêu cầu | Mô tả nghiệp vụ & Quy tắc kỹ thuật | Tiêu chí nghiệm thu (Acceptance Criteria) |
| :---: | :--- | :--- | :--- |
| **`FR-SIM-03`** | **Background Processing (Hangfire)** | Sử dụng **Hangfire** để thực thi các tác vụ bất đồng bộ nặng chạy ngầm:<br/>1. **Auto-canceling expired reserved orders:** Quét định kỳ các đơn hàng có phiếu giữ chỗ Active đã vượt quá thời gian giữ chỗ (`ExpiresAt <= cutoffUtc`, chuẩn hóa UTC server-side đồng bộ với Confirm guard), tự động chuyển đơn sang `Cancelled`, giải phóng `reserved` hai cấp (warehouse & branch balances) về lại `available`, và phát sự kiện Realtime `StockLevelChanged` ngoài transaction.<br/>*(Lưu ý phạm vi Task 6: Thực thi auto-canceling expired reservations. Hạng mục Daily report aggregations thuộc trách nhiệm BE2 trong task riêng).*<br/>2. **Daily report aggregations:** Tổng hợp trước dữ liệu doanh thu, giá vốn và lợi nhuận hằng ngày cho từng Tenant. | - Tác vụ giải phóng reservation quá hạn chạy an toàn trong transaction có khóa advisory (SKU tăng dần -> RequestKey -> Order) và khóa hàng (`FOR UPDATE`), tránh xung đột (race condition/deadlock) nếu nhân viên bấm `Confirm` hoặc POS checkout đúng lúc job đang quét.<br/>- Mỗi reservation chỉ được giải phóng đúng 1 lần duy nhất (idempotent), hỗ trợ xử lý cô lập lỗi theo batch. |

> **Hiện trạng & Kiểm chứng (15/11/2026):**
> - Các Recurring Job đã đăng ký:
>   1. `oisms-reservation-expiry` (`ReservationExpiryJob`, `*/1 * * * *` UTC): hủy đơn giữ chỗ hết hạn và giải phóng `Reserved`.
>   2. `oisms-stock-count-periods` (`StockCountPeriodJob`, `5 * * * *` UTC): sinh kỳ kiểm kê định kỳ.
>   3. `oisms-daily-sales-summary` (`DailySalesSummaryJob`, `30 0 * * *` múi giờ Việt Nam `Asia/Ho_Chi_Minh` / `SE Asia Standard Time`, fallback `UTC+07`): tổng hợp dữ liệu vào bảng `daily_sales_summaries`.
> - **Cửa sổ 7 ngày và giới hạn (`Reports:SummaryRebuildDays = 7`):** mỗi đêm lúc `00:30` UTC+07, job quét danh sách tenant đang hoạt động (`IsActive = true`) trong privileged scope (cô lập lỗi theo từng tenant — lỗi 1 tenant không chặn các tenant còn lại, cuối lượt tổng hợp lỗi thành `AggregateException` cho Hangfire log) và chạy `RebuildAsync` cho cửa sổ **7 ngày gần nhất tính từ hôm qua** (`[yesterday - 6 .. yesterday]` theo UTC+07). Việc giới hạn 7 ngày giúp job ban đêm chạy nhanh và tự đồng bộ lại các đơn/phiếu trả phát sinh hoặc xác nhận trễ trong tuần.
> - **Cách chạy lại ngoài cửa sổ 7 ngày hoặc trong ngày:** `Owner` gọi `POST /api/reports/daily-summaries/rebuild` (hoặc bấm nút **"Tính lại"** trên tab Doanh thu & Lợi nhuận của trang Báo cáo) với khoảng `fromDate..toDate` tối đa `31` ngày mỗi lượt. Mỗi ngày thực thi trong 1 transaction với khóa `pg_advisory_xact_lock(tenant, ngày)` sau khi mở transaction, xóa dữ liệu cũ của ngày đó và ghi lại dòng mới (không ghi dòng toàn `0`).
> - `Hangfire:Enabled` mặc định `false` trong `appsettings.json` và `.env.example` (`HANGFIRE_ENABLED`). Khi tắt, không có Hangfire server và không đăng ký job. Cách bật: [cấu hình môi trường](../07-devops-and-deployment/03-environment-configuration.md#2-khóa-cấu-hình-backend-net); trên AWS xem [cấu hình production](../13-deploy-aws/03-cau-hinh-moi-truong-production.md#4-cấu-hình-backend-trên-production).
>
> **Kiểm chứng trên Docker (`HANGFIRE_ENABLED=true`):**
> - *08/11/2026 (`oisms-reservation-expiry`):* chỉ cấu hình `Database__*`, Hangfire kết nối đúng DB và tạo bảng trong schema `hangfire`. Đơn Admin giữ chỗ 2 sản phẩm để hết hạn tự nhiên: 23 giây sau hạn đơn chuyển `Cancelled`, phiếu giữ chỗ `Expired`, `Reserved` về 0, `OnHand` giữ nguyên và sổ cái không có dòng `IN` bù.
> - *15/11/2026 (`oisms-daily-sales-summary`):* bật `HANGFIRE_ENABLED=true`, xác nhận `hangfire.hash` ghi nhận `Cron = 30 0 * * *`, `TimeZoneId = Asia/Ho_Chi_Minh`. Kích hoạt thực thi job trên Hangfire server (`LastJobId = 401`, trạng thái `Succeeded`): bảng `daily_sales_summaries` ghi nhận 2 dòng trong cửa sổ `2026-11-08..2026-11-14` (`2026-11-13` POS doanh thu `1.495.000 đ` / giá vốn `750.000 đ`, `2026-11-14` Admin doanh thu `897.000 đ` / giá vốn `450.000 đ`), tổng `2.392.000 đ` doanh thu và `1.200.000 đ` giá vốn khớp 100% với `GET /api/reports/revenue-profit`; đơn trong ngày `2026-11-15` (`598.000 đ`) hiển thị `NotSummarized` (`Chưa tổng hợp`) trên UI và chuyển sang `Khớp` (3 dòng tổng hợp) sau khi bấm nút **"Tính lại"**.
