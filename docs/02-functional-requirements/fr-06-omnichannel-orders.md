# FR-06: Trung Tâm Đơn Hàng Đa Kênh & Máy Trạng Thái (Centralized Order Hub)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-06` |
| **Phiên bản (Version)** | `v1.1.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-15` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-ORD-01..03` (Trang 2) |
| **Gói công việc (Task Package)** | Task Package 3 — Centralized Order Hub & Anti-Oversell Engine |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả chi tiết mô hình đơn hàng hợp nhất (`Canonical Order Model`), máy trạng thái và quy trình xử lý đơn. | `INIT-FR-06` |
| `v1.1.0` | `2026-11-15` | Nhóm dự án OISMS | Làm rõ `Draft` chỉ là trạng thái khởi tạo trong giao dịch (không lưu với đơn online) và bổ sung luồng giữ chỗ kỹ thuật POS qua `POST /api/inventory/reserve`. | `OISMS-59-G3A` |

---

## 1. Bảng Đặc Tả Chi Tiết Yêu Cầu (`FR-ORD`)

| Mã yêu cầu | Tên yêu cầu | Mô tả nghiệp vụ & Quy tắc kỹ thuật | Tiêu chí nghiệm thu (Acceptance Criteria) |
| :---: | :--- | :--- | :--- |
| **`FR-ORD-01`** | **Omnichannel Order Ingestion** | Chuẩn hóa đơn hàng từ đa kênh (Bán tại quầy `POS`, Tạo thủ công trên `Admin`, và `Webhook Simulator` từ Shopee/TikTok/Lazada) về một **Mô hình Đơn hàng Hợp nhất (`Canonical Order Model`)**. | - Lưu rõ nguồn kênh bán (`Channel`: `POS`, `ADMIN`, `SHOPEE`, `TIKTOK`, `LAZADA`) và mã đơn gốc từ sàn (`ExternalOrderId`). |
| **`FR-ORD-02`** | **Order State Machine** | Thực thi nghiêm ngặt vòng đời chuyển trạng thái đơn hàng: `Draft -> Reserved -> Confirmed -> Completed` (hoặc `Cancelled`). `Draft` chỉ là trạng thái khởi tạo trong giao dịch tạo đơn, **không lưu `Draft` riêng đối với đơn online** (`Admin`, `Shopee`, `TikTok`, `Lazada`): bước `Draft -> Reserved` (`Order.Reserve()`) và tạo giữ chỗ thực thi trong cùng một giao dịch nguyên tử (`NFR-SEC-02`, `FR-RSE-02`). | - Chặn mọi bước nhảy trạng thái trái phép (`409 INVALID_STATE_TRANSITION`, ví dụ: từ `Cancelled` sang `Confirmed`, từ `Reserved` sang `Completed`, xác nhận lần hai, hoặc hủy đơn `Confirmed`). |
| **`FR-ORD-03`** | **Order Processing** | Cho phép nhân viên (`Staff`/`Owner`) xem danh sách đơn hàng chờ xử lý, phê duyệt đơn (`Approve/Confirm`), in phiếu giao hàng (`Print Shipping Slips`) hoặc hủy đơn (`Cancel`). | - Giao diện Web Admin hiển thị danh sách đơn theo trạng thái, hỗ trợ duyệt đơn, hủy đơn và mở mẫu in phiếu giao hàng. |

---

## 2. Quy Ước Trạng Thái `Draft` & Luồng Giữ Chỗ Kỹ Thuật POS

- **Đơn hàng online (`Admin`, `Shopee`, `TikTok`, `Lazada`):** Không có API lưu đơn nháp (`Draft`). `OrderIntakeService` khởi tạo thực thể `Order` ở `Draft`, gọi `Order.Reserve()` để chuyển sang `Reserved` và ghi nhận `Reservation` (`Active`) trong cùng một database transaction, đảm bảo thao tác tạo đơn và giữ chỗ mang tính nguyên tử.
- **Luồng giữ chỗ kỹ thuật của POS (`POST /api/inventory/reserve`):** Khi gọi `POST /api/inventory/reserve` mà không truyền `OrderId`/`OrderItemId` (endpoint kỹ thuật không có frontend gọi, chủ yếu dùng cho kiểm thử hoặc giữ chỗ theo kho), `ReservationService` tự sinh một đơn `Channel = "POS"` ở trạng thái `Draft` kèm bản ghi `Reservation` ở trạng thái `Active`. Đơn này **giữ nguyên `Draft`** (không chuyển sang `Reserved`) vì `Order.CanCancel` và `Order.CanExpireReservations` của kênh `POS` chỉ cho phép hủy/hết hạn khi đơn ở `Draft` chưa thanh toán; nhờ đó `ReservationExpiryJob` vẫn thu hồi đúng số lượng `Reserved` và chuyển đơn sang `Cancelled` khi quá hạn TTL.
