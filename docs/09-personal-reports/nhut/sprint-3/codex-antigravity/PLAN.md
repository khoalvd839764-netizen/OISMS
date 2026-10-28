# Kế hoạch hoàn thiện BE2 trước khi gộp

Ngày: **2026-10-26**. Phạm vi: code Nhựt tại PR #20 và phần đọc / báo cáo / automated core tests của Sprint 3. Đây là plan triển khai; không phải xác nhận các finding đã được sửa.

## 1. Baseline và phạm vi

| Thành phần | Nhánh / SHA đã kiểm tra |
| --- | --- |
| Nhựt / PR #20 | `feature/OISMS-42-sprint-3-backend-2` @ `d7fa4bc8738a1f22053d9b313f8e90c423b0483d` |
| `origin/develop` khi fetch | `eb7fe67cabbe03f7fdd9aff9c4ed71ed8ca1f6be` |
| Lõi BE1 để tích hợp | `origin/feature/OISMS-40-loi-van-hanh-kho-va-don-hang` @ `4206cc4b12f0128ee08927dc83550c9e8db5cd6e` |

Nhựt thêm 19 file thay đổi (+2.276 dòng) so với `origin/develop`. Head remote OISMS-42 và `refs/pull/20/head` trùng SHA; tác giả commit là Nhựt Minh Phùng. Nhánh OISMS-32 cũ có cùng nội dung, không phải một gói thay đổi thứ hai cần kéo.

Nguồn yêu cầu: `docs/08-sprints/sprint-3/README.md`, `docs/02-functional-requirements/fr-11-reports-analytics.md`, `docs/05-api-contracts/04-order-endpoints.md`, `docs/06-testing-and-qa/03-concurrency-stress-test.md`. Bản Sprint 3 tại nhánh Nhựt còn tham chiếu Confluence **v9**; bản tại BE1 `4206cc4` đã đồng bộ **v11**, chuyển Simulator cho BE1 và automated core tests cho BE2. Plan dùng bản v11 đã lưu trong repo làm baseline phân công; chưa tuyên bố đã đọc Confluence live trong lần review này.

Giữ các phần BE1 đã nghiệm thu: POS, order state machine, transfer, stock count, webhook/realtime và reservation expiry. Không viết thêm engine giữ/trừ tồn. Các lỗi auth/catalog chưa có bằng chứng trong diff này không được tự biến thành một đợt viết lại foundation; T03 lập ma trận kiểm chứng và chỉ mở thêm finding khi tái hiện được lỗi.

Hai thư mục untracked có sẵn `docs/11-ui-screens/`, `docs/12-error-and-status-codes/` nằm ngoài phạm vi. Không add, xóa hay đưa chúng vào handover BE2.

## 2. Kết quả kiểm tra trước fix

- Release build thành công, **0 warning / 0 error**.
- Unit test thực chạy: **114/114 passed**, không skipped, TRX được kiểm tra lại. Đây là test suite hiện có, không chứng minh đủ AC nghiệp vụ.
- Probe bổ sung dùng **EF InMemory** tái hiện lỗi giữ chỗ, confirm thiếu tồn, idempotency giữa shop và số liệu velocity. Chi tiết tại [evidence](./evidence/README.md).
- `git merge-tree` mô phỏng ghép BE1 phát hiện **5 conflict**: Order DTO, interface, service, controller và `Program.cs`. Đây chỉ là preview, chưa merge working tree.
- Ngoài conflict do Git nhận diện, hai controller có thể cùng đăng ký `POST /api/webhooks/simulator/{channel}` sau khi ghép; phải kiểm tra routing tại runtime.
- Chưa chạy toàn bộ integration suite PostgreSQL / HTTP của baseline đã hợp nhất, vì bước hợp nhất chưa diễn ra. Không lấy test pass của BE1 trước đây làm kết quả pass của nhánh BE2 hiện tại.

## 3. Thứ tự triển khai

Thực hiện **T01 → T02 → T03 → T04 → T05 → T06** trên cùng nhánh OISMS-42. Mỗi task có kiểm thử của chính thay đổi đó; T05 bổ sung kiểm thử xuyên luồng, không phải dồn toàn bộ kiểm thử đến cuối.

| Task | Ưu tiên | Kết quả cần đạt | Finding / khoảng trống |
| --- | --- | --- | --- |
| [T01 — core alignment](./task-01-core-alignment/brief.md) | P0 | Tích hợp BE1, giải quyết conflict theo semantics; một engine ghi và một webhook ingress | BE2-F01, F02, F06 |
| [T02 — read APIs](./task-02-read-apis/brief.md) | P1 | Order list/detail/filter dùng DTO chuẩn; list/detail chuyển kho và kiểm kê có dữ liệu thật | BE2-F09 |
| [T03 — access / isolation](./task-03-access-isolation/brief.md) | P1 | Owner-only financial report; HTTP RBAC và tenant isolation cho API BE2 | BE2-F03, F08 |
| [T04 — reports](./task-04-report-correctness/brief.md) | P1 | Đúng phạm vi đơn hoàn tất, filter SKU, tổng tồn nhiều chi nhánh, velocity và threshold | BE2-F04, F05, F10 |
| [T05 — core tests](./task-05-core-tests/brief.md) | P1 | PostgreSQL + HTTP chứng minh locking, idempotency, rollback, expiry; runner concurrent thực | BE2-F07, F08 |
| [T06 — handover / PR](./task-06-premerge-handover/brief.md) | P1 | Regression, bằng chứng truy vết, tổng kết đúng thực tế và PR sẵn sàng review | Toàn bộ finding / release gate |

