---
id: may-trang-thai-ky-kiem-ke
title: Máy trạng thái kỳ kiểm kê định kỳ
roles: [Owner, Staff]
keywords: [kỳ kiểm kê, ky kiem ke, planned, in progress, completed, cancelled, kiểm kê định kỳ]
source: docs/04-architecture-and-design/04-state-machines.md#42-máy-trạng-thái-kỳ-kiểm-kê-định-kỳ-stockcountperiod
---
Kỳ kiểm kê định kỳ được quản lý theo vòng đời từ lúc lập kế hoạch đến khi hoàn tất. Khi lịch kiểm kê được thiết lập, kỳ kiểm kê mới được khởi tạo ở trạng thái Lên kế hoạch (Planned). Khi đến hạn kiểm kê, người dùng bấm bắt đầu đếm để chuyển kỳ sang trạng thái Đang thực hiện (InProgress), đồng thời hệ thống chụp ảnh số dư sổ sách và tạo phiếu kiểm kê tương ứng. Khi phiếu kiểm kê được Owner phê duyệt chốt cân bằng tồn thành công, kỳ kiểm kê chuyển sang trạng thái Hoàn tất (Completed). Nếu phiếu kiểm kê bị hủy trong lúc đang đếm, kỳ kiểm kê sẽ quay lại trạng thái Planned để có thể bắt đầu lại từ đầu khi sẵn sàng.
