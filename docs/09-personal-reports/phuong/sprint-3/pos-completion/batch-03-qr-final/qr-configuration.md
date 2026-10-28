# Cấu hình QR theo cửa hàng và chi nhánh

Quyết định ngày 28/10/2026: tài khoản nhận tiền riêng từng chi nhánh và phải khớp tenant đang đăng nhập. Không có tài khoản mặc định. Cấu hình thiếu hoặc sai sẽ ẩn QR và khóa checkout bằng QR; tiền mặt vẫn dùng bình thường.

Nguồn cấu hình ưu tiên là `window.__OISMS_POS_BANK_CONFIGS__`, một mảng được deployment inject trước khi tải module ứng dụng:

```js
window.__OISMS_POS_BANK_CONFIGS__ = [{
  version: 1,
  tenantId: '<tenant UUID>',
  branchId: '<branch UUID>',
  bankConfig: {
    bankBin: '<BIN ngân hàng 6 chữ số>',
    bankName: '<Tên ngân hàng>',
    accountNumber: '<Tài khoản thật do chủ cửa hàng cung cấp>',
    accountName: '<Tên chủ tài khoản>',
    isTestFixture: false
  }
}];
```

Nếu deployment đã inject mảng, nó là nguồn quyết định: không tìm thấy đúng context thì trả về thiếu cấu hình, không fallback xuống localStorage. Khi không inject, POS đọc envelope cùng cấu trúc tại key `pos_bank_config_v1:${encodeURIComponent(tenantId)}:${encodeURIComponent(branchId)}` trong localStorage của origin POS. Cấu hình phải được người quản trị triển khai/cung cấp; B03 chưa thêm màn hình quản lý hay API lưu cấu hình ngân hàng.

Cấu hình global cũ `pos_bank_config`, `__OISMS_POS_BANK_CONFIG__` và các biến `VITE_POS_DEFAULT_BANK_*` không còn được dùng. Khi nâng cấp, phải chuyển từng tài khoản sang envelope đúng tenant/branch; không tự chuyển cấu hình global vì không xác định được người sở hữu.

QR đã hiển thị giữ nguyên requestKey và snapshot người nhận trong cùng ý định/giỏ hàng. Đóng/mở modal hoặc đổi tab thanh toán không đổi identity. Giỏ/giá/số lượng đổi sẽ tạo ý định mới. Sau khi gửi mà kết quả chưa rõ, pending lưu cả payload và snapshot QR trong sessionStorage theo tenant/user/branch; thay cấu hình không đổi người nhận của pending. Đơn legacy không có snapshot chỉ cho retry payload cũ và cảnh báo, không dựng QR sang tài khoản mới. Xóa dữ liệu trình duyệt hoặc đóng phiên có thể mất sessionStorage; không coi nó là hệ thống đối soát ngân hàng.

Thu ngân phải kiểm tra tiền đã vào qua ứng dụng/SMS ngân hàng rồi mới xác nhận checkout. Hiển thị/quét ảnh QR hoặc hết thời gian không xác nhận thanh toán. QuickLink là dịch vụ ảnh bên ngoài; module có kiểm tra payload/TLV/CRC, nhưng B03 không cung cấp webhook tự động đối soát, không kiểm chứng quyền sở hữu tài khoản ngân hàng và không thử chuyển tiền thật. Tài khoản fixture luôn ghi `isTestFixture: true` và UI hiển thị **DỮ LIỆU KIỂM THỬ — không chuyển tiền thật**.
