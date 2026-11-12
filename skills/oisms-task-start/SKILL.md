---
name: oisms-task-start
description: Quy trình chuẩn khi bắt đầu một task OISMS, đọc CodeGraph và docs theo đúng thứ tự, có giới hạn để tiết kiệm token. Dùng khi nhận task code, sửa lỗi, review hoặc lập plan trong repo OISMS, hoặc khi người dùng yêu cầu đọc docs và CodeGraph trước khi làm. Không cần cho câu hỏi nhanh hoặc khi đã có đủ bối cảnh trong phiên.
---

# Bắt đầu task OISMS

Mục tiêu: hiểu đủ để làm đúng với ít lượt đọc nhất. Đọc theo thứ tự dưới đây, đủ thông tin thì dừng và bắt tay làm.

## Bước 1. Nắm trạng thái (1 lượt)

```bash
git branch --show-current && git log --oneline -3 && git status --short | wc -l
ls tmp/claude-anti/ 2>/dev/null
```

- Có sổ task đúng task này trong `tmp/claude-anti/` thì đọc sổ và làm tiếp từ đó, bỏ qua các bước đã ghi.
- Working tree bẩn thì chỉ xem `git status --short -- <vùng liên quan>` và không ghi đè thay đổi có sẵn.
- Bối cảnh chung đã có trong `.claude/CLAUDE.md`; không đọc lại README hay docs tổng quan.

## Bước 2. Xác định loại task

Ghi một dòng: task gì, vùng nào, mức rủi ro (FAST / STANDARD / CRITICAL theo [claude-to-antigravity](../claude-to-antigravity/SKILL.md)). Chạm tenant, ledger, WAC, reservation, idempotency, transaction, auth hoặc schema thì là CRITICAL.

## Bước 3. CodeGraph trước

Có `.codegraph/` thì dùng CodeGraph trước khi grep hoặc đọc file.

- Gọi `codegraph_explore` (nếu bị hoãn, tải bằng tool search), hoặc shell: `codegraph explore "<tên>"`.
- Truyền tên symbol hoặc file cụ thể, gom vào **một** truy vấn: `OrderService ConfirmOrderAsync ISqlLockService`.
- Luồng từ A đến B: nêu tên các symbol trải dọc luồng để thấy đường gọi.
- Source trả về coi như đã đọc; không mở lại file và không grep để kiểm tra lại.
- Xem mục blast radius để biết caller và test bị ảnh hưởng.
- Dùng `Read` có `offset`/`limit` cho config, docs, hoặc phần CodeGraph không trả về.

File lớn (`OrderService.cs`, `InventoryPage.tsx`, `ProductsPage.tsx`, `apiAdapter.ts`): không mở nguyên file, chỉ hỏi theo tên method hoặc function.

CodeGraph lỗi hoặc thiếu kết quả: ghi lại giới hạn, tìm bằng grep có phạm vi. Không tự index lại repo; đó là quyết định của người dùng.

## Bước 4. Đọc đúng mục docs

Xác định nguồn trong bảng, xem danh sách đề mục rồi chỉ đọc mục cần:

```bash
grep -n '^#' <file>            # xem đề mục
sed -n '<từ>,<đến>p' <file>    # đọc đúng đoạn
```

| Việc | Nguồn |
| --- | --- |
| Chọn task, phân công | `docs/08-sprints/sprint-4/README.md`, `docs/bug-fixaffter/CHECKLIST-BUGFIX-AND-UPGRADE.md` |
| CS-01..08 | `docs/bug-fixaffter/Comming Soon function/COMING-SOON.md` |
| Upgrade 01..06 | `docs/Upgrade-recommended/<số>-*.md` |
| Kho, đơn, giá vốn, giữ chỗ | `docs/00-overview/02-core-domain-concepts.md`, `docs/02-functional-requirements/fr-03` đến `fr-07` |
| Thêm/sửa API | `docs/05-api-contracts/`, `docs/01-users-and-rbac/02-rbac-matrix.md`, `docs/12-error-and-status-codes/02-error-codes-catalog.md` |
| Schema, trạng thái | `docs/database/oisms_schema.sql`, `docs/04-architecture-and-design/03-database-design.md`, `04-state-machines.md` |
| Màn hình | `docs/11-ui-screens/<màn hình>.md`, `docs/12-error-and-status-codes/03-color-system-tokens.md` |
| Viết test, gate | `docs/06-testing-and-qa/` |

