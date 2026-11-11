---
id: co-lap-du-lieu-da-nguoi-thue
title: Kiến trúc đa người thuê và cô lập dữ liệu
roles: [Owner, Staff]
keywords: [đa người thuê, da nguoi thue, cô lập dữ liệu, co lap du lieu, tenant, bảo mật cửa hàng, bao mat cua hang]
source: docs/00-overview/02-core-domain-concepts.md#1-kiến-trúc-đa-người-thuê--cô-lập-dữ-liệu-multi-tenancy--data-isolation
---
OISMS vận hành theo mô hình kiến trúc đa người thuê (Multi-Tenancy) phục vụ nhiều chuỗi cửa hàng bán lẻ trên cùng một hạ tầng dùng chung. Mỗi doanh nghiệp đăng ký sử dụng được cấp một định danh riêng và hoạt động trong một không gian dữ liệu hoàn toàn độc lập. Cửa hàng của người dùng được xác định tự động khi đăng nhập. Mọi truy vấn dữ liệu từ sản phẩm, tồn kho, đơn hàng, sổ cái đến báo cáo tài chính đều được hệ thống tự động lọc theo cửa hàng đang đăng nhập, nên người dùng không xem hay sửa được dữ liệu của cửa hàng khác.
