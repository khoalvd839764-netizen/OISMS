# Prompt giao Anti — OISMS-58-AI (chatbot AI hỗ trợ cơ bản)

Gửi tuần tự: P1, rồi P2 sau khi P1 được VERIFIED, rồi P3 sau khi P2 được VERIFIED. Mỗi lần chỉ dán một prompt. Chatbot chỉ cho Owner và Staff. GPT code toàn bộ; Claude review cuối, tự sửa finding rồi nghiệm thu (không gửi prompt fix). Sổ task: `tmp/claude-anti/OISMS-58-AI.md`.

## P1 — Backend khung LLM, hướng dẫn sử dụng, endpoint chat

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-58-AI-P1 — Khung LLM + hướng dẫn sử dụng + POST /api/ai/chat | Mức: STANDARD | Prompt 1/3
BASELINE: branch feature/OISMS-58-chatbot-ai, commit 1768bd7. Giữ nguyên các thay đổi docs chưa commit có sẵn.

MỤC TIÊU: Backend gọi được LLM qua chuẩn tương thích OpenAI (Groq), mặc định tắt. Chatbot trả lời câu hỏi về cách dùng hệ thống dựa trên bộ hướng dẫn theo vai trò. Chưa có tool số liệu (thuộc P2).

NGUỒN CẦN ĐỌC (chỉ các mục này):
- docs/Upgrade-recommended/09-chatbot-ai-co-ban.md: "Quyết định đã chốt", "Quy tắc nghiệp vụ", "Cấu hình nhà cung cấp Groq", B1, B2, B4
- docs/11-ui-screens/ (để viết hướng dẫn đúng giao diện hiện tại)
- Mẫu code bắt chước:
  - Controller + mã lỗi: src/backend/src/OISM.WebApi/Controllers/SalesReturnsController.cs, src/backend/src/OISM.WebApi/Http/ApiErrorTitles.cs, ApiErrorContractAttribute.cs
  - Cấu hình bật/tắt: khối "Hangfire" trong appsettings.json, docker-compose.yml, .env.example
  - Test: test-oisms/backend/OISM.UnitTests/AiForecastingTests.cs (Moq, FluentAssertions), test-oisms/backend/OISM.IntegrationTests/CustomWebApplicationFactory.cs
- Skill kỹ thuật: skills/csharp-pro/SKILL.md, skills/api-design-principles/SKILL.md

