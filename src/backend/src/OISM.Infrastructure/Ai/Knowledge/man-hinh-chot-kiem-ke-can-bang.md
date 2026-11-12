---
id: man-hinh-chot-kiem-ke-can-bang
title: Phê duyệt chốt kiểm kê cân bằng kho
roles: [Owner]
keywords: [chốt kiểm kê, chot kiem ke, phê duyệt kiểm kê, phe duyet kiem ke, cân bằng tồn, can bang ton]
source: docs/Upgrade-recommended/Done/03-quy-dinh-kiem-ke.md#quyết-định-đã-chốt
---
Phê duyệt chốt phiếu kiểm kê để cân bằng tồn kho là thẩm quyền của Chủ cửa hàng (Owner). Nhân viên quản lý sau khi đếm xong sẽ nộp phiếu để chờ duyệt. Owner kiểm tra bảng chênh lệch giữa số đếm thực tế và số dư sổ sách. Nếu thấy số liệu chưa chuẩn xác, Owner có quyền yêu cầu đếm lại các mặt hàng nghi ngờ. Khi bấm phê duyệt chốt kiểm kê, hệ thống thực hiện giao dịch nguyên tử: tự động sinh các bút toán điều chỉnh vào sổ cái tồn kho và cập nhật số dư tồn thực tế cho khớp chính xác với số đếm thực tế. Nếu cửa hàng có từ hai Owner trở lên, Owner trực tiếp tham gia ghi số đếm không được phép tự duyệt phiếu của mình.
