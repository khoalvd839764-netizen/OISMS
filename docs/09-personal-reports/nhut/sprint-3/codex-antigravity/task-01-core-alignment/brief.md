# S3-BE2-T01 — Hợp nhất baseline và một lõi ghi tồn

**Trạng thái:** CHANGES_REQUESTED — Codex R1. **Ưu tiên:** P0. **Finding gốc:** BE2-F01, BE2-F02, BE2-F06. Vòng fix hiện tại theo [brief v1.1](./briefs/v1.1-fix-r1.md): BE2-T01-F01 và BE2-F06.

## Mục tiêu

Đưa lõi BE1 đã nghiệm thu vào `feature/OISMS-42-sprint-3-backend-2`, giữ query/report của Nhựt để hoàn thiện sau, loại đường xử lý đơn và webhook cạnh tranh. Không merge vào develop trong task này.

## Input / phạm vi

- BE2 đầu vào: `d7fa4bc8738a1f22053d9b313f8e90c423b0483d`.
- BE1 review ban đầu: `4206cc4b12f0128ee08927dc83550c9e8db5cd6e`, còn lỗi B-01. Remote-tracking ref sau đó có fix `b5ba71ef7ac2bf055f4ee8963e159d4f27c69aae`; đây chưa phải xác nhận CI pass/reviewer VERIFIED. Khi bắt đầu, fetch và kiểm tra fix/CI, pin baseline có B-01 đã kiểm chứng theo PLAN.md và PROMPT-FIX-BE2.md.
- Giải quyết 5 conflict: Orders DTO/interface/service/controller và WebApi Program.
- Rà soát thêm Simulator DTO/interface/service/controller, DI và consumer; chỉ một `POST /api/webhooks/simulator/{channel}` dùng `IWebhookService` BE1. Không giữ ingest gọi trực tiếp engine BE2 hoặc bypass cấu hình shop/tenant.
- Dùng `IOrderService` / DTO chuẩn BE1. Có thể tách query service để bảo toàn mutation; không đổi contract POS, complete hay expiry để vừa với DTO cũ của BE2.
- Đưa phân công Sprint 3 đã lưu ở BE1 (Confluence v11) vào baseline. Không mở rộng sang sửa reports, auth hay triển khai read APIs của T02 trong cùng task.

## Acceptance criteria

| AC | Điều kiện đạt / bằng chứng |
| --- | --- |
| AC-01 | Làm trên OISMS-42; ghi SHA trước/sau và nguồn baseline. Không reset/clean hay đưa tài liệu ngoài phạm vi vào diff. |
| AC-02 | Không còn conflict marker, duplicate DI implementation hoặc duplicate webhook route. Release build và WebApplicationFactory startup pass. |
| AC-03 | HTTP create/confirm/cancel/complete/POS vẫn dùng BE1; RequestKey, snapshot, reservation, allocation, ledger và expiry contract được giữ. Chạy các suite BE1 liên quan trên baseline ghép. |
| AC-04 | Webhook 3 kênh đi qua ingress BE1; một request hợp lệ tạo reservation thật; thiếu tồn không tạo Reserved giả. HTTP request không gặp ambiguous endpoint. |
| AC-05 | Replay cùng tenant/channel/shop/order/payload không tăng giữ chỗ; shop khác không trả nhầm đơn; payload mismatch bị từ chối theo error contract BE1. |
| AC-06 | Ghi rõ query stub còn lại dành cho T02 và trạng thái batch tuần tự dành cho T05; không gọi toàn bộ BE2 đã hoàn thành. |

Không chọn toàn bộ file bằng `ours/theirs`, không viết lại BE1 engine hoặc chỉnh số liệu seed để test giả pass. Nếu test BE1 fail, xác định regression hay baseline và ghi đúng evidence.

## Bàn giao

Tạo `handover.md`, evidence và AC→testcase trong thư mục này. Trạng thái cao nhất Anti tự ghi: `READY_FOR_REVIEW v1.0`. Theo prompt giao việc cập nhật, commit/push thay đổi T01 đúng nhánh OISMS-42 sau khi kiểm thử cần thiết pass; ghi trạng thái CI thực tế và lỗi còn mở. Codex review T01 trước khi giao T02. Không merge vào develop.
