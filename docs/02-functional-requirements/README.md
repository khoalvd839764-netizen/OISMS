# 02. ĐẶC TẢ YÊU CẦU CHỨC NĂNG (FUNCTIONAL REQUIREMENTS)

| Thông tin kiểm soát thư mục | Chi tiết |
| :--- | :--- |
| **Mã phân hệ (Module ID)** | `MOD-FR-02` |
| **Phiên bản hiện hành** | `v1.2.0` |
| **Ngày cập nhật gần nhất** | `2026-11-12` |
| **Nguồn đối chiếu gốc** | `De bai CNPM.pdf` — Mục 3.2.c & 3.2.g (Trang 1, 2, 3, 5, 6) |

---

## Danh mục Yêu Cầu Chức Năng

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
| **11** | `DOC-FR-11` | [`fr-11-reports-analytics.md`](./fr-11-reports-analytics.md) | `FR-REP-01..03` | `v1.1.0` | Báo cáo Doanh thu, COGS, Lợi nhuận gộp, Giá trị tồn kho và Cảnh báo tồn kho tối thiểu. |
| **12** | `DOC-FR-12` | [`fr-12-ai-forecasting.md`](./fr-12-ai-forecasting.md) | `FR-AI-01` | `v1.0.0` | Dự báo nhu cầu tiêu thụ bằng AI và Gợi ý kế hoạch nhập lại hàng hóa. |
| **13** | `DOC-FR-13` | [`fr-13-ai-assistant.md`](./fr-13-ai-assistant.md) | `FR-AI-02` | `v1.0.0` | Trợ lý Admin hỏi đáp tiếng Việt và tóm tắt số liệu trong phạm vi quyền. |

---

## Hiện Trạng Triển Khai Theo Yêu Cầu (đối chiếu code 12/11/2026)

Các file `fr-*.md` đặc tả yêu cầu. Xem [checklist theo từng mã](../../checklist.md#3-yêu-cầu-chức-năng-functional-requirements) để đối chiếu mã nguồn, kiểm thử và các tiêu chí còn mở.

| Mã | Hiện trạng | Ghi chú |
| :--- | :--- | :--- |
| `FR-AUTH-01..02` | Đã có | Đăng ký tạo Tenant và Owner. Owner tạo chi nhánh/kho qua màn tổ chức. Đăng nhập bằng email hoặc SĐT; quên mật khẩu qua link email và Owner đặt lại hộ nhân viên (Upgrade 10, 15/11/2026). Chưa có OTP SMS. |
| `FR-AUTH-03` | Đã có, đã chốt | Đã đóng lệch RBAC (14/11/2026): che giá vốn `averageCost = null` với Cashier/Staff, ghi kho chỉ Owner, xóa catalog chỉ Owner. Xem [ma trận RBAC](../01-users-and-rbac/02-rbac-matrix.md#21-lệch-giữa-yêu-cầu-và-code-hiện-tại-đối-chiếu-06112026). |
| `FR-AUTH-04` | Đã có | Đã có phân công nhiều chi nhánh cho nhân viên (`user_branches`, CS-02, 14/11/2026). |
| `FR-PROD-01..04` | Đã có các luồng chính | Quét barcode đã có tại POS; phần quét barcode ở màn kiểm kê không làm theo quyết định 21/11/2026 (thiếu thiết bị). Giá theo bảng giá và phiếu đổi giá (Upgrade 04), có nhiều đơn vị tính (Upgrade 05). |
| `FR-INV-01..04` | Đã có | Phiếu mua → phiếu nhập (Upgrade 01-A, Upgrade 12), lịch/kỳ kiểm kê, duyệt chênh lệch (Upgrade 03). Trả hàng (`Return`, CS-01): hoàn thành API & UI, ghi sổ cái IN, tính lại WAC, khấu trừ doanh thu/giá vốn thuần. |
| `FR-COST-01..02` | Đã có | WAC theo đơn vị gốc (phân bổ phí theo VAS 02, tách VAT), chụp `CostPrice` khi Confirm/POS. |
| `FR-RSE-01..03` | Đã có | Khóa `pg_advisory_xact_lock` + `FOR UPDATE`. |
| `FR-ORD-01..03` | Đã có mô hình đa kênh và xử lý đơn | Đơn Admin/webhook bắt đầu ở `Reserved`. Có hóa đơn bán và truy vết (Upgrade 02). Đã có in phiếu giao hàng A4/80 mm (Gói 4, 15/11/2026). |
| `FR-POS-01..04` | Đã có checkout và tự động in biên nhận | Một ứng dụng `pos-app` (5174), Admin `/pos` chuyển hướng. Tự mở hộp thoại in biên nhận sau checkout thành công (Gói 4, 15/11/2026) và in phiếu xuất kho `02-VT` (Upgrade 14, 21/11/2026). |
| `FR-SIM-01..02` | Đã có | Simulator gửi 1–200 đơn đồng thời. Admin nhận thông báo đơn mới. POS nhận `BranchOrderCreated` theo chi nhánh kèm toast/âm thanh (Gói 4, 15/11/2026). |
| `FR-SIM-03` | Đã có đủ 3 nhóm job | Có job hết hạn giữ chỗ, job sinh kỳ kiểm kê và job tổng hợp báo cáo hằng ngày `oisms-daily-sales-summary` (15/11/2026). [Đặc tả Hangfire](./fr-10-background-hangfire.md) |
| `FR-REP-01..03` | Đã có đầy đủ báo cáo và cảnh báo tồn | Báo cáo theo ngày nghiệp vụ `UTC+07:00`, trừ hàng trả, phân bổ chiết khấu, giá vốn/lãi gộp chỉ Owner xem. Đã có lọc tối đa 20 SKU + `skuBreakdown` (21/11/2026). |
| `FR-AI-01` | Đã có API và bảng Admin | Đã đánh giá backtest (21/11/2026): WAPE `7,70%`, Recall `100%` (7/7) trên dữ liệu tổng hợp. |
| `FR-AI-02` | Đã có API và panel Admin, mặc định tắt ở local | Claude nghiệm thu 10/11/2026, PR #42 vào `develop` (bật trên bản demo). API AI dùng gói miễn phí hạn mức thấp (xem [fr-13](./fr-13-ai-assistant.md)). |

Trạng thái Coming Soon & Upgrade (cập nhật 23/11/2026): `CS-01..CS-08` đã hoàn thành (`CS-05` VietQR thủ công xong, webhook ngân hàng tự động tạm gác). `U01-B` đã được thay thế bởi `Upgrade 12` (19/11/2026); `Upgrade 13` (tồn kho theo kỳ) và `Upgrade 14` (in chứng từ `02-VT`) đã hoàn thành (21/11/2026). Chi tiết: [checklist.md](../../checklist.md).
