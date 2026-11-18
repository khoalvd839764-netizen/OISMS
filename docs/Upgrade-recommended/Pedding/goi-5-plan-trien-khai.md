# Gói 5 — Plan triển khai

Plan cho bốn đầu việc còn mở của Gói 5: [lọc báo cáo theo SKU](./goi-5-bao-cao-doanh-thu-theo-sku.md), [đánh giá dự báo nhập hàng](./goi-5-danh-gia-du-bao-nhap-hang.md), [kiểm chứng NFR](./goi-5-kiem-chung-nfr.md) và [hồ sơ bàn giao](./goi-5-ho-so-ban-giao.md). Lập ngày 15/11/2026, đối chiếu code nhánh `feature/OISMS-59-hoan-thanh-nghiep-vu-con-lai` (commit `b917822`). Các quyết định chốt qua phiên chất vấn cùng ngày.

**Phạm vi môi trường:** mọi test và phép đo trong gói này chạy **local** (Docker Compose và PostgreSQL trên máy dev). Không chạy test, đo tải hay đọc dữ liệu trên bản demo AWS. Tiêu chí nào chỉ chứng minh được trên môi trường deploy thì ghi rõ "chưa đo trên deploy" trong báo cáo, không coi là đạt.

## Hiện trạng code liên quan

| Vùng | Hiện trạng | Hệ quả cho plan |
| --- | --- | --- |
| `ReportFilterDto` | Có `BranchId`, `Channel`, `StartDate`, `EndDate`, `TopN`; chưa có SKU. | Thêm một trường tùy chọn, không đổi contract cũ. |
| `ReportService.GetRevenueProfitReportAsync` | Tải đơn `Confirmed`/`Completed` và phiếu trả `Confirmed` qua `RevenueProfitCalculationHelper`, cộng ở cấp đơn (`TotalAmount`) và cấp phiếu. | Lọc SKU phải chuyển sang cộng ở cấp dòng. |
| `RevenueProfitCalculationHelper` | Đã có `AllocateOrderRevenueByItem` (phân bổ `TotalAmount` theo `Quantity × UnitPrice`) và `AllocateReturnRevenueByItem` (dùng `RefundAmount` từng dòng, sàn dùng `Quantity × UnitPrice`). `DailySalesSummaryService` đang dùng hai hàm này. | Dùng lại, không viết quy tắc phân bổ mới. |
| `ReportsPage` | Chỉ Owner vào được (chuyển `/403`), dù API `revenue-profit` cho Staff và trả `null` giá vốn. Đã có in báo cáo (Gói 4). | Bộ lọc SKU chỉ thêm ở tab P&L, giữ chỉ Owner. |
| `AiForecastingService.GetReorderRecommendationsAsync` | Cửa sổ cố định 30 ngày tính đến `ReportTimeHelper.Today()`; công thức nằm trong vòng lặp của service. | Muốn backtest phải tách công thức thành hàm nhận mốc `asOf`. |
| Đo NFR | `seed_revenue_perf.py/.sql` tạo khoảng 5.000 đơn qua API (khoảng 16 phút). Script đo POS cũ `measure-pos-latency.mjs` đã mất sau khi tách `pos-app`. Hai project test đã có `coverlet`. | Cần script đo mới và dữ liệu SQL bulk. |
| HTTPS | Caddy chỉ có trong `docker-compose.prod.yml` (`docker/caddy/Caddyfile`), dùng biến `ADMIN_DOMAIN`, `POS_DOMAIN`, `SIM_DOMAIN`. Local dev chạy HTTP. | Kiểm TLS bằng cách chạy Caddy local với tên miền `*.localhost` (Caddy tự cấp chứng chỉ nội bộ). |
| SDD | Nằm trên Confluence (page `983053`). Mục 1, 3.4–3.6, 3.10–3.11 chưa đạt theo báo cáo Sprint 5. | Confluence là bản chuẩn; repo chỉ giữ checklist review và bảng đối chiếu code. |

## Quyết định đã chốt

