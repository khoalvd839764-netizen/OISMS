# 11. Đặc Tả Màn Hình Tài Khoản Nhận Tiền (VietQR, CS-05)

| Thông tin | Chi tiết |
| :--- | :--- |
| **Mã tài liệu** | `DOC-SCR-11` |
| **Phiên bản** | `v1.0.0` |
| **Ngày hiệu lực** | `2026-11-11` |
| **Route Admin** | `/settings/payment-accounts`, `/admin/settings/payment-accounts` |
| **Quyền truy cập** | Màn quản lý: chỉ `Owner`. Danh sách tài khoản đang dùng (`/active`) cho `Owner`, `Staff`, `Cashier` để POS chọn khi thanh toán QR |
| **Source code** | `src/frontend/admin-dashboard/src/pages/PaymentAccountsPage.tsx`; POS dùng `src/frontend/pos-app/src/services/paymentAccountSource.ts`, `qrService.ts` |
| **API** | `GET /api/payment-accounts` (Owner), `GET /api/payment-accounts/active`, `POST /api/payment-accounts`, `PUT /api/payment-accounts/{id}`, `POST /api/payment-accounts/{id}/set-default`, `POST /api/payment-accounts/{id}/deactivate` |
| **Tài liệu chức năng** | [Kế hoạch 07 VietQR](../Upgrade-recommended/07-thanh-toan-qr-vietqr.md), [review PR 39](../report-after-upgrade/vietqr-cs05-review-pr39.md) |

---

## 1. Bố cục

| Khu vực | Nội dung |
| :--- | :--- |
| Tiêu đề | "Tài khoản nhận tiền", nút **Thêm tài khoản** |
| Thẻ quy tắc | Tài khoản dùng chung cho cả cửa hàng; mỗi cửa hàng có đúng một tài khoản mặc định, được chọn sẵn khi mở thanh toán QR; QR sinh theo chuẩn NAPAS 247 kèm số tiền và mã tham chiếu; tài khoản đã có giao dịch không xóa được, chỉ ngừng dùng |
| Bảng | Ngân hàng, Số tài khoản, Chủ tài khoản, Trạng thái (`Hoạt động` hoặc `Đã tắt`, kèm nhãn mặc định), Thao tác |
| Thao tác mỗi dòng | **Xem thử mã QR**, **Đặt làm tài khoản mặc định**, **Ngừng sử dụng** |
| Trạng thái rỗng | "Chưa có tài khoản nhận tiền nào" |

## 2. Hộp thoại Thêm tài khoản

| Trường | Ràng buộc |
| :--- | :--- |
| Ngân hàng thụ hưởng | Bắt buộc, chọn từ danh sách ngân hàng (mã BIN) |
| Số tài khoản | Bắt buộc, tối đa 19 ký tự |
| Chủ tài khoản | Bắt buộc |
| Đặt làm mặc định | Ô chọn "Đặt làm tài khoản mặc định cho quầy bán hàng POS" |

## 3. Liên quan tới POS

- Modal thanh toán ở POS gọi `GET /api/payment-accounts/active`, chọn sẵn tài khoản mặc định; chỉ một tài khoản thì hiện dạng chữ.
- Không có tài khoản nào thì nút QR bị khóa.
- Checkout gửi `paymentAccountId`; backend chụp thông tin người nhận vào `payments` để đối soát khi tài khoản đổi sau này.
- Thu ngân xác nhận đã nhận tiền thủ công. Xác nhận tự động qua webhook ngân hàng chưa làm.
