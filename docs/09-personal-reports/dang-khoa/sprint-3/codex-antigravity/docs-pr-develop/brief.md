# Prompt giao Antigravity — S3-BE1-DOCS-PR-DEVELOP / v1.0

Dùng skill `codex-antigravity-workflow`, vai trò **Antigravity**. Đọc skill và reference vai trò tại repo trước khi làm. Đây là task **tái cấu trúc tài liệu cá nhân Sprint 3, tổng kết phần BE1 và đóng gói code đã nghiệm thu vào PR có base develop**. Không triển khai thêm nghiệp vụ.

## 1. Yêu cầu và quyền thực hiện

Người dùng yêu cầu: tách tài liệu đang gộp thành các folder có README giao tiếp Codex–Anti, một file tổng kết phần của Lê Võ Đăng Khoa, và tạo pull request đúng quy tắc repo để đưa code về `develop`. Người dùng xác nhận Anti có MCP để tra cứu mã task: **ưu tiên MCP đang kết nối để tra Jira/GitHub và metadata thật, không suy đoán hoặc để placeholder khi có thể tra được**.

Trong task này được tổ chức lại docs trong phạm vi dưới đây, commit các thay đổi BE1 đã được review cùng docs liên quan, push nhánh feature và tạo/cập nhật PR vào develop. Đích đưa code vào develop phải qua PR và các điều kiện review/CI thực tế. Không tự push trực tiếp develop, force-push, bypass rules hoặc tạo approval giả. Chỉ merge khi mọi điều kiện thực tế của repo và checklist bắt buộc đã được đáp ứng; nếu còn blocker thì giữ PR draft/blocked và ghi rõ lý do, không tuyên bố code đã vào develop.

## 2. Baseline và các nguồn phải đọc

- Workspace: `D:/OISMS`.
- Remote đã kiểm tra: `https://github.com/khoalvd839764-netizen/OISMS.git`.
- Nhánh hiện tại: `feature/OISMS-40-loi-van-hanh-kho-va-don-hang`.
- HEAD đầu vào: `95cbbdbe220dcfca2ca47ac88f465e8bf1e41961` **cộng working tree chưa commit** đã được review. Không checkout/reset về HEAD hoặc mặc định worktree mới từ HEAD đã có đủ code nghiệm thu.
- Base PR: **develop**. Kiểm tra lại branch/remote và fetch trạng thái mới trước khi đóng gói PR.
- Có nhiều file source/schema/test/docs modified và untracked đã được nghiệm thu; không coi untracked là file thừa. Hai thư mục `docs/11-ui-screens/`, `docs/12-error-and-status-codes/` là thay đổi có sẵn ngoài phạm vi mặc định; giữ nguyên, không tự đưa vào PR BE1.
- Nếu `.codegraph/` tồn tại, dùng CodeGraph trước khi tìm/đọc source. Backend trước đây không được index trả đúng; nếu vẫn thiếu, ghi nhận và đọc source, không tự index. Việc đọc/tổ chức Markdown không yêu cầu index code.

Đọc và đối chiếu:

1. `.github/pull_request_template.md`: nguồn chuẩn PR cụ thể của repo.
2. `.github/workflows/ci.yml` và `docs/07-devops-and-deployment/02-ci-cd-github-actions.md`: đọc workflow thực thi hiện tại; không chỉ dựa mô tả docs.
3. `docs/08-sprints/sprint-3/README.md`: phân công BE1 và phạm vi liên quan.
4. `docs/09-personal-reports/dang-khoa/README.md`, toàn bộ `sprint-3/README.md`, các brief/review Task 5/6 và tài liệu Sprint 2 liên quan như đầu vào lịch sử.
5. Kết luận hiện hành Task 5: `sprint-3/S3-BE1-T05-REVIEW-R5.md`; Task 6: `sprint-3/S3-BE1-T06-REVIEW-R3.md` và mục 44 của README Sprint 3. Sau khi move phải cập nhật đường dẫn tương ứng.

### Tra cứu MCP bắt buộc trước khi điền PR