Không đọc: `docs/08-sprints/sprint-2` và `sprint-3` (trừ khi cần baseline), `docs/09-personal-reports/` (trừ khi task yêu cầu), log trong `bin/`, `obj/`, `TestResults/`.

Tài liệu kiến trúc cũ dùng tên `ApplicationDbContext`, `ITenantContext`; code hiện tại là `AppDbContext`, `ICurrentTenantService`.

## Bước 5. Chọn skill phù hợp

Được phép đọc `skills/` để tìm kỹ năng hỗ trợ task. Tra bảng trước; không có hàng phù hợp thì xem `ls skills/` và dòng mô tả đầu file: `sed -n '1,8p' skills/<tên>/SKILL.md`.

| Task | Skill trong `skills/` |
| --- | --- |
| C#, service, EF Core | `csharp-pro` |
| Ranh giới tầng, tách service | `architecture-patterns` |
| Thiết kế endpoint, DTO, contract | `api-design-principles` |
| Schema, migration, trigger, lock | `postgresql` |
| Query chậm, index, NFR hiệu năng | `sql-optimization-patterns` |
| JWT, RBAC, phiên đăng nhập | `auth-implementation-patterns` |
| Rà bảo mật, tenant isolation | `security-auditor` |
| TypeScript, kiểu dữ liệu frontend | `typescript-pro` |
| State React, hook, tách page lớn | `react-state-management` |
| Viết unit/integration test | `unit-testing-test-generate` |
| Review diff | `code-review-excellence` |
| CI, GitHub Actions | `github-actions-templates` |
| Giao việc cho Antigravity | `claude-to-antigravity` |

- Chọn tối đa 1–2 skill thật sự giúp task; FAST thường không cần. Ghi một dòng đã chọn skill nào và vì sao.
- Chỉ đọc `SKILL.md` của skill đã chọn. Thư mục `resources/`, `references/` chỉ mở khi `SKILL.md` chỉ tới và task cần đúng phần đó.
- `react-state-management`, `unit-testing-test-generate`, `github-actions-templates`, `postgresql` là file dài: xem đề mục bằng `grep -n '^#'` rồi đọc đúng đoạn.
- Skill là gợi ý kỹ thuật chung. Khi khác với docs hoặc invariant OISMS thì docs và invariant thắng. Không thêm thư viện hay refactor chỉ vì skill khuyên.
- Không dùng `codex-antigravity-workflow` cùng lúc với `claude-to-antigravity`, trừ khi người dùng yêu cầu luồng Codex.
- Skill đã đọc trong phiên thì không đọc lại.

## Bước 6. Đối chiếu và chốt

Trước khi code hoặc viết prompt, tóm tắt tối đa 6 dòng:

- **Docs yêu cầu:** … (`đường-dẫn#mục`)
- **Code đang có:** … (`file:dòng`)
- **Còn thiếu:** …
- **Skill áp dụng:** … (hoặc không cần)
- **Mâu thuẫn hoặc chưa rõ:** … (nếu có thì hỏi người dùng đúng điểm đó, không tự chọn nghiệp vụ)
- **Việc tiếp theo và cách kiểm chứng:** …

Docs xác định yêu cầu, code và test xác định hiện trạng. Báo cáo kế hoạch không phải bằng chứng đã làm.

## Giới hạn đọc

Mức khuyến nghị trước khi có tóm tắt ở bước 6:

| Mức | CodeGraph | Mục docs | Skill |
| --- | --- | --- | --- |
| FAST | 1 lượt | 0–1 | 0 |
| STANDARD | 1–2 lượt | 1–3 | 0–1 |
| CRITICAL | 2–4 lượt | 3–5, thêm test liên quan | 1–2 |

Vượt mức mà vẫn chưa rõ thì dừng, nêu điều còn thiếu và hỏi hoặc đổi hướng tìm; không đọc lan thêm. Cần đọc thêm phải có lý do cụ thể.

Không lặp lại khi đã có trong phiên: file đã đọc, kết quả CodeGraph, tóm tắt docs. Chỉ đọc lại phần đã thay đổi.

## Sau bước này

- Giao Antigravity thì tiếp tục theo [claude-to-antigravity](../claude-to-antigravity/SKILL.md).
- Tự làm thì sửa trong phạm vi đã chốt và chạy kiểm chứng theo tác động.
- Mọi commit, push, PR, merge đều cần người dùng đồng ý trong chat.
