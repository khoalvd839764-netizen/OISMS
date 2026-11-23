# 02. Kế Hoạch Kiểm Thử Đơn Vị & Tích Hợp (Unit & Integration Testing Guide)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-QA-02` |
| **Phiên bản (Version)** | `v1.3.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-19` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `NFR-MAINT-02`, `3.2.e` (Trang 4, 5) |
| **Phạm vi áp dụng** | `test-oisms/backend/OISM.UnitTests` & `test-oisms/backend/OISM.IntegrationTests` |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả phương pháp kiểm thử tự động trên PostgreSQL thật và hướng dẫn đo độ phủ mã nguồn (`Code Coverage`). | `INIT-QA-02` |
| `v1.1.0` | `2026-11-06` | Nhóm dự án OISMS | Cập nhật số lượng test theo hiện trạng (unit 221/221 PASS, integration 364/365 ở lần chạy gần nhất), biến `OISM_TEST_*`, ghi rõ số coverage là mốc Sprint 2. | `OISMS-46` |
| `v1.2.0` | `2026-11-11` | Nhóm dự án OISMS | Cập nhật quy mô test sau CS-01, CS-05, Trợ lý AI và gộp POS: unit 380/380 PASS, POS 89/89 PASS (chạy 11/11/2026); integration chưa chạy lại. Thêm test app giả lập và Admin. | `OISMS-58` |
| `v1.3.0` | `2026-11-19` | Nhóm dự án OISMS | Bổ sung bộ test tích hợp PostgreSQL thật `PostgresGoodsIssueIntegrationTests` cho Phiếu xuất kho 02-VT (`goods_issues`, `goods_issue_items`) và cập nhật baseline 50 bảng (`DatabaseBaselineValidatorTests`, `PostgresTenantIsolationTests`). | `OISMS-66-L2-G3` |

---

## 1. Kiến Trúc Bộ Kiểm Thử Tự Động

| Project | Quy mô hiện tại | Phạm vi |
| :--- | :--- | :--- |
| `OISM.UnitTests` (xUnit, Moq, FluentAssertions, EF InMemory) | 243 `[Fact]` + 34 `[Theory]`; lần chạy 11/11/2026: 380/380 PASS | WAC, quy tắc sổ cái, `PhoneHelper`, `SkuAttributeHelper`, validator, quy đổi đơn vị, giá, gợi ý nhập hàng, `DemoSeedPolicy`, trả hàng, tài khoản nhận tiền, Trợ lý AI (tool, router, kho tài liệu), webhook catalog. |
| `OISM.IntegrationTests` (`WebApplicationFactory`, PostgreSQL thật) | 382 `[Fact]` + 5 `[Theory]` (đếm 11/11/2026); lần chạy 07/11/2026: 372/372 PASS trên PostgreSQL (xem [review PR 39](../report-after-upgrade/vietqr-cs05-review-pr39.md)). Chưa chạy lại sau CS-01 và Trợ lý AI | Cô lập tenant, ACID/rollback, `FOR UPDATE` và `pg_advisory_xact_lock`, idempotency, RBAC trên API, migration chạy lại, SignalR. |
| `src/frontend/pos-app` (`node --test`, file test ở `test-oisms/frontend/pos-app/`) | 89/89 PASS (11/11/2026) | Giỏ hàng đa đơn vị (`posCartUnits.ts` của `pos-app`), QR, checkout giữ `requestKey`, phiên đăng nhập, realtime, hóa đơn, service worker. |
| `src/frontend/marketplace-simulator`, `admin-dashboard` (`node --test`) | `test-oisms/frontend/marketplace-simulator/test-payload.mjs`, `test-oisms/frontend/admin-dashboard/test-ai-chat-format.mjs` | Payload webhook giả lập; định dạng câu trả lời Trợ lý AI. |

