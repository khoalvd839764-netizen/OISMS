---
id: gia-von-binh-quan-gia-quyen
title: Giá vốn bình quân gia quyền khi nhập kho
roles: [Owner]
keywords: [giá vốn, gia von, bình quân gia quyền, binh quan gia quyen, wac, công thức giá vốn, cong thuc gia von]
source: docs/00-overview/02-core-domain-concepts.md#41-công-thức-tính-lại-giá-vốn-khi-nhập-kho-fr-cost-01
---
Hệ thống tính toán giá vốn hàng bán theo phương pháp bình quân gia quyền di động (Weighted Average Cost - WAC). Thông tin giá vốn là dữ liệu nhạy cảm chỉ dành cho Chủ cửa hàng (Owner). Tại thời điểm xác nhận phiếu nhập hàng từ nhà cung cấp, hệ thống tự động tính lại đơn giá vốn của biến thể SKU theo công thức: Giá vốn mới bằng tổng giá trị tồn kho cũ cộng giá trị lô hàng mới nhập, chia cho tổng số lượng tồn kho cũ cộng số lượng mới nhập. Với các mặt hàng có nhiều đơn vị tính, số lượng và giá vốn đưa vào công thức luôn được quy đổi chuẩn xác về đơn vị tính gốc của SKU trước khi tính toán, bảo đảm số liệu nhất quán.
