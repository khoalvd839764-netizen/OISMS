# Bàn giao — S3-ADMIN-B01 / v1.1

- **Vai trò:** Antigravity (Frontend & API integration — phụ trách: Phúc Hữu)
- **Trạng thái:** **READY_FOR_REVIEW** (chờ Codex review nghiệm thu các finding của B01)
- **Vòng review:** Review R1 & Codex Baseline Review (`b1b9c804be2dbe033b25a55f4ca4ae925753d5a2`) -> Khắc phục triệt để 5 findings (B01-F01, B01-F02, B01-F03, B01-F04, PR26-F04).
- **Baseline và phạm vi diff:**
  - Repo root: `C:\Fontend`
  - Branch: `feature/OISMS-43-orders-transfers`
  - Baseline commit Codex đã review: `b1b9c804be2dbe033b25a55f4ca4ae925753d5a2`
  - Phạm vi sửa đổi: Hoàn toàn trong phạm vi B01 (Orders Center, Contract & API đọc transfers/counts). Không sửa code POS, không triển khai trước B02/B03.

---

## 1. Chi tiết khắc phục 5 finding của đợt review

### 1. B01-F01 (P1) — Khóa chặt Contract kênh bán Admin & Rõ ràng phạm vi giữ chỗ
- **Nguyên nhân baseline:** `OrdersPage.tsx` mặc định chọn kênh "Shopee" và dropdown cho chọn Shopee/TikTok/Lazada/POS. Tuy nhiên endpoint `POST /api/orders` chỉ chấp nhận `channel == "Admin"` (trả về 400 `VALIDATION_ERROR` cho mọi kênh khác). Đồng thời backend nhận `BranchId` chứ không nhận `WarehouseId`.
- **Giải pháp:**
  - Cố định kênh bán là `"Admin"` cho đơn tạo thủ công trên Admin Dashboard (`newChannel = "Admin"`), hiển thị input read-only "Admin (Đơn tạo thủ công)".
  - Điều chỉnh nhãn và ghi chú kho hàng: làm rõ việc giữ chỗ tồn kho online được backend hạch toán trên số dư khả dụng của **Toàn chi nhánh (Branch-level balance)**. Kho được chọn trong modal chỉ phục vụ tra cứu tồn khả dụng thực tế tại điểm kho để đối chiếu.

### 2. B01-F02 (P1) — Bảo toàn Snapshot yêu cầu & Cách ly Owner chống Replay chéo
- **Nguyên nhân baseline:**
  - Retry dựng lại payload từ catalog nên giá bán bị thay đổi theo catalog sau khi reload.
  - Storage key `oism_pending_order_${tenantId}` dùng chung cho cả tenant dẫn đến User B có thể gửi lại pending của User A.
  - Khi rớt mạng (kết quả unknown), người dùng gõ sửa tên khách hàng thì `handleFormChange` tự ý xóa pending và sinh key mới.
  - Mọi mã lỗi 409 đều bị quy thành thiếu tồn kho.
- **Giải pháp:**
  - Cách ly bộ nhớ lưu trữ `PendingOrderSnapshot` theo cả `tenantId` VÀ `userId`: `oism_pending_order_${tenantId}_${userId}` trong `apiAdapter.ts` và `OrdersPage.tsx`. Chặn tuyệt đối user khác đọc hoặc replay.
  - Lưu nguyên trạng `PendingOrderSnapshot` gồm toàn bộ `requestPayload` (giá snapshot, số lượng, SKU, branchId, customerName, requestKey). Khi retry gửi lại đúng snapshot này, không bao giờ đọc lại từ catalog.
  - Khi gặp sự cố mạng / mất kết nối (`isUnknownState = true`), khóa chỉnh sửa biểu mẫu (disabled inputs) kèm cảnh báo hướng dẫn rõ ràng. `handleFormChange` không xóa snapshot khi ở trạng thái unknown; chỉ người dùng bấm "Hủy và tạo yêu cầu mới" sau khi xác nhận mới giải phóng snapshot.
  - Phân định rõ ràng mã lỗi 409: `INSUFFICIENT_STOCK` (thiếu tồn kho), `IDEMPOTENCY_CONFLICT` / `IDEMPOTENCY_PAYLOAD_MISMATCH` (xung đột định danh thao tác), và các lỗi 409 nghiệp vụ khác.

### 3. B01-F03 (P2) — Giải phóng khóa Submit an toàn & Loại bỏ luồng Checkout cũ
- **Nguyên nhân baseline:**
  - `createOrder` gán `isCreatingOrderRef.current = true` trước khi validation; khi SKU inactive hoặc dữ liệu sai thì `return` sớm nằm ngoài khối `try/finally`, làm khóa submit bị kẹt vĩnh viễn.
  - Nút "Thử lại" bên trong thông báo lỗi gọi ngược về `apiAdapter.checkout(...)` (luồng checkout POS cũ) thay vì `createOrder()`.
