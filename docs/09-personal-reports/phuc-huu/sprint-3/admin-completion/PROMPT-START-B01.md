# Prompt khởi động sau POS — S3-ADMIN-B01 / v1.0

**Chỉ dùng khi POS đã hoàn tất. Kế hoạch Admin v1.1 đã gộp thành 3 đợt; không chạy A01–A06 như 6 task nghiệm thu riêng.**

Bạn là Antigravity triển khai theo `.agents/skills/codex-antigravity-workflow/SKILL.md`, reference Antigravity/OISMS và AGENTS. Dùng CodeGraph trước khi tìm/đọc code; đối chiếu source mới nếu index không đủ.

Đọc:

- `docs/09-personal-reports/phuc-huu/sprint-3/admin-completion/PLAN.md` — bản v1.1, ba đợt Admin.
- `docs/09-personal-reports/phuc-huu/sprint-3/admin-completion/reviews/pr26-review-r1.md`.
- `docs/09-personal-reports/phuc-huu/sprint-3/admin-completion/evidence/pr26-r1/README.md` và nguồn contract/phân công dẫn trong plan.

Chỉ thực hiện **S3-ADMIN-B01 — Contract, API đọc và đơn hàng**, gộp đầy đủ **A01-AC-01..04 + A02-AC-01..04**. Một brief, một handover, một review chung; không dừng chờ Codex review từng hạng mục A.

Kiểm tra gate POS B02+B03 đã VERIFIED và code/tài liệu đã được bảo toàn trước khi chuyển nhánh. Pin head/develop thực tế; làm trên `feature/OISMS-43-orders-transfers`, không sửa Admin trong working tree nhánh POS, không copy đè snapshot PR26 lên code mới. Không reset/clean/stash làm mất thay đổi.

Tạo `docs/09-personal-reports/phuc-huu/sprint-3/admin-completion/batch-01-contract-orders/brief.md` theo mẫu skill, baseline mới và AC gộp trên trước triển khai. Giữ các ID gốc để truy vết.

Thực hiện trong cùng đợt:

1. Chốt route/DTO/filter/status/role/errorCode/session-context theo backend hiện hành. GET list/detail transfers/counts tại R1 còn thiếu controller/query implementation, là phụ thuộc BE2. Nếu baseline mới đã có thì tái sử dụng; nếu cần bổ sung chỉ giới hạn query/controller/tests, giữ tenant/role/filter/pagination, không đổi mutation/ledger/WAC/schema. Cache/lỗi 401/403/405/500/network phải hiển thị trung thực, không giả dữ liệu fresh.
2. Hoàn thiện Orders Center: list/filter/paging và response race; tạo đơn/giữ chỗ thật với key/payload ổn định khi retry; detail và action matrix đúng backend; refetch sau thao tác; notification link orderId và in phiếu giao hàng từ server snapshot, đúng vai trò. Không tạo engine trừ tồn khác hoặc giả state local.
3. Dùng chung test fixture/provider/context giữa hai phần. Kiểm chứng API đọc tenant A/B và browser mới đọc chứng từ, HTTP/backend test tạo/reserve/lifecycle và tồn, component/browser các nhánh lỗi/race, print preview phù hợp. Giữ regression POS liên quan. Không gán mock/browser fixture thành PostgreSQL/Hub thật.

Anti tự tái hiện finding, viết regression trực tiếp, sửa và kiểm chứng cả đợt trước khi bàn giao. Phụ thuộc backend thiếu chỉ chặn phần phụ thuộc; tiếp tục Orders/contract/front đủ điều kiện trong B01, không tách ra một task review mới. Thiếu gate bắt buộc thì báo BLOCKED phần đó và điều kiện gỡ, không tự PASS.

Lưu một `handover.md`, bảng toàn bộ A01/A02 AC, F04 và evidence/log/source hash/provider trong `batch-01-contract-orders/`. Chỉ đóng PR26-F04 khi đủ bằng chứng API+UI; không tự đóng F02/F06 chỉ vì đã chốt contract. Các phần thuộc đợt sau vẫn PLANNED.

Giữ POS và engine BE1 đã nghiệm thu, tài liệu/evidence/lịch sử PR26. **Không triển khai Admin B02/B03, không commit/push/merge/đổi base PR trong B01.** Bàn giao `READY_FOR_REVIEW` để Codex review chung một lần. Vòng fix sau chỉ xử lý ID còn mở và regression bị ảnh hưởng, không làm lại phần đã đạt.
