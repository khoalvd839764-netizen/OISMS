# Task brief — S3-ADMIN-B01 / v1.0

Vai trò thực hiện: Antigravity (Frontend & API integration - Phúc Hữu). Đọc `skills/codex-antigravity-workflow/SKILL.md` và reference của vai trò trước khi làm.

## Mục tiêu và baseline

- Yêu cầu người dùng / sprint / người phụ trách: Hoàn thiện Admin OISMS, đợt S3-ADMIN-B01: Contract, API đọc chứng từ và nghiệp vụ đơn hàng.
- Kết quả cần đạt trong task này:
  1. Part A (A01-AC-01..04): Khóa contract route/DTO/filter/status/role/errorCode; triển khai API đọc chứng từ thật (GET list/detail) cho Stock Transfers và Stock Counts trên backend với multi-tenant isolation, role authorization (`Owner,Staff` 200, Cashier 403, 404 cho ID không tồn tại); loại bỏ hoàn toàn fake local cache fallback trong frontend services; đóng finding **PR26-F04** bằng chứng cứ API + UI.
  2. Part B (A02-AC-01..04): Orders Center server-side list/filter/pagination chống response đảo thứ tự (race condition); tạo đơn và giữ chỗ qua API thật (`POST /api/orders`) với `RequestKey` và payload ổn định khi retry; xử lý phân loại lỗi (thiếu tồn, 400/401/403/409, network); detail và action matrix (`confirm`, `process`, `complete`, `cancel`) bám sát backend state machine và refetch server; notification mở đúng đơn; in chứng từ từ server snapshot không rò rỉ COGS cho vai trò không được phép.
- Repo, branch/commit và thay đổi sẵn có cần bảo toàn:
  - Repo root: `C:\Fontend`
  - Branch: `feature/OISMS-43-orders-transfers`
  - Baseline commit HEAD: `403431446d213bbbf7f27d01b9622672f2fa4653`
  - Develop commit: `ecc0c85c7d6f81841d8138ff61ae40d33ef013d2`
  - Bảo toàn toàn bộ working tree hiện tại, không commit/push/merge/đổi base PR.
- Nguồn yêu cầu, chuẩn và report trước cần đọc:
  - `skills/codex-antigravity-workflow/SKILL.md`
  - `docs/09-personal-reports/phuc-huu/sprint-3/admin-completion/PLAN.md` (v1.1)
  - `docs/09-personal-reports/phuc-huu/sprint-3/admin-completion/reviews/pr26-review-r1.md`
- Đã có bằng chứng / còn thiếu:
  - Review R1 xác định PR26-F04: GET list/detail cho transfers và counts chưa có controller/query methods (`NotImplementedException`).
  - Unit tests backend: 146 tests passed.

## Phạm vi

- Thành phần/hành vi được sửa, file dự kiến:
  - Backend API:
    - `src/backend/src/OISM.Application/Features/StockTransfers/Services/StockTransferService.cs` (GetTransferByIdAsync, GetTransfersAsync)
    - `src/backend/src/OISM.WebApi/Controllers/StockTransfersController.cs` (GET /api/stock-transfers, GET /api/stock-transfers/{id})
    - `src/backend/src/OISM.Application/Features/StockCounts/Services/StockCountService.cs` (GetStockCountByIdAsync, GetStockCountsAsync)
    - `src/backend/src/OISM.WebApi/Controllers/StockCountsController.cs` (GET /api/stock-counts, GET /api/stock-counts/{id})
  - Frontend Admin:
    - `src/frontend/admin-dashboard/src/services/stockTransferService.ts` (bỏ fallback fake local cache, propagate API errors)
    - `src/frontend/admin-dashboard/src/services/stockCountService.ts` (bỏ fallback fake local cache, propagate API errors)
    - `src/frontend/admin-dashboard/src/services/orderService.ts` (thêm createOrder gọi POST /api/orders)
    - `src/frontend/admin-dashboard/src/pages/OrdersPage.tsx` (seq ref chống out-of-order responses, gọi POST /api/orders qua orderService, retry giữ nguyên RequestKey)
    - `src/frontend/admin-dashboard/src/components/orders/OrderDetailDrawer.tsx` (bảo đảm action matrix & in phiếu chuẩn hóa)
  - Test/helper/tài liệu trực tiếp phục vụ task:
    - Backend Unit/Integration tests: `StockTransferQueryTests.cs`, `StockCountQueryTests.cs` (kiểm chứng query, pagination, tenant isolation, 404, role authorization).
    - Frontend build & unit/integration checks.
