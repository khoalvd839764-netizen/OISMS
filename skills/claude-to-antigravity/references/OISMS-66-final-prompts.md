# OISMS-66 — Plan fix trước bản final và prompt giao Antigravity

Lập 21/11/2026 từ [MUST-DO-UPDATE-BEFORE-FINAL.md](../../../docs/Upgrade-recommended/Pedding/MUST-DO-UPDATE-BEFORE-FINAL.md). Đặc tả gốc và quyết định Q1–Q13: [goi-5-plan-trien-khai.md](../../../docs/Upgrade-recommended/Pedding/goi-5-plan-trien-khai.md).

**Ngoài plan:** `FR-PROD-03` quét barcode kiểm kê (không làm vì không có thiết bị quét); hồ sơ bàn giao mục 3 (nhóm đang tự update); hai quyết định nhóm (màn Handoff, số lẻ ở đơn vị đếm).

## Prompt khởi động cho Antigravity (dán một lần)

```text
Đọc skills/claude-to-antigravity/references/antigravity.md, rồi đọc skills/claude-to-antigravity/references/OISMS-66-final-prompts.md và làm việc ở CHẾ ĐỘ TỰ CHẠY theo mục "Luật tự lấy việc của Anti" trong file đó.

Tóm tắt:
- Ưu tiên prompt ở mục "Prompt fix đang chờ"; không có thì lấy task READY đầu tiên trong "Bảng trạng thái" mà mọi phụ thuộc đã VERIFIED.
- Làm đúng prompt "Prompt FIN-x" của task đó; mỗi task chỉ đọc nguồn prompt đó nêu.
- Xong task: ghi bàn giao vào tmp/claude-anti/OISMS-66-FIN/handoff-FIN-x.md theo assets/handoff.md, cập nhật cột Trạng thái và Bàn giao, rồi tự lấy task tiếp theo, không chờ tôi.
- Vướng quá 2 lần thử: ghi BLOCKED kèm lý do và chuyển task khác.
- Mọi kết quả test/đo (cả FAIL) phải lưu đủ ba nơi theo mục "Lưu kết quả kiểm thử": docs/06-testing-and-qa/evidence/<mã đề>/, sổ evidence/README.md, và mục "Kết quả kiểm thử bản final (11/2026)" trong 02-unit-and-integration-test.md hoặc 03-concurrency-stress-test.md. Không sửa RTM. Ghi các đường dẫn này trong bàn giao.
- Dừng khi không còn task đủ điều kiện; in bảng tóm tắt các task đã làm trong lượt.

Luật cứng: không commit/push/PR, không tự ghi VERIFIED, không sửa prompt/AC, không docker compose down -v, không chạy gì trên AWS.
```

Sau mỗi lượt Anti dừng: người dùng báo Claude "review FIN" → Claude đọc các file bàn giao, cập nhật bảng (VERIFIED / CHANGES_REQUESTED + prompt fix) → người dùng dán lại prompt khởi động ở trên cho Anti chạy lượt tiếp.

## Plan

Định tuyến **CHIA**. Lý do: nhiều mã đề bài độc lập, trải backend, Admin, POS, script đo và hạ tầng Caddy. Không task nào chạm schema, ledger, transaction hay luồng ghi kho, nên không có task CRITICAL. Riêng FIN-A chạm báo cáo tài chính nên phải giữ che giá vốn với Staff.

| Task | Nội dung | Mục must-do | Mức | Phụ thuộc | Trần vòng sửa |
| --- | --- | --- | --- | --- | --- |
| FIN-A | Backend lọc báo cáo doanh thu theo nhiều SKU | 1.1 | STANDARD | — | 2 |
| FIN-B | Admin: bộ chọn SKU ở tab P&L | 1.1 | STANDARD | FIN-A | 2 |
| FIN-C | Tách công thức dự báo + bộ đánh giá backtest | 4.2 | STANDARD | — | 2 |
| FIN-D | Script đo độ trễ, dữ liệu bulk, chạy lại 50 request tranh 1 đơn vị, EXPLAIN index | 2.1 | STANDARD | FIN-A (đo lọc SKU) | 2 |
| FIN-E | Caddy chỉ TLS 1.3, chặn `/health/ready` ra ngoài (BF-026), script uptime, nghiệm thu thao tác POS | 2.2, 2.3, 2.4, 5.1 | STANDARD | — | 2 |
| FIN-F | Đo coverage theo nhóm lớp lõi, bổ sung test nếu < 80% | 2.5 | STANDARD | FIN-A, FIN-C | 2 |
| FIN-G | Test hồi quy POS BF-019 và BF-020, sửa nếu BF-020 còn lỗi | 4.3 | STANDARD | — | 2 |
| FIN-H | Biên bản E2E chuyển kho và kiểm kê trên Admin local | 4.1 | STANDARD | — | 2 |
| FIN-I | BF-024 tìm kiếm phiếu nhập, BF-027 trường tổng số lượng (trả `null` khi khác đơn vị) | 5.1 | STANDARD | FIN-A | 2 |
| FIN-J | Tối ưu tải dữ liệu báo cáo `revenue-profit` (chỉ lấy cột cần dùng), đo lại NFR-PERF-01 | 2.1 | STANDARD | FIN-A, FIN-D, FIN-I | 2 |

**Thứ tự gửi:** đợt 1 song song (khác file): FIN-A, FIN-C, FIN-E, FIN-G, FIN-H. Đợt 2: FIN-B, FIN-D, FIN-I. Đợt 3: FIN-F. Mỗi task tối đa 3 prompt gửi Anti.

**Claude tự làm (không giao Anti):**

- BF-025: thêm `*.sql text eol=lf` vào `.gitattributes` (1 dòng).
- BF-021: code đã che `costOfGoodsSold`/`grossProfit` với Staff qua `ICostVisibilityService` và route `revenue-profit` đã nằm trong `PostgresCostVisibilityScanIntegrationTests`. Claude chạy lại test đó trên PostgreSQL rồi đóng BF-021 trong `docs/bug-fixaffter/CHECKLIST.md` và file lỗi.
- Review từng bàn giao; sau khi VERIFIED cập nhật `checklist.md`, MUST-DO, `docs/Upgrade-recommended/Pedding/README.md`, `docs/bug-fixaffter/CHECKLIST.md`.

**Người dùng làm:** chuyển prompt; đồng ý cài `dotnet-reportgenerator-globaltool` ở FIN-F; đồng ý commit.

**Đã chốt BF-027 (người dùng, 21/11/2026):** giữ trường `total*Quantity` để tương thích nhưng trả `null` khi các SKU trong kết quả khác đơn vị gốc; cùng đơn vị thì trả tổng như cũ. Docs ghi rõ.

**Môi trường:** mọi đo và test chạy local (Docker Compose, PostgreSQL máy dev). Không chạy trên AWS. Test PostgreSQL cần biến `OISM_TEST_*` (xem `.github/workflows/ci.yml`).

**Baseline chung:** branch `feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2`, commit `879a1ef`. Giữ nguyên các file docs chưa commit (MUST-DO, checklist, Pedding README, goi-2, file này).

## Bảng trạng thái (nguồn sự thật cho chế độ tự chạy)

Anti chỉ được sửa cột **Trạng thái** và **Bàn giao** của task mình đang làm, theo luật bên dưới. Chỉ Claude được ghi `VERIFIED` hoặc `CHANGES_REQUESTED`.

