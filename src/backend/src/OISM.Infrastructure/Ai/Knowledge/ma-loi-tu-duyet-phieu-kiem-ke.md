---
id: ma-loi-tu-duyet-phieu-kiem-ke
title: Mã lỗi tự duyệt kiểm kê STOCK_COUNT_SELF_APPROVAL
roles: [Owner]
keywords: [tự duyệt kiểm kê, tu duyet kiem ke, self approval, duyệt kiểm kê, duyet kiem ke, owner duyệt, owner duyet]
source: docs/12-error-and-status-codes/02-error-codes-catalog.md#25-phân-hệ-kiểm-kê-kho-stock_count
---
Mã lỗi STOCK_COUNT_SELF_APPROVAL (HTTP 409) phát sinh khi một Chủ cửa hàng (Owner) trực tiếp tham gia ghi số lượng đếm thực tế trên các dòng hàng của phiếu kiểm kê và sau đó cố gắng tự phê duyệt chốt chính phiếu kiểm kê đó, trong khi cửa hàng vẫn còn tài khoản Owner khác đang hoạt động. Đây là quy tắc kiểm soát nội bộ nhằm ngăn ngừa gian lận kho vận. Để xử lý tình huống này, phiếu kiểm kê cần được gửi cho một Owner khác trong cửa hàng để kiểm tra tính khách quan và thực hiện thao tác phê duyệt. Trường hợp cửa hàng chỉ có duy nhất một Owner, Owner tự duyệt bắt buộc phải nhập lý do giải trình trong phần ghi chú.
