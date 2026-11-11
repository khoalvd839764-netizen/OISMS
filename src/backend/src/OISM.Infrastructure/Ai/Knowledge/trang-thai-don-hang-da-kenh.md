---
id: trang-thai-don-hang-da-kenh
title: Máy trạng thái và vòng đời đơn hàng đa kênh
roles: [Owner, Staff]
keywords: [trạng thái đơn, trang thai don, vòng đời đơn hàng, vong doi don hang, draft, reserved, confirmed, completed, cancelled]
source: docs/04-architecture-and-design/04-state-machines.md#1-máy-trạng-thái-đơn-hàng-đa-kênh-fr-ord-02-fr-rse-03
---
Vòng đời của một đơn hàng đa kênh tuân thủ máy trạng thái hữu hạn chuẩn hóa. Đơn hàng khởi tạo ở trạng thái Nháp (Draft). Nếu còn đủ tồn khả dụng, đơn chuyển sang Đang giữ chỗ (Reserved) để giữ hàng trong 15 phút. Khi nhân viên duyệt đơn hoặc thanh toán tại quầy POS, đơn chuyển sang Đã duyệt (Confirmed), hệ thống trừ tồn thực tế, ghi sổ cái xuất và chụp giá vốn. Khi giao hàng thành công, đơn chuyển sang Hoàn tất (Completed). Đơn ở trạng thái Draft hoặc Reserved có thể chuyển sang Đã hủy (Cancelled) để hoàn trả tồn khả dụng. Đơn đã Confirmed hoặc Completed không thể hủy mà phải xử lý qua quy trình trả hàng từ khách.