| Task | Phụ thuộc (phải `VERIFIED`) | Trạng thái | Bàn giao |
| --- | --- | --- | --- |
| FIN-A | — | VERIFIED | tmp/claude-anti/OISMS-66-FIN/handoff-FIN-A.md |
| FIN-C | — | VERIFIED | tmp/claude-anti/OISMS-66-FIN/handoff-FIN-C.md + FIN-C-fix1 (Claude tự sửa) |
| FIN-E | — | VERIFIED | tmp/claude-anti/OISMS-66-FIN/handoff-FIN-E.md + FIN-E-fix1 (Claude tự sửa) |
| FIN-G | — | VERIFIED | tmp/claude-anti/OISMS-66-FIN/handoff-FIN-G.md |
| FIN-H | — | VERIFIED | tmp/claude-anti/OISMS-66-FIN/handoff-FIN-H.md |
| FIN-B | FIN-A | VERIFIED | tmp/claude-anti/OISMS-66-FIN/handoff-FIN-B.md |
| FIN-D | FIN-A | VERIFIED | tmp/claude-anti/OISMS-66-FIN/handoff-FIN-D.md + FIN-D-fix1 (Claude tự sửa) |
| FIN-I | FIN-A | VERIFIED | tmp/claude-anti/OISMS-66-FIN/handoff-FIN-I.md + FIN-I-fix1 (Claude tự sửa) |
| FIN-F | FIN-A, FIN-C | VERIFIED | tmp/claude-anti/OISMS-66-FIN/handoff-FIN-F.md |
| FIN-J | FIN-A, FIN-D, FIN-I | VERIFIED | tmp/claude-anti/OISMS-66-FIN/handoff-FIN-J.md (review: evidence/goi-5/NFR-PERF-01/2026-11-21-FIN-J-review.md) |

Giá trị trạng thái: `READY` → `IMPLEMENTING` → `READY_FOR_REVIEW` → `VERIFIED`; `CHANGES_REQUESTED` (Claude ghi, kèm prompt fix ở mục dưới); `BLOCKED` (Anti ghi, kèm lý do trong file bàn giao).

## Prompt fix đang chờ

Claude thêm prompt fix vào đây sau mỗi lần review có finding chặn. Anti làm prompt fix **trước** mọi task mới.

**Review 21/11/2026 (Claude):** build 0 lỗi; Unit PASS 434/434; Integration PostgreSQL 16 local PASS 480/480 (20 phút 46 giây); Admin build OK; POS build, lint (0 lỗi) và `npm test` PASS 101/101. FIN-A, B, F, G, H: `VERIFIED`. Bốn task dưới cần một vòng fix. **Cập nhật cùng ngày: người dùng yêu cầu Claude tự sửa; cả bốn fix đã xong và `VERIFIED`, Anti không làm lại các prompt fix này.** Bằng chứng: `evidence/CS-04/2026-11-21-FIN-I-fix1.md`, `docs/13-deploy-aws/*`, `evidence/goi-5/NFR-PERF-01/2026-11-21-FIN-D-fix1.md`, `evidence/FR-AI-01/2026-11-21-FIN-C-fix1.md`.

### Fix FIN-I-fix1 — BF-024 giữ `receiptId` sau khi đóng phiếu

FINDING: `InventoryPage.tsx` không còn xóa `receiptId` khỏi URL, nhưng `closeReceiptModal` cũng không xóa. Đóng phiếu xong bấm F5 thì phiếu tự mở lại, và mọi lần `searchParams` đổi (ví dụ mở `?purchaseOrderId=`) cũng mở lại phiếu cũ.
YÊU CẦU: khi đóng modal phiếu nhập (mọi đường đóng: `closeReceiptModal` và các chỗ gọi `setReceiptModal(false)` cho phiếu đã lưu), xóa `receiptId` khỏi URL bằng `setSearchParams(..., { replace: true })`. Giữ hành vi F5 khi modal còn mở. Không sửa backend.
GATE: `npm run build --prefix src/frontend/admin-dashboard`; kiểm tra tay 3 ca (mở từ tìm kiếm + F5 vẫn mở; đóng + F5 không mở lại; mở phiếu khác từ tìm kiếm mở đúng phiếu mới). Lưu bằng chứng `evidence/CS-04/<ngày>-FIN-I-fix1.md`, thêm dòng sổ tổng hợp, cập nhật mục Cách sửa của BF-024.

### Fix FIN-E-fix1 — docs deploy còn gọi `/health/ready` qua domain công khai

FINDING: sau khi Caddy trả 403 cho `/health/ready`, các hướng dẫn sau vẫn bảo kiểm tra `https://<ADMIN_DOMAIN>/health/ready` và mong HTTP 200: `docs/13-deploy-aws/04-quy-trinh-deploy-va-cap-nhat.md` (dòng ~40, ~45, ~76, ~119, ~144, bảng ~189), `05-van-hanh-giam-sat-backup.md` (dòng ~36–38, ~73, gợi ý uptime ngoài gọi `/health/ready`), `01-kien-truc-trien-khai.md` (~99, còn ghi `/health/ready` công khai), `README.md` (~18), `03-cau-hinh-moi-truong-production.md` (~115, ~142).
YÊU CẦU: chỉ sửa docs. Kiểm tra readiness đổi sang lệnh nội bộ trên host, ví dụ `docker compose ... exec backend wget -qO- http://localhost:8080/health/ready` hoặc `curl` từ host vào cổng nội bộ (đối chiếu `docker-compose.prod.yml` xem backend có publish cổng không, chọn lệnh chạy được). Uptime ngoài dùng `/health/live`. Ghi rõ thay đổi chỉ có hiệu lực sau khi deploy Caddyfile mới; không chạy gì trên AWS.
GATE: `grep -rn "health/ready" docs/13-deploy-aws` không còn chỗ nào gọi qua domain công khai mà mong 200. Thêm dòng sổ tổng hợp (Loại: Docs).

### Fix FIN-D-fix1 — đo lại `POST /api/orders`

FINDING: `measure-latency.mjs` gửi payload sai DTO nên `POST /api/orders` trả lỗi 0/30; đây là lỗi bộ đo, không phải số đo.
YÊU CẦU: đọc DTO tạo đơn Admin (`Features/Orders/Dtos`, `docs/05-api-contracts/04-order-endpoints.md`) và sửa payload, có `Idempotency-Key` mới mỗi request. Chạy lại riêng nhóm này với c=1 và c=10 trên `oisms_perf`, ghi p50/p95/p99 và ĐẠT/CHƯA ĐẠT. Các số còn lại giữ nguyên, không đo lại. Không tối ưu code báo cáo (đó là quyết định của nhóm).
GATE: kết quả ≥ 29/30 request thành công mỗi mức; cập nhật `evidence/goi-5/NFR-PERF-01/` (file mới `<ngày>-FIN-D-fix1.md` + JSON thô), sổ tổng hợp, `01-performance-and-load.md`.

### Fix FIN-C-fix1 — chưa đo recall trên nhóm Eligible

FINDING: `report.md` ghi kết luận **ĐẠT**, nhưng nhóm Eligible có "Cần nhập thực tế = 0" nên recall là N/A. Ngưỡng Q6 yêu cầu cả WAPE ≤ 35% và recall ≥ 80% trên Eligible, nên hiện mới chứng minh được WAPE.
YÊU CẦU: bổ sung vào `seed-eval.sql` các SKU bán đều (≥ 10 ngày/30 ngày) có tồn thấp ở một số mốc `t` để thực sự cần nhập trong `[t, t+H)`, cùng các SKU tồn dư (để đo cả dương tính giả). Giữ UUID cố định, idempotent, không đổi công thức `reorder-v1`. Chạy lại backtest, ghi recall và precision của nhóm Eligible. Nếu recall < 80% thì ghi **CHƯA ĐẠT** trung thực, không chỉnh công thức.
GATE: `report.md`/`results.json` mới có recall Eligible là số; cập nhật `evidence/FR-AI-01/<ngày>-FIN-C-fix1.md`, sổ tổng hợp, `fr-12-ai-forecasting.md`, `danh-gia-du-bao-nhap-hang-cs08.md`.


