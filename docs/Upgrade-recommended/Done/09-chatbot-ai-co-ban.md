# 09. Chatbot AI hỗ trợ cơ bản

> Trạng thái: lập ngày 09/11/2026, **đã triển khai trên nhánh `feature/OISMS-58-chatbot-ai` (10/11/2026)**. Do không có kinh phí, API AI chỉ dùng gói miễn phí với hạn mức rất thấp; xem mục "Lưu ý: giới hạn hạn mức API AI". Đây không phải đề xuất của GVHD. Một người đảm nhận cả backend và frontend (khung chat trên Admin). Chatbot chỉ dành cho Owner và Staff.

## Lưu ý: giới hạn hạn mức API AI

Trợ lý OISMS đã được tích hợp và chạy được với Groq (`openai/gpt-oss-120b`) và Google Gemini (`gemini-3.5-flash`). **Nhóm không có kinh phí cho API AI**, nên bản demo chỉ dùng key gói miễn phí và hạn mức rất thấp:

| Nhà cung cấp | Hạn mức gói miễn phí (đo hoặc tra ngày 10/11/2026) | Ảnh hưởng thực tế |
| :--- | :--- | :--- |
| Groq | 30 request/phút, 8.000 token/phút, khoảng 200.000 token/ngày | Khoảng 40–60 câu hỏi số liệu mỗi ngày; "Tóm tắt hôm nay" có thể chạm giới hạn token/phút |
| Google Gemini (AI Studio Free Tier) | 20 request/ngày cho mỗi model | Khoảng 5–10 câu hỏi mỗi ngày, vì mỗi câu gọi model 2–4 lượt |

Hệ quả:
- Khi hết lượt, chatbot báo `AI_MODEL_QUOTA` ("Model … đã hết lượt của nhà cung cấp") và gợi ý hỏi lại bằng model còn lại. Đây là giới hạn của gói miễn phí, không phải lỗi hệ thống.
- Không dùng chatbot cho kiểm thử tải hay demo liên tục. Trước buổi demo nên chờ hạn mức ngày được làm mới (Groq theo giờ UTC, Gemini theo giờ Thái Bình Dương) và quay sẵn video dự phòng.
- Mọi chức năng không dùng AI vẫn chạy bình thường khi AI hết lượt hoặc tắt, kể cả tab Gợi ý nhập hàng (`FR-AI-01`, tính bằng công thức).
- Muốn tăng hạn mức: gắn tài khoản thanh toán cho key (Groq Developer tier, hoặc credit Google Cloud của gói Google AI Pro cho Gemini) rồi chỉ đổi biến môi trường, không cần sửa code.
- Gói miễn phí có thể dùng dữ liệu gửi lên để cải thiện model; chỉ dùng với dữ liệu demo.

## Vấn đề cần giải quyết

Chủ cửa hàng và nhân viên muốn hỏi nhanh bằng tiếng Việt như "hôm nay nhập xuất bao nhiêu", "doanh thu tuần này", "SKU nào sắp hết", "tạo phiếu nhập thế nào" mà không phải mở từng màn báo cáo. Chatbot chỉ **đọc và tóm tắt**; mọi số liệu do backend tính, AI chỉ diễn đạt lại.

## Quyết định đã chốt

| Câu hỏi | Quyết định | Nguồn |
| :--- | :--- | :--- |
| Phạm vi | Tóm tắt nhập/xuất kho, doanh thu, cảnh báo sắp hết hàng và hướng dẫn sử dụng. Bỏ phần soạn và gửi thông báo nội bộ. | Người dùng chốt 09/11/2026 |
| Quyền ghi | AI không có thao tác ghi nào: không đụng kho, đơn, giá, sổ cái. | Như trên |
| Vai trò được dùng | Chỉ Owner và Staff. Cashier không dùng chatbot (endpoint trả 403, POS không có khung chat). | Người dùng chốt 10/11/2026 |
| Gắn với `FR-AI-01` | Làm luôn trong task này: (1) tool `get_reorder_suggestions` dùng lại `AiForecastingService` để chatbot trả lời "nên nhập gì, bao nhiêu, vì sao"; (2) mở lại tab Gợi ý nhập hàng trong `InventoryPage.tsx` (giao diện bảng mà `FR-AI-01` yêu cầu); (3) LLM diễn giải lý do gợi ý, con số vẫn do công thức tính. | Người dùng chốt 10/11/2026 |
| Database | Không thêm bảng, không migration. Vẫn 44 bảng. Lịch sử chat do frontend giữ và gửi kèm mỗi request. | Như trên |
| Nhà cung cấp | Giai đoạn demo dùng **Groq** (gói free). Backend gọi qua chuẩn tương thích OpenAI để đổi nhà cung cấp chỉ bằng cấu hình. | Người dùng chốt 10/11/2026 |
| Nhánh | `feature/OISMS-58-chatbot-ai`, tạo từ commit trả hàng `1768bd7`. PR trả hàng merge vào `develop` trước (merge commit, không squash). | Như trên |
| Bật/tắt | `Ai:Enabled=false` mặc định. Khi tắt, endpoint trả 503 `AI_DISABLED`. CI không gọi API thật. | Đề xuất của nhóm |

## Hiện trạng trước khi làm (kiểm tra 09/11/2026)

