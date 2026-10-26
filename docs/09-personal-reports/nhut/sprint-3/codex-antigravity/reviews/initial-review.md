# Codex initial review — PR #20

Ngày: 2026-10-26. Reviewed commit: `d7fa4bc8738a1f22053d9b313f8e90c423b0483d`. Kết luận: **CHANGES_REQUIRED**.

Đường dẫn và số dòng dưới đây ứng với commit đã pin, trước fix. Dùng `git show <SHA>:<path>` nếu số dòng thay đổi. CodeGraph đã được thử nhưng không trả đúng source backend cho truy vấn review; các kết luận dưới đây đối chiếu trực tiếp source Git và probe độc lập, không coi graph frontend là bằng chứng backend.

## BE2-F01 — P0 — Engine đơn hàng riêng không bảo vệ tồn

**Vị trí:** `src/backend/src/OISM.Application/Features/Orders/Services/OrderService.cs:182`, `:221`, `:262`, `:279`.

`CreateOrderAsync` có thể ghi `Status=Reserved` nhưng không tạo Reservation hay tăng Reserved. Confirm trừ tồn bằng `Math.Max(0, ...)`, không từ chối thiếu tồn, ghi ledger với `WarehouseId=Guid.Empty`; không cập nhật warehouse allocation theo lõi BE1. Probe: tồn 1, đặt 2 vẫn Reserved với 0 reservation; confirm vẫn thành Confirmed, OnHand=0, ledger OUT=2. Đây là service-level proof trên InMemory; PostgreSQL có thể từ chối FK warehouse thay vì lưu thành công, không được dùng kết quả này để khẳng định PostgreSQL chấp nhận ledger lỗi.

**Sửa:** T01 dùng engine BE1 đã nghiệm thu, không vá thêm một engine thứ hai. Regression xác nhận giữ chỗ thật, rollback và warehouse/branch/ledger nhất quán.

## BE2-F02 — P1 — Conflict hợp đồng và route khi tích hợp BE1

**Vị trí:** `Orders/Dtos/OrderDtos.cs`, `Orders/Services/IOrderService.cs`, `Orders/Services/OrderService.cs`, `Controllers/OrdersController.cs`, `Program.cs` dưới backend; `Controllers/SimulatorController.cs:14,39`.

Merge preview có 5 conflict Git. Ngoài ra SimulatorController của BE2 và WebhooksController của BE1 cùng khai báo `POST /api/webhooks/simulator/{channel}`; Git không báo add/add vì khác tên file. Nếu giữ cả hai, routing runtime có thể báo ambiguous endpoint. DTO/signature BE2 cũng không giữ các contract POS/complete/expiry của BE1.

**Sửa:** T01 giữ API/mutation BE1, bổ sung query theo contract chuẩn, một webhook ingress; test khởi động DI và request HTTP thực sau khi ghép. Đây là vấn đề tích hợp với baseline BE1, không phải lỗi route hiện có trên nhánh Nhựt đứng riêng.

## BE2-F03 — P1 — Staff truy cập báo cáo doanh thu / lợi nhuận

**Vị trí:** `src/backend/src/OISM.WebApi/Controllers/ReportsController.cs:14,28`.

Class cho `Owner,Staff` và action revenue không thu hẹp quyền. `DOC-API-04` quy định endpoint này chỉ dành cho Owner. Staff có thể nhận net revenue, COGS và gross profit. Chưa thực hiện HTTP RBAC probe trong lượt lập plan; kết luận dựa vào attribute và contract rõ ràng.

**Sửa:** T03 bảo vệ action revenue với Owner; giữ quyền các báo cáo khác theo contract; test HTTP Owner 200 / Staff 403 / Cashier 403 / anonymous 401.

## BE2-F04 — P1 — Revenue tính cả đơn chưa hoàn tất, thiếu filter SKU

**Vị trí:** `src/backend/src/OISM.Application/Features/Reports/Services/ReportService.cs:36`; `Features/Reports/Dtos/ReportDtos.cs:3`.

FR-REP-01 yêu cầu số liệu từ đơn hoàn tất và filter theo SKU. Service cộng cả `Confirmed`; DTO không có SKU filter. Probe: hai đơn Completed tổng 30 và một Confirmed tổng 100 trả revenue=130 / orders=3 thay vì 30 / 2 theo FR đang lưu. Thời điểm ghi nhận/filter theo CreatedAt hay CompletedAt là chi tiết cần chốt trong T04, chưa coi là một lỗi độc lập đã có contract rõ.

**Sửa:** T04 chốt semantics và triển khai filter SKU không cộng lại toàn bộ đơn khi chỉ chọn một SKU; kiểm thử snapshot giá vốn không đổi khi WAC hiện tại thay đổi.

## BE2-F05 — P1 — Velocity trộn phạm vi chi nhánh và bỏ qua Channel

**Vị trí:** `src/backend/src/OISM.Application/Features/Reports/Services/ReportService.cs:189–234,239–264`.

Sales gộp theo SKU trên nhiều branch nhưng `CurrentOnHand` chỉ lấy balance đầu tiên; slow-moving lặp từng balance và gắn lại doanh số toàn SKU. Filter Channel được nhận trong DTO nhưng không áp dụng cho velocity. `DaysWithoutSale` bị gán hằng 0/30. Probe: tồn 3+7 trả tổng tồn 10 nhưng bestseller OnHand=3; filter Shopee bán 1 lại trả quantity=3 do cộng cả POS; slow-moving có 2 dòng cùng SKU với ngày=30.

