# Plan hoàn thiện Frontend Admin — PR26 / v1.1

Ngày 28/10/2026. Trạng thái **PLANNED — chờ hoàn tất POS** theo yêu cầu người dùng. Đây là kế hoạch cho phần Admin của Phúc Hữu, không thay kế hoạch POS và không bắt đầu implementation trong lượt này.

Cập nhật v1.1 theo yêu cầu người dùng: **gộp 6 hạng mục A01–A06 thành 3 đợt B01–B03** để giảm số lần bàn giao/review và kiểm thử lặp. Giữ toàn bộ AC và mã finding; A01–A06 bên dưới là nhóm công việc để truy vết, không còn là 6 task phải nghiệm thu riêng.

## Mục tiêu và điều kiện bắt đầu

Admin thực hiện đơn hàng, chuyển kho, kiểm kê, báo cáo và realtime bằng dữ liệu server thật; không coi local fallback hoặc build xanh là nghiệp vụ đã đạt. Đầu vào [review R1](./reviews/pr26-review-r1.md), PR26-F01..F08 OPEN.

1. POS hoàn tất B02 và B03, có nghiệm thu các AC bắt buộc và trạng thái tích hợp rõ ràng. Không coi B01 đã VERIFIED hoặc B02 chỉ READY_FOR_REVIEW là POS xong.
2. Bảo toàn/persist toàn bộ working tree POS và tài liệu trước khi chuyển nhánh; không checkout/reset/clean ghi đè để lấy code Admin. Không tự thao tác git trong plan này.
3. Tại lúc bắt đầu, đọc lại AGENTS/skill, fetch/read trạng thái mới, pin branch/head/develop thực tế và review diff mới. Làm fix trên `feature/OISMS-43-orders-transfers`; không làm Admin trên nhánh POS. Snapshot R1 chỉ là bằng chứng, không copy đè lên branch mới.
4. Nếu POS tích hợp vào develop sau review này, đưa thay đổi develop vào nhánh Admin theo quy tắc repo và giải quyết conflict có đối chiếu cả hai bên. Chạy lại regression bị ảnh hưởng; không mặc định giữ hết phía nào.

Baseline tham chiếu: PR26 head `403431446d213bbbf7f27d01b9622672f2fa4653`, develop `ecc0c85c7d6f81841d8138ff61ae40d33ef013d2`, ahead 3/behind 0 lúc R1. Hiện PR trỏ main; feature thực 28 files so develop. [Metadata/evidence](./evidence/pr26-r1/README.md). Không dùng SHA này thay cho head mới ở lượt triển khai.

Nguồn yêu cầu: `docs/08-sprints/sprint-3/README.md`, FR Order/Inventory/Reports/SignalR, `docs/05-api-contracts/`, backend DTO/controller/service hiện hành, quy tắc GitHub ở Sprint 2; báo cáo nền `docs/09-personal-reports/phuc-huu/sprint-2/S2_ADMIN_REPORT_2026-10-18.md`. Đọc nguồn liên quan từng task, không audit toàn repo lại.

## Ba đợt triển khai và nghiệm thu

| Đợt | Phạm vi gộp / AC gốc | Kết quả và finding | Phụ thuộc / trạng thái |
| --- | --- | --- | --- |
| **S3-ADMIN-B01 — Contract, API đọc và đơn hàng** | A01-AC-01..04 + A02-AC-01..04 | API đọc chứng từ thật, session/context contract; Orders tạo/giữ chỗ/duyệt/hủy/in. Đóng F04, chuẩn bị nền cho F02/F06. | Sau POS; **PLANNED** |
| **S3-ADMIN-B02 — Chuyển kho và kiểm kê** | A03-AC-01..04 + A04-AC-01..04 | Hai luồng kho dùng API thật, state machine/retry nhất quán, lưu/chốt đúng số đếm, drift. Đóng F01/F03/F05/F06. | B01; **PLANNED** |
| **S3-ADMIN-B03 — Báo cáo, realtime và nghiệm thu cuối** | A05-AC-01..04 + A06-AC-01..05 | Dashboard/report/filter; Hub theo phiên/branch, reconnect; E2E liên thông POS và PR develop. Đóng F02/F07/F08 và kiểm tra trạng thái mọi finding. | B01–B02; **PLANNED** |