- **Giải pháp:**
  - Đưa toàn bộ các bước validation lên trước khi chiếm giữ khóa submit `isCreatingOrderRef.current`.
  - Đặt `isCreatingOrderRef.current = false` và `setLoadingCreate(false)` trong khối `finally` ngoài cùng, bảo đảm mọi đường thoát luôn giải phóng khóa.
  - Chuẩn hóa nút "Thử lại": luôn gọi `createOrder()`, loại bỏ triệt để mọi tham chiếu đến `apiAdapter.checkout`.

### 4. B01-F04 (P1) — Chống Race Condition chi tiết đơn hàng & Khóa danh tính thao tác
- **Nguyên nhân baseline:**
  - Mở đơn A, đóng rồi mở đơn B; nếu GET B trả về trước và GET A trả về sau, `detail` của B bị ghi đè bởi A, dẫn đến thao tác Duyệt nhầm đơn A.
  - Các handler thao tác (`handleAction`, `handleCancel`) không kiểm tra xem `detail.id` có khớp với `orderId` hiện tại của Drawer hay không.
- **Giải pháp:**
  - Thêm `activeReqRef = useRef(0)` trong `OrderDetailDrawer.tsx`.
  - Khi `orderId` thay đổi, lập tức `setDetail(null)` để làm sạch dữ liệu cũ và tăng sequence ref; phản hồi chậm từ đơn hàng trước bị hủy bỏ ngay lập tức.
  - Thêm chốt chặn danh tính (Identity Guard) bắt buộc trước mọi mutation:
    ```typescript
    if (!detail || detail.id !== orderId || loading || actionLoading) return;
    ```
  - Khi xảy ra lỗi thao tác (409, conflict), tự động tải lại trạng thái mới nhất từ server (`orderService.getOrderById(detail.id)`) để đồng bộ UI.

### 5. PR26-F04 (P1) — Hoàn tất tích hợp API đọc Transfers & Counts
- **Nguyên nhân baseline:**
  - Backend trả phân trang mặc định 20 dòng (`PageNumber`, `PageSize`), Frontend gọi không truyền phân trang và không hiển thị thanh điều khiển trang.
  - Sai lệch tên trường: Backend trả `number` và `productName`, Frontend dùng `transferNumber` và `skuName`.
  - Nếu API trả HTTP 200 payload không đúng cấu trúc, service âm thầm trả về mảng rỗng `[]` như thể không có dữ liệu.
- **Giải pháp:**
  - Chuẩn hóa DTO và Adapter: `StockTransferPagedResult` và `StockCountPagedResult` tiếp nhận đầy đủ metadata phân trang (`items`, `totalCount`, `pageNumber`, `pageSize`, `totalPages`, `hasPreviousPage`, `hasNextPage`).
  - Hàm `normalizeTransfer` ánh xạ 2 chiều (`number` <-> `transferNumber`, `productName` <-> `skuName`).
  - Thêm giao diện điều khiển phân trang đầy đủ trên `InventoryTransfersPage.tsx` và `StockCountsPage.tsx`.
  - Thêm sequence ref (`countReqSeqRef`, `transferReqSeqRef`) trên cả 2 màn hình danh sách và Drawer để triệt tiêu race condition khi đổi bộ lọc.
  - Dữ liệu trả về không đúng cấu trúc DTO sẽ ném lỗi rõ ràng, không nuốt thành mảng rỗng.

---

## 2. Bảng bằng chứng kiểm chứng (Honest Evidence Mapping)

| AC / Finding | Trạng thái | Bằng chứng kiểm chứng chi tiết | Provider / Môi trường |
| --- | --- | --- | --- |
| **B01-F01** | Đạt | `B01FindingRegressionTests.B01_F01_CreateOrder_NonAdminChannel_ShouldThrow_ValidationError_400` PASS (4 channels: Shopee, TikTok, Lazada, POS đều bị từ chối 400). `B01_F01_CreateOrder_AdminChannel_ShouldSucceed_AndReserveStock` PASS (channel Admin tạo thành công, giữ chỗ branch balance = 2). `OrdersPage.tsx` khóa read-only Admin. | .NET 8 / InMemory TestDbContext & Frontend Vite Build |
| **B01-F02** | Đạt | `B01FindingRegressionTests.B01_F02_IdempotencyReplay_SamePayload_ShouldSucceed_DifferentPayload_ShouldThrow_409` PASS (cùng payload trả lại kết quả cũ `IsReplay = true`, khác payload ném 409). `apiAdapter.getPendingOrderStorageKey` cách ly tenantId + userId. Biểu mẫu khóa an toàn khi unknown state. | .NET 8 / InMemory TestDbContext & Frontend Build |
| **B01-F03** | Đạt | `OrdersPage.tsx` validation trước khóa; khối `finally` giải phóng `isCreatingOrderRef.current = false` trên 100% đường thoát. Nút "Thử lại" gọi `createOrder()`. | Frontend Build & Mã nguồn `OrdersPage.tsx` |
| **B01-F04** | Đạt | `OrderDetailDrawer.tsx` `activeReqRef` hủy bỏ response chậm; `setDetail(null)` ngay khi đổi ID; Identity guard `detail.id !== orderId` chặn thao tác nhầm đơn. | Frontend Build & Mã nguồn `OrderDetailDrawer.tsx` |
| **PR26-F04** | Đạt | `B01FindingRegressionTests.PR26_F04_FilterRequestDtos_ShouldHaveCorrectPaginationDefaults` PASS. `StockDocumentQueryTests` (5 tests) PASS. `StockDocumentApiTests` (2 tests) PASS. Thanh phân trang và mapping trường hiển thị chuẩn xác trên UI. | .NET 8 InMemory / WebApplicationFactory & Frontend Build |
| **A01-AC-01..04** | Đạt | Đọc dữ liệu thật qua EF Core, cô lập tenant tuyệt đối, phân quyền `Owner,Staff` 200, `Cashier` 403, unauthenticated 401. Không nuốt lỗi thành mảng rỗng. | Unit Tests (158/158 PASS) & Integration Tests (2/2 PASS) |
| **A02-AC-01..04** | Đạt | Phân trang server-side, `orderReqSeqRef` chống đảo thứ tự response, `requestKey` header Idempotency-Key, in chứng từ ẩn WAC/COGS (`print:hidden`). | Unit Tests & Frontend Build |