| # | Câu hỏi | Quyết định | Lý do |
| --- | --- | --- | --- |
| Q1 | Lọc một hay nhiều SKU? | **Nhiều SKU**, tham số `skuIds` (mảng Guid), tối đa **20** SKU mỗi lần; vượt trả `400 VALIDATION_ERROR`. Không truyền hoặc mảng rỗng = không lọc. | Cho phép so sánh nhiều mặt hàng; giới hạn để giữ truy vấn và URL gọn. |
| Q1b | Kết quả khi lọc nhiều SKU | KPI, breakdown kênh, chi nhánh, ngày tính trên **tập SKU đã chọn**. Thêm mảng `skuBreakdown` (chỉ có khi lọc): mỗi SKU có `skuId`, `skuCode`, `skuName`, doanh thu, hàng trả, doanh thu thuần, COGS, COGS trả, COGS thuần, lãi gộp, số đơn, số lượng theo từng đơn vị. `OrderCount` ở phần tổng đếm **đơn khác nhau**. Khi không lọc, payload giữ nguyên như cũ. | So sánh được giữa các SKU, không đổi payload và độ trễ báo cáo không lọc. Số lượng tách theo đơn vị để tránh lỗi cộng khác đơn vị (BF-027). |
| Q2 | Doanh thu dòng khi có điều chỉnh cấp đơn | Dùng `AllocateOrderRevenueByItem` cho dòng bán, `AllocateReturnRevenueByItem` cho dòng trả. COGS = `CostPrice × BaseQuantity`; COGS trả = `OriginalUnitCost × BaseQuantity`. | Cùng quy tắc với `daily_sales_summaries`; tổng các SKU khớp tổng không lọc. |
| Q3 | SKU không tồn tại hoặc thuộc tenant khác | **Bỏ qua, trả số 0.** Không thêm mã lỗi. | Global query filter đã ẩn SKU tenant khác; một đường xử lý, không lộ thông tin. |
| Q4 | Phạm vi áp bộ lọc | Chỉ `GET /api/reports/revenue-profit` (tab P&L). Không áp cho Tổng quan, vận tốc tồn kho, cảnh báo. Ẩn khối đối soát `daily_sales_summaries` khi đang lọc SKU. | Đúng `FR-REP-01`; bảng tổng hợp ngày không lưu theo SKU. |
| Q4b | Staff có dùng màn Báo cáo không? | **Giữ chỉ Owner** ở UI. API được test: Staff gọi có `skuIds` nhận `null` ở mọi trường giá vốn/lãi gộp (kể cả trong `skuBreakdown`), Cashier `403`. | Giữ phạm vi gói nhỏ; ma trận RBAC đã ghi `/reports` chỉ mở cho Owner. |
| Q5 | Dữ liệu đánh giá dự báo | **Hai tập, đều chạy local:** (a) tenant `FORECAST-EVAL` sinh 90 ngày lịch sử với các mẫu nhu cầu biết trước (đều, theo tuần, gián đoạn, có ngày hết hàng), seed ngẫu nhiên cố định; (b) bản sao dữ liệu demo restore vào DB local riêng, nếu đủ lịch sử. File dump không commit. Báo cáo ghi rõ (a) là dữ liệu tổng hợp. | Dữ liệu demo ngắn; dữ liệu sinh kiểm được công thức ở nhiều dạng nhu cầu. |
| Q6 | Thước đo và ngưỡng (chốt trước khi chạy) | `H = LeadTime + SafetyDays` theo mặc định API. **WAPE ≤ 35%** cho lượng bán dự báo `velocity × H` so với thực tế trong `[t, t+H)`; **recall ≥ 80%** cho cờ cần nhập. Chỉ tính đạt/không cho SKU có ≥ 10 ngày bán trong cửa sổ 30 ngày. Kỳ có ngày hết hàng báo riêng, không tính WAPE. | WAPE không vỡ khi bán bằng 0; recall đo đúng mục đích không để hết hàng. |
| Q6b | Nếu không đạt ngưỡng | Ghi **CHƯA ĐẠT** trung thực, đề xuất chỉnh thành đầu việc riêng. Gói 5 không đổi kết quả API dự báo. CS-08 đóng ở phần "có đánh giá so với thực tế". | Tránh chỉnh công thức cho khớp dữ liệu đánh giá. |
| Q7 | Dữ liệu đo báo cáo `< 100.000` bản ghi | **SQL bulk** khoảng 100.000 dòng `order_items` và ledger tương ứng, tenant `PERF`, trong **DB local dành riêng để đo**. Ghi rõ dữ liệu không đi qua API. | Tạo qua API mất nhiều giờ; đo đọc không cần luồng ghi. |
| Q8 | Stress và đo tải | **Chỉ local.** Không chạy trên AWS. | Ledger append-only, không ghi dữ liệu thử vào DB demo; theo phạm vi môi trường của gói. |
| Q9 | Chính sách TLS | **Chỉ TLS 1.3**: thêm `tls { protocols tls1.3 }` cho các site trong `docker/caddy/Caddyfile`. Kiểm bằng Caddy chạy local với tên miền `*.localhost`. Áp lên bản demo là bước deploy riêng ngoài gói này. | Đúng nguyên văn đề bài; trình duyệt hiện đại đều hỗ trợ. |
| Q10 | Uptime | **Không đo trên AWS.** Viết script thăm dò `uptime-probe` dùng được cho URL bất kỳ, chạy thử local để kiểm script. `NFR-USA-01` ghi **chưa đo trên deploy**. | Uptime chỉ có ý nghĩa trên môi trường thật; phạm vi gói chỉ local. |
| Q11 | Coverage | Unit + integration (cả nhóm `Postgres*`) bằng `coverlet`, gộp bằng `reportgenerator`. Báo **line coverage theo nhóm lớp**: ghi sổ (`OrderService`, `GoodsReceiptService`, `SalesReturnService`), `ReservationService`, `WacCalculator`, `RevenueProfitCalculationHelper`. Mỗi nhóm ≥ 80%; thiếu thì bổ sung test, không nới phạm vi. | Tránh số toàn solution che phần lõi. |
| Q12 | SDD | **Confluence là bản chuẩn**, nhóm tài liệu sửa trực tiếp. Repo giữ checklist review và bảng so sánh sơ đồ với code do Claude chuẩn bị. | Theo cách làm hiện tại của nhóm tài liệu. |
| Q13 | `NFR-USA-02` thiết bị | **Chỉ giả lập kích thước** 1366×768, 768×1024, 375×812. Ghi rõ không có thiết bị POS cầm tay thật. | Nhóm không có thiết bị. |

