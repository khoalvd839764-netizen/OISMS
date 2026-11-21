# Báo cáo đánh giá dự báo nhập hàng (`CS-08` / `FR-AI-01`)

- **Ngày:** 2026-11-21
- **Commit nền:** `879a1ef`
- **Phiên bản công thức:** `ReorderForecastCalculator.FormulaVersion = "reorder-v1"`

## 1. Đã làm

- Tách công thức dự báo từ `AiForecastingService` sang hàm thuần `ReorderForecastCalculator.Calculate(...)` (không phụ thuộc `DbContext` hay đồng hồ hệ thống, nhận mốc `asOf`).
- `AiForecastingService.GetReorderRecommendationsAsync` gọi `ReorderForecastCalculator.Calculate` với `asOf = DateOnly.FromDateTime(ReportTimeHelper.Today())`, giữ nguyên 100% contract và kết quả API.
- Tạo bộ đánh giá `test-oisms/forecast-eval/Oism.ForecastEval.csproj` (không đưa vào `OISM.sln` để không làm chậm CI):
  - Từ chối chạy nếu host DB không phải `localhost`/`127.0.0.1` (đã kiểm chứng trả exit code `2` khi trỏ `prod-db.example.com`).
  - Chạy ở chế độ chỉ đọc (`SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY;`).
  - Kèm `test-oisms/forecast-eval/seed-eval.sql` sinh dữ liệu tổng hợp 90 ngày cho tenant `FORECAST-EVAL` với 6 SKU (đều, theo tuần, gián đoạn, hết hàng, không bán), chạy lặp lại 2 lần cho cùng kết quả.

## 2. Lệnh chạy & Kết quả

- Unit tests: `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~AiForecasting"` → **PASS 9/9** (gồm test mới `GetReorderRecommendationsAsync_MatchesPureCalculator_OnSameDataset`).
- Backtest local:
  ```bash
  OISM_EVAL_DATABASE=oisms_forecast_eval dotnet run --project test-oisms/forecast-eval/Oism.ForecastEval.csproj -- --tenant FORECAST-EVAL --commit 879a1ef --out-dir docs/06-testing-and-qa/evidence/FR-AI-01
  ```
- Kết quả (`H = 10`, bước `t` mỗi 7 ngày, 8 mốc):
  - Nhóm đủ dữ liệu (`>= 10d/30d`, không hết hàng): `27` mẫu, **WAPE = 9.19%** (ngưỡng `<= 35%` → **ĐẠT**).
  - Nhóm bán gián đoạn (`1-9d/30d`): `8` mẫu, WAPE `0.00%` (báo riêng).
  - Nhóm không bán (`0d/30d`): `8` mẫu (báo riêng).
  - Nhóm có kỳ hết hàng (`Stockout`): `5` mẫu, Recall cần nhập `100.00%` (5/5, báo riêng).
- Chi tiết bảng và Top 10 sai số lớn nhất: [`docs/06-testing-and-qa/evidence/FR-AI-01/report.md`](../06-testing-and-qa/evidence/FR-AI-01/report.md) và [`results.json`](../06-testing-and-qa/evidence/FR-AI-01/results.json).

## 3. Giới hạn

- Kết quả đo trên **dữ liệu tổng hợp** (`FORECAST-EVAL` 90 ngày); chưa đo trên bản sao dữ liệu demo thực tế và chưa chụp ảnh minh họa UI Admin (mục cuối của `goi-5-danh-gia-du-bao-nhap-hang.md` giữ chưa tick).
- *(Đã xử lý ở FIN-C-fix1 bên dưới)* Trong nhóm `Eligible` của dữ liệu tổng hợp lần đầu có `ActualNeedCount = 0` do tồn đầu kỳ cao; trường hợp thiếu hàng thực tế đã được bao phủ ở nhóm `Stockout` (`Recall = 100%`).

## Chạy lại FIN-C-fix1 (21/11/2026)

Lần đầu, nhóm `Eligible` không có ca cần nhập nên recall là N/A, nhưng bộ đánh giá vẫn kết luận ĐẠT. Đã sửa: recall N/A không còn tính là đạt; seed thêm 2 SKU bán đều có tồn thấp nhưng không hết hàng; báo thêm precision. Công thức `reorder-v1` giữ nguyên.

| Nhóm Eligible | Kết quả |
| --- | --- |
| Số mẫu | 43 |
| WAPE | **7.70%** (ngưỡng ≤ 35%) |
| Recall | **100%** (7/7, ngưỡng ≥ 80%) |
| Precision | 87.50% (7/8) |

Kết luận: **ĐẠT** trên dữ liệu tổng hợp. Mẫu dương tính còn ít (7 ca), chưa đối chiếu dữ liệu bán thật. [Bằng chứng](../06-testing-and-qa/evidence/FR-AI-01/2026-11-21-FIN-C-fix1.md)
