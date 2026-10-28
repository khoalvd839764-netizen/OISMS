# Kế hoạch fix và hoàn thiện Frontend POS — OISMS-44

Ngày: **27/10/2026**, Asia/Saigon. Phụ trách: **Phương Lê — Frontend POS**. Trạng thái hiện tại: **B01 VERIFIED v1.2 — review R3**, B01-F01/F02/F03 CLOSED trong phạm vi T02 + phần in T05; [review B01 R3](./batch-01-receipt-print/reviews/v1.2-review-r3.md). T01 VERIFIED v1.3 giữ nguyên. Phần còn lại tổ chức thành **ba đợt**, đã xong B01, còn B02–B03, chưa nghiệm thu toàn bộ POS. **POS-STOCK-F01 nằm ở T03 / đợt 2**, không tạo task thứ bảy.

Cập nhật **28/10/2026 — review R4**: [B02 v1.3](./batch-02-stock-realtime-pwa/reviews/v1.3-review-r4.md) **CHANGES_REQUESTED**. Hai ca R3 đã pass, benchmark validator/timing đã đạt; F01/F02/F03/F05 giữ CLOSED. F04 còn barcode cũ về sau GET mới làm8→10 và search mớiAPI8 nhưng cache/cart10 (Edge production tái hiện). F06 live workflow chưa thực thi, OPEN/NOT RUN/BLOCKED; không đóng toàn POS-STOCK-F01. Build/34 baseline/19 B02 tests từ root PASS, browser checkout→barcode8 PASS. [Prompt fix v1.4](./batch-02-stock-realtime-pwa/reviews/v1.4-fix-prompt.md); B03 vẫn PLANNED, chưa commit working tree.

## 1. Baseline, yêu cầu và phạm vi

