---
id: man-hinh-tra-hang-tu-khach
title: Lập và xác nhận phiếu trả hàng từ khách
roles: [Owner, Staff]
keywords: [trả hàng, tra hang, phiếu trả hàng, phieu tra hang, hoàn tiền, hoan tien, khách trả, khach tra]
source: docs/04-architecture-and-design/04-state-machines.md#5-máy-trạng-thái-phiếu-trả-hàng-từ-khách-salesreturn-cs-01
---
Vào mục Trả hàng tại đường dẫn /sales-returns để tiếp nhận hàng hoàn trả từ khách hàng. Bấm Tạo phiếu trả, tìm kiếm và chọn đơn hàng gốc đã xuất kho thành công (ở trạng thái đã duyệt hoặc đã hoàn tất). Chọn kho nhận hàng thuộc đúng chi nhánh của đơn hàng gốc, chọn các dòng SKU và nhập số lượng trả, bảo đảm không vượt quá số lượng còn được phép trả của đơn. Nhập lý do trả hàng và số tiền hoàn lại cho khách. Khi bấm Xác nhận trả hàng, hệ thống tăng lại số lượng tồn thực tế trong kho, ghi bút toán nhập vào sổ cái với đơn giá vốn được lấy từ giá vốn đã chụp lúc bán, tính lại giá vốn bình quân và ghi nhận phương thức hoàn tiền.
