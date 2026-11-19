---
id: may-trang-thai-phieu-mua-hang
title: Máy trạng thái phiếu mua hàng
roles: [Owner, Staff]
keywords: [trạng thái phiếu mua, trang thai phieu mua, vòng đời phiếu mua, vong doi phieu mua, chờ duyệt, cho duyet, draft, pendingapproval, approved, received]
source: docs/04-architecture-and-design/04-state-machines.md#21-máy-trạng-thái-phiếu-mua-hàng-oisms-47-u01a-oisms-66-l2-g1
---
Phiếu mua hàng đi qua các trạng thái: Nháp (Draft) → Chờ duyệt (PendingApproval) → Đã duyệt (Approved) → Nhận một phần (PartiallyReceived) / Đã nhận đủ (Received) / Đã đóng (Closed); có thể Hủy (Cancelled). Staff lập đề nghị mua ở Draft rồi bấm Gửi để chuyển sang PendingApproval; Owner tự tạo thì đơn vào thẳng PendingApproval. Ở PendingApproval, Owner chọn nhà cung cấp, ngày đặt, giá, phí, chiết khấu, VAT và lưu nhiều lần; sau đó Duyệt (sang Approved), Trả về Draft kèm lý do bắt buộc, hoặc Hủy. Khi duyệt, hệ thống chốt giá vốn đơn vị sau phân bổ và giá này không đổi nữa. Kho nhập từng đợt: nhận một phần thì PartiallyReceived, đủ hết thì Received. Owner đóng phiếu kèm lý do khi ngừng nhận tiếp, và chỉ hủy được phiếu chưa phát sinh nhận hàng. Staff chỉ hủy được Draft do mình tạo.
