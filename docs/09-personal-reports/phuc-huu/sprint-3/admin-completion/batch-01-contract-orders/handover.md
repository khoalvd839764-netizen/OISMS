# Bàn giao — S3-ADMIN-B01 / v1.0

- Vai trò: Antigravity (Frontend & API integration — phụ trách: Phúc Hữu)
- Trạng thái: **READY_FOR_REVIEW** (chờ Codex review nghiệm thu chung đợt B01)
- Vòng review: Review R1 -> Triển khai B01 (Contract, API đọc chứng từ và nghiệp vụ đơn hàng)
- Baseline và phạm vi diff được báo cáo:
  - Repo root: `C:\Fontend`
  - Branch: `feature/OISMS-43-orders-transfers`
  - Commit HEAD baseline: `403431446d213bbbf7f27d01b9622672f2fa4653`
  - Develop reference: `ecc0c85c7d6f81841d8138ff61ae40d33ef013d2`
  - Không commit, không push, không đổi base PR theo đúng chỉ đạo.
- Hành vi đã thay đổi, file chính và lý do:
  1. `src/backend/src/OISM.Application/Features/StockTransfers/Services/StockTransferService.cs`: Triển khai `GetTransferByIdAsync` và `GetTransfersAsync` đọc dữ liệu thật từ EF Core với phân trang, lọc theo chi nhánh/kho/trạng thái và cô lập tenant tuyệt đối (`TenantId == _tenantService.TenantId`).
  2. `src/backend/src/OISM.WebApi/Controllers/StockTransfersController.cs`: Thêm `[HttpGet]` và `[HttpGet("{id:guid}")]` với `[Authorize(Roles = "Owner,Staff")]`, trả RFC 7807 ProblemDetails khi lỗi hoặc không tìm thấy (404 `NOT_FOUND`).
  3. `src/backend/src/OISM.Application/Features/StockCounts/Services/StockCountService.cs`: Triển khai `GetStockCountByIdAsync` và `GetStockCountsAsync` đọc dữ liệu thật từ EF Core với phân trang, lọc và cô lập tenant.
  4. `src/backend/src/OISM.WebApi/Controllers/StockCountsController.cs`: Thêm `[HttpGet]` và `[HttpGet("{id:guid}")]` với `[Authorize(Roles = "Owner,Staff")]`, trả ProblemDetails khi 404 `NOT_FOUND`.
  5. `src/frontend/admin-dashboard/src/services/stockTransferService.ts`: Bỏ khối `catch` nuốt lỗi trả `loadLocalTransfers()`, trả dữ liệu server thật hoặc ném lỗi để UI hiển thị trung thực.
  6. `src/frontend/admin-dashboard/src/services/stockCountService.ts`: Bỏ khối `catch` nuốt lỗi trả `loadLocalCounts()`, ném lỗi để UI hiển thị trung thực.
  7. `src/frontend/admin-dashboard/src/types/ordersCenter.ts` & `src/frontend/admin-dashboard/src/services/orderService.ts`: Bổ sung `CreateOrderRequest` và phương thức `createOrder` gọi `POST /api/orders` với Header `Idempotency-Key` và `X-Request-Key`.
  8. `src/frontend/admin-dashboard/src/pages/OrdersPage.tsx`:
     - Chống response đảo thứ tự (race condition) khi lọc nhanh hoặc chuyển trang bằng `orderReqSeqRef`.
     - Thay thế `apiAdapter.checkout` bằng `orderService.createOrder` (`POST /api/orders`) ghi nhận đơn hàng và giữ chỗ thật trong PostgreSQL, duy trì `orderIdempotencyKey` và payload ổn định khi retry.
     - Phân loại rõ ràng các mã lỗi (400 validation, 401/403 forbidden, 409 thiếu tồn kho, network).
     - Đọc query parameter `orderId` từ URL (`useSearchParams`) để mở trực tiếp chi tiết đơn hàng khi click từ thông báo NotificationCenter.
  9. `src/frontend/admin-dashboard/src/components/orders/OrderDetailDrawer.tsx`:
     - Ẩn giá vốn WAC đối với vai trò không phải Owner (`account.role !== 'Owner'`).
     - Tối ưu hóa layout in chứng từ `@media print`: ẩn nút thao tác, nút đóng, modal hủy đơn và cột/dòng giá vốn COGS snapshot (`print:hidden`), đảm bảo phiếu xuất kho/giao hàng không làm lộ giá vốn.
  10. `src/frontend/admin-dashboard/src/pages/InventoryTransfersPage.tsx` & `src/frontend/admin-dashboard/src/pages/StockCountsPage.tsx`: Thêm thông báo lỗi trực quan kèm nút "Thử lại" khi API gặp sự cố, loại bỏ tình trạng hiển thị bảng rỗng giả mạo dữ liệu thành công.

---

## Bằng chứng kiểm chứng

