# Báo cáo Backtest Dự báo Nhập hàng (`Oism.ForecastEval`)

- **FormulaVersion:** `reorder-v1`
- **Commit:** `879a1ef`
- **Tập dữ liệu:** Tenant `FORECAST-EVAL` (DB `oisms_forecast_eval`), khoảng `2026-08-01` → `2026-10-29`
- **Tham số:** `LeadTimeDays = 7`, `SafetyStockDays = 3`, `H = 10`, bước `t` mỗi `7` ngày (8 mốc)
- **Ngưỡng Q6 (chỉ tính trên nhóm `Eligible >=10d/30d`, không có ngày hết hàng):** `WAPE <= 35%`, `Recall >= 80%`
- **Kết luận tổng thể:** **ĐẠT**

## Kết quả theo nhóm SKU

| Nhóm | Số mẫu | Tổng thực tế | Tổng dự báo | Sai số tuyệt đối | WAPE | Cần nhập thực tế | Đúng (TP) | Recall | Dự báo cần nhập | Precision |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Eligible (>=10d/30d) | 43 | 2962 | 3002 | 228 | 7.70% | 7 | 7 | 100.00% | 8 | 87.50% |
| Intermittent (1-9d/30d) | 8 | 48 | 48 | 0 | 0.00% | 0 | 0 | N/A | 0 | N/A |
| NoSales (0d/30d) | 8 | 0 | 0 | 0 | N/A | 0 | 0 | N/A | 0 | N/A |
| Stockout | 5 | 104 | 223.4 | 175.4 | 168.65% | 5 | 5 | 100.00% | 5 | 100.00% |

## Top 10 sai số lớn nhất

| Mốc `t` | SKU | Nhóm | Tồn tại `t` | Dự báo `v×H` | Thực tế `[t, t+H)` | Sai số `|err|` |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| 2026-10-12 | EVAL-STOCKOUT-01 | Stockout | 0 | 58.7 | 0 | 58.7 |
| 2026-10-19 | EVAL-STOCKOUT-01 | Stockout | 0 | 44.7 | 0 | 44.7 |
| 2026-09-14 | EVAL-STOCKOUT-01 | Eligible (>=10d/30d) | 164 | 24 | 60 | 36 |
| 2026-09-21 | EVAL-STOCKOUT-01 | Eligible (>=10d/30d) | 122 | 26 | 60 | 34 |
| 2026-08-31 | EVAL-STOCKOUT-01 | Stockout | 0 | 40 | 12 | 28 |
| 2026-09-07 | EVAL-STOCKOUT-01 | Stockout | 0 | 26 | 54 | 28 |
| 2026-09-28 | EVAL-STOCKOUT-01 | Eligible (>=10d/30d) | 80 | 40 | 60 | 20 |
| 2026-10-05 | EVAL-STOCKOUT-01 | Stockout | 38 | 54 | 38 | 16 |
| 2026-08-31 | EVAL-LOWSTOCK-WEEKLY-01 | Eligible (>=10d/30d) | 34 | 62 | 54 | 8 |
| 2026-09-07 | EVAL-LOWSTOCK-WEEKLY-01 | Eligible (>=10d/30d) | 89 | 62 | 54 | 8 |
