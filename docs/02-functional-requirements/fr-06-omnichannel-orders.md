# FR-06: Trung Tâm Đơn Hàng Đa Kênh & Máy Trạng Thái (Centralized Order Hub)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-FR-06` |
| **Phiên bản (Version)** | `v1.2.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-19` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-ORD-01..03` (Trang 2) |
| **Gói công việc (Task Package)** | Task Package 3 — Centralized Order Hub & Anti-Oversell Engine |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả chi tiết mô hình đơn hàng hợp nhất (`Canonical Order Model`), máy trạng thái và quy trình xử lý đơn. | `INIT-FR-06` |
| `v1.1.0` | `2026-11-15` | Nhóm dự án OISMS | Làm rõ `Draft` chỉ là trạng thái khởi tạo trong giao dịch (không lưu với đơn online) và bổ sung luồng giữ chỗ kỹ thuật POS qua `POST /api/inventory/reserve`. | `OISMS-59-G3A` |
| `v1.2.0` | `2026-11-19` | Nhóm dự án OISMS | Lập chứng từ Phiếu xuất kho 02-VT (`goods_issues`, `goods_issue_items`) trong cùng transaction xác nhận đơn, hỗ trợ in đơn lẻ, in hàng loạt và bảng kê POS theo ngày. | `OISMS-66-L2-G3` |

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

---

## 3. Phiếu Giao Hàng (`FR-ORD-03`, `CS-06`, `OISMS-59-G4`)

- Nút **In chứng từ** trong `OrderDetailDrawer` mở `ShippingSlipPrintModal` (chọn khổ **A4** hoặc **80mm**), hiển thị thông tin người nhận (tên, SĐT, địa chỉ giao), dòng hàng (số lượng + tên đơn vị), đơn giá, thành tiền và tổng thu (COD); **không render giá vốn (`costPrice` / `totalCogs`) với mọi vai trò**.
- **Điều kiện bật nút:** Chỉ bật với đơn kênh khác `POS` (`Admin`, `Shopee`, `TikTok`, `Lazada`) ở trạng thái `Confirmed` hoặc `Completed`. Đơn `POS` dùng chức năng in lại hóa đơn bán hàng; đơn `Reserved` / `Cancelled` bị vô hiệu hóa kèm `title` giải thích lý do.

---

## 4. Phiếu Xuất Kho 02-VT & In Hàng Loạt (`Upgrade 13`, `OISMS-66-L2-G3`)

- **Lập nguyên tử trong cùng transaction:** Khi xác nhận đơn (`POST /api/orders/{id}/confirm`), ngay sau khi ghi dòng sổ cái `OUT` và lập `SalesInvoice`, hệ thống lập chứng từ **Phiếu xuất kho (`goods_issues`, `goods_issue_items`)** trong cùng database transaction mà không thêm khóa mới hay đổi thứ tự khóa.
- **Đánh số theo kho xuất (`P5`, `Q13`):**
  - Đơn xuất từ 1 kho: tạo 1 phiếu mang số `PXK-<số hóa đơn>` (ví dụ `PXK-INV-20261119-103000-123`).
  - Đơn xuất từ nhiều kho: nhóm các dòng sổ `OUT` theo `WarehouseId` tăng dần (`OrderBy(g => g.Key)`), mỗi kho tạo 1 phiếu mang số `PXK-<số hóa đơn>-1`, `PXK-<số hóa đơn>-2`, ... kèm `warehouse_name_snapshot`.
- **Liên kết 1–1 với dòng sổ cái `OUT`:** Mỗi dòng `goods_issue_items` gắn 1–1 với dòng `inventory_transactions` (`UNIQUE (tenant_id, inventory_transaction_id)`), lưu `quantity` (theo đơn vị dòng đơn), `base_quantity` (theo đơn vị gốc) và `unit_cost = unit_cost` của dòng sổ cái.
- **In phiếu 02-VT, in hàng loạt và che giá theo vai trò:**
  - Màn Đơn hàng (`OrdersPage.tsx`) không tự bật hộp thoại in khi xác nhận đơn; người dùng bấm nút **In phiếu xuất kho** trên từng đơn `Confirmed`/`Completed` hoặc tích chọn nhiều đơn đã xác nhận để bấm **In phiếu xuất kho hàng loạt** (`GoodsIssuePrintModal` — Mẫu số `02-VT`, chữ ký *Người lập phiếu*, *Thủ kho*, *Người nhận hàng*).
  - `Owner` xem và in đầy đủ cột **Đơn giá vốn** và **Thành tiền vốn**; `Staff` nhận `unitCost = null`, `totalCost = null` từ API và bản in ẩn 2 cột giá trị vốn; `Cashier` bị chặn `403 Forbidden`.
