---
id: ma-loi-quy-cach-don-vi-tinh
title: Các mã lỗi về đơn vị tính và quy cách SKU
roles: [Owner, Staff]
keywords: [lỗi đơn vị tính, loi don vi tinh, đơn vị gốc khóa, don vi goc khoa, hệ số không hợp lệ, he so khong hop le]
source: docs/12-error-and-status-codes/02-error-codes-catalog.md#22-phân-hệ-danh-mục-sản-phẩm--biến-thể-sku-prod
---
Phân hệ đơn vị tính có các mã lỗi thường gặp khi cấu hình hàng hóa: Mã BASE_UNIT_LOCKED (HTTP 409) xảy ra khi cố ý thay đổi đơn vị tính gốc của SKU đã phát sinh giao dịch đơn hàng, phiếu nhập, sổ cái hoặc vẫn còn đơn vị quy đổi đang bật; hướng xử lý là giữ nguyên đơn vị gốc. Mã UNIT_GROUP_INCOMPATIBLE (HTTP 422) xuất hiện khi chọn đơn vị giao dịch khác nhóm đo lường với đơn vị gốc và không thuộc nhóm bao gói; hướng xử lý là chọn đơn vị cùng nhóm. Mã INVALID_UNIT_FACTOR (HTTP 400) xảy ra khi nhập hệ số quy đổi nhỏ hơn hoặc bằng 0; hướng xử lý là nhập hệ số dương đúng định dạng và không được tự làm tròn số.