- Dùng MCP Jira/Atlassian hiện có của Anti để tra **OISMS-40**: tên task thật, project/parent liên quan, assignee, mô tả, AC, trạng thái, deadline và URL. Nếu task dẫn tới task con, tra các task thật cần thiết để tổng kết đúng phạm vi. Không tự tạo mã Jira mới.
- Dùng MCP GitHub nếu có để đọc repo, PR đang mở theo head/base, quy tắc/ruleset/branch protection, CI và quyền merge. Nếu không có khả năng phù hợp, dùng CLI/API/browser đang được phép. Không bịa tên MCP tool.
- Nếu MCP lỗi/thiếu quyền: ghi lỗi và metadata nào chưa xác minh; hoàn tất phần docs/commit có thể làm độc lập. Không bịa deadline, người review, trạng thái Jira hoặc URL PR.
- Bước tra cứu không có nghĩa đã chuyển Jira sang In Review hay đăng bình luận Jira. Chỉ tick các checklist đó nếu kiểm tra thấy đã thực hiện; không tự gửi bình luận/thông báo Jira trong task tra cứu này.

## 3. Mục tiêu cấu trúc tài liệu

Phạm vi tái cấu trúc chính là `docs/09-personal-reports/dang-khoa/sprint-3/`. Giữ thư mục Sprint 2 và báo cáo các thành viên khác; chỉ sửa link ở nơi khác khi trực tiếp cần vì đường dẫn tài liệu được di chuyển.

Cấu trúc đích:

```text
docs/09-personal-reports/dang-khoa/
  README.md                         # mục lục cá nhân, link tổng kết Sprint 3
  sprint-3/
    README.md                       # index ngắn, trạng thái hiện hành và link
    SUMMARY.md                      # một file tổng kết chính thức phần Đăng Khoa/BE1
    codex-antigravity/
      README.md                     # quy ước giao tiếp, bảng task, mapping tài liệu cũ/mới
      task-01-contract-foundation/
        README.md
        handoffs/...
      task-02-pos-checkout/
        README.md
        briefs/...
        handoffs/...
        reviews/...
      task-03-order-lifecycle/
        README.md
        briefs/...
        handoffs/...
        reviews/...
      task-04a-stock-transfer/
        README.md
        briefs/...
        handoffs/...
        reviews/...
      task-04b-stock-count/
        README.md
        briefs/...
        handoffs/...
        reviews/...
      task-05-webhook-realtime/
        README.md
        briefs/...
        handoffs/...
        reviews/...
      task-06-reservation-expiry/
        README.md
        briefs/...
        handoffs/...
        reviews/...
      docs-pr-develop/
        brief.md                    # prompt này
        README.md                   # kết quả docs/commit/PR, URL và blocker thực tế
```

Chỉ tạo các file/thư mục con có nội dung thật. Dấu `...` là các phiên bản thực tế, không phải yêu cầu tạo file giả. Không bịa brief/review lịch sử cho Task 1–4 nếu trước đây chỉ có section trong README; khi trích xuất section có thật thì giữ metadata phiên bản/ngày và ghi nguồn section cũ. Có thể đặt tên file theo revision thật, nhất quán và dễ tìm.

### README giao tiếp Codex–Anti

- `codex-antigravity/README.md`: hướng dẫn nơi đặt brief, handoff, review, cách xác định bản mới nhất và tiến trình `READY → IMPLEMENTING → READY_FOR_REVIEW → VERIFIED / CHANGES_REQUESTED`. Link đến skill gốc, không sao chép/chỉnh quy tắc skill thành một bản mâu thuẫn. Task này không cài plugin hay bật vòng giao tiếp agent tự động.
- Mỗi task có README riêng: ID/mục tiêu/phạm vi, phiên bản hiện hành, baseline, người triển khai/reviewer, bảng finding có ID ổn định, bằng chứng test theo provider và link đến các vòng trước. README là index và trạng thái hiện hành; nội dung dài nằm trong handoffs/reviews.
- Di chuyển đầy đủ các brief/review Task 5/6 đang là file rời vào folder đúng task. Tách nội dung liên quan Task 1/2/3/4A/4B từ README monolithic, không để lịch sử bị mất.
- Giữ thứ tự lịch sử, ngày, phiên bản, AC, finding ID, kết luận reviewer và số liệu gốc. Handoff cũ READY_FOR_REVIEW/CHANGES_REQUESTED vẫn giữ như lịch sử, không sửa thành VERIFIED hồi tố. Index mới chỉ trỏ kết luận nghiệm thu cuối cùng.
- Không giữ thêm một bản monolithic vài nghìn dòng ở một tên archive khác để né việc chia tài liệu; không nhân đôi cùng một kết luận làm hai nguồn hiện hành.

### Một file SUMMARY.md

File tổng kết phải đọc được độc lập, viết tiếng Việt rõ ràng và dẫn đến bằng chứng chi tiết. Nội dung gồm:

