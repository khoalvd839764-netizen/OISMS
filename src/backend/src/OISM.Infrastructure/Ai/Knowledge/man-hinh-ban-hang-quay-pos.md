---
id: man-hinh-ban-hang-quay-pos
title: Bán hàng và thanh toán nhanh tại quầy POS
roles: [Owner, Staff]
keywords: [bán hàng pos, ban hang pos, quầy thu ngân, quay thu ngan, thanh toán nhanh, thanh toan nhanh, f2, f4]
source: docs/11-ui-screens/06-pos-terminal-screens.md#1-mục-tiêu--luồng-nghiệp-vụ
---
Ứng dụng POS độc lập được thiết kế tối ưu cho nhân viên thu ngân tại quầy bán lẻ với thao tác bàn phím thuận tiện. Thu ngân bấm phím F2 để quét mã vạch sản phẩm hoặc tìm kiếm nhanh theo tên mặt hàng, mã SKU. Giỏ hàng hiển thị các mặt hàng đã chọn, số lượng và thành tiền, đồng thời tự động chặn nếu sản phẩm đã hết tồn khả dụng. Bấm phím F4 để mở hộp thoại thanh toán, hỗ trợ hai phương thức: tiền mặt và chuyển khoản mã VietQR. Khi bấm xác nhận thanh toán, hệ thống thực hiện chuỗi thao tác nguyên tử trong một giao dịch duy nhất: trừ tồn kho thực tế, ghi sổ cái xuất hàng, chụp giá vốn, lập hóa đơn bán lẻ và in hóa đơn nhiệt.
