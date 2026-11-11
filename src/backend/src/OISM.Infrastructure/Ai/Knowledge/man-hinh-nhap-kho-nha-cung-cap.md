---
id: man-hinh-nhap-kho-nha-cung-cap
title: Tạo và xác nhận phiếu nhập kho nhà cung cấp
roles: [Owner, Staff]
keywords: [nhập kho, nhap kho, phiếu nhập, phieu nhap, nhà cung cấp, nha cung cap, xác nhận nhập, xac nhan nhap]
source: docs/11-ui-screens/04-inventory-screens.md#31-modal-nhập-kho-nhà-cung-cấp-goods-receipt-modal
---
Vào mục Kho và sổ kho rồi bấm Tạo phiếu nhập để tiếp nhận hàng từ nhà cung cấp. Người dùng chọn phiếu mua hàng đã được duyệt còn số lượng cần nhận, chọn kho tiếp nhận thuộc chi nhánh của phiếu mua. Kiểm tra danh sách mặt hàng, đơn vị giao dịch và số lượng thực nhận, bảo đảm không vượt quá số lượng còn lại trên phiếu mua. Khi lưu nháp, phiếu chưa tác động đến tồn kho hay giá vốn. Khi bấm Xác nhận nhập kho, hệ thống thực hiện kiểm tra nguyên tử: tăng số lượng tồn thực tế trong kho, ghi bút toán nhập vào sổ cái và tự động tính lại giá vốn bình quân gia quyền của SKU theo đơn vị gốc.
