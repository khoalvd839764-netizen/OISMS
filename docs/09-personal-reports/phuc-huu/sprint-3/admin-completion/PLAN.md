# Plan hoàn thiện sản phẩm Admin — v2.0 / ưu tiên luồng cơ bản

Ngày 28/10/2026. **CORE_PLANNED — chưa chạy smoke toàn sản phẩm trong lượt lập kế hoạch này.**

Theo chỉ đạo mới của người dùng: ưu tiên sản phẩm chạy được các luồng cơ bản; sửa lỗi và hoàn thiện evidence sâu sau. Kế hoạch này thay thứ tự điều phối v1.x: **không chờ B01 VERIFIED hoặc đóng hết E01/E02 mới triển khai luồng kho/báo cáo độc lập**. Giữ nguyên lịch sử review, ID finding và AC đầy đủ bên dưới; không tự biến finding chưa sửa thành CLOSED.

## Thứ tự ưu tiên đã làm rõ

Theo làm rõ của người dùng: **hoàn thiện các phần/tầng cơ bản trước, rồi fix lỗi quan trọng tập trung**. Tái sử dụng kiến trúc/engine hiện có, không xây lại các tầng backend hay database.

1. **Hoàn thiện nền tảng:** đủ màn hình, form, service/API mapping và dữ liệu nền cần cho Orders/Transfers/Counts/Reports; nối endpoint hiện có. Không dừng xây phần độc lập để đóng hết finding ở module trước.
2. **Nối và thử luồng cơ bản:** thực hiện C0–C5 để biết phần nào chạy, phần nào thiếu. Ghi cả bug quan trọng theo ID/ảnh hưởng; lỗi chặn thao tác chỉ sửa tối thiểu đủ để tiếp tục thử, hoặc đánh dấu luồng BLOCKED rồi làm module độc lập khác. Chưa có nghĩa các luồng đều PASS.
3. **Ổn định tập trung:** sau khi nền tảng và wiring đã đủ, gom sửa lỗi quan trọng: dữ liệu/quyền/idempotency/tồn, rồi lỗi chức năng chính. Chạy lại đúng luồng chịu tác động, một lần bàn giao/review tổng hợp. Lỗi phụ tiếp tục backlog.

Các nhãn P0-CORE và yêu cầu sửa trước PASS trong bảng dưới xác định ưu tiên của **đợt ổn định và điều kiện kết luận**, không bắt dừng xây dựng toàn bộ các tầng ở bước 1–2. Chỉ xác nhận CORE_READY_WITH_KNOWN_ISSUES sau bước 3 khi không còn lỗi quan trọng về dữ liệu/quyền hoặc chặn core flow; trước đó báo CORE_IN_PROGRESS/PARTIAL_CORE và lỗi còn mở đúng thực tế.
## Mốc cần đạt và cách báo trạng thái

- **Mốc 1 — CORE_READY_WITH_KNOWN_ISSUES:** một bộ Admin/backend/PostgreSQL đúng phiên bản chạy được toàn bộ checklist luồng cơ bản bên dưới; có hạn chế công khai và backlog. Đây là mốc sử dụng nội bộ/dev-test, không đồng nghĩa nghiệm thu đầy đủ Sprint/production.
- **Mốc 2 — VERIFIED đầy đủ:** xử lý backlog cần thiết và đạt AC gốc/DoD dự án, integration/provider/CI theo phạm vi. Giữ [DoD dự án](../../../../06-testing-and-qa/04-acceptance-criteria-dod.md); không sửa quy tắc toàn repo trong task lập plan này.
- Trước khi đủ mốc 1, báo từng luồng PASS/BLOCKED/NOT RUN. Nếu chỉ một phần chạy, dùng **PARTIAL_CORE**, chỉ rõ luồng dùng được; không báo toàn sản phẩm đã sẵn sàng.
- Không trì hoãn lỗi đã tái hiện làm mất/sai dữ liệu, rò tenant/quyền, tạo chứng từ trùng, giữ/trừ tồn sai hoặc giả thành công. Có thể hoãn tính năng phụ và vô hiệu hóa an toàn phần chưa dùng được; ví dụ tắt realtime lỗi phiên và dùng refetch thủ công. Không cấp thêm quyền hoặc giấu lỗi API để đi tiếp.

## Baseline và phạm vi