Mỗi đợt **một prompt, một brief, một handover và một review tổng hợp**. Bên trong đợt, Anti tự kiểm tra và sửa các phần liên quan trước khi bàn giao, không dừng chờ Codex nghiệm thu từng hạng mục A. Ví dụ B01 hoàn thiện query contract trước khi nối/test UI; đây là thứ tự kỹ thuật trong cùng đợt, không tách thành hai vòng review. Chỉ triển khai đợt sau khi đợt trước được nghiệm thu.

Chọn 3 đợt để giữ ba kết quả có thể đánh giá riêng: dữ liệu/đơn hàng, nghiệp vụ kho, tích hợp cuối. Hai đợt sẽ gom nhiều luồng contract/stock/realtime vào cùng một lần debug hơn. Số vòng sửa phụ thuộc lỗi tìm thấy, không mặc định mỗi đợt phải qua 3 vòng hoặc đủ số vòng thì tự đạt.

Bảng dưới chỉ giữ mapping công việc cũ; **không tạo handover/review riêng cho A01–A06**:

| Hạng mục | Kết quả cần đạt | Finding chính | Thuộc đợt |
| --- | --- | --- | --- |
| A01 | Khóa contract/session và API đọc chứng từ thật | F04; chuẩn bị F02/F06 | B01 |
| A02 | Orders Center và luồng tạo/duyệt/hủy/in thật | Regression Orders, yêu cầu FR-ORD-03 | B01 |
| A03 | Chuyển kho 2 bước đúng state, không giả thành công | F01/F05, phần transfer F06 | B02 |
| A04 | Kiểm kê lưu/chốt đúng số đếm, drift/retry an toàn | F03, phần count F06 | B02 |
| A05 | Dashboard/báo cáo/filter/role và thời gian đúng | Các gate report còn thiếu | B03 |
| A06 | Hub theo phiên/branch, tích hợp E2E và PR develop | F02/F07/F08, nghiệm thu toàn bộ | B03 |

## Cách giảm vòng debug và kiểm thử lặp

- Đầu mỗi đợt pin contract/fixture/backend test/provider một lần; dùng chung cho các nhóm A trong đợt. Không mock rộng đến mức mọi query đều trả cùng dữ liệu hoặc mọi lỗi thành success.
- Với finding cần sửa, lập bản đồ caller/API/state liên quan và quy tắc đúng trước khi thay product code; tạo regression chạy FAIL đúng lỗi trên baseline rồi PASS cùng assertion sau sửa. Dùng callback/payload thật của UI và contract backend, không chỉ kiểm tra helper trong trường hợp đầu vào lý tưởng. Làm các bước này trong cùng đợt, không thêm vòng người dùng duyệt.
- Anti tái hiện finding bằng regression có assertion hành vi, sửa nguyên nhân và chạy checks liên quan trước khi gửi READY_FOR_REVIEW. Bàn giao đủ raw log/source hash/provider và một bảng AC của cả đợt.
- Có một lệnh hoặc danh sách lệnh nghiệm thu cố định cho cả đợt với cwd/provider/setup/timeout/cleanup rõ; kết quả cần phân biệt PASS/FAIL/NOT RUN. Gate tích hợp bắt buộc chưa chạy không được thay bằng build/unit xanh hoặc lời khẳng định100%.
- Codex review cả đợt, gom các lỗi đã có bằng chứng trong một báo cáo và một prompt fix. Vòng sau ưu tiên ID còn mở và regression trực tiếp bị ảnh hưởng; giữ phần đã đóng, không bắt đầu lại toàn đợt.
- Test phạm vi hẹp khi sửa từng lỗi; chạy build/regression liên quan trước bàn giao đợt. Giữ kết quả đã pass khi source/dependency/môi trường/rủi ro không đổi. Bộ E2E liên thông đầy đủ chạy ở B03 trên head tích hợp; các kiểm chứng DB riêng bắt buộc của B01/B02 vẫn phải chạy tại đợt tương ứng.
- Sau hai vòng review–fix cùng lỗi chặn, đánh giá lại nguyên nhân, contract và harness trước khi giao vòng tiếp; không lặp cùng prompt/cách thử. Thiếu môi trường hoặc quyết định thật sự thì ghi điều kiện gỡ rõ, tiếp tục phần độc lập trong đợt.
- Một hạng mục có phụ thuộc thiếu chỉ chặn phần cần phụ thuộc đó; không yêu cầu dừng mọi việc còn lại trong đợt. Chưa đủ AC bắt buộc thì chưa nghiệm thu đợt, không giảm yêu cầu để giảm số vòng.

