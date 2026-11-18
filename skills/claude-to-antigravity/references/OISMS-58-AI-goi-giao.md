# Gói giao việc GPT — OISMS-58-AI (chatbot AI hỗ trợ cơ bản)

Một gói duy nhất, GPT làm tuần tự từ Bước 0 đến Bước 8. Xong bước nào, qua gate bước đó thì mới sang bước sau. Hết Bước 8 thì GPT trả **một báo cáo cuối** theo mẫu ở cuối file. Claude review toàn bộ, tự sửa lỗi còn lại và nghiệm thu; GPT không nhận prompt sửa.

Gói này thay cho P1, P2, P3 trong `OISMS-58-AI-prompts.md` (giữ file đó để tham chiếu AC).

---

## Luật chung (áp dụng mọi bước)

- **Vai của bạn:** bạn đóng vai **Antigravity (Anti)** trong quy trình `skills/claude-to-antigravity/`. Mọi chỗ trong repo ghi "Anti" hoặc "Antigravity" là nói về bạn; "Claude" là người review và nghiệm thu cuối.
- Đọc `skills/claude-to-antigravity/references/antigravity.md` trước khi bắt đầu.
- **Nguồn yêu cầu:** `docs/Upgrade-recommended/09-chatbot-ai-co-ban.md`. Docs là yêu cầu, code là hiện trạng. Mâu thuẫn thì ghi vào báo cáo, không tự đổi yêu cầu.
- **Baseline:** branch `feature/OISMS-58-chatbot-ai`, commit `1768bd7`. Giữ nguyên các thay đổi docs chưa commit có sẵn (09-chatbot-ai-co-ban.md, Upgrade-recommended/README.md, sổ task trong tmp/).
- **Vai trò:** chatbot chỉ cho Owner và Staff. Cashier luôn nhận 403. Không sửa `pos-app`.
- **Không được:** commit, push, tạo PR; thêm migration hay đổi schema (vẫn 44 bảng); sửa service ghi kho/đơn/giá, `ReportService`, `AiForecastingService`; thêm NuGet ngoài `Microsoft.Extensions.*` của .NET 8; thêm package npm; ghi key thật vào bất kỳ file nào; gọi mạng thật trong unit/integration test.
- **Lỗi:** một lỗi thử tối đa 2 lần. Quá thì dừng toàn bộ gói ở bước đó, ghi `BLOCKED` và lý do vào báo cáo cuối. Không nới assertion, không skip test, không đổi AC để có PASS.
- **Nhật ký:** sau mỗi bước, nối thêm vào `tmp/claude-anti/OISMS-58-AI-gpt-log.md` (thư mục tmp/ không vào Git): bước, file đã đổi, lệnh gate, kết quả PASS/FAIL/NOT RUN, ghi chú.
- **Lệnh nền:** đặt `DOTNET_CLI_HOME=/tmp/oisms-dotnet-cli DOTNET_CLI_TELEMETRY_OPTOUT=1`; build/test .NET thêm `-p:UseAppHost=false`.
- **Gate chung (dùng lại ở nhiều bước):**
  - `G-BUILD`: `dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal` → 0 lỗi
  - `G-UNIT`: `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false` → PASS toàn bộ
  - `G-KEY`: `git grep -n "gsk_"` → rỗng

---

## Bước 0 — Chuẩn bị

- Kiểm tra `git branch --show-current` = `feature/OISMS-58-chatbot-ai`, `git log -1` = `1768bd7`. Sai thì dừng, báo `BLOCKED`.
- Đọc 09-chatbot-ai-co-ban.md các mục: Quyết định đã chốt, Quy tắc nghiệp vụ, Quyền theo tool, Cấu hình nhà cung cấp Groq, B1–B6, Kế hoạch triển khai frontend.
- Chạy `G-BUILD`, `G-UNIT` trên baseline, ghi số test PASS làm mốc. Baseline đã FAIL thì ghi lại, không sửa, tiếp tục.

