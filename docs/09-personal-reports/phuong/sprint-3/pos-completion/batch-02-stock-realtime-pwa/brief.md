# Brief — S3-POS-B02 / v1.0 — Tồn realtime, PWA và thao tác quầy

Ngày **28/10/2026**, Asia/Saigon. Trạng thái **CHANGES_REQUESTED v1.3 — review R4**: B02-F01/F02/F03/F05 CLOSED; F04 OPEN; F06 benchmark đạt, live workflow OPEN/NOT RUN/BLOCKED. POS-STOCK-F01 chưa đóng toàn bộ. [Review R4 và bằng chứng](./reviews/v1.3-review-r4.md), [prompt fix v1.4](./reviews/v1.4-fix-prompt.md). Thực hiện: Antigravity, Frontend POS của Phương. Một đợt triển khai/handover/review chung cho **T03 và phần T05 còn lại**, theo ba đợt đã thống nhất; không tạo thêm task. AC v1.0 dưới đây giữ nguyên; review R1–R3 được bảo toàn.

## Mục tiêu và baseline

- Sửa **POS-STOCK-F01**: sau checkout của chính máy, tồn hiển thị cập nhật qua API mà không F5, không phụ thuộc SignalR đang kết nối.
- Hoàn thiện nhận thay đổi từ máy/kênh khác qua Hub, vòng đời phiên/branch, PWA/offline an toàn, phím tắt, touch và search race.
- Repo `D:\OISMS`, branch `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`, HEAD `44317f3b5188c276fa3ef25c8faf30323e503079` **cộng working tree T01 v1.3 và B01 v1.2 đã VERIFIED, chưa commit**. Không checkout lại HEAD để lấy baseline.
- [Baseline SHA256](./evidence/baseline.json) chụp đầu vào POS và backend Hub tham chiếu. Đã xác minh 11 input B01 khớp [review R3 VERIFIED](../batch-01-receipt-print/reviews/v1.2-review-r3.md). Nếu source khác hash trước khi bắt đầu, đọc diff/ghi nhận thay đổi, không reset hoặc ghi đè. Sau triển khai lưu hash mới của phiên bản đã test.
- Nguồn: [plan](../PLAN.md), [brief T01](../task-01-checkout-safety/brief.md), [brief B01](../batch-01-receipt-print/brief.md), [contract Sprint 3](../../../../../08-sprints/sprint-3/01-api-contracts-sprint-3.md), [FR realtime](../../../../../02-functional-requirements/fr-09-realtime-signalr.md), [API POS](../../../../../05-api-contracts/05-pos-endpoints.md).
- Đọc `.agents/skills/codex-antigravity-workflow/SKILL.md` và reference Antigravity. Dùng CodeGraph trước khi tìm/đọc code; kết quả thiếu/rút gọn đối chiếu source, không tự index.

## Phạm vi được triển khai

Trong `src/frontend/pos-app`: App/ProductSearch/CartTable/PosHeader hoặc component cần thiết; services/hooks/types cho stock invalidation, SignalR và thông báo; SW/manifest/icons/index.html/Vite/package/lockfile phục vụ PWA; test/helpers và tài liệu trực tiếp cho AC. Tái sử dụng luồng checkout/receipt đã nghiệm thu, không viết lại.

Được thêm SignalR client chính thức phù hợp backend và dependency build/test/PWA thực sự cần cho đợt này, ghi lý do, đối chiếu tài liệu chính thức và khóa phiên bản bằng lockfile. Không nâng package hàng loạt. Có thể đưa CSS/runtime assets cần thiết về build local để shell hoạt động offline; không redesign UI hoặc đổi stack. Adapter realtime Admin hiện có mock methods, không coi đó là kết nối Hub thật và không sửa Admin trong đợt này.

Ngoài phạm vi: QR/thanh toán ngân hàng, bộ giả lập sàn mới, sửa backend/Hub/schema/engine tồn, durable outbox, offline queue cho checkout, refactor rộng, cập nhật/push/merge PR. B03/T04/T06 xử lý QR, full-stack transaction E2E và PR cuối. Nếu phát hiện backend thực sự chặn AC, ghi repro/contract và tiếp tục phần frontend độc lập; không tự vá backend để làm test xanh.

