# HỆ THỐNG TÀI LIỆU KỸ THUẬT DỰ ÁN OISMS (DOCUMENTATION PORTAL)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Dự án (Project)** | **OISMS** — Omnichannel Inventory and Sales Management System (Hệ thống quản lý bán hàng và tồn kho đa kênh) |
| **Mã tài liệu gốc** | `DOC-ROOT-00` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày cập nhật** | `2026-10-22` |
| **Nguồn truy vết gốc** | `C:\Users\Jacki Khoa\Documents\De bai CNPM.pdf` (6 trang) |

---

## Cấu Trúc 10 Phân Hệ Tài Liệu Chuẩn Hóa

| Phân hệ | Đường dẫn thư mục | Mã Module | Mô tả nội dung trọng tâm |
| :---: | :--- | :---: | :--- |
| **00** | [`00-overview/`](./00-overview/README.md) | `MOD-OVW-00` | Tầm nhìn dự án, bài toán kinh doanh, từ điển khái niệm cốt lõi và bảng đối chiếu nguyên văn đề bài PDF. |
| **01** | [`01-users-and-rbac/`](./01-users-and-rbac/README.md) | `MOD-USR-01` | Đặc tả 5 tác nhân (Actors), Ma trận phân quyền RBAC (`Owner`, `Staff`, `Cashier`) và Vòng đời Tenant. |
| **02** | [`02-functional-requirements/`](./02-functional-requirements/README.md) | `MOD-FR-02` | Đặc tả chi tiết 12 module Yêu cầu chức năng (`FR-AUTH`, `FR-PROD`, `FR-INV`, `FR-COST`, `FR-RSE`, `FR-ORD`, `FR-POS`, `FR-SIM`, `FR-REP`, `FR-AI`). |
| **03** | [`03-non-functional-requirements/`](./03-non-functional-requirements/README.md) | `MOD-NFR-03` | Đặc tả Yêu cầu phi chức năng: Hiệu năng `p95`, Chịu tải Flash Sale `50 reqs/1 SKU`, Multi-tenant Isolation, ACID, Sổ cái bất biến và Coverage $\ge 80\%$. |
| **04** | [`04-architecture-and-design/`](./04-architecture-and-design/README.md) | `MOD-ARC-04` | Kiến trúc Clean Architecture 4 tầng, Thiết kế Multi-tenant, ERD 29 bảng PostgreSQL, State Machines và 4 Sequence Diagrams trọng tâm. |
| **05** | [`05-api-contracts/`](./05-api-contracts/README.md) | `MOD-API-05` | Đặc tả hợp đồng REST API Endpoints phân tách theo từng nhóm chức năng. |
| **06** | [`06-testing-and-qa/`](./06-testing-and-qa/README.md) | `MOD-QA-06` | Ma trận truy vết yêu cầu (`RTM`), Hướng dẫn chạy 194 Unit/Integration Tests, Stress Test đồng thời và tiêu chuẩn `DoD`. |
| **07** | [`07-devops-and-deployment/`](./07-devops-and-deployment/README.md) | `MOD-OPS-07` | Hướng dẫn triển khai Docker Compose, CI/CD GitHub Actions và cấu hình `.env` / Nginx SPA. |
| **08** | [`08-sprints/`](./08-sprints/README.md) | `MOD-SPR-08` | Tài liệu kế hoạch và hướng dẫn tổng hợp theo từng giai đoạn (`sprint-2` đến `sprint-5`). |
| **09** | [`09-personal-reports/`](./09-personal-reports/README.md) | `MOD-PER-09` | Báo cáo bàn giao và biên bản nghiệm thu cá nhân của từng thành viên (`dang-khoa`, `nhut`, `phuc-huu`, `phuong`). |
| **10** | [`10-ai-skills/`](./10-ai-skills/README.md) | `MOD-SKL-10` | Danh mục 12 AI Skills được cài đặt tại `skills/`, bản đồ ánh xạ kiến trúc OISMS và hướng dẫn kích hoạt/tích hợp trong Antigravity. |
| **DB** | [`database/`](./database/README.md) | `MOD-DB-00` | Toàn văn kịch bản SQL khởi tạo 29 bảng, ràng buộc khóa ngoại kép, trigger và seed data (`oisms_schema.sql`). |
