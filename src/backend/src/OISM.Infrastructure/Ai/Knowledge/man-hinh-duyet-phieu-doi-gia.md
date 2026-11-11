---
id: man-hinh-duyet-phieu-doi-gia
title: Duyệt và từ chối phiếu đổi giá bán
roles: [Owner]
keywords: [duyệt giá, duyet gia, phê duyệt giá, phe duyet gia, từ chối giá, tu choi gia, bảng giá, bang gia]
source: docs/Upgrade-recommended/04-bang-gia-theo-thoi-gian.md#phân-quyền
---
Chức năng phê duyệt hoặc từ chối phiếu đổi giá bán là quyền độc quyền của Chủ cửa hàng (Owner). Nhân viên quản lý (Staff) chỉ được tạo phiếu ở trạng thái chờ duyệt và không thể tự kích hoạt giá mới. Owner kiểm tra danh sách các phiếu đổi giá đang chờ duyệt, đối chiếu mã SKU, đơn vị tính, mức giá bán lẻ và bán sỉ đề xuất cùng lý do điều chỉnh. Nếu chấp thuận, Owner bấm duyệt để kích hoạt giá mới theo mốc thời gian hiệu lực. Nếu từ chối, phiếu sẽ bị hủy bỏ và mức giá cũ tiếp tục được duy trì. Owner cũng có quyền hủy các dòng giá đã lên lịch trong tương lai trước khi mốc thời gian hiệu lực bắt đầu.
