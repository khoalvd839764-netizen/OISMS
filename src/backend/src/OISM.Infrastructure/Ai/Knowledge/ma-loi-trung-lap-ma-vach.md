---
id: ma-loi-trung-lap-ma-vach
title: Mã lỗi trùng lặp mã vạch DUPLICATE_BARCODE
roles: [Owner, Staff]
keywords: [trùng mã vạch, trung ma vach, duplicate barcode, trùng barcode, trung barcode, mã ean, ma ean]
source: docs/12-error-and-status-codes/02-error-codes-catalog.md#22-phân-hệ-danh-mục-sản-phẩm--biến-thể-sku-prod
---
Mã lỗi DUPLICATE_BARCODE (HTTP 409) phát sinh khi người dùng thêm mới hoặc chỉnh sửa biến thể SKU hoặc quy cách đơn vị tính mà nhập mã vạch đã tồn tại trước đó trong cùng một cửa hàng. Quy tắc tính duy nhất của mã vạch áp dụng trên toàn bộ danh mục SKU và các đơn vị tính quy cách, kể cả đối với các quy cách khác của chính SKU đó. Để khắc phục sự cố này trên giao diện, người dùng cần kiểm tra lại mã vạch thực tế in trên bao bì sản phẩm để tránh nhập nhầm, nhập một mã vạch khác chưa từng được sử dụng, hoặc bấm nút Tự động sinh mã EAN-13 để hệ thống tự cấp một mã vạch chuẩn quốc gia không trùng lặp.