| Nội dung | Hiện trạng |
| :--- | :--- |
| `AiController`, `AiForecastingService` | Có `/api/ai/export-history` và `/api/ai/reorder-recommendations` (Owner, Staff). Tính bằng quy tắc, chưa gọi LLM. |
| Doanh thu | `IReportService.GetRevenueProfitReportAsync(filter)`; `GetDashboardSummaryAsync(isOwner)` đã ẩn lãi gộp với người không phải Owner. |
| Cảnh báo tồn | `IReportService.GetStockAlertsAsync(branchId)`. |
| Nhập/xuất | `inventory_transactions.type` chỉ có `IN`, `OUT`. Chưa có query tổng hợp theo ngày cho chatbot. Dòng chuyển kho có `TransferItemId`. |
| Ngày nghiệp vụ | `ReportTimeHelper` (UTC+07:00, lọc nửa mở). |
| Hướng dẫn sử dụng | Chưa có tài liệu cho người dùng cuối; `docs/11-ui-screens/` là đặc tả màn hình. |
| HTTP client tới dịch vụ ngoài | Chưa có `AddHttpClient` hay RateLimiter trong backend. |

**Đã xử lý ở nhánh trả hàng:** trước commit `1768bd7`, `GET /api/reports/revenue-profit` không truyền `isOwner` nên có thể trả `GrossProfit` cho Staff. Commit trả hàng đã sửa (`GetRevenueProfitReportAsync(filter, isOwner, ct)`) và đổi doanh thu thành doanh thu thuần sau trả hàng. Chatbot vẫn tự lọc trường này, không dựa vào hành vi của API báo cáo.

## Quy tắc nghiệp vụ

1. **Số liệu từ backend:** AI chỉ được dùng số tool trả về. System prompt yêu cầu không tự tính, không đoán; câu trả lời ghi rõ khoảng ngày và chi nhánh đã dùng.
2. **Lọc theo vai trò trước khi gửi:** danh sách tool và trường dữ liệu lọc theo role trong JWT. Staff không bao giờ nhận `GrossProfit`, giá vốn, COGS.
3. **Tenant:** tool gọi qua service hiện có nên tenant lấy từ JWT (`ICurrentTenantService`). Prompt không chứa `TenantId`.
4. **Dữ liệu không phải lệnh:** tin nhắn người dùng, tên sản phẩm, tên chi nhánh là dữ liệu; prompt dặn AI không làm theo chỉ dẫn nằm trong dữ liệu.
5. **Ngày nghiệp vụ:** khoảng ngày theo UTC+07:00, lọc `CreatedAt >= start AND CreatedAt < endBoundary`. Mặc định "hôm nay" khi người dùng không nêu ngày; khoảng tối đa 92 ngày.
6. **Hạn mức:** gói free dùng chung một key cho mọi tenant, nên có rate limit theo user và cache kết quả tool.

### Quyền theo tool

| Tool | Dữ liệu | Owner | Staff |
| :--- | :--- | :-: | :-: |
| `get_stock_movement(from, to, branchId?)` | Tổng `IN`/`OUT` theo ngày, tách dòng chuyển kho và trả hàng, top SKU | ✓ | ✓ |
| `get_revenue(from, to, branchName?)` | Doanh thu, số đơn, theo kênh, theo ngày | ✓ có lãi gộp | ✗ (chốt 10/11/2026: Staff không hỏi doanh thu qua trợ lý) |
| `get_low_stock(branchId?)` | SKU chạm hoặc dưới `ReorderPoint`, số khả dụng | ✓ | ✓ |
| `get_reorder_suggestions(branchId?, leadTimeDays?)` | Từ `AiForecastingService`: tốc độ bán/ngày, số ngày dự kiến hết hàng, số lượng đề xuất nhập, độ tin cậy (`FR-AI-01`) | ✓ | ✓ |
| Hướng dẫn sử dụng | Nằm trong system prompt, lọc theo vai trò | ✓ | ✓ |

Cashier không dùng chatbot, khớp ma trận RBAC hiện tại (Cashier không xem báo cáo, bị cấm `FR-AI-01`). `02-rbac-matrix.md` chỉ cần thêm dòng chatbot: Owner ✓, Staff ✓, Cashier ❌ 403.

## Cấu hình nhà cung cấp Groq

### Thông số

| Mục | Giá trị |
| :--- | :--- |
| Base URL | `https://api.groq.com/openai/v1` (endpoint tương thích OpenAI, dùng `POST /chat/completions`) |
| Xác thực | Header `Authorization: Bearer <key>` |
| Model đề xuất | `openai/gpt-oss-120b` (có tool calling, tiếng Việt khá). Dự phòng: `llama-3.3-70b-versatile`. Kiểm tra danh sách thật bằng `GET /openai/v1/models` trước khi chốt. |
| Hạn mức free | Khoảng 30 request/phút và vài trăm đến vài nghìn request/ngày, tùy model. Xem số chính xác ở trang Limits trong console Groq. |
| Dữ liệu | Chỉ dùng dữ liệu demo. Không đưa dữ liệu khách hàng thật qua gói free. |

### Ánh xạ cấu hình

| Biến trong `.env` | Khóa .NET (`appsettings`) | Giá trị demo | Ghi chú |
| :--- | :--- | :--- | :--- |
| `AI_ENABLED` | `Ai__Enabled` | `true` | Mặc định `false` trong `.env.example` và appsettings |
| `AI_BASE_URL` | `Ai__BaseUrl` | `https://api.groq.com/openai/v1` | |
| `AI_MODEL` | `Ai__Model` | `openai/gpt-oss-120b` | |
| `AI_API_KEY` | `Ai__ApiKey` | key Groq | **Chỉ ghi trong `.env`** (đã nằm trong `.gitignore`) hoặc `dotnet user-secrets`. Không ghi vào `appsettings*.json`, `.env.example`, docs, test, log. |
| `AI_TIMEOUT_SECONDS` | `Ai__TimeoutSeconds` | `30` | |
| `AI_MAX_TOKENS` | `Ai__MaxTokens` | `1024` | |

`docker-compose.yml` (service backend) thêm:

