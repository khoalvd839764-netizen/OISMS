# KẾT QUẢ KIỂM THỬ TỔNG HỢP THEO ĐỀ BÀI OISMS

| Thông tin | Chi tiết |
| --- | --- |
| **Mã tài liệu** | `DOC-TEST-RESULT-FINAL` |
| **Phiên bản** | `v1.0.0` |
| **Ngày chốt số liệu** | 21/11/2026 (Asia/Saigon) |
| **Phiên bản code** | Nhánh `feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2`, commit `bcb4f9b` (code) và `df99b9b` (docs), PR #52 |
| **Phạm vi** | Gom số liệu kiểm thử ứng với 30 FR + 1 mục dự báo, 11 NFR của đề bài. Bằng chứng gốc ở [`evidence/`](./evidence/README.md) |

Tài liệu này chỉ tổng hợp. Mỗi con số đều có link tới file bằng chứng gốc (lệnh chạy, máy, dữ liệu, kết quả thô). Khi chạy lại, thêm dòng vào [sổ bằng chứng](./evidence/README.md) rồi cập nhật bảng tương ứng ở đây.

## 1. Môi trường kiểm thử

| Hạng mục | Giá trị |
| --- | --- |
| Máy đo | Linux 7.0.0-43-generic x86_64, AMD Ryzen 7 H 255 (16 vCPU), RAM 22 GiB |
| Backend | .NET 8, EF Core 8 + Npgsql |
| CSDL | PostgreSQL 16 (Docker `postgres:16-alpine`, `/dev/shm` 256 MB) |
| Dữ liệu đo hiệu năng | DB riêng `oisms_perf`, tenant `PERF`: 50.000 đơn, 100.000 dòng hàng, 100.025 bút toán sổ kho, 25 SKU (sinh bằng `test-oisms/nfr/seed-perf.sql`) |
| Dữ liệu đánh giá dự báo | DB riêng `oisms_forecast_eval`, tenant `FORECAST-EVAL`: 8 SKU × 90 ngày dữ liệu tổng hợp |
| Phạm vi | **Mọi phép đo chạy local** (chốt Gói 5). Chưa đo trên bản demo AWS |

## 2. Bộ kiểm thử tự động (lần chạy cuối 21/11/2026)

| Bộ test | Môi trường | Kết quả |
| --- | --- | --- |
| Backend Unit (`OISM.UnitTests`) | EF Core InMemory | **PASS 434/434** |
| Backend Integration (`OISM.IntegrationTests`) | PostgreSQL 16 thật, DB cô lập mỗi lần chạy | **PASS 481/481** |
| POS (`npm test`, `pos-app`) | Node VM chạy component thật | **PASS 101/101** |
| POS live (`npm run test:live`) | Docker `docker/pos-tests/compose.yml` | **PASS 8/8** |
| App giả lập sàn (`npm test`) | Node | **PASS 9/9** |
| Build Admin, POS, giả lập sàn | Vite | **PASS** |
| Lint POS | oxlint | **0 lỗi** |

