# 02. ĐẶC TẢ YÊU CẦU CHỨC NĂNG (FUNCTIONAL REQUIREMENTS)

| Thông tin kiểm soát thư mục | Chi tiết |
| :--- | :--- |
| **Mã phân hệ (Module ID)** | `MOD-FR-02` |
| **Phiên bản hiện hành** | `v1.1.0` |
| **Ngày cập nhật gần nhất** | `2026-11-06` |
| **Nguồn đối chiếu gốc** | `De bai CNPM.pdf` — Mục 3.2.c & 3.2.g (Trang 1, 2, 3, 5, 6) |

---

## Danh mục 12 Module Yêu Cầu Chức Năng

| STT | Mã tài liệu | Tên file | Mã FR bao phủ | Phiên bản | Nội dung chính |
| :---: | :---: | :--- | :---: | :---: | :--- |
| **1** | `DOC-FR-01` | [`fr-01-auth-tenant.md`](./fr-01-auth-tenant.md) | `FR-AUTH-01..04` | `v1.0.0` | Đăng ký Tenant, Xác thực JWT, Phân quyền RBAC và Quản lý Chi nhánh/Kho. |
| **2** | `DOC-FR-02` | [`fr-02-product-catalog.md`](./fr-02-product-catalog.md) | `FR-PROD-01..04` | `v1.0.0` | Danh mục phân cấp, Thương hiệu, Sản phẩm/Biến thể SKU, Barcode EAN-13 và Bảng giá sỉ/lẻ. |
| **3** | `DOC-FR-03` | [`fr-03-inventory-ledger.md`](./fr-03-inventory-ledger.md) | `FR-INV-01..04` | `v1.0.0` | Sổ cái kho bất biến Append-Only, Nhập kho NCC, Chuyển kho 2 bước và Kiểm kê kho. |
| **4** | `DOC-FR-04` | [`fr-04-cost-wac.md`](./fr-04-cost-wac.md) | `FR-COST-01..02` | `v1.0.0` | Tính Giá vốn bình quân tự động (WAC) khi nhập kho và Chụp giá vốn lịch sử vào dòng đơn hàng. |
| **5** | `DOC-FR-05` | [`fr-05-stock-reservation.md`](./fr-05-stock-reservation.md) | `FR-RSE-01..03` | `v1.0.0` | Tồn khả dụng động (`available = on_hand - reserved`), Giữ chỗ, Trừ/Hoàn tồn và Khóa DB chống bán vượt. |
| **6** | `DOC-FR-06` | [`fr-06-omnichannel-orders.md`](./fr-06-omnichannel-orders.md) | `FR-ORD-01..03` | `v1.0.0` | Tiếp nhận đơn đa kênh vào Canonical Order Model, Order State Machine và Xử lý/In phiếu giao. |
| **7** | `DOC-FR-07` | [`fr-07-pos-checkout.md`](./fr-07-pos-checkout.md) | `FR-POS-01..04` | `v1.0.0` | Giao diện PWA POS, Kiểm soát tồn tại quầy, In hóa đơn nhiệt và Luồng Fast Checkout nguyên tử. |
| **8** | `DOC-FR-08` | [`fr-08-marketplace-sync.md`](./fr-08-marketplace-sync.md) | `FR-SIM-01` | `v1.0.0` | Công cụ giả lập gửi Webhook đơn hàng từ Shopee, TikTok Shop, Lazada. |
| **9** | `DOC-FR-09` | [`fr-09-realtime-signalr.md`](./fr-09-realtime-signalr.md) | `FR-SIM-02` | `v1.0.0` | Đẩy cảnh báo âm thanh/popup đơn hàng mới thời gian thực qua SignalR. |
| **10** | `DOC-FR-10` | [`fr-10-background-hangfire.md`](./fr-10-background-hangfire.md) | `FR-SIM-03` | `v1.0.0` | Xử lý tác vụ nền Hangfire: tự động hủy đơn giữ chỗ quá hạn và tổng hợp báo cáo hằng ngày (hiện mặc định tắt). |
| **11** | `DOC-FR-11` | [`fr-11-reports-analytics.md`](./fr-11-reports-analytics.md) | `FR-REP-01..03` | `v1.0.0` | Báo cáo Doanh thu, COGS, Lợi nhuận gộp, Giá trị tồn kho và Cảnh báo tồn kho tối thiểu. |
| **12** | `DOC-FR-12` | [`fr-12-ai-forecasting.md`](./fr-12-ai-forecasting.md) | `FR-AI-01` | `v1.0.0` | Dự báo nhu cầu tiêu thụ bằng AI và Gợi ý kế hoạch nhập lại hàng hóa. |