## Phase A — Lọc báo cáo doanh thu theo SKU (`FR-REP-01`)

Rủi ro: STANDARD (chỉ đọc, không chạm ledger/transaction). Chạm báo cáo tài chính nên phải giữ che giá vốn với Staff.

### A1. Backend

- `ReportFilterDto`: thêm `List<Guid>? SkuIds`. Kiểm tra số phần tử ≤ 20, loại trùng; vượt trả `400 VALIDATION_ERROR` qua profile `Reports`.
- `RevenueProfitCalculationHelper.QueryRevenueOrdersAsync` và `QueryConfirmedReturnsAsync`: thêm tham số `skuIds`; khi có thì thêm `Where(o => o.Items.Any(i => skuIds.Contains(i.SkuId)))` (phiếu trả: `r.Items.Any(i => skuIds.Contains(i.OrderItem.SkuId))`) để lọc ở PostgreSQL.
- `ReportService.GetRevenueProfitReportAsync`: khi có `SkuIds`, đi nhánh tính theo dòng:
  - Doanh thu: phần tử của `AllocateOrderRevenueByItem(order)` ứng với các dòng thuộc tập SKU.
  - Hàng trả: phần tử của `AllocateReturnRevenueByItem(return)` ứng với dòng thuộc tập SKU; COGS trả bằng `CalculateReturnItemCogs`.
  - Breakdown kênh, chi nhánh, ngày (UTC+07, `GetOrderBusinessDate`, `GetReturnBusinessDate`) dùng cùng số đã lọc. `OrderCount` đếm đơn khác nhau.
  - `skuBreakdown` theo Q1b; số lượng nhóm theo đơn vị, không cộng khác đơn vị.
  - Che `CostOfGoodsSold`, `ReturnCogs`, `NetCogs`, `GrossProfit` (cả trong `skuBreakdown`) khi `!isOwner`.
- Khi `SkuIds` rỗng hoặc `null`: giữ nguyên nhánh cũ. Bằng chứng: toàn bộ `ReportServiceTests` hiện có PASS không sửa assertion.
- Cập nhật `docs/05-api-contracts` cho tham số `skuIds` (query lặp `skuIds=...&skuIds=...`) và `skuBreakdown`.

### A2. Admin

