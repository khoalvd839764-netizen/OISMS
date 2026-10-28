# Danh sách lỗi cần sửa

Ngày: **28–29/10/2026**. Source: baseline và working tree trong [README](README.md).

## Cách đọc

- **P1:** ưu tiên cao — có nguy cơ sai phiên/quyền, giao dịch lặp, số liệu hoặc kết quả nghiệp vụ sai.
- **P2:** lỗi chức năng, thao tác bị kẹt, thông báo hoặc đồng bộ sai trong các tình huống cụ thể.
- **Tái hiện có kiểm soát:** chạy service/component hiện tại với HTTP/SignalR giả lập hoặc phản hồi được trì hoãn; không ghi vào database đang sử dụng.
- **Xác nhận bằng code:** đường thực thi đã được đối chiếu; tình huống lỗi đầy đủ chưa được chạy trên browser/database thật.
- **Tái hiện trên browser:** quan sát trực tiếp ở localhost, không thực hiện giao dịch ghi dữ liệu.

Mọi mục bên dưới ở trạng thái **OPEN**. Bằng chứng cho các phép thử có kiểm soát nằm trong `evidence/probe-results.json`; tên kịch bản được ghi trong từng lỗi. File đó là phụ lục kỹ thuật, không thay nội dung Markdown này.

## Bảng tổng hợp

| ID | Ưu tiên | Khu vực | Lỗi | Bằng chứng |
| --- | --- | --- | --- | --- |
| BF-001 | P1 | Đăng nhập/HTTP | Phản hồi cũ có thể ghi đè token mới hoặc gửi lại ý định của A bằng token B | Tái hiện có kiểm soát |
| BF-002 | P1 | Realtime | Phiên mới tái sử dụng kết nối SignalR của phiên cũ | Tái hiện có kiểm soát; PR26-F02 |
| BF-003 | P1 | POS | Retry cùng đơn sinh request key mới | Tái hiện có kiểm soát |
| BF-004 | P1 | Chuyển kho | Retry tạo phiếu sinh key mới | Tái hiện có kiểm soát; PR26-F06 |
| BF-005 | P1 | Kiểm kê | Form tạo kiểm kê không bảo toàn key cho lần thử lại | Tái hiện có kiểm soát; PR26-F06 |
| BF-006 | P1 | Kiểm kê | Xóa số đã lưu rồi đồng ý gán tồn sổ sách vẫn chốt số cũ | Tái hiện có kiểm soát; nhánh PR26-F03 |
| BF-007 | P2 | Kiểm kê | Lưu ô đếm trống tự ghi số sổ sách mà không hỏi | Tái hiện có kiểm soát |
| BF-008 | P2 | Chuyển/kiểm kê | Đổi chứng từ trong lúc request cũ chạy có thể khóa nút vĩnh viễn | Tái hiện có kiểm soát |
| BF-009 | P1 | Báo cáo | Phản hồi A đến muộn ghi đè dữ liệu của bộ lọc B | Tái hiện có kiểm soát |
| BF-010 | P2 | Báo cáo | API lỗi nhưng số liệu cũ vẫn hiển thị dưới bộ lọc mới | Tái hiện có kiểm soát |
| BF-011 | P1 | Báo cáo | “Hôm nay” và biên ngày dùng UTC sai với ngày Việt Nam | Tái hiện có kiểm soát |
| BF-012 | P2 | Chuyển kho | Chọn kho nguồn nhưng hiển thị tổng tồn toàn tenant | Tái hiện có kiểm soát |
| BF-013 | P1 | Giữ chỗ kho | Sửa form khi kết quả chưa rõ làm mất ý định/key cũ | Xác nhận bằng code |
| BF-014 | P2 | Chi nhánh/kho | Lỗi đọc API bị trình bày thành không có dữ liệu | Tái hiện có kiểm soát |
| BF-015 | P2 | POS | Checkout 401 không xử lý hết phiên, bỏ mất ProblemDetails | Tái hiện có kiểm soát |
| BF-016 | P2 | Realtime | Không nối nhóm chi nhánh và không cập nhật đủ các màn theo sự kiện tồn | Xác nhận bằng code; PR26-F07 |
| BF-017 | P2 | Thông báo/báo cáo | Link `?tab=alerts` vẫn mở tab doanh thu | Tái hiện trên browser |
| BF-018 | P2 | Báo cáo | Không có bộ lọc kênh Admin, thay bằng Web không khớp dữ liệu tạo đơn | Tái hiện có kiểm soát + code |
| BF-019 | P2 | POS | HTTP 200 thiếu định danh đơn vẫn bị nhận là checkout thành công | Tái hiện có kiểm soát |
| BF-020 | P1 | POS riêng 5174 | Chưa khóa ngữ cảnh giỏ/chi nhánh/phiên trong khi checkout chạy | Tái hiện có kiểm soát |

