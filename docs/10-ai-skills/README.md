# 10. PHÂN HỆ AI SKILLS & TRỢ LÝ PHÁT TRIỂN (AI SKILLS & ASSISTANTS)

| Thông tin kiểm soát thư mục | Chi tiết |
| :--- | :--- |
| **Mã phân hệ (Module ID)** | `MOD-SKL-10` |
| **Phiên bản hiện hành** | `v1.0.0` |
| **Ngày cập nhật gần nhất** | `2026-10-23` |
| **Thư mục lưu trữ skill vật lý** | `D:\OISMS\skills\` |
| **Nguồn repository kỹ năng** | [`https://github.com/rmyndharis/antigravity-skills`](https://github.com/rmyndharis/antigravity-skills) |

---

## 1. Mục Đích & Vai Trò Của Phân Hệ

Phân hệ **10-ai-skills** tài liệu hóa toàn diện hệ thống kỹ năng AI (AI Skills) được tích hợp trong dự án **OISMS (Omnichannel Inventory and Sales Management System)** nhằm hỗ trợ đội ngũ kỹ sư và Agent AI (Antigravity) trong suốt vòng đời phát triển phần mềm:
- **Chuẩn hóa kiến trúc:** Giữ vững ranh giới 4 tầng Clean Architecture của Backend `.NET 8` và mô hình ứng dụng kép `React 19` (Admin & POS PWA).
- **Đảm bảo chất lượng dữ liệu & hiệu năng:** Tối ưu hóa lược đồ 29 bảng PostgreSQL, duy trì tính bất biến của sổ cái kho (Append-Only Ledger) và công thức tính giá vốn bình quân gia quyền (WAC).
- **An toàn bảo mật đa tổ chức (Multi-Tenant):** Kiểm soát truy cập chặt chẽ thông qua JWT/RBAC và bộ lọc toàn cục `tenant_id` của EF Core.
- **Tự động hóa QA & DevOps:** Tạo bộ kiểm thử tự động xUnit đạt độ phủ $\ge 80\%$, đồng thời tự động hóa quy trình CI/CD qua GitHub Actions.

---

## 2. Danh Mục Tài Liệu Trong Phân Hệ `10-ai-skills/`

| STT | Mã tài liệu | Tên file | Phiên bản | Nội dung chính |
| :---: | :---: | :--- | :---: | :--- |
| **1** | `DOC-SKL-01` | [`01-skills-catalog-and-mapping.md`](./01-skills-catalog-and-mapping.md) | `v1.0.0` | Bảng đặc tả chi tiết 12 AI Skills, phân nhóm nghiệp vụ kỹ thuật, ánh xạ tương ứng vào từng thành phần mã nguồn và tài liệu kiến trúc OISMS. |
| **2** | `DOC-SKL-02` | [`02-antigravity-integration-guide.md`](./02-antigravity-integration-guide.md) | `v1.0.0` | Hướng dẫn cơ chế Discovery của Antigravity, thiết lập NTFS Junction `.agent/skills`, cách kích hoạt tự động (Progressive Disclosure) và chủ động (Proactive prompt). |

---

## 3. Tổng Hợp Nhanh 12 AI Skills Đã Cài Đặt

| Nhóm lĩnh vực | Tên Skill | Vị trí thư mục | Trọng tâm hỗ trợ OISMS |
| :--- | :--- | :--- | :--- |
| **Backend** | [`csharp-pro`](../../skills/csharp-pro/SKILL.md) | `skills/csharp-pro/` | Code C# .NET 8 hiện đại, record types, async/await, LINQ, pattern matching |
| **Backend** | [`architecture-patterns`](../../skills/architecture-patterns/SKILL.md) | `skills/architecture-patterns/` | Clean Architecture 4 tầng, DDD, CQRS, phân định Domain/Application/Infra/WebAPI |
| **Backend** | [`api-design-principles`](../../skills/api-design-principles/SKILL.md) | `skills/api-design-principles/` | Chuẩn hóa RESTful WebAPI, DTO Request/Response, HTTP status, OpenAPI/Swagger |
| **Database** | [`postgresql`](../../skills/postgresql/SKILL.md) | `skills/postgresql/` | Thiết kế schema 29 bảng, foreign keys kép, kiểu NUMERIC(18,2), partition |
| **Database** | [`sql-optimization-patterns`](../../skills/sql-optimization-patterns/SKILL.md) | `skills/sql-optimization-patterns/` | Phân tích EXPLAIN, composite indexes `(tenant_id, ...)`, chịu tải Flash Sale |
| **Frontend** | [`typescript-pro`](../../skills/typescript-pro/SKILL.md) | `skills/typescript-pro/` | Strict Type checking cho React 19, đồng bộ interfaces giữa WebAPI và Frontend |
| **Frontend** | [`react-state-management`](../../skills/react-state-management/SKILL.md) | `skills/react-state-management/` | State cart, barcode scanning thời gian thực cho POS PWA; cache server state cho Admin |
| **Security** | [`auth-implementation-patterns`](../../skills/auth-implementation-patterns/SKILL.md) | `skills/auth-implementation-patterns/` | Xác thực JWT Bearer, phân quyền RBAC 3 vai trò, giải mã TenantContext |
| **Security** | [`security-auditor`](../../skills/security-auditor/SKILL.md) | `skills/security-auditor/` | Rà soát bảo mật DevSecOps, phòng chống rò rỉ chéo tenant, tuân thủ OWASP |
| **Quality** | [`code-review-excellence`](../../skills/code-review-excellence/SKILL.md) | `skills/code-review-excellence/` | Chuẩn mực review PR, phát hiện code smell, phòng ngừa nợ kỹ thuật (technical debt) |
| **Testing** | [`unit-testing-test-generate`](../../skills/unit-testing-test-generate/SKILL.md) | `skills/unit-testing-test-generate/` | Sinh test xUnit tự động cho Use Cases (WAC, Ledger, Reservation), mục tiêu $\ge 80\%$ |
| **DevOps** | [`github-actions-templates`](../../skills/github-actions-templates/SKILL.md) | `skills/github-actions-templates/` | Tối ưu CI/CD GitHub Actions build, test, Docker packaging cho .NET và React |

---

## 4. Liên Kết Nhanh Đến Các Phân Hệ Liên Quan

- **Phân hệ 04 — Kiến trúc & Thiết kế Kỹ thuật:** [`../04-architecture-and-design/`](../04-architecture-and-design/README.md)
- **Phân hệ 05 — Hợp đồng REST API (API Contracts):** [`../05-api-contracts/`](../05-api-contracts/README.md)
- **Phân hệ 06 — Kiểm thử & Đảm bảo Chất lượng (QA):** [`../06-testing-and-qa/`](../06-testing-and-qa/README.md)
- **Phân hệ 07 — DevOps & Triển khai Container:** [`../07-devops-and-deployment/`](../07-devops-and-deployment/README.md)
