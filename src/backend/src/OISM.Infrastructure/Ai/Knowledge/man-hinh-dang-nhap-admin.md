---
id: man-hinh-dang-nhap-admin
title: Đăng nhập hệ thống Admin
roles: [Owner, Staff]
keywords: [đăng nhập, dang nhap, tài khoản, tai khoan, mật khẩu, mat khau, login, auth]
source: docs/11-ui-screens/01-auth-screens.md#1-màn-hình-đăng-nhập-hệ-thống-login
---
Mở trang đăng nhập Admin tại đường dẫn /login. Người dùng nhập mã cửa hàng (tenant slug), email hoặc số điện thoại đã đăng ký và mật khẩu tài khoản. Bấm nút Đăng nhập để truy cập vào hệ thống. Nút này có cơ chế chống bấm lặp và hiển thị biểu tượng xoay khi đang gửi yêu cầu. Nếu tài khoản có vai trò Thu ngân (Cashier), phiên đăng nhập Admin sẽ bị thu hồi và hệ thống tự động chuyển hướng sang màn hình đăng nhập của ứng dụng POS riêng. Thu ngân cũng có thể bấm trực tiếp nút liên kết dành cho thu ngân ở dưới nút đăng nhập để sang quầy POS. Khi phiên làm việc hết hạn sau 60 phút, người dùng cần thực hiện đăng nhập lại để tiếp tục thao tác.
