# 10. Đặc Tả Màn Hình Kết Nối Sàn Và App Giả Lập Đơn Sàn

| Thông tin | Chi tiết |
| :--- | :--- |
| **Mã tài liệu** | `DOC-SCR-10` |
| **Phiên bản** | `v1.0.0` |
| **Ngày hiệu lực** | `2026-11-06` |
| **Route Admin** | `/shop-integrations`, `/admin/shop-integrations` |
| **App giả lập** | Local `http://localhost:5175`, deploy `https://<SIM_DOMAIN>` |
| **Quyền truy cập** | Màn Admin: chỉ `Owner`. App giả lập: không đăng nhập, xác thực bằng secret |
| **Source code** | `src/frontend/admin-dashboard/src/pages/ShopIntegrationsPage.tsx`, `src/frontend/admin-dashboard/src/services/shopIntegrationService.ts`, `src/frontend/marketplace-simulator/src/App.tsx` |
| **API** | `/api/shop-integrations`, `GET /api/webhooks/simulator/whoami`, `POST /api/webhooks/simulator/{channel}` |
| **Tài liệu chức năng** | [`DOC-SIM-14`](../14-webhook-simulator/README.md) |

---

## 1. Màn "Kết nối sàn" (Admin)

Menu: mục **Kết nối sàn** ở cuối thanh điều hướng, ngay dưới **Nhân viên & chi nhánh**. Staff và Cashier không thấy mục này; vào thẳng route sẽ bị chuyển sang `/403`.

### 1.1. Bố cục

| Khu vực | Nội dung |
| :--- | :--- |
| Tiêu đề | "Kết nối sàn", mô tả ngắn. Nút **Mở app giả lập** (mở tab mới, chỉ hiện khi có `VITE_SIMULATOR_URL` hoặc chạy dev) và nút **Tạo kết nối** |
| Thẻ hướng dẫn | 4 bước: tạo kết nối, copy secret (chỉ hiện một lần), dán vào app giả lập, đổi secret khi lỡ mất |
| Bảng kết nối | Cột Gian hàng (sàn và mã gian hàng), Chi nhánh xử lý đơn, Trạng thái (`Đang nhận đơn` hoặc `Đã tạm dừng`), Cập nhật, Thao tác (**Sửa**, **Đổi secret**) |
| Trạng thái rỗng | "Chưa có kết nối sàn" kèm gợi ý tạo kết nối đầu tiên |
| Lỗi tải | Hộp đỏ kèm nút **Thử lại** |

Bảng không bao giờ hiển thị secret hay bản băm; API danh sách cũng không trả các trường này.

### 1.2. Hộp thoại Tạo kết nối

| Trường | Ràng buộc |
| :--- | :--- |
| Sàn | `Shopee`, `TikTok Shop`, `Lazada` |
| Mã gian hàng trên sàn | Bắt buộc, tối đa 100 ký tự |
| Chi nhánh xử lý đơn | Chỉ liệt kê chi nhánh đang hoạt động. Không có chi nhánh nào thì báo tạo chi nhánh trước và khóa nút tạo |

Nút **Tạo và lấy secret** gọi `POST /api/shop-integrations`. Lỗi từ backend hiện trong hộp thoại, ví dụ 409 "Gian hàng ... đã được kết nối". Thành công thì đóng hộp thoại tạo và mở hộp thoại secret.

### 1.3. Hộp thoại Secret

Dùng chung cho "Đã tạo kết nối" và "Đã đổi secret".

- Ô secret chỉ đọc, chữ đơn cách, bấm vào là bôi đen toàn bộ. Nút **Copy** ghi vào clipboard và đổi thành "Đã copy".
- Cảnh báo màu cam: secret chỉ hiện một lần, đóng hộp thoại là không xem lại được.
- Ô **Tôi đã lưu secret**. Copy thành công thì ô này tự được đánh dấu.
- Nút **Xong** chỉ bật khi đã đánh dấu. Bấm X khi chưa đánh dấu thì hộp thoại không đóng mà hiện nhắc "Copy hoặc đánh dấu ... trước khi đóng".
- Có link sang app giả lập khi biết địa chỉ app.

Secret chỉ nằm trong state của hộp thoại; đóng là mất, không lưu vào `localStorage` hay URL.

### 1.4. Hộp thoại Sửa

- Đổi chi nhánh xử lý đơn. Danh sách gồm các chi nhánh đang hoạt động và chi nhánh hiện tại, kể cả khi chi nhánh đó đã tắt.
- Ô **Đang nhận đơn**. Bỏ chọn thì webhook và `whoami` dùng secret của kết nối này trả 401.
- Không đổi được sàn và mã gian hàng; muốn đổi thì tạo kết nối mới.

### 1.5. Hộp thoại Đổi secret

Hỏi xác nhận, nói rõ secret cũ hết hiệu lực ngay và nơi đang dùng secret cũ sẽ nhận 401. Nút **Đổi secret** màu đỏ gọi `POST /api/shop-integrations/{id}/rotate-secret`, thành công thì mở hộp thoại secret.

---

## 2. App giả lập đơn sàn

App riêng `marketplace-simulator`, không đăng nhập, chỉ gọi `/api/webhooks/*`.

### 2.1. Kết nối gian hàng

- Ô **Dán webhook secret** (ẩn ký tự, có nút hiện/ẩn) và nút **Thêm** (hoặc Enter).
- Bấm Thêm thì app gọi `whoami`. Thành công: thêm vào danh sách và chọn luôn. Thất bại: hiện thông báo của backend, ví dụ "Webhook Secret không hợp lệ.".
- Mỗi kết nối hiện "Sàn · Mã gian hàng" và tên chi nhánh. Chi nhánh đã tắt thì hiện chữ vàng "chi nhánh đã tắt, đơn sẽ bị từ chối".
- Nút X bỏ kết nối khỏi danh sách. Danh sách chỉ trong bộ nhớ tab.
- Khi chưa có kết nối: khung hướng dẫn 3 bước lấy secret trong Admin, kèm link "mở trang đó" tới `/shop-integrations` của Admin (dùng `VITE_ADMIN_URL`; dev mặc định `http://localhost:5173`).

### 2.2. Đơn hàng, Gửi, Nhật ký

| Khu vực | Nội dung |
| :--- | :--- |
| Đơn hàng | Khách hàng, điện thoại, địa chỉ; các dòng Mã SKU, SL, Đơn giá; tổng tiền gửi lên; danh sách lỗi nhập liệu màu vàng |
| Gửi | **Gửi đơn mới**, **Gửi lại** (mong đợi 200), **Đổi nội dung** (mong đợi 409), **Bắn** N đơn (1–200), tùy chọn dùng chung một mã đơn; dòng tóm tắt số kết quả theo status |
| Nhật ký | Giờ, Loại, Gian hàng, Mã đơn sàn, HTTP (xanh lá 201, xanh dương 200, vàng 4xx, đỏ 5xx), Kết quả, ms. Bấm một dòng để xem JSON request và response |

Bố cục hai cột trên màn rộng, một cột trên điện thoại; không cuộn ngang ở 375px.
