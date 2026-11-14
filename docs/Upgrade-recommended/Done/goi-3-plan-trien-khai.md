# Gói 3 — Plan triển khai

Plan cho các tiêu chí còn mở trong [Gói 3](./goi-3-vong-doi-don-hang-va-hangfire.md). Lập ngày 15/11/2026.

## Quyết định đã chốt

| # | Câu hỏi | Quyết định | Lý do |
| --- | --- | --- | --- |
| Q1 | Đơn online có lưu `Draft` không? | **Không.** Giữ cách tạo đơn đi qua `Draft → Reserved` trong cùng giao dịch. | `NFR-SEC-02` yêu cầu `tạo đơn → giữ chỗ` nguyên tử. Nếu lưu nháp riêng, hai bước tách thành hai giao dịch, trái yêu cầu. `FR-RSE-02` cũng ghi đơn online vào thẳng `Reserved`. State machine vẫn bắt buộc đi qua `Draft` vì `Order.Reserve()` chặn mọi trạng thái khác. |
| Q2 | Doanh thu tính vào ngày nào? | **Giữ `CreatedAt`** của đơn, chỉ đếm đơn `Confirmed`/`Completed`. Hàng trả theo `ConfirmedAt` của phiếu trả. | Không đổi báo cáo hiện có. Đổi lại, job phải tính lại một cửa sổ nhiều ngày. |
| Q3 | Báo cáo có đọc từ `daily_sales_summaries` không? | **Không.** Bảng chỉ để tổng hợp trước và đối soát. | Ít rủi ro, đủ cho `FR-SIM-03`. |

Cách diễn giải `NFR-SEC-02` theo Q1, ghi vào docs ở bước A1:

| Kênh | Giao dịch 1 | Giao dịch 2 |
| --- | --- | --- |
| POS | Tạo đơn + giữ chỗ + trừ tồn + ghi sổ `OUT` + `Completed` | — |
| Admin, sàn | Tạo đơn + giữ chỗ (`Reserved`) | Xác nhận: trừ `OnHand`, giảm `Reserved`, ghi sổ `OUT`, chụp `CostPrice` |
| Hủy / hết hạn | Chỉ giảm `Reserved`, không ghi `IN` bù | — |

Đơn online không thể gộp xác nhận vào giao dịch tạo đơn vì `FR-ORD-03` yêu cầu nhân viên duyệt. Mỗi giao dịch tự nó rollback 100% khi lỗi.

## Phase A — Vòng đời đơn

### A1. Docs ranh giới giao dịch

- `docs/02-functional-requirements/fr-06-omnichannel-orders.md`: ghi `Draft` là trạng thái khởi tạo trong giao dịch, không lưu với đơn online.
- `docs/04-architecture-and-design/04-state-machines.md`: thêm bảng ranh giới giao dịch theo kênh như trên.
- `docs/03-non-functional-requirements/03-reliability-and-acid.md`: ghi cách diễn giải `NFR-SEC-02`.

### A2. Đơn nháp POS do `POST /api/inventory/reserve` sinh ra

`ReservationService` tự tạo đơn POS ở `Draft` và tạo reservation `Active`, tức là đơn nháp đang giữ tồn. Endpoint này không có frontend nào gọi, chỉ dùng cho test.

- Không đổi sang `Reserved`. `Order.CanCancel` của đơn POS chỉ cho hủy khi `Draft`, và `CanExpireReservations` dựa vào `CanCancel`. Đổi trạng thái sẽ làm job hết hạn không giải phóng được đơn này.
- Ghi chú trong code và `fr-06` rằng đây là luồng giữ chỗ kỹ thuật của POS. Thêm test xác nhận job hết hạn vẫn giải phóng đúng số lượng.

### A3. Test vòng đời trên PostgreSQL thật

Bổ sung vào `OnlineOrderLifecycleIntegrationTests` hoặc tạo class `Postgres*` mới:

1. Bước nhảy trái phép trả `INVALID_STATE_TRANSITION`: `Cancelled → Confirmed`, `Reserved → Completed`, xác nhận lần hai.
2. Lỗi giữa giao dịch tạo đơn (ví dụ dòng thứ hai hết tồn): không còn đơn, không còn reservation, `Reserved` không đổi.
3. Lỗi giữa giao dịch xác nhận: `OnHand`, `Reserved`, sổ kho và `CostPrice` không đổi.
4. Xác nhận và job hết hạn chạy song song trên cùng đơn: chỉ một bên thắng, `Reserved` giảm đúng một lần.
5. Đối soát sau cả vòng đời: tổng `IN − OUT` trên sổ bằng `OnHand`, `Reserved` khớp tổng reservation `Active`.

Test nào đã có sẵn thì chỉ trỏ tới, không viết lại.

## Phase B — Job tổng hợp báo cáo hằng ngày

### B1. Entity và cấu hình EF

- `OISM.Domain/Entities/DailySalesSummary.cs` theo đúng cột schema (`docs/database/oisms_schema.sql`).
- `Persistence/Configurations/DailySalesSummaryConfiguration.cs`, `DbSet` và query filter tenant trong `AppDbContext`.
- Schema, `RequiredTables` và `init-oisms.sh` đã có bảng nên **không cần migration**. Thêm một test so cấu hình EF với cột thật để bắt lệch tên.

### B2. Service tổng hợp

`OISM.Application/Features/Reports/Services/DailySalesSummaryService.cs`, hàm `RebuildAsync(tenantId, businessDate)`:

