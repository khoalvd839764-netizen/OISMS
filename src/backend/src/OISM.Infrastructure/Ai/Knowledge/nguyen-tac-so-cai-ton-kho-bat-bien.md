---
id: nguyen-tac-so-cai-ton-kho-bat-bien
title: Nguyên tắc sổ cái tồn kho bất biến chỉ ghi thêm
roles: [Owner, Staff]
keywords: [sổ cái bất biến, so cai bat bien, chỉ ghi thêm, chi ghi them, append only, bút toán điều chỉnh, but toan dieu chinh]
source: docs/00-overview/02-core-domain-concepts.md#3-sổ-cái-tồn-kho-bất-biến-chỉ-ghi-thêm-append-only-inventory-ledger
---
Sổ cái tồn kho trong OISMS hoạt động theo nguyên tắc bất biến chỉ ghi thêm (Append-Only), tương tự như sổ cái ngân hàng. Mọi thao tác làm thay đổi số lượng tồn kho (nhập kho, xuất bán, trả hàng, chuyển kho, kiểm kê) đều bắt buộc phải ghi thành các dòng bút toán nhập hoặc xuất riêng biệt. Hệ thống nghiêm cấm tuyệt đối mọi hành vi chỉnh sửa hoặc xóa bỏ các dòng sổ cái đã ghi nhận. Không một người dùng nào có thể trực tiếp sửa con số tồn kho mà không thông qua giao dịch sổ cái. Nếu có sai sót phát sinh trong quá trình vận hành, người dùng bắt buộc phải tạo bút toán điều chỉnh bù hoặc đảo để cân bằng lại số liệu.
