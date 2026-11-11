---
id: man-hinh-chuyen-kho-hai-buoc
title: Thực hiện điều chuyển kho hai bước
roles: [Owner, Staff]
keywords: [chuyển kho, chuyen kho, điều chuyển, dieu chuyen, xuất chuyển, xuat chuyen, tiếp nhận kho, tiep nhan kho]
source: docs/11-ui-screens/04-inventory-screens.md#32-modal-chuyển-kho-2-bước-stock-transfer-modal
---
Vào mục Chuyển kho tại đường dẫn /inventory/transfers để điều phối hàng hóa giữa các kho. Bấm Tạo phiếu điều chuyển, chọn kho xuất và kho nhận (bắt buộc hai kho phải khác nhau), thêm danh sách SKU và số lượng cần chuyển. Quy trình diễn ra qua 2 bước độc lập: Bước 1 là Xuất kho gửi, kho xuất kiểm hàng và xác nhận xuất, hệ thống trừ tồn thực tế tại kho xuất, ghi sổ cái xuất và chuyển phiếu sang trạng thái đang chuyển (InTransit). Bước 2 là Tiếp nhận kho, nhân viên kho nhận kiểm đếm hàng thực tế tới nơi rồi bấm tiếp nhận phiếu để tăng tồn thực tế tại kho nhận và ghi sổ cái nhập hoàn tất chu trình.