- `types/reports.ts`: `ReportFilter.skuIds`, kiểu `SkuRevenueBreakdown`; `reportService.getRevenueProfit` truyền `skuIds` dạng lặp tham số.
- `ReportsPage` tab P&L: ô chọn nhiều SKU có tìm kiếm (dùng API tìm SKU sẵn có, chọn khi làm), hiện chip SKU đã chọn, nút bỏ chọn, chặn chọn quá 20. Đồng bộ `?skus=` trên URL như `?tab=`. Đổi lựa chọn tăng `activeReqRef` để chặn race.
- Khi đang lọc: hiện bảng `skuBreakdown`, ẩn khối đối soát ngày; tiêu đề in (Gói 4) thêm danh sách SKU.

### A3. Test

| Test | Môi trường |
| --- | --- |
| Đơn có SKU A và B, chiết khấu cấp đơn; phiếu trả riêng A ở ngày khác ngày bán. Lọc [A]: doanh thu = phần phân bổ của A, trừ đúng phần trả A theo ngày trả. Lọc [B]: không bị phiếu trả A ảnh hưởng. Lọc [A, B] = báo cáo không lọc; tổng `skuBreakdown` = KPI. `OrderCount` của [A, B] đếm đơn một lần. | Unit `ReportServiceTests` |
| Lọc kết hợp SKU + chi nhánh + kênh + khoảng ngày; SKU không có giao dịch; SKU tenant khác trả 0; 21 SKU trả `400`; trùng SKU được loại. Staff nhận `null` giá vốn/lãi gộp kể cả `skuBreakdown`. | Unit + controller test |
| Ranh giới ngày UTC+07 với lọc SKU; `Items.Any(... Contains ...)` dịch đúng sang SQL; tenant khác không lọt; Cashier `403`. | Integration PostgreSQL local |
| Đối chiếu UI với API: cùng bộ lọc, KPI và bảng SKU trên màn hình bằng JSON API. | Thủ công trên Admin local, lưu ảnh |

## Phase B — Đánh giá dự báo nhập hàng (`FR-AI-01`, CS-08)

Rủi ro: STANDARD. Không đổi kết quả API hiện có.

### B1. Tách công thức

- Tạo `ReorderForecastCalculator` (Application, hàm thuần) nhận: các dòng xuất bán đã quy về đơn vị gốc, mốc `asOf`, `available`, `reorderPoint`, `leadTimeDays`, `safetyStockDays`; trả `dailyVelocity`, `daysUntilStockout`, `needsReorder`, `suggestedQuantity`, `confidenceLevel`.
- `AiForecastingService` gọi hàm này với `asOf = ReportTimeHelper.Today()`. Thêm hằng `FormulaVersion = "reorder-v1"`.
- Bằng chứng không đổi hành vi: `AiForecastingTests` hiện có PASS không sửa assertion, thêm test so kết quả service với calculator trên cùng dữ liệu.

### B2. Bộ chạy đánh giá

- Project console mới `test-oisms/forecast-eval/` (tham khảo `test-oisms/ai-eval`), đọc DB local **chỉ đọc** qua chuỗi kết nối trong biến môi trường, tham chiếu `OISM.Application` để dùng đúng `ReorderForecastCalculator`. Từ chối chạy nếu host không phải `localhost`/`127.0.0.1`.
- Mốc dự báo `t` mỗi 7 ngày. Ở mỗi `t`, chỉ dùng giao dịch `< t`; thực tế lấy lượng xuất bán trong `[t, t+H)`.
- Tồn tại `t` dựng lại từ `balance_after` của ledger; ngày `on_hand = 0` đánh dấu kỳ hết hàng, báo riêng.
- Phân nhóm SKU: đủ dữ liệu (≥ 10 ngày bán), bán gián đoạn, không bán, có kỳ hết hàng.
- Đầu ra: `results.json` + `report.md` gồm phiên bản công thức, commit, tập dữ liệu, tham số, kết quả theo nhóm so với ngưỡng Q6, top 10 sai số lớn.
- Script sinh tập (a): tạo tenant `FORECAST-EVAL` ở DB local, 90 ngày, cấu hình mẫu nhu cầu trong file, seed cố định để tái lập.
- Tập (b): hướng dẫn restore bản sao demo vào DB local riêng; người dùng tự lấy dump, Claude không truy cập AWS.

### B3. Bằng chứng