## AC gốc được giữ nguyên để truy vết

## A01 — Contract và dữ liệu đọc thật

Vai trò: Phúc Hữu/Antigravity frontend; **phụ thuộc Backend 2 (Nhựt)** cho query chứng từ. API đọc là bổ sung cần thiết, không viết lại engine mutation Backend 1. Nếu backend mới đã cung cấp route tương đương thì nối route hiện có thay vì triển khai trùng.

Phạm vi dự kiến: StockTransfers/StockCounts controller + query methods hiện NotImplemented hoặc query service thích hợp, tests query/tenant; services/types http Admin liên quan; tài liệu contract. Backend mutation, ledger, WAC, schema không thuộc A01.

| AC | Kết quả và kiểm chứng |
| --- | --- |
| A01-AC-01 | Pin baseline sau POS; lập bảng route/DTO/filter/status/role/errorCode của orders/transfers/counts/reports/Hub theo source hiện tại. Không dùng comment TODO thay contract. |
| A01-AC-02 | GET list/detail transfers/counts hoạt động với dữ liệu thật, filter/page/totalCount nhất quán; 404 đúng cho ID không thấy, không rò tenant. Query tenant A không lấy dữ liệu B, Cashier bị từ chối đúng; kiểm chứng HTTP và provider DB thật phù hợp. |
| A01-AC-03 | Frontend đọc đúng DTO, gồm number/productName, phân trang đủ; browser mới/session mới cùng tenant đọc chứng từ cũ từ API. Lỗi/thiếu endpoint hiển thị error/stale/retry, không hiện [] hoặc cache cũ như fresh. |
| A01-AC-04 | Chốt cách context owner/tenant/token/branch được truyền và vô hiệu async response/cache cũ cho service mới; auth 401/403 không bị fallback che. Không đổi quyền report Owner/Staff khi nguồn yêu cầu chưa thống nhất. |

Đóng F04 khi có API+UI và bằng chứng; nếu chưa có quyền/môi trường backend thì ghi phụ thuộc BLOCKED, tiếp tục phần contract/front độc lập, không tự gọi verified. Kết quả A01 được đưa chung vào handover **B01**, không chờ review A01 riêng trước khi làm phần A02 đủ điều kiện.

## A02 — Orders Center và chứng từ giao hàng

Phạm vi: OrdersPage/orders components/orderService, phần apiAdapter tạo đơn liên quan, tests và print assets cần thiết. Giữ engine Order/Reserve hiện có; không thêm luồng trừ tồn khác.

| AC | Kết quả và kiểm chứng |
| --- | --- |
| A02-AC-01 | List/search/status/channel/branch/date/page khớp contract OrderFilterDto; thay filter nhanh hoặc response đảo thứ tự không vẽ dữ liệu query/session cũ. |
| A02-AC-02 | Tạo đơn Admin và giữ chỗ thật, key/payload ổn định cho cùng ý định, lỗi thiếu tồn/400/401/403/409/network có phân loại; reload/reauth không gửi yêu cầu của owner cũ bằng phiên mới. Không đánh dấu thành công trước server. |
| A02-AC-03 | Detail/action matrix Draft/Reserved/Confirmed/Processing/Completed/Cancelled bám backend thực; confirm/process/complete/cancel thành công thì refetch server, không fake state local. Lỗi 409 hoặc detail response cũ không thao tác nhầm đơn. |
| A02-AC-04 | Notification link orderId mở đúng đơn; in phiếu giao hàng đúng server snapshot, không in nhầm đơn/hở COGS theo role/loại phiếu. Print preview A4 hoặc định dạng đã chốt rõ; không claim chỉ `window.print()` là đã kiểm chứng. |

Kiểm chứng component/browser fixture các nhánh lỗi/race + backend test HTTP create/reserve/lifecycle/refresh, đối chứng tồn. Không tái nghiệm thu toàn bộ POS; chạy regression tích hợp bị tác động. Báo cáo A01+A02 chung tại `batch-01-contract-orders/`.