## Hiện trạng và hợp đồng đã đối chiếu

### 1. Làm mới tồn

`App.onSuccessAndPrint` hiện lưu receipt/xóa giỏ, chưa invalidation stock. ProductSearch giữ results; effect phụ thuộc searchTerm/branchId, onFocus có thể mở lại results cũ. Dùng search/barcode/inventory GET hiện có cho SKU/branch; không thêm endpoint chỉ để làm mới UI.

- Sau **checkout được xác nhận** (201 hoặc 200 replay), chủ động invalidate/refetch search results và tồn liên quan của đúng phiên/branch. GET refresh lỗi phải báo rõ dữ liệu chưa đồng bộ và cho thử lại; không biến checkout đã thành công thành thất bại/unknown, không POST lại chỉ vì GET refresh lỗi.
- Lấy số dư chuẩn từ API, không trừ local thêm mỗi lần checkout/replay/event. Event đến trước/sau hoặc lặp chỉ dẫn đến refetch phù hợp/coalesce, không thay 8 thành 6.
- Stock mới thấp hơn giỏ cần cảnh báo. Không tự cắt/xóa quantity, đổi price hoặc sửa snapshot pending đang unknown; server vẫn quyết định đủ tồn. Cache/state phải theo tenant/user/branch, response/callback cũ không ghi đè phiên mới.

### 2. SignalR

Đọc các file backend tham chiếu, không sửa:

- `src/backend/src/OISM.WebApi/Hubs/NotificationHub.cs`
- `src/backend/src/OISM.WebApi/Services/RealtimeNotifier.cs`
- `src/backend/src/OISM.Application/Features/Realtime/Dtos/RealtimeDtos.cs`
- `src/backend/src/OISM.WebApi/Program.cs` để đối chiếu Hub/JWT/CORS hiện hành.

Contract hiện tại:

- Hub `/hubs/notifications`, JWT cho Owner/Staff/Cashier. Cách cung cấp token phải theo SignalR client/backend; token query chỉ dành Hub path theo contract, không áp cho REST hoặc ghi token vào log/evidence.
- Server tự join `tenant_{tenantId}`. Client gọi **`JoinBranchGroup(branchId)`** / **`LeaveBranchGroup(branchId)`**, không tự gửi tên group. Server xác thực branch cùng tenant và active. Khi reconnect có connection mới phải đăng ký lại branch hiện hành.
- `OrderCreated` gửi vào nhóm tenant, cho đơn Reserved mới từ Admin/sàn, không phát cho POS hoặc replay cũ. Không chờ event này để refresh checkout POS của chính máy.
- `StockLevelChanged` gửi nhóm branch, có `branchId`, `skuId`, `newAvailableStock`, `timestamp`.
- `LowStockWarning` gửi nhóm tenant, có `branchId`, `skuId`, `skuCode`, `currentAvailable`, `threshold`, `alertedAt`. Thông báo đúng contract; số tồn/giỏ chỉ refresh cho branch đang chọn. Không suy diễn stock payload có tenantId/eventId nếu DTO không có; ranh giới tenant từ kết nối JWT/phiên hiện tại, bỏ callback của phiên cũ.
- Không có yêu cầu eventId mới từ server. Chống listener/callback trùng, bỏ kết quả cũ và refetch số dư chuẩn; không sửa contract để giải quyết client lifecycle.

### 3. PWA/offline

Đã có manifest và SW registration. SW hiện cache ít asset, activate xóa mọi cache khác tên, fallback HTML cho mọi request không chứa `/api/`. `index.html` còn CDN Tailwind/fonts. Phải kiểm tra **build production**, không lấy dev server chạy online làm bằng chứng offline.