Mock và EF InMemory không chứng minh được transaction hay concurrency; các thay đổi chạm ledger, WAC, reservation, idempotency, concurrency phải có integration test trên PostgreSQL thật.

---

## 2. Lệnh Thực Thi Kiểm Thử & Đo Độ Phủ (`NFR-MAINT-02`)

Integration test cần các biến `OISM_TEST_HOST`, `OISM_TEST_PORT`, `OISM_TEST_ADMIN_USER`, `OISM_TEST_ADMIN_PASSWORD`, `OISM_TEST_APP_USER`, `OISM_TEST_APP_PASSWORD` (cách đặt xem `.github/workflows/ci.yml` hoặc [DOC-OPS-02](../07-devops-and-deployment/02-ci-cd-github-actions.md)).

```powershell
# Unit test
dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj

# Integration test một nhóm
dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj --filter "FullyQualifiedName~<TenTest>"

# Toàn bộ (như CI)
dotnet test src/backend/OISM.sln --configuration Release

# Chạy kèm thu thập báo cáo Code Coverage (Cobertura)
dotnet test src/backend/OISM.sln --collect:"XPlat Code Coverage"
```

- **Chỉ tiêu đề bài:** Tối thiểu `80%` cho `Inventory Ledger`, `Anti-Oversell Reserve`, `COGS`.
- **Kết quả đo tại Sprint 2 (mốc lịch sử, chưa đo lại sau các gói Upgrade):** **`92.49%` Line Coverage** trong Integration Tests và **`94.83%`** khi kết hợp Unit Tests.

## POS sau khi tách — 08/11/2026

Các số test ở bảng trước là mốc lịch sử. POS chỉ còn một implementation tại `src/frontend/pos-app`; xem [báo cáo kiểm chứng mới](../report-after-upgrade/tach-pos-khoi-admin-2026-11-08.md). Chạy `npm test --prefix src/frontend/pos-app`; test live riêng: `docker compose -f docker/pos-tests/compose.yml up -d --build`, rồi `npm run test:live --prefix src/frontend/pos-app`. Guard kiểm chứng đúng container/database test trước bất kỳ POST nào. Không chạy giao dịch fixture trên cổng demo 5000.

---

## Kết quả kiểm thử bản final (11/2026)

### FIN-A — Lọc báo cáo doanh thu/COGS/lãi gộp theo nhiều SKU (`FR-REP-01`)

- **Test mới:** 3 unit test trong `ReportServiceTests` (giới hạn 20 SKU & loại trùng, phân bổ chiết khấu cấp đơn + trả hàng khác ngày + `skuBreakdown`, che giá vốn `Staff`) và 4 integration test PostgreSQL trong `PostgresReportSkuFilterIntegrationTests` (400 `VALIDATION_ERROR`, SKU không tồn tại/tenant khác trả 0, `Staff` null / `Cashier` 403, lọc kết hợp + ranh giới UTC+07 nửa mở) + route `skuIds` trong `PostgresCostVisibilityScanIntegrationTests`.
- **Lệnh & Kết quả:**
  - `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false` → **PASS 431/431**
  - `dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~Report|FullyQualifiedName~DailySalesSummary|FullyQualifiedName~CostVisibility"` → **PASS 20/20** (PostgreSQL thật)
- **Bằng chứng:** [`evidence/FR-REP-01/2026-11-21-FIN-A.md`](./evidence/FR-REP-01/2026-11-21-FIN-A.md)

### FIN-C — Tách công thức dự báo & Backtest `CS-08` (`FR-AI-01`)