1. Lê Võ Đăng Khoa, BE1/Lead, Sprint 3, mã Jira đã tra MCP; phạm vi được giao và phạm vi thực sự đã nghiệm thu.
2. Bảng kết quả: Task 1 Contract/Foundation **DONE theo báo cáo gốc**, không tự đổi thành VERIFIED nếu thiếu biên bản tương ứng; Task 2 **VERIFIED v1.2**, Task 3 **VERIFIED v1.1**, Task 4A **VERIFIED v1.2**, Task 4B **VERIFIED v1.3**, Task 5 **VERIFIED v1.4**, Task 6 **VERIFIED v1.2**. Link đúng review cuối.
3. Bàn giao kỹ thuật: POS/Order lifecycle, transfer, stock count, webhook/realtime, expiry; API/contracts/schema/provisioning, cách bật/tắt worker, test/reproduce và giới hạn cần biết.
4. Nguồn kiểm chứng hiện hành: Codex R3 Task 6 chạy mới build 0 warning/error, 129/129 unit PASS; full integration **260/261 PASS, 1 FAIL, 0 skipped, 13m48s**; trong full có 11 ReservationExpiry và 26 Webhook/Hub/Simulator liên quan PASS. Đây là một lượt full, không cộng thêm 37 thành lượt độc lập.
5. B-01 còn mở: `PostgresBackend2RegressionTests.Postgres_Warehouse_ChangeBranch_WhenHasHistory_ReturnsControlled409_AndCleanWarehouseSucceeds`, expected 200/actual 500 khi đổi branch kho trống, ngoài phạm vi nghiệm thu Task 6. Không tuyên bố toàn solution 100% xanh.
6. Đối chiếu kế hoạch Jira/Sprint với phần đã review: full Simulator, thiết kế AI, daily aggregation, phần BE2/GET/UI hoặc production chỉ ghi hoàn tất nếu có bằng chứng riêng; nếu chưa xác minh ghi rõ chưa thuộc nghiệm thu này. Không suy từ “Task 6 đã VERIFIED” thành “toàn bộ phần việc mọi người hoặc toàn dự án đã hoàn tất”.
7. Link PR, branch/base, commit cuối và trạng thái merge thực tế khi đã có; chưa merge thì ghi rõ PR/DRAFT/CI_BLOCKED, không ghi code đã ở develop.

## 4. Giữ khả năng truy vết và liên kết

- Lập mapping `đường dẫn/section/anchor cũ → đường dẫn/anchor mới` trong README giao tiếp. Đối chiếu toàn bộ section của README cũ trước/sau để không sót nội dung. Không cắt tài liệu theo số dòng mù quáng.
- Cập nhật link Markdown, ảnh, prompt launcher và tham chiếu repository-relative bị ảnh hưởng trong repo. GitHub phải mở được các link tài liệu được bàn giao, không phụ thuộc `D:/OISMS` của máy cá nhân.
- Với anchor README cũ đã được dùng từ nơi khác, ưu tiên cập nhật toàn bộ tham chiếu trong repo; giữ các anchor chuyển tiếp cần thiết ở index cũ để người đọc lịch sử có thể đến đúng file mới. Không xóa lịch sử chỉ vì có kết luận mới.
- Raw logs/TRX/probe trong `tmp` là local evidence; không commit nguyên thư mục tmp hoặc để link publish giả vờ GitHub đọc được file ignored. Có thể thêm evidence summary nhỏ đã rà dữ liệu nhạy cảm: command, provider, kết quả thật, nguồn reviewer/revision và thời điểm. Với log chỉ có local, ghi rõ local-only và cung cấp bước reproduce; không bịa log mới hoặc coi summary là raw log.
- Giữ UTF-8 sạch, không null bytes/mojibake. Không tạo hàng loạt file placeholder rỗng.

## 5. Đóng gói Git và PR vào develop

### 5.1. Bảo toàn code đã VERIFIED và phân loại thay đổi

