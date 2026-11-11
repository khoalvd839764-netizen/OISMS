---
id: co-che-giu-cho-ton-kho
title: Cơ chế giữ chỗ tồn kho và chống bán vượt
roles: [Owner, Staff]
keywords: [giữ chỗ, giu cho, chống bán vượt, chong ban vuot, khóa tồn kho, khoa ton kho, reservation]
source: docs/00-overview/02-core-domain-concepts.md#5-công-thức-tồn-khả-dụng--cơ-chế-chống-bán-vượt-anti-oversell--stock-reservation
---
Cơ chế giữ chỗ tồn kho là giải pháp bảo đảm tính toàn vẹn số liệu khi nhiều khách hàng cùng đặt mua trong các đợt cao điểm bán hàng. Khi một đơn hàng trực tuyến được tạo, hệ thống lập tức tăng số lượng giữ chỗ và giảm số lượng tồn khả dụng tương ứng, nhưng chưa trừ tồn thực tế và chưa ghi sổ cái. Nếu đơn hàng được nhân viên duyệt xuất kho, số lượng giữ chỗ được giải phóng, tồn thực tế bị trừ và hệ thống ghi bút toán xuất vào sổ cái. Nếu đơn hàng bị hủy hoặc quá thời hạn thanh toán (mặc định 15 phút), lượng hàng giữ chỗ sẽ được hoàn trả lại cho quỹ tồn khả dụng chung để các khách hàng khác tiếp tục đặt mua bình thường.