## BF-001 — HTTP và refresh không gắn với chủ sở hữu phiên

**P1 · OPEN · Cần sửa trước khi nghiệm thu nhiều tài khoản/tenant.**

**Kịch bản 1:** Phiên A đang refresh token. Người dùng đăng nhập B trước khi refresh A trả về. Phản hồi A vẫn được ghi vào localStorage và header mặc định, thay token B bằng token A.

**Kịch bản 2:** Một POST của A trả 401 muộn sau khi B đã đăng nhập. Interceptor thấy token hiện tại khác token của request, tự gửi lại POST bằng token B, không xác minh đó có còn là cùng phiên/người dùng hay không.

**Ảnh hưởng:** UI có thể hiện B nhưng HTTP sử dụng A; hoặc một ý định của A được thực hiện trong ngữ cảnh B. Backend kiểm tra tenant không thay thế được kiểm tra chủ sở hữu ý định phía client.

**Bằng chứng:** `auth-refresh-owner`, `auth-old-401-replay`. Probe chỉ dùng token fixture và HTTP điều khiển; không thực hiện giao dịch chéo tenant trên hệ thống đang chạy.

**Vị trí:** `src/frontend/admin-dashboard/src/services/httpClient.ts`, các hàm `refreshAccessToken`, response interceptor 401; liên quan `context/AuthContext.tsx` login/logout.

**Điều kiện đóng:** bind generation/actor với refresh, queue và từng request; logout/login vô hiệu kết quả cũ; không tự replay mutation của actor cũ bằng token mới. Có kiểm thử A refresh → logout → B login → A trả muộn và A POST → B login → A 401.

## BF-002 — Kết nối SignalR không được thay theo phiên

**P1 · OPEN · Nối với PR26-F02.**

**Tái hiện:** A kết nối SignalR → token/người dùng chuyển sang B → NotificationCenter gọi `start()` lại. Adapter thấy connection đang Connected và trả về ngay, giữ transport đã xác thực A. Listener của B vẫn nhận được sự kiện fixture từ transport A.

**Nguyên nhân:** singleton không lưu identity/generation của kết nối. Auth logout không gọi stop/dispose Hub; cleanup NotificationCenter chỉ bỏ listener. `stop()` cũng chưa reset đầy đủ instance, nhóm chi nhánh và latest event.

**Ảnh hưởng:** nguy cơ thông báo của phiên/tenant cũ xuất hiện trong phiên mới. Probe xác nhận đường client; chưa phát dữ liệu thật chéo tenant qua browser.

**Vị trí:** `services/realtimeAdapter.ts:start/stop/initSignalRConnection`, `components/NotificationCenter.tsx` effect, `context/AuthContext.tsx:logout`.

**Bằng chứng:** `realtime-owner`; đối chiếu báo cáo PR26-F02 cũ, không mặc định finding đã đóng vì happy path chạy được.

**Điều kiện đóng:** mỗi kết nối thuộc actor/token phù hợp; chặn callback/deferred start/reconnect cũ; logout đóng và reset; test fake transport và transport thật riêng trên DB test.