Làm trên `feature/OISMS-43-orders-transfers`, head tham chiếu `f276eeed72782f7272f32a62f4bb61ba761f3e89` + working tree v1.4. Pin lại source khi bắt đầu; bảo toàn skill, tài liệu, test và artifact đang có. Develop đã fetch ở lượt trước là `ecc0c85`, không coi là mới nhất mãi.

Đã có: query API đọc chuyển kho/kiểm kê; component tests **13/13 PASS**, sensitivity **2/2 PASS** tại review v1.4; HTTP PostgreSQL **3/3 PASS** reviewer v1.3, hash liên quan không đổi ở v1.4. Đây là đầu vào được tái sử dụng, không phải kết quả smoke toàn sản phẩm. Xem [review v1.4](./batch-01-contract-orders/reviews/v1.4-review.md).

POS tại nhánh riêng `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`, head tham chiếu `ee68bad`, đã được tài liệu POS ghi VERIFIED; chưa là ancestor của Admin tại lần đối chiếu trước. Chạy bản POS đó cùng backend tương thích để smoke liên thông; không tự merge toàn nhánh chỉ để có bản demo. Nếu cần tích hợp source, đối chiếu phạm vi/contract và bảo toàn thay đổi trước khi quyết định cách tích hợp.

Phạm vi sản phẩm: Admin auth/session, dữ liệu nền sẵn có, đơn hàng, chuyển kho, kiểm kê, báo cáo/dashboard, kết nối POS và realtime ở mức phù hợp. Tái sử dụng API/engine hiện có; không viết lại backend tồn/ledger/WAC, thêm module nghiệp vụ, migration hoặc đổi policy quyền để lấp chỗ trống. Bug backend trực tiếp chặn luồng phải có repro/contract và phương án sửa giới hạn; không âm thầm mở rộng thành refactor.

## Thứ tự thực hiện — một đợt hoàn thiện luồng cơ bản

Đây là các checkpoint kỹ thuật trong cùng đợt, không phải sáu task phải chờ Codex duyệt riêng. Anti tự chạy/sửa trong phạm vi, tiếp tục phần độc lập khi một dependency bị chặn. Codex review tổng hợp khi bàn giao hoặc khi có quyết định thật sự cần người dùng.

| Bước / ưu tiên | Kết quả sản phẩm cần có | Việc cần làm và bằng chứng tối thiểu |
| --- | --- | --- |
| C0 — P0, môi trường | Một địa chỉ Admin dùng được với backend đúng source và PostgreSQL test | Ghi source/port/provider/URL; dùng host backend mới hoặc build image dev phù hợp, không tiếp tục dùng image cũ trả GET chứng từ 405. Xác minh login + Orders + GET transfers/counts không 405. Giữ dữ liệu làm việc/production; dùng DB test có guard hoặc fixture hiện có. Không dừng dịch vụ người dùng không liên quan. |
| C1 — P0, phiên và dữ liệu nền | Owner/Staff đăng nhập đúng quyền; SKU/chi nhánh/kho/tồn đọc được | Dùng danh mục có sẵn, chuẩn bị fixture tồn dương qua seed/API đã có; không cập nhật trực tiếp balance/ledger. Cashier bị chặn route Admin theo policy, tenant B không đọc dữ liệu A. Kiểm tra đăng xuất/đăng nhập lại không hiển thị dữ liệu phiên cũ. Không mở rộng thành làm lại catalog. |
| C2 — P0, đơn hàng | Tạo/giữ chỗ → duyệt → xử lý/hoàn tất; nhánh hủy độc lập | Thực hiện trên UI/API thật, mở lại đơn sau reload; đối chiếu trạng thái và tồn trước/sau. Tạo đơn khác để kiểm tra hủy và giải phóng giữ chỗ, không đòi một đơn vừa hoàn tất vừa hủy. Có ca thiếu tồn, double click/retry một ý định không tạo hai đơn. Giữ khóa/payload hiện có và server snapshot. Chưa bắt hoàn thiện print hay mutation-testing sâu. |
| C3 — P0, chuyển kho | Tạo Draft → Dispatch → Receive, đọc lại bằng phiên mới | Sửa những chỗ chặn gọi API/mapping/action matrix; không fake local success. Nguồn giảm đúng khi dispatch, đích tăng đúng khi receive; mỗi bước một lần. Cancel chỉ khi trạng thái backend cho phép. Lỗi POST không đổi state thành thành công; retry giữ ý định. |
| C4 — P0, kiểm kê | Tạo → nhập số đếm → lưu → chốt, reload thấy kết quả server | Chọn cách đơn giản: bắt buộc Lưu trước Chốt nếu dirty, không cần auto-save phức tạp. Ca server lưu 10, UI sửa 7 phải lưu/chốt 7 hoặc chặn rõ; 0 hợp lệ. Record fail không confirm; drift báo kiểm kê lại, không tự vượt guard. Đối chiếu adjustment/số dư thật. |
| C5 — P1, báo cáo và liên thông | Dashboard/report đọc dữ liệu thật; giao dịch Admin/POS cùng phản ánh tồn | Chọn fixture/khoảng ngày biết trước, đối chiếu doanh thu/COGS/quyền theo nguồn hiện hành. Tạo giao dịch từ POS hoặc dùng flow POS đã nghiệm thu trên cùng môi trường test; tải lại Admin để thấy dữ liệu mới. Trạng thái realtime có thể “chưa bật”; refresh thủ công là cách dùng hợp lệ ở mốc 1. |

