---
id: ma-loi-sai-lech-don-gia-niem-yet
title: Mã lỗi sai lệch đơn giá niêm yết PRICE_MISMATCH
roles: [Owner, Staff]
keywords: [sai lệch giá, sai lech gia, price mismatch, lệch đơn giá, lech don gia, bảng giá đổi, bang gia doi]
source: docs/12-error-and-status-codes/02-error-codes-catalog.md#22-phân-hệ-danh-mục-sản-phẩm--biến-thể-sku-prod
---
Mã lỗi PRICE_MISMATCH (HTTP 400) xảy ra khi đơn giá của mặt hàng trong giỏ hàng gửi lên thanh toán không trùng khớp với đơn giá đang có hiệu lực trong bảng giá của hệ thống. Tình huống này thường phát sinh khi có phiếu đổi giá mới vừa đến mốc thời gian hiệu lực trong lúc giỏ hàng của thu ngân đang chờ thanh toán, hoặc do dữ liệu bảng giá trên máy trạm chưa được cập nhật mới nhất. Chi tiết lỗi đính kèm danh sách toàn bộ các dòng sản phẩm bị lệch giá cùng mức giá hiện hành mới. Để xử lý, giao diện hiển thị bảng đối chiếu mức giá mới để nhân viên xác nhận lại với khách trước khi gửi lại yêu cầu thanh toán.
