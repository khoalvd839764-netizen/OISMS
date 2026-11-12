# Gói giao GPT đợt 2 — OISMS-58-AI: tối ưu hiển thị và chọn chi nhánh

Làm tuần tự Bước 9 → Bước 12, qua gate mới sang bước sau. Hết Bước 12 trả **một báo cáo cuối** theo mẫu ở cuối file `OISMS-58-AI-goi-giao.md`. Claude review, tự sửa và nghiệm thu.

## Luật chung

- **Vai của bạn:** bạn đóng vai **Antigravity (Anti)** trong quy trình `skills/claude-to-antigravity/`. "Claude" là người review và nghiệm thu cuối.
- Áp dụng nguyên mục "Luật chung" của `skills/claude-to-antigravity/references/OISMS-58-AI-goi-giao.md` (không commit, không migration, không key thật, không gọi mạng trong test, một lỗi tối đa 2 lần thử, gate G-BUILD / G-UNIT / G-KEY).
- **Nguồn yêu cầu:** `docs/Upgrade-recommended/09-chatbot-ai-co-ban.md`, mục **"Đợt 2: tối ưu hiển thị và chọn chi nhánh"** (Vấn đề, Quyết định, B7, F6, Kiểm thử đợt 2).
- **Baseline:** branch `feature/OISMS-58-chatbot-ai`, working tree hiện tại (đợt 1 đã được Claude nghiệm thu, chưa commit). Giữ nguyên mọi thay đổi có sẵn.
- **Nhật ký:** nối tiếp vào `tmp/claude-anti/OISMS-58-AI-gpt-log.md`, tiêu đề "Đợt 2 — Bước …".
- **Không sửa:** `ReportService`, `AiForecastingService`, schema, `pos-app`, endpoint khác ngoài `POST /api/ai/chat`.

---

## Bước 9 — Backend: chi nhánh theo tên, không dùng mã

**Được sửa:** `OISM.Application/Features/Ai/` (`Tools/*.cs`, `AiChatService.cs`, `Dtos/AiChatDtos.cs`), test unit/integration AI.

**AC:**
- 9.1 Schema 4 tool (`get_stock_movement`, `get_revenue`, `get_low_stock`, `get_reorder_suggestions`) **không còn** `branchId`; có `branchName` (string, tùy chọn, mô tả: "Tên chi nhánh người dùng nhắc tới trong câu hỏi. Để trống nếu không nhắc.").
- 9.2 `AiChatToolRunner` chọn chi nhánh theo thứ tự: `branchName` trong tool call → `request.BranchId` của panel → tất cả. Bỏ lỗi 400 "Chi nhánh của công cụ phải khớp…". `request.BranchId` vẫn phải thuộc tenant (giữ `ValidateBranchAsync`).
- 9.3 Đổi tên → chi nhánh: chỉ chi nhánh `IsActive` của tenant hiện tại; chuẩn hóa hai phía (bỏ dấu tiếng Việt kể cả đ/Đ, chữ thường, gộp khoảng trắng). Ưu tiên khớp tuyệt đối; nếu không có thì khớp chứa (tên chứa chuỗi tìm) **duy nhất**. 0 hoặc ≥ 2 kết quả → không chạy tool số liệu, tool result là `{"needBranchChoice":true,"choices":["Tên 1","Tên 2"]}` (tối đa 10, khớp nhiều thì liệt kê các chi nhánh khớp, không khớp thì liệt kê mọi chi nhánh đang hoạt động).
- 9.4 `AiChatResponse` thêm `Scope` (string: "Tất cả chi nhánh" hoặc tên chi nhánh đã dùng ở tool cuối có dữ liệu; null nếu không gọi tool) và `BranchChoices` (`IReadOnlyList<AiChatBranchChoice>` với `Id`, `Name`; rỗng nếu không cần chọn). Giữ nguyên `Reply`, `UsedTools`, `DateRange`.
- 9.5 Cache key tool dùng branchId đã đổi (không dùng chuỗi tên thô), vẫn phân vùng tenant + role.

**Test unit:** schema không có `branchId`; khớp tuyệt đối; "quan 1" → "Quận 1"; "Q.1" không khớp thì trả choices; chuỗi khớp 2 chi nhánh → choices gồm đúng 2; chi nhánh `IsActive=false` và chi nhánh tenant khác không bao giờ khớp hay xuất hiện trong choices; panel chọn chi nhánh A, tool gọi `branchName` = B → dùng B, không 400; `Scope` đúng.

**Gate:** G-BUILD, G-UNIT, integration `--filter "FullyQualifiedName~AiChat"` PASS, G-KEY.

---

## Bước 10 — Backend: system prompt và văn phong

**Được sửa:** `AiChatService.cs`, test unit.

**AC:**
- 10.1 Bỏ câu "Không đoán mã chi nhánh. Nếu cần một chi nhánh cụ thể nhưng chưa biết mã, yêu cầu người dùng chọn chi nhánh trong khung chat."
- 10.2 Thêm dòng phạm vi: `Phạm vi đang chọn trong khung chat: {Tất cả chi nhánh | tên chi nhánh}.` và quy tắc: "Khi được hỏi số liệu, gọi công cụ ngay với phạm vi đang chọn. Không bao giờ hỏi mã hay ID chi nhánh. Nếu người dùng nhắc tên chi nhánh, truyền tên đó vào branchName. Nếu công cụ trả needBranchChoice, chỉ trả lời một câu mời người dùng chọn chi nhánh ở các nút bên dưới."
- 10.3 Quy tắc định dạng: tối đa khoảng 8 dòng trừ khi người dùng xin chi tiết; dùng gạch đầu dòng "- "; chỉ in đậm `**…**` cho con số chính; không dùng bảng, tiêu đề `#`, emoji, link; tiền dạng `1.250.000 ₫`; số lượng kèm đơn vị; ngày `dd/MM/yyyy`; không nhắc lại câu hỏi; không hỏi lại khi đã đủ dữ liệu.
- 10.4 "Tóm tắt hôm nay" (câu chứa "tóm tắt hôm nay") được hướng dẫn gọi đủ 4 tool trong một lượt nếu model hỗ trợ gọi nhiều tool cùng lúc.

