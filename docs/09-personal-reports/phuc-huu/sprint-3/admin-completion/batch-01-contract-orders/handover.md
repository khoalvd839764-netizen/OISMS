# Bàn giao — S3-ADMIN-B01 / v1.2

- **Vai trò:** Antigravity (Frontend & API integration — phụ trách: Phúc Hữu)
- **Trạng thái:** **READY_FOR_REVIEW** (chờ Codex review nghiệm thu các finding của B01)
- **Vòng review:** Review R2 (`a3c4739206130f6ef16989ff4a01f52cb886e72f`) -> Hoàn thiện triệt để 3 findings còn mở (B01-F02, B01-F04, PR26-F04).
- **Baseline và phạm vi diff:**
  - Repo root: `C:\Fontend`
  - Branch: `feature/OISMS-43-orders-transfers`
  - Baseline commit Codex đã review R2: `a3c4739206130f6ef16989ff4a01f52cb886e72f`
  - Phạm vi sửa đổi: Hoàn toàn trong phạm vi B01 (Orders Center, Contract & API đọc transfers/counts). Không sửa code POS, không triển khai trước B02/B03.

---

## 1. Chi tiết khắc phục các finding theo Review R2

### 1. B01-F02 — Bảo toàn yêu cầu chưa rõ kết quả & Khóa đồng bộ form
- **Hiện tượng Codex R2 tái hiện:**
  - Khi gửi tạo đơn, nếu giữ phản hồi chưa trả rồi reload trang và mở lại modal, việc gõ sửa tên khách hàng làm pending bị tự ý xóa và sinh key mới mà không qua bước xác nhận discard. Nguyên nhân: snapshot lưu `isUnknownState=false` trước khi gọi POST.
  - Sau lỗi mạng, chỉ có ô tên khách hàng bị khóa; các ô chi nhánh, kho, SKU, số lượng vẫn sửa được, dẫn đến giao diện hiển thị dữ liệu mới nhưng retry lại gửi snapshot cũ.
- **Giải pháp v1.2:**
  - **Lưu snapshot ở trạng thái unknown trước POST:** `isUnknownState: true` được lưu vào `PendingOrderSnapshot` NGAY TRƯỚC KHI gửi `POST /api/orders`. Nếu reload hoặc rớt mạng khi POST đang bay, snapshot khôi phục sau reload luôn ở trạng thái unknown.
  - **Khóa toàn bộ trường biểu mẫu (`isFormLocked`):** Khi có snapshot đang chờ (`isRetryingOrder || isUnknownState || !!pendingSnapshot`), toàn bộ 5 trường giao dịch gồm Tên khách, Chi nhánh xuất, Kho xuất, SKU mặt hàng và Số lượng đặt đều bị khóa (`disabled={isFormLocked}`). Giao diện và payload gửi đi luôn nhất quán 100%.
  - **Chặn tuyệt đối silent wipe / key regen:** `handleFormChange` là no-op khi `isFormLocked`. Chỉ người dùng chủ động bấm "Hủy và tạo yêu cầu mới" và xác nhận qua dialog xác nhận mới được phép xóa snapshot và sinh key mới.

### 2. B01-F04 — Chặn mọi phản hồi cũ ghi đè đơn đang chọn (All-Paths Generation & Identity Guards)
- **Hiện tượng Codex R2 tái hiện:**
  - Mở đơn A, bấm duyệt A và giữ phản hồi; đóng rồi mở đơn B; khi thao tác A hoàn tất thì drawer B bị ghi đè thành A.
  - Nút "Thử lại" tải chi tiết trả muộn khi đã chuyển sang đơn khác cũng ghi đè state của đơn mới.
- **Giải pháp v1.2:**
  - **Generation Guard đồng nhất trên mọi đường:** Áp dụng `activeReqRef = useRef(0)` kết hợp `orderId === targetOrderId` cho toàn bộ các luồng:
    1. Tải chi tiết ban đầu (`useEffect` - tăng generation ngay cả khi `orderId = null` lúc đóng drawer).
    2. Nút bấm "Thử lại" (`fetchDetail(orderId)` đồng bộ generation check).
    3. Thao tác duyệt/xử lý/hoàn tất (`handleAction` kiểm tra generation trước và sau khi refetch authoritative).
    4. Thao tác hủy đơn (`handleCancel` kiểm tra generation trước và sau khi refetch).
    5. Xử lý lỗi mutation (catch block chỉ alert và refetch nếu generation vẫn còn hiệu lực).
  - Đóng drawer hoặc chuyển đơn ngay lập tức vô hiệu hóa mọi callback đang bay, bảo đảm không còn bất kỳ kẽ hở ghi đè chéo nào.

