# Công việc còn mở theo đề bài OISMS

Mục 5.1 của checklist còn **Gói 5**. Gói 1–4 đã triển khai các đầu việc chính, với một số tiêu chí chi tiết còn mở. Mỗi tài liệu dưới đây ghi hiện trạng, đầu việc và bằng chứng cần có để đóng mục. [Checklist](../../../checklist.md) · [Năm gói trong đề bài](../../08-sprints/sprint-2/DE_BAI_OISM.md#5-các-gói-công-việc-trong-pdf-trang-5-6)

## Gói 5 còn mở

| Công việc | Mã hoặc hồ sơ |
| --- | --- |
| [Lọc báo cáo doanh thu theo SKU](./goi-5-bao-cao-doanh-thu-theo-sku.md) | `FR-REP-01` |
| [Đánh giá dự báo nhập hàng](./goi-5-danh-gia-du-bao-nhap-hang.md) | `FR-AI-01` / CS-08 |
| [Kiểm chứng NFR](./goi-5-kiem-chung-nfr.md) | Hiệu năng, HTTPS/TLS, uptime, thao tác POS và coverage |
| [Hoàn thiện hồ sơ bàn giao](./goi-5-ho-so-ban-giao.md) | URS/RTM, kiến trúc, SDD và hướng dẫn sử dụng |

## Tiêu chí còn mở thuộc Gói 1–4

| Công việc | Mã hoặc hồ sơ |
| --- | --- |
| [Gói 1 — Phân quyền và kiến trúc](./goi-1-phan-quyen-va-kien-truc.md) | `FR-AUTH-03`, `NFR-MAINT-01`, CS-02, BF-022, BF-023 |
| [Gói 2 — Quét mã vạch khi kiểm kê](./goi-2-quet-ma-vach-kiem-ke.md) | `FR-PROD-03`, bằng chứng E2E kho |
| [Gói 3 — Vòng đời đơn và Hangfire](./goi-3-vong-doi-don-hang-va-hangfire.md) | `FR-ORD-02`, `FR-SIM-03`, `NFR-SEC-02` |
| [Gói 4 — In chứng từ và thông báo POS](./goi-4-in-chung-tu-va-thong-bao-pos.md) | `FR-ORD-03`, `FR-POS-03`, `FR-SIM-02`, CS-06 |

## Nâng cấp 10

- **01 — Mua hàng, nhập kho và giá vốn** đã xong cả 01-A và 01-B (12/11/2026), đặc tả chuyển sang [Done](../Done/01-mua-hang-nhap-kho-gia-von.md). Câu hỏi còn treo: giữ WAC hay chuyển FIFO/giá đích danh. [Báo cáo 01-B](../../report-after-upgrade/hoa-don-nha-cung-cap-u01b.md)
- **10 — Đăng nhập SĐT và quên mật khẩu.** Chưa triển khai hai luồng. [Kế hoạch 10](./10-quen-mat-khau-dang-nhap-sdt.md)

## Việc liên quan

- **Webhook xác nhận VietQR tự động.** Upgrade 07 giai đoạn C, tùy chọn đang tạm gác. [Kế hoạch VietQR](../Done/07-thanh-toan-qr-vietqr.md)
- **Kiểm chứng hồi quy POS.** [BF-019](../../bug-fixaffter/errors/BF-019-pos-nhan-http-200-sai-contract-la.md) · [BF-020](../../bug-fixaffter/errors/BF-020-pos-rieng-chua-khoa-gio-va-ngu.md)

## Lỗi và quyết định còn mở

Các mục dưới đây không thuộc gói nào ở trên nhưng vẫn mở trong [checklist lỗi](../../bug-fixaffter/CHECKLIST.md). BF-022 và BF-023 xử lý cùng [Gói 1](./goi-1-phan-quyen-va-kien-truc.md).

| Mục | Ưu tiên | Hiện trạng |
| --- | --- | --- |
| [BF-024](../../bug-fixaffter/errors/BF-024-tim-kiem-phieu-nhap-khong-mo-dung-phieu.md) — Tìm kiếm toàn hệ thống (CS-04) | P2 | Bấm kết quả phiếu nhập không mở đúng phiếu. Chưa sửa. |
| [BF-025](../../bug-fixaffter/errors/BF-025-integration-test-crlf-tren-windows.md) — Test AC01 trên Windows | P2 | Test fail do CRLF, chỉ ảnh hưởng môi trường kiểm thử. Chưa sửa. |
| [BF-026](../../bug-fixaffter/errors/BF-026-health-ready-lo-thong-tin-db.md) — `/health/ready` công khai | P3 | Trả tên user database. Chấp nhận cho bản demo, chưa sửa. |
| [BF-027](../../bug-fixaffter/errors/BF-027-tong-so-luong-bao-cao-cong-khac-don-vi.md) — `total*Quantity` trong báo cáo | P3 | API cộng gộp số lượng khác đơn vị; giao diện đã tránh dùng. Chưa sửa. |
| CS-07 — Tổng quan theo thời gian | — | KPI và biểu đồ lấy dữ liệu thật nhưng là số lũy kế; API chưa lọc theo khoảng thời gian. [Báo cáo frontend](../../report-after-upgrade/frontend-coming-soon-va-upgrade.md) |

Quyết định cần nhóm chốt:

- [ ] **Màn Handoff:** bỏ đặc tả [07-handoff-screens.md](../../11-ui-screens/07-handoff-screens.md) hay làm lại màn hình.
- [ ] **Số lẻ ở đơn vị đếm:** đơn vị mặc định cho phép 3 chữ số lẻ nên POS nhận số lẻ với "Cái". Chốt có cấm hay không.
- [ ] **BF-021:** [checklist lỗi](../../bug-fixaffter/CHECKLIST.md) còn ghi chưa sửa, nhưng [ma trận RBAC](../../01-users-and-rbac/02-rbac-matrix.md#21-lệch-giữa-yêu-cầu-và-code-hiện-tại-đối-chiếu-06112026) ghi đã sửa ở CS-01 P2 và `ReportService` trả `null` giá vốn, lãi gộp cho Staff. Cần xác nhận và đóng mục trong checklist lỗi.