**Sửa:** T04 query cùng grain/filter, một dòng/SKU khi xem tổng, ngày không bán tính từ dữ liệu thật và thời điểm kiểm thử cố định.

## BE2-F06 — P1 — Webhook idempotency thiếu shop và chữ ký payload

**Vị trí:** `src/backend/src/OISM.Application/Features/Simulator/Services/SimulatorService.cs:70–83,119–139`.

Lookup bỏ `ExternalShopId`, không so sánh nội dung replay; sau đó gọi engine BE2 tạo Reserved giả. Probe: SHOP-B gửi cùng external order ID được trả đơn SHOP-A; cùng key thay quantity từ 1 thành 3 vẫn trả quantity=1, không báo mismatch.

**Sửa:** T01 loại đường ingest riêng, dùng webhook BE1 với identity `(tenant, channel, external_shop_id, external_order_id)` và payload signature. T05 kiểm chứng shop tách biệt và replay/mismatch qua HTTP + PostgreSQL.

## BE2-F07 — P1 — Batch không concurrent, mọi exception bị đếm là conflict

**Vị trí:** `src/backend/src/OISM.Application/Features/Simulator/Services/SimulatorService.cs:160,184,188,198`.

Vòng for await từng request nên không có tranh chấp thực. Catch mọi exception, gồm cancellation/lỗi hệ thống, đều tăng FailedConflictCount. ID chỉ theo giây và chỉ số nên các batch cùng giây có thể replay nhau. Probe không seed tồn vẫn báo 50 success, 0 conflict, 0 reservation; đây không phải thử nghiệm concurrency PostgreSQL.

**Sửa:** T05 runner/test dùng HTTP ingress canonical, barrier/gate, identity mỗi run riêng, scope/connection riêng; phân loại kết quả và kiểm tra DB cuối cùng. Không chỉ đổi for thành Task.WhenAll trên cùng DbContext.

## BE2-F08 — P1 — Bằng chứng kiểm thử chưa phủ các tuyên bố bàn giao

**Vị trí:** `src/backend/tests/OISM.UnitTests/TestDbContextFactory.cs:17`; báo cáo `S3_BACKEND2_REPORT_2026-10-22.md:9–10,22,72`.

114 unit tests thực sự pass, nhưng các test mới dùng InMemory và kiểm tra happy path. Chưa có bằng chứng mới về HTTP quyền Owner-only, nhiều tenant trong cùng store, real reservation, PostgreSQL transaction/locking hoặc 50 request chồng lấp. Vì vậy kết luận hoàn thành 100% và kiểm chứng concurrent của báo cáo chưa đủ cơ sở; số lượng test đúng không đồng nghĩa đã đủ coverage.

**Sửa:** T03 thêm RBAC/isolation; T05 automated core tests thực; T06 handover truy vết đúng provider, commit, testcase và giới hạn.

## BE2-F09 — P1 — Gói API đọc chưa hoàn chỉnh theo baseline tích hợp

**Vị trí baseline BE1 `4206cc4`:** `OrderService.GetOrdersAsync` / `GetOrderByIdAsync` để stub; `StockTransfersController` và `StockCountsController` chỉ có mutation routes. Nhánh Nhựt có GET order riêng, nhưng không có thay đổi bổ sung list/detail transfer/count trong 19 file bàn giao.

Phân công Sprint 3 v11 yêu cầu API đọc đơn hàng và chứng từ kho. Giải quyết conflict bằng cách giữ BE1 nguyên trạng sẽ bỏ mất phần query; chỉ giữ BE2 sẽ mất engine. Đây là khoảng trống tính năng ở baseline ghép, không phải một regression đã tái hiện bằng HTTP trên nhánh Nhựt đứng riêng. Goods receipts, balances và ledger đã có GET; không cần viết lại các phần đó.

**Sửa:** T02 hoàn thiện read endpoints và filters với tenant scope; kiểm thử GET không gây side effect.

## BE2-F10 — P2 — Hiệu năng và semantics threshold chưa được chứng minh

**Vị trí:** `ReportService.cs:59,172,208,288`.

Revenue tải toàn bộ order/items rồi aggregate trong memory; chưa có benchmark NFR `<2s` dưới 100.000 bản ghi. Stock alert tự bỏ threshold=0 dù FR mô tả `available <= threshold`; cần chốt 0 là disabled hay ngưỡng hợp lệ và có test tương ứng. Chưa đo được một vi phạm performance thực tế, không tuyên bố đã thất bại benchmark.

**Sửa:** T04 ưu tiên projection/aggregate SQL, giới hạn hợp lý theo contract và benchmark PostgreSQL; làm rõ ngưỡng 0 thay vì giấu bằng happy-path test.

## Phân loại ngoài phạm vi

Baseline B-01 từ review BE1 giữ riêng; không đổi ID thành một regression BE2. Không đánh dấu các task BE1 đã VERIFIED là chưa làm chỉ vì nhánh Nhựt chưa nhận baseline. Hai thư mục tài liệu untracked ngoài phạm vi được bảo toàn.