## Luật tự lấy việc của Anti

1. Đọc bảng trạng thái từ đầu xuống. Nếu mục "Prompt fix đang chờ" có prompt cho task đang `CHANGES_REQUESTED` thì làm prompt fix đó trước.
2. Không có fix: chọn task **đầu tiên** có trạng thái `READY` mà mọi task phụ thuộc đã `VERIFIED`. `READY_FOR_REVIEW` chưa phải `VERIFIED`, không được dùng làm nền.
3. Đổi trạng thái task đó thành `IMPLEMENTING`, rồi làm đúng prompt của task (mục "Prompt FIN-x" bên dưới), như thể người dùng dán prompt đó.
4. Xong: ghi bàn giao theo `skills/claude-to-antigravity/assets/handoff.md` vào `tmp/claude-anti/OISMS-66-FIN/handoff-FIN-x.md` (vòng fix: `handoff-FIN-x-fix1.md`), đổi trạng thái thành `READY_FOR_REVIEW` (hoặc `BLOCKED`) và ghi đường dẫn file bàn giao vào cột Bàn giao.
5. Quay lại bước 1 và lấy việc tiếp theo, **không chờ người dùng**.
6. Dừng hẳn khi không còn task nào thỏa bước 1–2 (mọi việc còn lại đều chờ Claude review hoặc đã `BLOCKED`). Khi dừng, in một bảng tóm tắt: task đã làm trong lượt, trạng thái, file bàn giao, việc người dùng cần làm.
7. Mỗi task giữ nguyên giới hạn trong prompt của nó: không commit/push, không thêm dependency, một lỗi tối đa 2 lần thử rồi `BLOCKED` và chuyển sang task kế tiếp. Không tự ghi `VERIFIED`, không sửa prompt hay AC trong file này.
8. Task sau không được sửa file thuộc scope của task trước đang `READY_FOR_REVIEW`, trừ khi prompt của nó cho phép rõ.
9. **Lưu kết quả kiểm thử vào hồ sơ đề bài (bắt buộc với mọi task, kể cả vòng fix).** Đề bài yêu cầu hồ sơ "Tài liệu kiểm thử: Unit, Integration, Concurrency" và bằng chứng nghiệm thu theo từng FR/NFR. Thiếu bước này thì bàn giao bị trả lại trước khi review. Xem mục "Lưu kết quả kiểm thử" ngay dưới.

## Lưu kết quả kiểm thử

Mọi lệnh test/đo trong GATE của task đều phải lưu kết quả ở **ba nơi**:

1. **Bằng chứng thô:** `docs/06-testing-and-qa/evidence/<MÃ ĐỀ>/<yyyy-mm-dd>-FIN-x.md` (một thư mục cho mỗi mã FR/NFR ở bảng dưới; task có nhiều mã thì ghi mỗi mã một file). Nội dung: commit + thay đổi chưa commit, máy và môi trường (OS, CPU/RAM khi đo hiệu năng, PostgreSQL thật hay InMemory), dữ liệu thử, lệnh và cwd, số test PASS/FAIL/NOT RUN (ví dụ `PASS 412/412`), tên test liên quan tới mã đề, kết luận ĐẠT / CHƯA ĐẠT / CHƯA ĐO TRÊN DEPLOY, giới hạn. Log dài chỉ giữ phần tóm tắt; file kết quả máy (CSV, JSON, HTML coverage, ảnh) để cùng thư mục.
2. **Sổ tổng hợp:** `docs/06-testing-and-qa/evidence/README.md`. Mỗi lần chạy thêm một dòng vào bảng (tạo bảng nếu chưa có): `Ngày | Task | Mã đề | Loại (Unit / Integration PostgreSQL / Concurrency / E2E / Đo NFR) | Lệnh | Kết quả | Bằng chứng`. Không xóa hay sửa dòng cũ; chạy lại thì thêm dòng mới.
3. **Tài liệu kiểm thử theo loại của đề:**
   - Unit và Integration → `docs/06-testing-and-qa/02-unit-and-integration-test.md`, mục `## Kết quả kiểm thử bản final (11/2026)` (tạo nếu chưa có; mỗi task một tiểu mục ngắn: test mới, lệnh, kết quả, link bằng chứng).
   - Concurrency → `docs/06-testing-and-qa/03-concurrency-stress-test.md`, cùng tên mục, ghi kết quả chạy lại Kịch bản 1 (50 đơn tranh 1 đơn vị).
   - E2E giao diện → biên bản ở `evidence/` và một dòng trong sổ tổng hợp.
   - **Không sửa RTM** (`01-requirements-matrix-rtm.md`) và các tài liệu hồ sơ mục 3 MUST-DO: nhóm đang tự cập nhật, sẽ lấy link từ sổ tổng hợp.

Kết quả FAIL hoặc CHƯA ĐẠT vẫn phải lưu đủ ba nơi, ghi trung thực. Không chạy lại để thay kết quả xấu bằng kết quả đẹp mà không ghi cả hai.

| Task | Mã đề (thư mục evidence) | Loại test cần lưu |
| --- | --- | --- |
| FIN-A | `FR-REP-01` | Unit, Integration PostgreSQL |
| FIN-B | `FR-REP-01` | Build + đối chiếu UI/API (ảnh) |
| FIN-C | `FR-AI-01` | Unit, kết quả backtest (`report.md`, `results.json`) |
| FIN-D | `NFR-PERF-01`, `NFR-PERF-02`, `NFR-TENANT-02` | Đo độ trễ, Concurrency, EXPLAIN |
| FIN-E | `NFR-SEC-01`, `NFR-USA-01`, `NFR-USA-02` | Output openssl/curl, CSV uptime, test:live + ảnh |
| FIN-F | `NFR-MAINT-02` | Coverage Unit + Integration PostgreSQL |
| FIN-G | `FR-POS-03`, `FR-POS-04` (hồi quy BF-019, BF-020) | Test POS (`npm test`) |
| FIN-H | `FR-INV-03`, `FR-INV-04` | E2E giao diện (biên bản + ảnh) |
| FIN-I | `FR-REP-02` (BF-027), `CS-04` (BF-024) | Unit, kiểm tra trình duyệt (ảnh) |

## Prompt FIN-A — Backend lọc báo cáo theo SKU

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-66-FIN-A — Lọc báo cáo doanh thu/COGS/lãi gộp theo SKU   | Mức: STANDARD | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2, commit 879a1ef. Giữ nguyên docs chưa commit có sẵn.

MỤC TIÊU: GET /api/reports/revenue-profit nhận skuIds (tối đa 20) và tính KPI, breakdown kênh/chi nhánh/ngày trên tập SKU đã chọn ở cấp dòng; không truyền skuIds thì payload và kết quả giữ nguyên.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/Pedding/goi-5-plan-trien-khai.md (Quyết định Q1, Q1b, Q2, Q3, Q4, Q4b; Phase A mục A1, A3)
- docs/02-functional-requirements/fr-11-reports-analytics.md
- Mẫu cộng theo dòng: src/backend/src/OISM.Application/Features/Reports/Services/DailySalesSummaryService.cs (cách dùng AllocateOrderRevenueByItem, AllocateReturnRevenueByItem, CalculateReturnItemCogs)
- src/backend/src/OISM.Application/Features/Reports/Services/{ReportService.cs,RevenueProfitCalculationHelper.cs}, Dtos/ReportDtos.cs

