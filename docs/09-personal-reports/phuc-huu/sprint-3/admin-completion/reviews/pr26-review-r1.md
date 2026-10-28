# Review PR #26 — R1 / 28-10-2026

Kết luận **CHANGES_REQUESTED**. **PR26-F01..F08 OPEN**. Các mục dưới là lỗi source hoặc phụ thuộc trực tiếp ngăn nghiệm thu Sprint 3; không phải yêu cầu viết lại toàn bộ Admin.

## Baseline và phạm vi

- [PR #26](https://github.com/khoalvd839764-netizen/OISMS/pull/26), tác giả `dophuc799-cpu`, title tại lúc kiểm tra `Feature/oisms 43 orders transfers`.
- Head `403431446d213bbbf7f27d01b9622672f2fa4653`, branch `feature/OISMS-43-orders-transfers`; base hiện tại `main` tại `f1bc1211ddc5eea0972dffa9ba3c06115559b110`.
- GitHub metadata: OPEN, chưa merge, mergeable/clean; **284 file so main**. Compare với `develop` `ecc0c85c7d6f81841d8138ff61ae40d33ef013d2`: ahead 3, behind 0, **28 file**, gồm 27 file Admin và `test-auth.ps1`. Đây là phạm vi feature dùng để review; không coi các thay đổi backend kế thừa là code mới do PR26 viết.
- Snapshot tải bằng codeload tại đúng head vào `D:/OISMS/tmp/pr26-review/snapshot/OISMS-403431446d213bbbf7f27d01b9622672f2fa4653`. Không checkout nhánh chính, không sửa product source của snapshot. CodeGraph ở workspace chỉ có baseline Admin và không trả đủ backend; đã dùng trước rồi đối chiếu source snapshot/DTO thật, không lấy index baseline thay source PR.
- Nguồn AC: phân công Phúc Hữu trong `docs/08-sprints/sprint-3/README.md` (Admin làm nghiệp vụ thật, không sửa state local giả), API/state machine backend tại cùng snapshot, quy tắc PR vào develop tại `docs/08-sprints/sprint-2/CONFLUENCE_QUY_TAC_LAM_VIEC_GITHUB.md`.

Đường dẫn source dưới đây tính từ root snapshot/PR, dòng 1-based tại head nói trên.

## PR26-F01 — P1 — API chuyển kho thất bại vẫn được trả thành công và ghi local như chứng từ thật

- `src/frontend/admin-dashboard/src/services/stockTransferService.ts:82–108`, `:126–159`, `:166–178`.
- Create catch mọi lỗi (kể cả 400/403/409/500/network) rồi tự sinh ID/số TRF/Draft, lưu localStorage và resolve. Dispatch/receive/cancel cũng nuốt lỗi rồi đổi trạng thái cache. Modal/drawer do đó không nhận reject để hiển thị thất bại.
- Probe actual TS, HTTP fixture reject 409 toàn bộ mutation và 405 toàn bộ GET: create vẫn trả Draft với số tự sinh, dispatch vẫn thành InTransit, cancel vẫn thành Rejected. Không có backend nào xác nhận các trạng thái này.
- Đóng khi: chỉ DTO server thành công mới thay business state; lỗi definite phải được trả đúng, unknown result giữ key/payload và trạng thái cần kiểm tra/retry. Không biến cache recovery thành chứng từ đã tạo hoặc chuyển trạng thái thành công. Test lỗi 400/403/409/500/network và response mất sau commit; dữ liệu server là nguồn chuẩn.

## PR26-F02 — P1 — Hub không dừng/đổi context khi logout, tài khoản mới có thể nhận event kết nối cũ

- `src/frontend/admin-dashboard/src/components/NotificationCenter.tsx:34–51`; `src/frontend/admin-dashboard/src/services/realtimeAdapter.ts:165–175`, `:201–210`, `:77–97`.
- Effect start theo `sound`, cleanup chỉ unsubscribe; logout trong AuthContext không stop Hub. Không có caller `realtimeAdapter.stop()` trong source app. start gặp Connected thì return, không xét owner/tenant/token. JWT query factory đổi token không thay tenant groups của transport đã kết nối.
- Probe: start token-A → bỏ token/đăng nhập token-B → start lần nữa → vẫn 1 connection được auth token-A; subscriber mới nhận `PRIVATE-A` từ callback connection A. Provider fake transport chạy actual handler, chưa phát event tenant thật trên live backend.
- Đóng khi: lifecycle gắn owner/tenant/token/context và route lifetime; logout dừng/thu hồi Hub, callback/start/reconnect cũ bị vô hiệu, login B tạo connection đúng B, không trùng listener/late status. Có test A→logout→B và deferred start/reconnect; không thay backend tenant isolation.

## PR26-F03 — P1 — Chốt kiểm kê bỏ qua số lượng đang sửa trên màn hình

- `src/frontend/admin-dashboard/src/components/stockCounts/StockCountDetailDrawer.tsx:120–151`, `:435–441`.
- Số nhập nằm ở countedMap. Nút Chốt chỉ gửi note/skuAdjustmentCosts, không lưu current countedMap và cũng không chặn khi dirty. Backend confirm dùng CountedQuantity đã lưu trước đó.
- Repro **production React/Edge thật**, REST fixture, Hub offline: server/cache đã lưu Counting=10 → nhập 7 → bấm Chốt → **POST duy nhất là `/confirm`, body `{}`**, fixture chốt 10; không POST `/record`. Node component probe độc lập cho cùng kết quả. Không khẳng định ledger PostgreSQL thật đã bị ghi sai trong lượt review.
- Với phiếu mới chưa record, lời mời “gán bằng tồn sổ sách để chốt” cũng không gửi record; backend thật chỉ chốt Counting đủ dòng, nên không thực hiện được lời hứa UI đó.
- Đóng khi: chốt phải lưu đúng snapshot người dùng đã xác nhận rồi confirm, hoặc chặn dirty và yêu cầu lưu rõ ràng; không xác nhận thành công với số đã cũ. Record fail không confirm. Giữ 0 là số đếm hợp lệ, không âm/NaN; xử lý drift theo errorCode thực tế `STOCK_COUNT_SEQUENCE_DRIFT`, không gọi mọi 409 là drift. Có test đã lưu 10→sửa 7→chốt, phiếu mới, thiếu dòng, surplus cost và lỗi record.

## PR26-F04 — P1 / phụ thuộc BE2 — API đọc chuyển kho/kiểm kê chưa được triển khai nhưng UI che lỗi bằng local cache

- `src/frontend/admin-dashboard/src/services/stockTransferService.ts:35–67`, `src/frontend/admin-dashboard/src/services/stockCountService.ts:64–103`.
- Snapshot `StockTransfersController.cs` và `StockCountsController.cs` chỉ có POST, không có GET list/detail. Các method query service vẫn `throw new NotImplementedException`: StockTransferService `:867–874`, StockCountService `:1001–1008`. Đã tìm cả WebApi, không có route query tương đương được map thay thế.
- UI gọi GET các route đó rồi catch trả cache hoặc [] không đánh dấu chưa đồng bộ. Máy khác/browser mới không đọc được chứng từ đã tạo; phiếu local cũ có thể được coi như thông tin hiện hành. Đây là **dependency baseline BE2**, không gán là regression backend do PR26 tạo; nó trực tiếp chặn nghiệm thu tính năng UI đang claim.
- Đóng khi: bổ sung/wire API query đúng phân công BE2, tenant/role scope, filter/pagination/DTO khớp; frontend hiển thị lỗi/not-ready/stale trung thực. Browser mới phải đọc lại được chứng từ thật. Có HTTP/integration tenant A/B và list/detail/not found, không chỉ test mock trả local.

## PR26-F05 — P2 — UI cho Reject khi InTransit nhưng gọi endpoint chỉ cho hủy Draft

- `src/frontend/admin-dashboard/src/components/transfers/TransferDetailDrawer.tsx:194–201`, `src/frontend/admin-dashboard/src/services/stockTransferService.ts:166–178`.
- UI mở “Từ chối nhận” cho InTransit, POST `/cancel`, rồi ghi Rejected. Backend cùng snapshot cho cancel **chỉ Draft** (`StockTransferService.cs:825–827`), status hợp đồng chỉ Draft/InTransit/Received/Cancelled; không có nghiệp vụ Reject InTransit. Khi bỏ catch nuốt lỗi F01, nút vẫn luôn bị 409.
- Đóng khi: UI bám state machine hiện có, cho Cancel ở Draft và Receive ở InTransit, không giả Rejected. Nếu muốn nghiệp vụ trả hàng/từ chối sau dispatch thì phải có quyết định và task backend riêng, không tự thêm rollback/ledger trong scope Admin này. Test action matrix theo status và lỗi 409 thật.

## PR26-F06 — P2 — Retry tạo chứng từ đổi RequestKey, có thể tạo trùng khi response bị mất

- `src/frontend/admin-dashboard/src/services/stockTransferService.ts:73–78`; `src/frontend/admin-dashboard/src/services/stockCountService.ts:109–117`; `src/frontend/admin-dashboard/src/components/stockCounts/CreateStockCountModal.tsx:105–119`.
- Transfer luôn generateUuid mỗi call, kể cả caller có key. Count tạo key khi request không có, trong khi modal không giữ/truyền key. Sau server đã tạo nhưng timeout, bấm lại là ý định cũ với khóa mới. Header/body trong từng lần có khớp nhưng không ổn định xuyên retry.
- Probe hai call create count cùng payload không key sinh `fixture-key-3` rồi `fixture-key-4`. Đây là proof code path đổi identity, không phải số đơn trùng đã đo trên PostgreSQL.
- Đóng khi: key và payload ổn định cho cùng ý định, retry/reauth/reload theo policy recovery phù hợp và owner isolation; chỉ tạo key mới khi ý định mới đã được xử lý rõ. Kiểm chứng lost response + replay tạo đúng 1 chứng từ trên backend test, double click không mở hai operation.

## PR26-F07 — P2 — Stock realtime chưa được nối tới branch group và bảng tồn

- `src/frontend/admin-dashboard/src/services/realtimeAdapter.ts:100–103`, `:213–235` và caller integration.
- `joinBranch/leaveBranch` được định nghĩa nhưng không được gọi trong app. Hub thật chỉ tự join nhóm tenant, StockLevelChanged phát nhóm branch. Không có listener `oism:stock-changed` ngoài dòng dispatch chính nó; InventoryPage/bảng tồn không refetch. Reconnect chỉ cập nhật status/rejoin nếu có currentBranchId, không refresh dữ liệu bị lỡ khi offline.
- Đóng khi: đăng ký/rời đúng branch theo view/context, event liên quan làm authoritative refetch, reconnect khôi phục dữ liệu; bỏ event/callback cũ/sai branch. Có transport event/reconnect test với SDK/service production + browser xác nhận bảng thay đổi không F5; không dùng emitMock làm bằng chứng Hub thật.

## PR26-F08 — P2 / delivery — PR trỏ main thay vì develop theo quy tắc repo

- Metadata GitHub hiện tại `base.ref=main`, 284 files. Quy tắc feature PR của repo yêu cầu vào develop, release mới từ develop vào main. Compare develop chứng minh feature thực chỉ 28 file.
- Đóng khi tới bước tích hợp: cập nhật base develop, viết title/body cho toàn scope Orders/Transfers/Counts/Reports/Realtime, kiểm tra diff/conflict và CI trên head cuối sau POS. Không đổi base hoặc merge trong lượt review/lập plan này.

## Phần đã có và giới hạn nghiệm thu

- Có Orders Center gọi API list/detail/actions thật, DTO normalize hỗ trợ snapshot; không mở finding sai tên pageIndex/search vì controller đang dùng OrderFilterDto khớp frontend.
- ReportsService đã gọi 3 route backend hiện có và DTO cơ bản khớp; **API doanh thu đã có tại snapshot này**. Backlog cũ “chưa có API doanh thu” không mô tả đúng head PR26.
- Có UI chuyển kho, kiểm kê, branch mapping, màn báo cáo và SignalR client chính thức; đây là implementation đầu vào để hoàn thiện, chưa phải nghiệm thu đủ AC.
- Reviewer `npm ci --ignore-scripts --no-audit --no-fund` + `npm run build` trên snapshot: PASS. Có warning chunk JS >500KB, không coi riêng warning là lỗi chặn.
- Hai GitHub checks đã completed/success: Backend Build & Test, Frontend Build. Workflow frontend chỉ install/build, không chạy browser nghiệp vụ Admin. Không dùng màu xanh để đóng các finding trên.
- Node probes actual TS/TSX (HTTP/storage/hooks/fake transport) và Edge production fixture đã tái hiện F01/F02/F03/key behavior. Không chạy live backend/PostgreSQL/Hub transport hoặc script `test-auth.ps1`, không phát sinh giao dịch thật.
- Cần kiểm tra trong plan A02/A05: lookup/list/detail response đảo thứ tự; ngày “Hôm nay” ở Việt Nam so với cách `toISOString()`/gắn `Z` hiện tại (source ReportsPage :58–80, :108–109); ranh giới cuối ngày; Dashboard baseline còn tính từ apiAdapter cũ; print preview chứng từ thật. Đây là kiểm chứng hoàn thiện, chưa gán thêm finding runtime chưa chạy hoặc tự thay chính sách timezone/RBAC. Owner-only report route đang là baseline, backend cho Owner/Staff; cần chốt quyền theo nguồn yêu cầu, không tự cấp thêm quyền.

[Evidence R1](../evidence/pr26-r1/README.md), [plan 6 task sau POS](../PLAN.md). Chưa gửi review/comment lên GitHub hoặc thay base PR.
