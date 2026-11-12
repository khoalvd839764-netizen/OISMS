# Báo cáo sau nâng cấp: Lịch kiểm kê và duyệt chênh lệch (Upgrade 03)

| Mục | Nội dung |
| --- | --- |
| Task | `OISMS-47-U03`, phần backend (`P1`) |
| Ngày kiểm tra | 03/11/2026 |
| Đặc tả | [Tài liệu 03](../Upgrade-recommended/Done/03-quy-dinh-kiem-ke.md), mục "Quyết định đã chốt" |
| Phạm vi | Backend, test, tài liệu API. **Chưa có giao diện (`P2`).** |
| Trạng thái | Backend đã kiểm chứng trên PostgreSQL 16 bằng database test riêng. Chưa kiểm chứng đầu cuối từ giao diện, chưa chạy migration trên database Docker đang dùng. |

> **Cập nhật 04/11/2026:** giao diện của phần này đã có. Kết quả nghiệm thu xem [báo cáo frontend](frontend-coming-soon-va-upgrade.md). Các mục "chưa có giao diện" bên dưới là hiện trạng tại ngày kiểm tra ghi trong bảng.

## Đã làm

- **Dữ liệu:** thêm `stock_count_schedules`, `stock_count_periods`, `stock_count_approvals` (chỉ INSERT); thêm cột loại kỳ, lý do đột xuất, người nộp vào `stock_counts`; thêm người đếm, thời điểm đếm, thời điểm chụp vào `stock_count_items`; thêm trạng thái `PendingApproval`.
- **Lịch và kỳ:** cấu hình lịch giữa tháng, cuối tháng theo từng kho. Job Hangfire mỗi giờ sinh kỳ tháng này và tháng sau, chạy lại không trùng. Quá hạn được tính khi đọc.
- **Bắt đầu đếm:** phiếu và ảnh chụp số dư chỉ sinh khi bấm bắt đầu, nên giao dịch trước ngày đếm không gây lệch.
- **Duyệt:** `Counting → PendingApproval → Confirmed`. Chưa duyệt thì chưa ghi tồn, sổ kho, phiếu điều chỉnh. Mọi thao tác nộp, duyệt, đếm lại, hủy đều lưu người làm, lý do, số liệu lúc đó.
- **Lệch sequence:** báo đủ mã SKU ở bước nộp và bước duyệt; đếm lại đúng các SKU đó, giữ số cũ trong hồ sơ.
- **Quyền:** chỉ Owner duyệt và yêu cầu đếm lại. Người duyệt không được trùng người đếm; tenant chỉ có một Owner thì được tự duyệt nhưng phải ghi lý do.

## Kiểm chứng

Chạy trên PostgreSQL 16 của `oism_postgres`, cổng `5432`. Fixture tự tạo database test riêng mỗi lần chạy; phép so schema dùng 2 database tạm rồi xóa. Không chạy gì trên database `oisms`.

| Lệnh hoặc phép kiểm | Kết quả |
| --- | --- |
| `dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal` | 0 lỗi |
| `dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false` | PASS 179/179 |
| `dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false` | PASS 362/362, 18 phút 20 giây |
| Tạo database từ schema tại `46d8d23`, chạy file migration 2 lần bằng `psql`, so `pg_dump -s` với `oisms_schema.sql` | Đều thoát mã 0. Khác biệt duy nhất là thứ tự cột của `stock_count_items` |
| So SQL nhúng trong `DatabaseMigrationService` với file migration | Giống nhau sau khi bỏ khoảng trắng và comment |
| Test `U03_Migration_TuSchemaTruocU03...`: đưa database về trước U03, chạy `UpgradeDatabaseSchemaAsync` 2 lần, phiếu `Counting` cũ đi tiếp đến chốt | PASS |

Test mới: [`StockCountU03IntegrationTests.cs`](../../test-oisms/backend/OISM.IntegrationTests/StockCountU03IntegrationTests.cs) (14 test), [`StockCountPeriodCalculatorTests.cs`](../../test-oisms/backend/OISM.UnitTests/StockCountPeriodCalculatorTests.cs) (6 test). Có các ca song song: 8 job sinh kỳ, 10 lần bắt đầu cùng một kỳ, 10 lần duyệt cùng một phiếu.