- Ngoài phạm vi:
  - Không viết lại engine trừ tồn kho, ledger, WAC, schema database, hay payment POS.
  - Không chỉnh sửa `src/frontend/pos-app`.
  - Không triển khai Admin B02 (chuyển kho/kiểm kê mutations) hoặc B03 (báo cáo/realtime cuối).
  - Không commit/push/đổi base PR trong lượt này.
- Ràng buộc tương thích, dữ liệu, quyền truy cập:
  - Chuẩn Clean Architecture & Multi-tenancy (`IMustHaveTenant`).
  - Phân quyền: `Owner,Staff` được phép xem transfers/counts/orders; `Cashier` bị 403 Forbidden đối với transfers/counts.
  - RFC 7807 ProblemDetails cho tất cả error responses.

## Tiêu chí nghiệm thu

| ID | Hành vi hoặc đầu ra cần đạt | Cách kiểm chứng |
| --- | --- | --- |
| A01-AC-01 | Khóa bảng route/DTO/filter/status/role/errorCode theo source hiện tại. Không dùng TODO thay contract. | Đối chiếu và tài liệu hóa trong handover.md. |
| A01-AC-02 | GET list/detail transfers/counts hoạt động với dữ liệu thật, filter/page/totalCount nhất quán; 404 đúng cho ID không thấy, không rò tenant. Query tenant A không lấy dữ liệu B, Cashier bị từ chối 403. | Backend integration/unit tests với InMemory/Test DB kiểm chứng 200, 404, 403, tenant isolation. |
| A01-AC-03 | Frontend đọc đúng DTO gồm number/productName, phân trang đủ; browser mới/session mới cùng tenant đọc chứng từ cũ từ API. Lỗi/thiếu endpoint hiển thị error/stale/retry, không hiện [] hoặc cache cũ như fresh. | Code review & component/service test loại bỏ fake fallback; hiển thị trạng thái lỗi. |
| A01-AC-04 | Session/context owner/tenant/token/branch được truyền và vô hiệu async response/cache cũ; auth 401/403 không bị fallback che. | Service interceptors và auth context không swallow auth errors. |
| A02-AC-01 | List/search/status/channel/branch/date/page khớp contract OrderFilterDto; thay filter nhanh hoặc response đảo thứ tự không vẽ dữ liệu query cũ. | Cơ chế sequence counter (orderReqSeqRef) trong OrdersPage.tsx. |
| A02-AC-02 | Tạo đơn Admin và giữ chỗ thật (`POST /api/orders`), RequestKey và payload ổn định cho cùng ý định retry; phân loại lỗi thiếu tồn/400/401/403/409/network; reload/reauth không gửi yêu cầu của owner cũ bằng phiên mới. | OrdersPage.tsx tích hợp orderService.createOrder, lưu trữ pending order state với stable key. |
| A02-AC-03 | Detail/action matrix (Draft/Reserved/Confirmed/Processing/Completed/Cancelled) bám sát backend; confirm/process/complete/cancel thành công thì refetch server, không fake state local. Lỗi 409 không thao tác nhầm đơn. | State machine matching backend controller; gọi refetch Orders sau mỗi mutation. |
| A02-AC-04 | Notification link orderId mở đúng đơn; in phiếu giao hàng đúng server snapshot, không hở COGS cho vai trò không được phép. | In phiếu qua print preview chuẩn hóa từ snapshot server. |

## Finding cần sửa (từ Review R1)

| ID | AC | Mức độ, bằng chứng và kỳ vọng/thực tế | Điều kiện đóng |
| --- | --- | --- | --- |
| PR26-F04 | A01-AC-02, A01-AC-03 | P1 / phụ thuộc BE2: API đọc chuyển kho/kiểm kê chưa được triển khai (`NotImplementedException`), UI che lỗi bằng local cache. | Bổ sung GET list/detail trên backend; loại bỏ local cache fallback trên frontend; test HTTP tenant A/B, 404, 403 và UI hiển thị lỗi trung thực. |