ĐƯỢC SỬA: OISM.Application/Features/Reports/**, OISM.WebApi/Controllers/ReportsController.cs (chỉ nếu cần validation), test-oisms/backend/OISM.UnitTests/ReportServiceTests.cs, file integration test mới, docs liệt kê dưới.
KHÔNG ĐƯỢC SỬA: schema, DailySalesSummaryService (chỉ đọc), frontend, các endpoint báo cáo khác.

ACCEPTANCE CRITERIA:
- AC-01 (Q1): ReportFilterDto có List<Guid>? SkuIds; loại trùng; > 20 phần tử → 400 VALIDATION_ERROR (ProblemDetails) → unit/controller test
- AC-02 (Q2): Đơn có SKU A, B và chiết khấu cấp đơn; phiếu trả riêng A ở ngày khác ngày bán. Lọc [A] = phần phân bổ của A trừ phần trả A theo ngày trả; lọc [B] không bị phiếu trả A ảnh hưởng; lọc [A,B] = báo cáo không lọc → unit test
- AC-03 (Q1b): Có skuBreakdown khi lọc (skuId, skuCode, skuName, doanh thu, hàng trả, thuần, COGS, COGS trả, COGS thuần, lãi gộp, số đơn, số lượng theo từng đơn vị); tổng skuBreakdown = KPI; OrderCount đếm đơn khác nhau → unit test
- AC-04 (Q3): SKU không tồn tại hoặc thuộc tenant khác → số 0, không lỗi → integration PostgreSQL
- AC-05 (Q4b): Staff có skuIds nhận null mọi trường COGS/lãi gộp (kể cả trong skuBreakdown); Cashier 403 → integration PostgreSQL; route có skuIds thêm vào PostgresCostVisibilityScanIntegrationTests
- AC-06: Lọc kết hợp SKU + chi nhánh + kênh + khoảng ngày, ranh giới UTC+07 nửa mở; điều kiện SKU dịch được sang SQL (không tải hết đơn về bộ nhớ) → integration PostgreSQL
- AC-07: Không truyền skuIds: toàn bộ ReportServiceTests hiện có PASS, không sửa assertion cũ

INVARIANT PHẢI GIỮ: tenant chỉ từ JWT; che giá qua ICostVisibilityService (không tự kiểm vai trò); chỉ đọc, không mở transaction; múi giờ UTC+07, lọc CreatedAt < endBoundary.

DOCS PHẢI CẬP NHẬT:
- docs/02-functional-requirements/fr-11-reports-analytics.md — lọc nhiều SKU, skuBreakdown, quy tắc phân bổ, phạm vi Q4
- docs/05-api-contracts/04-order-endpoints.md (mục revenue-profit) — skuIds (query lặp), giới hạn 20, skuBreakdown
- Báo cáo: docs/report-after-upgrade/loc-bao-cao-theo-sku-fin-a.md — đã làm, ảnh hưởng chức năng cũ, không đổi DB, kiểm chứng, giới hạn

GATE:
- dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal
- dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false
- dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~Report|FullyQualifiedName~DailySalesSummary|FullyQualifiedName~CostVisibility" (PostgreSQL thật, biến OISM_TEST_*)

GIỚI HẠN: Không commit/push. Không thêm dependency, không migration. Một lỗi tối đa 2 lần thử rồi BLOCKED. Không nới assertion, không skip test. Lệch làm tròn phải theo đúng quy tắc dồn phần dư của helper, không thêm sai số cho phép.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```

## Prompt FIN-B — Admin bộ chọn SKU

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-66-FIN-B — Bộ lọc SKU ở tab P&L màn Báo cáo   | Mức: STANDARD | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2 sau khi FIN-A VERIFIED.

MỤC TIÊU: Owner chọn nhiều SKU (tối đa 20) ở tab P&L, KPI/biểu đồ/bảng hiển thị đúng số API trả, có bảng theo SKU và in được.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/Pedding/goi-5-plan-trien-khai.md (Phase A mục A2; Q4, Q4b)
- docs/report-after-upgrade/loc-bao-cao-theo-sku-fin-a.md và docs/05-api-contracts (skuIds, skuBreakdown)
- src/frontend/admin-dashboard/src/pages/ReportsPage.tsx (đồng bộ ?tab=, activeReqRef, in báo cáo)
- src/frontend/admin-dashboard/src/types/reports.ts, src/frontend/admin-dashboard/src/services/reportService.ts
- Tìm SKU: dùng API tìm SKU sẵn có trong services/ (ghi rõ đã chọn API nào trong bàn giao)

ĐƯỢC SỬA: ReportsPage.tsx, types/reports.ts, services/reportService.ts, component mới trong src/components/reports/ (nếu cần), docs dưới.
KHÔNG ĐƯỢC SỬA: backend, các tab báo cáo khác, phân quyền route.

ACCEPTANCE CRITERIA:
- AC-01: Ô chọn nhiều SKU có tìm kiếm, chip SKU đã chọn, nút bỏ chọn; chặn chọn quá 20 có thông báo
- AC-02: Gửi skuIds dạng tham số lặp; đồng bộ ?skus= trên URL, F5 giữ lựa chọn
- AC-03: Đổi lựa chọn tăng activeReqRef, phản hồi cũ không ghi đè kết quả mới
- AC-04: Đang lọc: hiện bảng skuBreakdown (số lượng hiển thị theo từng đơn vị, không cộng khác đơn vị), ẩn khối đối soát daily_sales_summaries
- AC-05: Bản in tiêu đề có danh sách SKU đang lọc
- AC-06: Bỏ hết SKU: màn hình như trước khi có tính năng
- AC-07: Đối chiếu tay trên Admin local: cùng bộ lọc, KPI và bảng SKU bằng JSON API; lưu 2 ảnh (lọc 1 SKU, lọc 3 SKU)

INVARIANT PHẢI GIỮ: /reports chỉ Owner; không tự tính lại số liệu tài chính ở frontend, chỉ hiển thị số API trả.

DOCS PHẢI CẬP NHẬT:
- docs/11-ui-screens/ (file màn Báo cáo) — bộ lọc SKU, bảng SKU, hành vi ẩn đối soát
- Báo cáo: bổ sung mục Admin vào docs/report-after-upgrade/loc-bao-cao-theo-sku-fin-a.md

GATE:
- npm run build --prefix src/frontend/admin-dashboard
- Ảnh đối chiếu UI/API lưu trong bàn giao (đường dẫn file)

GIỚI HẠN: Không commit/push. Không thêm dependency. Một lỗi tối đa 2 lần thử rồi BLOCKED.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```

## Prompt FIN-C — Đánh giá dự báo nhập hàng

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-66-FIN-C — Tách công thức dự báo và backtest CS-08   | Mức: STANDARD | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2, commit 879a1ef.

MỤC TIÊU: Công thức gợi ý nhập hàng thành hàm thuần nhận mốc asOf (kết quả API không đổi), có bộ chạy backtest local cho ra WAPE/recall so với ngưỡng đã chốt.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/Pedding/goi-5-plan-trien-khai.md (Q5, Q6, Q6b; Phase B mục B1–B3)
- docs/Upgrade-recommended/Pedding/goi-5-danh-gia-du-bao-nhap-hang.md
- docs/02-functional-requirements/fr-12-ai-forecasting.md
- src/backend/src/OISM.Application/Features/Ai/Services/AiForecastingService.cs
- test-oisms/backend/OISM.UnitTests/AiForecastingTests.cs
- Mẫu project console: test-oisms/ai-eval/

ĐƯỢC SỬA: OISM.Application/Features/Ai/** (thêm ReorderForecastCalculator), AiForecastingTests.cs (thêm test), thư mục mới test-oisms/forecast-eval/, docs dưới.
KHÔNG ĐƯỢC SỬA: contract API dự báo, schema, OISM.sln (project eval không đưa vào solution nếu làm CI chậm; ghi rõ lựa chọn).

ACCEPTANCE CRITERIA:
- AC-01 (B1): ReorderForecastCalculator thuần (không DbContext, không DateTime.Now); AiForecastingService gọi với asOf = ReportTimeHelper.Today(); hằng FormulaVersion = "reorder-v1"
- AC-02 (B1): AiForecastingTests cũ PASS không sửa assertion; thêm test so kết quả service với calculator trên cùng dữ liệu
- AC-03 (B2): forecast-eval từ chối chạy khi host DB không phải localhost/127.0.0.1; chỉ đọc
- AC-04 (B2): Script sinh tenant FORECAST-EVAL 90 ngày, mẫu nhu cầu đều/theo tuần/gián đoạn/có ngày hết hàng, seed cố định; chạy 2 lần cho cùng dữ liệu
- AC-05 (B2, Q6): Mốc t mỗi 7 ngày, chỉ dùng giao dịch < t; thực tế trong [t, t+H); WAPE cho velocity × H, recall cho cờ cần nhập; chỉ tính đạt/không cho SKU ≥ 10 ngày bán/30 ngày; kỳ hết hàng báo riêng
- AC-06 (B2): Xuất results.json + report.md (FormulaVersion, commit, tập dữ liệu, tham số, kết quả theo nhóm so ngưỡng, top 10 sai số)
- AC-07 (Q6b): Không đạt ngưỡng thì ghi CHƯA ĐẠT, không chỉnh công thức

INVARIANT PHẢI GIỮ: tenant chỉ từ JWT trong service; bộ eval không ghi vào DB demo/AWS; số lượng quy về đơn vị gốc.

DOCS PHẢI CẬP NHẬT:
- docs/02-functional-requirements/fr-12-ai-forecasting.md — FormulaVersion, thước đo, ngưỡng, kết quả
- docs/Upgrade-recommended/Pedding/goi-5-danh-gia-du-bao-nhap-hang.md — tick tiêu chí có bằng chứng
- Báo cáo: docs/report-after-upgrade/danh-gia-du-bao-nhap-hang-cs08.md — lệnh chạy, kết quả, ĐẠT/CHƯA ĐẠT, giới hạn (dữ liệu tổng hợp)

GATE:
- dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal
- dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~AiForecasting"
- Chạy forecast-eval trên PostgreSQL local, đính kèm report.md

GIỚI HẠN: Không commit/push. Không thêm dependency ngoài Npgsql (đã có). Không commit file dump. Một lỗi tối đa 2 lần thử rồi BLOCKED.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```

## Prompt FIN-D — Đo hiệu năng và index

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-66-FIN-D — Đo NFR-PERF-01, NFR-PERF-02, NFR-TENANT-02 (local)   | Mức: STANDARD | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2 sau khi FIN-A VERIFIED.

MỤC TIÊU: Có script tái lập được và kết quả thô cho ba ngưỡng độ trễ của đề, kịch bản 50 request tranh 1 đơn vị và kế hoạch truy vấn dùng index.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/Pedding/goi-5-plan-trien-khai.md (Q7, Q8; Phase C dòng NFR-PERF-01, NFR-PERF-02, NFR-TENANT-02)
- docs/Upgrade-recommended/Pedding/goi-5-kiem-chung-nfr.md
- docs/03-non-functional-requirements/01-performance-and-load.md, 02-security-and-isolation.md
- test-oisms/api-python/seed_revenue_perf.py (dữ liệu cũ, chậm)
- test-oisms/backend/OISM.IntegrationTests/OnlineOrderLifecycleIntegrationTests.cs (AC06_ConcurrencyStressTest_50CreateOrders_CompetingForOneUnit_ExactlyOneWins)

ĐƯỢC SỬA: thư mục mới test-oisms/nfr/, docs/06-testing-and-qa/evidence/goi-5/{NFR-PERF-01,NFR-PERF-02,NFR-TENANT-02}/, docs dưới.
KHÔNG ĐƯỢC SỬA: code backend/frontend, schema, DB local đang dùng hằng ngày (đo trên DB riêng).

ACCEPTANCE CRITERIA:
- AC-01 (Q7): Script SQL bulk ~100.000 dòng order_items + ledger tương ứng cho tenant PERF trong DB local dành riêng; ghi rõ dữ liệu không đi qua API; script từ chối host khác localhost
- AC-02: test-oisms/nfr/measure-latency.mjs (Node, không thư viện ngoài): đăng nhập, gọi N lần với đồng thời 1 và 10, xuất p50/p95/p99
- AC-03: Đo /api/pos/search (từ khóa, barcode), POS checkout, tạo đơn Admin (mỗi lần RequestKey mới), revenue-profit không lọc / 1 SKU / 20 SKU; so ngưỡng p95 < 200 ms, < 500 ms, < 2 s
- AC-04: Chạy lại test AC06 50 request trên PostgreSQL local, lưu log và truy vấn kiểm available = 0, 1 thành công, 49 lỗi 409
- AC-05: EXPLAIN (ANALYZE, BUFFERS) cho ledger theo SKU/chi nhánh, danh sách đơn theo tenant + ngày, báo cáo lọc SKU (SQL lấy từ log EF); ghi kích thước bảng và index được dùng
- AC-06: Mỗi kết quả ghi commit, cấu hình máy, dữ liệu, lệnh, kết quả thô, trạng thái ĐẠT / CHƯA ĐẠT / CHƯA ĐO TRÊN DEPLOY

INVARIANT PHẢI GIỮ: không ghi dữ liệu thử vào DB demo hay AWS; không docker compose down -v.

DOCS PHẢI CẬP NHẬT:
- docs/03-non-functional-requirements/README.md, 01-performance-and-load.md, 02-security-and-isolation.md — cột bằng chứng trỏ evidence/goi-5/, môi trường local
- docs/Upgrade-recommended/Pedding/goi-5-kiem-chung-nfr.md — tick tiêu chí có bằng chứng
- test-oisms/nfr/README.md — cách chạy

GATE:
- Chạy đủ script, đính kèm đường dẫn file kết quả thô
- dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~AC06_ConcurrencyStressTest_50CreateOrders"

GIỚI HẠN: Không commit/push. Không thêm dependency. Không chỉnh code để số đo đẹp hơn; số chưa đạt ghi CHƯA ĐẠT. Một lỗi tối đa 2 lần thử rồi BLOCKED.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```

## Prompt FIN-E — TLS 1.3, health, uptime, thao tác POS

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-66-FIN-E — NFR-SEC-01, NFR-USA-01, NFR-USA-02 và BF-026   | Mức: STANDARD | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2, commit 879a1ef.

MỤC TIÊU: Caddy chỉ nhận TLS 1.3 và không đưa /health/ready ra Internet; có script uptime dùng lại được; có bằng chứng thanh toán POS bằng quét + Enter / dưới 3 lần bấm ở 3 kích thước màn hình.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/Pedding/goi-5-plan-trien-khai.md (Q9, Q10, Q13; Phase C dòng NFR-SEC-01, NFR-USA-01, NFR-USA-02)
- docs/bug-fixaffter/errors/BF-026-health-ready-lo-thong-tin-db.md (chọn phương án Caddy chỉ cho /health/live ra ngoài)
- docker/caddy/Caddyfile, docker-compose.prod.yml
- src/frontend/pos-app/src/pages/PosPage.tsx (phím tắt), test-oisms/frontend/pos-app/test-live.mjs

ĐƯỢC SỬA: docker/caddy/Caddyfile, thư mục mới test-oisms/nfr/uptime-probe/, test-oisms/frontend/pos-app/test-live.mjs (thêm kịch bản đếm bước), docs/06-testing-and-qa/evidence/goi-5/{NFR-SEC-01,NFR-USA-01,NFR-USA-02}/, docs dưới.
KHÔNG ĐƯỢC SỬA: backend (Program.cs giữ nguyên /health/ready để healthcheck nội bộ), code POS (nếu cần sửa để đạt NFR-USA-02 thì dừng và báo).

ACCEPTANCE CRITERIA:
- AC-01 (Q9): Mọi site trong Caddyfile có tls { protocols tls1.3 }
- AC-02: Chạy docker-compose.prod.yml local với project name riêng, ADMIN_DOMAIN=admin.localhost, POS_DOMAIN=pos.localhost, SIM_DOMAIN=sim.localhost; openssl s_client -tls1_3 thành công, -tls1_2 bị từ chối; lưu output
- AC-03 (BF-026): Qua Caddy /health/live trả 200, /health/ready không trả nội dung backend (404/403); healthcheck trong container vẫn chạy
- AC-04 (Q10): uptime-probe gọi /health/live và trang POS theo chu kỳ, chỉ tính khung 07:00–22:00 Asia/Saigon, xuất CSV + tổng hợp %; chạy thử local ≥ 1 giờ
- AC-05 (Q13): Ghi đường thao tác quét barcode + Enter → F4 → xác nhận, đếm lần bấm ở 1366×768, 768×1024, 375×812; kịch bản test:live khẳng định số bước; ảnh từng kích thước
- AC-06: Trạng thái ghi đúng: TLS ĐẠT local / CHƯA ĐO TRÊN DEPLOY; uptime CHƯA ĐO TRÊN DEPLOY; NFR-USA-02 ĐẠT phần giả lập, thiết bị cầm tay thật chưa kiểm

INVARIANT PHẢI GIỮ: không đụng volume DB local đang dùng; không deploy lên AWS.

DOCS PHẢI CẬP NHẬT:
- docs/03-non-functional-requirements/02-security-and-isolation.md, 04-usability-and-pwa.md, README.md — bằng chứng, môi trường
- docs/13-deploy-aws/ (file quy trình deploy) — chính sách chỉ TLS 1.3, /health/ready không public, ghi chưa áp lên bản demo
- docs/bug-fixaffter/errors/BF-026-health-ready-lo-thong-tin-db.md — cách sửa, kiểm chứng
- Báo cáo: docs/report-after-upgrade/tls-uptime-pos-nfr-fin-e.md

GATE:
- Output openssl, curl /health/live và /health/ready qua Caddy local
- npm run test:live --prefix src/frontend/pos-app (với docker/pos-tests/compose.yml)

GIỚI HẠN: Không commit/push. Không thêm dependency. Một lỗi tối đa 2 lần thử rồi BLOCKED.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```

## Prompt FIN-F — Coverage lõi

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-66-FIN-F — Đo coverage NFR-MAINT-02   | Mức: STANDARD | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2 sau khi FIN-A và FIN-C VERIFIED.

MỤC TIÊU: Có số line coverage unit + integration (gồm nhóm Postgres*) theo từng nhóm lớp lõi; nhóm nào < 80% thì bổ sung test đến khi đạt.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/Pedding/goi-5-plan-trien-khai.md (Q11; Phase C dòng NFR-MAINT-02)
- docs/03-non-functional-requirements/05-maintainability-testing.md
- test-oisms/backend/OISM.IntegrationTests/PostgresTestEnvironment.cs

ĐƯỢC SỬA: test-oisms/backend/** (chỉ thêm test), test-oisms/nfr/coverage/ (script), docs/06-testing-and-qa/evidence/goi-5/NFR-MAINT-02/, docs dưới.
KHÔNG ĐƯỢC SỬA: code sản phẩm trong src/ (thấy lỗi thật thì dừng và báo, không tự sửa).

ACCEPTANCE CRITERIA:
- AC-01: dotnet test --collect:"XPlat Code Coverage" cho cả hai project với OISM_TEST_* trỏ PostgreSQL local; gộp bằng reportgenerator (hỏi người dùng trước khi cài dotnet tool)
- AC-02 (Q11): Báo line coverage riêng cho: nhóm ghi sổ (OrderService, GoodsReceiptService, SalesReturnService), ReservationService, WacCalculator, RevenueProfitCalculationHelper
- AC-03: Nhóm < 80% có test bổ sung nhắm đúng nhánh thiếu; không đổi phạm vi đo để đạt
- AC-04: Lưu HTML/summary, lệnh, commit, số test chạy, trạng thái ĐẠT/CHƯA ĐẠT từng nhóm

DOCS PHẢI CẬP NHẬT:
- docs/03-non-functional-requirements/05-maintainability-testing.md — số mới, cách đo
- docs/Upgrade-recommended/Pedding/goi-5-kiem-chung-nfr.md — tick coverage

GATE:
- Lệnh coverage đầy đủ (unit + integration PostgreSQL), toàn bộ test PASS

GIỚI HẠN: Không commit/push. Không nới assertion, không skip test, không loại file khỏi phạm vi đo. Một lỗi tối đa 2 lần thử rồi BLOCKED.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```

## Prompt FIN-G — Hồi quy POS BF-019, BF-020

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-66-FIN-G — Test hồi quy POS BF-019 và BF-020   | Mức: STANDARD | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2, commit 879a1ef.

MỤC TIÊU: Có test tự động chứng minh POS không coi HTTP 200 sai contract là thành công (BF-019) và phản hồi checkout muộn không xóa giỏ/ghi đè hóa đơn của ngữ cảnh mới (BF-020); nếu BF-020 còn lỗi thì sửa.

NGUỒN CẦN ĐỌC:
- docs/bug-fixaffter/errors/BF-019-pos-nhan-http-200-sai-contract-la.md (Điều kiện đóng)
- docs/bug-fixaffter/errors/BF-020-pos-rieng-chua-khoa-gio-va-ngu.md (Kịch bản, Điều kiện đóng)
- src/frontend/pos-app/src/services/posApi.ts (checkoutPosOrder), src/frontend/pos-app/src/App.tsx (getPendingCheckout), src/frontend/pos-app/src/components/pos/PaymentModal.tsx
- Mẫu test: test-oisms/frontend/pos-app/test-checkout.mjs, test-support/

ĐƯỢC SỬA: test-oisms/frontend/pos-app/ (test mới, thêm vào script "test" trong src/frontend/pos-app/package.json), src/frontend/pos-app/src/{App.tsx,components/pos/PaymentModal.tsx,services/posApi.ts} chỉ khi test BF-020 FAIL.
KHÔNG ĐƯỢC SỬA: backend, admin-dashboard.

ACCEPTANCE CRITERIA:
- AC-01 (BF-019): HTTP 200 body rỗng / thiếu mã đơn / mã là GUID rỗng → isUnknown, giỏ và requestKey giữ nguyên → test
- AC-02 (BF-020): Checkout A chưa trả → đóng modal → đổi chi nhánh B, thêm hàng → A trả thành công → giỏ B còn nguyên, hóa đơn A không hiện trong ngữ cảnh B → test
- AC-03: Retry cùng requestKey sau kết quả chưa rõ (BF-003) vẫn đúng → test
- AC-04: Nếu AC-02 FAIL trước sửa: ghi FAIL trước → sửa → PASS sau, cùng kịch bản

INVARIANT PHẢI GIỮ: giữ nguyên requestKey khi retry lúc kết quả chưa rõ; không tự sinh mã đơn thay server.

DOCS PHẢI CẬP NHẬT:
- docs/bug-fixaffter/errors/BF-019-…md, BF-020-…md — mục kiểm chứng, tên test
- Nếu sửa code POS: docs/report-after-upgrade/hoi-quy-pos-bf019-bf020.md

GATE:
- npm run build --prefix src/frontend/pos-app
- npm run lint --prefix src/frontend/pos-app
- npm test --prefix src/frontend/pos-app

GIỚI HẠN: Không commit/push. Không thêm dependency. Một lỗi tối đa 2 lần thử rồi BLOCKED. Không nới assertion.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```

## Prompt FIN-H — Biên bản E2E chuyển kho và kiểm kê

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-66-FIN-H — Biên bản E2E chuyển kho 2 bước và kiểm kê   | Mức: STANDARD | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2, commit 879a1ef.

MỤC TIÊU: Có biên bản chạy tay toàn luồng UI chuyển kho và kiểm kê trên Admin local, đối chiếu số tồn và sổ kho sau mỗi bước. Không làm quét barcode (đã quyết định không làm).

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/Pedding/goi-2-quet-ma-vach-kiem-ke.md (phần E2E)
- docs/02-functional-requirements/fr-03-inventory-ledger.md (FR-INV-03, FR-INV-04)
- docs/04-architecture-and-design/04-state-machines.md (chuyển kho, kiểm kê PendingApproval)
- docs/06-testing-and-qa/02-unit-and-integration-test.md
- Màn hình: src/frontend/admin-dashboard/src/pages/{InventoryTransfersPage.tsx,StockCountsPage.tsx}

ĐƯỢC SỬA: docs/06-testing-and-qa/evidence/e2e-kho/ (biên bản + ảnh), docs dưới. Chỉ ghi nhận lỗi, không sửa code.
KHÔNG ĐƯỢC SỬA: src/**, schema, dữ liệu demo AWS.

ACCEPTANCE CRITERIA:
- AC-01: Chuyển kho A→B: tạo phiếu → xuất (A giảm, có dòng OUT) → hàng đang chuyển → B xác nhận nhận (B tăng, có dòng IN); ghi số tồn và dòng sổ trước/sau
- AC-02: Chuyển kho bị hủy trước khi xuất và nhận thiếu (nếu hệ thống hỗ trợ) ghi đúng hành vi
- AC-03: Kiểm kê: tạo phiên → nhập số đếm lệch tăng và lệch giảm → gửi duyệt PendingApproval → Owner duyệt → có bút toán điều chỉnh, tồn khớp số đếm
- AC-04: Kiểm tra quyền: Staff không duyệt được chênh lệch (nếu đặc tả ghi vậy), Cashier không vào màn kho
- AC-05: Mỗi bước có ảnh, giá trị kỳ vọng, giá trị thực tế, PASS/FAIL; lỗi phát hiện ghi thành mục riêng để Claude định tuyến

INVARIANT PHẢI GIỮ: chạy trên Docker local, không docker compose down -v.

DOCS PHẢI CẬP NHẬT:
- docs/06-testing-and-qa/evidence/e2e-kho/README.md — biên bản (commit, môi trường, tài khoản vai trò, kết quả)
- docs/Upgrade-recommended/Pedding/goi-2-quet-ma-vach-kiem-ke.md — tick bằng chứng E2E

GATE:
- Biên bản đủ AC-01..05 với ảnh

GIỚI HẠN: Không commit/push. Không sửa code. Bước FAIL ghi trung thực, không lặp lại để đổi kết quả.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```

## Prompt FIN-I — BF-024 và BF-027

BF-027 đã chốt 21/11/2026: trả `null` khi khác đơn vị gốc. Gửi sau khi FIN-A VERIFIED.

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-66-FIN-I — Sửa BF-024 và BF-027   | Mức: STANDARD | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2 sau khi FIN-A VERIFIED (cùng vùng ReportService).

MỤC TIÊU: Bấm kết quả phiếu nhập ở ô tìm kiếm mở đúng phiếu; API inventory-velocity không trả tổng số lượng cộng khác đơn vị.

NGUỒN CẦN ĐỌC:
- docs/bug-fixaffter/errors/BF-024-tim-kiem-phieu-nhap-khong-mo-dung-phieu.md
- docs/bug-fixaffter/errors/BF-027-tong-so-luong-bao-cao-cong-khac-don-vi.md
- src/frontend/admin-dashboard/src/components/AdminShell.tsx, src/frontend/admin-dashboard/src/pages/InventoryPage.tsx
- src/backend/src/OISM.Application/Features/Reports/Services/ReportService.cs (GetInventoryVelocityReportAsync), Dtos/ReportDtos.cs

ĐƯỢC SỬA: các file trên, test-oisms/backend/OISM.UnitTests/ReportServiceTests.cs, docs dưới.
KHÔNG ĐƯỢC SỬA: GET /api/search backend (trừ khi chứng minh lỗi nằm ở đó, ghi rõ), schema.

ACCEPTANCE CRITERIA:
- AC-01 (BF-024): Tenant có ≥ 2 phiếu nhập; bấm kết quả phiếu thứ hai mở đúng tab và đúng phiếu theo ID; F5 tại URL đó vẫn mở đúng → kiểm trên trình duyệt, lưu ảnh
- AC-02 (BF-027, đã chốt 21/11/2026): giữ các trường total*Quantity; nếu các SKU trong kết quả khác đơn vị gốc thì trả null, cùng một đơn vị gốc thì trả tổng như cũ (kiểu DTO đổi sang decimal? nếu cần) → unit test với 2 SKU khác đơn vị gốc (null) và 2 SKU cùng đơn vị (tổng đúng)
- AC-03: Giao diện Admin dùng quantityByUnit không bị ảnh hưởng
- AC-04: Test ReportServiceTests cũ PASS; assertion về total*Quantity chỉ được đổi ở ca dữ liệu khác đơn vị (thành null), ghi rõ từng assertion đã đổi

DOCS PHẢI CẬP NHẬT:
- docs/05-api-contracts/04-order-endpoints.md (DOC-API-04) — hành vi trường total*Quantity
- docs/bug-fixaffter/errors/BF-024-…md, BF-027-…md — cách sửa, kiểm chứng

GATE:
- dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal
- dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~ReportService"
- npm run build --prefix src/frontend/admin-dashboard

GIỚI HẠN: Không commit/push. Không thêm dependency. Một lỗi tối đa 2 lần thử rồi BLOCKED.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```

## Prompt FIN-J — Tối ưu tải dữ liệu báo cáo revenue-profit

Thêm 21/11/2026 sau review FIN-D: `GET /api/reports/revenue-profit` không truyền ngày mất p95 2,75 s (c=1) và 3,35 s (c=10) trên 50.000 đơn / 100.000 dòng; 20 SKU c=10 mất 3,14 s. EXPLAIN cho thấy SQL dùng index (≤ 35 ms), thời gian nằm ở việc EF dựng toàn bộ entity `Order` + `OrderItem` + `Branch` rồi tính trong C#.

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-66-FIN-J — Tối ưu tải dữ liệu báo cáo revenue-profit   | Mức: STANDARD | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2, commit 879a1ef + thay đổi chưa commit của FIN-A..FIN-I (đã VERIFIED 21/11/2026, gồm các bản fix1). Đã kiểm tra lại: Unit 434/434, Integration PostgreSQL 481/481.

MỤC TIÊU: Giảm độ trễ revenue-profit bằng cách chỉ tải cột cần dùng, KHÔNG đổi bất kỳ con số nào trong kết quả API và KHÔNG đổi nghiệp vụ. Đo lại trước/sau bằng cùng một script.

NGUỒN CẦN ĐỌC:
- src/backend/src/OISM.Application/Features/Reports/Services/RevenueProfitCalculationHelper.cs (QueryRevenueOrdersAsync, QueryConfirmedReturnsAsync hoặc hàm tải phiếu trả tương ứng, AllocateOrderRevenueByItem, AllocateReturnRevenueByItem, CalculateOrderItemCogs, CalculateReturnItemCogs, CalculateReturnCogs, GetOrderBusinessDate, GetReturnBusinessDate)
- src/backend/src/OISM.Application/Features/Reports/Services/ReportService.cs (GetRevenueProfitReportAsync, BuildSkuFilteredRevenueProfitReportAsync)
- src/backend/src/OISM.Application/Features/Reports/Services/DailySalesSummaryService.cs (dùng chung helper, phải cho cùng kết quả)
- test-oisms/nfr/measure-latency.mjs, test-oisms/nfr/README.md, docs/06-testing-and-qa/evidence/goi-5/NFR-PERF-01/
- docs/03-non-functional-requirements/01-performance-and-load.md

ĐƯỢC SỬA: RevenueProfitCalculationHelper.cs, ReportService.cs, DailySalesSummaryService.cs (chỉ phần gọi helper), test trong test-oisms/backend (thêm test, không sửa assertion cũ), test-oisms/nfr/measure-latency.mjs (thêm ca đo), evidence và docs dưới.
KHÔNG ĐƯỢC SỬA: schema, migration, DTO trả về, controller, frontend, công thức phân bổ/làm tròn, quy tắc che giá Staff, bảng daily_sales_summaries và cách dùng nó.

MÔI TRƯỜNG ĐO (đã dùng ở FIN-D-fix1, làm y hệt để số trước/sau so được):
- DB riêng oisms_perf trên PostgreSQL Docker local (đã seed bằng test-oisms/nfr/seed-perf.sql; hiện có thêm 60 đơn Admin Reserved do FIN-D-fix1 tạo, không cần xóa). Không đo trên DB oisms, không đo trên AWS.
- Backend chạy trực tiếp bản Debug ở cổng 55002, trỏ vào oisms_perf bằng user test oisms_test_app (mật khẩu lấy từ OISM_TEST_APP_PASSWORD trong .github/workflows/ci.yml, KHÔNG đọc .env):
  cd src/backend/src/OISM.WebApi && env ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:55002 Database__Host=127.0.0.1 Database__Port=5432 Database__Database=oisms_perf Database__Username=oisms_test_app Database__Password=<OISM_TEST_APP_PASSWORD> Database__SeedDemoData=false Hangfire__Enabled=false Ai__Enabled=false dotnet bin/Debug/net8.0/OISM.WebApi.dll
  Build lại (dotnet build ... -p:UseAppHost=false) và khởi động lại backend giữa lần đo "trước" và "sau". Bỏ request đầu tiên sau khởi động khỏi kết luận nếu nó là ngoại lệ JIT (ghi rõ).
- Script đo có sẵn --only "<nhãn>" để chỉ đo nhóm cần thiết, ví dụ --only "revenue-profit".
- Để so kết quả (AC-03), lưu body JSON của 4 ca revenue-profit với token Owner (tài khoản perf mặc định trong measure-latency.mjs) trước khi sửa vào tmp/fin-j/before-*.json, sau khi sửa vào tmp/fin-j/after-*.json, rồi so bằng một script node nhỏ (chuẩn hóa thứ tự mảng nếu cần). Làm thêm 1 ca bằng tài khoản Staff của tenant PERF nếu có; không có thì ghi NOT RUN và dựa vào AC-05 integration.

CÁCH LÀM (bắt buộc theo hướng này):
1. Đo trước: chạy measure-latency.mjs trên oisms_perf, lưu JSON "before". Thêm ca "revenue-profit 30 ngày gần nhất" (startDate/endDate theo UTC+07 như ReportsPage gửi) bên cạnh ca không lọc, 1 SKU, 20 SKU.
2. Thay Include(o => o.Items) / Include(o => o.Branch) bằng Select sang record nội bộ nhẹ (ví dụ RevenueOrderRow { Id, BranchId, Channel, CreatedAt, TotalAmount, Items: List<RevenueOrderItemRow> } với item chỉ có các cột helper thực sự đọc: SkuId, Quantity, UnitPrice, BaseQuantity, CostPrice (hoặc cột giá vốn snapshot hiện dùng), UnitNameSnapshot, SkuCodeSnapshot, SkuNameSnapshot). Phiếu trả làm tương tự, gồm cột cần cho AllocateReturnRevenueByItem (RefundAmount, OrderItem.UnitPrice, kênh của đơn gốc, ConfirmedAt...).
3. Tên chi nhánh lấy bằng một truy vấn riêng theo danh sách BranchId (Dictionary), không join vào mỗi đơn.
4. Nếu vẫn tải dòng hàng qua navigation thì dùng AsSplitQuery() để tránh nhân bản dòng; giữ AsNoTracking.
5. Giữ nguyên thứ tự dòng hàng trong mỗi đơn như trước (phân bổ dồn phần dư làm tròn phụ thuộc thứ tự/giá trị lớn nhất). Nếu cần, OrderBy theo khóa ổn định đã dùng trước đây; ghi rõ trong bàn giao.
6. Các hàm Allocate*/Calculate* nhận record mới (hoặc overload) nhưng giữ đúng phép tính. Không chuyển phép tính sang SQL GROUP BY.
7. Bộ lọc tenant, trạng thái Confirmed/Completed, branch, channel, skuIds, mốc nửa mở UTC (ToUniversalTime) giữ nguyên.