- Repo/workspace: `D:/OISMS`; làm trực tiếp trên `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`, theo yêu cầu người dùng.
- PR: [#24](https://github.com/khoalvd839764-netizen/OISMS/pull/24), head `44317f3b5188c276fa3ef25c8faf30323e503079`.
- Base lúc review: `9e41eb11f751dfeac64d2eef3b360a02810113be`; develop fetch khi lập plan: `ecc0c85` (merge PR #25, thay đổi tài liệu/phân công). Chưa merge develop mới vào nhánh POS trong lượt lập plan; khi tích hợp phải giữ các cập nhật tài liệu đó.
- Khi chuyển nhánh, tracked working tree sạch; các file [review R1](../PR24_REVIEW_2026-10-27.md) và [evidence](../evidence/pr24-r1/README.md) chưa commit được giữ nguyên. Không reset/clean/stash hoặc ghi đè thay đổi của người khác.
- Nguồn: [phân công Sprint 3](../../../../08-sprints/sprint-3/README.md), [FR-POS](../../../../02-functional-requirements/fr-07-pos-checkout.md), [FR realtime](../../../../02-functional-requirements/fr-09-realtime-signalr.md), [API POS](../../../../05-api-contracts/05-pos-endpoints.md), [contract Sprint 3](../../../../08-sprints/sprint-3/01-api-contracts-sprint-3.md), [báo cáo Sprint 2](../../sprint-2/S2_POS_REPORT_2026-10-19.md).
- Giữ backend BE1 đã nghiệm thu, DTO/route checkout, secret/tenant policy và engine tồn kho. Phạm vi sửa chính: `src/frontend/pos-app`; có thể bổ sung test/helper, cấu hình frontend và tài liệu trực tiếp cần cho AC.
- Không viết lại Admin/backend, thêm schema hoặc tích hợp đối soát ngân hàng thật trong plan frontend. Dependency mới phục vụ SignalR/QR/test phải được ghi cụ thể trong task, dùng nguồn chính thức và khóa phiên bản; không nâng dependency hàng loạt.

## 2. Đã có bằng chứng và còn thiếu

| Hạng mục | Hiện trạng | Cần hoàn thiện |
| --- | --- | --- |
| Checkout API | Canonical/alias và headers khớp body; DTO tương thích backend | Pending identity bền qua retry, xử lý lỗi có phân loại |
| Idempotency | R4 v1.3 VERIFIED T01: retry/reset/500/reload/reauth và recovery nhiều owner giữ key/payload | Gate browser/HTTP/PostgreSQL thật ở T06; không dùng số đơn mô hình làm bằng chứng DB |
| Hết phiên | R4: identity xuyên LoginModal/App/modal, guard không gửi A bằng token B; recovery theo incoming user đạt | Giữ regression khi triển khai T02–T05; kiểm chứng browser thật tại T06 |
| Hóa đơn | B01 v1.2 VERIFIED: server snapshot, draft/in lại và phần in đã đạt; timestamp bắt buộc từ server, receipt lỗi giữ key | Giữ regression trong B02; full-stack E2E và CI head cuối ở B03 |
| Tồn sau checkout | Người dùng phải F5 mới thấy số mới; callback success chưa refetch/invalidate kết quả SKU — POS-STOCK-F01 | T03 chủ động refresh sau checkout/replay, cập nhật branch đúng, xử lý lỗi refresh và event đến sau mà không trừ đúp |
| Realtime | Backend có Hub/events; pos-app chưa có client SignalR nối thật | Kết nối, vòng đời phiên/chi nhánh, refetch tồn và chống event trùng |
| QR | SVG mô phỏng, báo cáo ghi VietQR động đã đạt; PR24-F04 | Chốt hợp đồng/phạm vi và bằng chứng QR, báo cáo đúng thực tế |
| PWA/in/keyboard | Có manifest, SW được đăng ký ở index.html, phím tắt và CSS in; chưa nghiệm thu browser/thiết bị | Kiểm chứng shell offline, cập nhật cache, 58/80mm và tương tác khi pending |
| Tests | B01 v1.2 build/34 tests PASS; 11 runtime cases reviewer đạt, gồm clock client khác server và retry snapshot; print evidence R2 giữ hiệu lực do source in không đổi | B02 thêm tests cho phạm vi của đợt; full backend/PostgreSQL vẫn ở B03 |

Những thiếu hụt SignalR/PWA/QR là đối chiếu với phân công/claim hiện có, chưa phải finding mới có repro đầy đủ. Không tự gọi toàn bộ các mục này là regression do PR #24.

## 3. Thứ tự và trạng thái task

T01 đã được nghiệm thu độc lập. Phần còn lại giao tuần tự theo ba đợt dưới đây, mỗi đợt một prompt, một handover và một vòng review tổng hợp (có vòng fix nếu còn finding). Các task/AC gốc ở mục 4–9 giữ nguyên; gộp cách giao việc, không bỏ tiêu chí hoặc tự giảm phạm vi QR.

| Đợt | Phạm vi gộp và AC gốc | Kết quả / trạng thái |
| --- | --- | --- |
| [S3-POS-B01 — Hóa đơn và in nhiệt](./batch-01-receipt-print/brief.md) | Toàn bộ T02-AC-01..05 + T05-AC-02; phần print preview/thiết bị in của T05-AC-05 | **VERIFIED v1.2 — R3**, B01-F01/F02/F03 CLOSED; [review](./batch-01-receipt-print/reviews/v1.2-review-r3.md) |
| [S3-POS-B02 — Tồn realtime và thao tác quầy](./batch-02-stock-realtime-pwa/handover.md) | Toàn bộ T03-AC-01..07 + T05-AC-01/03/04/06; phần desktop/touch/barcode/keyboard/reopen/reconnect còn lại của T05-AC-05 | **VERIFIED v1.8**, F01..F06 CLOSED; [handover](./batch-02-stock-realtime-pwa/handover.md) |
| [S3-POS-B03 — QR và nghiệm thu cuối](./batch-03-qr-final/handover.md) | Toàn bộ T04-AC-01..05 + T06-AC-01..07 | Tích hợp VietQR, nghiệm thu E2E test backend 5001 + PG 5433, Edge browser evidence. **READY_FOR_REVIEW v1.2-integration** |

T05 hoàn tất khi cả B01 và B02 đạt các phần AC được phân bổ; không đánh dấu toàn T05 xong sau B01. T06 kiểm chứng lại các luồng tích hợp của cả ba đợt, không thay thế bằng test stub ở B01/B02. QR thiếu quyết định chỉ chặn phần phụ thuộc QR trong B03; vẫn chuẩn bị/chạy các kiểm tra độc lập đã đủ điều kiện và ghi rõ phần chưa chạy. Không gọi B03 VERIFIED khi AC bắt buộc hoặc quyết định phạm vi QR còn thiếu.

Bảng dưới giữ mã task gốc cho lịch sử và báo cáo:

| Task | Kết quả | Finding / nguồn | Phụ thuộc | Trạng thái |
| --- | --- | --- | --- | --- |
| [S3-POS-T01](./task-01-checkout-safety/brief.md) | Checkout an toàn khi mất mạng, hết phiên, retry | POS-T01-F01/F02/F03/F04 CLOSED theo R4 | Không | VERIFIED v1.3 — R4 |
| S3-POS-T02 | Map và in hóa đơn từ server receipt | PR24-F03 / FR-POS-03; B01-F01/F02/F03 CLOSED | T01 | VERIFIED qua B01 v1.2 — R3 |
| S3-POS-T03 | Làm mới tồn ngay sau checkout, không F5; SignalR cho thay đổi từ máy/kênh khác | POS-STOCK-F01 / FR-SIM-02 | T01–T02 | VERIFIED qua B02 v1.8 |
| S3-POS-T04 | Chốt QR, thực hiện đúng phạm vi và sửa claim nghiệm thu | PR24-F04 / FR-POS-03 | Hợp đồng QR được chốt | Hoàn tất trong B03 v1.2-integration |
| S3-POS-T05 | PWA, offline an toàn, phím tắt, cảm ứng và khổ in | FR-POS-01..03 | T01–T02 | VERIFIED qua B01 (in) & B02 (realtime/PWA) |
| S3-POS-T06 | E2E, bằng chứng cuối, báo cáo và cập nhật PR #24 | Toàn bộ phạm vi đã chốt | Các task trước đạt | Hoàn tất trong B03 v1.2-integration |

B01 đã VERIFIED; B02 đã có brief riêng và pin HEAD + working tree/hash thực tế sau B01 (code hiện chưa commit). Chỉ giao B02 trong prompt hiện tại; tới B03 tạo brief/baseline riêng sau B02, không tự triển khai cả hai đợt. Không tự gửi lệnh sang ứng dụng/agent khác.

## 4. S3-POS-T01 — An toàn checkout/retry/session

Phạm vi và AC đầy đủ tại [brief T01](./task-01-checkout-safety/brief.md). Thiết kế pending checkout tập trung ở tầng state/service frontend, không để lifetime key phụ thuộc mỗi lần render/click của modal. Phân biệt thất bại có kết quả chắc chắn và kết quả chưa xác định; không tự tạo giao dịch mới để bỏ qua lỗi cũ.

## 5. S3-POS-T02 — Receipt từ snapshot server

File dự kiến: `types/pos.ts`, `services/api.ts`, `PaymentModal.tsx`, `ReceiptPrintModal.tsx`, mapper/test liên quan. Giữ phiếu tạm tính riêng, không dùng số chứng từ tự sinh làm hóa đơn chính thức khi server response chưa đủ dữ liệu.

| AC | Điều kiện đạt |
| --- | --- |
| T02-AC-01 | Có type cho response/receipt backend, bỏ `receipt?: any`; map đầy đủ items, số lượng, giá, subtotal, tổng tiền, phương thức, tiền đưa/thối, số chứng từ và timestamp. |
| T02-AC-02 | Server snapshot đổi tên sản phẩm trong khi giỏ cũ giữ cùng SKU/giá: phiếu in khớp receipt, không lấy tên/số tiền từ giỏ để thay dữ liệu server. |
| T02-AC-03 | 200 replay và 201 mới in cùng dữ liệu chuẩn. Response success nhưng receipt hỏng/thiếu không được xóa pending rồi in hóa đơn giả; giữ trạng thái cần đối soát phù hợp. |
| T02-AC-04 | Tạm tính có nhãn chưa thanh toán, không gọi checkout, không xóa giỏ/key. Hủy dialog in không tạo giao dịch mới; in lại cùng receipt không checkout lại. |
| T02-AC-05 | Null/optional thông tin chi nhánh/khách hàng có quy tắc hiển thị rõ, không tự bịa địa chỉ/số điện thoại. Receipt không chứa giá vốn/COGS; amount/format làm tròn theo contract. |

Kiểm chứng: mapper/component tests thực, browser print preview và backend receipt đối chứng. Sửa mapping cùng contract, không sửa giá vốn/DTO backend để vừa dữ liệu giả của frontend.

## 6. S3-POS-T03 — Làm mới tồn sau checkout và SignalR

Phạm vi gồm hai luồng chung một task: (1) chủ động làm mới tồn sau checkout của chính máy; (2) SignalR/refetch cho thay đổi từ máy/kênh khác. Dùng API tồn/search/barcode hiện có, giỏ/search state và vòng đời phiên/chi nhánh; giữ payload pending theo T01. Luồng (1) phải hoạt động cả khi Hub mất kết nối. Thêm dependency client cần thiết cho luồng (2) nếu chọn giải pháp chính thức; ghi lý do và lockfile. Đối chiếu tên event/payload với contract/backend hiện tại; không dựa vào tên nhóm cũ trong FR khi implementation đã có contract mới.

| AC | Điều kiện đạt |
| --- | --- |
| T03-AC-01 | Kết nối `/hubs/notifications` với token hợp lệ; dispose/reconnect khi logout, token hoặc branch thay đổi, không tạo listener trùng sau mount/reconnect. |
| T03-AC-02 | Nhận `OrderCreated`, `StockLevelChanged`, `LowStockWarning` theo contract; popup/âm thanh theo tương tác/quyền trình duyệt, trạng thái mất kết nối hiển thị rõ. |
| T03-AC-03 | Event liên quan làm refetch tồn bằng API hiện có cho branch đang chọn; không trừ local thêm lần nữa sau checkout hoặc tin số liệu từ event khác tenant/branch. |
| T03-AC-04 | Tồn giảm thấp hơn số lượng giỏ: cảnh báo cụ thể, không tự cắt/xóa pending payload đang chưa rõ kết quả; server vẫn là nơi quyết định đủ tồn khi checkout. |
| T03-AC-05 | Reconnect refetch trạng thái thật; đổi tenant/user không nhận dữ liệu phiên trước. Có browser/Hub transport test và phân biệt test stub với transport thật. |
| T03-AC-06 | Checkout của chính máy POS thành công, kể cả 200 replay, phải invalidation/refetch tồn và kết quả SKU của đúng branch, không cần F5 hoặc chờ SignalR. Mất kết nối Hub vẫn làm mới được sau xác nhận checkout; không tự trừ local thêm khi event cùng đơn đến sau đó. |
| T03-AC-07 | Kết quả search/barcode đã có trước checkout không được mở lại như dữ liệu mới sau thanh toán. GET làm mới lỗi phải hiển thị trạng thái chưa cập nhật và cho thử lại, không xóa kết quả thành tồn 0 hoặc hiển thị số cũ như đã đồng bộ. |

### POS-STOCK-F01 — P2 — Tồn hiển thị chỉ cập nhật sau F5

**Ghi nhận ngày 27/10/2026, trạng thái OPEN / xử lý trong T03.** Người dùng báo sau checkout phải F5 mới thấy số dư hàng cập nhật. Đối chiếu source hiện tại cho thấy `App.tsx` callback `onSuccessAndPrint` chỉ lưu receipt/xóa cart, không kích hoạt làm mới tồn; props ProductSearch chưa có trigger invalidation sau checkout. `ProductSearch.tsx` giữ results trong state, effect chỉ phụ thuộc searchTerm/branchId; chọn hàng xóa searchTerm nhưng giữ results, và onFocus mở lại results cũ mà không query mới.

**Ca đối chứng cần chạy khi fix:** search SKU có Available=10 → checkout mua 2 → quay lại kết quả SKU cùng branch phải thấy 8 mà không reload toàn trang. Replay cùng order hoặc SignalR đến sau không được làm số hiển thị thành 6. Thử cả khi Hub đang mất kết nối, GET refresh lỗi và đổi branch trước khi response cũ về.

Đây là lỗi đồng bộ UI có đường nguyên nhân cụ thể trong source; chưa đo/đối chiếu database hoặc browser live trong lượt ghi nhận này, nên không kết luận database chưa trừ hàng. Không mở lại engine tồn/transaction backend khi chưa có bằng chứng backend sai.

Giải pháp cần đạt có hai phần: refresh chủ động sau checkout của chính máy bằng API hiện có; nhận SignalR/refetch cho biến động từ máy hoặc kênh khác. Giữ payload pending chưa rõ kết quả theo T01; cập nhật số tồn không được âm thầm sửa snapshot checkout đang pending. Không đưa lỗi này thành điều kiện mới của vòng fix T01 hoặc tự bắt đầu T03 trước khi giao task.

Ngoài phạm vi: viết lại Hub, durable outbox, tự phát event giả để trình diễn như dữ liệu server.

## 7. S3-POS-T04 — QR và phạm vi nghiệm thu

**Decision gate:** nguồn hiện tại yêu cầu QR nhưng chưa có contract cấu hình ngân hàng/doanh nghiệp và cách xác nhận thanh toán đủ để gọi VietQR thật đã hoàn tất. Task này phải ghi quyết định nghiệp vụ trước phần triển khai phụ thuộc; không tự đặt tài khoản nhận tiền hoặc coi hiển thị QR là đã thanh toán.

| AC | Điều kiện đạt |
| --- | --- |
| T04-AC-01 | Ma trận hiện trạng: SVG mô phỏng, QR mã hóa payload thanh toán và đối soát tiền là các mức khác nhau; xác định mức phải giao cho Sprint 3 và phần chuyển tiếp. |
| T04-AC-02 | Nếu triển khai QR thực: có cấu hình tài khoản nhận tiền được cung cấp/phê duyệt, nguồn sinh/chuẩn QR rõ, số tiền/nội dung gắn với giao dịch; không hardcode thông tin giả. Không tự tạo order lần hai khi đổi tab payment/retry. |
| T04-AC-03 | Luồng xác nhận chuyển khoản đúng contract backend hiện hành, xác định người xác nhận/điều kiện xác nhận. Không suy ra thanh toán thành công chỉ vì QR được hiển thị, được quét hoặc hết thời gian đếm. |
| T04-AC-04 | Nếu chỉ giữ demo/chuyển tiếp: UI và AC-POS-04 ghi chưa hoàn tất đúng phạm vi, giữ cảnh báo mô phỏng, không claim 100% POS/QR. Việc giảm phạm vi QR phải có quyết định rõ của người phụ trách trước nghiệm thu toàn bộ. |
| T04-AC-05 | Test payload bằng cấu hình fixture, validate số tiền/identity và luồng frontend; không chuyển tiền thật trong kiểm thử tự động. Bằng chứng nào chưa có ghi NOT RUN. |

PR24-F04 có thể đóng phần báo cáo bằng việc sửa claim; điều đó không tự làm toàn bộ yêu cầu QR trở thành hoàn tất. Không mở rộng sang payment gateway/webhook đối soát ngân hàng trong task frontend này nếu chưa có task backend riêng.

## 8. S3-POS-T05 — PWA, thao tác quầy và in nhiệt

File dự kiến: `public/sw.js`, manifest/icons, `index.html`, `App.tsx`, component liên quan, CSS in và test. Bắt đầu bằng kiểm chứng hiện trạng; chỉ sửa chỗ không đáp ứng, giữ UI đã dùng được.

| AC | Điều kiện đạt |
| --- | --- |
| T05-AC-01 | F2 focus tìm kiếm, F4 mở thanh toán một lần khi hợp lệ, Esc đóng an toàn; khi submitting/unknown không làm mất pending hoặc mở hai checkout song song. |
| T05-AC-02 | Kiểm chứng khổ in 58mm và 80mm bằng print preview/PDF kích thước phù hợp; có lựa chọn/quy tắc khổ in, không chỉ ghi cả hai trong comment nhưng CSS cố định 80mm. |
| T05-AC-03 | Manifest/icons/Service Worker hoạt động trên origin được hỗ trợ; phiên bản mới cập nhật shell/assets nhất quán. Không xóa cache ứng dụng khác cùng origin ngoài namespace POS. |
| T05-AC-04 | Offline hiển thị trạng thái rõ, giữ giỏ/pending theo T01; không giả thanh toán thành công, không queue/replay POST checkout ngoài cơ chế đã chốt. API và dữ liệu tenant không bị cache public bởi SW. |
| T05-AC-05 | Test desktop/touch viewport, barcode/keyboard, mở lại ứng dụng và reconnect. Thiết bị/máy in vật lý chưa thử phải được ghi là chưa thử, không lấy build làm bằng chứng UX. |
| T05-AC-06 | Kết quả search/barcode không ghi đè branch mới bằng response cũ khi đổi branch hoặc gõ nhanh; đo độ trễ theo điều kiện/dataset công bố và đối chiếu mục tiêu tra cứu p95 <200ms. Phân biệt thời gian UI/network/backend; chưa đo hoặc không đạt thì ghi đúng, không tuyên bố đạt từ debounce/build. |

## 9. S3-POS-T06 — Kiểm thử tích hợp, báo cáo và PR

| AC | Điều kiện đạt |
| --- | --- |
| T06-AC-01 | Chạy browser với backend và PostgreSQL test có guard/marker. Đăng nhập, chọn branch, search/barcode, checkout tiền mặt, receipt/in lại, thiếu tồn, lỗi mạng sau commit, 401/relogin, đổi tài khoản/branch, SignalR/reconnect đều có evidence. |
| T06-AC-02 | Case response mất sau commit: retry bằng frontend thật, DB cuối đúng một order/payment/lần OUT; cart chỉ xóa khi đã xác định success, key/payload không đổi khi còn unknown. Không dùng server model stub làm bằng chứng PostgreSQL. |
| T06-AC-03 | Có test module/component thật cho F01/F02/F03 và các negative paths; giữ 13 test cũ nhưng không gọi source assertions là E2E. `npm run build`, `npm test` và bộ tests mới đều PASS, ghi đúng runner/count/provider. |
| T06-AC-04 | Bảng PR24-F01..F04 có evidence đóng, quyết định QR còn lại rõ. Báo cáo bỏ claim 100% khi có AC chưa đạt; cập nhật checklist/phần việc đúng người, không đổi lịch sử review cũ. |
| T06-AC-05 | Fetch develop/head, kiểm tra diff/conflict trên nhánh POS và giữ cập nhật docs PR #25. CI cho head cuối success; chỉ chạy lại backend suite cục bộ khi có thay đổi/rủi ro backend liên quan, không chạy lặp để tăng số lượng evidence. |
| T06-AC-06 | Cập nhật PR #24 hiện có theo template/quy tắc repo: phạm vi, hành vi trước/sau, contract, test và giới hạn; không tạo PR trùng, force-push hay tự merge develop. |
| T06-AC-07 | Nghiệm thu lại POS-STOCK-F01 qua frontend và backend test: Available 10, checkout mua 2 → UI 8 không F5; replay/event đến sau vẫn 8. Có case Hub offline, API refresh lỗi và đổi branch trước response cũ; đối chiếu số dư DB/API, không chỉ đổi state local cho giống expected. |

Sau test từng task, bàn giao `READY_FOR_REVIEW v1.x`, có `handover.md`, AC → testcase → kết quả và tested SHA. Chỉ chuyển VERIFIED khi đủ bằng chứng. Mỗi task tiếp theo pin baseline sau task trước; lỗi mới phải gắn AC/repro, không mở vòng refactor ngoài phạm vi.

## 10. Lệnh và điều kiện môi trường

Lệnh cơ bản chạy trong `D:/OISMS/src/frontend/pos-app`:

```powershell
npm ci
npm run build
npm test
npm run dev -- --port 5174
```

Không cần chạy lại npm ci khi dependencies đúng lock và không thay đổi. Browser/E2E cần backend test và PostgreSQL test sẵn sàng với dữ liệu fixture; không reset/truncate database nghiệp vụ hoặc volume Docker người dùng. Chọn/lưu lệnh runner E2E cụ thể khi tạo test harness; chưa có runner đó thì ghi thiếu môi trường/NOT RUN.

Các số liệu build/13 tests/CI trong review R1 là baseline đã chạy, không được sao chép làm kết quả của code fix chưa viết. Hoàn thành toàn bộ POS chỉ được ghi khi tất cả AC bắt buộc đã đạt hoặc có quyết định phạm vi được ghi rõ, đặc biệt QR và kiểm chứng thiết bị.
