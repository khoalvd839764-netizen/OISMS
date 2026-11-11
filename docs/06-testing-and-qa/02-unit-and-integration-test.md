# 02. Kế Hoạch Kiểm Thử Đơn Vị & Tích Hợp (Unit & Integration Testing Guide)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-QA-02` |
| **Phiên bản (Version)** | `v1.2.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-11` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `NFR-MAINT-02`, `3.2.e` (Trang 4, 5) |
| **Phạm vi áp dụng** | `test-oisms/backend/OISM.UnitTests` & `test-oisms/backend/OISM.IntegrationTests` |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả phương pháp kiểm thử tự động trên PostgreSQL thật và hướng dẫn đo độ phủ mã nguồn (`Code Coverage`). | `INIT-QA-02` |
| `v1.1.0` | `2026-11-06` | Nhóm dự án OISMS | Cập nhật số lượng test theo hiện trạng (unit 221/221 PASS, integration 364/365 ở lần chạy gần nhất), biến `OISM_TEST_*`, ghi rõ số coverage là mốc Sprint 2. | `OISMS-46` |
| `v1.2.0` | `2026-11-11` | Nhóm dự án OISMS | Cập nhật quy mô test sau CS-01, CS-05, Trợ lý AI và gộp POS: unit 380/380 PASS, POS 89/89 PASS (chạy 11/11/2026); integration chưa chạy lại. Thêm test app giả lập và Admin. | `OISMS-58` |

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
