---
id: phan-quyen-tro-ly-oisms
title: Phân quyền truy cập Trợ lý OISMS
roles: [Owner, Staff]
keywords: [quyền trợ lý, quyen tro ly, phân quyền chatbot, phan quyen chatbot, ai rbac, quyền ai, quyen ai]
source: docs/01-users-and-rbac/02-rbac-matrix.md#2-ma-trận-phân-quyền-chi-tiết-cấp-nghiệp-vụ--api-fr-auth-03
---
Trợ lý OISMS chỉ phục vụ hai vai trò người dùng trong hệ thống là Chủ cửa hàng (Owner) và Quản lý (Staff). Vai trò Thu ngân (Cashier) không có quyền sử dụng trợ lý và nhận mã lỗi từ chối khi gọi dịch vụ. Quyền hạn của Owner và Staff khi tương tác với trợ lý được phân tách rõ ràng: Owner được quyền truy vấn toàn bộ dữ liệu, bao gồm doanh thu thuần, chi tiết đơn hàng, lợi nhuận gộp, biến động kho, cảnh báo tồn và hướng dẫn nghiệp vụ. Ngược lại, Staff chỉ được tra cứu số liệu nhập xuất kho, mặt hàng tồn thấp, gợi ý nhập hàng và hướng dẫn thao tác. Công cụ doanh thu bị ẩn hoàn toàn với Staff để bảo đảm bí mật tài chính.
