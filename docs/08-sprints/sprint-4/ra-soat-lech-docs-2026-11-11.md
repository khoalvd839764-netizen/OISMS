# Rà soát docs lệch so với code — phục vụ nghiệm thu Sprint 4 (11/11/2026)

Mốc đối chiếu: nhánh `feature/OISMS-58-chatbot-ai`, commit `e2d2a57`. Phạm vi: 343 file `.md` trong `docs/`. Thư mục lịch sử (`08-sprints/sprint-2,3`, `09-personal-reports`) chỉ kiểm tra link, không xét nội dung vì đã ghi rõ là mốc cũ.

Cách quét: grep mẫu lệch đã biết (số bảng, tên class cũ, POS Admin, `Processing`), so toàn bộ route của 28 controller với `docs/05-api-contracts/`, kiểm tra link tương đối và đường dẫn trong backtick, đối chiếu trạng thái CS/Upgrade với code Admin, POS và backend. Chưa chạy build hay test; số test bên dưới là số `[Fact]`/`[Theory]` đếm trong source, không phải kết quả chạy.

## Hiện trạng code dùng làm mốc

| Hạng mục | Code hiện tại |
| --- | --- |
| Số bảng | 44 (`oisms_schema.sql`, `RequiredTables`, `init-oisms.sh`) |
| Controller | 28 (+ `PosCheckoutActionHelper`) |
| Trạng thái đơn | `Draft, Reserved, Confirmed, Completed, Cancelled`, không có `Processing` |
| Job Hangfire | `ReservationExpiryJob`, `StockCountPeriodJob`; không có job tổng hợp báo cáo hằng ngày |
| CS-06 in ấn | `OrderDetailDrawer.tsx:248` "In chứng từ" và `ReportsPage.tsx:317` "In báo cáo" còn nhãn Coming soon. In lại hóa đơn bán (U02) đã có |
| CS-02 | Tạo/sửa/khóa nhân viên, vai trò `Staff`/`Cashier`. Không có bảng hay API phân công chi nhánh |
| U01-B | Không có `supplier_invoices`, `purchase_variances` |
| U02 giao diện | Đã có trang `/invoices` (`SalesInvoicesPage`, modal truy vết, in lại) |
| POS | Chỉ còn `src/frontend/pos-app`; Admin `/pos` chỉ chuyển hướng |
| Test (đếm trong source) | Unit 243 `[Fact]` + 34 `[Theory]`; Integration 382 `[Fact]` + 5 `[Theory]` |
| Màn demo concurrency | Không tìm thấy trong Admin |

## A. Lệch ảnh hưởng trực tiếp nghiệm thu Sprint 4

### `08-sprints/sprint-4/BAO_CAO_TONG_KET_NGHIEM_THU_SPRINT_4.md`

| # | Dòng | Tài liệu ghi | Thực tế | Cần người dùng quyết |
| --- | --- | --- | --- | --- |
| A1 | 11, 136 | "HOÀN THÀNH TOÀN DIỆN (100% DoD)", "100% khối lượng" | Còn thiếu CS-06, U01-B, phân công chi nhánh CS-02, Hangfire báo cáo hằng ngày, màn demo concurrency (đều có trong kế hoạch `README.md` Sprint 4) | Có |
| A2 | 55 (CS-06) | PASS: in phiếu giao hàng và báo cáo A4 | In chứng từ đơn và in báo cáo vẫn Coming soon. Chỉ in hóa đơn POS 80mm và in lại hóa đơn bán là có | Có |
| A3 | 51 (CS-02) | PASS, phân quyền "Owner, Staff" | Vai trò gán được là `Staff`, `Cashier`; chưa có phân công chi nhánh → nên là đạt một phần | Có |
| A4 | 50 (CS-01) | PASS trong Sprint 4 | Làm xong ở PR #41 ngày 09/11/2026, sau mốc kết thúc Sprint 4 ghi trong báo cáo (04/11) | Có: tính vào Sprint 4 hay Sprint 5 |
| A5 | 54 (CS-05) | VietQR "trên cả POS Admin và POS PWA" | POS Admin đã gỡ (OISMS-50, 08/11), chỉ còn `pos-app` | Không |
| A6 | 57 (CS-08) | "Thuật toán AI phân tích…" | Gợi ý nhập hàng dựa trên quy tắc (theo `frontend-coming-soon-va-upgrade.md`); Trợ lý AI (FR-AI-02) là phần khác, làm sau | Không |
| A7 | 63–68 | Bảng Upgrade không có dòng U01-B | U01-B chưa làm, nên ghi rõ "chưa làm" thay vì bỏ qua | Không |
| A8 | 10 | Thời gian `23/10 – 04/11 (D13 – D25)` | `README.md` Sprint 4 và `08-sprints/README.md` ghi `23/10 – 27/10 (D13 – D18)`; Sprint 5 ghi `28/10 – 01/11` → chồng lấn | Có |
| A9 | 75–77 | 212/212 unit, 364/365 integration, 63/63 FE | Là mốc một lần chạy; `CHECKLIST.md` ghi integration 372/372 ngày 07/11. Số test trong source đã tăng. Nên ghi ngày và lệnh chạy | Không |
| A10 | 29–32 | Nhân sự: Phúc Hữu, Phương Lê tạm nghỉ, làm bù Sprint 5 | `README.md` Sprint 4 vẫn phân công hai bạn này (CS-05 cho Phương Lê, Admin cho Phúc Hữu) | Có |

