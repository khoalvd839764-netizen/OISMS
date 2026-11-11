---
id: khai-niem-ton-kha-dung
title: Khái niệm và công thức tính tồn khả dụng
roles: [Owner, Staff]
keywords: [tồn khả dụng, ton kha dung, tồn thực tế, ton thuc te, tồn giữ chỗ, ton giu cho, công thức tồn, cong thuc ton]
source: docs/00-overview/02-core-domain-concepts.md#5-công-thức-tồn-khả-dụng--cơ-chế-chống-bán-vượt-anti-oversell--stock-reservation
---
Hệ thống OISMS quản lý chặt chẽ 3 chỉ số tồn kho cho từng biến thể SKU tại mỗi chi nhánh và kho hàng. Tồn thực tế (OnHand) là số lượng hàng hóa vật lý đang có trong kho. Tồn giữ chỗ (Reserved) là số lượng hàng đã được giữ lại cho các đơn hàng trực tuyến hoặc đơn Admin đang chờ duyệt. Tồn khả dụng (Available) là số lượng hàng thực tế sẵn sàng để tiếp tục bán, được tính theo công thức: Tồn khả dụng bằng Tồn thực tế trừ Tồn giữ chỗ. Khi tạo đơn hàng mới, hệ thống bắt buộc kiểm tra tồn khả dụng phải lớn hơn hoặc bằng số lượng đặt mua. Tồn khả dụng không bao giờ được phép âm, giúp loại trừ triệt để tình trạng bán vượt tồn kho.
