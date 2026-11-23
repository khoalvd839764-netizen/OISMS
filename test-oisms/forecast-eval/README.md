# Oism.ForecastEval — Backtest dự báo nhập hàng (`CS-08` / `FR-AI-01`)

Công cụ console chạy local (chỉ đọc) gọi trực tiếp `ReorderForecastCalculator` từ `OISM.Application` để đối chiếu công thức dự báo với thực tế lịch sử.

## An toàn

- Từ chối chạy nếu `Host` trong chuỗi kết nối không phải `localhost` hoặc `127.0.0.1`.
- Mở phiên PostgreSQL ở chế độ `SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY;`.
- Không đưa vào `OISM.sln` để không làm chậm CI.

## Cách chạy trên PostgreSQL local (DB riêng `oisms_forecast_eval`)

```bash
# 1. Tạo DB đánh giá riêng và nạp schema chuẩn (chạy 1 lần)
docker exec -i oism_postgres psql -U postgres -c "CREATE DATABASE oisms_forecast_eval;"
docker exec -i oism_postgres psql -U postgres -d oisms_forecast_eval < docs/database/oisms_schema.sql

# 2. Sinh dữ liệu tổng hợp 90 ngày cho tenant FORECAST-EVAL (chạy lặp lại cho kết quả giống hệt nhau)
docker exec -i oism_postgres psql -U postgres -d oisms_forecast_eval < test-oisms/forecast-eval/seed-eval.sql

# 3. Chạy đánh giá backtest
OISM_EVAL_DATABASE=oisms_forecast_eval dotnet run --project test-oisms/forecast-eval/Oism.ForecastEval.csproj -- --tenant FORECAST-EVAL --commit 879a1ef --out-dir docs/06-testing-and-qa/evidence/FR-AI-01
```
