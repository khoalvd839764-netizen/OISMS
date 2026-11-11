---
id: may-trang-thai-phieu-mua-hang
title: Máy trạng thái phiếu mua hàng
roles: [Owner, Staff]
keywords: [trạng thái phiếu mua, trang thai phieu mua, vòng đời phiếu mua, vong doi phieu mua, draft, approved, received]
source: docs/04-architecture-and-design/04-state-machines.md#21-máy-trạng-thái-phiếu-mua-hàng-oisms-47-u01a
---
Phiếu mua hàng từ nhà cung cấp vận hành qua các trạng thái nghiêm ngặt. Ban đầu, phiếu được lập ở trạng thái Nháp (Draft) bởi Quản lý hoặc Chủ cửa hàng. Khi Owner phê duyệt, phiếu chuyển sang Đã duyệt (Approved) và khóa cố định giá cùng số lượng đặt. Khi kho xác nhận các đợt nhập hàng, nếu mới nhận một phần số lượng, phiếu chuyển sang Nhận một phần (PartiallyReceived); khi đã nhận đủ toàn bộ các dòng hàng, phiếu chuyển sang Đã nhận đủ (Received). Owner có quyền hủy phiếu (Cancelled) khi còn nháp hoặc khi đã duyệt nhưng chưa phát sinh nhận hàng. Owner cũng có quyền đóng phiếu (Closed) kèm lý do giải trình khi ngừng nhận tiếp.