- Báo cáo `docs/report-after-upgrade/danh-gia-du-bao-nhap-hang-cs08.md` có lệnh chạy và kết quả, kết luận ĐẠT/CHƯA ĐẠT theo Q6, xử lý theo Q6b.
- Ảnh tab Gợi ý nhập hàng (Admin local) cạnh JSON API cho một SKU đủ lịch sử và một SKU ít dữ liệu.
- Liên kết vào CS-08 và RTM `FR-AI-01`.

## Phase C — Kiểm chứng NFR (local)

Script ở `test-oisms/nfr/`, kết quả thô ở `docs/06-testing-and-qa/evidence/goi-5/<mã NFR>/`. Mỗi kết quả ghi commit, cấu hình máy, dữ liệu, lệnh và trạng thái ĐẠT / CHƯA ĐẠT / CHƯA ĐO TRÊN DEPLOY.

| Mã | Cách làm (local) | Kết luận được |
| --- | --- | --- |
| `NFR-PERF-01` | Script Node không thư viện ngoài `measure-latency.mjs`: đăng nhập, gọi N lần với độ đồng thời 1 và 10, tính p50/p95/p99. Endpoint: `/api/pos/search` (từ khóa và barcode), POS checkout và tạo đơn Admin (mỗi lần `RequestKey` mới), `revenue-profit` không lọc, lọc 1 SKU và 20 SKU. Dữ liệu theo Q7. | Đạt/chưa đạt trên máy dev; ghi cấu hình máy. |
| `NFR-PERF-02` | Chạy lại test 50 webhook tranh một đơn vị trên PostgreSQL local, lưu log `dotnet test` và truy vấn kiểm `available = 0`, 1 thành công, 49 `409`. | Đạt local. |
| `NFR-TENANT-02` | Trên DB đo, `EXPLAIN (ANALYZE, BUFFERS)` cho truy vấn ledger theo SKU/chi nhánh, danh sách đơn theo tenant + ngày, báo cáo doanh thu có lọc SKU (lấy SQL từ log EF). Lưu kích thước bảng và xác nhận dùng index. | Đạt local. |
| `NFR-SEC-01` | Sửa `Caddyfile` theo Q9. Chạy `docker-compose.prod.yml` local với `ADMIN_DOMAIN=admin.localhost`, `POS_DOMAIN=pos.localhost`, `SIM_DOMAIN=sim.localhost` (Caddy tự cấp chứng chỉ nội bộ). Kiểm `openssl s_client -tls1_3` thành công và `-tls1_2` bị từ chối; lưu output. Không đụng DB local đang dùng (dùng project name riêng). | Cấu hình đúng; trên deploy ghi chưa đo. |
| `NFR-USA-01` | Viết `uptime-probe` (gọi `/health/live` và trang POS theo chu kỳ, chỉ tính khung 07:00–22:00 Asia/Saigon, xuất CSV và tổng hợp). Chạy thử local vài giờ để kiểm script. | Chưa đo trên deploy. |
| `NFR-USA-02` | Ghi đường thao tác trên POS local: quét barcode + Enter → F4 → xác nhận, đếm lần bấm. Kiểm ở 1366×768, 768×1024, 375×812 (Q13). Thêm một test `test:live` khẳng định số bước. | Đạt phần giả lập; thiết bị cầm tay thật ghi chưa kiểm. |
| `NFR-MAINT-02` | `dotnet test --collect:"XPlat Code Coverage"` cho hai project với biến `OISM_TEST_*` trỏ PostgreSQL local, gộp bằng `reportgenerator` (cài dotnet tool, hỏi trước khi cài), báo theo nhóm Q11. | Đạt/chưa đạt theo nhóm. |

## Phase D — Hồ sơ bàn giao

Làm sau A–C vì RTM cần liên kết bằng chứng.

