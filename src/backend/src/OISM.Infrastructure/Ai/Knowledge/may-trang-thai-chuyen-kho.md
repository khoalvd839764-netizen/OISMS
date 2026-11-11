---
id: may-trang-thai-chuyen-kho
title: Máy trạng thái điều chuyển kho hai bước
roles: [Owner, Staff]
keywords: [trạng thái chuyển kho, trang thai chuyen kho, xuất chuyển, xuat chuyen, in transit, received, cancelled]
source: docs/04-architecture-and-design/04-state-machines.md#3-máy-trạng-thái-chuyển-kho-2-bước-fr-inv-03
---
Quy trình điều chuyển kho giữa các chi nhánh được quản lý qua máy trạng thái 2 bước độc lập. Phiếu chuyển kho được tạo ở trạng thái Nháp (Draft) để người dùng chọn kho xuất, kho nhận và danh sách mặt hàng. Khi kho xuất thực hiện gửi hàng, phiếu chuyển sang trạng thái Đang chuyển (InTransit), hệ thống lập tức trừ số lượng tồn thực tế tại kho xuất và ghi bút toán xuất vào sổ cái. Khi hàng đến nơi và kho nhận kiểm đếm đầy đủ, người dùng bấm tiếp nhận để đưa phiếu sang trạng thái Đã nhận (Received), hệ thống tăng tồn thực tế tại kho nhận và ghi bút toán nhập sổ cái. Phiếu ở trạng thái Draft có thể hủy bỏ nếu chưa xuất kho.
