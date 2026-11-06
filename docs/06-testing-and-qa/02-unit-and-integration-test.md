# 02. Kế Hoạch Kiểm Thử Đơn Vị & Tích Hợp (Unit & Integration Testing Guide)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-QA-02` |
| **Phiên bản (Version)** | `v1.1.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-06` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `NFR-MAINT-02`, `3.2.e` (Trang 4, 5) |
| **Phạm vi áp dụng** | `src/backend/tests/OISM.UnitTests` & `src/backend/tests/OISM.IntegrationTests` |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả phương pháp kiểm thử tự động trên PostgreSQL thật và hướng dẫn đo độ phủ mã nguồn (`Code Coverage`). | `INIT-QA-02` |
| `v1.1.0` | `2026-11-06` | Nhóm dự án OISMS | Cập nhật số lượng test theo hiện trạng (unit 221/221 PASS, integration 364/365 ở lần chạy gần nhất), biến `OISM_TEST_*`, ghi rõ số coverage là mốc Sprint 2. | `OISMS-46` |

---

## 1. Kiến Trúc Bộ Kiểm Thử Tự Động

| Project | Quy mô hiện tại | Phạm vi |
| :--- | :--- | :--- |
| `OISM.UnitTests` (xUnit, Moq, FluentAssertions, EF InMemory) | 149 `[Fact]` + 16 `[Theory]`; lần chạy 06/11/2026: 221/221 PASS | WAC, quy tắc sổ cái, `PhoneHelper`, `SkuAttributeHelper`, validator, quy đổi đơn vị, giá, gợi ý nhập hàng, `DemoSeedPolicy`. |
| `OISM.IntegrationTests` (`WebApplicationFactory`, PostgreSQL thật) | 357 `[Fact]` + 2 `[Theory]`; lần chạy gần nhất 364/365 (1 FAIL do CRLF trên Windows, xem [báo cáo](../report-after-upgrade/frontend-coming-soon-va-upgrade.md#lệnh-kiểm-chứng)) | Cô lập tenant, ACID/rollback, `FOR UPDATE` và `pg_advisory_xact_lock`, idempotency, RBAC trên API, migration chạy lại, SignalR. |
| `src/frontend/pos-app` (`node --test`) | 63/63 | Giỏ hàng đa đơn vị (cả hai bản `posCartUnits.ts`), QR, môi trường build, luồng POS. |

Mock và EF InMemory không chứng minh được transaction hay concurrency; các thay đổi chạm ledger, WAC, reservation, idempotency, concurrency phải có integration test trên PostgreSQL thật.

---

## 2. Lệnh Thực Thi Kiểm Thử & Đo Độ Phủ (`NFR-MAINT-02`)

Integration test cần các biến `OISM_TEST_HOST`, `OISM_TEST_PORT`, `OISM_TEST_ADMIN_USER`, `OISM_TEST_ADMIN_PASSWORD`, `OISM_TEST_APP_USER`, `OISM_TEST_APP_PASSWORD` (cách đặt xem `.github/workflows/ci.yml` hoặc [DOC-OPS-02](../07-devops-and-deployment/02-ci-cd-github-actions.md)).

```powershell
# Unit test
dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj

# Integration test một nhóm
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj --filter "FullyQualifiedName~<TenTest>"

# Toàn bộ (như CI)
dotnet test src/backend/OISM.sln --configuration Release

# Chạy kèm thu thập báo cáo Code Coverage (Cobertura)
dotnet test src/backend/OISM.sln --collect:"XPlat Code Coverage"
```

- **Chỉ tiêu đề bài:** Tối thiểu `80%` cho `Inventory Ledger`, `Anti-Oversell Reserve`, `COGS`.
- **Kết quả đo tại Sprint 2 (mốc lịch sử, chưa đo lại sau các gói Upgrade):** **`92.49%` Line Coverage** trong Integration Tests và **`94.83%`** khi kết hợp Unit Tests.