Không bắt C2 đóng toàn bộ finding/evidence trước khi làm C3/C4. Những luồng dùng chung auth/contract/tồn vẫn phải sửa dependency chung trước; không gọi luồng đã PASS nếu dependency đó thực tế còn sai.

## Checklist smoke mốc 1

Một người thực hiện trên browser và API thật, ghi mã chứng từ/fixture và kết quả. Có thể dùng manual smoke có thể lặp lại; không bắt viết E2E framework mới để đạt mốc cơ bản.

| ID | Thao tác | Kết quả quan sát bắt buộc |
| --- | --- | --- |
| CORE-01 | Login Owner, logout, login Staff; thử Cashier/tenant B | Quyền đúng, không nhìn/thao tác nhầm dữ liệu phiên/tenant khác |
| CORE-02 | Mở SKU/kho/tồn nền hiện có | Dữ liệu từ API, đủ fixture cho các bước tiếp theo, không fake fallback |
| CORE-03 | Tạo đơn Admin đủ tồn → reserve → confirm → process/complete theo contract | Có ID server, state đúng, tồn/giữ chỗ/ledger phù hợp từng bước |
| CORE-04 | Tạo đơn khác → cancel; thử thiếu tồn và gửi trùng/retry | Giải phóng đúng; lỗi rõ; không tạo hai chứng từ hay trừ tồn hai lần |
| CORE-05 | Chuyển kho Draft → dispatch → receive; reload/session mới đọc lại | Nguồn/đích đúng mỗi bước; list/detail không 405 hoặc chỉ đọc cache |
| CORE-06 | Count nhập 7 thay số đã lưu 10 → lưu → chốt; thêm ca 0/dirty/drift | Server chốt đúng số đã xác nhận, hoặc chặn đúng điều kiện; không ghi adjustment sai |
| CORE-07 | Mở dashboard/report đúng role và khoảng ngày của fixture | Có số thật/empty thật, không giả số hoặc hiển thị số cũ dưới filter mới; đối chiếu mẫu số liệu biết trước |
| CORE-08 | Giao dịch POS trên cùng backend/test DB → refresh Admin | Thấy chứng từ/tồn thay đổi đúng; ghi rõ head Admin/POS/backend và kết quả |

Smoke không thay proof transaction/concurrency của engine. Tái sử dụng evidence provider thật còn hợp lệ; khi sửa đường transaction/idempotency/tenant hoặc bằng chứng không khớp source thì chạy test tích hợp liên quan trước khi đánh dấu luồng PASS. Không chạy lại toàn suite chỉ vì đổi CSS/câu chữ.

## Chuyển lỗi sang làm sau

[BACKLOG.md](./BACKLOG.md) là danh sách tiếp tục sau mốc 1, giữ finding ID và điều kiện đóng.