---

## Hiện Trạng Triển Khai Theo Yêu Cầu (đối chiếu code 06/11/2026)

Các file `fr-*.md` là **yêu cầu**. Bảng dưới tóm tắt mức hiện thực trong code; khi lệch, yêu cầu vẫn là chuẩn và cần nhóm quyết định.

| Mã | Hiện trạng | Ghi chú |
| :--- | :--- | :--- |
| `FR-AUTH-01..02` | Đã có | Đăng ký chỉ tạo Tenant + Owner; Quên mật khẩu là nút tắt có nhãn Coming soon. |
| `FR-AUTH-03` | Đã có, còn lệch | Xem [lệch RBAC](../01-users-and-rbac/02-rbac-matrix.md#21-lệch-giữa-yêu-cầu-và-code-hiện-tại-đối-chiếu-06112026). |
| `FR-AUTH-04` | Đã có | Phân công người dùng vào chi nhánh (phần của CS-02) chưa có. |
| `FR-PROD-01..04` | Đã có | Giá theo bảng giá thời gian + phiếu đổi giá (Upgrade 04), nhiều đơn vị tính (Upgrade 05). |
| `FR-INV-01..04` | Đã có | Phiếu mua → phiếu nhập (Upgrade 01-A), lịch/kỳ kiểm kê, duyệt chênh lệch (Upgrade 03). Trả hàng (`Return`, CS-01): backend đã hoàn thành API, ghi sổ cái IN, tính lại WAC, khấu trừ doanh thu/giá vốn thuần và truy vết chuỗi chứng từ (09/11/2026). |
| `FR-COST-01..02` | Đã có | WAC theo đơn vị gốc, chụp `CostPrice` khi Confirm/POS. |
| `FR-RSE-01..03` | Đã có | Khóa `pg_advisory_xact_lock` + `FOR UPDATE`. |
| `FR-ORD-01..03` | Đã có | Hóa đơn bán và truy vết chứng từ (Upgrade 02). |
| `FR-POS-01..04` | Đã có | Một giao diện POS trong `pos-app` (5174), chuyển từ Admin; `/pos` Admin chỉ chuyển hướng. |
| `FR-SIM-01..02` | Đã có; POS realtime tồn đạt 08/11/2026 | Webhook có chữ ký; POS nhận `StockLevelChanged`, đổi nhóm chi nhánh, lọc phiên cũ và refetch khi reconnect. Đã kiểm chứng backend/PostgreSQL thật và hai tab. Popup/âm thanh đơn mới trên POS nằm ngoài pha này; chưa đánh dấu toàn bộ FR-09 hoàn tất. |
| `FR-SIM-03` | Có code, mặc định tắt | Xem [fr-10](./fr-10-background-hangfire.md). |
| `FR-REP-01..03` | Đã có | Báo cáo theo ngày nghiệp vụ `UTC+07:00`, lọc nửa mở; khấu trừ hàng trả (`returnAmount`, `returnCogs`) ra số thuần; bảo mật giá vốn và lãi gộp chỉ Owner xem được. |
| `FR-AI-01` | Đã có API | Tab Gợi ý nhập hàng đang tạm ẩn trong `InventoryPage.tsx`, API vẫn chạy. |

Còn thiếu theo danh sách Coming Soon: CS-01 (trả hàng), CS-06, U01-B. CS-05 (VietQR) đã xong, chưa có xác nhận tự động qua webhook ngân hàng. Chi tiết: [báo cáo frontend](../report-after-upgrade/frontend-coming-soon-va-upgrade.md).