- **Test mới:** `GetReorderRecommendationsAsync_MatchesPureCalculator_OnSameDataset` trong `AiForecastingTests` đối chiếu kết quả service với hàm thuần `ReorderForecastCalculator.Calculate` (`FormulaVersion = "reorder-v1"`), cộng thêm bộ `test-oisms/forecast-eval/` chạy backtest local chỉ đọc trên tenant tổng hợp `FORECAST-EVAL` 90 ngày.
- **Lệnh & Kết quả:**
  - `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~AiForecasting"` → **PASS 9/9**
  - `OISM_EVAL_DATABASE=oisms_forecast_eval dotnet run --project test-oisms/forecast-eval/Oism.ForecastEval.csproj -- --tenant FORECAST-EVAL --commit 879a1ef --out-dir docs/06-testing-and-qa/evidence/FR-AI-01` → **ĐẠT** (`WAPE = 9.19% ≤ 35%` trên nhóm `Eligible`; `Stockout` Recall `100%`)
  - **FIN-C-fix1 (21/11/2026):** recall N/A không còn tính là đạt, seed thêm 2 SKU tồn thấp. Chạy lại → **ĐẠT** nhóm `Eligible`: `WAPE 7.70%`, `Recall 100%` (7/7), precision `87.50%` ([bằng chứng](./evidence/FR-AI-01/2026-11-21-FIN-C-fix1.md))
- **Bằng chứng:** [`evidence/FR-AI-01/2026-11-21-FIN-C.md`](./evidence/FR-AI-01/2026-11-21-FIN-C.md), [`report.md`](./evidence/FR-AI-01/report.md)

### FIN-E — TLS 1.3, `/health/ready` (`BF-026`), `uptime-probe` và thao tác POS (`NFR-SEC-01`, `NFR-USA-01`, `NFR-USA-02`)

- **Kiểm thử & đo local:**
  - `openssl s_client -tls1_3` / `-tls1_2` & `curl /health/live` (`200`) / `/health/ready` (`403`) qua `caddy:2.10-alpine` local (`admin.localhost:8443`).
  - `node test-oisms/nfr/uptime-probe/uptime-probe.mjs --interval-sec 2 --max-probes 5` → 5/5 OK trong khung 07:00–22:00 UTC+07.
  - `npm run test:live --prefix src/frontend/pos-app` (với `docker/pos-tests/compose.yml`) → **PASS 8/8** (gồm check mới `NFR-USA-02` 2 bước < 3).
- **Bằng chứng:** [`evidence/goi-5/NFR-SEC-01/2026-11-21-FIN-E.md`](./evidence/goi-5/NFR-SEC-01/2026-11-21-FIN-E.md), [`NFR-USA-01`](./evidence/goi-5/NFR-USA-01/2026-11-21-FIN-E.md), [`NFR-USA-02`](./evidence/goi-5/NFR-USA-02/2026-11-21-FIN-E.md)

### FIN-G — Hồi quy POS `BF-019` và `BF-020` (`FR-POS-03`, `FR-POS-04`)

- **Test mới:** `test-oisms/frontend/pos-app/test-bf019-bf020.mjs` (thêm vào `"test"` trong `src/frontend/pos-app/package.json`). Trước khi sửa `PaymentModal.tsx`, ca `BF-020` FAIL (`1 !== 0`); sau khi thêm `contextSignatureRef` chặn phản hồi muộn sai ngữ cảnh, toàn bộ PASS.
- **Lệnh & Kết quả:**
  - `npm run build --prefix src/frontend/pos-app` → Thành công
  - `npm run lint --prefix src/frontend/pos-app` → 0 lỗi
  - `npm test --prefix src/frontend/pos-app` → **PASS 101/101**
- **Bằng chứng:** [`evidence/FR-POS-03/2026-11-21-FIN-G.md`](./evidence/FR-POS-03/2026-11-21-FIN-G.md), [`FR-POS-04`](./evidence/FR-POS-04/2026-11-21-FIN-G.md)

### FIN-B — Bộ chọn SKU ở tab P&L màn Báo cáo Admin (`FR-REP-01`)