**Qua bước khi:** đúng branch, đã có mốc build/test.

---

## Bước 1 — Khung LLM và cấu hình (chưa có endpoint)

**Mẫu bắt chước:** khối `Hangfire` trong `appsettings.json`, `docker-compose.yml`, `.env.example`; DI trong `OISM.Infrastructure/DependencyInjection.cs`.

**Được sửa:** `OISM.Application/Common/Interfaces/ILlmClient.cs` (mới), `OISM.Infrastructure/Services/Ai/` (mới: `AiOptions`, `OpenAiCompatibleLlmClient`, `DisabledLlmClient`), Infrastructure `DependencyInjection.cs`, `OISM.WebApi/appsettings.json`, `docker-compose.yml` (service backend), `.env.example`, `.env.production.example`, test mới.

**AC:**
- 1.1 `AiOptions` đọc section `Ai`: Enabled, BaseUrl, Model, ApiKey, TimeoutSeconds, MaxTokens, MaxHistoryMessages=10, MaxToolRounds=3. `appsettings.json` có `Enabled=false`, không có ApiKey.
- 1.2 Compose map `Ai__Enabled/BaseUrl/Model/ApiKey` từ `AI_ENABLED/AI_BASE_URL/AI_MODEL/AI_API_KEY` (mặc định false, `https://api.groq.com/openai/v1`, `openai/gpt-oss-120b`, rỗng). Hai file `.env*.example` thêm biến, `AI_ENABLED=false`, `AI_API_KEY=` để trống, ngay trên dòng `AI_API_KEY=` có chú thích: `# Các thành viên code hãy tự lấy API key riêng (Groq: console.groq.com/keys) và đặt vào .env, không commit.`
- 1.3 `ILlmClient.ChatAsync` nhận system prompt, danh sách message (user/assistant/tool), danh sách tool (tên, mô tả, JSON schema) và trả về text hoặc danh sách tool call (id, tên, arguments JSON), finishReason, token usage.
- 1.4 `OpenAiCompatibleLlmClient`: `AddHttpClient`, `POST {BaseUrl}/chat/completions`, header Bearer, timeout theo cấu hình. Map lỗi: 429 → `OismException("AI_RATE_LIMITED", …, 429)`; 5xx, timeout, JSON sai → `AI_UPSTREAM_ERROR` 502. Không log key hay header Authorization; không đưa body lỗi gốc vào message trả client.
- 1.5 `Enabled=false` → `DisabledLlmClient` ném `AI_DISABLED` 503. `Enabled=true` mà ApiKey rỗng → log cảnh báo, dùng `DisabledLlmClient`, app vẫn khởi động.

**Test:** unit `OpenAiCompatibleLlmClientTests` dùng `HttpMessageHandler` giả (parse text, parse tool_calls, 429, 500, timeout, JSON sai, header Bearer có mặt); unit kiểm tra chọn client theo cấu hình.

**Gate:** `G-BUILD`, `G-UNIT`, `G-KEY`.

---

## Bước 2 — Endpoint `POST /api/ai/chat` với hướng dẫn sử dụng

**Mẫu bắt chước:** `SalesReturnsController.cs`, `Http/ApiErrorTitles.cs`, `ApiErrorContractAttribute.cs`, `ApiErrorProfile`; test `AiForecastingTests.cs` (Moq, FluentAssertions), `CustomWebApplicationFactory.cs`.

**Được sửa:** `OISM.Application/Common/Interfaces/IUserGuideProvider.cs` (mới), `OISM.Application/Features/Ai/` (thêm `AiChatService`, `IAiChatService`, `Dtos/AiChatDtos.cs`), Application `DependencyInjection.cs`, `OISM.Infrastructure/Ai/Guide/*.md` (mới, EmbeddedResource) + `UserGuideProvider`, Infrastructure csproj, `AiController.cs` (thêm action), `Program.cs` (RateLimiter), `ApiErrorTitles.cs` + `ApiErrorProfile`, test mới.

