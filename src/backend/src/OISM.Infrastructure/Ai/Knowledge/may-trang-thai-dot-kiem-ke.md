---
id: may-trang-thai-dot-kiem-ke
title: Máy trạng thái đợt kiểm kê kho
roles: [Owner, Staff]
keywords: [trạng thái kiểm kê, trang thai kiem ke, counting, pending approval, confirmed, cancelled]
source: docs/04-architecture-and-design/04-state-machines.md#41-máy-trạng-thái-đợt-kiểm-kê-stockcount
---
Đợt kiểm kê kho thực tế trải qua các bước trạng thái tuần tự. Khi bắt đầu, phiếu kiểm kê ở trạng thái Nháp (Draft), sau đó chuyển sang Đang đếm (Counting) khi nhân viên tiến hành ghi nhận số lượng đếm thực tế cho từng mặt hàng. Khi đã nhập đủ số lượng, nhân viên nộp phiếu để chuyển sang Chờ duyệt (PendingApproval). Từ trạng thái này, nếu phát hiện sai lệch số liệu hoặc nghi ngờ, Owner có thể yêu cầu đếm lại để phiếu quay về trạng thái Counting. Khi Owner chấp thuận số liệu, phiếu chuyển sang Đã chốt (Confirmed), hệ thống tự động sinh bút toán cân bằng kho. Phiếu có thể bị hủy (Cancelled) khi còn ở các bước trước khi chốt.