| AC | Trạng thái | Bằng chứng kiểm chứng cụ thể |
| --- | --- | --- |
| **A01-AC-01** | Đạt | Khóa bảng Route/DTO/Filter/Status/Role/ErrorCode chi tiết tại `evidence/contract-mapping.md`. Khớp 100% giữa WebApi Controller và Frontend Types. |
| **A01-AC-02** | Đạt | `StockDocumentQueryTests.cs` (5 tests) & `StockDocumentApiTests.cs` (2 tests) PASS: GET list/detail transfers/counts hoạt động với dữ liệu thật; phân trang/filter chính xác; 404 ProblemDetails cho ID không tồn tại; cô lập Tenant A không rò Tenant B; 403 Forbidden cho Cashier; 401 cho Unauthenticated. |
| **A01-AC-03** | Đạt | Đã loại bỏ fallback local cache trong `stockTransferService.ts` và `stockCountService.ts`. `InventoryTransfersPage.tsx` và `StockCountsPage.tsx` hiển thị banner lỗi và nút "Thử lại" khi API lỗi thay vì giả danh sách rỗng. |
| **A01-AC-04** | Đạt | Session/auth token được gắn đầy đủ qua `httpClient`; mã lỗi 401/403 không bị fallback che giấu; phân quyền `Owner,Staff` trên Controller được kiểm chứng bằng test tích hợp HTTP. |
| **A02-AC-01** | Đạt | `OrdersPage.tsx` tích hợp `orderReqSeqRef` kiểm soát thứ tự phản hồi; response cũ chậm hơn bị hủy bỏ, chống ghi đè dữ liệu khi người dùng chuyển filter/page nhanh. |
| **A02-AC-02** | Đạt | `OrdersPage.tsx` gọi trực tiếp `orderService.createOrder` (`POST /api/orders`) với `RequestKey` truyền qua header `Idempotency-Key` / `X-Request-Key` và body payload; duy trì key và payload khi retry trong cùng phiên; phân loại lỗi chi tiết (thiếu tồn, 400, 401/403, network). |
| **A02-AC-03** | Đạt | State machine khớp backend: `Reserved` -> `confirm` / `cancel`; `Confirmed` -> `process` / `complete`; `Processing` -> `complete`. Sau mỗi thao tác mutation đều gọi `loadOrders()` để refetch dữ liệu server authoritative. |
| **A02-AC-04** | Đạt | `OrdersPage.tsx` hỗ trợ `useSearchParams` bắt `orderId` từ link notification để mở đúng đơn hàng; `OrderDetailDrawer.tsx` áp dụng `print:hidden` và kiểm soát vai trò `isOwner`, không in hở COGS snapshot trên chứng từ giao hàng. |

---

## Lệnh và thư mục chạy kiểm tra

| Lệnh thực tế | Thư mục chạy | Môi trường / Provider | Pass / Fail / Not Run | Kết quả và giới hạn |
| --- | --- | --- | --- | --- |
| `dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj` | `C:\Fontend` | .NET 8 / InMemory TestDbContext | **PASS** (151/151 tests) | 146 baseline unit tests + 5 tests mới `StockDocumentQueryTests` kiểm chứng tenant isolation, query filter và pagination. |
| `dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj --filter "FullyQualifiedName~StockDocumentApiTests"` | `C:\Fontend` | .NET 8 / CustomWebApplicationFactory | **PASS** (2/2 tests) | Kiểm chứng HTTP GET list/detail, role `Owner,Staff` 200, `Cashier` 403 Forbidden, Anonymous 401, Not Found 404 ProblemDetails. |
| `npm --prefix src/frontend/admin-dashboard run build` | `C:\Fontend` | Node.js v24.16 / Vite 8 + tsc | **PASS** | `tsc -b && vite build` thành công, 0 lỗi TypeScript, 0 lỗi cú pháp. |
| `dotnet test ... --filter "FullyQualifiedName~OnlineOrderLifecycleIntegrationTests"` | `C:\Fontend` | PostgreSQL Docker daemon | **NOT RUN** (Cần Docker) | Yêu cầu daemon PostgreSQL thật trên localhost (PostgresTestEnvironment.cs). Ghi nhận đúng hiện trạng môi trường, không làm giả kết quả. |

---

## Bảng Finding

| ID | Trạng thái | Nguyên nhân và phần sửa | Scenario / bằng chứng |
| --- | --- | --- | --- |
| **PR26-F04** | **Đã sửa chờ review** | Backend thiếu `GET` list/detail transfers/counts (`NotImplementedException`), frontend che lỗi bằng `loadLocalTransfers()`. **Đã sửa:** Triển khai query methods trong `StockTransferService.cs`, `StockCountService.cs`, bổ sung endpoints trong `StockTransfersController.cs`, `StockCountsController.cs`; bỏ fallback local cache và thêm UI error banner trên Frontend. | Test `StockDocumentQueryTests` PASS (5/5), `StockDocumentApiTests` PASS (2/2), Frontend build PASS. |
| **PR26-F01** | Mở (Thuộc B02) | POST chuyển kho thất bại giả lập Draft thành công. | PLANNED cho đợt S3-ADMIN-B02. |
| **PR26-F02** | Mở (Thuộc B03) | SignalR Hub không đổi context/stop khi logout. | PLANNED cho đợt S3-ADMIN-B03. |
| **PR26-F03** | Mở (Thuộc B02) | Chốt kiểm kê bỏ qua số lượng đang sửa trên màn hình. | PLANNED cho đợt S3-ADMIN-B02. |
| **PR26-F05** | Mở (Thuộc B02) | Action matrix chuyển kho sai trạng thái. | PLANNED cho đợt S3-ADMIN-B02. |
| **PR26-F06** | Mở (Thuộc B02) | Đổi RequestKey khi retry đợt kiểm kê / chuyển kho. | Đã chuẩn hóa trước cho Orders trong B01; phần Transfers/Counts thuộc B02. |
| **PR26-F07** | Mở (Thuộc B03) | Hub chưa nối branch group và bảng tồn. | PLANNED cho đợt S3-ADMIN-B03. |
| **PR26-F08** | Mở (Thuộc B03) | PR trỏ main thay vì develop. | PLANNED cho bước delivery cuối B03. |

- **Blocker còn lại và điều kiện gỡ:** Không có blocker nội bộ trong B01. Các đợt B02 (Chuyển kho & Kiểm kê) và B03 (Báo cáo & Realtime) sẽ tiếp tục sau khi B01 được reviewer nghiệm thu.
- **Đề xuất ngoài phạm vi:** Không có.