```yaml
      Ai__Enabled: ${AI_ENABLED:-false}
      Ai__BaseUrl: ${AI_BASE_URL:-https://api.groq.com/openai/v1}
      Ai__Model: ${AI_MODEL:-openai/gpt-oss-120b}
      Ai__ApiKey: ${AI_API_KEY:-}
```

`.env.example` thêm cùng các biến với `AI_ENABLED=false` và `AI_API_KEY=` để trống.

Chạy backend ngoài Docker (`scripts/run-backend.ps1` hoặc `dotnet run`): đặt key bằng user-secrets của project `OISM.WebApi` (cần thêm `UserSecretsId` vào csproj) hoặc biến môi trường `Ai__ApiKey` trong terminal, không sửa appsettings.

### Quy tắc an toàn cho key

1. Key đã từng dán vào chat, tài liệu hay ticket coi như đã lộ: thu hồi ở console Groq (API Keys) và tạo key mới trước khi dùng.
2. Backend không log header `Authorization` và không trả nội dung lỗi gốc của Groq cho client.
3. Khởi động với `Ai:Enabled=true` mà thiếu `Ai:ApiKey` thì backend ghi cảnh báo và chuyển sang `DisabledLlmClient`, không làm sập ứng dụng.
4. Bản demo AWS: key đặt trong `.env.production` trên máy chủ, không đưa vào image Docker hay build frontend. Frontend không bao giờ biết key; mọi lời gọi Groq đi qua backend.

### Các bước cấu hình và kiểm tra

1. Tạo key mới trên console Groq, thu hồi key cũ đã lộ.
2. Kiểm tra key và model bằng lệnh gọi danh sách model (chạy trong terminal của bạn, không lưu lệnh vào file):
   `curl -s https://api.groq.com/openai/v1/models -H "Authorization: Bearer $AI_API_KEY"`
3. Ghi các biến `AI_*` vào `.env` local.
4. Sau khi code xong B1, chạy `docker compose up -d --build backend`, gọi `POST /api/ai/chat` với tài khoản Owner demo, hỏi "Cách tạo phiếu nhập hàng?".
5. Thử `AI_ENABLED=false` để chắc endpoint trả 503 `AI_DISABLED` và nút chat biến mất.
6. Ghi bằng chứng (câu hỏi, tool đã gọi, câu trả lời, thời gian phản hồi) vào báo cáo; che key trong mọi ảnh chụp.

## Kế hoạch triển khai backend

### B1. Khung LLM

- `OISM.Application/Common/Interfaces/ILlmClient.cs`: `ChatAsync(LlmRequest request, CancellationToken ct)`; request gồm system prompt, danh sách tin nhắn, danh sách tool; response gồm text hoặc các lời gọi tool, `finishReason`, số token.
- `OISM.Infrastructure/Services/Ai/OpenAiCompatibleLlmClient.cs`: `HttpClient` qua `AddHttpClient`, gọi `POST {BaseUrl}/chat/completions`. Dùng được cho Groq, Gemini (endpoint tương thích OpenAI), OpenRouter, Cerebras, Mistral, Ollama.
- `OISM.Infrastructure/Services/Ai/DisabledLlmClient.cs`: ném `OismException(AI_DISABLED, 503)`.
- `AiOptions` (section `Ai`): `Enabled`, `BaseUrl`, `Model`, `MaxTokens`, `TimeoutSeconds`, `MaxToolRounds` (3), `MaxHistoryMessages` (10). API key đọc từ biến môi trường `AI_API_KEY`, không ghi vào appsettings, không commit.
- Đăng ký trong `OISM.Infrastructure/DependencyInjection.cs` theo `Ai:Enabled`.
- Xử lý lỗi nhà cung cấp: 429 → `AI_RATE_LIMITED` (429); timeout, 5xx, phản hồi sai định dạng → `AI_UPSTREAM_ERROR` (502). Không trả nội dung lỗi gốc của nhà cung cấp cho client.

### B2. Hướng dẫn sử dụng