ACCEPTANCE CRITERIA:
- AC-01: Toàn bộ ReportServiceTests, DailySalesSummary*, PostgresReportSkuFilterIntegrationTests, PostgresCostVisibilityScanIntegrationTests PASS, không sửa assertion cũ.
- AC-02: Thêm 1 integration test PostgreSQL so kết quả revenue-profit (Owner, không lọc và có skuIds, có chiết khấu đơn + phiếu trả khác ngày + 2 chi nhánh) với một snapshot số liệu tính tay; mọi trường tiền khớp tới đồng.
- AC-03: Kết quả /revenue-profit trước và sau trên oisms_perf giống hệt nhau (so JSON, bỏ qua thứ tự mảng nếu thứ tự không thuộc hợp đồng). Lưu diff = rỗng vào evidence.
- AC-04: Đo sau với cùng máy, cùng dữ liệu, c=1 và c=10: ghi p50/p95/p99 trước/sau cho 4 ca (không lọc, 30 ngày, 1 SKU, 20 SKU). Ghi ĐẠT/CHƯA ĐẠT trung thực so ngưỡng 2 s; không đạt thì ghi CHƯA ĐẠT, không đổi ngưỡng.
- AC-05: Staff vẫn nhận null ở mọi trường giá vốn/lãi gộp; Cashier 403.