### `08-sprints/sprint-4/README.md`

| # | Dòng | Tài liệu ghi | Thực tế |
| --- | --- | --- | --- |
| A11 | 21 (v2.3.0) | Upgrade 02: "giao diện Admin, POS chưa làm" | Admin đã có `/invoices`, truy vết, in lại |
| A12 | 39 | "`159/159` Unit Tests PASS" | Báo cáo tổng kết ghi 212/212; hai file lệch nhau |
| A13 | 40 | CS-05 "trong `PaymentModal.tsx` trên cả 2 POS" | Chỉ còn một POS |
| A14 | 39–40 | Kế hoạch có Hangfire daily reports, màn demo concurrency, đo NFR POS < 200ms | Chưa có job báo cáo ngày, chưa có màn demo; `03-non-functional-requirements/README.md` ghi "chưa đo lại p95 trên AWS" |

## B. Docs hiện trạng lệch code (nên sửa)

| # | File:dòng | Lệch | Sửa thành |
| --- | --- | --- | --- |
| B1 | `04-architecture-and-design/04-state-machines.md:41` | "trả hàng (CS-01, chưa làm)" | CS-01 đã có, `/api/sales-returns` |
| B2 | `Upgrade-recommended/README.md:10` | "giao diện chưa làm" | Giao diện Admin đã có |
| B3 | `Upgrade-recommended/README.md:15` | "bật QR ở POS Admin… Chưa triển khai" | Đã triển khai (CS-05 `[x]`), POS Admin đã gỡ |
| B4 | `Upgrade-recommended/09-chatbot-ai-co-ban.md:141` | Coi tab Gợi ý nhập hàng và CS-01 là đang ẩn | Tab đã mở lại (10/11), CS-01 đã xong; kho tài liệu RAG nên mô tả hai chức năng này |
| B5 | `bug-fixaffter/CHECKLIST.md:26,38,42` | BF-003, BF-015, BF-019 `[~]` "Đã sửa ở POS Admin; POS riêng cần kiểm chứng lại" | POS hiện tại chính là bản chuyển từ POS Admin; cần kiểm chứng trên `pos-app` rồi đóng `[x]` hoặc ghi lại lý do |
| B6 | `bug-fixaffter/CHECKLIST.md:90` | CS-05 "POS PWA và POS Admin" | Một POS `pos-app` |
| B7 | `report-after-upgrade/frontend-coming-soon-va-upgrade.md:20,37,41,88,99,108` | Còn mô tả "hai POS", "POS Admin" | Đây là file hiện trạng frontend chính, nên thêm ghi chú 08/11 hoặc sửa thành một POS |
| B8 | `report-after-upgrade/frontend-coming-soon-va-upgrade.md:91`, `lich-kiem-ke-duyet-chenh-lech.md:65` | "đủ 43 bảng" | Mốc trước CS-05; hiện là 44 (thêm ghi chú mốc) |
| B9 | `06-testing-and-qa/02-unit-and-integration-test.md:26–28` | Unit 149 Fact + 16 Theory; Integration 357 + 2; "cả hai bản `posCartUnits.ts`" | Source hiện 243 + 34 và 382 + 5; chỉ còn một `posCartUnits.ts` |
| B10 | `06-testing-and-qa/01-requirements-matrix-rtm.md` (v1.0.0, 22/10) | Không có FR-AI-02, trả hàng, mua hàng, bảng giá, đa đơn vị, lịch kiểm kê, hóa đơn bán, tài khoản nhận tiền | Bổ sung dòng cho các chức năng Sprint 4 (Sprint 5 yêu cầu "RTM kiểm chứng 100%") |
| B11 | `04-architecture-and-design/01-clean-architecture.md:57` | "25 controller" | 28 controller |
| B12 | `Upgrade-recommended/10-quen-mat-khau-dang-nhap-sdt.md:118`, `01-mua-hang-nhap-kho-gia-von.md:80` | `RequiredTables` "trong `DatabaseMigrationService` và `Program.cs`" | `Program.cs` không còn `RequiredTables`; chỉ `DatabaseMigrationService` |
| B13 | `03-non-functional-requirements/README.md:30` | Script `pos-app/measure-pos-latency.mjs` | File không còn trong `pos-app` (chỉ có ở `scratch/`) |
| B14 | `11-ui-screens/` | Không có đặc tả màn Tài khoản nhận tiền (`/settings/payment-accounts`) và màn Hóa đơn bán (`/invoices`, chỉ nhắc ở README) | Thêm mục đặc tả hoặc ghi rõ chưa có |
| B15 | `01-users-and-rbac/02-rbac-matrix.md:20` (v1.3.0) | "Staff bị ẩn toàn bộ giá vốn và lãi gộp" | Quy ước dự án: Staff được xem giá vốn ở truy vết, chỉ lãi gộp là Owner. Cần thống nhất câu này với dòng 92 và RBAC thật |

