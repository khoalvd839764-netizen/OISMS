# 02. Quy Trình Tích Hợp Liên Tục Với GitHub Actions (CI Pipeline Specification)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-OPS-02` |
| **Phiên bản (Version)** | `v1.1.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-06` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `3.2.e`, `3.2.g` (Trang 5, 6) |
| **Phạm vi áp dụng** | `.github/workflows/ci.yml` & quy tắc nhánh Git (`main`, `develop`, `feature/*`) |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả luồng tự động hóa kiểm tra Build và Test trên GitHub Actions CI. | `INIT-OPS-02` |
| `v1.1.0` | `2026-11-06` | Nhóm dự án OISMS | Đồng bộ với `ci.yml`: Node 22, `npm install`, biến `OISM_TEST_*`, bước `npm test` của POS; ghi rõ CI không deploy. | `OISMS-46` |

---

## 1. Khi Nào Pipeline Chạy

`push` hoặc `pull_request` vào `main`, `master`, `develop`. Pipeline chỉ kiểm tra build và test; **không có bước deploy tự động**. Deploy lên EC2 làm thủ công, xem [`13-deploy-aws`](../13-deploy-aws/04-quy-trinh-deploy-va-cap-nhat.md).

## 2. Các Job

### `build-and-test-backend` (ubuntu-latest)

- Service container `postgres:16-alpine` (user/password `postgres`/`admin123`, cổng `5432`, có health check).
- Biến môi trường cho integration test cần DB thật:

| Biến | Giá trị trong CI |
| :--- | :--- |
| `OISM_TEST_HOST` / `OISM_TEST_PORT` | `localhost` / `5432` |
| `OISM_TEST_ADMIN_USER` / `OISM_TEST_ADMIN_PASSWORD` | `postgres` / `admin123` |
| `OISM_TEST_APP_USER` / `OISM_TEST_APP_PASSWORD` | `oisms_test_app` / mật khẩu thử nghiệm |

- Các bước: `actions/setup-dotnet` 8.0.x → `dotnet restore src/backend/OISM.sln` → `dotnet build ... -c Release` → `dotnet test src/backend/OISM.sln --no-build -c Release`.

### `build-frontend` (ubuntu-latest)

- `actions/setup-node` với Node `22`.
- `src/frontend/admin-dashboard`: `npm install` rồi `npm run build` (`tsc -b && vite build`).
- `src/frontend/pos-app`: `npm install`, `npm run build`, rồi `npm test` (chạy `test-pos-e2e.mjs`, `test-cart-units.mjs`, `test-b03-qr.mjs`, `test-b03-environment.mjs` bằng `node --test`).

## 3. Chạy Lại Ở Máy Local

```bash
dotnet build src/backend/OISM.sln
dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj
npm run build --prefix src/frontend/admin-dashboard
npm run build --prefix src/frontend/pos-app
npm test --prefix src/frontend/pos-app
```

Integration test cần PostgreSQL thật: đặt các biến `OISM_TEST_*` như bảng trên rồi chạy `dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj`. Không có biến thì nhóm test cần DB không chạy được. `scripts/build-all.ps1` gộp build backend, test, build hai frontend.

## 4. Quy Ước Nhánh Và Commit

- Nhánh: `main`, `develop` (tích hợp), `feature/OISMS-<id>-<mô-tả>`. PR thường vào `develop`.
- Commit: `[OISMS-<id>] <type>(<scope>): <mô tả tiếng Việt>`.
- Thay đổi chạm auth, tenant, migration, transaction, ledger, WAC, reservation, idempotency, concurrency cần bằng chứng trên PostgreSQL thật; mock/InMemory không đủ.