## BF-003 — POS đổi request key khi thử lại thanh toán

**P1 · OPEN.**

**Tái hiện:** Thanh toán tiền mặt → request có thể đã commit nhưng client nhận lỗi mất phản hồi → giữ nguyên giỏ rồi bấm thanh toán lại. `handleCheckout` tạo UUID mới mỗi lần.

**Ảnh hưởng:** backend không thể nhận lần sau là replay của cùng ý định; có thể tạo hai đơn và ghi xuất tồn hai lần nếu lần đầu đã commit. Chống double click trong lúc đang gửi không giải quyết retry sau lỗi.

**Vị trí:** `admin-dashboard/src/components/pos/PaymentModal.tsx:handleCheckout` và `pos-app/src/components/PaymentModal.tsx:handleCheckout`.

**Bằng chứng:** `pos-retry-key`: hai lần gửi cùng giỏ có hai key khác nhau. Chưa cố tình tạo đơn trùng trên DB đang dùng.

**Điều kiện đóng:** lưu snapshot giỏ/chi nhánh/actor/key trước POST; kết quả unknown giữ nguyên key/payload qua retry/reload; đối soát đơn trước khi tạo ý định mới. Test response lost after commit trên DB test: một đơn, một lần trừ tồn.

## BF-004 — Tạo phiếu chuyển kho đổi key khi retry

**P1 · OPEN · Nhánh transfer của PR26-F06.**

**Tái hiện:** POST tạo phiếu mất phản hồi → bấm tạo lại với cùng form. Service luôn gọi `generateUuid()` mới; caller không có snapshot/key ổn định.

**Ảnh hưởng:** có thể sinh nhiều phiếu Draft cho cùng một yêu cầu. Không khẳng định đã trừ tồn hai lần ở bước tạo Draft; rủi ro xuất lặp xuất hiện nếu các phiếu trùng tiếp tục được xử lý.

**Vị trí:** `services/stockTransferService.ts:createTransfer`, `components/transfers/CreateTransferModal.tsx:handleSubmit`.

**Bằng chứng:** `retry-transfer`.

**Điều kiện đóng:** một ý định tạo phiếu giữ cùng key/payload, gồm timeout/reopen/reload; unknown có đối soát hoặc thao tác bỏ ý định rõ ràng. Test replay sau commit trả cùng transfer ID.

## BF-005 — Form tạo kiểm kê không giữ định danh retry

**P1 · OPEN · Nhánh count của PR26-F06.**

**Tái hiện:** Tạo kiểm kê mất phản hồi rồi gửi lại. Service hỗ trợ `req.requestKey`, nhưng form không cung cấp/persist key, nên lần gọi sau sinh key mới.

**Ảnh hưởng:** nhiều phiên kiểm kê/snapshot cho cùng một ý định, người dùng khó xác định phiên nào cần tiếp tục chốt.

**Vị trí:** `services/stockCountService.ts:createStockCount`, `components/stockCounts/CreateStockCountModal.tsx:handleSubmit`.

**Bằng chứng:** `retry-count`.

**Điều kiện đóng:** form có snapshot/key theo actor; giữ qua unknown/retry/reload; xác minh bằng callback form thật và HTTP/PostgreSQL replay.

## BF-006 — Xóa số đã lưu rồi chốt có thể xác nhận số cũ

**P1 · OPEN · Nhánh còn lại của PR26-F03.**

**Tái hiện:** Tồn sổ sách 8, số đếm đã lưu 10 → xóa ô đếm → bấm Chốt → đồng ý thông báo “gán bằng tồn sổ sách”.

**Kỳ vọng:** lưu 8 rồi xác nhận, hoặc chặn để người dùng lưu trước.

**Thực tế:** dirty check bỏ qua chuỗi rỗng; lời xác nhận gán số sổ sách không gọi record. Client gọi confirm với số server cũ 10. Probe quan sát **0 record, 1 confirm**.