## Lỗi phát hiện khi nghiệm thu và đã sửa

Phần code ban đầu chỉ được build, chưa chạy test.

| Lỗi | Hậu quả | Kiểm chứng |
| --- | --- | --- |
| Thiếu cấu hình khóa ngoại `Branch` cho lịch và kỳ, EF sinh cột không tồn tại | Mọi thao tác tạo lịch trả 500, toàn bộ tính năng lịch không chạy | Các test `U03_Schedule`, `U03_Job`, `U03_Start` |
| `Start` nhận `RequestKey` từ client, ngoài thiết kế | Hủy phiếu rồi bắt đầu lại với khóa cũ sẽ nhận lại phiếu đã hủy, kỳ kẹt ở `Planned` | `U03_Start_ChupSoDuLucBam...` |
| Nộp và duyệt ghi đè `Note` của phiếu | Mất ghi chú gốc | `U03_ChuaDuyetChuaGhiTon...` |
| Tạo, sửa lịch không giữ khóa sinh kỳ | Chạy trùng lúc với job có thể lỗi trùng `period_key` | Sửa theo thiết kế; test song song chỉ phủ job với job |
| Loại kỳ nhận sai chữ hoa thường | Giá trị lọt xuống ràng buộc DB, trả 500 thay vì 400 | `U03_Schedule_TaoLich...` |
| Bộ lọc `from`, `to` gửi thẳng mốc +07:00 vào Npgsql | `GET /api/stock-count-periods` trả 500 | `U03_LocKy_ThoiGianCoMuiGio07...` |
| Replay `Start` không nạp tên sản phẩm; hai mã lỗi không dùng trong code và tài liệu | Dữ liệu trả về thiếu tên, tài liệu sai | Đọc code |

Bộ test kiểm kê cũ được chuyển sang luồng nộp rồi duyệt. Bước chốt giữ nguyên các kiểm tra. Test `AC08_Concurrency_RaceBetweenConfirmAndRecord` nay chỉ còn nhánh ghi số bị từ chối, vì đã nộp thì không ghi số được.

Các sửa đổi và test trên do người nghiệm thu vừa làm vừa tự kiểm, chưa qua reviewer độc lập.

## Điểm còn lưu ý

- **Giao diện Admin tạm hỏng ở bước chốt:** nút chốt trả 409 vì chưa có bước nộp, Staff nhận 403. Hết khi xong `P2`.
- **Bắt đầu sớm:** API cho bắt đầu kỳ trước ngày dự kiến. Đặc tả chưa quy định.
- **Staff vẫn hủy được phiếu đang chờ duyệt:** không ghi tồn nhưng hồ sơ có lưu lại. Chưa có quyết định cần chặn.
- **Chưa làm ở bản đầu:** nhóm SKU cho lịch định kỳ, ngày nghỉ, ngưỡng duyệt, cửa sổ chốt cuối tháng, nhắc qua SignalR, lịch sử đổi lịch.
- **Quy tắc chưa có GVHD xác nhận:** các mục trong "Quyết định đã chốt", nhất là tự duyệt khi chỉ có một Owner.

## Triển khai

Backend này cần đủ 43 bảng (mốc 03/11/2026; hiện là 44 bảng sau khi thêm `payment_accounts`). Database cũ phải backup rồi chạy `docker/postgres/migrations/20261103_stock_count_schedules.sql`; thiếu bảng thì `/health/ready` báo chưa sẵn sàng. Job chỉ chạy khi `Hangfire:Enabled = true`; khi tắt, kỳ vẫn được sinh lúc tạo hoặc sửa lịch nhưng không tự sinh tháng mới.

## Còn lại

- `P2`: giao diện Admin cấu hình lịch, danh sách kỳ, nộp, duyệt, đếm lại.
- Cập nhật `checklist.md` sau khi xong `P2`.