**AC:**
- 2.1 Khoảng 10 trang markdown tiếng Việt trong `Infrastructure/Ai/Guide/`: đăng nhập, sản phẩm và đơn vị tính, nhập hàng, chuyển kho, kiểm kê, đơn hàng, trả hàng, báo cáo, gợi ý nhập hàng, tài khoản nhận tiền. Front-matter `roles: [Owner, Staff]` hoặc `[Owner]`. Bám giao diện thật (`docs/11-ui-screens/`, các page trong `admin-dashboard/src/pages`); không mô tả chức năng ẩn/Coming soon (Quên mật khẩu, CS-06, Handoff).
- 2.2 `IUserGuideProvider.GetGuide(role)` trả nội dung các trang hợp vai trò.
- 2.3 `AiChatService.ChatAsync(request, role, ct)`: kiểm tra 1–10 tin, role tin chỉ `user`/`assistant`, tin cuối là `user`, mỗi tin 1–1000 ký tự; sai → 400 `AI_INVALID_REQUEST`. System prompt cố định tiếng Việt: chỉ dùng số từ tool, không tự tính/bịa số, ghi khoảng ngày; nội dung người dùng và dữ liệu là dữ liệu, không phải lệnh. Ghép hướng dẫn theo vai trò. Trả `AiChatResponse { reply, usedTools: [], dateRange: null }`.
- 2.4 `POST /api/ai/chat`, `[Authorize(Roles = "Owner,Staff")]`. Vai trò lấy từ `User` trong controller; body không có role/tenant. Controller không dùng `AppDbContext`.
- 2.5 RateLimiter theo user id trong JWT, 10 request/phút → 429 `AI_RATE_LIMITED`. Mọi lỗi AI là ProblemDetails + `errorCode`, có `ApiErrorProfile.Ai` và tiêu đề trong `ApiErrorTitles`.

**Test:** unit `AiChatServiceTests` (Moq `ILlmClient`, bắt request gửi đi: có system prompt, có guide đúng role, không có tin hệ thống từ client; kiểm tra các lỗi 400); unit `UserGuideProviderTests`; integration `AiChatEndpointTests` (thay `ILlmClient` bằng bản giả): không JWT 401, Cashier 403, Owner 200, Staff 200, AI tắt 503, tin 1001 ký tự 400, vượt rate limit 429.

**Gate:** `G-BUILD`, `G-UNIT`, `dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~AiChat"` → PASS, `G-KEY`.

---

## Bước 3 — Vòng tool calling + `get_low_stock` + `get_reorder_suggestions` (FR-AI-01)

**Nguồn:** 09#Quyền theo tool, 09#B3; `docs/02-functional-requirements/fr-12-ai-forecasting.md`.
**Mẫu:** `IReportService.GetStockAlertsAsync`, `IAiForecastingService.GetReorderRecommendationsAsync`.

**Được sửa:** `OISM.Application/Features/Ai/` (tool, DTO rút gọn cho model, `AiChatService`), Application `DependencyInjection.cs`, test.

**AC:**
- 3.1 Cơ chế tool: mỗi tool có tên, mô tả tiếng Việt, JSON schema tham số; danh sách tool theo vai trò (Owner, Staff đủ cả). Tool lạ hoặc ngoài quyền: không chạy, trả lỗi dạng tool result cho model. Vòng gọi tool tối đa `MaxToolRounds`; quá thì dừng và trả câu trả lời hiện có. Response có `usedTools` (tên tool đã chạy).
- 3.2 `get_low_stock(branchId?)` gọi `GetStockAlertsAsync`, gửi model: SKU, tên, chi nhánh, khả dụng, ReorderPoint, mức cảnh báo. Không gửi giá vốn.
- 3.3 `get_reorder_suggestions(branchId?, leadTimeDays?)` gọi `GetReorderRecommendationsAsync` với `OnlyNeedsReorder = true`; gửi tối đa 20 SKU có `DaysUntilStockout` nhỏ nhất, giữ `DailySalesVelocity`, `DaysUntilStockout`, `SuggestedQuantity`, `ConfidenceLevel`, `Reason`, `BaseUnit`.
- 3.4 `branchId` không thuộc tenant hiện tại → 400 `AI_INVALID_REQUEST`, không lộ dữ liệu.
- 3.5 Cache `IMemoryCache` 5 phút cho kết quả tool, khóa gồm tenantId + tên tool + tham số đã chuẩn hóa.

