# Gói 5 — Đánh giá dự báo nhập hàng

## Hiện trạng

`FR-AI-01` đã có API và bảng gợi ý nhập hàng. Công thức hiện lấy tốc độ bán trong cửa sổ 30 ngày, tồn khả dụng, thời gian cung ứng và ngày dự phòng. Unit test xác minh công thức và ca biên. CS-08 còn thiếu kết quả so với thực tế. [Đặc tả dự báo](../../02-functional-requirements/fr-12-ai-forecasting.md) · [AiForecastingService](../../../src/backend/src/OISM.Application/Features/Ai/Services/AiForecastingService.cs#L186) · [Unit test hiện có](../../../test-oisms/backend/OISM.UnitTests/AiForecastingTests.cs)

## Việc cần làm

- [x] Chọn tập lịch sử theo SKU và chi nhánh, chuẩn hóa về đơn vị gốc. Ghi khoảng ngày, độ phủ dữ liệu và các kỳ thiếu hàng hoặc thiếu lịch sử bán (`test-oisms/forecast-eval/seed-eval.sql`, tenant `FORECAST-EVAL`, 90 ngày).
- [x] Chốt khoảng dự báo, thước đo và ngưỡng chấp nhận trước khi xem kết quả (`H = 10`, `WAPE ≤ 35%`, `Recall ≥ 80%` cho SKU ≥ 10 ngày bán/30 ngày).
- [x] Chạy đối chiếu trên dữ liệu lịch sử theo mốc thời gian (`Oism.ForecastEval`, chỉ đọc, mốc `t` mỗi 7 ngày, tách riêng bán gián đoạn, không bán và kỳ hết hàng).
- [x] Lưu cách chạy, đầu vào, tham số, kết quả theo nhóm SKU và ví dụ có sai số lớn (`docs/06-testing-and-qa/evidence/FR-AI-01/report.md`, `results.json`).

## Bằng chứng nghiệm thu

- [x] Có báo cáo tái lập được, ghi rõ phiên bản công thức (`reorder-v1`), dữ liệu, thước đo, ngưỡng đã thống nhất và kết quả so với ngưỡng (`docs/report-after-upgrade/danh-gia-du-bao-nhap-hang-cs08.md`).
- [ ] Có minh họa gợi ý trên UI khớp kết quả API, gồm SKU có đủ lịch sử và SKU có ít dữ liệu. Kết quả được liên kết vào CS-08 và RTM.