- Ghi `git status`, branch/HEAD, staged diff, unstaged diff và untracked trước làm; lập danh sách file BE1 sẽ đưa vào PR. Xét cả commits có sẵn trên feature branch so với develop, không chỉ các file vừa sửa trong task docs.
- Ghi hash source application/tests trước và sau tái cấu trúc. Task docs không được thay đổi hành vi, test assertions, package versions hoặc SQL nghiệp vụ đã nghiệm thu. Chỉ đóng gói implementation BE1 hiện có, không viết lại core để làm đẹp PR.
- Không `git reset --hard`, `git clean`, tự stash/ghi đè thay đổi người khác, force-push hay amend commit đã push. Không dùng `git add .`/`git add -A` toàn repo; stage danh sách file đã phân loại.
- Không commit `.env`, secrets/tokens, credentials MCP, bin/obj/node_modules, tmp, raw TRX/test output hoặc dữ liệu runtime. Không đưa các thư mục docs ngoài phạm vi đã nêu vào chỉ vì chúng đang untracked.
- File tracked có thể dùng git mv; file untracked dùng thao tác move phù hợp, xác minh đường dẫn tuyệt đối nằm trong repo. Không nhầm `git mv` thất bại với quyền xóa bỏ file untracked.

### 5.2. Đồng bộ base và commit

- Tra/fetch origin và kiểm tra develop thật tồn tại. Giữ nhánh feature hiện tại nếu đúng task; tên đang phù hợp template `feature/OISMS-xxx-...`. Không tạo nhánh mới chỉ để đổi tên tùy ý.
- Commit đúng phạm vi được giao trước khi tích hợp base nếu cần bảo toàn working tree. Mỗi commit mới có mã Jira và mô tả tiếng Việt theo template repo, ví dụ:
  - `[OISMS-40] feat(backend): Hoàn thiện webhook, realtime và tự hủy giữ chỗ`
  - `[OISMS-40] docs: Tách tài liệu giao tiếp và tổng kết Backend 1 Sprint 3`
- Chọn số commit theo thay đổi thật, không bắt buộc tạo hai commit nếu phân chia không hợp lý. Không sửa lại lịch sử của người khác để làm tất cả commit cũ giống mẫu mới.
- Nếu phải đồng bộ develop, ưu tiên merge vào nhánh feature đã bảo toàn thay đổi, tránh rewrite remote branch. Giải quyết conflict theo contract/source hiện hành; không chọn ours/theirs hàng loạt. Nếu conflict thay đổi core/schema/test thì không còn là docs-only: ghi rõ và chạy lại kiểm chứng thích hợp.
- So diff cuối với **origin/develop mới fetch**, bảo đảm PR không lẫn code/thành viên khác ngoài phạm vi chủ động giao.

### 5.3. Tạo/cập nhật PR

- Ưu tiên MCP GitHub để tìm PR đang mở cùng head/base và cập nhật, tránh PR trùng. Nếu cần dùng gh thì kiểm tra CLI/auth trước; môi trường Codex lúc soạn prompt chưa tìm thấy gh trong PATH, không mặc định Anti thiếu MCP hoặc không thể tạo PR.
- Repository `khoalvd839764-netizen/OISMS`, head nhánh feature thật, **base develop**.
- Tiêu đề gợi ý, chỉnh theo scope cuối: `[OISMS-40] Hoàn thiện lõi Backend 1 Sprint 3 và chuẩn hóa tài liệu bàn giao`.
- PR body bám đầy đủ `.github/pull_request_template.md`, tiếng Việt: Jira task/link/deadline/assignee từ MCP; danh mục feature/test/docs đúng diff cuối; API bàn giao có thật; schema/worker/realtime; evidence theo provider; giới hạn và B-01; checklist đúng trạng thái.
- Không tự tick checklist Jira In Review/đã dán link PR/đúng deadline nếu chưa có bằng chứng. Không bịa ảnh Swagger hoặc tên test. Thiếu thủ tục bắt buộc thì ghi pending và giữ draft phù hợp.
- Nếu dùng gh, ghi body UTF-8 ra file rồi dùng --body-file, không nối multiline shell có nguy cơ lỗi dấu/escape. Nếu môi trường Codex có tool attach_artifact, gắn URL PR thực sau khi tạo; nếu Anti không có tool này, trả URL thật để Codex gắn sau, không giả đã attach.

### 5.4. CI, review và điều kiện đưa code vào develop

