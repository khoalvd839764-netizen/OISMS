---
id: quy-tac-nghiep-vu-tra-hang
title: Quy tắc nghiệp vụ trả hàng từ khách
roles: [Owner, Staff]
keywords: [quy tắc trả hàng, quy tac tra hang, hoàn tiền, hoan tien, nhận lại hàng, nhan lai hang, sales return]
source: docs/04-architecture-and-design/04-state-machines.md#5-máy-trạng-thái-phiếu-trả-hàng-từ-khách-salesreturn-cs-01
---
Quy trình trả hàng từ khách áp dụng cho các đơn hàng gốc đã xuất kho thành công ở trạng thái đã duyệt (Confirmed) hoặc đã hoàn tất (Completed). Phiếu trả hàng được tạo ở trạng thái Nháp (Draft). Hệ thống kiểm tra số lượng trả trên từng dòng mặt hàng không được vượt quá số lượng đã mua trừ đi các lần trả trước đó, và số tiền hoàn lại không được vượt mức quy định. Khi phiếu được xác nhận chuyển sang Confirmed, hệ thống tự động tăng tồn thực tế trong kho nhận, ghi dòng sổ cái nhập với giá vốn lấy từ giá vốn đã chụp lúc bán, tính lại giá vốn bình quân và ghi nhận thông tin hoàn tiền. Phiếu đã xác nhận là trạng thái kết thúc, không thể sửa hay hủy.