**Test unit:** bắt `LlmRequest.SystemPrompt`: không còn câu cũ ở 10.1; có dòng phạm vi đúng với panel (Tất cả / tên chi nhánh); có quy tắc 10.2 và 10.3.

**Gate:** G-BUILD, G-UNIT, G-KEY.

---

## Bước 11 — Frontend: hiển thị Markdown an toàn và nút chọn chi nhánh

**Mẫu:** `components/AiAssistant/AiAssistantPanel.tsx`, `useAiChat.ts`, `services/aiChatService.ts`, `types/aiChat.ts`.

**Được sửa:** các file trên, `components/AiAssistant/chatFormat.ts` (mới), `components/AiAssistant/ChatMessageBody.tsx` (mới), `index.css` (khối `.ai-assistant`).

**AC:**
- 11.1 `chatFormat.ts` (hàm thuần, không import React): `parseChatMessage(text)` trả mảng khối `{type:"paragraph"|"bullets"|"numbers", lines|items}`; mỗi dòng là mảng đoạn `{text, bold?, code?}`. Nhận `**đậm**`, `` `mã` ``, gạch đầu dòng `- ` / `* ` / `• `, danh sách `1. `. Làm sạch: `**` lẻ bị bỏ; `#`… đầu dòng → đoạn in đậm; dòng bảng `| a | b |` → "a · b", dòng ngăn `|---|` bị bỏ; nhiều dòng trống gộp thành một.
- 11.2 `ChatMessageBody` render khối thành `<p>`, `<ul>`, `<ol>`, `<strong>`, `<code>`. Không `dangerouslySetInnerHTML`, không tạo `<a>`. Tin của người dùng vẫn hiển thị chữ thuần.
- 11.3 Types và service đọc `scope`, `branchChoices`. Dòng nguồn: "Nguồn: {tool} · {scope} · dd/MM–dd/MM" (bỏ phần thiếu).
- 11.4 Dưới tin trợ lý có `branchChoices`: nút cho từng chi nhánh. Bấm → đặt ô chọn chi nhánh của panel = id đó → gửi lại **câu hỏi người dùng gần nhất** với `branchId` mới (không thêm tin người dùng trùng lặp vào lịch sử hiển thị; có thể hiện dòng nhỏ "Đã chọn chi nhánh {tên}"). Nút bị khóa khi đang gửi.
- 11.5 Ô chọn chi nhánh chỉ liệt kê chi nhánh đang hoạt động (lọc theo trường trạng thái mà `/api/branches` trả về).

**Gate:** `npm run build --prefix src/frontend/admin-dashboard` PASS.

---

## Bước 12 — Test, eval, docs, báo cáo

**Được sửa:** `test-oisms/frontend/admin-dashboard/test-ai-chat-format.mjs` (mới), `test-oisms/ai-eval/chatbot-questions.json`, `docs/02-functional-requirements/fr-13-ai-assistant.md`, `docs/05-api-contracts/04-order-endpoints.md` (thêm `scope`, `branchChoices`).

**AC:**
- 12.1 Test frontend (node --test, transpile `chatFormat.ts` bằng `typescript` của admin-dashboard theo mẫu `test-oisms/frontend/pos-app/test-cart-units.mjs` + `test-support/app-path.mjs`): đậm; danh sách gạch và số; `**` lẻ; tiêu đề `#`; bảng; chuỗi `<script>alert(1)</script>` giữ nguyên là chữ trong kết quả parse; văn bản hai ảnh lỗi người dùng gửi ("Để có thể tổng hợp **doanh thu hôm nay**, …") không còn `**`.
- 12.2 Eval thêm 4 câu có tên chi nhánh: có dấu, không dấu, tên sai, chuỗi khớp nhiều chi nhánh; mỗi câu ghi `branchName` mong đợi hoặc mong đợi `needBranchChoice`.
- 12.3 Docs cập nhật contract response và mô tả chọn chi nhánh.

**Gate cuối:**
- G-BUILD, G-UNIT, G-KEY
- `dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~AiChat|FullyQualifiedName~PostgresAiStockMovement"` (PostgreSQL thật nếu có; không có thì NOT RUN phần Postgres)
- `node --test test-oisms/frontend/admin-dashboard/test-ai-chat-format.mjs` PASS
- `npm run build --prefix src/frontend/admin-dashboard` PASS
- `git diff --stat` + `git status --short`

**Thử tay (nếu backend local có key trong `.env` của người dùng):** "Tóm tắt hôm nay…" với Tất cả chi nhánh → trả số ngay, không hỏi chi nhánh; "SKU nào sắp hết ở chi nhánh Quận 1" → số của Quận 1 hoặc nút chọn; bấm nút chọn → trả số; trên màn hình không còn `**`. Không chạy được thì NOT RUN và lý do.

Báo cáo cuối theo "Mẫu báo cáo cuối" trong `OISMS-58-AI-goi-giao.md`, bảng tiến độ ghi Bước 9–12.
