# OISMS-58-RAG — prompt giao Antigravity

Giao tuần tự: Prompt 1 → Claude nghiệm thu → Prompt 2. Sổ task: `tmp/claude-anti/OISMS-58-RAG.md`.

## Prompt 1 — Nội dung kho tài liệu

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-58-RAG-P1 — Viết kho tài liệu RAG cho Trợ lý OISMS | Mức: STANDARD | Prompt 1/3
BASELINE: branch feature/OISMS-58-chatbot-ai, commit ccb169d. Giữ nguyên thay đổi có sẵn.

MỤC TIÊU: tạo kho tài liệu dạng mục nhỏ để trợ lý truy xuất và trả lời đúng theo docs, không bịa.
Prompt này CHỈ viết nội dung và test kiểm tra kho; chưa sửa luồng chat.

NGUỒN CẦN ĐỌC (chỉ các mục này):
- docs/Upgrade-recommended/09-chatbot-ai-co-ban.md, mục "Đợt 4" (Quyết định, K1, K3 dòng "Unit, kiểm tra kho")
- Nguồn nội dung: docs/11-ui-screens/*.md (bỏ 07-handoff), docs/00-overview/02-core-domain-concepts.md,
  docs/04-architecture-and-design/04-state-machines.md, docs/01-users-and-rbac/02-rbac-matrix.md,
  docs/12-error-and-status-codes/02-error-codes-catalog.md, docs/02-functional-requirements/fr-13-ai-assistant.md
- Mẫu hiện có: src/backend/src/OISM.Infrastructure/Ai/Guide/*.md, test-oisms/backend/OISM.UnitTests/UserGuideProviderTests.cs

ĐƯỢC SỬA: tạo src/backend/src/OISM.Infrastructure/Ai/Knowledge/*.md; thêm EmbeddedResource "Ai/Knowledge/*.md"
vào OISM.Infrastructure.csproj; tạo test-oisms/backend/OISM.UnitTests/KnowledgeFilesTests.cs.
KHÔNG ĐƯỢC SỬA: Ai/Guide/ (giữ tới Prompt 2), AiChatService, UserGuideProvider, frontend, docs/.

ACCEPTANCE CRITERIA:
- AC-01 (09#Quyết định "Định dạng"): mỗi file một mục, front-matter YAML đúng 5 khóa
  id (kebab-case, không trùng), title, roles (tập con [Owner, Staff], không rỗng), keywords (3–12 từ, có cả dạng không dấu),
  source (đường dẫn file trong docs/ kèm #mục); thân 300–1.200 ký tự, tiếng Việt, câu ngắn.
- AC-02 (09#K1): 40–70 mục phủ đủ 4 nhóm: thao tác theo màn hình, khái niệm nghiệp vụ (tồn khả dụng, giữ chỗ,
  giá vốn bình quân, đơn vị tính và hệ số, trạng thái đơn, trả hàng), quyền theo vai trò, mã lỗi thường gặp + cách xử lý.
  Gộp nội dung 10 trang Ai/Guide/ vào kho mới.
- AC-03 (09#Quyết định "Nguồn nội dung"): mọi câu viết lại từ đúng file trong source; không thêm hiểu biết ngoài docs.
- AC-04 (09#Quyết định "Quyền"): roles theo 02-rbac-matrix.md; mục nói về giá vốn, lãi gộp, doanh thu qua trợ lý,
  kết nối sàn/secret, tài khoản nhận tiền, quản lý nhân viên chỉ Owner nếu ma trận nói vậy. Liệt kê các mục chỉ Owner trong bàn giao.
- AC-05 (09#Quyết định "Không đưa vào"): không có secret, key, mật khẩu, tên bảng/trigger/class, chức năng Coming soon hoặc đã ẩn
  (Handoff, Quên mật khẩu, in chứng từ CS-06...).
- AC-06 (09#K3 "Unit, kiểm tra kho"): KnowledgeFilesTests đọc embedded resource và kiểm: front-matter hợp lệ, id không trùng,
  roles hợp lệ, file source tồn tại (tìm root repo bằng cách đi lên tới thư mục có OISM.sln hoặc .git), độ dài thân,
  số mục 40–70, không chứa gsk_ / AIza / sk- / whsec_.
- AC-07: docs mâu thuẫn nhau hoặc mâu thuẫn mục "Hiện trạng" của docs → KHÔNG tự chọn, ghi bảng mâu thuẫn trong bàn giao.

GATE (chạy thật và ghi kết quả):
- dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal → 0 lỗi
- dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~KnowledgeFiles|FullyQualifiedName~UserGuide" → PASS
- Bảng trong bàn giao: id | title | roles | source (đủ mọi mục)

GIỚI HẠN:
- Không commit/push. Không thêm package. Không gọi API AI.
- Một lỗi tối đa 2 lần thử; quá thì dừng, trả BLOCKED. Không nới assertion hay skip test.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md, kèm bảng mục và bảng mâu thuẫn docs.
```

## Prompt 2 — Truy xuất, ghép prompt, hiển thị nguồn

Prompt 1 đã `VERIFIED` ngày 11/11/2026 (Claude tự sửa source/anchor 34 mục, 2 mục nội dung, parser và test anchor).

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-58-RAG-P2 — Truy xuất tài liệu cho Trợ lý OISMS | Mức: CRITICAL (lọc theo vai trò) | Prompt 1/3
BASELINE: branch feature/OISMS-58-chatbot-ai, commit ccb169d + working tree chưa commit của P1 (Ai/Knowledge/ 57 mục,
KnowledgeFilesTests.cs, csproj, plan 09 Đợt 4) đã được Claude nghiệm thu. Giữ nguyên, không sửa các file đó;
ngoại lệ duy nhất: KnowledgeFilesTests được đổi typeof(UserGuideProvider) thành typeof(KnowledgeBase).

MỤC TIÊU: trợ lý truy xuất mục tài liệu phù hợp theo câu hỏi và vai trò, chỉ trả lời hướng dẫn/nghiệp vụ từ mục đó,
không có thì từ chối bằng câu cố định; response trả danh sách nguồn để Admin hiển thị.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/09-chatbot-ai-co-ban.md, mục "Đợt 4" (Quyết định, K2, K3)
- Mẫu code: src/backend/src/OISM.Infrastructure/Ai/UserGuideProvider.cs (nạp resource),
  src/backend/src/OISM.Application/Features/Ai/Services/AiChatService.cs (ghép prompt),
  AiChatToolRunner.NormalizeName (bỏ dấu tiếng Việt kể cả đ/Đ)

ĐƯỢC SỬA: Application Common/Interfaces (IKnowledgeBase thay IUserGuideProvider), Features/Ai (Service, Dtos),
Infrastructure/Ai (KnowledgeBase thay UserGuideProvider, xóa Ai/Guide/ và resource cũ), DependencyInjection.cs,
test-oisms/backend (unit + AiChatEndpointTests), test-oisms/ai-eval, admin-dashboard: types/aiChat.ts, AiAssistantPanel.tsx,
docs/02-functional-requirements/fr-13-ai-assistant.md (thêm 1 đoạn về kho tài liệu).
KHÔNG ĐƯỢC SỬA: nội dung Ai/Knowledge/*.md (báo lại nếu cần sửa), tool số liệu, ReportService, LlmRouter, schema.

ACCEPTANCE CRITERIA:
- AC-01 (K2): IKnowledgeBase.Search(query, role, top) trả mục {Id, Title, Content, Score}; lọc roles TRƯỚC khi xếp hạng.
  Tách front-matter theo DÒNG "---" (như KnowledgeFilesTests), không Split("---"): anchor trong source có thể chứa "---".
- AC-02 (Quyết định "Truy xuất"): BM25 cục bộ (k1=1.2, b=0.75) trên title+keywords+thân đã bỏ dấu, chữ thường; cộng điểm
  khi token khớp keywords/title; không dependency mới, không gọi mạng.
- AC-03 (Quyết định "Thời điểm"): AiChatService truy xuất theo tin nhắn user cuối trước lần gọi model đầu; ghép tối đa 4 mục,
  tổng ≤ 3.000 ký tự, dạng "[id] title\nnội dung", dưới tiêu đề "Tài liệu tham chiếu"; không thêm lượt gọi model.
- AC-04 (Quyết định "Chống bịa"): dưới ngưỡng điểm (hằng số có tên, chỉnh bằng eval) → không ghép mục, ghép dòng
  "Không có tài liệu phù hợp."; SystemRules thêm: câu hỏi hướng dẫn/nghiệp vụ chỉ trả lời từ Tài liệu tham chiếu, cuối câu
  ghi mã mục đã dùng; không có thì trả đúng "Tài liệu OISMS chưa có thông tin này."; câu hỏi số liệu vẫn dùng tool.
- AC-05 (Quyết định "Hiển thị nguồn"): AiChatResponse thêm Sources (IReadOnlyList<AiChatSource(Id, Title)>, rỗng nếu không ghép);
  panel Admin thêm "Tài liệu: {title, …}" vào dòng nguồn hiện có.
- AC-06 (K3 "Unit, truy xuất"): "nhap hang the nao" tìm đúng mục nhập hàng; Staff không bao giờ nhận mục chỉ Owner
  (lặp mọi mục Owner, query = title của nó); "thời tiết hôm nay", "bầu cử" → 0 mục; giới hạn 4 mục/3.000 ký tự.
- AC-07 (K3 "Eval truy xuất"): test-oisms/backend/OISM.UnitTests/Ai/knowledge-questions.json ~30 câu có expectedIds +
  5 câu ngoài phạm vi; unit test đo top-3 hit ≥ 90% và 5/5 câu ngoài phạm vi trả 0 mục; in tỉ lệ ra output.
- AC-08: ai-eval và mọi test cũ dùng KnowledgeBase; UserGuideProviderTests thay bằng test KnowledgeBase tương đương
  (Staff không thấy mục Owner); test hiện có vẫn PASS, không nới assertion.

INVARIANT PHẢI GIỮ: role lấy từ JWT như cũ; Staff không nhận tool get_revenue; kho dùng chung mọi tenant, không chứa dữ liệu tenant.

GATE (chạy thật và ghi kết quả):
- dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal
- dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false (full suite)
- dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~AiChat"
- npm run build --prefix src/frontend/admin-dashboard
- Ghi top-3 hit rate và ngưỡng điểm đã chọn. Eval sống AI_LIVE_EVAL: NOT RUN (tốn hạn mức) trừ khi người dùng yêu cầu.

GIỚI HẠN:
- Không commit/push. Không thêm package. Không gọi API AI trong test.
- Một lỗi tối đa 2 lần thử; quá thì dừng, trả BLOCKED. Không nới assertion hay skip test.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```
