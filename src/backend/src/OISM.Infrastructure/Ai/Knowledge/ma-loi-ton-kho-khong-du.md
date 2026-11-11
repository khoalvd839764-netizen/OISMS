---
id: ma-loi-ton-kho-khong-du
title: Mã lỗi tồn kho không đủ INSUFFICIENT_STOCK
roles: [Owner, Staff]
keywords: [lỗi tồn kho, loi ton kho, không đủ tồn, khong du ton, insufficient stock, hết hàng, het hang]
source: docs/12-error-and-status-codes/02-error-codes-catalog.md#23-phân-hệ-sổ-cái-tồn-kho--giữ-chỗ-inv--rse
---
Mã lỗi INSUFFICIENT_STOCK (HTTP 409) phát sinh khi số lượng tồn khả dụng (bằng tồn thực tế trừ đi số lượng đang giữ chỗ) của mặt hàng không đủ để đáp ứng số lượng yêu cầu khi bán lẻ tại quầy POS, duyệt đơn hàng hoặc tạo phiếu xuất chuyển kho. Thông báo mẫu hiển thị chi tiết mã SKU, số lượng sản phẩm hiện còn khả dụng và số lượng mà người dùng đang yêu cầu. Để khắc phục lỗi này trên giao diện, nhân viên bán hàng cần thỏa thuận với khách để giảm số lượng sản phẩm cần mua, chuyển sang chi nhánh khác còn hàng tồn khả dụng để xuất đơn, hoặc nhân viên kho tiến hành lập phiếu nhập thêm hàng từ nhà cung cấp.
