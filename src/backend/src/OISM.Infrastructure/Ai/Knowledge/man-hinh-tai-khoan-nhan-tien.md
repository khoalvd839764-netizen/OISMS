---
id: man-hinh-tai-khoan-nhan-tien
title: Quản lý tài khoản ngân hàng nhận tiền
roles: [Owner]
keywords: [tài khoản nhận tiền, tai khoan nhan tien, ngân hàng, ngan hang, vietqr, thụ hưởng, thu huong]
source: docs/05-api-contracts/05-pos-endpoints.md#1a-tài-khoản-nhận-tiền-apipayment-accounts-cs-05
---
Quản lý tài khoản ngân hàng thụ hưởng là chức năng thuộc thẩm quyền của Chủ cửa hàng (Owner). Nhân viên và thu ngân chỉ có quyền xem danh sách tài khoản đang hoạt động để phục vụ tạo mã QR thanh toán tại quầy. Owner mở mục Tài khoản nhận tiền ở cuối thanh điều hướng để thêm tài khoản ngân hàng mới, chọn tên ngân hàng, nhập số tài khoản và tên chủ tài khoản. Sau khi tạo, mã ngân hàng và số tài khoản không sửa được; Owner chỉ sửa được tên ngân hàng, tên viết tắt và tên chủ tài khoản. Owner có thể thiết lập một tài khoản làm mặc định để hệ thống tự động sinh mã VietQR khi thanh toán POS, hoặc chuyển sang trạng thái ngừng dùng khi cần.