**Vị trí:** `components/stockCounts/StockCountDetailDrawer.tsx:handleConfirmCount`, đoạn `hasDirtyEdits`, `uncounted` và POST confirm.

**Bằng chứng:** `count-cleared-value`.

**Điều kiện đóng:** xóa giá trị cũng là dirty; lựa chọn gán số sổ sách phải được ghi thành công trước confirm; record lỗi tuyệt đối không confirm. Test số 0, chuỗi rỗng và saved10 → expected8.

## BF-007 — Lưu kết quả đếm tự gán ô trống

**P2 · OPEN.**

**Tái hiện:** Mở kiểm kê chưa đếm → không nhập gì → bấm Lưu kết quả đếm.

**Thực tế:** `handleSaveCounts` thay ô trống bằng `expectedQuantity`, gửi lên server như số thực đếm. Không hỏi người dùng có muốn gán số sổ sách.

**Ảnh hưởng:** phiếu có thể được coi là đã đếm dù chưa đếm thực tế; làm mất ý nghĩa “chưa đếm”.

**Vị trí:** `StockCountDetailDrawer.tsx:handleSaveCounts`, phép gán `countedVal`.

**Bằng chứng:** `count-save-blank`: người dùng chưa nhập nhưng request record có số 8.

**Điều kiện đóng:** giữ khác biệt chưa nhập/0; yêu cầu nhập đủ hoặc có hành động gán sổ sách được xác nhận riêng. Không tự điền khi chỉ bấm Lưu.

## BF-008 — Drawer chứng từ bị kẹt loading của thao tác cũ

**P2 · OPEN.**

**Tái hiện:** A đang Lưu kiểm kê hoặc Xuất gửi chuyển kho → đóng/chuyển sang chứng từ B → request A hoàn tất.

**Thực tế:** generation guard bỏ kết quả A, nhưng `actionLoading` không được reset khi đổi ID. `finally` của A cũng không được reset do generation đã khác. Nút thao tác của B vẫn disabled.

**Vị trí:** effect theo ID và các `finally` trong `StockCountDetailDrawer.tsx`, `TransferDetailDrawer.tsx`.

**Bằng chứng:** `count-action-lock`, `transfer-action-lock`; cả hai đều đã chạy component React thật.

**Điều kiện đóng:** trạng thái action thuộc ID/generation; B không kế thừa busy của A, nhưng A vẫn được theo dõi/đối soát an toàn. Test chuyển ID và close/reopen khi request pending.

## BF-009 — Báo cáo bị phản hồi cũ ghi đè

**P1 · OPEN.**

**Tái hiện:** Chọn chi nhánh A rồi B nhanh → B trả 9.900 trước → A trả 1.100 sau.

**Thực tế:** bộ lọc vẫn B nhưng số hiển thị thành 1.100 của A. `loadReports` không có request generation/cancellation/context check.

**Vị trí:** `pages/ReportsPage.tsx:loadReports` và effect tải theo bộ lọc.

**Bằng chứng:** `report-response-order`.

**Điều kiện đóng:** chỉ response của query/actor hiện tại được commit; kiểm tra đủ branch/channel/date/tab và stale loading/error, không chỉ helper service.

## BF-010 — Lỗi báo cáo vẫn để số cũ dưới bộ lọc mới

**P2 · OPEN.**

**Tái hiện:** Có báo cáo A → đổi sang C → API C thất bại.

**Thực tế:** có thông báo lỗi nhưng vẫn render số của lần trước dưới bộ lọc C; không gắn nhãn dữ liệu cũ hoặc ẩn số.

**Vị trí:** `ReportsPage.tsx:loadReports/catch` và các khối render `pnlData`, `velocityData`, `alertsData`.

**Bằng chứng:** `report-stale-on-error`.

**Điều kiện đóng:** dữ liệu gắn với bộ lọc đã tải thành công; đổi query phải reset hoặc thể hiện stale cùng query cũ rõ ràng. Test HTTP error, 401/403 và chuyển tab.