- **Kiểm chứng:** `npm run build --prefix src/frontend/admin-dashboard` thành công (`0` lỗi); đối chiếu trực tiếp trên `http://localhost:5173/admin/reports` (1 SKU và 2/2 SKU hiện có của Shop A, kèm F5 giữ `?skus=` và ẩn khối đối soát ngày) khớp 100% với JSON API `GET /api/reports/revenue-profit`.
- **Bằng chứng:** [`evidence/FR-REP-01/2026-11-21-FIN-B.md`](./evidence/FR-REP-01/2026-11-21-FIN-B.md)

### FIN-I — Sửa `BF-024` (tìm kiếm phiếu nhập) và `BF-027` (`total*Quantity` trả `null` khi khác đơn vị)

- **Test mới (`BF-027`):** `GetInventoryVelocityReport_DifferentBaseUnits_ReturnsNullTotals_BF027` và `GetInventoryVelocityReport_SameBaseUnit_ReturnsSummedTotals_BF027` trong `ReportServiceTests` (không sửa bất kỳ assertion cũ nào).
- **Lệnh & Kết quả:**
  - `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~ReportService"` → **PASS 19/19**
  - `npm run build --prefix src/frontend/admin-dashboard` → Thành công (`0` lỗi)
  - Kiểm tra trình duyệt `BF-024` trên `http://localhost:5173` → **PASS** (bấm phiếu thứ 2 mở đúng phiếu theo ID, F5 giữ nguyên).
- **Bằng chứng:** [`evidence/FR-REP-02/2026-11-21-FIN-I.md`](./evidence/FR-REP-02/2026-11-21-FIN-I.md), [`evidence/CS-04/2026-11-21-FIN-I.md`](./evidence/CS-04/2026-11-21-FIN-I.md)

### FIN-F — Đo lại độ phủ kiểm thử tự động bản Final (`NFR-MAINT-02`)

- **Lệnh & Kết quả:**
  - `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false --no-restore --collect:"XPlat Code Coverage" --results-directory tmp/coverage-unit` → **PASS 434/434**
  - `dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --no-restore --collect:"XPlat Code Coverage" --results-directory tmp/coverage-int` → **PASS 480/480** (PostgreSQL 16 thật)
  - `python3 test-oisms/nfr/coverage/merge-coverage.py docs/06-testing-and-qa/evidence/goi-5/NFR-MAINT-02/coverage-summary.json` → **ĐẠT `>= 80%`** trên cả 4 nhóm cốt lõi: Ghi sổ `90.90%` (`1787/1966`), `ReservationService` `92.51%` (`346/374`), `WacCalculator` `100.00%` (`16/16`), `RevenueProfitCalculationHelper` `89.35%` (`151/169`).
- **Bằng chứng:** [`evidence/goi-5/NFR-MAINT-02/2026-11-21-FIN-F.md`](./evidence/goi-5/NFR-MAINT-02/2026-11-21-FIN-F.md), [`coverage-summary.json`](./evidence/goi-5/NFR-MAINT-02/coverage-summary.json)

### FIN-J (`NFR-PERF-01` — Tối ưu tải dữ liệu `revenue-profit`)

- **Test mới:** `RevenueProfit_MatchesHandCalculatedSnapshot_AcrossBranchesDiscountAndReturn_FinJ_AC02` trong `PostgresReportSkuFilterIntegrationTests.cs` (đối chiếu số liệu tính tay: 2 chi nhánh, chiết khấu cấp đơn, phiếu trả khác ngày bán, không lọc và lọc `skuIds`).
- **Lệnh:**
  - `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false --no-build` → **PASS 434/434**
  - `dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --no-build --filter "FullyQualifiedName~Report|FullyQualifiedName~DailySalesSummary|FullyQualifiedName~CostVisibility"` → **PASS 21/21** (PostgreSQL 16 local)
- **Bằng chứng:** [`evidence/goi-5/NFR-PERF-01/2026-11-21-FIN-J.md`](./evidence/goi-5/NFR-PERF-01/2026-11-21-FIN-J.md)