## C. Link và đường dẫn hỏng

Test đã chuyển từ `src/backend/tests` sang `test-oisms/backend` (commit `a9e5837`), component POS chuyển vào `components/pos/`.

| File | Link hỏng | Đường dẫn đúng |
| --- | --- | --- |
| `report-after-upgrade/lich-kiem-ke-duyet-chenh-lech.md` | `src/backend/tests/.../StockCountU03IntegrationTests.cs`, `StockCountPeriodCalculatorTests.cs` | `test-oisms/backend/...` |
| `report-after-upgrade/hoa-don-truy-vet-xuat-kho.md` | `src/backend/tests/.../SalesInvoiceTraceIntegrationTests.cs` | `test-oisms/backend/...` |
| `Upgrade-recommended/05-nhieu-don-vi-tinh.md` | `pos-app/src/components/CartTable.tsx`, `PaymentModal.tsx` | `CartTable` không còn; `components/pos/PaymentModal.tsx` |
| `08-sprints/sprint-3/01-api-contracts-sprint-3.md` | 29 link dùng `../../../../src/...` (thừa một cấp) | `../../../src/...` |
| `08-sprints/sprint-2/TONG_HOP_TOAN_BO_SPRINT_2_NHUT_BACKEND2.md` | `file:///Users/bo/...` | Link tương đối tới `09-personal-reports/nhut/sprint-2/` |

Đường dẫn `src/backend/tests/...` dạng chữ (không phải link) còn trong `database/README.md`, các báo cáo `report-after-upgrade/*` và `bug-fixaffter/errors/BF-011, BF-025, BF-036`. Báo cáo có ngày có thể giữ nguyên; `database/README.md` là tài liệu hiện hành nên sửa.

Đường dẫn file Admin đã gỡ (`admin-dashboard/src/components/pos/PaymentModal.tsx`, `services/posApi.ts`) còn trong `BF-003`, `BF-015`, `BF-019`, `vietqr-cs05-review-pr39.md`, `07-thanh-toan-qr-vietqr.md` (file 07 đã có ghi chú đầu trang).

## D. Đã kiểm, không lệch

- `05-api-contracts/`: đủ toàn bộ route của 28 controller; không có route nào trong docs mà code không có.
- `14-webhook-simulator/README.md`, `11-ui-screens/10-shop-integration-screens.md`: khớp commit `9f962a5` (chọn sản phẩm theo secret).
- `database/README.md`, `03-database-design.md`, `07-devops/01-docker-compose-guide.md`: số 44 bảng đúng; số cũ nằm trong lịch sử phiên bản.
- `fr-06`, `fr-10`, `03-de-bai-cnpm-reference.md`: chữ "Processing" là tên yêu cầu của đề bài, không phải trạng thái đơn. `04-state-machines.md:40` đã ghi rõ không có trạng thái `Processing`.
- `OISM.WebApi` trong `11-ui-screens`, `12-error-and-status-codes`, `ERD.md`: đúng tên hiện tại (grep không phân biệt hoa thường nên bị bắt nhầm).
- `02-functional-requirements/README.md`, `00-overview/01-project-vision.md`: trạng thái CS-01, CS-05, CS-06, U01-B, CS-02 khớp code.
- `05-nhieu-don-vi-tinh.md`, `07-thanh-toan-qr-vietqr.md`: có ghi chú đầu trang rằng mô tả hai POS là bối cảnh cũ.