**Test (unit, Moq):** tool lạ không chạy; quá 3 vòng dừng; payload gửi model không chứa "cost"/"giá vốn"; reorder chỉ 20 dòng và đúng thứ tự; cache không dùng chéo tenant; branchId tenant khác bị chặn.

**Gate:** `G-BUILD`, `G-UNIT`, `G-KEY`.

---

## Bước 4 — `get_stock_movement` (nhập/xuất kho)

**Nguồn:** 09#B3, Quy tắc 5. **Mẫu:** `AiForecastingService.LoadExportRowsAsync`, `ReportTimeHelper`; test `PostgresTestEnvironment.cs`, `PostgresSalesReturnIntegrationTests.cs`.

**Được sửa:** `OISM.Application/Features/Ai/` (`IStockMovementQuery` + hiện thực, tool), Application `DependencyInjection.cs`, test.

**AC:**
- 4.1 `IStockMovementQuery` (chỉ đọc, `AsNoTracking`): tổng IN và OUT theo ngày giờ VN (UTC+07:00, `CreatedAt >= start AND CreatedAt < endBoundary`) và theo chi nhánh; tách riêng dòng chuyển kho (`TransferItemId`) và dòng trả hàng (`SalesReturnItemId`); top 10 SKU xuất; số lượng theo đơn vị gốc.
- 4.2 Tham số `from`, `to` (yyyy-MM-dd, theo ngày VN): thiếu → hôm nay; from > to → 400; khoảng > 92 ngày → 400 `AI_INVALID_REQUEST`. Response chat có `dateRange` thật.

**Test PostgreSQL thật** `PostgresAiStockMovementIntegrationTests`: giao dịch 23:59 và 00:01 giờ VN rơi đúng ngày; chuyển kho và trả hàng tách riêng; tenant B không xuất hiện khi tenant A hỏi.

**Gate:** `G-BUILD`, `G-UNIT`, `dotnet test …IntegrationTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~PostgresAiStockMovement"` với biến `OISM_TEST_*` (xem `.github/workflows/ci.yml`). Không có PostgreSQL → ghi `BLOCKED` cho gate này, vẫn làm tiếp bước sau, Claude sẽ chạy khi nghiệm thu.

---

## Bước 5 — `get_revenue` có lọc vai trò + bộ câu hỏi đánh giá

**Nguồn:** 09#Quy tắc 2, `docs/01-users-and-rbac/02-rbac-matrix.md`. **Mẫu:** `IReportService.GetRevenueProfitReportAsync(filter, isOwner, ct)`.

**AC:**
- 5.1 `get_revenue(from, to, branchId?)` gọi `GetRevenueProfitReportAsync(filter, isOwner)`. Owner nhận DTO có lãi gộp. Staff nhận **kiểu DTO khác**, không có thuộc tính GrossProfit/Cogs/NetCogs/ReturnCogs (không chỉ để null). Doanh thu là doanh thu thuần đã trừ trả hàng (theo ReportService hiện tại).
- 5.2 Tham số ngày theo quy tắc 4.2.
- 5.3 `test-oisms/ai-eval/chatbot-questions.json`: 20 câu tiếng Việt (ít nhất 4 câu cho mỗi tool, 2 câu hướng dẫn không cần tool), mỗi câu ghi tool mong đợi và from/to mong đợi tính từ một ngày gốc cố định ghi trong file. Test live chỉ chạy khi `AI_LIVE_EVAL=1` và có `Ai__ApiKey`; không có thì bỏ qua kèm lý do (không tính FAIL); in tỷ lệ đúng tool và đúng ngày.