- **D1. RTM.** Tách các dòng đang gộp (ví dụ dòng `NFR-PERF, TENANT, SEC, MAINT` ghi số Sprint 2) thành một dòng mỗi mã. Cột: đặc tả, hiện thực, test, bằng chứng, môi trường đo, trạng thái, ngày đối chiếu. Mã chỉ đo local ghi rõ.
- **D2. Kiến trúc.** Vẽ lại sơ đồ gói bằng Mermaid từ tham chiếu thật giữa các `.csproj` và frontend; đối chiếu `ArchitectureRulesTests`; ghi ngoại lệ đã chốt (`IAppDbContext` lộ EF Core).
- **D3. SDD UML 2.0** (Q12). Với mục 1, 3.4–3.6 (mua hàng/WAC, chuyển kho 2 bước, kiểm kê + `PendingApproval`), 3.10–3.11 (nhân sự/RBAC + `user_branches`, báo cáo + lọc SKU + `daily_sales_summaries`): Claude chuẩn bị trong `docs/04-architecture-and-design/sdd-review/` bảng đối chiếu (tên lớp, trạng thái, chuyển trạng thái, khóa, quyền theo code hiện tại) và checklist review. Nhóm tài liệu sửa trên Confluence và đánh dấu đã đối chiếu.
- **D4. Hướng dẫn cài đặt và sử dụng.** Chạy lại hướng dẫn Docker local trên thư mục sạch (không `down -v` DB có dữ liệu). Viết hướng dẫn sử dụng theo vai trò Owner/Staff/Cashier: luồng chính, quyền cần có, thao tác, kết quả mong đợi, kèm ảnh chụp trên môi trường local.
- **D5. Danh mục hồ sơ** theo sáu nhóm của đề bài, mỗi tài liệu có phiên bản/commit và liên kết; cập nhật checklist 5.2.

## Thứ tự

```text
A (BE → FE) ─────────────┐
B1 → B2 → B3             ├─► C (PERF-01 lọc SKU, TENANT-02) ─► D1, D5
C còn lại song song ─────┘
D2, D3, D4 bắt đầu sớm, cập nhật lại sau A
```

A và B độc lập, có thể giao hai người.

## Lệnh kiểm tra trước khi đóng gói

```bash
dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal
dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false
dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --filter 'FullyQualifiedName~Report|FullyQualifiedName~DailySalesSummary|FullyQualifiedName~AiForecasting'
npm run build --prefix src/frontend/admin-dashboard
```

## Docs phải cập nhật

- `docs/02-functional-requirements/fr-11-reports-analytics.md`: lọc nhiều SKU, `skuBreakdown`, quy tắc phân bổ, phạm vi Q4.
- `docs/05-api-contracts/`: tham số `skuIds`, giới hạn 20, `skuBreakdown`.
- `docs/02-functional-requirements/fr-12-ai-forecasting.md`: `FormulaVersion`, thước đo, ngưỡng và kết quả đánh giá.
- `docs/03-non-functional-requirements/README.md` và từng file NFR: cột bằng chứng trỏ tới `evidence/goi-5/`, ghi môi trường đo là local.
- `docs/03-non-functional-requirements/02-security-and-isolation.md`, `docs/13-deploy-aws/`: chính sách chỉ TLS 1.3 trong `Caddyfile`, ghi chưa áp lên bản demo.
- `docs/06-testing-and-qa/01-requirements-matrix-rtm.md`: D1.
- `docs/04-architecture-and-design/`: sơ đồ gói, thư mục `sdd-review/`.
- `docs/bug-fixaffter/CHECKLIST.md`: CS-08.
- Bốn file `goi-5-*.md`, [Pedding/README.md](./README.md), `checklist.md` mục 5.1, 5.2, 6.1: tick tiêu chí đã có bằng chứng; chuyển Gói 5 sang `Done` khi đủ.

## Rủi ro và giới hạn

- **Chỉ đo local:** độ trễ đo trên máy dev không phản ánh EC2; uptime và TLS trên deploy không có bằng chứng. `NFR-USA-01` và phần deploy của `NFR-SEC-01`, `NFR-PERF-01/02` vẫn mở sau gói này.
- **Lọc SKU lệch tổng:** phân bổ làm tròn có thể lệch vài đồng; test phải dùng đúng quy tắc dồn phần dư của helper, không nới sai số.
- **Đánh giá dự báo trên dữ liệu sinh** chỉ chứng minh công thức chạy đúng với mẫu đã định, không chứng minh độ chính xác với cửa hàng thật.
- **Coverage** có thể dưới 80% ở nhóm ledger/reservation nếu thiếu PostgreSQL; phải chạy đủ nhóm `Postgres*` trước khi kết luận.
- **SDD trên Confluence** ngoài tầm kiểm tra của repo; trạng thái đạt do người review trong nhóm xác nhận.
- **Ép TLS 1.3** khi áp lên deploy có thể chặn thiết bị cũ chỉ hỗ trợ TLS 1.2.