## BF-011 — Sai ngày và biên thời gian báo cáo

**P1 · OPEN.**

**Tái hiện 1:** 01:00 ngày 29/10/2026 ở Việt Nam → chọn “Hôm nay”. Component lấy `toISOString().split('T')[0]` và gửi ngày **28/10**.

**Tái hiện 2:** chọn ngày lịch 28/10 → gửi `2026-10-28T00:00:00Z`; nếu ngày nghiệp vụ là Asia/Saigon, mốc đầu ngày đúng phải là `2026-10-27T17:00:00Z`.

**Biên cuối ngày:** frontend gửi đến `23:59:59Z`, backend lọc `CreatedAt <= EndDate`. Giao dịch ở `23:59:59.500` bị bỏ sót. Đây là lỗi biên độc lập với lựa chọn timezone.

**Vị trí:** `ReportsPage.tsx:calculateDates/loadReports`; `Features/Reports/Services/ReportService.cs` lọc ngày và nhóm `dailyTrend`.

**Bằng chứng:** `report-today-vietnam`, `report-timezone`, đối chiếu predicate backend.

**Điều kiện đóng:** chốt timezone nghiệp vụ; ngày UI theo đúng người dùng/tenant; chuyển sang UTC một lần; dùng khoảng `[đầu ngày, đầu ngày kế tiếp)` cho lọc. Test 00:00–07:00 Việt Nam, đầu tháng, phần lẻ giây và dailyTrend cùng timezone.

## BF-012 — Tồn trên form chuyển kho không theo kho nguồn

**P2 · OPEN.**

**Tái hiện:** Chọn kho B, kho B có tồn riêng 3 → dropdown SKU vẫn hiện “Tồn: 50” là tổng tenant. Đổi kho không phát sinh lần đọc warehouse balances nào.

**Ảnh hưởng:** người dùng chọn/số lượng dựa trên tồn sai phạm vi; có thể tạo phiếu rồi chỉ phát hiện thiếu hàng khi xuất gửi. Server còn kiểm tra tồn nên chưa khẳng định bán/xuất vượt tồn thành công.

**Vị trí:** `CreateTransferModal.tsx` effect `getSkus`, đổi `fromWarehouseId`, option hiển thị `sku.onHand`; `apiAdapter.ts:getSkus` tổng hợp số dư nhiều chi nhánh.

**Bằng chứng:** `transfer-source-stock`.

**Điều kiện đóng:** tải/hiển thị OnHand, Reserved, Available đúng kho nguồn; thay kho vô hiệu response cũ và các dòng không còn hợp lệ; SKU ngừng hoạt động có xử lý rõ ràng.

## BF-013 — Giữ chỗ có thể mất ý định chưa rõ kết quả khi sửa form

**P1 · OPEN · Xác nhận bằng code; chưa chạy lost-response trên DB thật.**

**Kịch bản:** POST reserve timeout sau khi có thể đã commit → UI ở trạng thái retry → người dùng đổi chi nhánh/kho/SKU/số lượng.

**Thực tế:** `handleReserveFormChange` tự xóa pending reservation và sinh key mới, không yêu cầu bỏ/đối soát ý định cũ. Trong khi đó nút “Bỏ thao tác cũ” riêng lại có confirm, nên guard có thể bị đi vòng bằng cách sửa input.

**Ảnh hưởng:** lần giữ chỗ đầu còn tồn tại nhưng UI mất theo dõi; thao tác tiếp theo có key khác và có thể giữ chỗ thêm.

**Vị trí:** `pages/InventoryPage.tsx:handleReserveFormChange/handleDiscardPendingReservation/handleReserveStock`.

**Điều kiện đóng:** khóa snapshot khi unknown hoặc yêu cầu quyết định rõ trước khi đổi; không mất pending/key chỉ do gõ input. Test timeout-after-commit, đổi trường, reload, retry và số reserved cuối trên DB test.