**Test (unit, Moq, bắt payload):** Staff hỏi doanh thu → JSON gửi model không chứa "grossProfit", "cogs" (không phân biệt hoa thường); Owner có grossProfit.

**Gate:** `G-BUILD`, `G-UNIT`, `--filter "FullyQualifiedName~AiChat"` PASS, `G-KEY`. Eval live: có key thì chạy và ghi tỷ lệ, không có thì `NOT RUN`.

---

## Bước 6 — Frontend: panel chat trên Admin

**Nguồn:** 09#Kế hoạch triển khai frontend (Nguyên tắc, F1, F2, F3). **Mẫu:** `AdminShell.tsx` (topbar-right, `NotificationCenter`), `components/NotificationCenter.tsx`, `services/salesReturnService.ts` (gọi `httpClient`, đọc `errorCode`), `context/AuthContext.tsx`.

**Được sửa:** `admin-dashboard/src/types/aiChat.ts`, `services/aiChatService.ts`, `components/AiAssistant/` (mới), `AdminShell.tsx` (chỉ thêm nút và panel), `context/AuthContext.tsx` (chỉ reset chat khi logout), `index.css` (khối `.ai-assistant`).

**AC:**
- 6.1 `sendChat` dùng `httpClient`, timeout 45 giây, có AbortSignal. `mapAiChatError` thuần: `AI_DISABLED` → ẩn nút đến hết phiên; `AI_RATE_LIMITED` → "Bạn hỏi hơi nhanh, thử lại sau ít phút", khóa gửi 30 giây; `AI_UPSTREAM_ERROR`/timeout/mất mạng → "Trợ lý AI tạm thời không trả lời được" + Thử lại; 400 → thông báo server; 401 → để luồng auth sẵn có xử lý.
- 6.2 `useAiChat`: lịch sử chỉ trong React state, gửi tối đa 10 tin; một request một lúc; đóng panel thì abort; reset khi logout hoặc đổi tenantId; không dùng localStorage/sessionStorage.
- 6.3 Nút icon Bot trong `topbar-right` cạnh `NotificationCenter`, chỉ Owner/Staff.
- 6.4 Panel phải 380px (mobile toàn màn hình): tiêu đề "Trợ lý OISMS", nút cuộc trò chuyện mới, nút đóng; chọn chi nhánh (mặc định Tất cả); danh sách tin; dòng "Nguồn: … · dd/MM–dd/MM" từ `usedTools`/`dateRange`; nút "Tóm tắt hôm nay"; câu hỏi mẫu ("Hôm nay nhập xuất bao nhiêu?", "Doanh thu 7 ngày qua?", "Nên nhập thêm gì tuần này?", "Cách tạo phiếu nhập hàng?"); ô nhập đếm 0/1000, Enter gửi, Shift+Enter xuống dòng; trạng thái đang trả lời; lỗi + Thử lại.
- 6.5 Câu trả lời AI render dạng text, không `dangerouslySetInnerHTML`; chuỗi `<script>` hiển thị như chữ.
- 6.6 Lỗi AI chỉ hiện trong panel, không đẩy vào `networkError` chung.

**Gate:** `npm run build --prefix src/frontend/admin-dashboard` → PASS.

---

## Bước 7 — Mở lại tab Gợi ý nhập hàng (FR-AI-01)

**Nguồn:** 09#F3b, `docs/02-functional-requirements/fr-12-ai-forecasting.md`.

**Được sửa:** `admin-dashboard/src/pages/InventoryPage.tsx` (chỉ phần tab Gợi ý nhập hàng), `docs/report-after-upgrade/frontend-coming-soon-va-upgrade.md`, `docs/02-functional-requirements/README.md`.

