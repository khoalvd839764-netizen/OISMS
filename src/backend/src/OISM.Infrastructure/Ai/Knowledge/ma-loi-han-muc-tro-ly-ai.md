---
id: ma-loi-han-muc-tro-ly-ai
title: Các mã lỗi về giới hạn và hạn mức Trợ lý OISMS
roles: [Owner, Staff]
keywords: [lỗi trợ lý ai, loi tro ly ai, ai rate limited, ai model quota, hạn mức ai, han muc ai, hết lượt ai, het luot ai]
source: docs/12-error-and-status-codes/02-error-codes-catalog.md#28-trợ-lý-oisms-ai
---
Khi sử dụng Trợ lý OISMS, người dùng có thể gặp các mã lỗi liên quan đến giới hạn sử dụng: Mã AI_RATE_LIMITED (HTTP 429) xảy ra khi người dùng đặt câu hỏi quá nhanh, vượt tần suất 10 câu mỗi phút; giao diện sẽ khóa nút gửi trong 30 giây kèm thông báo thử lại sau ít phút. Mã AI_MODEL_QUOTA (HTTP 429) phát sinh khi gói dịch vụ miễn phí của nhà cung cấp mô hình trí tuệ nhân tạo đã hết lượt trong ngày; giao diện sẽ hiển thị gợi ý chuyển sang sử dụng mô hình còn lại trong ô chọn mô hình. Mã AI_DISABLED (HTTP 503) hiển thị khi tính năng trợ lý chưa được kích hoạt trên hệ thống; nút mở trợ lý sẽ tạm ẩn đến hết phiên.
