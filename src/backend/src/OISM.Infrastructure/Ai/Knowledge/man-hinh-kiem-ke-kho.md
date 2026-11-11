---
id: man-hinh-kiem-ke-kho
title: Thực hiện đợt kiểm kê kho
roles: [Owner, Staff]
keywords: [kiểm kê, kiem ke, phiếu kiểm kê, phieu kiem ke, số đếm thực tế, so dem thuc te, đếm lại, dem lai]
source: docs/11-ui-screens/04-inventory-screens.md#33-modal-kiểm-kê--phát-hiện-sai-lệch-stock-count-modal
---
Vào mục Kiểm kê kho tại đường dẫn /inventory/counts để đối soát tồn thực tế với sổ sách. Người dùng lọc theo chi nhánh và kho hàng, tạo phiếu kiểm kê mới hoặc mở phiếu từ kỳ định kỳ. Nhân viên kiểm đếm nhập số lượng đếm được cho từng dòng SKU và lưu lại. Nếu phát hiện giao dịch phát sinh xen giữa làm thay đổi số dư sổ sách trong lúc đếm, hệ thống kích hoạt cơ chế phát hiện sai lệch sequence và cảnh báo các SKU bị lệch. Nhân viên thực hiện đếm lại (recount) cho các mặt hàng bị lệch để cập nhật mốc đối soát mới. Sau khi nhập đủ số đếm cho mọi mặt hàng, nhân viên nộp phiếu để chờ phê duyệt.
