---
id: ma-loi-phien-lam-viec-het-han
title: Mã lỗi phiên làm việc hết hạn AUTH_TOKEN_EXPIRED
roles: [Owner, Staff]
keywords: [hết hạn token, het han token, auth token expired, phiên hết hạn, phien het han, đăng nhập lại, dang nhap lai]
source: docs/12-error-and-status-codes/02-error-codes-catalog.md#21-phân-hệ-xác-thực-khách-thuê--phân-quyền-auth
---
Mã lỗi AUTH_TOKEN_EXPIRED (HTTP 401) xuất hiện khi mã xác thực người dùng gửi kèm yêu cầu đã quá thời hạn sử dụng 60 phút. Khi gặp mã lỗi này, ứng dụng quản trị Admin hoặc quầy POS sẽ tự động kích hoạt cơ chế gửi mã làm mới (Refresh Token) lên máy chủ để nhận mã xác thực mới mà không làm gián đoạn công việc của người dùng. Trong trường hợp cả mã làm mới cũng đã hết hạn sau 30 ngày hoặc đã bị hệ thống thu hồi bảo mật, ứng dụng sẽ tự động chuyển hướng người dùng về màn hình thông báo hết hạn phiên làm việc hoặc hiển thị hộp thoại yêu cầu nhập lại mật khẩu để tiếp tục thao tác an toàn.