| Mục hiện tại | Quyết định cho mốc 1 |
| --- | --- |
| B01-E01: first-submit/reload sensitivity và manual-retry coverage | Hoãn phần tự động hóa chuyên sâu; giữ code guard hiện tại và smoke cơ bản về phiên, key/payload/retry. Nếu có bug sản phẩm thực gây gửi trùng/chéo owner thì nâng P0 ngay. Không gọi E01 CLOSED. |
| B01-E02: print A4/80mm và UI thừa | Hoãn print hoàn chỉnh, công khai tính năng in chưa nghiệm thu; mở chi tiết đơn để tra cứu tạm. Khi sửa sau chỉ tập trung print scope, không chặn Orders/stock core vì lỗi layout này. |
| B01-E02 / PR26-F04: browser đọc transfers/counts qua backend mới | Làm ngay trong C0/C3/C4 vì trực tiếp chặn luồng kho. Không hoãn dưới nhãn “thiếu evidence”. |
| PR26-F01/F03/F05/F06 | Kiểm tra trên source hiện hành trong luồng C3/C4; lỗi fake success, chốt sai số, action sai và retry trùng có tái hiện phải sửa trước PASS luồng đó. Review R1 là lịch sử, không mặc định bug còn nguyên. |
| PR26-F02/F07 realtime | Nếu chưa an toàn theo phiên/tenant, tắt kết nối/event cũ và dùng refetch thủ công cho mốc 1; khôi phục realtime đúng owner/branch/reconnect sau. Không cho Hub rò dữ liệu chạy chỉ để có thông báo. |
| PR26-F08 và AC release/CI/PR base | Thực hiện ở bước delivery sau khi code sẵn sàng; không đổi PR/merge trong lượt lập plan hoặc tự coi core-ready là đủ merge. |
| Style, responsive phụ, âm thanh, benchmark, coverage mở rộng | Hoãn; chỉ kéo lên nếu trực tiếp khiến không thể thao tác hoặc hiểu sai dữ liệu |

Lỗi mới: nếu chặn một core flow hoặc có rủi ro dữ liệu/quyền thì xử lý tại luồng; nếu không, ghi backlog một dòng, ảnh hưởng và cách tái hiện. Không mở vòng review chỉ để hoàn thiện hình thức báo cáo.

## Bàn giao và ngân sách mềm

- Dùng [prompt thực hiện luồng cơ bản](./PROMPT-START-B01.md). Tên file giữ để các link cũ hoạt động; nội dung v2.0 đã thay giới hạn “chỉ B01”.
- Tái sử dụng brief/evidence hiện có. Khi triển khai, lưu **một handoff core** tại `core-product/handover.md` (chỉ tạo khi có kết quả), gồm head/diff, URL/cách chạy, fixture/test DB, bảng CORE-01..08 PASS/FAIL/NOT RUN, finding làm ngay/để sau và giới hạn.
- Anti bàn giao `CORE_READY_FOR_REVIEW` khi đủ checklist; Codex mới xác nhận `CORE_READY_WITH_KNOWN_ISSUES`. Codex review một lần trên toàn diff đợt và smoke cần thiết; chỉ finding chặn core/an toàn mới yêu cầu fix trước mốc 1. Các review v1.x vẫn là lịch sử nghiệm thu đầy đủ, không dùng chúng để khóa phần độc lập theo thứ tự cũ.
- Checkpoint ban đầu 20–30 phút xác định bộ runtime đúng source; mỗi 45–60 phút triển khai phải có luồng chạy thêm hoặc blocker kỹ thuật cụ thể. Đây là nhịp điều phối, không phải deadline bỏ kiểm tra.
- Nếu cùng lỗi hai lần thử không có tiến triển, đổi giả thuyết/phương pháp và làm phần độc lập. Dependency môi trường thật thiếu thì ghi BLOCKED cho luồng cần nó, không tiếp tục viết báo cáo/test mô phỏng để thay thế.
- Kết thúc đợt khi toàn bộ CORE-01..08 đạt và không còn lỗi chặn an toàn: `CORE_READY_WITH_KNOWN_ISSUES`; chưa đủ thì `PARTIAL_CORE`. Chỉ dùng `VERIFIED` sau mốc 2. Lượt hiện tại mới lập plan, chưa triển khai hoặc smoke mới.

## AC đầy đủ giữ để hoàn thiện sau mốc 1

Các AC A01–A06 bên dưới được giữ nguyên để truy vết mục tiêu cuối. Phần báo cáo/tách B01/B02/B03 và điều kiện đóng toàn finding trong phần này áp dụng cho **nghiệm thu đầy đủ mốc 2**, không còn là yêu cầu chờ tuần tự của mốc 1. Invariant dữ liệu/quyền vẫn áp dụng ở cả hai mốc. ID CORE là tập kết quả vận hành tối thiểu, không thay/xóa ID AC gốc.

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