## BF-014 — Trang chi nhánh/kho nuốt lỗi tải dữ liệu

**P2 · OPEN.**

**Tái hiện:** `getBranches()` hoặc `getWarehouses()` trả `{error:…}`.

**Thực tế:** `loadOrgData` chỉ xét `.data`, không render lỗi. Trang hiện “Chưa có chi nhánh nào khớp bộ lọc” như một kết quả rỗng hợp lệ. Các thao tác bật/tắt cũng bỏ qua kết quả lỗi từ adapter.

**Vị trí:** `pages/OrganizationPage.tsx:loadOrgData/handleToggleBranch/handleToggleWarehouse`.

**Bằng chứng:** `organization-hidden-error` xác nhận nhánh đọc; nhánh toggle được đối chiếu code, chưa gửi mutation thử trên localhost.

**Điều kiện đóng:** tách loading/error/empty; hiển thị ProblemDetails và retry; không nhận bật/tắt thành công khi API trả lỗi.

## BF-015 — POS xử lý thiếu lỗi hết phiên và ProblemDetails

**P2 · OPEN.**

**Tái hiện:** Checkout trả HTTP 401 với `detail` mô tả hết phiên.

**Thực tế:** helper trả lỗi chung theo HTTP, giữ token hết hạn và không phát `pos-auth-expired`. Các lỗi nghiệp vụ cũng chỉ đọc `.message`, bỏ `.detail/errorCode` của backend. Nhánh search/barcode có xử lý 401, nhưng checkout không có.

**Vị trí:** `admin-dashboard/src/services/posApi.ts:checkoutPosOrder`; `pos-app/src/services/api.ts:checkoutPosOrder`.

**Bằng chứng:** `pos-checkout-401` trên helper Admin; source POS riêng có cùng cấu trúc xử lý.

**Điều kiện đóng:** phân biệt 400/401/403/409/network, hiển thị đúng detail/errorCode; reauth không mất snapshot cần đối soát và không replay bằng actor khác. Test cả hai POS.

## BF-016 — Đồng bộ realtime tồn kho chưa nối đủ

**P2 · OPEN · Xác nhận bằng code; nối với PR26-F07.**

**Thực tế:** server gửi `StockLevelChanged` vào nhóm chi nhánh. Trong source Admin đã quét không có caller `joinBranch/leaveBranch`. Adapter phát DOM event `oism:stock-changed`, nhưng các màn kho không đăng ký sự kiện đó. `onreconnected` không refetch dữ liệu bỏ lỡ. Kiểu `reservation.expired` tồn tại nhưng không có handler transport tương ứng.

**Ảnh hưởng:** nhãn SignalR “Trực tuyến” không bảo đảm bảng tồn/trạng thái đơn tự cập nhật. Cần làm mới hoặc focus lại tab Kho; đây là đường dùng tạm hiện có, không phải realtime đầy đủ.

**Vị trí:** `services/realtimeAdapter.ts`, `components/NotificationCenter.tsx`, `pages/InventoryPage.tsx`; `WebApi/Services/RealtimeNotifier.cs`, `Hubs/NotificationHub.cs`.

**Điều kiện đóng:** join/leave theo branch và actor, listener có cleanup, reconnect refetch, chuẩn hóa event expiry; chứng minh bằng Hub thật và dữ liệu test. Giữ xử lý BF-002 trước khi mở rộng realtime.

## BF-017 — Link thông báo cảnh báo không mở đúng tab

**P2 · OPEN · Tái hiện trên browser.**

**Tái hiện:** mở `http://localhost:5173/reports?tab=alerts`.

**Kỳ vọng:** tab Cảnh báo tồn kho tối thiểu.

**Thực tế:** trang vẫn mở P&L. Đây chính là URL `LowStockWarning` tạo ra. `ReportsPage` luôn khởi tạo `activeTab='pnl'` và không đọc query string.

