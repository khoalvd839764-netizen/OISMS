---
id: man-hinh-duyet-phieu-mua-hang
title: Duyệt, đóng và hủy phiếu mua hàng
roles: [Owner]
keywords: [duyệt phiếu mua, duyet phieu mua, trả về phiếu mua, tra ve phieu mua, đóng phiếu mua, dong phieu mua, hủy phiếu mua, huy phieu mua]
source: docs/Upgrade-recommended/Done/01-mua-hang-nhap-kho-gia-von.md#trạng-thái-đơn-mua
---
Duyệt, trả về, đóng và hủy phiếu mua hàng là quyền của Chủ cửa hàng (Owner). Owner chỉ duyệt được đơn ở Chờ duyệt (PendingApproval) khi đã chọn nhà cung cấp và nhập đủ đơn giá mọi dòng; dòng giá 0 phải xác nhận rõ. Chiết khấu không được lớn hơn tổng tiền hàng. Nếu đơn vừa bị sửa ở tab khác, hệ thống báo xung đột phiên bản và yêu cầu tải lại. Khi duyệt, phí vận chuyển và chiết khấu được phân bổ vào giá vốn từng dòng theo tỷ lệ số lượng nhân đơn giá, VAT không vào giá vốn. Giá đã duyệt là giá cam kết với nhà cung cấp, không đổi khi nhập kho. Owner có thể Trả về Draft kèm lý do bắt buộc để Staff sửa, hoặc Hủy. Sau khi duyệt, Staff thấy nhà cung cấp để nhận hàng nhưng không thấy giá. Owner đóng phiếu kèm lý do khi nhà cung cấp không giao đủ; phí của phần chưa nhận không vào giá vốn. Hóa đơn nhà cung cấp đã bỏ.
