---
id: phan-quyen-vai-tro-thu-ngan-cashier
title: Phân quyền vai trò Thu ngân Cashier
roles: [Owner, Staff]
keywords: [vai trò cashier, vai tro cashier, thu ngân, thu ngan, quyền pos, quyen pos, bán lẻ quầy, ban le quay]
source: docs/01-users-and-rbac/02-rbac-matrix.md#2-ma-trận-phân-quyền-chi-tiết-cấp-nghiệp-vụ--api-fr-auth-03
---
Thu ngân (Cashier) là vai trò chuyên biệt phục vụ bán lẻ và thanh toán trực tiếp tại quầy thông qua ứng dụng POS độc lập. Thu ngân được phép: tra cứu sản phẩm, quét mã vạch mặt hàng, xem tồn khả dụng tức thời tại chi nhánh làm việc, tạo giỏ hàng và thực hiện thanh toán nhanh tiền mặt hoặc quét mã QR ngân hàng, in hóa đơn nhiệt cho khách và in lại hóa đơn bán lẻ đã xuất. Thu ngân bị chặn hoàn toàn quyền truy cập vào bàn làm việc quản trị Admin, không được xem sổ cái tồn kho, giá vốn, báo cáo doanh thu, phiếu nhập xuất kho hay quản lý sản phẩm. Khi Cashier cố tình đăng nhập vào trang Admin, hệ thống sẽ từ chối phiên và chuyển hướng sang POS.
