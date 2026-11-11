---
id: ma-loi-vuot-so-luong-phieu-mua
title: Mã lỗi vượt số lượng mua PURCHASE_ORDER_OVER_RECEIPT
roles: [Owner, Staff]
keywords: [vượt số lượng mua, vuot so luong mua, over receipt, phiếu mua hàng, phieu mua hang, nhận dư hàng, nhan du hang]
source: docs/12-error-and-status-codes/02-error-codes-catalog.md#23a-phân-hệ-phiếu-mua-hàng-purchase_order-oisms-47-u01a
---
Mã lỗi PURCHASE_ORDER_OVER_RECEIPT (HTTP 409) phát sinh khi xác nhận phiếu nhập kho mà số lượng thực nhận trên một dòng hàng vượt quá số lượng còn được phép nhận của phiếu mua hàng tương ứng. Nguyên nhân thường do nhân viên nhập kho gõ nhầm số lượng, hoặc một phiếu nhập kho khác đã tiếp nhận hết phần số lượng còn lại trước đó. Để khắc phục sự cố này, nhân viên kho cần tải lại thông tin phiếu mua hàng để kiểm tra số lượng còn lại được phép nhận trên từng dòng. Nếu số lượng giao thực tế từ nhà cung cấp thực sự nhiều hơn so với hợp đồng ban đầu, nhân viên cần liên hệ để lập thêm một phiếu mua hàng bổ sung.