**AC:**
- 7.1 Bỏ comment khối nút tab có chú thích "Tạm ẩn tab Gợi ý nhập hàng". Tab hiện với Owner/Staff, ẩn với Cashier.
- 7.2 Bảng có đủ cột: SKU, tồn hiện tại, tốc độ bán TB/ngày, số ngày dự kiến hết hàng, số lượng đề xuất nhập. Thiếu thì bổ sung, không đổi logic gọi API.
- 7.3 Hai file docs ghi tab đã mở lại (ngày, branch).

**Gate:** `npm run build --prefix src/frontend/admin-dashboard` → PASS.

---

## Bước 8 — Docs, gate cuối, thử tay, báo cáo

**Được sửa:** `docs/01-users-and-rbac/02-rbac-matrix.md` (dòng chatbot: Owner ✓, Staff ✓, Cashier ❌ 403), `docs/02-functional-requirements/` (thêm `FR-AI-02` chatbot + README), `docs/05-api-contracts/04-order-endpoints.md` (`POST /api/ai/chat`), `docs/12-error-and-status-codes/02-error-codes-catalog.md` (`AI_DISABLED`, `AI_RATE_LIMITED`, `AI_UPSTREAM_ERROR`, `AI_INVALID_REQUEST`), `docs/07-devops-and-deployment/` (cách bật AI), `docs/11-ui-screens/` (mô tả panel chat).

**Gate cuối (chạy lại toàn bộ):**
- `G-BUILD`, `G-UNIT`, `G-KEY`
- `dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false` → ghi số PASS/FAIL so với mốc Bước 0 (test cần PostgreSQL mà không có môi trường thì ghi NOT RUN)
- `npm run build --prefix src/frontend/admin-dashboard`
- `git diff --stat` + `git status --short` (để Claude so phạm vi)

**Thử tay (nếu chạy được backend local với key trong `.env` của người dùng):** Owner hỏi doanh thu thấy lãi gộp; Staff không thấy; "Nên nhập thêm gì tuần này?" trả SKU từ gợi ý; "Tóm tắt hôm nay" hoạt động; tắt AI thì nút chat mất; Cashier gọi API nhận 403; tab Gợi ý nhập hàng có dữ liệu. Không chạy được thì ghi NOT RUN và lý do.

---

## Mẫu báo cáo cuối (GPT trả trong chat)

```text
TASK: OISMS-58-AI | Trạng thái: READY_FOR_REVIEW / BLOCKED (ở bước …)
BASELINE → HIỆN TẠI: 1768bd7 + thay đổi chưa commit
MỐC BƯỚC 0: build …, unit PASS x/y

TIẾN ĐỘ THEO BƯỚC:
| Bước | Trạng thái | AC đạt | Gate | Ghi chú |
| 1 | XONG/BLOCKED | 1.1–1.5 | G-BUILD PASS, G-UNIT PASS x/y, G-KEY rỗng | |
| … | | | | |

FILE ĐÃ ĐỔI: (git status --short)

KIỂM TRA ĐÃ CHẠY:
- {lệnh} | cwd | provider (InMemory/PostgreSQL) | PASS x/y, FAIL, NOT RUN | giới hạn

EVAL LIVE: model …, đúng tool x/20, đúng ngày x/20 (hoặc NOT RUN + lý do)
THỬ TAY: từng kịch bản → kết quả (hoặc NOT RUN + lý do)
LỆCH SO VỚI DOCS HOẶC GÓI GIAO: {không có / nêu cụ thể và lý do}
THỬ NGHIỆM THẤT BẠI: {giả thuyết → kết quả}
BLOCKER VÀ ĐIỀU KIỆN GỠ: {không có / nêu cụ thể}
NGOÀI SCOPE PHÁT HIỆN ĐƯỢC: {một dòng mỗi mục, chưa sửa}
```
