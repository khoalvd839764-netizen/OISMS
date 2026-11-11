# test-oisms — toàn bộ test của OISMS

Mọi test tự động của dự án nằm trong thư mục này. Source ứng dụng vẫn ở `src/`.

| Thư mục | Nội dung | Công cụ |
| --- | --- | --- |
| `backend/OISM.UnitTests` | Unit test backend (service, validator, WAC, tồn kho…), dùng EF InMemory và Moq | xUnit, .NET 8 |
| `backend/OISM.IntegrationTests` | Integration test API; nhóm `Postgres*`/concurrency cần PostgreSQL thật | xUnit, `WebApplicationFactory` |
| `frontend/pos-app` | Test POS: checkout, giỏ đa đơn vị, QR, auth, realtime, hóa đơn, service worker; `test-live.mjs` chạy riêng trên backend cô lập | `node --test` |
| `frontend/pos-app/test-support` | Harness dùng chung: `app-path.mjs` (đường dẫn tới source POS), `pos-runtime.cjs`, proxy mạng, guard môi trường | |
| `frontend/marketplace-simulator` | Test dựng payload webhook của app giả lập sàn | `node --test` |
| `api-python` | Script kiểm thử hộp đen API trả hàng CS-01 (`test_sales_return_api.py`) trên backend đang chạy, tự bán đơn POS rồi trả hàng. `seed_revenue_perf.py` + `.sql` bắn 5000 đơn POS/sàn vào tenant demo local qua API, rải 90 ngày và kiểm tra kho để đo tốc độ báo cáo doanh thu (chỉ chạy local, dữ liệu không gỡ được). Cần `OISMS_PASSWORD` | Python 3 + `requests` |
| `test-data` | Chỗ để dữ liệu mẫu/fixture dùng chung (JSON, SQL). Hiện dữ liệu test vẫn được tạo trong code | |

## Cách chạy

Backend (hai project test vẫn nằm trong `src/backend/OISM.sln`):

```bash
dotnet test src/backend/OISM.sln
dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj
dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj --filter "FullyQualifiedName~<TenTest>"
```

Integration test cần PostgreSQL: đặt các biến `OISM_TEST_*` (xem `backend/OISM.IntegrationTests/PostgresTestEnvironment.cs` và `.github/workflows/ci.yml`).

Frontend: lệnh vẫn chạy từ app tương ứng, vì test dùng `typescript` và các gói trong `node_modules` của app đó (cần `npm ci` trong app trước):

```bash
npm test --prefix src/frontend/pos-app
npm run test:live --prefix src/frontend/pos-app   # cần docker/pos-tests/compose.yml
npm test --prefix src/frontend/marketplace-simulator
```

## Thêm test mới

- Backend: thêm file `.cs` vào project test phù hợp, không cần sửa gì thêm.
- POS: tạo `frontend/pos-app/test-<ten>.mjs`, nạp source qua `APP_URL`/`APP_DIR`/`ts` từ `./test-support/app-path.mjs`, rồi thêm file vào script `test` trong `src/frontend/pos-app/package.json`.
- Simulator: tương tự, thêm vào script `test` của `src/frontend/marketplace-simulator/package.json`.

Lịch sử chuyển test POS từ Admin: [frontend/pos-app/TEST-MIGRATION.md](frontend/pos-app/TEST-MIGRATION.md).