## 4. Cách ghép baseline trong T01

**Cập nhật sau chẩn đoán PR #21:** CI của `4206cc4` đã được xác nhận có 129/129 unit pass và 260/261 integration pass, lỗi còn lại là B-01. Remote-tracking ref BE1 sau đó có commit `b5ba71ef7ac2bf055f4ee8963e159d4f27c69aae` với nội dung fix B-01. Chưa xác nhận CI/review của commit fix trong lần tạo prompt này. T01 phải fetch, kiểm chứng và pin baseline có fix B-01, đối chiếu phần thay đổi thêm; giữ `4206cc4` trong bảng đầu làm mốc review lịch sử. Xem [prompt fix BE2](./PROMPT-FIX-BE2.md).

BE1 hiện chưa nằm trong `origin/develop` đã kiểm tra. Khi triển khai T01, tích hợp commit BE1 đã pin **vào nhánh OISMS-42** rồi giải quyết conflict tại đó. Đây là ghép baseline vào feature branch; không phải merge PR #20 vào `develop`. Nếu PR BE1 đã vào develop trước khi T01 bắt đầu, fetch lại và kiểm tra SHA/ancestry để chọn develop chứa đúng baseline, tránh tích hợp hai lần.

Giữ triển khai mutation của BE1 và đưa query BE2 vào các điểm mở rộng có sẵn. Các hàm `GetOrdersAsync` / `GetOrderByIdAsync` của BE1 đang để `NotImplementedException` dành cho BE2: T02 phải hoàn thiện, không để trả 500. Không chọn toàn bộ file bằng `ours/theirs`; không giữ hai phiên bản `IOrderService` hoặc hai route nhận cùng payload.

Trước T01 cần kiểm tra lại head remote và working tree. Nếu baseline thay đổi, ghi SHA mới và diff bổ sung; không tự giả định review cũ áp dụng cho code mới.

## 5. Quy tắc cho các quyết định nghiệp vụ

- Theo API contract hiện lưu: `revenue-profit` chỉ `Owner`; `inventory-velocity` và `stock-alerts` vẫn `Owner,Staff`. Không suy rộng để cấm Staff toàn bộ báo cáo.
- FR-REP-01 yêu cầu số liệu từ đơn **đã hoàn tất**, dùng `OrderItem.CostPrice` đã snapshot. Test hiện tính cả `Confirmed` phải được điều chỉnh theo yêu cầu, không dùng chính test này để định nghĩa ngược nghiệp vụ.
- T04 ghi rõ thời điểm ghi nhận, múi giờ, biên thời gian, cách xử lý đơn thiếu CostPrice, SKU chưa bán và ngưỡng 0. FR không chỉ rõ toàn bộ các chi tiết này; chốt hợp đồng có test trước khi code. Nếu nguồn MCP có thay đổi được phê duyệt, ghi nguồn và cập nhật AC tương ứng.
- Stock tổng hợp không có filter branch phải gộp cùng SKU qua các chi nhánh. Không cộng warehouse balance và branch balance như hai nguồn tồn vật lý độc lập.
- Một batch chạy tuần tự hoặc 50 service calls trên InMemory không được gọi là bằng chứng concurrency. T05 cần request chồng lấp, context/connection riêng và kiểm tra invariant sau cùng.

## 6. Điều kiện trước khi đề nghị gộp

1. T01–T06 được reviewer nghiệm thu; mọi finding P0/P1 đã đóng với evidence đúng commit.
2. API đọc không còn stub; chỉ một engine ghi tồn và không có route trùng. HTTP contract, RBAC và tenant tests pass.
3. Release build, unit suite, PostgreSQL integration suite và các test concurrent thật được chạy trên head cuối cùng; số lượng test lấy từ runner/TRX thực tế.
4. Báo cáo FR-REP-01 đo được `< 2 giây` trên dataset dưới 100.000 bản ghi theo môi trường/điều kiện được ghi rõ; không tuyên bố đạt chỉ từ unit test nhỏ.
5. Baseline **B-01** (Warehouse đổi branch rỗng) được theo dõi riêng. Đây là baseline từ review BE1 trước, chưa tái kiểm trên nhánh Nhựt trong lượt này. Nếu còn fail ở regression cuối thì ghi **BLOCKED**, mở task fix riêng hoặc có quyết định chấp nhận rõ ràng của người phụ trách; không gọi all-green và không tự sửa lẫn vào reports.
6. Diff PR #20 chỉ còn phần BE2 cần review sau khi đối chiếu develop cuối cùng, không gồm tài liệu untracked ngoài phạm vi. Các dependency BE1 được ghi trong PR.
7. Người dùng quyết định merge sau review/CI; việc lập plan này không thực hiện merge hay kích hoạt tự động merge.
