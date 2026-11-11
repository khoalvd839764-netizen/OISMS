---
id: tro-ly-oisms-doanh-thu-lai-gop
title: Hỏi doanh thu và lãi gộp qua Trợ lý OISMS
roles: [Owner]
keywords: [hỏi doanh thu, hoi doanh thu, hỏi lãi gộp, hoi lai gop, trợ lý ai, tro ly ai, lợi nhuận trợ lý]
source: docs/02-functional-requirements/fr-13-ai-assistant.md#fr-13-trợ-lý-oisms
---
Hỏi thông tin doanh thu và lợi nhuận gộp qua Trợ lý OISMS là tính năng độc quyền dành riêng cho Chủ cửa hàng (Owner). Công cụ tra cứu doanh thu hoàn toàn bị ẩn và không bao giờ được cung cấp cho mô hình ngôn ngữ khi người đăng nhập là Nhân viên quản lý (Staff). Khi Staff đặt câu hỏi liên quan đến doanh thu hoặc lãi gộp, trợ lý sẽ phản hồi thông báo tài khoản không có quyền xem thông tin này. Owner có thể hỏi tổng doanh thu thuần, số lượng đơn hàng bán được theo từng ngày, phân tích theo kênh bán hàng và lợi nhuận gộp trong khoảng thời gian tối đa 92 ngày. Số liệu được tổng hợp theo múi giờ chuẩn và hiển thị định dạng số tiền rõ ràng.