- Cache shell/assets đúng phiên bản và namespace POS; chỉ xóa cache cũ thuộc namespace POS. Không phá cache ứng dụng khác cùng origin.
- Chỉ cache các GET shell/static asset phù hợp. REST/auth/Hub negotiate/transport và POST checkout không được nhận HTML fallback hoặc cache dữ liệu tenant. Không queue/replay POST ngoài cơ chế retry T01.
- Shell/assets thiết yếu phải dùng được khi offline sau lần load online thành công; không phụ thuộc CDN đang online để vẽ UI thiết yếu. Font có fallback hợp lệ; không cần thêm bộ branding mới.
- SW update không tự reload làm mất giỏ/pending; có luồng cập nhật nhất quán. Offline báo rõ trạng thái và giữ attempt; không fake success hoặc mở thêm giao dịch trong lúc unknown.
- Giữ phạm vi lưu pending đã chốt ở T01 (sessionStorage theo tab). Ghi rõ ca reload/restore tab hoặc mở lại PWA nào đã thử; không ngầm đổi thành phục hồi đa thiết bị/ngoài session, hoặc chuyển dữ liệu tenant sang public cache để đạt test offline.

## AC — giữ mã gốc

| AC | Điều kiện đạt / ca kiểm chứng |
| --- | --- |
| T03-AC-01 | Hub dùng JWT hợp lệ; connect/stop/reconnect theo user/tenant/token/branch. Join/leave branch đúng method, rejoin sau reconnect; không listener hoặc connection trùng qua mount/unmount/StrictMode. Callback/response phiên cũ không thay UI phiên mới. |
| T03-AC-02 | Nhận OrderCreated/StockLevelChanged/LowStockWarning đúng DTO/casing. Thông báo và âm thanh theo tương tác/quyền browser, có mute/khả năng không phát âm mà không crash; trạng thái disconnected/reconnecting rõ. Không dùng mock emit làm bằng chứng Hub thật. |
| T03-AC-03 | Event liên quan refetch stock của branch hiện tại, không local decrement. Event lặp/đến sau checkout không trừ đúp; event branch khác không đổi stock/giỏ branch hiện tại. |
| T03-AC-04 | API xác nhận tồn thấp hơn giỏ: cảnh báo SKU/số lượng cụ thể, không âm thầm chỉnh snapshot pending, quantity hoặc giá; lỗi checkout nghiệp vụ xử lý theo T01. |
| T03-AC-05 | Reconnect refetch và đăng ký lại branch; đổi user/tenant không nhận state/callback cũ. Có bằng chứng browser + **Hub transport thật** cho connection/join/nhận event và reconnect; test stub phải ghi riêng. |
| T03-AC-06 | **POS-STOCK-F01:** Available 10 → checkout mua 2 → UI hiển thị 8 không F5, không chờ Hub. 200 replay/event đến sau vẫn 8, không thành 6. Thử Hub offline; checkout success không bị đổi thành unknown chỉ vì refresh GET lỗi. |
| T03-AC-07 | Search/barcode results cũ không được mở lại như đã đồng bộ sau checkout. Refresh GET lỗi hiển thị stale/error + thử lại; không giả tồn 0 hoặc số cũ như dữ liệu mới. |
| T05-AC-01 | F2 focus tìm kiếm, F4 mở thanh toán một lần khi hợp lệ, Esc đóng an toàn. Khi submitting/unknown không làm mất attempt hoặc mở song song hai checkout; tương tác với dialog hóa đơn giữ hành vi B01. |
| T05-AC-03 | Manifest/icons/SW hoạt động trên localhost/HTTPS được hỗ trợ; load/reopen shell offline từ bản build production, cập nhật phiên bản assets nhất quán. Cache khác namespace POS còn nguyên. |
| T05-AC-04 | Offline/online hiển thị rõ và bảo toàn giỏ/pending theo T01. Không cache API/auth/Hub/user data public, không fake checkout hoặc queue POST. Reconnect không tự tạo đơn mới; refresh stock và retry checkout là hai thao tác riêng. |
| T05-AC-05 — phần B02 | Browser thật trên desktop và touch viewport: barcode/keyboard, mở lại ứng dụng, offline/reconnect và thao tác khi pending. Giữ evidence in B01 nếu không sửa phần in; chỉ kiểm tra lại khi B02 tác động tới in/assets/dialog liên quan. Không claim scanner/máy in vật lý nếu chưa thử. |
| T05-AC-06 | Gõ nhanh/đổi branch và response đảo thứ tự: search/barcode không ghi đè kết quả mới. Có số đo lookup p95 theo dataset/environment, tách UI/debounce/network/backend và đối chiếu mục tiêu <200ms đúng chỉ số; không coi debounce/build là bằng chứng latency đạt. Chưa đo/không đạt phải ghi đúng. |

