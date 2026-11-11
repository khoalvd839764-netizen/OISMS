# CHECKLIST TIẾN ĐỘ ĐỒ ÁN OISMS — SPRINT 2 ĐẾN SPRINT 4 & RÀ SOÁT SẢN PHẨM

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-CHK-S2` |
| **Phiên bản (Version)** | `v1.5.0` |
| **Ngày cập nhật** | `2026-11-11` (Asia/Saigon) |
| **Nguồn truy vết gốc** | `C:\Users\Jacki Khoa\Documents\De bai CNPM.pdf` (Trang 1 – 6) & Confluence Page ID `5275650` (Version 9) |
| **Bằng chứng kiểm thử Sprint 2** | **194/194 Tests Pass (100%)** — 103 Unit Tests + 91 Integration Tests trên PostgreSQL 16+ thật; Code Coverage Core Inventory đạt **`92.49% – 94.83%`** |
| **Nguồn cập nhật Sprint 3** | [Phân công Sprint 3 lưu trong repo, Confluence v11](docs/08-sprints/sprint-3/README.md) và [báo cáo tổng kết Đăng Khoa](docs/09-personal-reports/dang-khoa/sprint-3/SUMMARY.md); không tra cứu/cập nhật Confluence live trong lượt này |
| **Hiện trạng sản phẩm mới nhất** | [Cập nhật 11/11/2026](#0b-hiện-trạng-sản-phẩm--cập-nhật-11112026): Sprint 4 hoàn thành phần lớn ([báo cáo tổng kết](docs/08-sprints/sprint-4/BAO_CAO_TONG_KET_NGHIEM_THU_SPRINT_4.md)). Còn thiếu CS-06, U01-B, phân công chi nhánh của CS-02, job Hangfire báo cáo hằng ngày, lọc báo cáo theo SKU (dời sprint sau). Chi tiết lỗi và Coming Soon: [`docs/bug-fixaffter/CHECKLIST.md`](docs/bug-fixaffter/CHECKLIST.md) |
| **Frontend Coming Soon và Upgrade** | [Báo cáo 04/11/2026](docs/report-after-upgrade/frontend-coming-soon-va-upgrade.md) (đã có ghi chú cập nhật 11/11). CS-05 xong 07/11/2026, CS-01 xong 09/11/2026 (PR #41), POS gộp thành một ứng dụng `pos-app` ngày 08/11/2026. |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả nội dung & Chi tiết rà soát thay đổi (Change Check) | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | • Tổng hợp toàn bộ yêu cầu đề bài (`28 FR + AI`, `11 NFR`, `5 Task Packages`, `6 Deliverables`) và đánh dấu nghiệm thu `[x]` các hạng mục đã hoàn thành trong Sprint 2.<br/>• **Check thay đổi khi đối chiếu Confluence (`Page ID: 5275650`, Version 9):** Đồng bộ chính xác lịch trình (`S2: 13–17/10`, `S3: 18–22/10`, `S4: 23–27/10`, `S5: 28/10–01/11`) và phân công 5 thành viên cho các hạng mục chưa hoàn thành `[ ]`. | `INIT-SYNC-CHK-S2` |
| `v1.1.0` | `2026-10-27` | Lê Võ Đăng Khoa — Backend 1 | Cập nhật checklist BE1 Sprint 3 theo hồ sơ T01–T06, B-01 và PR #21; tách phần backend đã đạt khỏi UI, API đọc/báo cáo BE2 và công việc Sprint 4; đồng bộ phân công Simulator theo bản v11 đã lưu trong repo. | `SYNC-BE1-S3-CHECKLIST` |
| `v1.2.0` | `2026-10-29` | Rà soát sản phẩm | Bổ sung checklist hiện trạng Admin/POS, 20 nhóm lỗi và 8 mục Coming soon; giữ nguyên bằng chứng Sprint 2/BE1 lịch sử, không tự nâng thành nghiệm thu toàn sản phẩm. | `docs/bug-fixaffter/` |
| `v1.3.0` | `2026-10-30` | Nhóm dự án OISMS | Cập nhật 20/20 lỗi `BF-001..020` đã sửa, Upgrade 06 hoàn tất; thêm mục 0A.2, 0A.2B. *(Dòng lịch sử bổ sung 11/11/2026 theo trường phiên bản trước đó; bản gốc thiếu dòng này.)* | `OISMS-46` |
| `v1.4.0` | `2026-11-05` | Phùng Minh Nhựt — Backend 2 (chỉnh sửa sau review: Lê Võ Đăng Khoa) | Rà soát toàn diện VS Code, Docker, GitHub Actions CI, Jira/Checklist, Backend .NET 8 (ForwardedHeaders, CORS order, null safety, log encoding) và Frontend React 19 (Multi-unit LineKey, pagination race condition, receipt order matching). | `MASTER-REVIEW-OISMS-47` |
| `v1.5.0` | `2026-11-11` | Rà soát docs Sprint 4 | Thêm mục 0B hiện trạng 11/11/2026; cập nhật CS-01, CS-05, POS sau khi gộp, BF-003/015/019/020, BE2-F03, BE2-F04; ghi chú các mục 0, 0A, 1, 2, 3 đã đổi; bổ sung `NFR-USA-01` còn thiếu và thư mục docs mới. Xem [rà soát docs](docs/08-sprints/sprint-4/ra-soat-lech-docs-2026-11-11.md). | `RA-SOAT-DOCS-S4` |

---

## 0. Checklist Công Việc Đăng Khoa — Backend 1 Sprint 3

Chi tiết 42 hạng mục đã triển khai và 5 mục chưa xác nhận hoàn tất nằm tại [SUMMARY.md](docs/09-personal-reports/dang-khoa/sprint-3/SUMMARY.md). Các dấu `[x]` Sprint 2 bên dưới giữ ý nghĩa tại baseline lịch sử; không phải kết quả chạy kiểm thử mới ngày 27/10/2026.

- [x] **T01 — Nền tảng và hợp đồng API:** Domain entities, EF mapping/DbSet, DTO/service interface, RequestKey và schema bổ sung. Trạng thái DONE theo báo cáo gốc, chưa có biên bản nghiệm thu độc lập riêng.
- [x] **T02 — POS checkout nguyên tử:** Toàn giỏ trong một transaction; khóa/kiểm tra tồn khả dụng, trừ tồn, lưu giá vốn, payment/OUT và chống thanh toán trùng. VERIFIED v1.2.
- [x] **T03 — Vòng đời đơn online:** Giữ hàng, xác nhận xuất, hủy và hoàn tất; rollback, retry và tranh chấp confirm/cancel. VERIFIED v1.1.
- [x] **T04A — Chuyển kho hai bước:** Lập phiếu, dispatch/receive, giá vốn xuất chuyển, ledger và hủy Draft; chống trùng, concurrency và rollback. VERIFIED v1.2.
- [x] **T04B — Kiểm kê kho:** Snapshot tồn/sequence, ghi số đếm, tạo chứng từ điều chỉnh liên kết, kiểm tra drift và ghi IN/OUT đúng một lần. VERIFIED v1.3.
- [x] **T05 — Lõi webhook và SignalR:** Nhận payload ba kênh, xác thực/ánh xạ shop, chống trùng, giữ hàng bằng lõi chung và phát sự kiện sau commit. VERIFIED v1.4.
- [x] **T06 — Hết hạn giữ chỗ:** Hangfire PostgreSQL, quét reservation quá hạn, hủy/release nguyên tử, worker/concurrency/fault/cancellation tests. VERIFIED v1.2.
- [x] **B-01 — Đổi chi nhánh kho sạch:** Có fix `b5ba71e`; nhóm warehouse/B-01 đã chạy lại PASS theo bằng chứng trong báo cáo tổng kết.
- [x] **Bàn giao BE1:** Tài liệu theo task và PR #21; code đã vào develop tại merge commit `4dbfd8e`, có fix B-01.
- [ ] **Simulator đầy đủ:** Chưa có bằng chứng nghiệm thu UI/sender HTTP concurrent hoàn chỉnh; batch tạo đơn tuần tự trên nhánh tích hợp không chứng minh mục này. *(11/11/2026: đã có app giả lập `marketplace-simulator` cổng 5175; runner HTTP concurrent vẫn chưa có.)*
- [ ] **UI E2E Admin/POS/realtime:** Cần bằng chứng tích hợp frontend riêng.
- [ ] **Thiết kế AI gợi ý nhập hàng:** Chưa có bằng chứng hoàn tất trong gói BE1 hiện được tổng kết. *(11/11/2026: đã có ở Sprint 4, xem CS-08.)*
- [ ] **Triển khai production và bật/provision Hangfire trên môi trường đích:** Chưa thuộc kết luận nghiệm thu local. *(11/11/2026: đã deploy AWS `oisms.khoadang.site`; Hangfire trên AWS vẫn tắt.)*

Quy ước: mục FR bao gồm nhiều người hoặc nhiều sprint chỉ được đánh dấu hoàn tất khi toàn bộ phạm vi có bằng chứng. Phần BE1 đã đạt được đánh dấu riêng ở các mục con; `[ ]` của nhóm không phủ nhận phần backend đã hoàn thành.

---

## 0B. Hiện trạng sản phẩm — cập nhật 11/11/2026

Đối chiếu code nhánh `feature/OISMS-58-chatbot-ai`. Lần chạy gần nhất: unit backend 380/380 PASS, `npm test` POS 89/89 PASS (11/11/2026); integration trên PostgreSQL 372/372 PASS (07/11/2026, chưa chạy lại sau CS-01 và Trợ lý AI). Truy vết từng chức năng sang test: [RTM mục 4](docs/06-testing-and-qa/01-requirements-matrix-rtm.md).

- [x] **Upgrade 01-A, 02, 03, 04, 05, 06:** backend và giao diện Admin đã có.
- [ ] **Upgrade 01-B:** hóa đơn NCC và chênh lệch mua hàng chưa làm.
- [x] **CS-01, CS-03, CS-04, CS-05, CS-07, CS-08:** đã có backend và giao diện. CS-08 là gợi ý theo quy tắc, còn thiếu bằng chứng đánh giá độ chính xác (xem 0A.3).
- [ ] **CS-02:** có tạo/sửa/khóa nhân viên, vai trò `Staff`/`Cashier`; chưa có phân công chi nhánh.
- [ ] **CS-06:** in biên nhận POS và in lại hóa đơn bán đã có; in chứng từ giao hàng và in báo cáo còn Coming soon.
- [x] **Trợ lý OISMS (`FR-AI-02`):** Owner, Staff hỏi đáp chỉ đọc; mặc định tắt, dùng gói miễn phí hạn mức thấp.
- [x] **POS:** một ứng dụng `src/frontend/pos-app` (cổng 5174), Admin `/pos` chỉ chuyển hướng.
- [ ] **Kiểm chứng lỗi POS sau khi gộp:** BF-003, BF-015 đã có test và đóng; BF-019 có code chặn, chưa có test riêng; BF-020 (checkout cũ xóa giỏ mới khi đổi chi nhánh) chưa có test, cần kiểm chứng lại.
- [x] **Kết nối sàn và app giả lập (cổng 5175):** tạo secret, chọn sản phẩm của shop theo secret, bắn đơn qua webhook.
- [ ] **Job Hangfire tổng hợp báo cáo hằng ngày, màn demo concurrency:** chưa có. Hangfire mặc định tắt, kể cả bản demo AWS.
- [ ] **NFR trên môi trường deploy:** chưa đo p95 POS, chưa chạy stress test 50 đơn trên AWS.
- [x] **BE2-F04 (doanh thu tính cả đơn `Confirmed`):** nhóm chốt 11/11/2026 giữ cách tính hiện tại (`Confirmed` và `Completed`), đã ghi vào [`fr-11`](docs/02-functional-requirements/fr-11-reports-analytics.md) v1.1.0 và API contract.
- [ ] **Lọc báo cáo doanh thu theo SKU (`BE2-F04`):** chưa có trong code, hoàn thành ở sprint sau.

Mục 0, 0A, 1, 2, 3 là baseline Sprint 2–3, giữ nguyên dấu tại thời điểm ghi; chỗ nào đã đổi thì có ghi chú *(11/11/2026)*.

---

## 0A. Checklist hiện trạng sản phẩm — cập nhật 29/10/2026

**Chưa nghiệm thu toàn bộ sản phẩm.** Các dấu `[x]` ở phần Sprint 2 và báo cáo BE1 bên dưới phản ánh baseline/bằng chứng được ghi tại thời điểm đó. Khi đánh giá khả năng sử dụng hiện tại, phải đối chiếu các mục còn mở trong phần này. Trạng thái bàn giao READY_FOR_REVIEW chỉ có nghĩa chờ review.

Baseline rà soát: nhánh `feature/OISMS-43-orders-transfers`, HEAD `f276eeed72782f7272f32a62f4bb61ba761f3e89` cộng working tree chưa commit. Chi tiết tái hiện, ảnh hưởng, vị trí code và điều kiện đóng: [`docs/bug-fixaffter/errors/`](docs/bug-fixaffter/errors/).

### 0A.1. Nền tảng và các luồng cơ bản

- [x] Đã có báo cáo Markdown cho đợt rà soát, tách lỗi đang hoạt động, chức năng chưa hoàn thiện và kết quả kiểm tra.
- [ ] **Đăng nhập/đổi tài khoản:** hoàn thiện vòng đời HTTP, refresh và SignalR theo chủ sở hữu phiên; `BF-001`, `BF-002` còn mở. *(30/10/2026: các lỗi BF nêu ở dòng này đã sửa, xem 0A.2.)*
- [ ] **Phiếu nhập hàng:** đã bổ sung tạo Draft, đọc danh sách server và thao tác xác nhận riêng ở lượt trước; cần nghiệm thu lại toàn luồng tạo → xác nhận → tồn/sổ kho → tải lại. Không suy diễn từ kiểm thử từng phần thành hoàn tất sản phẩm.
- [ ] **Chuyển kho:** đã có UI gọi backend; còn retry tạo phiếu, khóa thao tác và tồn theo kho nguồn (`BF-004`, `BF-008`, `BF-012`); cần nghiệm thu tạo → dispatch → receive → tồn hai kho → lịch sử. *(30/10/2026: các lỗi BF nêu ở dòng này đã sửa, xem 0A.2.)*
- [ ] **Kiểm kê:** đã có UI và cập nhật phân loại lịch sử ở lượt trước; còn retry, ô đếm trống và khóa thao tác (`BF-005..008`); cần nghiệm thu lưu số đếm → xác nhận → tồn/điều chỉnh/lịch sử → tải lại. *(30/10/2026: các lỗi BF nêu ở dòng này đã sửa, xem 0A.2.)*
- [ ] **Giữ chỗ và đơn hàng:** bảo toàn ý định khi kết quả chưa xác định (`BF-013`), nghiệm thu tạo/giữ → xác nhận hoặc hủy/hết hạn → tồn/sổ kho. In chứng từ Admin còn Coming soon (`CS-06`). *(30/10/2026: các lỗi BF nêu ở dòng này đã sửa, xem 0A.2.)*
- [x] **POS (`pos-app`, cổng 5174; `/pos` của Admin chỉ chuyển hướng, từ 08/11/2026):** catalog, giỏ, thanh toán tiền mặt và VietQR (`CS-05`). *(11/11/2026: BF-003, BF-015 có test tự động trong `test-oisms/frontend/pos-app/` và đã đóng; BF-019 có code chặn nhưng chưa có test riêng.)*
- [ ] **POS riêng:** phải khóa/tách đúng giỏ và chi nhánh khi giao dịch đang chạy (`BF-020`). *(11/11/2026: chỉ còn một POS `pos-app`; bản sửa BF-020 cũ không còn, POS hiện tại có cơ chế pending riêng nhưng chưa có test cho trường hợp đổi chi nhánh, cần kiểm chứng lại.)*
- [ ] **Đồng bộ realtime:** hoàn thiện nhóm chi nhánh, sự kiện tồn và refetch sau reconnect (`BF-002`, `BF-016`); kiểm chứng nhiều phiên và chi nhánh trên môi trường test. *(30/10/2026: các lỗi BF nêu ở dòng này đã sửa, xem 0A.2.)*
- [ ] **Báo cáo/cảnh báo:** sửa phản hồi cũ, dữ liệu cũ khi lỗi, ngày/múi giờ, link cảnh báo và kênh Admin (`BF-009..011`, `BF-017`, `BF-018`); các finding BE2-F03/F04 bên dưới vẫn cần xử lý/đối chiếu riêng. *(11/11/2026: các BF đã sửa 30/10; BE2-F03 và quy tắc doanh thu BE2-F04 đã đóng, còn lọc SKU.)*
- [ ] **Chi nhánh/kho:** không trình bày lỗi API như danh sách trống (`BF-014`); nghiệm thu tải, lưu và đọc lại. *(30/10/2026: các lỗi BF nêu ở dòng này đã sửa, xem 0A.2.)*

### 0A.2. Checklist 20 lỗi hệ thống đã khắc phục (`OISMS-46` — VERIFIED PASS 30/10/2026)

Toàn bộ 20 mục dưới đây đã được **KHẮC PHỤC TRIỆT ĐỂ & KIỂM THỬ THÀNH CÔNG (20/20 PASS)** tại 30/10/2026. *(11/11/2026: sau khi gộp POS, BF-019 và BF-020 về trạng thái cần kiểm chứng lại trên `pos-app`, xem [CHECKLIST lỗi](docs/bug-fixaffter/CHECKLIST.md).)* Xem chi tiết tại [errors/](docs/bug-fixaffter/errors/) và [CHECKLIST.md](docs/bug-fixaffter/CHECKLIST.md).

- [x] **P1 · BF-001:** Chặn refresh/HTTP cũ ghi đè/xóa phiên mới hoặc replay ý định cũ bằng token tài khoản mới.
- [x] **P1 · BF-002:** Tạo/đóng và reset kết nối SignalR đúng phiên đăng nhập kèm lifecycle guards.
- [x] **P1 · BF-003:** Giữ nguyên `requestKey` theo chữ ký giỏ hàng khi retry thanh toán POS có kết quả chưa rõ.
- [x] **P1 · BF-004:** Bảo toàn `requestKey` khi retry tạo phiếu chuyển kho.
- [x] **P1 · BF-005:** Bảo toàn `requestKey` của form tạo kiểm kê.
- [x] **P1 · BF-006:** Không chốt số đếm cũ sau khi xóa ô và đồng ý gán số sổ sách (`recordCounts` trước khi `confirmStockCount`).
- [x] **P2 · BF-007:** Không tự lưu ô đếm trống thành tồn sổ sách khi chưa xác nhận.
- [x] **P2 · BF-008:** Không để request chứng từ cũ khóa nút trên chứng từ mới (`busyIdsRef` theo từng ID chứng từ).
- [x] **P1 · BF-009:** Không cho phản hồi báo cáo cũ ghi đè bộ lọc mới.
- [x] **P2 · BF-010:** Xử lý xóa sạch dữ liệu báo cáo cũ khi truy vấn bộ lọc mới thất bại.
- [x] **P1 · BF-011:** Thống nhất ngày Việt Nam (`UTC+07:00`), timezone nghiệp vụ và khoảng nửa mở `CreatedAt < EndDateBoundary` (bao phủ cả `.999`).
- [x] **P2 · BF-012:** Hiển thị tồn của đúng kho nguồn khi lập chuyển kho, không fallback về tồn tổng tenant và khóa SKU ngừng hoạt động.
- [x] **P1 · BF-013:** Khóa form giữ chỗ và bảo toàn `idempotencyKey` pending khi đang ở trạng thái unknown.
- [x] **P2 · BF-014:** Hiển thị lỗi đọc/lưu chi nhánh, kho đúng trạng thái banner lỗi kèm nút retry.
- [x] **P2 · BF-015:** Xử lý hết phiên (`pos-auth-expired`), bảo toàn giỏ hàng và trích xuất `ProblemDetails` khi POS checkout thất bại.
- [x] **P2 · BF-016:** Nối đầy đủ sự kiện realtime tồn kho (`stock.changed`, `reservation.expired`) và refetch khi reconnect.
- [x] **P2 · BF-017:** Mở đúng tab cảnh báo từ link `/reports?tab=alerts` và sự kiện điều hướng từ `NotificationCenter`.
- [x] **P2 · BF-018:** Bổ sung bộ lọc kênh `Admin` và loại bỏ tùy chọn `Web` không hợp lệ.
- [x] **P2 · BF-019:** Không nhận checkout thành công khi response thiếu định danh đơn server hoặc chứa GUID rỗng.
- [x] **P1 · BF-020:** Không để kết quả checkout POS riêng xóa giỏ hoặc cập nhật ngữ cảnh mới.

### 0A.2B. Checklist Nâng cấp Kiến trúc & Nghiệp vụ (`docs/Upgrade-recommended/`)

- [x] **`Upgrade 06` — Rà soát & Chuẩn hóa Clean Architecture Backend (`OISMS-46` — Đã hoàn thành):** Chuyển nghiệp vụ/transaction từ 8 Controller về `OISM.Application`, cô lập `IPasswordHasher`, `ISqlLockService`, `IWarehouseBranchReassignmentStore` ở `OISM.Infrastructure`, tách `AddApplicationServices` / `AddInfrastructureServices` và xóa thư mục `OISMS/` trùng lặp. Xem [clean-architecture.md](docs/report-after-upgrade/clean-architecture.md).
- [x] **`Upgrade 05` — Nhiều đơn vị tính cho hàng hóa vật lý (`OISMS-47` — Bước 1):** Bảng `units`, `sku_units`, tồn theo đơn vị gốc `BaseUnitId` và quy đổi `BaseQuantity = Quantity × FactorSnapshot`. *Tiến độ: đã xong backend và giao diện.*
- [x] **`Upgrade 04` — Bảng giá bán theo thời gian hiệu lực (`OISMS-47` — Bước 2):** Bảng `price_lists`, `sku_unit_prices` chỉ thêm dòng mới, phiếu đổi giá `price_change_requests` có bước duyệt và chụp snapshot giá vào dòng đơn.
- [x] **`Upgrade 01-A` — Mua hàng và nhập kho từ phiếu mua (`OISMS-47` — Bước 3):** Phiếu mua hàng `purchase_orders` có bước duyệt, bắt buộc phiếu nhập NCC gắn phiếu mua đã duyệt, chặn nhập vượt, lấy giá và hệ số từ phiếu mua.
- [ ] **`Upgrade 01-B` — Hóa đơn NCC và chênh lệch (`OISMS-47` — làm sau `01-A`):** Hóa đơn NCC `supplier_invoices`, đối chiếu ba bên và chứng từ chênh lệch `purchase_variances`.
- [x] **`Upgrade 02` — Truy vết chứng từ xuất kho (`OISMS-47` — Bước 4):** Chuẩn hóa chứng từ nguồn cho mọi dòng `OUT` và hóa đơn/biên nhận bán `sales_invoices`. Backend đã xong 03/11/2026 ([báo cáo](docs/report-after-upgrade/hoa-don-truy-vet-xuat-kho.md)); giao diện Admin, POS đã xong 04/11/2026.
- [x] **`Upgrade 03` — Quy định kiểm kê định kỳ & đột xuất (`OISMS-47` — Bước 5):** Lịch kiểm kê Giữa tháng (ngày 15) / Cuối tháng / Đột xuất và bước duyệt chênh lệch `PendingApproval`.

### 0A.3. Chức năng chưa hoàn thiện / Coming soon

Gắn nhãn hoặc khóa nút giúp phản ánh đúng hiện trạng; **không được đánh dấu hoàn thành yêu cầu** chỉ vì đã gắn Coming soon. Phạm vi và điều kiện sử dụng thật: [CHECKLIST lỗi, mục B](docs/bug-fixaffter/CHECKLIST.md#b-chức-năng-coming-soon).

- [x] **CS-01:** Luồng nhận hàng trả từ khách, liên kết đơn gốc và ghi nhận tồn/lịch sử. *(Xong 09/11/2026, PR #41: phiếu trả, nhập `IN` theo giá vốn lúc bán, tính lại WAC, trừ doanh thu theo ngày trả.)*
- [ ] **CS-02:** Danh sách, tạo/sửa nhân viên, phân quyền và phân công chi nhánh. *(Còn thiếu phân công chi nhánh.)*
- [x] **CS-03:** Lưu ngưỡng cảnh báo trên server và đồng bộ Kho/Báo cáo.
- [x] **CS-04:** Tìm kiếm toàn hệ thống ở thanh đầu trang.
- [x] **CS-05:** Thanh toán QR VietQR ở POS (`pos-app`; lúc nghiệm thu 07/11 có cả POS PWA và POS Admin), thu ngân chọn tài khoản nhận và xác nhận thủ công; Owner quản lý tài khoản nhận tiền theo cửa hàng; `payments` lưu bản chụp người nhận. *(Xác nhận tự động qua webhook ngân hàng tạm gác. Bằng chứng: [review PR 39](docs/report-after-upgrade/vietqr-cs05-review-pr39.md).)*
- [ ] **CS-06:** In chứng từ giao hàng và báo cáo Admin. *(11/11/2026: in biên nhận POS và in lại hóa đơn bán đã có; hai nút in còn lại vẫn Coming soon.)*
- [x] **CS-07:** KPI bán hàng, đơn gần đây và biểu đồ Tổng quan từ dữ liệu server đầy đủ.
- [ ] **CS-08:** Dự báo và gợi ý nhập hàng AI có API/model/UI và bằng chứng đánh giá. *(Đã có API và giao diện, gợi ý theo quy tắc; tab mở lại cho Owner, Staff ngày 10/11/2026. Có unit test `AiForecastingTests`; còn thiếu bằng chứng đánh giá độ chính xác.)*

### 0A.4. Bằng chứng và điều kiện nghiệm thu tiếp theo

- [x] Backend unit tests trong đợt rà soát: **158 PASS, 0 FAIL**.
- [x] Frontend regression React/DOM trong đợt rà soát: **13 PASS, 0 FAIL**.
- [x] Đã ghi bằng chứng cho các kịch bản tái hiện có kiểm soát và link cảnh báo sai tab trên browser. Script tái hiện lỗi thoát 0 không có nghĩa sản phẩm PASS.
- [x] Backend integration suite đầy đủ đã xuất TRX sau lần kiểm tra trước: **285 PASS, 0 FAIL, 0 SKIP**; tổng backend **443 PASS**. Kết quả này không thay thế nghiệm thu frontend E2E hoặc đóng các finding còn mở.
- [ ] Nghiệm thu E2E các luồng cơ bản trên database test riêng, gồm tải lại trang, lỗi mạng/kết quả unknown, đổi tài khoản và đổi chi nhánh.
- [ ] Review và đóng từng finding sau bản sửa cùng bằng chứng hồi quy; giữ các finding bàn giao cũ ở trạng thái chờ review nếu chưa đủ bằng chứng.

Trước khi đẩy nhánh ngày 29/10, đã chạy lại build Admin và POS riêng: cả hai PASS. 14 kiểm thử ở lượt triển khai trước chỉ được dẫn lại, không nhận là vừa chạy lại trong audit. Xem [lịch sử các đợt](docs/bug-fixaffter/README.md#lịch-sử-các-đợt) để biết lệnh, giới hạn và nguồn bằng chứng.

---

## 1. Checklist Yêu Cầu Chức Năng (Functional Requirements — `FR-*`)

> Baseline Sprint 2–3. Nhiều mục `[ ]` dưới đây đã có code ở Sprint 4 nhưng chưa có biên bản nghiệm thu E2E theo đúng điều kiện ghi ở từng dòng; hiện trạng xem [mục 0B](#0b-hiện-trạng-sản-phẩm--cập-nhật-11112026).

### 1.1. Quản lý Đa khách thuê, Xác thực & Phân quyền (`FR-AUTH`) — *Phụ trách: Nhựt & Đăng Khoa*
- [x] **`FR-AUTH-01` (Tenant Registration — Hoàn thành Sprint 2):** Đăng ký cửa hàng mới (`POST /api/auth/register-tenant`), tạo không gian dữ liệu cô lập với `TenantId` riêng, khởi tạo nguyên tử Chi nhánh/Kho mặc định và Owner; chuẩn hóa số điện thoại `E.164` (`PhoneHelper.cs`).
- [x] **`FR-AUTH-02` (User Authentication — Hoàn thành Sprint 2):** Đăng nhập/Đăng xuất qua Email hoặc SĐT + Mật khẩu (`POST /api/auth/login`, `/logout`, `/refresh-token`, `/me`); trả về JWT chứa `TenantId`, `UserId`, `Role`; xoay vòng Refresh Token nguyên tử với khóa dòng `FOR UPDATE` và chặn Tenant/User bị khóa (`IsActive = false`).
- [x] **`FR-AUTH-03` (Role-Based Access Control - RBAC — Hoàn thành Sprint 2):** Phân quyền chặt chẽ 3 vai trò `Owner` (Toàn quyền), `Staff` (Kho/Đơn hàng), `Cashier` (Chỉ POS) trên cả Backend API (`[Authorize(Roles)]`) và cổng đăng nhập tập trung Frontend (`Smart RBAC` + chặn trang `/403`).
- [x] **`FR-AUTH-04` (Branch & Warehouse Management — Hoàn thành Sprint 2):** Cho phép `Owner` tạo, cập nhật, bật/tắt trạng thái hiển thị (`IsActive`) của Chi nhánh (`/api/branches`) và Kho hàng (`/api/warehouses`); chặn đổi chi nhánh đối với kho đã có lịch sử giao dịch sổ cái (`409 Conflict`).

### 1.2. Quản lý Danh mục, Sản phẩm, Biến thể SKU, Mã vạch & Giá (`FR-PROD`) — *Phụ trách: Nhựt, Phúc Hữu & Nam Nguyễn*
- [x] **`FR-PROD-01` (Category & Brand Management — Hoàn thành Sprint 2):** Quản lý cây danh mục phân cấp cha - con (`/api/categories`) và thương hiệu (`/api/brands`); bảo vệ chống tạo vòng lặp chu trình bằng `PostgreSQL Advisory Lock` và thuật toán duyệt tổ tiên.
- [x] **`FR-PROD-02` (Product & Variant/SKU Management — Hoàn thành Sprint 2):** Hỗ trợ sản phẩm đơn giản và đa biến thể (Màu sắc, Kích thước, JSON Attributes giữ nguyên kiểu dữ liệu và `null` lồng nhau); đảm bảo mã `SKU` duy nhất trong cùng một Tenant (`uq_skus_tenant_sku`).
- [x] **`FR-PROD-03` (Barcode Management — Hoàn thành Sprint 2):** Tự động sinh mã vạch chuẩn `EAN-13` (có cơ chế retry 10 lần chống trùng) hoặc nhập thủ công `EAN-13`/`Code128` cho từng SKU; bảo vệ ràng buộc duy nhất theo Tenant (`uq_skus_3`).
- [x] **`FR-PROD-04` (Listed Price Management — Hoàn thành Sprint 2):** Thiết lập và cập nhật Giá bán lẻ (`RetailPrice`) và Giá bán sỉ (`WholesalePrice`) theo từng SKU (`PUT /api/products/{pId}/variants/{vId}/prices`), tách biệt hoàn toàn với giá vốn (`UnitCost`).

### 1.3. Sổ cái tồn kho & Nghiệp vụ kho (`FR-INV`) — *Phụ trách: Đăng Khoa, Phúc Hữu & Nam Nguyễn*
- [x] **`FR-INV-01` (Append-Only Inventory Ledger — Hoàn thành Sprint 2):** Ghi nhận nghiêm ngặt biến động kho vào bảng sổ cái chỉ ghi thêm `InventoryTransaction` (`TenantId`, `BranchId`, `WarehouseId`, `SKUId`, `Type (IN/OUT)`, `Quantity`, `BalanceAfter`, `ReferenceId`, `CreatedAt`); cấm tuyệt đối sửa trực tiếp cột `on_hand` hoặc `UPDATE`/`DELETE` sổ cái; hoàn thiện giao diện tra cứu Sổ cái trên Web Admin.
- [x] **`FR-INV-02` (Purchase Receipt Management — Hoàn thành Sprint 2):** Lập phiếu nhập hàng đa dòng từ Nhà cung cấp (`Draft`) và xác nhận nhập kho nguyên tử (`POST /api/inventory/goods-receipts/{id}/confirm`) $\rightarrow$ Tăng `on_hand` $\rightarrow$ Ghi sổ cái `IN` $\rightarrow$ Cập nhật lại giá vốn bình quân (`WAC`). Hoàn thiện toàn diện cả Backend và giao diện Web Admin.
- [ ] **`FR-INV-03` (Stock Transfer Management — Sprint 3):** Nhóm chức năng chuyển kho hai bước, gồm backend và giao diện.
  - [x] **Đăng Khoa (BE1):** API lập phiếu, dispatch, receive và hủy Draft; kiểm chứng giá vốn, ledger, idempotency và rollback — Task 4A VERIFIED v1.2.
  - [ ] **Phúc Hữu & Nam Nguyễn:** Nối giao diện chuyển kho và nghiệm thu UI E2E; chưa có bằng chứng hoàn tất trong lượt cập nhật này. *(11/11/2026: Admin có trang `/inventory/transfers`; chưa có biên bản E2E.)*
- [ ] **`FR-INV-04` (Stocktake Management — Sprint 3):** Nhóm chức năng kiểm kê, gồm backend và giao diện.
  - [x] **Đăng Khoa (BE1):** Snapshot tồn/sequence, ghi số đếm, chốt kiểm kê tạo chứng từ điều chỉnh và kiểm tra drift — Task 4B VERIFIED v1.3.
  - [ ] **Phúc Hữu & Nam Nguyễn:** Nối giao diện kiểm kê và nghiệm thu UI E2E; chưa có bằng chứng hoàn tất trong lượt cập nhật này. *(11/11/2026: Admin có trang `/inventory/counts` gồm lịch và duyệt chênh lệch; luồng kiểm kê đã chạy tay trong [báo cáo 04/11](docs/report-after-upgrade/frontend-coming-soon-va-upgrade.md); chưa có biên bản E2E đầy đủ.)*

### 1.4. Tính Giá vốn bình quân gia quyền (`FR-COST`) — *Phụ trách: Đăng Khoa*
- [x] **`FR-COST-01` (Automated Batch COGS Calculation — Hoàn thành Sprint 2):** Tự động tính lại giá vốn đơn vị của SKU ngay khi xác nhận Phiếu nhập kho theo công thức bình quân gia quyền: $\text{NewCost} = \frac{(\text{OldStock} \times \text{OldCost}) + (\text{ImportQty} \times \text{ImportPrice})}{\text{OldStock} + \text{ImportQty}}$.
- [x] **`FR-COST-02` (COGS Snapshot on Dispatch — Backend hoàn thiện Sprint 3):** Lưu giá vốn tại thời điểm POS checkout hoặc xác nhận xuất đơn online vào `OrderItem.CostPrice`, dùng cho lịch sử đơn và ledger; không chốt khi đơn chỉ Reserved. Bằng chứng trong Task 2 v1.2 và Task 3 v1.1.

### 1.5. Cơ chế Giữ chỗ & Chống bán vượt (`FR-RSE`) — *Phụ trách: Đăng Khoa (Trưởng nhóm)*
- [x] **`FR-RSE-01` (Available Stock Calculation — Hoàn thành Sprint 2):** Duy trì động tồn khả dụng theo SKU tại từng chi nhánh/kho (`available = on_hand - reserved`) trên toàn bộ API tồn kho và API tra cứu POS.
- [x] **`FR-RSE-02` (Stock Reservation Execution — Hoàn thành nền tảng ở Sprint 2):** Hiện thực `ReservationService.cs` và endpoint `POST /api/inventory/reserve`: tăng `reserved` khi giữ chỗ, từ chối bằng `409 Conflict` nếu `available < Quantity`, kiểm tra `IsActive` 4 cấp (Branch/Warehouse/Product/SKU) và chống giữ chỗ lặp bằng `Idempotency-Key` / `RequestKey` + `PostgreSQL Advisory Lock`.
- [x] **`FR-RSE-03` (Deduct/Release Execution — Backend hoàn thiện Sprint 3):** Confirm trừ OnHand/Reserved và ghi OUT; cancel/expiry giải phóng giữ chỗ đúng một lần, không ghi IN bù khi hàng chưa xuất. Có bằng chứng rollback/concurrency tại Task 3 v1.1 và worker thật tại Task 6 v1.2; worker chỉ tự chạy khi được bật/cấu hình trên môi trường đích.

### 1.6. Trung tâm Đơn hàng đa kênh (`FR-ORD`) — *Phụ trách: Đăng Khoa, Nhựt & Phúc Hữu*
- [x] **`FR-ORD-01` (Canonical Order Model Foundation — Hoàn thành nền tảng CSDL & Entity ở Sprint 2):** Đã thiết kế và khởi tạo bảng `orders`, `order_items` chuẩn hóa đa kênh và hỗ trợ khởi tạo đơn nháp trong luồng giữ chỗ.
- [ ] **`FR-ORD-01..03` (Full Order Hub, State Machine & Order Processing — Hoàn thiện đủ ở Sprint 3: `18/10 – 22/10`):**
  - [x] **Đăng Khoa:** Máy trạng thái đơn hàng và lõi webhook canonical — Task 3 v1.1 / Task 5 v1.4 đã nghiệm thu; gồm Reserved, Confirmed, Completed, Cancelled và xử lý Draft legacy theo contract.
  - [ ] **Nhựt:** API `list / detail / filter` đơn hàng và chứng từ kho; T02 BE2 vẫn PLANNED theo hồ sơ hiện có.
  - [ ] **Phúc Hữu & Nam Nguyễn:** Form tạo/duyệt/hủy đơn và in phiếu; chưa xác nhận nghiệm thu UI E2E trong lượt này.

### 1.7. Bán hàng tại quầy PWA POS (`FR-POS`) — *Phụ trách: Phương Lê & Đăng Khoa*
- [x] **`FR-POS-01` (Fast Sales Interface — Hoàn thành Sprint 2):** Hoàn thiện ứng dụng `src/frontend/pos-app` (React 19 PWA) tối ưu cảm ứng và phím tắt, kết nối API thật tìm kiếm sản phẩm theo Tên, mã SKU (`/api/pos/search`) và quét mã vạch (`/api/pos/barcode/{code}`).
- [x] **`FR-POS-02` (Counter Sales Constraint — Hoàn thành Sprint 2):** Kiểm tra tức thì số lượng `available` khi chọn hàng vào giỏ, chặn tuyệt đối việc thêm/tăng số lượng vượt quá tồn khả dụng để bảo vệ phần hàng đã giữ chỗ cho đơn online.
- [x] **`FR-POS-03` (Receipt Printing & Payment UI — Hoàn thành giao diện & in hóa đơn ở Sprint 2):** Hỗ trợ chọn phương thức thanh toán Tiền mặt (tự tính tiền thừa) / QR Code và in hóa đơn nhiệt khổ `80mm`/`58mm` qua CSS `@media print` và `window.print()`.
- [ ] **`FR-POS-04` (Fast Checkout Atomic Workflow — Hoàn thiện đủ ở Sprint 3: `18/10 – 22/10` & Tối ưu ở Sprint 4):** Kết nối nút Thanh toán POS với API giao dịch toàn giỏ nguyên tử (`Reserve -> Confirm -> Deduct`), xử lý idempotency chống bấm đúp và hoàn thiện trải nghiệm PWA/QR ở Sprint 4.
  - [x] **Đăng Khoa (BE1):** `/api/pos/checkout` và alias `/api/orders/checkout` dùng cùng transaction toàn giỏ; tạo đơn/payment, trừ tồn, lưu giá vốn, ghi OUT và bảo vệ retry — Task 2 VERIFIED v1.2.
  - [ ] **Phương Lê / tích hợp:** Nghiệm thu frontend checkout với backend, lỗi mạng/kết quả chưa xác định, in hóa đơn và hoàn thiện PWA/QR; chưa đánh dấu hoàn tất toàn bộ nhóm.

### 1.8. Giả lập Webhook, Thời gian thực SignalR & Tác vụ nền Hangfire (`FR-SIM`) — *Đăng Khoa phụ trách lõi/Simulator; Nhựt phụ trách automated core tests theo phân công v11*
- [ ] **`FR-SIM-01` (E-commerce Webhook Simulator — Sprint 3):** **Đăng Khoa (BE1)** phụ trách Simulator ba kênh; phối hợp **Nhựt (BE2)** về automated core tests.
  - [x] Endpoint nhận payload canonical `/api/webhooks/simulator/{channel}`, xác thực shop, chống trùng và giữ tồn thật đã nghiệm thu trong Task 5.
  - [x] Trên nhánh tích hợp có API `/api/webhooks/simulator/batch` sinh đơn tuần tự và gọi service nội bộ; không ghi nhận là sender HTTP concurrent hoặc công việc triển khai riêng của BE1.
  - [ ] Công cụ sender/UI giả lập đầy đủ và runner HTTP concurrent; chưa có bằng chứng nghiệm thu toàn bộ trong hồ sơ cập nhật này. *(11/11/2026: UI giả lập đã có, xem [DOC-SIM-14](docs/14-webhook-simulator/README.md); runner HTTP concurrent chưa có.)*
- [ ] **`FR-SIM-02` (Real-time Push Notification SignalR — Sprint 3):** Gồm backend phát sự kiện và frontend nhận thông báo.
  - [x] **Đăng Khoa:** SignalR Hub xác thực JWT, cô lập nhóm tenant/chi nhánh và phát sự kiện sau commit — Task 5 VERIFIED v1.4.
  - [ ] **Phúc Hữu / Phương:** Nối giao diện nhận popup, chuông và refetch khi reconnect; chưa xác nhận nghiệm thu UI E2E.
- [ ] **`FR-SIM-03` (Background Processing Hangfire — Kế hoạch Sprint 3 & Sprint 4):**
  - [x] **Sprint 3 — Đăng Khoa:** Hangfire PostgreSQL tự hủy đơn đủ điều kiện có reservation quá hạn, giải phóng toàn giỏ nguyên tử; worker thật đã được kiểm chứng — Task 6 VERIFIED v1.2.
  - [ ] **Sprint 4 — Đăng Khoa:** Hangfire tổng hợp báo cáo hằng ngày (`daily report aggregations`); không thuộc nghiệm thu auto-expiry Task 6.

### 1.9. Báo cáo & Cảnh báo tồn kho (`FR-REP`) — *Phụ trách: Nhựt, Phúc Hữu, Nam Nguyễn & Đăng Khoa*
- [ ] **`FR-REP-01..03` (Báo cáo cơ bản & Cảnh báo — Kế hoạch Sprint 3: `18/10 – 22/10`):** **Nhựt** viết câu truy vấn báo cáo `net revenue`, `COGS`, `gross profit`, `inventory value`, `velocity`, `low-stock`, `threshold`; **Phúc Hữu & Nam Nguyễn** nối các báo cáo cơ bản vào Dashboard đúng session, filter và role.
  - [x] **Fix sau — `BE2-F03` / T03:** *(Đóng 09/11/2026 theo [ma trận RBAC v1.3.0](docs/01-users-and-rbac/02-rbac-matrix.md): Staff được xem doanh thu và hàng trả, các trường giá vốn và lãi gộp trả `null`; Cashier 403.)* Yêu cầu gốc: giới hạn báo cáo tài chính về Owner.
  - [x] **Fix sau — `BE2-F04` / T04:** ~~Revenue hiện cộng cả đơn Confirmed; chỉ tính Completed theo yêu cầu đang lưu.~~ *(Đóng 11/11/2026: nhóm chốt giữ cách tính `Confirmed` và `Completed`, vì từ `Confirmed` hàng đã xuất kho và đã chụp giá vốn. Quy tắc ghi trong [`fr-11`](docs/02-functional-requirements/fr-11-reports-analytics.md) v1.1.0. Case đối chứng cũ "Completed 30 + Confirmed 100 = 30" không còn áp dụng; đúng là 130.)*
  - [ ] **Fix sau — `BE2-F04` / T04:** Bổ sung filter SKU; chỉ cộng doanh thu/COGS các dòng hàng khớp SKU, không cộng toàn bộ đơn nhiều SKU. Chốt thời điểm ghi nhận và biên ngày/múi giờ trước khi sửa. [Ghi nhận chi tiết và điều kiện đóng](docs/09-personal-reports/nhut/sprint-3/codex-antigravity/task-04-report-correctness/brief.md#ghi-nhận-để-fix-sau--27102026). *(11/11/2026: chưa có trong code (`ReportFilterDto` chưa có trường SKU); dời sang sprint sau.)*
- [ ] **`FR-REP-01..03` (Báo cáo nâng cao & Biểu đồ — Kế hoạch Sprint 4: `23/10 – 27/10`):** Hoàn thiện toàn bộ bộ lọc filter, biểu đồ trực quan doanh thu/giá vốn/lãi gộp, tốc độ luân chuyển và cảnh báo tồn thấp.

### 1.10. Dự báo nhu cầu AI & Gợi ý nhập hàng (`AI` - Gói 5) — *Phụ trách: Đăng Khoa, Nhựt, Phúc Hữu & Nam Nguyễn*
- [ ] **Chốt thiết kế AI (Sprint 3: `18/10 – 22/10`) & Hiện thực chạy thật (Sprint 4: `23/10 – 27/10`):** *(11/11/2026: đã có API và tab Gợi ý nhập hàng (gợi ý theo quy tắc, `AiForecastingService`); còn thiếu bằng chứng đánh giá độ chính xác. Trợ lý OISMS `FR-AI-02` là phần bổ sung, xem [fr-13](docs/02-functional-requirements/fr-13-ai-assistant.md).)*
  - **Đăng Khoa:** Thiết kế và hiện thực lõi `AI forecasting & evaluation`.
  - **Nhựt:** Chuẩn bị dữ liệu lịch sử và xây dựng API `recommendation / report` theo model của Khoa.
  - **Phúc Hữu & Nam Nguyễn:** Xây dựng giao diện AI gợi ý nhập hàng trên Web Admin.

---

## 2. Checklist Yêu Cầu Phi Chức Năng (`NFR-*`) — *Sprint 2 – 3 (Xây dựng), Sprint 4 – 5 (Đo kiểm & Hồi quy)*

- [x] **`NFR-PERF-01` (API Response Time cho POS Search/Barcode & Write Transactions — Hoàn thành xây dựng ở Sprint 2, Benchmark chính thức ở Sprint 4):** Tối ưu truy vấn tra cứu POS/Barcode `< 200ms` và giao dịch ghi Nhập kho/Giữ chỗ `< 500ms`. *(11/11/2026: benchmark chính thức chưa chạy; chưa đo p95 trên AWS.)*
- [x] **`NFR-PERF-02` (Concurrency & Anti-Oversell Handling — Hoàn thành lõi ở Sprint 2, Demo UI ở Sprint 4):** Áp dụng khóa dòng bi quan `SELECT ... FOR UPDATE` kết hợp `pg_advisory_xact_lock` trong `ReservationService`, vượt qua bài kiểm thử đồng thời tranh mua 1 SKU khả dụng cuối cùng (`available` không bao giờ âm). *(11/11/2026: màn demo concurrency chưa có; stress test trên AWS chưa chạy.)*
- [x] **`NFR-TENANT-01` (Multi-tenant Data Isolation — Hoàn thành Sprint 2):** Toàn bộ 29 bảng nghiệp vụ (mốc Sprint 2; hiện 44 bảng, 41 query filter) đều có `tenant_id`, thực thi **EF Core Global Query Filters** và khóa ngoại kép theo Tenant, đạt **0% rò rỉ dữ liệu chéo**.
- [x] **`NFR-TENANT-02` (Scalability Indexing — Hoàn thành Sprint 2):** Đã thiết lập đầy đủ Composite Indexes trên `(tenant_id, created_at)` và `(tenant_id, sku_id)` cho bảng Sổ cái và Đơn hàng.
- [x] **`NFR-SEC-01` (Encryption & Authentication — Hoàn thành Sprint 2):** Băm mật khẩu bằng `BCrypt`, JWT Access Token 60 phút và Refresh Token an toàn.
- [x] **`NFR-SEC-02` (ACID Compliance — Hoàn thành Sprint 2):** Toàn bộ luồng Đăng ký Tenant, Xác nhận Nhập kho + Tính WAC, và Giữ chỗ tồn kho đều thực thi trong Transaction nguyên tử, rollback 100% khi có lỗi.
- [x] **`NFR-SEC-03` (Append-Only Ledger Auditability — Hoàn thành Sprint 2):** Bảng `inventory_transactions` chỉ cho phép `INSERT`, cấm tuyệt đối `UPDATE`/`DELETE`.
- [ ] **`NFR-USA-01` (Availability — Sprint 4 – 5):** Độ sẵn sàng `99.5%` trong khung `07:00 – 22:00`. *(11/11/2026: container `restart: always`; bản demo chạy một máy EC2, có thể tắt khi không demo; chưa đo uptime.)*
- [x] **`NFR-USA-02` (POS Usability — Hoàn thành nền tảng ở Sprint 2, tối ưu thiết bị thật ở Sprint 4 & 5):** Giao diện PWA POS tối giản, hỗ trợ quét Barcode + Enter, đáp ứng mượt trên Desktop, Tablet và Mobile POS.
- [x] **`NFR-MAINT-01` (Clean Architecture — Hoàn thành Sprint 2):** Phân tách nghiêm ngặt 4 tầng `OISM.Domain`, `OISM.Application`, `OISM.Infrastructure`, `OISM.WebApi`.
- [x] **`NFR-MAINT-02` (Automated Test Coverage >= 80% — Đạt sớm ngay từ Sprint 2):** Đạt **194/194 bài test Pass 100%**, độ phủ mã nguồn module lõi `OISM.Application.Features.Inventory` đạt **`92.49% – 94.83%`** (vượt xa ngưỡng $\ge 80\%$ của đề bài). *(11/11/2026: unit 380/380, POS 89/89 PASS; integration 372/372 PASS ngày 07/11. Chưa đo lại coverage.)*

---

## 3. Checklist Hạ Tầng, DevOps & Chuẩn Hóa Tài Liệu (`docs/`)

- [x] **Đóng gói Docker Compose trọn bộ (`docker-compose.yml` — Hoàn thành Sprint 2):** Chạy đồng bộ 4 container `postgres`, `backend`, `admin-dashboard`, `pos-app` kèm cấu hình Nginx SPA Fallback (`try_files`). *(11/11/2026: compose có 7 service, thêm `redis`, `pgadmin`, `marketplace-simulator`; production thêm `caddy` qua `docker-compose.prod.yml`.)*
- [x] **Tích hợp liên tục GitHub Actions CI (`.github/workflows/ci.yml` — Hoàn thành Sprint 2):** Tự động build và chạy toàn bộ Unit/Integration Tests với PostgreSQL service container trên Linux runner. *(Hiện CI còn build Admin, POS, app giả lập và chạy `npm test` của POS, app giả lập.)*
- [x] **Chuẩn hóa kiến trúc thư mục tài liệu (`docs/` — Hoàn thành & Đồng bộ Confluence):**
  - [x] [`docs/00-overview/`](docs/00-overview/README.md) — Tầm nhìn, khái niệm cốt lõi & đối chiếu đề bài gốc.
  - [x] [`docs/01-users-and-rbac/`](docs/01-users-and-rbac/README.md) — Đặc tả Actors, Ma trận RBAC & Vòng đời Tenant.
  - [x] [`docs/02-functional-requirements/`](docs/02-functional-requirements/README.md) — 13 file đặc tả chi tiết từng module chức năng (`FR-01` đến `FR-13`, thêm Trợ lý AI).
  - [x] [`docs/03-non-functional-requirements/`](docs/03-non-functional-requirements/README.md) — 5 file đặc tả yêu cầu phi chức năng (`NFR-*`).
  - [x] [`docs/04-architecture-and-design/`](docs/04-architecture-and-design/README.md) — Clean Architecture, Multi-tenancy, ERD, State Machines & 4 Sequence Diagrams.
  - [x] [`docs/05-api-contracts/`](docs/05-api-contracts/README.md) — 5 file đặc tả hợp đồng REST API Endpoints.
  - [x] [`docs/06-testing-and-qa/`](docs/06-testing-and-qa/README.md) — Ma trận RTM (đồng bộ Confluence), Kế hoạch Test & DoD.
  - [x] [`docs/07-devops-and-deployment/`](docs/07-devops-and-deployment/README.md) — Hướng dẫn Docker Compose, CI/CD & Biến môi trường.
  - [x] [`docs/08-sprints/`](docs/08-sprints/README.md) — Kế hoạch & phân công chi tiết `sprint-2`, `sprint-3`, `sprint-4`, `sprint-5` khớp 100% với Confluence Page `5275650`. *(11/11/2026: README Sprint 4 v2.2.0–v2.4.0 và báo cáo tổng kết v1.1.0 chưa đồng bộ lên Confluence.)*
  - [x] [`docs/09-personal-reports/`](docs/09-personal-reports/README.md) — Báo cáo cá nhân chuẩn hóa tên tiếng Anh ngắn gọn của 4 thành viên (`dang-khoa`, `nhut`, `phuc-huu`, `phuong`).
  - [x] [`docs/10-ai-skills/`](docs/10-ai-skills/README.md) — Skill cho Codex/Antigravity/Claude.
  - [x] [`docs/11-ui-screens/`](docs/11-ui-screens/README.md) — Đặc tả màn hình Admin, POS, kết nối sàn, tài khoản nhận tiền và ma trận quyền theo route.
  - [x] [`docs/12-error-and-status-codes/`](docs/12-error-and-status-codes/README.md) — ProblemDetails, danh mục mã lỗi.
  - [x] [`docs/13-deploy-aws/`](docs/13-deploy-aws/README.md) — Triển khai EC2, Docker Compose production, Caddy HTTPS.
  - [x] [`docs/14-webhook-simulator/`](docs/14-webhook-simulator/README.md) — App giả lập đơn sàn qua webhook.
  - [x] [`docs/database/`](docs/database/README.md) — Từ điển dữ liệu 44 bảng, migration, quyền runtime.
  - [x] [`docs/Upgrade-recommended/`](docs/Upgrade-recommended/README.md), [`docs/report-after-upgrade/`](docs/report-after-upgrade/frontend-coming-soon-va-upgrade.md) — Đặc tả nâng cấp nghiệp vụ và báo cáo sau nâng cấp.
  - [x] [`docs/bug-fixaffter/`](docs/bug-fixaffter/README.md) — Theo dõi lỗi `BF-*`, Coming Soon, Upgrade.
