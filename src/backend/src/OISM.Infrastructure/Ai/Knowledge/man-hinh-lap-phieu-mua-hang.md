---
id: man-hinh-lap-phieu-mua-hang
title: Lập và sửa nháp phiếu mua hàng
roles: [Owner, Staff]
keywords: [phiếu mua hàng, phieu mua hang, đề nghị mua, de nghi mua, đặt hàng ncc, dat hang ncc, đơn mua, don mua, purchase order]
source: docs/Upgrade-recommended/Done/01-mua-hang-nhap-kho-gia-von.md#luồng-đề-xuất
---
Vào mục Đơn mua (/purchase-orders) để lập đơn mua. Staff tạo đề nghị mua ở trạng thái Nháp (Draft) chỉ gồm chi nhánh nhận hàng, SKU, đơn vị và số lượng; Staff không nhập nhà cung cấp, không nhập giá. Staff được sửa hoặc hủy Draft của mình, xong bấm Gửi để chuyển sang Chờ duyệt (PendingApproval); từ đó Staff không sửa được nữa. Owner tự tạo đơn thì đơn vào thẳng PendingApproval. Ở PendingApproval, Owner chọn nhà cung cấp, ngày đặt hàng (mặc định hôm nay, không chọn ngày tương lai), đơn giá từng dòng, phí vận chuyển, chiết khấu thương mại và VAT. Màn hình gợi ý giá của lần mua đã duyệt gần nhất cùng nhà cung cấp, SKU và đơn vị, kèm giá vốn bình quân hiện tại để so. Phí vận chuyển cộng vào, chiết khấu trừ khỏi giá vốn, chia theo tỷ lệ số lượng nhân đơn giá; VAT chỉ vào tổng thanh toán. Staff không thấy giá, phí và VAT trên đơn mua.
