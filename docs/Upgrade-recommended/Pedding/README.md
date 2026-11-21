# Công việc còn mở theo đề bài OISMS

> **⚠️ MUST DO — UPDATE BEFORE FINAL:** [Danh sách việc bắt buộc trước khi nộp bản final](./MUST-DO-UPDATE-BEFORE-FINAL.md) (lập 21/11/2026).

Mục 5.1 của checklist còn **Gói 5**. Gói 1 đã đóng toàn bộ tiêu chí ngày 14/11/2026 ([Done](../Done/goi-1-phan-quyen-va-kien-truc.md)), Gói 3 đóng ngày 15/11/2026 ([Done](../Done/goi-3-vong-doi-don-hang-va-hangfire.md)), Gói 4 triển khai xong ngày 15/11/2026 ([Done](../Done/goi-4-in-chung-tu-va-thong-bao-pos.md), còn bản in mẫu trên trình duyệt). Gói 2 đã triển khai các đầu việc chính, với một số tiêu chí chi tiết còn mở. Mỗi tài liệu dưới đây ghi hiện trạng, đầu việc và bằng chứng cần có để đóng mục. [Checklist](../../../checklist.md) · [Năm gói trong đề bài](../../08-sprints/sprint-2/DE_BAI_OISM.md#5-các-gói-công-việc-trong-pdf-trang-5-6)

## Gói 5 còn mở

[Plan triển khai Gói 5](./goi-5-plan-trien-khai.md) (15/11/2026, đã chốt Q1–Q13; mọi test và phép đo chạy local, không đo trên AWS).

| Công việc | Mã hoặc hồ sơ |
| --- | --- |
| [Lọc báo cáo doanh thu theo SKU](./goi-5-bao-cao-doanh-thu-theo-sku.md) | `FR-REP-01` |
| [Đánh giá dự báo nhập hàng](./goi-5-danh-gia-du-bao-nhap-hang.md) | `FR-AI-01` / CS-08 |
| [Kiểm chứng NFR](./goi-5-kiem-chung-nfr.md) | Hiệu năng, HTTPS/TLS, uptime, thao tác POS và coverage |
| [Hoàn thiện hồ sơ bàn giao](./goi-5-ho-so-ban-giao.md) | URS/RTM, kiến trúc, SDD và hướng dẫn sử dụng |

## Tiêu chí còn mở thuộc Gói 2

| Công việc | Mã hoặc hồ sơ |
| --- | --- |
| [Gói 2 — Quét mã vạch khi kiểm kê](./goi-2-quet-ma-vach-kiem-ke.md) — **không làm phần quét** (21/11/2026, không có thiết bị quét); vẫn làm biên bản E2E kho | `FR-PROD-03`, bằng chứng E2E kho |

## Upgrade 12 — Đơn mua và cấu hình giá (feedback 19/11/2026)

[Đặc tả đã chốt](./12-don-mua-va-cau-hinh-gia.md): bỏ hóa đơn NCC, thêm trạng thái `PendingApproval` cho đơn mua, ẩn giá mua và giá vốn với Staff, cấu hình giá bán (giữ giá cũ / WAC + lãi / giá mới) áp dụng khi nhập kho. **Gói 1 đã xong (19/11/2026):** đơn mua `PendingApproval`, phân bổ phí và chiết khấu vào giá vốn, che giá với Staff trên đơn mua và phiếu nhập, gỡ hóa đơn NCC ([báo cáo](../../report-after-upgrade/don-mua-pending-approval-u12.md)). **Gói 4 (cấu hình giá bán khi nhập kho, lý do đổi giá, xung đột Q19) và Gói 5 (che giá dùng chung, test quét mọi route GET) xong 21/11/2026** ([báo cáo G4](../../report-after-upgrade/cau-hinh-gia-ban-u12-g4.md), [báo cáo G5](../../report-after-upgrade/che-gia-dung-chung-u12-g5.md)).

## Upgrade 13 — Tồn kho theo kỳ (feedback 19/11/2026)

[Đặc tả đã chốt](./13-ton-kho-theo-ky.md): tab "Tồn theo kỳ" (ngày, tháng, khoảng tùy chọn) với đầu kỳ, nhập, xuất, cuối kỳ theo chi nhánh; thẻ kho; xuất Excel; Staff chỉ xem số lượng; phiếu xuất kho bán hàng theo mẫu 02-VT, lưu thành chứng từ gắn với từng dòng sổ `OUT` (thêm 2 bảng, lập cùng transaction checkout/confirm), bảng kê POS theo ngày, in hàng loạt đơn TMĐT. **Đã triển khai 19–21/11/2026** ([phiếu xuất](../../report-after-upgrade/phieu-xuat-kho-u13.md)).

**Phân công triển khai Upgrade 12 + 13:** [OISMS-66-lan2-phan-cong.md](./OISMS-66-lan2-phan-cong.md) (5 gói G1–G5, 3 người, lịch theo đợt).

## Nâng cấp 10

- **01 — Mua hàng, nhập kho và giá vốn** đã xong cả 01-A và 01-B (12/11/2026), đặc tả chuyển sang [Done](../Done/01-mua-hang-nhap-kho-gia-von.md). Câu hỏi còn treo: giữ WAC hay chuyển FIFO/giá đích danh. [Báo cáo 01-B](../../report-after-upgrade/hoa-don-nha-cung-cap-u01b.md). Phần 01-B đã bị Upgrade 12 thay thế ngày 19/11/2026.
- **10 — Đăng nhập SĐT và quên mật khẩu** đã xong giai đoạn 1 và 2 (15/11/2026), đặc tả chuyển sang [Done](../Done/10-quen-mat-khau-dang-nhap-sdt.md). Còn treo giai đoạn 3 (OTP SMS, cần nhà cung cấp trả phí). [Báo cáo](../../report-after-upgrade/quen-mat-khau-dang-nhap-sdt-u10.md)

## Việc liên quan

- **Webhook xác nhận VietQR tự động.** Upgrade 07 giai đoạn C, tùy chọn đang tạm gác. [Kế hoạch VietQR](../Done/07-thanh-toan-qr-vietqr.md)
- **Kiểm chứng hồi quy POS.** [BF-019](../../bug-fixaffter/errors/BF-019-pos-nhan-http-200-sai-contract-la.md) · [BF-020](../../bug-fixaffter/errors/BF-020-pos-rieng-chua-khoa-gio-va-ngu.md)

## Lỗi và quyết định còn mở

Các mục dưới đây không thuộc gói nào ở trên nhưng vẫn mở trong [checklist lỗi](../../bug-fixaffter/CHECKLIST.md).

| Mục | Ưu tiên | Hiện trạng |
| --- | --- | --- |
| [BF-024](../../bug-fixaffter/errors/BF-024-tim-kiem-phieu-nhap-khong-mo-dung-phieu.md) — Tìm kiếm toàn hệ thống (CS-04) | P2 | Đã sửa 21/11/2026 (FIN-I). |
| [BF-025](../../bug-fixaffter/errors/BF-025-integration-test-crlf-tren-windows.md) — Test AC01 trên Windows | P2 | Test fail do CRLF, chỉ ảnh hưởng môi trường kiểm thử. Chưa sửa. |
| [BF-026](../../bug-fixaffter/errors/BF-026-health-ready-lo-thong-tin-db.md) — `/health/ready` công khai | P3 | Đã sửa 21/11/2026 ở Caddyfile (FIN-E): trả 403 từ Internet. Có hiệu lực trên bản demo sau khi deploy. |
| [BF-027](../../bug-fixaffter/errors/BF-027-tong-so-luong-bao-cao-cong-khac-don-vi.md) — `total*Quantity` trong báo cáo | P3 | Đã sửa 21/11/2026 (FIN-I): trả `null` khi khác đơn vị gốc. |
| CS-07 — Tổng quan theo thời gian | — | KPI và biểu đồ lấy dữ liệu thật nhưng là số lũy kế; API chưa lọc theo khoảng thời gian. [Báo cáo frontend](../../report-after-upgrade/frontend-coming-soon-va-upgrade.md) |

Quyết định cần nhóm chốt:

- [ ] **Màn Handoff:** bỏ đặc tả [07-handoff-screens.md](../../11-ui-screens/07-handoff-screens.md) hay làm lại màn hình.
- [ ] **Số lẻ ở đơn vị đếm:** đơn vị mặc định cho phép 3 chữ số lẻ nên POS nhận số lẻ với "Cái". Chốt có cấm hay không.
- [ ] **BF-021:** [checklist lỗi](../../bug-fixaffter/CHECKLIST.md) còn ghi chưa sửa, nhưng [ma trận RBAC](../../01-users-and-rbac/02-rbac-matrix.md#21-lệch-giữa-yêu-cầu-và-code-hiện-tại-đối-chiếu-06112026) ghi đã sửa ở CS-01 P2 và `ReportService` trả `null` giá vốn, lãi gộp cho Staff. Cần xác nhận và đóng mục trong checklist lỗi.