## A03 — Chuyển kho đúng hai bước

Phạm vi: transfers UI/service/type; tests fixture và backend integration hiện có. Giữ Draft→InTransit→Received, Cancel Draft theo contract. Không tự thêm reject/return nghiệp vụ hoặc rollback OUT khi hàng đã xuất.

| AC | Kết quả và kiểm chứng |
| --- | --- |
| A03-AC-01 | F01: POST lỗi 400/403/409/500/network không sinh số phiếu hoặc đổi business state local; cache nếu giữ chỉ có nhãn/nguồn và context rõ. Response unknown có recovery, không fake success. |
| A03-AC-02 | F05: action matrix đúng; Draft tạo/dispatch/cancel, InTransit receive, trạng thái cuối không có action sai. Không hiển thị Rejected khi server không hỗ trợ. |
| A03-AC-03 | Phần transfer F06: payload key/header ổn định xuyên retry; lost response sau commit rồi replay chỉ 1 phiếu, double click không tạo trùng; mapping number/branch/warehouse/productName/WAC đúng server. |
| A03-AC-04 | Browser mới đọc được phiếu A tạo; dispatch giảm nguồn đúng 1 lần, receive tăng đích đúng 1 lần, retry không ghi ledger đúp; thiếu tồn/phiếu sai tenant/lỗi giữa bước không giả hoàn tất. Dùng test DB và đối chứng chứng từ/số dư thật. |

Đưa kết quả A03 vào handover **B02**, đóng F01/F05 và nhánh transfer F06; không đóng toàn F06 trước khi đủ kiểm chứng A04 trong cùng đợt.

## A04 — Kiểm kê đúng số thực tế và xử lý drift

Phạm vi: counts UI/service/type, stable pending identity và tests trực tiếp; giữ snapshot sequence, engine adjustment/WAC backend.

| AC | Kết quả và kiểm chứng |
| --- | --- |
| A04-AC-01 | Tạo ALL/PARTIAL đúng kho/branch/SKU; phần count F06 giữ key/payload xuyên timeout/retry/reopen theo recovery policy; browser mới đọc lại snapshot API, không trùng đợt khi replay. |
| A04-AC-02 | F03: server lưu 10, UI sửa 7 rồi Chốt phải lưu/chốt 7 hoặc chặn dirty rõ ràng. Không confirm khi record lỗi. Phiếu Draft chưa đếm/thiếu dòng không tự thành đủ; nếu người dùng chủ động chọn gán tồn sổ sách thì phải ghi record thật trước. |
| A04-AC-03 | Số 0 hợp lệ, số âm/NaN/đơn giá surplus thiếu bị chặn đúng; không tự mặc định unit cost=0. Record/confirm payload khớp bản đếm được xác nhận. |
| A04-AC-04 | `STOCK_COUNT_SEQUENCE_DRIFT` được phân biệt với 409 khác. Tạo giao dịch xen ngang trên test DB → drift không ghi adjustment/ledger; hướng dẫn kiểm kê lại theo backend policy, không tự refresh sequence để vượt guard. Confirm/retry thành công tạo đúng 1 adjustment, số dư/ledger đúng. |

Báo cáo A03+A04 chung tại `batch-02-transfers-counts/`, đóng F03 và F06 khi cả transfer/count đủ bằng chứng; review chung một lần cho đợt B02.

## A05 — Báo cáo và Dashboard đúng dữ liệu/session/filter

Phạm vi: ReportsPage/reportService/types, DashboardPage và mapping nguồn liên quan. API revenue-profit/inventory-velocity/stock-alerts đã có; không tạo lại API doanh thu chỉ vì backlog cũ. Nếu phát hiện backend query sai phải có repro/contract và task backend giới hạn riêng.