## Kiểm chứng và điều kiện môi trường

1. **Module/component tests:** actual production services/hooks/handlers, gồm refresh 10→8, replay/event trùng, GET lỗi, late response khi đổi branch/owner, token/reconnect lifecycle, frozen pending, phím tắt và SW cache rules. Không chỉ regex source hoặc tự viết hàm mô phỏng khác code sản phẩm.
2. **Browser production:** chạy build và preview origin riêng/localhost với SW thực. Desktop/touch, offline reload sau warm load, đổi phiên, asset update, giữ cache khác namespace và pending. Có thể mock API để fault injection nhưng phải ghi rõ browser thật + API stub, không gọi là backend/DB E2E.
3. **Hub transport thật:** dùng backend và database **test** có fixture/guard để connect JWT, JoinBranchGroup, nhận event từ nghiệp vụ test hiện có và reconnect. Không thêm production debug endpoint phát event. Không lấy gọi trực tiếp callback hoặc fake emitter để thay bước này. Nếu chưa có môi trường, ghi NOT RUN/nguyên nhân và tiếp tục phần độc lập; B02 chưa được coi đủ bằng chứng nghiệm thu toàn bộ khi AC transport bắt buộc còn thiếu.
4. B03/T06 vẫn là gate full-stack transaction/order/payment/OUT/DB sau mất response và tổng nghiệm thu/CI/PR. Không ép chạy lại toàn bộ backend suite trong B02; chỉ những bước test tích hợp Hub/stock cần thiết. Không reset/truncate DB nghiệp vụ hoặc volume Docker người dùng; tách môi trường test khi cần.
5. Giữ regression **34 tests baseline**, T01 owner/key/recovery và B01 receipt/timestamp/in lại. Nếu dependency/fixtures phải cập nhật, ghi lý do, không xóa test hoặc nới assertion để làm xanh. Phần nào chưa chạy phải ghi NOT RUN, không chép log B01 thành kết quả B02.

Lệnh cơ bản tại `D:\OISMS\src\frontend\pos-app`:

```powershell
npm run build
npm test
npm run preview -- --host 127.0.0.1 --port 5174
```

Không chạy npm ci khi deps/lock không đổi. Nếu thêm SignalR/build/test dependency thì cập nhật lockfile và ghi lệnh runner cụ thể cho suite mới trong handover. Đọc tài liệu chính thức trước khi chọn API/version; không tự thêm framework test mới khi runtime sẵn có đủ dùng.

## Bàn giao

- Một `handover.md` tại thư mục B02, **READY_FOR_REVIEW v1.0**, kèm evidence/v1.0: baseline HEAD + tested working-tree hashes, file/hành vi thay đổi, trạng thái POS-STOCK-F01, bảng từng AC gốc → testcase/runner/provider → PASS/FAIL/NOT RUN → bằng chứng, dependencies mới, giới hạn và bước còn chờ.
- Cập nhật plan và Sprint 3 README. Giữ history T01/B01, không tự mở lại finding đã đóng nếu không có regression mới; chỉ nhận VERIFIED sau review. Khi thiếu môi trường bắt buộc, chỉ rõ phần chưa đủ điều kiện, không claim cả T03/T05 hoàn tất.
- Không triển khai B03/QR, tự tạo agent, gửi lệnh sang app khác, commit/push/merge, reset/clean hoặc đổi nhánh. Dừng ở bàn giao đợt 2 để review.
