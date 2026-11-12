# Gói 5 — Đánh giá dự báo nhập hàng

## Hiện trạng

`FR-AI-01` đã có API và bảng gợi ý nhập hàng. Công thức hiện lấy tốc độ bán trong cửa sổ 30 ngày, tồn khả dụng, thời gian cung ứng và ngày dự phòng. Unit test xác minh công thức và ca biên. CS-08 còn thiếu kết quả so với thực tế. [Đặc tả dự báo](../../02-functional-requirements/fr-12-ai-forecasting.md) · [AiForecastingService](../../../src/backend/src/OISM.Application/Features/Ai/Services/AiForecastingService.cs#L186) · [Unit test hiện có](../../../test-oisms/backend/OISM.UnitTests/AiForecastingTests.cs)

## Việc cần làm

- [ ] Chọn tập lịch sử theo SKU và chi nhánh, chuẩn hóa về đơn vị gốc. Ghi khoảng ngày, độ phủ dữ liệu và các kỳ thiếu hàng hoặc thiếu lịch sử bán.
- [ ] Chốt khoảng dự báo, thước đo và ngưỡng chấp nhận trước khi xem kết quả. Có thể dùng sai số tuyệt đối trung bình cho lượng bán dự báo và tỷ lệ gợi ý nhập phù hợp cho các SKU có đủ dữ liệu.
- [ ] Chạy đối chiếu trên dữ liệu lịch sử theo mốc thời gian. Ở mỗi mốc, chỉ dùng dữ liệu đã có tại thời điểm dự báo rồi so với lượng bán quan sát trong khoảng sau đó. Báo riêng SKU không bán, bán gián đoạn và các ngày hết hàng vì số bán quan sát có thể thấp hơn nhu cầu.
- [ ] Lưu cách chạy, đầu vào, tham số, kết quả theo nhóm SKU và ví dụ có sai số lớn. Ghi rõ quy tắc nào nên giữ hoặc điều chỉnh dựa trên kết quả.

## Bằng chứng nghiệm thu

- [ ] Có báo cáo tái lập được, ghi rõ phiên bản công thức, dữ liệu, thước đo, ngưỡng đã thống nhất và kết quả so với ngưỡng.
- [ ] Có minh họa gợi ý trên UI khớp kết quả API, gồm SKU có đủ lịch sử và SKU có ít dữ liệu. Kết quả được liên kết vào CS-08 và RTM.