- Workflow hiện tại chạy `dotnet test src/backend/OISM.sln` cùng build frontend. **B-01 còn fail local nên có thể làm required CI đỏ**. Không dùng kết luận VERIFIED phạm vi Task 6 làm lý do bỏ qua CI của PR.
- Đọc checks/ruleset/required reviews của chính PR/commit cuối. Không tự sửa B-01, bỏ/skip/nới assertion, filter test khỏi CI, thêm continue-on-error hoặc tắt branch protection để merge.
- Chỉ tái cấu trúc docs và source hashes không đổi: kế thừa kết quả reviewer R3 đúng nguồn/commit-working-tree, chạy kiểm links/docs; không lặp full suite local vô ích. CI vẫn phải chạy trên PR head. Nếu merge base/conflict hoặc source/hosting/schema/test thay đổi: chạy checks phù hợp và full regression một lượt, phân biệt baseline/new failure.
- Nếu checks đang chạy, chờ kết quả khi môi trường cho phép. Nếu CI/required review/checklist còn chặn, tạo/giữ **Draft PR** hoặc trạng thái blocked đúng thực tế, ghi check/link/SHA và phần cần xử lý. Không tự merge bằng quyền admin hoặc push thẳng develop.
- Khi mọi điều kiện thực tế đã đủ và quyền merge hợp lệ, merge qua GitHub bằng phương thức repo cho phép; bảo toàn working tree ngoài phạm vi, không tự xóa nhánh/local data người dùng đang cần. Nếu chưa merge được, đầu ra phải nói rõ **PR đã tạo, develop chưa nhận code**, cùng blocker cụ thể. Không xin lại quyền cho các thao tác commit/push/tạo PR đã được giao; chỉ báo khi thật sự cần quyết định mới ngoài scope.

## 6. AC cố định và cách kiểm

| AC | Đầu ra cần đạt | Kiểm chứng |
| --- | --- | --- |
| AC-01 | MCP metadata Jira/GitHub thật, baseline và danh mục file rõ | Ghi mã/URL task đã tra, branch/SHA/dirty tree, PR hiện có/rules đã đọc; phần không truy cập được ghi đúng giới hạn. |
| AC-02 | Tài liệu được chia theo task/vòng, có README giao tiếp | Mapping toàn bộ section/file cũ; không mất lịch sử, AC/finding/status reviewer; không còn README monolithic làm nguồn trạng thái hiện hành. |
| AC-03 | Một SUMMARY.md đúng phần BE1 | Đối chiếu final review/Jira/Sprint; trạng thái đúng revision, count/provider chính xác, B-01 và phạm vi chưa nghiệm thu rõ. |
| AC-04 | Links/anchors/encoding dùng được trên GitHub | Kiểm link nội bộ và mapping cũ/mới, không link công bố vào file tmp ignored; UTF-8 sạch; code fences/HTML anchors hợp lệ. |
| AC-05 | Code VERIFIED và thay đổi ngoài scope được bảo toàn | Hash source/test trước/sau docs khớp; staged diff không có secrets/generated/unrelated files; mọi thay đổi thêm vì sync base được liệt kê và kiểm riêng. |
| AC-06 | Commit/push/PR đúng repo và base develop | Commit mới đúng mã Jira/tiếng Việt, không rewrite; URL PR thật hoặc PR cũ được cập nhật; head/base/SHA và diff cuối được đối chiếu. |
| AC-07 | Quy tắc merge được tuân thủ, không giả CI xanh | Checks/reviews/checklist đúng SHA; merge chỉ khi đủ điều kiện, nếu B-01 hoặc thủ tục khác chặn thì draft/blocked có evidence, không bỏ qua bảo vệ. |
| AC-08 | Bàn giao có thể kiểm lại | README docs-pr-develop ghi tree, mapping, kết quả docs/hash/checks, commit IDs, URL/head/base, trạng thái PR/merge, blockers và metadata chưa xác minh. |

## 7. Bàn giao cuối

Cập nhật `codex-antigravity/docs-pr-develop/README.md` với bảng AC-01..AC-08 và kết quả thật. Không tự VERIFIED task tái cấu trúc này thay Codex. Các task nghiệp vụ đã được reviewer VERIFIED phải giữ nguyên bằng chứng/phiên bản, không coi đó là quyền tự chứng nhận chất lượng docs mới.

Trả lời người dùng ngắn gọn gồm:

1. Link README index và SUMMARY.md mới.
2. Những nội dung đã chuyển và link mapping.
3. Commit IDs, branch/head/base và URL PR thật.
4. PR là Draft/Ready/Merged; code đã vào develop hay chưa.
5. Kết quả CI/test/docs và blocker còn lại, đặc biệt B-01 nếu vẫn chặn.

Không mở task nghiệp vụ mới, cài hệ thống agent tự động, sửa phần BE2/frontend hoặc triển khai production. Nếu không thể tạo/merge PR do thiếu quyền/CI, hoàn tất phần local trong phạm vi và báo đúng điểm chặn; không ghi hoàn tất bước chưa thực hiện.