| AC | Kết quả và kiểm chứng |
| --- | --- |
| A05-AC-01 | Đối chứng net revenue/COGS snapshot/gross profit/WAC inventory/best sellers/slow moving/alerts bằng fixture có kết quả biết trước và test DB. Dashboard không bỏ đơn do parser apiAdapter cũ chỉ nhận array hoặc cộng cả trạng thái chưa ghi nhận doanh thu. |
| A05-AC-02 | Chốt/report rõ timezone nghiệp vụ từ nguồn yêu cầu, kiểm tra today/7days/month/custom, qua nửa đêm Việt Nam, đầu/cuối ngày và precision timestamp. Không gắn Z vào ngày local rồi mặc định đúng; ngày từ/to đảo phải báo lỗi. |
| A05-AC-03 | Filter branch/channel/tab đúng, latest response thắng; lỗi/401/403 không để số cũ dưới nhãn filter mới như báo cáo fresh. Null/empty/0/negative xử lý đúng ý nghĩa, không tự che dữ liệu lỗi. |
| A05-AC-04 | Session/role theo quyết định đã chốt, không lộ COGS cho Cashier; Owner/Staff policy phải được đối chiếu giữa FR/route/backend trước khi đổi. Print report chỉ nội dung và filter hiện hành, kiểm chứng browser preview. |

Đưa kết quả A05 vào handover **B03**; không tự nâng quyền Staff dựa riêng backend đang cho phép.

## A06 — Realtime và nghiệm thu tích hợp cuối

Phạm vi: Admin realtimeAdapter/NotificationCenter/Auth lifecycle, view subscriptions, tests/evidence, cấu hình test CI liên quan và tài liệu/PR. Có thể tái sử dụng bài học POS đã nghiệm thu; không copy singleton khác context hoặc sửa POS ngoài regression có bằng chứng.

| AC | Kết quả và kiểm chứng |
| --- | --- |
| A06-AC-01 | F02: Hub thuộc đúng owner/tenant/token, logout/đổi phiên/deferred start/reconnect cũ không phát event hoặc đổi state phiên mới. Không listener đúp do remount/toggle sound. Initial connect failure có trạng thái/retry thật, không claim automatic reconnect xử lý mọi lỗi start. |
| A06-AC-02 | F07: join/leave branch đúng view/context, nhận OrderCreated/StockLevelChanged/LowStockWarning đúng payload; bảng liên quan refetch API, reconnect refetch dữ liệu bỏ lỡ. Sound mute/autoplay an toàn, link mở đúng order/tab. Sai tenant/branch không cập nhật dữ liệu. |
| A06-AC-03 | Hub transport thật trên backend/test DB được nhận diện, trigger event bằng nghiệp vụ hiện có, mất/kết nối lại và verify rejoin/refetch. Stub là lớp riêng, không coi emitMock/PASS build là transport evidence. Thiếu môi trường bắt buộc phải ghi NOT RUN/BLOCKED. |
| A06-AC-04 | E2E Admin tạo/duyệt/hủy ↔ POS và stock refresh; chuyển kho hai bước; count record/confirm/drift; reports đối chứng. Chạy trên test DB, giữ tenant/role/ledger/idempotency invariants, không dùng DB cá nhân/production để tạo dữ liệu thử. Chạy checks bị ảnh hưởng trên head cuối sau tích hợp. |
| A06-AC-05 | F08: PR base develop, title/body đầy đủ phạm vi và validation, diff chỉ thay đổi cần thiết sau merge develop/POS; giải quyết conflict và CI head cuối pass. Commit/push/cập nhật PR là bước delivery khi đã được người dùng giao thực hiện, không tự làm từ prompt review/plan này. Merge do reviewer quyết định. |

Báo cáo A05+A06 chung tại `batch-03-reports-realtime-final/`, bảng F01..F08 + AC/pass/fail/not run/provider và source hash cuối. Chỉ `READY_FOR_REVIEW`, Codex quyết định `VERIFIED`. Chưa kiểm chứng live DB/Hub hoặc còn finding chặn thì chưa nghiệm thu toàn bộ Admin.

## Cách thực hiện sau POS

Dùng [prompt khởi động B01](./PROMPT-START-B01.md). Khi bắt đầu mỗi đợt, tạo một brief theo mẫu skill tại thư mục batch tương ứng, pin source mới và nêu rõ phạm vi/AC/provider/lệnh; khi xong lưu một handover và evidence chung. Giữ mã finding và ID AC gốc, fix phần còn mở, không xóa review R1. Đánh giá lại plan nếu baseline mới đã sửa một lỗi hoặc thay contract; đóng bằng evidence, không lặp lại sửa đã hoàn tất. Không triển khai B02/B03 Admin trong prompt B01.
