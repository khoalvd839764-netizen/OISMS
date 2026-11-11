---
id: vong-doi-phieu-giu-cho
title: Vòng đời và trạng thái phiếu giữ chỗ
roles: [Owner, Staff]
keywords: [phiếu giữ chỗ, phieu giu cho, vòng đời giữ chỗ, vong doi giu cho, active, consumed, released, expired]
source: docs/04-architecture-and-design/04-state-machines.md#11-máy-trạng-thái-phiếu-giữ-chỗ-reservation
---
Phiếu giữ chỗ tồn kho được quản lý theo vòng đời chặt chẽ gồm 4 trạng thái. Khi đơn hàng đặt thành công và còn đủ tồn khả dụng, phiếu giữ chỗ được tạo ở trạng thái Đang hiệu lực (Active), làm tăng số lượng giữ chỗ tại kho và chi nhánh. Từ trạng thái này, phiếu có thể chuyển sang một trong ba trạng thái kết thúc: Chuyển sang Đã tiêu thụ (Consumed) khi đơn hàng được duyệt xuất kho, trừ tồn thực tế và ghi sổ cái xuất; Chuyển sang Đã giải phóng (Released) khi đơn hàng bị nhân viên hoặc khách hủy lúc phiếu vẫn còn thời hạn hiệu lực; Hoặc chuyển sang Hết hạn (Expired) khi thời hạn giữ chỗ đã trôi qua mà đơn hàng chưa được xác nhận thanh toán.