### 3. PR26-F04 — Hoàn thiện phân trang/filter và xác thực detail DTO
- **Hiện tượng Codex R2 ghi nhận:**
  - `StockCountsPage`: Đang ở trang 2 của 21 phiếu, chọn chi nhánh chỉ có 1 phiếu thì vẫn giữ `pageNumber=2`, dẫn đến bảng hiện 0 dòng và "Trang 2 / 1". Đổi chi nhánh và bấm "Xóa bộ lọc" chưa reset `pageNumber`.
  - `stockTransferService`: `GET detail` trả 200 `{unexpected:true}` vẫn được normalize thành `id=""`, `status="Draft"`, `createdAt` hiện tại thay vì báo lỗi.
- **Giải pháp v1.2:**
  - **Reset pageNumber khi lọc & xử lý trang vượt ngưỡng:**
    - `StockCountsPage.tsx`: Reset `setPageNumber(1)` khi thay đổi chi nhánh, kho, trạng thái và khi bấm "Xóa bộ lọc".
    - Bổ sung cơ chế auto-recovery trong `loadCounts`: nếu `pageNumber > totalPages && totalPages > 0`, tự động lùi về trang 1.
    - Áp dụng cơ chế tương tự cho `InventoryTransfersPage.tsx`.
  - **Xác thực hợp đồng DTO nghiêm ngặt (Reject Malformed Details):**
    - `stockTransferService.ts`: `normalizeTransfer` kiểm tra bắt buộc `id` (phải khác rỗng và khác Guid.Empty), `number` (hoặc `transferNumber`), và `status`. Ném lỗi rõ ràng nếu thiếu trường danh tính, từ chối payload `{unexpected:true}`.
    - `stockCountService.ts`: `getStockCountById` kiểm tra bắt buộc `id`, `number`, `status`.
    - `ordersCenter.ts`: `normalizeSummary` kiểm tra bắt buộc `id` và `number`.

---

## 2. Bảng bằng chứng kiểm chứng (Honest Evidence Mapping)

| AC / Finding | Trạng thái | Bằng chứng kiểm chứng chi tiết | Provider / Môi trường |
| --- | --- | --- | --- |
| **B01-F01** | Đạt | Khóa channel Admin cho đơn manual, hiển thị read-only; làm rõ giữ chỗ online hạch toán trên số dư chi nhánh (Branch-level balance). | Frontend Build & Mã nguồn `OrdersPage.tsx` |
| **B01-F02** | Đạt | `test_regression_v12.cjs` (PASS): Snapshot lưu `isUnknownState=true` trước POST; reload khôi phục trạng thái unknown; `isFormLocked` khóa toàn bộ 5 trường giao dịch; User B không truy cập được pending User A. `B01FindingRegressionTests.B01_F02_IdempotencyReplay_SamePayload_ShouldSucceed_DifferentPayload_ShouldThrow_409` (.NET PASS). | Node.js Test Runner & .NET 8 InMemory TestDbContext |
| **B01-F03** | Đạt | Validation trước khóa submit; khối `finally` giải phóng `isCreatingOrderRef.current = false` trên 100% đường thoát. Nút "Thử lại" gọi `createOrder()`. | Frontend Build & Mã nguồn `OrdersPage.tsx` |
| **B01-F04** | Đạt | `test_regression_v12.cjs` (PASS): Generation & identity guards chặn phản hồi chậm từ mutation A và manual retry A khi đã đóng hoặc chuyển sang đơn B. | Node.js Test Runner & Mã nguồn `OrderDetailDrawer.tsx` |
| **PR26-F04** | Đạt | `test_regression_v12.cjs` (PASS): `normalizeTransfer` từ chối `{unexpected:true}`, id rỗng/Guid.Empty, thiếu number/status; `StockCountsPage` đổi chi nhánh/xóa filter reset pageNumber=1, tự lùi về trang 1 khi trang vượt tổng số trang. `StockDocumentQueryTests` (5 tests) PASS, `StockDocumentApiTests` (2 tests) PASS. | Node.js Test Runner & .NET 8 WebApplicationFactory |
| **A01-AC-01..04** | Đạt | Đọc dữ liệu thật qua EF Core, cô lập tenant tuyệt đối, phân quyền `Owner,Staff` 200, `Cashier` 403, unauthenticated 401. Không nuốt lỗi thành mảng rỗng. | Unit Tests (158/158 PASS) & Integration Tests (2/2 PASS) |
| **A02-AC-01..04** | Đạt | Phân trang server-side, `orderReqSeqRef` chống đảo thứ tự response, `requestKey` header Idempotency-Key, in chứng từ ẩn WAC/COGS (`print:hidden`). | Unit Tests & Frontend Build |