- **Dùng chung quy tắc với `ReportService`.** Tách phần gom số liệu (lọc đơn, quy đổi ngày UTC+07 qua `ReportTimeHelper`, tính giá vốn hàng trả) thành helper dùng chung, để báo cáo trực tiếp và job không lệch nhau.
- **Số lượng theo đơn vị gốc** (`BaseQuantity`), không cộng `Quantity` khác đơn vị (tránh lặp lại BF-027).
- **Ghi:** mở transaction → `pg_advisory_xact_lock(tenant, ngày)` qua `ISqlLockService` → xóa các dòng của tenant/ngày → chèn lại → commit. Lượt chạy chồng nhau phải đợi khóa nên kết quả cuối cùng giống nhau.
- Không ghi dòng toàn số 0.

### B3. Job Hangfire

`OISM.Infrastructure/Jobs/DailySalesSummaryJob.cs`:

- Duyệt tenant trong `OpenPrivilegedScope()`, mỗi tenant gọi `RebuildAsync` cho cửa sổ **7 ngày gần nhất** (từ hôm qua lùi về). Cửa sổ đặt qua cấu hình `Reports:SummaryRebuildDays`.
- Lỗi một tenant: ghi log và chạy tiếp tenant khác. Job chỉ báo fail nếu có tenant lỗi.
- Đăng ký trong `HangfireConfigurationExtensions`: id `oisms-daily-sales-summary`, cron `30 0 * * *`, múi giờ +07:00. Thêm vào DI giống hai job hiện có.

### B4. Chạy lại ngày lỗi

- `POST /api/reports/daily-summaries/rebuild` trong `ReportsController`, chỉ `Owner`, body `{ fromDate, toDate }` (tối đa 31 ngày), tenant lấy từ JWT.
- Thao tác tính lại cho cùng kết quả nên không cần `Idempotency-Key`.
- Khi Hangfire tắt, endpoint vẫn chạy đồng bộ, để demo và test không phụ thuộc Hangfire.
- `GET /api/reports/daily-summaries?fromDate&toDate&branchId` chỉ `Owner`: mỗi ngày trả số từ bảng tổng hợp (doanh thu, hàng trả, giá vốn, lãi gộp, `updatedAt`) cạnh số báo cáo trực tiếp và cờ `matched`.

### B5. Frontend Admin

Trang Báo cáo (`ReportsPage.tsx`, đã chỉ cho Owner) thêm khu "Tổng hợp theo ngày":

- Bảng theo ngày: số tổng hợp, số trực tiếp, trạng thái Khớp / Lệch / Chưa tổng hợp, lần cập nhật cuối.
- Nút "Tính lại" theo khoảng ngày đang lọc, khóa nút khi đang chạy, báo lỗi theo `ProblemDetails`.
- Gọi qua `reportService.ts`; cập nhật `docs/11-ui-screens/08-report-screens.md`.

## Phase C — Bằng chứng nghiệm thu

### C1. Test integration PostgreSQL cho job

Dữ liệu: 2 tenant, 2 chi nhánh, 3 kênh, đơn nhiều đơn vị tính, đơn tạo ngày D xác nhận ngày D+2, hàng trả ngày D+1 cho đơn ngày D, đơn bị hủy.

1. Tổng doanh thu, giá vốn, hàng trả của từng ngày khớp `GetRevenueProfitReportAsync` cùng khoảng ngày.
2. Đơn xác nhận muộn: trước khi xác nhận ngày D không có đơn; sau khi chạy lại thì có.
3. Chạy hai lần liên tiếp: số liệu giống hệt.
4. Hai lượt chạy song song cùng tenant/ngày: không trùng dòng, không lỗi unique.
5. Tenant A chạy không ghi dữ liệu của tenant B.
6. Cashier và Staff gọi endpoint rebuild nhận 403.

### C2. Chạy thử trên Docker

Bật `HANGFIRE_ENABLED=true`, trigger job trên Hangfire, chụp số dòng trong `daily_sales_summaries` và so với API báo cáo. Ghi kết quả vào `fr-10`.

### C3. Cập nhật docs

- `fr-10`: bỏ dòng "chưa có job tổng hợp", thêm job, giờ chạy, cửa sổ, cách chạy lại.
- `fr-11`: ghi quy tắc ngày (Q2) và vai trò bảng tổng hợp (Q3).
- `docs/05-api-contracts/`: endpoint rebuild.
- `docs/database/README.md` mục 28: bảng đã được dùng.
- `checklist.md` mục 5.1, `Pedding/README.md`. Khi đủ bằng chứng, chuyển file Gói 3 sang `Done/`.

## Thứ tự và rủi ro

1. A1 → A2 → A3 (chỉ docs và test, rủi ro thấp).
2. B1 → B2 → C1 (lõi; chạm báo cáo, giá vốn và khóa nên là thay đổi rủi ro cao, phải có bằng chứng PostgreSQL).
3. B3 → B4 → C2 → C3.

Phase A và Phase B không phụ thuộc nhau, có thể làm song song.

| Rủi ro | Cách giảm |
| --- | --- |
| Tách helper làm đổi kết quả báo cáo hiện tại | Chạy `ReportServiceTests`, `PostgresSalesReturnIntegrationTests`, `MultiUnitFlowIntegrationTests` trước và sau khi tách |
| Đơn xác nhận sau cửa sổ 7 ngày | Ghi rõ giới hạn trong `fr-10`; dùng endpoint rebuild cho ngày cũ |
| Job chạy lâu khi nhiều tenant | Mỗi tenant/ngày một transaction ngắn, không giữ khóa qua nhiều ngày |

## Lệnh kiểm chứng

```bash
dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal
dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false
dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~DailySalesSummary|FullyQualifiedName~OnlineOrderLifecycle|FullyQualifiedName~ReservationExpiry"
```
