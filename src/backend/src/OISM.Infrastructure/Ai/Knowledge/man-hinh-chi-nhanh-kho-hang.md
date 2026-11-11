---
id: man-hinh-chi-nhanh-kho-hang
title: Quản lý mạng lưới chi nhánh và kho hàng
roles: [Owner]
keywords: [chi nhánh, chi nhanh, kho hàng, kho hang, cơ cấu tổ chức, co cau to chuc, tạo kho, tao kho]
source: docs/11-ui-screens/09-organization-screens.md#1-mục-tiêu--luồng-nghiệp-vụ
---
Màn hình Quản trị Cơ cấu Tổ chức tại đường dẫn /admin/organization là phân hệ dành riêng cho Chủ cửa hàng (Owner). Owner có toàn quyền tạo mới, chỉnh sửa thông tin liên hệ, địa chỉ và bật tắt trạng thái hoạt động của từng chi nhánh. Mỗi chi nhánh có thể quản lý nhiều kho hàng trực thuộc như kho bán lẻ tại quầy, kho tổng, kho dự trữ. Khi tạo chi nhánh mới, hệ thống tự động khởi tạo một kho mặc định gắn với chi nhánh đó. Để bảo đảm tính toàn vẹn dữ liệu, hệ thống nghiêm cấm hành vi chuyển đổi chi nhánh cha của một kho hàng nếu kho đó đã phát sinh bất kỳ giao dịch nhập xuất nào trong sổ cái tồn kho.
