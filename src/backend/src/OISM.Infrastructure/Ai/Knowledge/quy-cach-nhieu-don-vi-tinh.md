---
id: quy-cach-nhieu-don-vi-tinh
title: Nhiều đơn vị tính và hệ số quy đổi SKU
roles: [Owner, Staff]
keywords: [đơn vị tính, don vi tinh, hệ số quy đổi, he so quy doi, quy cách, quy cach, đơn vị giao dịch, don vi giao dich]
source: docs/00-overview/02-core-domain-concepts.md#41-công-thức-tính-lại-giá-vốn-khi-nhập-kho-fr-cost-01
---
Hệ thống hỗ trợ quản lý một biến thể SKU theo nhiều đơn vị tính giao dịch khác nhau như cái, hộp, lốc, thùng. Mỗi SKU có duy nhất một đơn vị gốc cố định để theo dõi tồn kho và tính giá vốn. Các đơn vị quy đổi được khai báo kèm hệ số quy đổi dương biểu thị một đơn vị giao dịch bằng bao nhiêu đơn vị gốc. Trên các chứng từ bán hàng, nhập kho hoặc chuyển kho, người dùng có thể chọn đơn vị giao dịch phù hợp. Hệ thống tự động nhân số lượng giao dịch với hệ số quy đổi để ra số lượng đơn vị gốc phục vụ trừ tồn và ghi sổ cái, đồng thời chụp lại hệ số tại thời điểm giao dịch để tránh sai lệch khi hệ số có điều chỉnh sau này.