INVARIANT PHẢI GIỮ: tenant chỉ từ JWT; Staff không thấy giá vốn; không đổi số liệu báo cáo; ngày nghiệp vụ UTC+07, lọc nửa mở.

DOCS PHẢI CẬP NHẬT:
- docs/03-non-functional-requirements/01-performance-and-load.md — bảng trước/sau, ghi rõ ca 30 ngày là cách màn Báo cáo dùng mặc định
- docs/06-testing-and-qa/evidence/goi-5/NFR-PERF-01/<ngày>-FIN-J.md + latency-before.json, latency-after.json, diff kết quả
- docs/06-testing-and-qa/evidence/README.md (thêm dòng), docs/06-testing-and-qa/02-unit-and-integration-test.md (tiểu mục FIN-J)
- docs/report-after-upgrade/loc-bao-cao-theo-sku-fin-a.md — mục "Tối ưu tải dữ liệu (FIN-J)"

GATE:
- dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal
- dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false
- dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~Report|FullyQualifiedName~DailySalesSummary|FullyQualifiedName~CostVisibility"
- node test-oisms/nfr/measure-latency.mjs (trước và sau)

GIỚI HẠN: Không commit/push. Không thêm dependency. Không đo trên AWS. Một lỗi tối đa 2 lần thử rồi BLOCKED. Nếu số liệu trước/sau lệch dù chỉ 1 đồng thì dừng, ghi BLOCKED kèm ca lệch, không "sửa test cho khớp".

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md, file tmp/claude-anti/OISMS-66-FIN/handoff-FIN-J.md.
```
