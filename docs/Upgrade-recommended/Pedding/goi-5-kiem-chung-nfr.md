# Gói 5 — Kiểm chứng NFR

## Hiện trạng

Đã có unit test, integration test PostgreSQL và test POS/simulator. Bài thử 50 webhook tranh một đơn vị tồn đã đạt trên PostgreSQL kiểm thử. Các ngưỡng độ trễ, uptime, TLS, thao tác POS và coverage hiện tại cần bằng chứng riêng. [Hiện trạng NFR](../../03-non-functional-requirements/README.md) · [RTM](../../06-testing-and-qa/01-requirements-matrix-rtm.md)

## Việc cần đo

| Mã | Phép đo và tiêu chí |
| --- | --- |
| `NFR-PERF-01` | Đo riêng POS search/barcode `p95 < 200 ms`, tạo đơn/giao dịch ghi `< 500 ms`, báo cáo doanh thu `< 2 giây` với dưới `100.000` bản ghi. Ghi endpoint, lượng dữ liệu, số request, độ đồng thời và độ trễ thực đo. |
| `NFR-PERF-02` | Lưu bằng chứng local hiện có và chạy lại trên AWS nếu cần bằng chứng deploy. Kịch bản 50 webhook tranh một đơn vị cuối phải có đúng 1 thành công, 49 trả `409`, tồn khả dụng về 0. |
| `NFR-TENANT-02` | Bổ sung bằng chứng quy mô lớn cho các chỉ mục đã có. Lưu kích thước dữ liệu, kế hoạch truy vấn và thời gian của các truy vấn ledger/đơn hàng đại diện. |
| `NFR-SEC-01` | Xác minh kết nối HTTPS thương lượng TLS 1.3 trên môi trường triển khai. Ghi kết quả TLS 1.2 và chốt chính sách giao thức với yêu cầu đề bài. |
| `NFR-USA-01` | Đo uptime trong khung 07:00–22:00 theo Asia/Saigon, với mục tiêu ít nhất 99,5%. Ghi khoảng thời gian đo, số lần kiểm tra và khoảng gián đoạn. |
| `NFR-USA-02` | Đối chiếu thanh toán dưới ba lần bấm hoặc qua barcode + Enter. Kiểm tra giao diện trên desktop, tablet và thiết bị POS cầm tay. Ghi đường thao tác đạt hoặc còn lệch. |
| `NFR-MAINT-02` | Đo coverage unit và integration theo cùng phạm vi cho Ledger, Anti-Oversell Reserve và COGS/WAC. Báo theo module và đối chiếu ngưỡng tối thiểu 80%. |

`NFR-MAINT-01` được xử lý ở [Gói 1](./goi-1-phan-quyen-va-kien-truc.md). `NFR-SEC-02` được xử lý cùng [vòng đời đơn ở Gói 3](./goi-3-vong-doi-don-hang-va-hangfire.md).

## Bằng chứng nghiệm thu

- [ ] Mỗi phép đo có phiên bản code, môi trường, cách chạy, dữ liệu thử, kết quả thô và trạng thái đạt/chưa đạt. Dùng dữ liệu thử riêng cho các kịch bản tạo đơn.
- [ ] Đính kèm log, báo cáo coverage hoặc bản ghi thao tác tương ứng và cập nhật RTM theo phạm vi đã kiểm chứng. [Đặc tả hiệu năng](../../03-non-functional-requirements/01-performance-and-load.md) · [Kịch bản tải](../../06-testing-and-qa/03-concurrency-stress-test.md) · [Script seed báo cáo](../../../test-oisms/api-python/seed_revenue_perf.py)