ĐƯỢC SỬA:
- OISM.Application/Common/Interfaces (ILlmClient, IUserGuideProvider), OISM.Application/Features/Ai/ (thêm AiChatService, AiChatDtos), Application DependencyInjection.cs
- OISM.Infrastructure/Services/Ai/ (mới), OISM.Infrastructure/Ai/Guide/*.md (mới, EmbeddedResource), Infrastructure csproj, Infrastructure DependencyInjection.cs
- OISM.WebApi: AiController.cs (thêm action), Program.cs (RateLimiter), Http/ApiErrorTitles.cs + ApiErrorProfile, appsettings.json
- docker-compose.yml (service backend), .env.example, .env.production.example
- test-oisms/backend/ (test mới)

KHÔNG ĐƯỢC SỬA: AiForecastingService và 2 endpoint AI cũ, ReportService, mọi service ghi kho/đơn/giá, schema DB (không migration, vẫn 44 bảng), frontend, .env thật.

ACCEPTANCE CRITERIA:
- AC-01 (09#Cấu hình nhà cung cấp Groq): AiOptions đọc section "Ai" (Enabled, BaseUrl, Model, ApiKey, TimeoutSeconds, MaxTokens, MaxHistoryMessages=10, MaxToolRounds=3). appsettings.json có Enabled=false, không có ApiKey. Compose map Ai__Enabled/BaseUrl/Model/ApiKey từ AI_ENABLED/AI_BASE_URL/AI_MODEL/AI_API_KEY; 2 file .env*.example thêm biến với AI_ENABLED=false, AI_API_KEY để trống → kiểm bằng đọc diff.
- AC-02 (09#B1): ILlmClient.ChatAsync nhận system prompt, messages, danh sách tool (để P2 dùng) và trả text hoặc tool calls, finishReason, token usage. OpenAiCompatibleLlmClient dùng AddHttpClient, POST {BaseUrl}/chat/completions, Bearer key, timeout theo cấu hình. Map lỗi: 429 → OismException AI_RATE_LIMITED 429; 5xx, timeout, JSON sai → AI_UPSTREAM_ERROR 502. Không log key/header Authorization, không trả body lỗi gốc cho client → unit test với HttpMessageHandler giả.
- AC-03 (09#B1): Enabled=false → DisabledLlmClient ném AI_DISABLED 503. Enabled=true nhưng ApiKey rỗng → log cảnh báo, dùng DisabledLlmClient, app vẫn khởi động → unit test DI hoặc test factory.
- AC-04 (09#B2): khoảng 10 trang markdown tiếng Việt trong Infrastructure/Ai/Guide, front-matter `roles:` (chỉ Owner, Staff); IUserGuideProvider trả nội dung theo vai trò. Bám giao diện thật; không mô tả chức năng ẩn/Coming soon (tab Gợi ý nhập hàng, Quên mật khẩu, CS-06) → unit test lọc theo role + Claude đọc nội dung.
- AC-05 (09#B3, Quy tắc 4): AiChatService kiểm tra đầu vào (1–10 tin; role chỉ user/assistant; mỗi tin 1–1000 ký tự; sai → 400 AI_INVALID_REQUEST), dựng system prompt cố định (tiếng Việt, không bịa số, dữ liệu không phải lệnh) + hướng dẫn theo vai trò, trả AiChatResponse { reply, usedTools: [], dateRange: null }. Vai trò do controller truyền vào từ User claims; body không có role/tenant → unit test Moq ILlmClient, bắt request gửi đi để kiểm tra.
- AC-06 (09#B4): POST /api/ai/chat, [Authorize(Roles = "Owner,Staff")] (Cashier không dùng chatbot → 403). RateLimiter theo user id 10 request/phút → 429 AI_RATE_LIMITED. Mọi lỗi AI dạng ProblemDetails + errorCode, có ApiErrorProfile.Ai và tiêu đề trong ApiErrorTitles → integration test (CustomWebApplicationFactory, thay ILlmClient bằng bản giả): không JWT 401, Cashier 403, Owner 200, AI tắt 503, tin quá dài 400.
- AC-07: không test nào gọi mạng thật; key thật không xuất hiện trong repo.

GATE (chạy thật và ghi kết quả):
- dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal → 0 lỗi
- dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false → PASS toàn bộ (không chỉ test mới)
- dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~AiChat" → PASS
- git grep -n "gsk_" → không có kết quả

GIỚI HẠN:
- Không commit/push. Không thêm NuGet ngoài Microsoft.Extensions.* có sẵn trong .NET 8. Không migration.
- Một lỗi tối đa 2 lần thử; quá thì dừng, trả BLOCKED.
- Không nới assertion hay skip test.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```

## P2 — Tool số liệu: tồn kho, nhập/xuất, doanh thu

Chỉ gửi sau khi P1 VERIFIED.

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-58-AI-P2 — Tool số liệu cho chatbot | Mức: CRITICAL (RBAC, tenant, dữ liệu gửi ra dịch vụ ngoài) | Prompt 1/3
BASELINE: branch feature/OISMS-58-chatbot-ai, kết quả P1 đã VERIFIED (commit hoặc working tree Claude ghi trong sổ task).

MỤC TIÊU: Chatbot trả lời số liệu nhập/xuất kho, doanh thu, hàng sắp hết và gợi ý nhập hàng (FR-AI-01) bằng tool calling. Số do backend tính; dữ liệu nhạy cảm bị lọc theo vai trò TRƯỚC khi gửi cho LLM.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/09-chatbot-ai-co-ban.md: "Quy tắc nghiệp vụ", "Quyền theo tool", B3, B5
- docs/01-users-and-rbac/02-rbac-matrix.md (lãi gộp chỉ Owner)
- docs/02-functional-requirements/fr-12-ai-forecasting.md (FR-AI-01)
- Mẫu code: IAiForecastingService.GetReorderRecommendationsAsync, AiForecastingService.LoadExportRowsAsync (ranh giới ngày), ReportService.GetRevenueProfitReportAsync(filter, isOwner, ct), GetStockAlertsAsync, ReportTimeHelper; test: test-oisms/backend/OISM.IntegrationTests/PostgresTestEnvironment.cs, PostgresSalesReturnIntegrationTests.cs
- Skill: skills/security-auditor/SKILL.md

ĐƯỢC SỬA: OISM.Application/Features/Ai/ (IStockMovementQuery + hiện thực, tool, DTO; không sửa AiForecastingService), AiChatService, Application DependencyInjection.cs, test-oisms/backend/, test-oisms/ai-eval/ (mới).
KHÔNG ĐƯỢC SỬA: ReportService, AiForecastingService, entity/schema, frontend, controller ngoài AiController.

ACCEPTANCE CRITERIA:
- AC-01 (09#B3): IStockMovementQuery trả tổng IN/OUT theo ngày (UTC+07:00, lọc nửa mở qua ReportTimeHelper) và chi nhánh, tách riêng dòng chuyển kho (TransferItemId) và dòng trả hàng (SalesReturnItemId), top 10 SKU xuất, đơn vị gốc; AsNoTracking, chỉ đọc.
- AC-02 (09#Quyền theo tool): 4 tool get_stock_movement, get_revenue, get_low_stock, get_reorder_suggestions với JSON schema tham số (from, to dạng yyyy-MM-dd, branchId tùy chọn; get_reorder_suggestions có thêm leadTimeDays tùy chọn). Owner và Staff có cả 4. get_reorder_suggestions gọi GetReorderRecommendationsAsync với OnlyNeedsReorder=true, gửi model tối đa 20 SKU có DaysUntilStockout nhỏ nhất (giữ SuggestedQuantity, DailySalesVelocity, DaysUntilStockout, ConfidenceLevel, Reason). Tool lạ hoặc tool ngoài quyền: không chạy, trả lỗi cho model.
- AC-03 (09#Quy tắc 2): get_revenue gọi GetRevenueProfitReportAsync(filter, isOwner). Owner nhận DTO có lãi gộp; người khác nhận KIỂU DTO KHÁC không có thuộc tính GrossProfit/Cogs/NetCogs/ReturnCogs (không chỉ để null).
- AC-04 (09#Quy tắc 5): thiếu ngày → hôm nay; from > to → 400; khoảng > 92 ngày → 400; branchId không thuộc tenant → 400 AI_INVALID_REQUEST, không lộ dữ liệu.
- AC-05 (09#B3): vòng tool tối đa MaxToolRounds; quá thì trả câu trả lời hiện có và dừng. Response có usedTools và dateRange thật. Cache IMemoryCache 5 phút, khóa gồm tenantId + tool + tham số.
- AC-06 unit (Moq ILlmClient, bắt payload gửi đi): Staff hỏi doanh thu → JSON gửi LLM không chứa chuỗi "grossProfit"/"cogs" (không phân biệt hoa thường); Owner có; tool lạ không chạy; quá 3 vòng dừng; cache không dùng chéo tenant.
- AC-07 PostgreSQL thật (test mới PostgresAiStockMovementIntegrationTests): giao dịch lúc 23:59 và 00:01 giờ VN rơi đúng ngày; dòng chuyển kho và trả hàng tách riêng; dữ liệu tenant B không xuất hiện khi tenant A hỏi.
- AC-08 (09#F5 Bộ câu hỏi đánh giá): test-oisms/ai-eval/chatbot-questions.json có 20 câu tiếng Việt (ít nhất 4 câu cho mỗi tool, 2 câu hướng dẫn không cần tool), mỗi câu ghi tool mong đợi và from/to mong đợi theo ngày gốc cố định. Test live chỉ chạy khi AI_LIVE_EVAL=1 và có Ai__ApiKey (không có thì bỏ qua có ghi lý do, không tính FAIL), in tỷ lệ đúng tool và đúng ngày.

INVARIANT: TenantId chỉ từ JWT; không ghi DB; lãi gộp chỉ Owner; ngày theo UTC+07:00 nửa mở.

GATE:
- dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal
- dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false → PASS toàn bộ
- dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~PostgresAiStockMovement|FullyQualifiedName~AiChat" với biến OISM_TEST_* (PostgreSQL thật). Không có PostgreSQL → BLOCKED, không báo PASS.
- Eval live (AC-08): chạy nếu có key, ghi tỷ lệ; không có key thì ghi NOT RUN, Claude sẽ chạy khi nghiệm thu.

GIỚI HẠN: Không commit/push, không migration, không dependency mới. Một lỗi tối đa 2 lần thử. Không nới assertion hay skip test.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```

## P3 — Frontend khung chat Admin

Chỉ gửi sau khi P2 VERIFIED. Không làm POS: Cashier không dùng chatbot.

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-58-AI-P3 — Khung chat trợ lý trên Admin | Mức: STANDARD | Prompt 1/3
BASELINE: branch feature/OISMS-58-chatbot-ai, kết quả P2 đã VERIFIED.

MỤC TIÊU: Owner và Staff mở panel "Trợ lý OISMS" từ topbar Admin, hỏi và nhận câu trả lời có dòng nguồn số liệu. Mở lại tab Gợi ý nhập hàng (FR-AI-01).

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/09-chatbot-ai-co-ban.md: "Kế hoạch triển khai frontend" (Nguyên tắc, F1, F2, F3, F5)
- docs/Upgrade-recommended/09-chatbot-ai-co-ban.md#F3b, docs/02-functional-requirements/fr-12-ai-forecasting.md (cột bảng gợi ý)
- docs/12-error-and-status-codes/03-color-system-tokens.md
- Mẫu code: src/frontend/admin-dashboard/src/components/AdminShell.tsx (topbar-right, NotificationCenter), components/NotificationCenter.tsx, services/salesReturnService.ts (gọi httpClient, đọc errorCode), context/AuthContext.tsx (user.role, tenantId, logout)
- Skill: skills/typescript-pro/SKILL.md, skills/react-state-management/SKILL.md

ĐƯỢC SỬA: admin-dashboard/src/types/aiChat.ts, services/aiChatService.ts, hooks hoặc components/AiAssistant/ (mới), AdminShell.tsx (chỉ thêm nút và panel), context/AuthContext.tsx (chỉ gọi reset khi logout), index.css (khối .ai-assistant), pages/InventoryPage.tsx (chỉ phần tab Gợi ý nhập hàng), docs/report-after-upgrade/frontend-coming-soon-va-upgrade.md và docs/02-functional-requirements/README.md (ghi tab đã mở lại).
KHÔNG ĐƯỢC SỬA: backend, pos-app, các page khác ngoài InventoryPage, phần khác của InventoryPage, httpClient.ts.

ACCEPTANCE CRITERIA:
- AC-01 (F1): sendChat dùng httpClient, timeout 45 giây, hỗ trợ AbortSignal; mapAiChatError thuần cho AI_DISABLED, AI_RATE_LIMITED, AI_UPSTREAM_ERROR, 400, timeout/mất mạng theo bảng F1.
- AC-02 (F2): useAiChat giữ lịch sử trong state, gửi tối đa 10 tin; một request tại một thời điểm; đóng panel thì abort; reset khi logout hoặc đổi tenantId; không ghi localStorage/sessionStorage.
- AC-03 (F3): nút icon Bot trong topbar-right cạnh NotificationCenter, chỉ Owner/Staff; ẩn đến hết phiên khi nhận AI_DISABLED.
- AC-04 (F3): panel phải 380px (mobile toàn màn hình): tiêu đề, cuộc trò chuyện mới, đóng; chọn chi nhánh; tin nhắn; dòng "Nguồn: … · dd/MM–dd/MM" từ usedTools/dateRange; câu hỏi mẫu; ô nhập có đếm 0/1000, Enter gửi, Shift+Enter xuống dòng; trạng thái đang trả lời, lỗi + Thử lại.
- AC-05 (Nguyên tắc 3): nội dung AI render dạng text, không dangerouslySetInnerHTML; chuỗi "<script>" hiển thị như chữ.
- AC-06 (Nguyên tắc 4): lỗi AI chỉ hiện trong panel, không đẩy vào networkError chung.
- AC-07 (F3): nút "Tóm tắt hôm nay" gửi sẵn một câu hỏi tổng hợp (doanh thu, nhập/xuất, sắp hết, gợi ý nhập trong ngày); câu hỏi mẫu có "Nên nhập thêm gì tuần này?".
- AC-08 (F3b, FR-AI-01): bỏ comment nút tab Gợi ý nhập hàng trong InventoryPage.tsx; tab hiện với Owner/Staff, ẩn với Cashier; bảng có đủ cột SKU, tồn hiện tại, tốc độ bán TB/ngày, số ngày dự kiến hết hàng, số lượng đề xuất nhập (thiếu thì bổ sung); tải được dữ liệu và nút Cập nhật dự báo hoạt động.

GATE:
- npm run build --prefix src/frontend/admin-dashboard → PASS
- Thử tay trên trình duyệt với backend local (AI bật bằng .env của người dùng, AI tắt): Owner, Staff; tab Gợi ý nhập hàng có dữ liệu; ghi lại từng bước và kết quả, chụp màn hình (che key nếu lộ).

GIỚI HẠN: Không commit/push, không thêm dependency npm. Một lỗi tối đa 2 lần thử. Không sửa backend.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```