- Viết mới khoảng 10 trang markdown ngắn, đặt tại `OISM.Infrastructure/Ai/Guide/` dạng embedded resource: đăng nhập, sản phẩm và đơn vị tính, nhập hàng, chuyển kho, kiểm kê, đơn hàng, POS, VietQR, báo cáo, tài khoản.
- Mỗi trang có front-matter `roles: [Owner, Staff]`. Backend ghép các trang hợp vai trò vào system prompt.
- Nội dung bám theo giao diện thật hiện tại, không mô tả chức năng đang ẩn hoặc Coming soon (Quên mật khẩu, CS-06). *(Cập nhật 11/11/2026: tab Gợi ý nhập hàng đã mở lại ngày 10/11 và CS-01 trả hàng đã xong ở PR #41, nên hai chức năng này được mô tả.)*

### B3. Application: `Features/Ai/`

- `Services/IStockMovementQuery.cs` và hiện thực: tổng `IN`/`OUT` theo ngày và chi nhánh từ `inventory_transactions`, tách riêng dòng có `TransferItemId`, top 10 SKU theo số lượng xuất, đơn vị gốc. Chỉ đọc, `AsNoTracking`.
- Tool `get_reorder_suggestions`: gọi `IAiForecastingService.GetReorderRecommendationsAsync` với `OnlyNeedsReorder = true`, gửi cho model tối đa 20 SKU có `DaysUntilStockout` nhỏ nhất. Không sửa `AiForecastingService`.
- `Services/AiChatService.cs`:
  1. Kiểm tra đầu vào: tối đa 10 tin, mỗi tin tối đa 1.000 ký tự, `branchId` thuộc tenant.
  2. Dựng system prompt (quy tắc + hướng dẫn theo vai trò) và danh sách tool theo vai trò.
  3. Vòng gọi tool tối đa `MaxToolRounds`; tool lạ hoặc tool ngoài quyền trả lỗi cho model, không chạy.
  4. Kết quả tool chuyển thành DTO rút gọn, đã bỏ trường nhạy cảm theo vai trò, rồi mới gửi lại model.
  5. Trả `{ reply, usedTools[], dateRange }`.
- `Dtos/AiChatDtos.cs`: `AiChatRequest`, `AiChatMessageDto` (`role`: `user`/`assistant`), `AiChatResponse`.
- Cache kết quả tool bằng `IMemoryCache` 5 phút, khóa gồm tenant, vai trò, tool, tham số. **Chỉ cache kỳ đã kết thúc** (ngày cuối trước hôm nay). Kỳ có hôm nay, cảnh báo tồn và gợi ý nhập luôn đọc lại (sửa 10/11/2026: người dùng bán đơn xong hỏi lại vẫn nhận doanh thu 0 do cache).

### B4. WebApi

- `AiController`: thêm `POST /api/ai/chat`, `[Authorize(Roles = "Owner,Staff")]`.
- RateLimiter của ASP.NET Core, phân vùng theo user id trong JWT: 10 request/phút. Bị chặn thì trả `AI_RATE_LIMITED`.
- Controller chỉ điều phối HTTP, không dùng `AppDbContext`.
- `docker-compose.yml`: thêm `AI_ENABLED` (mặc định `false`), `AI_BASE_URL`, `AI_MODEL`, `AI_API_KEY`.

### B5. Kiểm thử

| Loại | Nội dung |
| :--- | :--- |
| Unit (`ILlmClient` giả) | Staff không nhận `GrossProfit`, giá vốn trong dữ liệu gửi model; model gọi tool ngoài quyền thì không chạy; quá `MaxToolRounds` thì dừng; tin nhắn quá dài trả 400; tắt AI trả 503. |
| Unit | `OpenAiCompatibleLlmClient` với `HttpMessageHandler` giả: map 429, 5xx, timeout, JSON sai sang đúng mã lỗi. |
| Integration PostgreSQL | `IStockMovementQuery`: ranh giới ngày UTC+07:00, tách dòng chuyển kho, không lẫn dữ liệu tenant khác. |
| Integration endpoint | Không JWT → 401; `branchId` của tenant khác → 404 hoặc 400; rate limit trả 429. |
| Thử tay | Gọi một lần với key free thật trên máy local, ghi lại câu hỏi, tool đã gọi và câu trả lời làm bằng chứng. Không chạy trong CI. |

### B6. Tài liệu và bàn giao

- `docs/02-functional-requirements/`: thêm yêu cầu mới (đề xuất `FR-AI-02` Chatbot hỗ trợ), cập nhật bảng trong `README.md`.
- `docs/01-users-and-rbac/02-rbac-matrix.md`: thêm dòng chatbot theo bảng quyền ở trên.
- `docs/05-api-contracts/04-order-endpoints.md`: thêm `POST /api/ai/chat`.
- `docs/12-error-and-status-codes/02-error-codes-catalog.md`: `AI_DISABLED`, `AI_RATE_LIMITED`, `AI_UPSTREAM_ERROR`.
- `docs/07-devops-and-deployment/`: cách bật AI và đặt biến môi trường.
- `docs/11-ui-screens/`: thêm mô tả khung chat (mục F5).

## Kế hoạch triển khai frontend

### Hiện trạng frontend (kiểm tra 09/11/2026)

| Nội dung | Hiện trạng |
| :--- | :--- |
| Admin | `AdminShell.tsx` có `header.topbar`, phần `topbar-right` chứa `NotificationCenter`; trang con render qua `<Outlet />`. Gọi API bằng `httpClient` (axios, timeout 15 giây, tự refresh token). Lỗi đọc theo `data.errorCode \|\| data.extensions.errorCode`. |
| Phiên Admin | `AuthContext` có `user.role`, `user.tenantId`, `logout()`. Cashier đăng nhập Admin bị chuyển sang POS độc lập; Cashier không dùng chatbot nên `pos-app` không cần sửa. |
| POS | `pos-app` dùng `fetch` trong `services/posApi.ts`, token qua `getPosAccessToken()`; phiên trong `PosAuthContext` (`user.role`, `logout`, sự kiện `pos-session-ended`, `pos-auth-expired`). `PosPage.tsx` khoảng 430 dòng. |
| Kiểu dữ liệu AI | `admin-dashboard/src/types/ai.ts` đã có kiểu cho gợi ý nhập hàng. |
| Test | `pos-app` có `npm test` (node --test, file ở `test-oisms/frontend/pos-app/`). `admin-dashboard` chưa có script test, chỉ `npm run build`. |

### Nguyên tắc frontend

1. **Không tự lưu lịch sử chat:** chỉ giữ trong React state, tối đa 10 tin gửi lên server; không ghi `localStorage`/`sessionStorage` vì có số doanh thu. Xóa khi đăng xuất, đổi tenant, hết phiên.
2. **Không tự quyết quyền:** giao diện ẩn câu hỏi mẫu theo vai trò cho gọn, nhưng quyền thật do backend lọc. Không gửi role hay tenant trong body.
3. **Hiển thị văn bản thuần:** câu trả lời của AI render dạng text (giữ xuống dòng, danh sách đơn giản), **không dùng `dangerouslySetInnerHTML`**, không tự mở link do AI trả về.
4. **Không chặn công việc chính:** chat nằm trong panel trượt; lỗi AI chỉ hiện trong panel, không đẩy lên `networkError` chung của Admin .

### F1. Lớp dịch vụ và kiểu dữ liệu dùng chung

- `admin-dashboard/src/types/aiChat.ts` và `pos-app/src/types/aiChat.ts`: `AiChatMessage { role: 'user' | 'assistant'; content: string }`, `AiChatRequest { messages; branchId? }`, `AiChatResponse { reply; usedTools: string[]; dateRange?: { from; to } }`.
- `admin-dashboard/src/services/aiChatService.ts`: `sendChat(request, signal)` dùng `httpClient` với timeout riêng 45 giây (gọi LLM chậm hơn API thường).
- `pos-app/src/services/aiChatApi.ts`: cùng hàm, dùng `fetch` + `getPosAccessToken()` + `AbortController` như `posApi.ts`.
- Hàm thuần `mapAiChatError(status, errorCode)` (một bản cho mỗi app) trả câu thông báo:

| Mã | Hiển thị |
| :--- | :--- |
| `AI_DISABLED` (503) | Ẩn nút chat; nếu đang mở panel thì hiện "Trợ lý AI chưa được bật". |
| `AI_RATE_LIMITED` (429) | "Bạn hỏi hơi nhanh, thử lại sau ít phút." Khóa nút gửi 30 giây. |
| `AI_UPSTREAM_ERROR` (502), timeout, mất mạng | "Trợ lý AI tạm thời không trả lời được." Có nút Thử lại gửi lại đúng tin cuối. |
| 400 | Hiện thông báo từ server (tin quá dài, chi nhánh không hợp lệ). |
| 401 | Để luồng refresh/đăng xuất hiện có xử lý, panel tự đóng. |

- Hàm thuần `trimHistory(messages, 10)` và `buildRequest(messages, branchId)` để test được không cần DOM.

### F2. Trạng thái chat: hook `useAiChat`

- Mỗi app một hook `useAiChat()` trả `{ messages, status, error, send(text), retry(), reset() }`; `status`: `idle | sending | error | disabled`.
- Chỉ một request tại một thời điểm; gửi tin mới khi đang chờ thì bỏ qua. Đóng panel hoặc unmount thì `abort()` request đang chờ.
- Gọi `reset()` trong `logout()` và khi `user.tenantId` đổi.
- Kiểm tra trạng thái bật/tắt: lần mở panel đầu tiên mới biết (nhận `AI_DISABLED` thì đặt `disabled` và ẩn nút đến hết phiên). Không thêm endpoint kiểm tra riêng.

### F3. Admin: `components/AiAssistant/`

- `AiAssistantButton.tsx`: nút icon `Bot` (lucide-react) đặt trong `topbar-right` cạnh `NotificationCenter` của `AdminShell.tsx`; chỉ hiện với Owner và Staff.
- `AiAssistantPanel.tsx`: panel trượt bên phải, rộng 380px; trên mobile chiếm toàn màn hình. Gồm:
  - tiêu đề và nút đóng, nút "Cuộc trò chuyện mới";
  - ô chọn chi nhánh (lấy danh sách chi nhánh có sẵn), mặc định "Tất cả chi nhánh";
  - danh sách tin nhắn; mỗi câu trả lời có dòng nhỏ "Nguồn: Tồn kho · Doanh thu, 01/11–07/11" từ `usedTools` và `dateRange`;
  - nút "Tóm tắt hôm nay" gửi sẵn câu hỏi tổng hợp: doanh thu, nhập/xuất, hàng sắp hết và gợi ý nhập trong ngày;
  - câu hỏi mẫu khi chưa có tin: "Hôm nay nhập xuất bao nhiêu?", "Doanh thu 7 ngày qua?", "Nên nhập thêm gì tuần này?", "Cách tạo phiếu nhập hàng?";
  - ô nhập tối đa 1.000 ký tự có bộ đếm, Enter gửi, Shift+Enter xuống dòng;
  - trạng thái đang trả lời (ba chấm), lỗi và nút Thử lại.
- CSS theo token màu ở `docs/12-error-and-status-codes/03-color-system-tokens.md`, thêm vào `index.css` dưới một khối `.ai-assistant`.
- Không đụng `ReportsPage`, `InventoryPage` hay `DashboardPage`.

### F3b. Mở lại tab Gợi ý nhập hàng (`FR-AI-01`)

- `InventoryPage.tsx`: bỏ comment nút tab "Gợi ý nhập hàng" (khối có chú thích "Tạm ẩn tab Gợi ý nhập hàng"); panel và phần gọi `getReorderRecommendations` đã có sẵn.
- Kiểm tra bảng hiển thị đủ cột theo `fr-12-ai-forecasting.md`: SKU, tồn hiện tại, tốc độ bán trung bình/ngày, số ngày dự kiến hết hàng, số lượng đề xuất nhập. Thiếu cột nào thì bổ sung.
- Tab chỉ hiện với Owner và Staff (điều kiện `role !== "Cashier"` có sẵn).
- Cập nhật `docs/report-after-upgrade/frontend-coming-soon-va-upgrade.md` và `docs/02-functional-requirements/README.md`: tab đã mở lại.

### F4. POS

Không làm. Cashier không dùng chatbot (chốt 10/11/2026).

### F5. Kiểm thử frontend

| Loại | Nội dung |
| :--- | :--- |
| Admin | Chưa có script test. Đặt logic thuần trong `aiChatService.ts` để test được; đề xuất thêm `npm test` bằng node --test theo mẫu `pos-app` (cần thống nhất vì CI hiện chưa chạy test Admin). Tối thiểu phải qua `npm run build`. |
| Lint, build | `npm run build --prefix src/frontend/admin-dashboard`; `npm run build`, `npm run lint`, `npm test` cho `pos-app`. |
| Bộ câu hỏi đánh giá | 20 câu hỏi tiếng Việt trong `test-oisms/ai-eval/chatbot-questions.json`, mỗi câu ghi tool đúng và khoảng ngày đúng. Test chỉ chạy khi đặt `AI_LIVE_EVAL=1` và có key; ghi tỷ lệ chọn đúng tool và ngày cho từng model. Dùng để chọn model và làm bằng chứng khi bảo vệ. |
| Thử tay trên trình duyệt | Owner hỏi doanh thu thấy lãi gộp; Staff hỏi doanh thu không thấy lãi; Cashier gọi API nhận 403 và POS không có nút chat; tắt AI thì nút chat biến mất; đăng xuất rồi đăng nhập tài khoản khác thì lịch sử trống; câu trả lời chứa thẻ `<script>` hiển thị như chữ. Chụp màn hình làm bằng chứng. |

## Đợt 2: tối ưu hiển thị và chọn chi nhánh (lập 10/11/2026)

Phát hiện khi thử trên web sau nghiệm thu đợt 1.

### Vấn đề và nguyên nhân

| Vấn đề | Nguyên nhân trong code |
| :--- | :--- |
| Câu trả lời lộ ký tự thừa `**…**` | Model trả Markdown, panel render `<p>{content}</p>` dạng chữ thuần (`AiAssistantPanel.tsx`). |
| Chatbot hỏi "mã (ID) chi nhánh" kể cả khi đang chọn Tất cả chi nhánh | System prompt có câu "Không đoán mã chi nhánh… yêu cầu người dùng chọn chi nhánh" (`AiChatService.SystemRules`); 4 tool khai báo tham số `branchId` "Mã chi nhánh nếu đã biết"; model không được báo phạm vi đang chọn trong panel. |
| Người dùng gõ tên chi nhánh ("Quận 1") vẫn bị hỏi mã | Không có cách đổi tên → id; model chỉ có thể truyền GUID. |

### Quyết định

1. **Người dùng không bao giờ nhập mã chi nhánh.** Model không thấy và không truyền GUID.
2. Phạm vi mặc định là lựa chọn trong panel (mặc định Tất cả chi nhánh). Model gọi tool ngay với phạm vi đó, không hỏi lại.
3. Người dùng nhắc tên chi nhánh trong câu hỏi → model truyền `branchName`, **backend** đổi tên thành chi nhánh (trong tenant, đang hoạt động, so khớp không phân biệt hoa thường và dấu). Khớp đúng 1 → dùng. Không khớp hoặc khớp nhiều → trả danh sách lựa chọn, giao diện hiện **nút chọn chi nhánh**; bấm nút thì hỏi lại câu cũ với chi nhánh đó.
4. Câu trả lời được phép dùng Markdown tối giản (đậm, gạch đầu dòng, danh sách số); giao diện render an toàn thành phần tử React, không dùng HTML thô. Bảng, tiêu đề, link hiển thị dạng chữ thường.
5. System prompt quy định văn phong: ngắn gọn, số chính in đậm, tiền `1.250.000 ₫`, ngày `dd/MM/yyyy`, không hỏi lại khi đủ dữ liệu.

### B7. Backend đợt 2

- Bỏ `branchId` khỏi JSON schema của cả 4 tool; thêm `branchName` (string, tùy chọn: "Tên chi nhánh người dùng nhắc tới, để trống nếu không nhắc").
- `AiChatToolRunner`: thứ tự ưu tiên chi nhánh: `branchName` trong tool call (nếu có) → chi nhánh chọn trong panel → tất cả. Bỏ lỗi 400 khi khác chi nhánh panel. Hàm đổi tên: lấy chi nhánh `IsActive` của tenant, chuẩn hóa (bỏ dấu, chữ thường, gộp khoảng trắng); ưu tiên khớp tuyệt đối, sau đó khớp chứa duy nhất. 0 hoặc nhiều kết quả → tool result `{"needBranchChoice":true,"choices":[tên…]}`, không truy vấn số liệu.
- `AiChatResponse` thêm `scope` (tên phạm vi đã dùng, ví dụ "Tất cả chi nhánh" hoặc "Quận 1") và `branchChoices` (`[{id,name}]`, tối đa 10, rỗng nếu không cần chọn).
- System prompt: thêm dòng "Phạm vi đang chọn: …"; bỏ câu yêu cầu người dùng chọn mã; thêm quy tắc định dạng ở Quyết định 5; khi tool trả `needBranchChoice` thì chỉ nói một câu mời chọn chi nhánh bên dưới.

### F6. Frontend đợt 2

- `formatChatMessage` (hàm thuần trong `components/AiAssistant/chatFormat.ts`) tách nội dung thành khối: đoạn, danh sách gạch đầu dòng, danh sách số; trong dòng nhận `**đậm**` và `` `mã` ``. Dấu `**` lẻ, `#` đầu dòng, dòng bảng `|…|` được làm sạch thành chữ thường. Component render khối thành `<p>`, `<ul>`, `<ol>`, `<strong>`, `<code>`; không `dangerouslySetInnerHTML`, không tạo link.
- Dưới câu trả lời có `branchChoices`: hiện các nút tên chi nhánh. Bấm nút → đặt ô chọn chi nhánh của panel thành chi nhánh đó → gửi lại câu hỏi người dùng gần nhất với `branchId` mới.
- Dòng nguồn thêm phạm vi: "Nguồn: Doanh thu · Quận 1 · 04/11–10/11".
- Ô chọn chi nhánh chỉ liệt kê chi nhánh đang hoạt động.

### Kiểm thử đợt 2

| Loại | Nội dung |
| :--- | :--- |
| Unit backend | Schema 4 tool không có `branchId`, có `branchName`; prompt có dòng phạm vi và không còn câu yêu cầu mã; đổi tên: khớp tuyệt đối, khớp không dấu ("quan 1" → "Quận 1"), khớp chứa duy nhất, khớp nhiều → `branchChoices`, không khớp → `branchChoices`, chi nhánh ngừng hoạt động và chi nhánh tenant khác không bao giờ khớp; khác chi nhánh panel không còn 400; `scope` đúng. |
| Integration | `AiChat` cũ vẫn PASS; thêm ca response có `scope` và `branchChoices`. |
| Frontend | `test-oisms/frontend/admin-dashboard/test-ai-chat-format.mjs` (node --test, transpile TS theo mẫu `test-oisms/frontend/pos-app/test-cart-units.mjs`): đậm, danh sách, `**` lẻ, tiêu đề, bảng, chuỗi `<script>` giữ là chữ. `npm run build` Admin. |
| Eval | Thêm 4 câu có tên chi nhánh (có dấu, không dấu, sai tên, trùng tên) vào `chatbot-questions.json`. |
| Thử tay | Hai câu trong ảnh lỗi: "Tóm tắt hôm nay…" với Tất cả chi nhánh trả số ngay; "SKU sắp hết ở Quận 1" trả số của Quận 1 hoặc hiện nút chọn. Không còn `**` trên màn hình. |

## Đợt 3: người dùng chọn model AI (10/11/2026)

- Người dùng yêu cầu thêm Google Gemini để có 2 lựa chọn model. Claude làm trực tiếp, không qua GPT.
- Backend: `LlmRouter` (Infrastructure) vừa là `ILlmClient` vừa là `ILlmModelCatalog`; nhà cung cấp chính là Groq (khóa `Ai:*` cũ), nhà cung cấp thêm khai báo ở `Ai:Providers:<mã>`. Chỉ nhà cung cấp có key mới xuất hiện. `GET /api/ai/models` trả danh sách; `POST /api/ai/chat` nhận `model`.
- Gemini 3 qua endpoint tương thích OpenAI trả `extra_content.google.thought_signature` kèm mỗi lời gọi tool và **bắt buộc** gửi lại ở lượt sau (thiếu thì 400, đã thử thật). `LlmToolCall.ExtraContent` giữ và gửi lại nguyên văn.
- Thời gian chờ riêng theo nhà cung cấp (Gemini 60 giây); frontend chờ tối đa 90 giây.
- Frontend: ô "Model AI" cạnh ô chi nhánh; lựa chọn lưu ở localStorage của trình duyệt (không lưu lịch sử chat); dòng nguồn ghi model đã dùng; lỗi quá tải gợi ý chọn model khác.
- Đo thật với Gemini `gemini-3.5-flash`: 16/16 câu eval đúng tool và đúng ngày; mỗi lượt khoảng 15–20 giây; key đang ở Free Tier 20 request/ngày.

## Đợt 4: kho tài liệu RAG cho trợ lý (lập 11/11/2026)

Người dùng yêu cầu một thư mục tài liệu để trợ lý đọc và truy xuất thông tin hệ thống, trả lời đúng theo tài liệu, không bịa.

### Vấn đề

- Hướng dẫn hiện có (`OISM.Infrastructure/Ai/Guide/`, 10 trang, khoảng 3 KB) chỉ nói thao tác cơ bản. Hỏi về quy tắc nghiệp vụ (tồn khả dụng, giữ chỗ, giá vốn, đơn vị tính, trạng thái đơn, trả hàng, quyền, mã lỗi) thì model không có nguồn và có thể tự suy diễn.
- Toàn bộ hướng dẫn được nhét vào system prompt mỗi lượt. Tăng tài liệu theo cách này làm phình token, trong khi Groq miễn phí chỉ có 8.000 token/phút.

### Quyết định

| Chủ đề | Quyết định | Lý do |
| :--- | :--- | :--- |
| Vị trí | `src/backend/src/OISM.Infrastructure/Ai/Knowledge/`, embedded resource, thay cho `Ai/Guide/` | `src/backend/Dockerfile` chỉ copy `src/backend/`; đặt trong `docs/` thì image không có tài liệu |
| Định dạng | Mỗi file một mục 300–1.200 ký tự, front-matter `id`, `title`, `roles`, `keywords`, `source` | Mục nhỏ thì truy xuất đúng, ít token |
| Nguồn nội dung | Chỉ viết lại từ `docs/` (FR, màn hình, RBAC, mã lỗi, khái niệm nghiệp vụ); `source` trỏ tới file và mục docs có thật | Docs là yêu cầu; truy được nguồn khi nội dung sai |
| Truy xuất | BM25 cục bộ trên văn bản đã bỏ dấu, chữ thường; cộng điểm khi khớp `keywords`/`title`; không gọi API embedding | Gói miễn phí không đủ hạn mức; không thêm request mạng, không thêm dependency |
| Thời điểm | Truy xuất trước khi gọi model theo tin nhắn người dùng cuối, ghép tối đa 4 mục (≤ 3.000 ký tự) vào system prompt | Không tốn thêm lượt gọi model |
| Chống bịa | Dưới ngưỡng điểm thì không ghép mục nào và ghép dấu hiệu "không có tài liệu phù hợp"; prompt bắt buộc câu hỏi hướng dẫn hoặc nghiệp vụ chỉ trả lời từ mục được cấp, trích mã mục, không có thì trả đúng câu "Tài liệu OISMS chưa có thông tin này." | Mô hình không có chỗ để tự suy diễn |
| Quyền | Lọc theo `roles` trước khi xếp hạng; Staff không bao giờ nhận mục chỉ dành cho Owner (giá vốn, lãi gộp, kết nối sàn, tài khoản nhận tiền, nhân viên...) theo `docs/01-users-and-rbac/02-rbac-matrix.md` | Giữ quyết định Staff không xem doanh thu, lãi gộp |
| Không đưa vào | Secret, key, mật khẩu, dữ liệu tenant, chi tiết kỹ thuật nội bộ (bảng, trigger, class), chức năng Coming soon hoặc đang ẩn | Tài liệu dùng chung mọi tenant; không mở bề mặt tấn công |
| Hiển thị nguồn | `AiChatResponse` thêm `Sources` (mã, tiêu đề mục đã ghép); panel Admin ghi "Tài liệu: …" ở dòng nguồn | Người dùng kiểm được câu trả lời lấy từ đâu |

### K1. Nội dung kho tài liệu

- Khoảng 40–70 mục, nhóm: thao tác theo màn hình (`docs/11-ui-screens/`), khái niệm nghiệp vụ (`docs/00-overview/02-core-domain-concepts.md`, `docs/04-architecture-and-design/04-state-machines.md`), quyền theo vai trò (`docs/01-users-and-rbac/02-rbac-matrix.md`), mã lỗi thường gặp và cách xử lý (`docs/12-error-and-status-codes/02-error-codes-catalog.md`), câu hỏi thường gặp.
- Gộp 10 trang `Ai/Guide/` cũ vào kho mới rồi xóa thư mục cũ.
- Docs mâu thuẫn với nhau hoặc với giao diện hiện tại thì không tự chọn, liệt kê trong bàn giao để người dùng quyết định.

### K2. Truy xuất và ghép prompt

- `IKnowledgeBase` (Application) với `Search(string query, string role, int top)`; hiện thực `KnowledgeBase` (Infrastructure) nạp và kiểm tra front-matter khi khởi động, lỗi định dạng thì ném ngoại lệ.
- `AiChatService` gọi truy xuất theo tin nhắn người dùng cuối, ghép mục vào phần "Tài liệu tham chiếu" của system prompt thay cho "Hướng dẫn sử dụng OISMS"; câu hỏi số liệu vẫn đi qua tool như cũ.
- `ai-eval` dùng `KnowledgeBase` thay `UserGuideProvider`.

### K3. Kiểm thử

| Loại | Nội dung |
| :--- | :--- |
| Unit, kiểm tra kho | Mọi file có front-matter hợp lệ; `id` không trùng; `roles` chỉ gồm Owner, Staff; `source` trỏ tới file có thật trong `docs/`; độ dài trong giới hạn; không chứa chuỗi giống key (`gsk_`, `AIza`, `sk-`, `whsec_`) |
| Unit, truy xuất | Câu không dấu tìm đúng mục có dấu; Staff không bao giờ nhận mục Owner; câu ngoài phạm vi (thời tiết, chính trị) không trả mục nào; giới hạn 4 mục và 3.000 ký tự |
| Unit, prompt | Prompt chứa mã mục được ghép và quy tắc chống bịa; không có mục thì có dấu hiệu "không có tài liệu phù hợp"; `Sources` đúng |
| Eval truy xuất (không gọi model) | File câu hỏi mẫu khoảng 30 câu có mã mục mong đợi và 5 câu ngoài phạm vi; mục đúng nằm trong top 3 ở ít nhất 90% câu; 5 câu ngoài phạm vi không trả mục nào |
| Eval sống (tùy chọn) | `AI_LIVE_EVAL=1`, đếm câu trả lời có trích mã mục và câu từ chối đúng; tốn hạn mức nên chỉ chạy khi người dùng yêu cầu |

## Thứ tự và ước lượng

| Bước | Việc | Ước lượng |
| :--- | :--- | :--- |
| 1 | B1 khung LLM, cấu hình, mã lỗi | 0,5 ngày |
| 2 | B2 hướng dẫn sử dụng, chat trả lời hướng dẫn | 1 ngày |
| 3 | B4 endpoint, rate limit, compose (để frontend có API thật sớm) | 0,5 ngày |
| 4 | F1, F2, F3 khung chat Admin chạy với chức năng hướng dẫn | 1 ngày |
| 5 | B3 `get_low_stock`, `get_stock_movement` | 1 ngày |
| 6 | B3 `get_revenue` có lọc vai trò, `get_reorder_suggestions`, bộ câu hỏi đánh giá | 1 ngày |
| 7 | F3b mở lại tab Gợi ý nhập hàng, nút "Tóm tắt hôm nay" | 0,5 ngày |
| 8 | B5, F5 test và thử tay; B6 docs | 1,5 ngày |

## Rủi ro

| Rủi ro | Cách xử lý |
| :--- | :--- |
| Gói free bị giảm hạn mức hoặc đổi điều khoản | Đổi nhà cung cấp bằng cấu hình; endpoint trả lỗi rõ ràng, các chức năng khác không phụ thuộc AI. |
| Gói free dùng dữ liệu để huấn luyện | Chỉ dùng cho demo với dữ liệu giả. Môi trường thật phải dùng gói trả phí có điều khoản không huấn luyện. |
| Mất mạng hoặc Groq lỗi lúc bảo vệ | Quay sẵn video demo và chụp màn hình các câu hỏi chính; tab Gợi ý nhập hàng chạy không cần AI. |
| AI nói sai số | Số chỉ đến từ tool; response trả `usedTools` để giao diện hiển thị nguồn; prompt cấm tự tính. |
| Lộ lãi gộp cho Staff | Lọc ở DTO trước khi gửi model, có unit test riêng. |
| Prompt injection qua tên sản phẩm | Không có tool ghi; dữ liệu đánh dấu là dữ liệu; tool ngoài quyền không chạy. |

## Câu hỏi còn mở

1. Chốt model Groq sau khi kiểm tra danh sách model thật (`openai/gpt-oss-120b` hay `llama-3.3-70b-versatile`).
2. ~~Cashier có dùng chatbot không~~: không, chốt 10/11/2026.
3. Có bật AI trên bản demo AWS không.
4. ~~`GET /api/reports/revenue-profit` trả lãi gộp cho Staff~~: đã sửa trong commit trả hàng `1768bd7` (controller truyền `isOwner`). Tool `get_revenue` vẫn tự lọc.
5. Có thêm script `npm test` cho `admin-dashboard` và đưa vào CI không.
