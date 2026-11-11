---
id: chup-gia-von-lich-su-khi-ban
title: Chụp giá vốn lịch sử tại thời điểm xuất bán
roles: [Owner]
keywords: [chụp giá vốn, chup gia von, giá vốn lịch sử, gia von lich su, bảo toàn lợi nhuận, bao toan loi nhuan]
source: docs/00-overview/02-core-domain-concepts.md#42-chụp-giá-vốn-tại-thời-điểm-xuất-bán-fr-cost-02
---
Để bảo toàn tính chuẩn xác của báo cáo tài chính và lợi nhuận, hệ thống áp dụng cơ chế chụp giá vốn lịch sử (Cost Price Snapshot). Khi một đơn hàng chuyển sang trạng thái đã duyệt (Confirmed) hoặc được thanh toán hoàn tất tại quầy POS, hệ thống sẽ chụp lại nguyên vẹn giá vốn bình quân gia quyền hiện hành của SKU vào từng dòng chi tiết đơn hàng. Nhờ cơ chế này, những lần nhập hàng sau với đơn giá biến động làm thay đổi giá vốn bình quân hiện tại của sản phẩm sẽ không bao giờ làm sai lệch giá vốn và lợi nhuận gộp của các đơn hàng đã xuất bán trong quá khứ. Đây là nguyên tắc kế toán cốt lõi giúp số liệu luôn minh bạch.
