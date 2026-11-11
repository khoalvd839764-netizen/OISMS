---
id: co-che-kiem-soat-quyen-hai-lop
title: Cơ chế kiểm soát quyền hai lớp và bảo vệ giá vốn
roles: [Owner]
keywords: [kiểm soát quyền, kiem soat quyen, hai lớp, hai lop, bảo vệ giá vốn, bao ve gia von, rbac security]
source: docs/01-users-and-rbac/02-rbac-matrix.md#3-cơ-chế-kiểm-soát-hai-lớp-dual-layer-enforcement
---
Hệ thống OISMS thực thi cơ chế bảo vệ phân quyền hai lớp nghiêm ngặt để ngăn chặn hành vi vượt quyền và bảo mật số liệu tài chính. Lớp thứ nhất là tầng giao diện người dùng: hệ thống đọc vai trò từ mã xác thực để ẩn các menu, nút bấm thao tác vượt quyền và chặn điều hướng vào các trang cấm, chuyển hướng sang màn hình từ chối 403 nếu cố tình truy cập. Lớp thứ hai là tầng dịch vụ hệ thống: mọi yêu cầu gửi lên máy chủ đều được kiểm tra vai trò người dùng độc lập. Ngay cả khi người dùng gửi yêu cầu can thiệp trực tiếp, máy chủ sẽ từ chối với mã lỗi 403. Các dữ liệu giá vốn và lãi gộp luôn được làm sạch trước khi trả về cho nhân viên quản lý.