**Vị trí:** `realtimeAdapter.ts` tạo href và `ReportsPage.tsx` khởi tạo tab.

**Bằng chứng:** [Ảnh browser](evidence/reports-alert-link.jpg), `evidence/reports-alert-link.json`.

**Điều kiện đóng:** URL/query tab là một nguồn trạng thái được kiểm tra hợp lệ; direct link, notification, reload và browser back/forward mở đúng tab.

## BF-018 — Báo cáo không lọc được riêng đơn Admin

**P2 · OPEN.**

**Tái hiện:** tạo/đã có đơn kênh `Admin`, vào báo cáo và tìm bộ lọc tương ứng.

**Thực tế:** không có option `Admin`; có `Web` với nhãn Website Online. Backend lọc chuỗi channel chính xác, nên `Web` không thay thế cho `Admin`. Không khẳng định các kênh thật khác không hoạt động.

**Vị trí:** `ReportsPage.tsx` dropdown kênh; `types/ordersCenter.ts` và `OrderService`/report predicate channel.

**Bằng chứng:** `report-admin-channel` xác nhận option; đối chiếu backend exact-match.

**Điều kiện đóng:** danh sách kênh thống nhất với contract; có Admin; dữ liệu fixture Admin hiển thị đúng khi chọn Admin và không lẫn POS/sàn.

## BF-019 — POS nhận HTTP 200 sai contract là thành công

**P2 · OPEN.**

**Tái hiện:** checkout nhận HTTP 200 với `{unexpected:true}` hoặc body không đọc được.

**Thực tế:** helper vẫn trả `success:true` khi thiếu định danh/mã đơn server; PaymentModal có thể dùng mã tự sinh và gọi callback xóa giỏ như thanh toán thành công.

**Ảnh hưởng:** người dùng không có chứng từ server để đối soát trong trường hợp proxy/API trả response sai contract. Chưa quan sát response sai này trên backend thật.

**Vị trí:** `posApi.ts:checkoutPosOrder`, `pos-app/services/api.ts`, hai `PaymentModal.tsx` nhánh success/fallback orderNumber.

**Bằng chứng:** `pos-malformed-success`.

**Điều kiện đóng:** validate response thành công theo contract; không giả định thành công khi thiếu identity; giữ giỏ/snapshot ở trạng thái cần đối soát, không tự tạo mã server thay thế.

## BF-020 — POS riêng chưa khóa giỏ và ngữ cảnh khi checkout pending

**P1 · OPEN · Tái hiện có kiểm soát; không ghi vào POS đang chạy.**

**Kịch bản đã chạy:** POS riêng cổng 5174 bắt đầu checkout A → request chưa trả → bấm Escape để đóng modal → chuyển sang chi nhánh B, thêm hàng vào giỏ mới → request A trả thành công → giỏ B bị xóa.

**Thực tế:** busy chỉ nằm trong PaymentModal, không truyền lên App. Header, branch/logout và các callback giỏ không có context lock/generation tương ứng; callback `onSuccessAndPrint` gọi `setCart([])` không kiểm tra cart/actor đã thay đổi hay chưa.

**Ảnh hưởng:** phản hồi của giao dịch cũ có thể xóa giỏ hoặc hiển thị hóa đơn trong ngữ cảnh mới. POS tích hợp đã có `checkoutBusy`; không đồng nhất hai implementation.

**Vị trí:** `src/frontend/pos-app/src/App.tsx` thao tác giỏ/chi nhánh/logout và callback `onSuccessAndPrint`; `components/PaymentModal.tsx`.

**Bằng chứng:** `separate-pos-old-checkout`, thực thi App và PaymentModal thật; chỉ catalog entry và HTTP được thay bằng fixture.

**Điều kiện đóng:** snapshot theo cart/branch/actor, khóa hoặc tách giỏ pending; response cũ không làm mất giỏ mới. Test deferred checkout → sửa/chuyển phiên → trả kết quả, cùng retry của BF-003.
