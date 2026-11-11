---
id: ma-loi-sai-lech-sequence-kiem-ke
title: Mã lỗi sai lệch sequence kiểm kê kho
roles: [Owner, Staff]
keywords: [sai lệch sequence, sai lech sequence, stock count drift, lệch kiểm kê, lech kiem ke, đếm lại, dem lai]
source: docs/12-error-and-status-codes/02-error-codes-catalog.md#25-phân-hệ-kiểm-kê-kho-stock_count
---
Mã lỗi STOCK_COUNT_SEQUENCE_DRIFT (HTTP 409) xuất hiện khi hệ thống phát hiện có các giao dịch xuất bán, nhập kho hoặc chuyển kho phát sinh xen giữa tại kho kiểm kê làm thay đổi số dư sổ sách của các mặt hàng trong lúc nhân viên đang đếm hoặc trước khi nộp duyệt. Thông báo lỗi liệt kê danh sách cụ thể các mã SKU bị biến động sequence. Đây là cơ chế bảo vệ giúp tránh ghi đè sai lệch dữ liệu tồn kho. Để khắc phục, nhân viên kiểm đếm cần thực hiện thao tác gọi chức năng đếm lại (recount) riêng cho những mặt hàng được cảnh báo để hệ thống lấy lại mốc số dư sổ sách mới nhất trước khi tiếp tục nộp duyệt.