---

## 3. Lệnh và thư mục chạy kiểm tra thực tế

| Lệnh thực tế | Thư mục chạy | Môi trường / Provider | Pass / Fail / Not Run | Kết quả chi tiết |
| --- | --- | --- | --- | --- |
| `dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj` | `C:\Fontend` | .NET 8 / InMemory TestDbContext | **PASS** (158/158 tests) | 151 baseline unit tests + 7 unit tests hồi quy mới (`B01FindingRegressionTests`) kiểm chứng B01-F01..F04 & PR26-F04. Thời gian: 3s. |
| `dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj --filter "FullyQualifiedName~StockDocumentApiTests"` | `C:\Fontend` | .NET 8 / CustomWebApplicationFactory | **PASS** (2/2 tests) | Kiểm chứng HTTP GET list/detail transfers & counts, role `Owner,Staff` 200, `Cashier` 403 Forbidden, 404 ProblemDetails. |
| `npm --prefix src/frontend/admin-dashboard run build` | `C:\Fontend` | Node.js v24.16 / Vite 8.0.5 + tsc | **PASS** | `tsc -b && vite build` hoàn tất không có lỗi, 0 cảnh báo TypeScript. |
| `dotnet test ... --filter "FullyQualifiedName~OnlineOrderLifecycleIntegrationTests"` | `C:\Fontend` | Live PostgreSQL Docker daemon | **NOT RUN** (Cần Live Docker DB) | Yêu cầu máy chủ daemon PostgreSQL thực tế trên cổng 5432. Ghi nhận trung thực hiện trạng môi trường CI/local. |

---

## 4. Danh sách file thay đổi chính trong đợt fix

1. `src/frontend/admin-dashboard/src/pages/OrdersPage.tsx`: Khóa channel Admin (B01-F01), snapshot request & cách ly owner tenantId/userId (B01-F02), giải phóng submit lock an toàn (B01-F03), làm rõ phạm vi giữ chỗ chi nhánh.
2. `src/frontend/admin-dashboard/src/services/apiAdapter.ts`: Cập nhật `getPendingOrderStorageKey` nhận `(tenantId, userId)`.
3. `src/frontend/admin-dashboard/src/components/orders/OrderDetailDrawer.tsx`: `activeReqRef` chống response cũ đè dữ liệu, identity guard `detail.id !== orderId` chống duyệt nhầm đơn (B01-F04).
4. `src/frontend/admin-dashboard/src/types/stockTransfer.ts` & `stockCount.ts`: Bổ sung DTO phân trang đầy đủ (`StockTransferPagedResult`, `StockCountPagedResult`) và filter pagination.
5. `src/frontend/admin-dashboard/src/services/stockTransferService.ts` & `stockCountService.ts`: Hỗ trợ phân trang, mapping trường dữ liệu (`number` <-> `transferNumber`, `productName` <-> `skuName`), ném lỗi khi payload sai cấu trúc.
6. `src/frontend/admin-dashboard/src/pages/InventoryTransfersPage.tsx` & `StockCountsPage.tsx`: Thêm thanh điều khiển phân trang UI, `transferReqSeqRef` & `countReqSeqRef` chống race condition.
7. `src/frontend/admin-dashboard/src/components/transfers/TransferDetailDrawer.tsx` & `StockCountDetailDrawer.tsx`: Bổ sung `activeReqRef` và identity guards trước mọi thao tác xuất gửi/nhận/từ chối/chốt kiểm kê.
8. `src/backend/tests/OISM.UnitTests/B01FindingRegressionTests.cs`: Bộ unit tests tự động kiểm chứng 5 finding.

---

- **Trạng thái:** **READY_FOR_REVIEW** (Đã đóng toàn bộ 5 finding theo phản hồi Codex, sẵn sàng bàn giao cho Codex nghiệm thu).