---

## 3. Lệnh và thư mục chạy kiểm tra thực tế

| Lệnh thực tế | Thư mục chạy | Môi trường / Provider | Pass / Fail / Not Run | Kết quả chi tiết |
| --- | --- | --- | --- | --- |
| `dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj` | `C:\Fontend` | .NET 8 / InMemory TestDbContext | **PASS** (158/158 tests) | 151 baseline unit tests + 7 unit tests hồi quy (`B01FindingRegressionTests`). Thời gian: 5s. |
| `dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj --filter "FullyQualifiedName~StockDocumentApiTests"` | `C:\Fontend` | .NET 8 / CustomWebApplicationFactory | **PASS** (2/2 tests) | Kiểm chứng HTTP GET list/detail transfers & counts, role `Owner,Staff` 200, `Cashier` 403 Forbidden, 404 ProblemDetails. |
| `node scratch/test_regression_v12.cjs` | `C:\Fontend` | Node.js v24.16 Native Test Runner | **PASS** (13/13 tests) | Kiểm chứng tự động 4 nhóm kịch bản Frontend: normalizeTransfer contract rejection, pagination filter reset & out-of-range recovery, in-flight pending snapshot lock & owner isolation, Drawer generation & identity race condition guards. |
| `npm --prefix src/frontend/admin-dashboard run build` | `C:\Fontend` | Node.js v24.16 / Vite 8.0.5 + tsc | **PASS** | `tsc -b && vite build` hoàn tất không có lỗi, 0 cảnh báo TypeScript. |
| `dotnet test ... --filter "FullyQualifiedName~OnlineOrderLifecycleIntegrationTests"` | `C:\Fontend` | Live PostgreSQL Docker daemon | **NOT RUN** (Cần Live Docker DB) | Yêu cầu máy chủ daemon PostgreSQL thực tế trên cổng 5432. Ghi nhận trung thực hiện trạng môi trường CI/local theo Honest Evidence. |

---

## 4. Danh sách file thay đổi trong đợt fix v1.2

1. `src/frontend/admin-dashboard/src/pages/OrdersPage.tsx`: Lưu snapshot với `isUnknownState: true` trước POST; khóa toàn bộ 5 trường biểu mẫu (`isFormLocked`); chặn silent discard / key regen khi unknown.
2. `src/frontend/admin-dashboard/src/components/orders/OrderDetailDrawer.tsx`: Bổ sung generation & identity guards trên toàn bộ các luồng (initial fetch, retry button, handleAction, handleCancel, error refresh).
3. `src/frontend/admin-dashboard/src/components/transfers/TransferDetailDrawer.tsx` & `StockCountDetailDrawer.tsx`: Đồng bộ generation & identity guards cho các thao tác dispatch/receive/reject và record/confirm/cancel.
4. `src/frontend/admin-dashboard/src/services/stockTransferService.ts`: Bổ sung kiểm tra bắt buộc `id`, `number`, `status` trong `normalizeTransfer`, từ chối payload `{unexpected:true}`.
5. `src/frontend/admin-dashboard/src/services/stockCountService.ts`: Bổ sung kiểm tra bắt buộc `id`, `number`, `status` trong `getStockCountById`.
6. `src/frontend/admin-dashboard/src/types/ordersCenter.ts`: Bổ sung kiểm tra bắt buộc `id`, `number` trong `normalizeSummary`.
7. `src/frontend/admin-dashboard/src/pages/StockCountsPage.tsx`: Reset `pageNumber = 1` khi đổi chi nhánh và bấm xóa bộ lọc; auto-recover khi `pageNumber > totalPages`.
8. `src/frontend/admin-dashboard/src/pages/InventoryTransfersPage.tsx`: Auto-recover khi `pageNumber > totalPages`.

---

- **Trạng thái:** **READY_FOR_REVIEW** (Đã đóng trọn vẹn toàn bộ các điểm còn mở của B01-F02, B01-F04 và PR26-F04 theo Review R2 của Codex).
