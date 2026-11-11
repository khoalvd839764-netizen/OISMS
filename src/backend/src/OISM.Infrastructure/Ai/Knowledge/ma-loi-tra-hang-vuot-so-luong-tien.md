---
id: ma-loi-tra-hang-vuot-so-luong-tien
title: Mã lỗi vượt số lượng hoặc tiền hoàn trả hàng
roles: [Owner, Staff]
keywords: [lỗi trả hàng, loi tra hang, vượt số lượng trả, vuot so luong tra, tiền hoàn vượt, tien hoan vuot, return exceeded]
source: docs/12-error-and-status-codes/02-error-codes-catalog.md#26a-phân-hệ-trả-hàng-từ-khách-sales_returns-cs-01
---
Khi lập phiếu trả hàng từ khách, hệ thống có hai mã lỗi chặn vi phạm tài chính: Mã RETURN_QUANTITY_EXCEEDED (HTTP 422) phát sinh khi số lượng yêu cầu trả trên một dòng hàng vượt quá số lượng khách đã mua trừ đi các đợt đã trả thành công trước đó; hướng xử lý là giảm số lượng trả về đúng mức tối đa cho phép. Mã REFUND_AMOUNT_EXCEEDED (HTTP 422) phát sinh khi số tiền hoàn nhập cho khách cao hơn đơn giá nhân với số lượng trả hoặc vượt quá tổng giá trị thanh toán của đơn hàng gốc; hướng xử lý là điều chỉnh lại số tiền hoàn trả cho khách không vượt quá giá trị thực tế của lượng hàng được trả.
