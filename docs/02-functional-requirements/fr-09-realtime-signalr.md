# FR-09: Thông Báo Thời Gian Thực Qua SignalR (Real-time Push Notification)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-09` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-SIM-02` (Trang 3, 6) |
| **Gói công việc (Task Package)** | Task Package 4 — POS Front-end & Real-time Integration |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả cơ chế đẩy thông báo đơn hàng mới và cảnh báo tồn kho thời gian thực bằng SignalR. | `INIT-FR-09` |

---

## 1. Bảng Đặc Tả Chi Tiết Yêu Cầu (`FR-SIM-02`)

| Mã yêu cầu | Tên yêu cầu | Mô tả nghiệp vụ & Quy tắc kỹ thuật | Tiêu chí nghiệm thu (Acceptance Criteria) |
| :---: | :--- | :--- | :--- |
| **`FR-SIM-02`** | **Real-time Push Notification** | Sử dụng **ASP.NET Core SignalR** để đẩy cảnh báo âm thanh và thông báo nổi (`Sound/Popup Alerts`) ngay khi có đơn hàng mới hoặc biến động tồn kho trực tiếp tới màn hình `Admin Dashboard` và `POS` mà không cần tải lại trang (`No page refresh`). | - Cô lập kênh nhận thông báo theo `TenantId` (Nhóm SignalR `tenant_{TenantId}`), đảm bảo cửa hàng A không nhận thông báo đơn của cửa hàng B.<br/>- Hiển thị tức thì popup thông báo trên giao diện khi có đơn mới từ Webhook/POS. |

---

## 2. Hiện Thực Chi Tiết (Gói 4 — `OISMS-59-G4`, 15/11/2026)

- **Phạm vi chi nhánh (`BranchOrderCreated`):**
  - Đơn online mới (`Admin`, `Shopee`, `TikTok`, `Lazada`) phát song song `OrderCreated` (vào `tenant_{tenantId}` cho Admin) và `BranchOrderCreated` (vào `tenant_{tenantId}_branch_{branchId}` cho POS tại chi nhánh đó) sau khi DB transaction commit.
  - Payload `BranchOrderCreated` (`BranchOrderRealtimeDto`) chỉ gồm `orderId`, `orderNumber`, `branchId`, `channel`, `createdAt`, `items[{skuId, skuCode, productName, quantity, unitName}]`; tuyệt đối không gửi tổng tiền, tên khách hàng hay giá vốn.
  - `JoinBranchGroup(branchId)` kiểm tra phân công chi nhánh với `Cashier` qua `IBranchAccessService`: Cashier được gán chi nhánh sẽ bị từ chối (`HubException`) nếu vào chi nhánh khác; Cashier chưa được gán vẫn vào được mọi chi nhánh.
  - `Cashier` không vào nhóm `tenant_{tenantId}` khi kết nối (chỉ `Owner`/`Staff` vào), nên không nhận `OrderCreated`/`LowStockWarning` có tổng tiền, tên khách của mọi chi nhánh. POS chỉ nhận `StockLevelChanged` và `BranchOrderCreated` qua nhóm chi nhánh.
- **Đơn POS (`PosCheckoutService`):**
  - Sau khi commit giao dịch checkout thành công (không phải replay `RequestKey`), phát `OrderCreated` (`channel = "POS"`) vào `tenant_{tenantId}` để `OrdersPage` tự nạp lại và ghi nhận vào `NotificationCenter` ở Admin, **không phát `BranchOrderCreated`** và **Admin không phát âm thanh** với kênh `POS`.
- **Giao diện POS (`pos-app`):**
  - Toast góc dưới-trái tự ẩn, khử trùng theo `orderId`, không che giỏ hàng và không chiếm phím tắt; đổi chi nhánh xóa toast cũ.
  - `PosHeader` có nút bật/tắt tiếng (`localStorage`), và hiện cảnh báo `"có thể đã bỏ lỡ thông báo"` khi trạng thái realtime khác `connected` (không bù thông báo khi nối lại).
