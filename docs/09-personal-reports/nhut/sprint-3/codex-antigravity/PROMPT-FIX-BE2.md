# Prompt triển khai fix backend Nhựt — PR #20

Ngày: 2026-10-26. Dùng prompt dưới đây để giao **S3-BE2-T01** trước. Plan hoàn thiện BE2 Sprint 3 gồm T01–T06; các task sau cần kết quả review của task trước. File này chỉ giao việc, chưa xác nhận triển khai hoặc kiểm thử đã hoàn tất.

```text
Bạn là Antigravity, triển khai fix phần Backend 2 Sprint 3 của Nhựt theo plan Codex đã lập.

ĐÍCH LÀM VIỆC
- Repo: D:/OISMS.
- Nhánh duy nhất nhận thay đổi BE2: feature/OISMS-42-sprint-3-backend-2.
- PR hiện có: https://github.com/khoalvd839764-netizen/OISMS/pull/20.
- Head Nhựt đã review: d7fa4bc8738a1f22053d9b313f8e90c423b0483d.
- Không sửa/push nhánh BE1 trong task BE2; không merge vào develop.

ĐỌC TRƯỚC KHI SỬA
Gói tài liệu tại:
docs/09-personal-reports/nhut/sprint-3/codex-antigravity/

Đọc lần lượt:
1. README.md
2. PLAN.md
3. reviews/initial-review.md
4. evidence/README.md và probe-results.json
5. task-01-core-alignment/brief.md

Nếu repo có AGENTS.md/skill áp dụng thì đọc và tuân thủ. Nếu .codegraph tồn tại, dùng CodeGraph trước để định vị/hiểu code; khi graph thiếu backend, ghi rõ và đối chiếu source trực tiếp. Dùng MCP Jira/Confluence/GitHub nếu có; ghi ID/version/SHA thực sự đọc được, không tự nhận đã đồng bộ nguồn live.

THỨ TỰ TỔNG THỂ
T01: Hợp nhất core BE1, xử lý conflict và route webhook trùng.
T02: Hoàn thiện API đọc order, transfer, stock count.
T03: Quyền báo cáo, tenant isolation và validation.
T04: Sửa report revenue/COGS/velocity/filter/threshold và hiệu năng.
T05: Automated core tests PostgreSQL/HTTP/concurrency/Hangfire thật.
T06: Regression cuối, tổng kết và bàn giao PR.

LƯỢT NÀY CHỈ LÀM T01
Đóng BE2-F01, BE2-F02, BE2-F06 theo acceptance criteria của brief. Chưa triển khai T02–T06; chúng cần review T01 trước.

1. KIỂM TRA WORKSPACE VÀ BASELINE
- Kiểm tra git status, branch, worktree và fetch remote mới nhất.
- Working tree OISMS-42 có tài liệu plan/review của Codex chưa commit: đây là input cần giữ và đưa vào đúng gói BE2, không được xóa hoặc ghi đè.
- docs/11-ui-screens/ và docs/12-error-and-status-codes/ là untracked ngoài phạm vi: không stage.
- Có thể đang có worktree D:/OISMS-b01 thuộc nhánh BE1 đang làm việc. Không sửa, checkout lại nhánh đó hoặc lấy thay đổi chưa commit từ worktree này.
- Nếu remote OISMS-42 có commit mới, đọc diff và tích hợp an toàn; không reset, clean, force-push hoặc ghi đè công việc khác.

2. LẤY ĐÚNG CORE BE1 CÓ FIX B-01
- Baseline review BE1 ban đầu: 4206cc4b12f0128ee08927dc83550c9e8db5cd6e; CI của commit này còn B-01.
- Khi soạn prompt, local remote-tracking ref BE1 đã có commit:
  b5ba71ef7ac2bf055f4ee8963e159d4f27c69aae
  [OISMS-40] fix: allow clean warehouse branch reassignment
- Đây là dấu vết commit fix, chưa phải xác nhận Codex VERIFIED hay CI xanh.
- Fetch, kiểm tra head thực tế của PR #21, ancestry, diff B-01, handover và CI đúng commit. Pin SHA sử dụng trong handover T01.
- Tích hợp baseline BE1 có fix B-01 vào nhánh OISMS-42. Nếu BE1 đã được merge vào develop, dùng develop chứa đúng baseline và ghi rõ ancestry.
- Không dùng lại 4206cc4 rồi gọi baseline all-green. Nếu B-01 chưa được kiểm chứng hoặc vẫn fail, ghi rõ dependency chưa đạt; không âm thầm bỏ test hay tự sửa B-01 lẫn vào BE2.

3. HỢP NHẤT THEO NGHIỆP VỤ
- Giữ engine mutation BE1 đã nghiệm thu: POS checkout, reserve/create, confirm, cancel, complete, locking, idempotency, cost snapshot và reservation expiry.
- Giải quyết 5 conflict đã phát hiện ở OrderDtos, IOrderService, OrderService, OrdersController và Program.cs; kiểm tra thêm conflict mới trên baseline mới.
- Bảo toàn phần query của Nhựt để T02 nối vào contract BE1. Không dùng ours/theirs cho cả file rồi làm mất chức năng.
- Chỉ một ingress POST /api/webhooks/simulator/{channel}, dùng IWebhookService và core BE1.
- Loại đường ingest BE2 tự tạo Reserved nhưng không tạo reservation, không giữ/trừ tồn thật.
- Không giữ hai engine ghi tồn, hai implementation cạnh tranh hoặc route ambiguous sau khi merge.
- Idempotency webhook phải giữ đủ tenant/channel/shop/order và payload signature; không trả đơn của shop khác khi external order ID trùng nhau.
- Giữ DTO/API POS, complete, webhook và expiry chuẩn BE1. Không xóa contract để build pass.
- Bảo toàn báo cáo/query BE2 ngoài phạm vi T01; ghi các stub và batch tuần tự còn lại để xử lý đúng task sau.

4. KIỂM THỬ T01
- Release build và khởi động DI/WebApplicationFactory thành công.
- HTTP routing xác nhận chỉ một webhook ingress, không ambiguous endpoint.
- Kiểm chứng 3 kênh qua core chuẩn; tạo đơn hợp lệ có reservation persisted thật; thiếu tồn không tạo Reserved giả.
- Replay cùng key/payload không giữ chỗ lần hai; khác shop không trả nhầm đơn; payload mismatch trả lỗi theo contract.
- Chạy lại các suite BE1 liên quan trên nhánh sau hợp nhất, bao gồm regression B-01 trên PostgreSQL thật.
- Cần testcase HTTP/PostgreSQL cho đường vừa tích hợp; InMemory không thay thế bằng chứng transaction/locking/FK.
- Không skip test, đổi expected theo code sai, vô hiệu hóa FK/tenant guard hoặc đổi cấu hình CI để né lỗi.
- Ghi số test và kết quả thực từ runner/TRX. Các số cũ 114 của BE2 hoặc 129/261 của BE1 là baseline lịch sử.

5. BÀN GIAO, COMMIT VÀ PUSH ĐÚNG NHÁNH
- Tạo task-01-core-alignment/handover.md và evidence: baseline SHAs, root cause, diff, cách giải quyết conflict, AC -> testcase -> kết quả, lệnh tái chạy, vấn đề còn mở.
- Cập nhật README task/status theo kết quả; chỉ ghi READY_FOR_REVIEW v1.0 khi đủ AC/evidence. Không tự ghi Codex VERIFIED hoặc backend hoàn thành 100%.
- Khi các kiểm thử cần thiết của T01 pass, commit các thay đổi đúng phạm vi, kèm tài liệu plan/review BE2 được bảo toàn. Kiểm tra staged diff trước commit; không git add . một cách mù quáng.
- Push về nhánh Nhựt:
  git push origin HEAD:feature/OISMS-42-sprint-3-backend-2
- PR #20 sẽ nhận commit mới; không tạo PR trùng, không push nhánh BE1/develop và không force-push.
- Đọc CI của commit vừa push qua MCP/công cụ đã xác thực. Nếu còn fail, ghi đúng testcase/finding và phân biệt lỗi T01, task sau hoặc dependency; không tuyên bố CI xanh khi chưa xác minh.
- CI tổng thể có thể còn lỗi thuộc T02–T05: ghi rõ và để PR chưa sẵn sàng merge; không vượt phạm vi sửa toàn bộ task sau trong T01.
- Bàn giao cho Codex review T01 rồi mới nhận task tiếp theo; không merge develop hay bật auto-merge.

KẾT QUẢ CUỐI CẦN TRẢ
1. Nhánh và commit SHA đã push, link PR #20.
2. SHA BE1 thực sự đã tích hợp và trạng thái B-01/CI đúng commit.
3. Các finding T01 đã xử lý cùng AC/test evidence.
4. Kết quả build/tests và CI thực tế, gồm các lỗi còn mở.
5. Đường dẫn handover T01 và phần việc còn lại T02–T06.
```