Lệnh chạy: xem [mục 7](#7-cách-chạy-lại).

## 3. Kết quả theo yêu cầu phi chức năng (11 NFR)

| Mã | Yêu cầu của đề | Kết quả đo | Kết luận | Bằng chứng |
| --- | --- | --- | --- | --- |
| `NFR-PERF-01` | Tìm SKU/quét barcode p95 < 200 ms; tạo đơn/ghi < 500 ms; báo cáo < 2 s với < 100.000 bản ghi | Tìm SKU p95 ≤ 25 ms, barcode ≤ 10 ms; tạo đơn Admin ≤ 231 ms; checkout POS 10 quầy khác SKU ≤ 58 ms; báo cáo 8/8 ca ≤ 1,2 s (chi tiết [mục 4](#4-chi-tiết-nfr-perf-01)) | **ĐẠT local** (giới hạn: 10 quầy cùng 1 SKU) | [shm256](./evidence/goi-5/NFR-PERF-01/2026-11-21-shm256.md), [checkout nhiều SKU](./evidence/goi-5/NFR-PERF-01/2026-11-21-checkout-multisku.md) |
| `NFR-PERF-02` | 50 đơn đồng thời tranh 1 đơn vị tồn cuối: đúng 1 thành công | `AC06_ConcurrencyStressTest_50CreateOrders_CompetingForOneUnit_ExactlyOneWins`: 1 thành công, 49 nhận `409`, tồn khả dụng còn 0 | **ĐẠT** | [FIN-D](./evidence/goi-5/NFR-PERF-02/2026-11-21-FIN-D.md), [kịch bản](./03-concurrency-stress-test.md) |
| `NFR-TENANT-01` | Cô lập dữ liệu giữa tenant | `PostgresTenantIsolationTests` và test quét quyền nằm trong bộ 481/481 | **ĐẠT** | [Test cô lập](../../test-oisms/backend/OISM.IntegrationTests/PostgresTenantIsolationTests.cs) |
| `NFR-TENANT-02` | Truy vấn dùng chỉ mục theo tenant | `EXPLAIN ANALYZE` trên 100.000 dòng: sổ kho 0,131 ms (`ix_inventory_transactions_sku_history`), đơn theo ngày 0,161 ms (`ix_orders_tenant_created_at`), lọc SKU 34,9 ms (`ix_order_items_sku_orders`) | **ĐẠT local** | [FIN-D](./evidence/goi-5/NFR-TENANT-02/2026-11-21-FIN-D.md), [output thô](./evidence/goi-5/NFR-TENANT-02/explain-analyze.txt) |
| `NFR-SEC-01` | BCrypt, HTTPS/TLS 1.3, JWT 60 phút + refresh token | Caddy local: `openssl -tls1_3` thành công (`TLS_AES_128_GCM_SHA256`), `-tls1_2` bị từ chối; `/health/ready` qua proxy trả 403 | **ĐẠT local**, chưa đo trên bản demo | [FIN-E](./evidence/goi-5/NFR-SEC-01/2026-11-21-FIN-E.md) |
| `NFR-SEC-02` | Giao dịch nguyên tử | Test rollback giữa giao dịch trên PostgreSQL nằm trong bộ 481/481 | **ĐẠT** | [Test PostgreSQL](../../test-oisms/backend/OISM.IntegrationTests/PostgresOrderLifecycleAtomicityTests.cs) |
| `NFR-SEC-03` | Sổ cái chỉ ghi thêm | Trigger `ledger_append_only` chặn `UPDATE`, `DELETE`, `TRUNCATE`; `/health/ready` kiểm tra trigger | **ĐẠT** | [Schema](../database/oisms_schema.sql), [Test kiểm kê](../../test-oisms/backend/OISM.IntegrationTests/StockCountIntegrationTests.cs) |
| `NFR-USA-01` | Uptime 99,5% khung 07:00–22:00 | Có script `test-oisms/nfr/uptime-probe/`; mới chạy thử 5/5 probe local | **CHƯA ĐO** trên bản demo | [FIN-E](./evidence/goi-5/NFR-USA-01/2026-11-21-FIN-E.md) |
| `NFR-USA-02` | Thanh toán POS < 3 thao tác; đáp ứng desktop, tablet, máy cầm tay | `test:live`: quét mã + Enter → F4 → xác nhận, 2 bước, PASS 8/8 | **ĐẠT phần thao tác**; chưa chụp 3 kích thước, chưa thử máy cầm tay | [FIN-E](./evidence/goi-5/NFR-USA-02/2026-11-21-FIN-E.md) |
| `NFR-MAINT-01` | Clean Architecture | `ArchitectureRulesTests` khóa quy tắc phụ thuộc giữa 4 tầng, 5/5 PASS (trong bộ 481/481) | **ĐẠT** | [ArchitectureRulesTests](../../test-oisms/backend/OISM.IntegrationTests/ArchitectureRulesTests.cs) |
| `NFR-MAINT-02` | Coverage ≥ 80% cho Ledger, Reserve, COGS | Ledger 90,90%, Reserve 92,51%, WAC 100%, doanh thu/giá vốn/lãi 89,35% (Unit + Integration PostgreSQL) | **ĐẠT** | [FIN-F](./evidence/goi-5/NFR-MAINT-02/2026-11-21-FIN-F.md), [coverage-summary.json](./evidence/goi-5/NFR-MAINT-02/coverage-summary.json) |

**Tổng:** 8/11 ĐẠT; `NFR-SEC-01`, `NFR-USA-01`, `NFR-USA-02` còn thiếu phần đo trên bản demo hoặc thiết bị thật.

## 4. Chi tiết `NFR-PERF-01`

Đo bằng `test-oisms/nfr/measure-latency.mjs` trên `oisms_perf` (100.000 dòng hàng), sau tối ưu báo cáo (FIN-J) và tăng `/dev/shm` PostgreSQL. `c` là số request đồng thời.

| Thao tác | Ngưỡng | c=1 p95 | c=10 p95 | Kết quả |
| --- | --- | ---: | ---: | --- |
| `GET /api/pos/search` (tìm SKU) | < 200 ms | 16,30 ms | 24,92 ms | **ĐẠT** |
| `GET /api/pos/barcode/{code}` (quét mã) | < 200 ms | 6,28 ms | 10,05 ms | **ĐẠT** |
| `POST /api/orders` (tạo đơn Admin) | < 500 ms | 41,73 ms | 230,94 ms | **ĐẠT** |
| `POST /api/pos/checkout`, các quầy bán SKU khác nhau | < 500 ms | 38,74–49,59 ms | 53,08–58,35 ms | **ĐẠT** |
| `POST /api/pos/checkout`, mọi quầy cùng bán 1 SKU | < 500 ms | 42,72–49,51 ms | 379,00–577,48 ms | Giới hạn đã biết (xem dưới) |
| `revenue-profit` không lọc | < 2 s | 662,01 ms | 1.090,50 ms | **ĐẠT** |
| `revenue-profit` 30 ngày (mặc định màn Báo cáo) | < 2 s | 406,96 ms | 1.075,74 ms | **ĐẠT** |
| `revenue-profit` 1 SKU | < 2 s | 56,82 ms | 133,83 ms | **ĐẠT** |
| `revenue-profit` 20 SKU | < 2 s | 361,58 ms | 1.160,97 ms | **ĐẠT** |

**Trước và sau tối ưu báo cáo (FIN-J):** p95 báo cáo không lọc giảm từ 2.876 ms xuống 420 ms (c=1) và từ 3.538 ms xuống 863 ms (c=10). Kết quả JSON trước và sau giống hệt nhau ([FIN-J](./evidence/goi-5/NFR-PERF-01/2026-11-21-FIN-J.md)).

**Giới hạn đã biết:** 10 quầy cùng bán đúng 1 SKU tại cùng thời điểm phải xếp hàng qua khóa tồn kho (`pg_advisory_xact_lock` + `FOR UPDATE`), đây là thiết kế chống bán âm theo `NFR-PERF-02`. Mỗi giao dịch ~40 ms nên p50 ~357 ms; p95 dao động 379–577 ms giữa hai lượt đo.

**Sự cố đã xử lý khi đo:** container PostgreSQL mặc định chỉ có 64 MB `/dev/shm`; 10 truy vấn song song cùng lúc báo `could not resize shared memory segment`, EF tự chạy lại sau 1–4 s làm p95 lên ~5 s. Đã thêm `shm_size: 256mb` vào `docker-compose.yml` ([review](./evidence/goi-5/NFR-PERF-01/2026-11-21-FIN-J-review.md)).

## 5. Kết quả theo yêu cầu chức năng

### 5.1. Các mục được kiểm thử bổ sung trước bản final

| Mã | Nội dung kiểm thử | Kết quả | Bằng chứng |
| --- | --- | --- | --- |
| `FR-REP-01` | Lọc báo cáo theo tối đa 20 SKU, `skuBreakdown`, phân bổ chiết khấu và hàng trả theo dòng, Staff nhận `null` giá vốn/lãi, Cashier 403, mốc UTC+7 nửa mở | Unit 3 test mới; Integration PostgreSQL 5 test (`PostgresReportSkuFilterIntegrationTests`, gồm so khớp số liệu tính tay tới từng đồng); đối chiếu giao diện và API khớp 100% | [FIN-A](./evidence/FR-REP-01/2026-11-21-FIN-A.md), [FIN-B](./evidence/FR-REP-01/2026-11-21-FIN-B.md) |
| `FR-REP-02` | Trường `total*Quantity` trả `null` khi khác đơn vị gốc (BF-027) | 2 unit test mới PASS, test cũ giữ nguyên | [FIN-I](./evidence/FR-REP-02/2026-11-21-FIN-I.md) |
| `FR-AI-01` (CS-08) | Backtest công thức `reorder-v1`: mốc mỗi 7 ngày, chân trời 10 ngày | Nhóm đủ dữ liệu: WAPE **7,70%** (ngưỡng ≤ 35%), recall **100%** (7/7, ngưỡng ≥ 80%), precision 87,50% | [FIN-C-fix1](./evidence/FR-AI-01/2026-11-21-FIN-C-fix1.md), [report.md](./evidence/FR-AI-01/report.md) |
| `FR-INV-03` | Chuyển kho 2 bước trên giao diện: Draft → InTransit (OUT −2) → Received (IN +2); hủy phiếu không đổi tồn | PASS, có ảnh và đối chiếu sổ kho | [E2E kho](./evidence/e2e-kho/README.md) |
| `FR-INV-04` | Kiểm kê: lệch +2/−2 → PendingApproval → Owner duyệt → ghi IN/OUT; Staff không duyệt được; Cashier bị chặn | PASS 8/8 bước, 12 ảnh | [E2E kho](./evidence/e2e-kho/README.md) |
| `FR-POS-03`, `FR-POS-04` | Hồi quy BF-019 (HTTP 200 thiếu mã đơn) và BF-020 (phản hồi checkout muộn không xóa giỏ mới), giữ `RequestKey` khi retry | Test tái hiện BF-020 FAIL trước sửa, PASS sau sửa; `npm test` 101/101 | [FIN-G](./evidence/FR-POS-03/2026-11-21-FIN-G.md) |
| CS-04 | Tìm kiếm mở đúng phiếu nhập, F5 giữ phiếu, đóng phiếu không tự mở lại (BF-024) | PASS 4/4 ca trên trình duyệt | [FIN-I-fix1](./evidence/CS-04/2026-11-21-FIN-I-fix1.md) |

### 5.2. Các FR còn lại

Các FR khác (`FR-AUTH`, `FR-PROD`, `FR-INV-01/02`, `FR-COST`, `FR-RSE`, `FR-ORD`, `FR-POS-01/02`, `FR-SIM`, `FR-REP-03`) được bao phủ bởi bộ Unit 434/434 và Integration PostgreSQL 481/481 ở mục 2. Từng mã và file test tương ứng ghi tại [checklist mục 3](../../checklist.md#3-yêu-cầu-chức-năng-functional-requirements) và [RTM](./01-requirements-matrix-rtm.md).

**Không kiểm thử:** `FR-PROD-03` phần quét barcode ở màn kiểm kê. Nhóm quyết định không làm ngày 21/11/2026 vì không có thiết bị quét; phần sinh mã EAN-13/Code128 và quét mã ở POS vẫn có.

## 6. Giới hạn chung

- Mọi số đo hiệu năng, TLS, uptime là **local**; chưa đo trên bản demo `oisms.khoadang.site`.
- Đánh giá dự báo dùng **dữ liệu tổng hợp**, số ca cần nhập trong nhóm đủ dữ liệu còn ít (7 ca); chưa đối chiếu dữ liệu bán thật.
- Coverage đo bằng Coverlet và script gộp `test-oisms/nfr/coverage/merge-coverage.py` (chưa có báo cáo HTML từ `reportgenerator`).
- Chưa có ảnh POS ở 3 kích thước màn hình và chưa thử máy cầm tay (`NFR-USA-02`).

## 7. Cách chạy lại

```bash
# Backend (integration cần PostgreSQL và biến OISM_TEST_*, xem .github/workflows/ci.yml)
dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal
dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false
dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false

# Frontend
npm test --prefix src/frontend/pos-app
npm run test:live --prefix src/frontend/pos-app
npm test --prefix src/frontend/marketplace-simulator

# Đo hiệu năng (backend trỏ vào DB oisms_perf, xem test-oisms/nfr/README.md)
node test-oisms/nfr/measure-latency.mjs --base-url http://127.0.0.1:55002 --n-report 50

# Đánh giá dự báo (xem test-oisms/forecast-eval/README.md)
OISM_EVAL_DATABASE=oisms_forecast_eval dotnet run --project test-oisms/forecast-eval/Oism.ForecastEval.csproj -- --tenant FORECAST-EVAL --out-dir docs/06-testing-and-qa/evidence/FR-AI-01
```
