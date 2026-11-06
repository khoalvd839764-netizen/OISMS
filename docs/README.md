# HỆ THỐNG TÀI LIỆU KỸ THUẬT DỰ ÁN OISMS (DOCUMENTATION PORTAL)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Dự án (Project)** | **OISMS** — Omnichannel Inventory and Sales Management System (Hệ thống quản lý bán hàng và tồn kho đa kênh) |
| **Mã tài liệu gốc** | `DOC-ROOT-00` |
| **Phiên bản (Version)** | `v1.1.0` |
| **Ngày cập nhật** | `2026-11-06` |
| **Nguồn truy vết gốc** | `De bai CNPM.pdf` (6 trang) |
| **Bản demo** | Admin [oisms.khoadang.site](https://oisms.khoadang.site) · POS [pos.oisms.khoadang.site](https://pos.oisms.khoadang.site) (AWS EC2, có thể tắt ngoài giờ demo) |

---

## Cách Đọc Bộ Tài Liệu

- **Yêu cầu** (`00`–`03`, `11`, `12`) là chuẩn nghiệp vụ. Nếu code lệch, tài liệu ghi lại ở mục "Hiện trạng" hoặc "Lệch giữa yêu cầu và code" và cần nhóm quyết định; không tự đổi yêu cầu theo code.
- **Thiết kế, API, vận hành** (`04`, `05`, `07`, `13`, `database`) mô tả hệ thống đang chạy, đã đối chiếu với code ngày 06/11/2026.
- **Lịch sử** (`08`, `09`, các báo cáo có ngày) giữ nguyên nội dung tại thời điểm viết. Số liệu cũ như "29 bảng", `ApplicationDbContext`, "194 tests" trong các thư mục này là mốc lịch sử; hiện trạng là **43 bảng**, `AppDbContext`, `OISM.WebApi`.

---

## Danh Mục Phân Hệ

| Phân hệ | Đường dẫn thư mục | Mã Module | Loại | Nội dung trọng tâm |
| :---: | :--- | :---: | :---: | :--- |
| **00** | [`00-overview/`](./00-overview/README.md) | `MOD-OVW-00` | Yêu cầu | Tầm nhìn, bài toán kinh doanh, khái niệm nghiệp vụ cốt lõi, đối chiếu đề bài PDF, hiện trạng triển khai. |
| **01** | [`01-users-and-rbac/`](./01-users-and-rbac/README.md) | `MOD-USR-01` | Yêu cầu | 5 tác nhân, ma trận RBAC (`Owner`, `Staff`, `Cashier`) kèm bảng lệch với code, vòng đời Tenant. |
| **02** | [`02-functional-requirements/`](./02-functional-requirements/README.md) | `MOD-FR-02` | Yêu cầu | 12 file `FR-*` và bảng hiện trạng triển khai theo từng mã. |
| **03** | [`03-non-functional-requirements/`](./03-non-functional-requirements/README.md) | `MOD-NFR-03` | Yêu cầu | Hiệu năng `p95`, Flash Sale `50 reqs/1 SKU`, cô lập tenant, ACID, sổ cái bất biến, coverage `≥ 80%`, hiện trạng đáp ứng. |
| **04** | [`04-architecture-and-design/`](./04-architecture-and-design/README.md) | `MOD-ARC-04` | Thiết kế | Clean Architecture 4 tầng, multi-tenant, ERD 43 bảng, máy trạng thái, 4 sơ đồ trình tự. |
| **05** | [`05-api-contracts/`](./05-api-contracts/README.md) | `MOD-API-05` | Thiết kế | Hợp đồng REST API theo 25 controller (đã đối chiếu route và `[Authorize]`). |
| **06** | [`06-testing-and-qa/`](./06-testing-and-qa/README.md) | `MOD-QA-06` | Kiểm thử | RTM, cấu trúc bộ test (unit 221, integration trên PostgreSQL thật, test POS), stress test, DoD. |
| **07** | [`07-devops-and-deployment/`](./07-devops-and-deployment/README.md) | `MOD-OPS-07` | Vận hành | Docker Compose local, CI GitHub Actions, biến môi trường, Nginx SPA. |
| **08** | [`08-sprints/`](./08-sprints/README.md) | `MOD-SPR-08` | Lịch sử | Kế hoạch và báo cáo theo sprint (`sprint-2` đến `sprint-5`). |
| **09** | [`09-personal-reports/`](./09-personal-reports/README.md) | `MOD-PER-09` | Lịch sử | Báo cáo bàn giao, biên bản nghiệm thu cá nhân. |
| **10** | [`10-ai-skills/`](./10-ai-skills/README.md) | `MOD-SKL-10` | Công cụ | 12 AI Skills trong `skills/` và quy trình Codex–Antigravity (chỉ dùng khi được yêu cầu). |
| **11** | [`11-ui-screens/`](./11-ui-screens/README.md) | `MOD-SCR-11` | Yêu cầu | Đặc tả màn hình Admin, POS và ma trận quyền theo route. |
| **12** | [`12-error-and-status-codes/`](./12-error-and-status-codes/README.md) | `MOD-ERR-12` | Yêu cầu | Chuẩn lỗi RFC 7807, từ điển mã lỗi (kèm đối chiếu code), màu và phản hồi UI. |
| **13** | [`13-deploy-aws/`](./13-deploy-aws/README.md) | `MOD-AWS-13` | Vận hành | Cách deploy hiện tại lên AWS EC2 (Docker Compose + Caddy + Let's Encrypt), quy trình cập nhật, backup, xử lý sự cố, kiến thức deploy nền tảng. |
| **DB** | [`database/`](./database/README.md) | `MOD-DB-00` | Thiết kế | `oisms_schema.sql` (43 bảng), từ điển dữ liệu, quy trình nâng cấp DB có dữ liệu, SQL cấp quyền Hangfire. |
| **UP** | [`Upgrade-recommended/`](./Upgrade-recommended/README.md) | — | Yêu cầu | 6 gói nâng cấp nghiệp vụ của GVHD (mua hàng, truy vết, kiểm kê, bảng giá, đa đơn vị, rà soát kiến trúc). |
| **RP** | [`report-after-upgrade/`](./report-after-upgrade/frontend-coming-soon-va-upgrade.md) | — | Báo cáo | Báo cáo sau nâng cấp và nghiệm thu frontend (hiện trạng Coming Soon). |
| **BF** | [`bug-fixaffter/`](./bug-fixaffter/README.md) | — | Theo dõi | [`CHECKLIST.md`](./bug-fixaffter/CHECKLIST.md) lỗi đã sửa/chưa sửa, Coming Soon, Upgrade; mỗi lỗi một file trong `errors/`. |

---

## Tra Cứu Nhanh

| Cần gì | Xem |
| :--- | :--- |
| Chạy hệ thống ở máy local | [`07/01-docker-compose-guide.md`](./07-devops-and-deployment/01-docker-compose-guide.md) |
| Deploy hoặc cập nhật bản trên AWS | [`13-deploy-aws/04-quy-trinh-deploy-va-cap-nhat.md`](./13-deploy-aws/04-quy-trinh-deploy-va-cap-nhat.md) |
| `/health/ready` trả 503 "schema is incomplete" | [Nâng cấp database đã có dữ liệu](./database/README.md#nâng-cấp-database-đã-có-dữ-liệu-cả-nhóm-đọc-khi-kéo-code-mới) |
| Quyền của từng vai trò | [`01/02-rbac-matrix.md`](./01-users-and-rbac/02-rbac-matrix.md) |
| Lỗi nào đã sửa, chưa sửa | [`bug-fixaffter/CHECKLIST.md`](./bug-fixaffter/CHECKLIST.md) |
| Chức năng nào còn thiếu | [`02-functional-requirements/README.md`](./02-functional-requirements/README.md#hiện-trạng-triển-khai-theo-yêu-cầu-đối-chiếu-code-06112026), [báo cáo frontend](./report-after-upgrade/frontend-coming-soon-va-upgrade.md) |
| Mã lỗi API | [`12/02-error-codes-catalog.md`](./12-error-and-status-codes/02-error-codes-catalog.md) |