## Ngoài `docs/`

`.claude/CLAUDE.md` còn ghi 30 bảng, `RequiredTables` ở `Program.cs`, trạng thái `Processing`, nhánh `fix/clean-architecture-20261029`, trạng thái cập nhật 01/11. File này hướng dẫn công cụ AI nên lệch sẽ dẫn tới gợi ý sai.

## Kết quả sửa nhóm B và C (11/11/2026)

Nhóm A xem mục sau.

| Mục | Kết quả |
| --- | --- |
| B1–B4 | Đã sửa trạng thái CS-01, U02, VietQR, ghi chú kho tài liệu Trợ lý AI |
| B5 | BF-003, BF-015 chuyển `[x]` (có test trong `pos-app`); BF-019 giữ `[~]`, ghi rõ code đã chặn nhưng chưa có test riêng. Cập nhật file lỗi và nhật ký `bug-fixaffter/README.md` |
| B6–B8 | Sửa CS-05 trong CHECKLIST; thêm ghi chú cập nhật 11/11 đầu `frontend-coming-soon-va-upgrade.md` (gộp POS, đường dẫn test, 44 bảng); ghi chú mốc bảng ở `lich-kiem-ke-duyet-chenh-lech.md` |
| B9 | `02-unit-and-integration-test.md` lên v1.2.0 với số test mới |
| B10 | RTM lên v1.1.0, thêm mục 4 truy vết chức năng từ Sprint 4 (gồm cả mục chưa làm: U01-B, CS-06, Hangfire báo cáo ngày) |
| B11–B13 | Sửa số controller, `RequiredTables`, script đo POS; NFR cập nhật 41 query filter |
| B14 | Thêm mục 7 "Hóa đơn bán" vào `05-order-screens.md`, file mới `11-payment-account-screens.md`, sửa dòng `/pos` trong mục lục |
| B15 | **Không lệch.** Thân ma trận đúng code (báo cáo ẩn giá vốn với Staff, truy vết cho Staff xem giá vốn dòng). Chỉ làm rõ câu trong lịch sử v1.3.0 |
| C | Sửa 35 link; chạy lại kiểm tra: 0 link hỏng. Ghi chú đường dẫn test mới trong `database/README.md` |

Lệnh đã chạy:

- `npm test --prefix src/frontend/pos-app`: PASS 89/89
- `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false`: PASS 380/380
- Integration test trên PostgreSQL: NOT RUN (cần biến `OISM_TEST_*`)
- Kiểm tay trên trình duyệt: NOT RUN

Còn lại có chủ ý: đường dẫn file Admin POS đã gỡ trong `BF-003`, `BF-015`, `BF-019`, `vietqr-cs05-review-pr39.md` và `src/backend/tests/` trong các báo cáo có ngày được giữ nguyên vì là bằng chứng tại thời điểm viết.

## Kết quả sửa nhóm A (11/11/2026)

Quyết định của nhóm: CS-01 giữ hoàn thành trong Sprint 4; mốc Sprint 4 là 23/10–27/10; CS-02, CS-06 ghi đạt một phần; giữ bảng phân công kế hoạch, thêm ghi chú thực tế.

| Mục | Kết quả |
| --- | --- |
| A1, A7 | Báo cáo tổng kết lên v1.1.0: trạng thái "Hoàn thành phần lớn", thêm dòng U01-B "Chưa làm", viết lại kết luận kèm danh sách hạng mục còn lại |
| A2, A3 | CS-06, CS-02 đổi thành "Đạt một phần", ghi rõ phần đã có và phần thiếu; CS-02 sửa vai trò thành `Staff`, `Cashier` |
| A4 | Giữ CS-01 PASS theo quyết định |
| A5, A6 | CS-05 ghi chú POS đã gộp 08/11; CS-08 mô tả đúng là gợi ý theo quy tắc |
| A8 | Thời gian thực hiện 23/10–27/10 (D13–D18); thêm dòng "Ngày chốt số liệu nghiệm thu 04/11/2026" vì số test và deploy lấy theo ngày này |
| A9 | Ghi ngày chạy 04/11/2026 cho số test |
| A10–A14 | README Sprint 4 lên v2.4.0, thêm mục 3 "Thực tế thực hiện so với kế hoạch" (nhân sự, U02, POS, số test, hạng mục chưa đạt) |
