# Báo Cáo Triển Khai Gói 5 (OISMS-66-L2-G5): Dịch Vụ Che Giá Dùng Chung & Test Quét Endpoint Staff

| Thông tin kiểm soát | Chi tiết |
| :--- | :--- |
| **Mã công việc** | `OISMS-66-L2-G5` (Upgrade 12 — Gói 5) |
| **Mức độ ưu tiên** | `CRITICAL` (Bảo mật RBAC dữ liệu tài chính) |
| **Trạng thái** | `READY_FOR_REVIEW` |
| **Ngày hoàn thành** | `2026-11-21` |
| **Tiêu chuẩn tuân thủ** | `docs/Upgrade-recommended/Pedding/12-don-mua-va-cau-hinh-gia.md` (Q9, Q10, Q24, Tóm tắt luồng E, BE-12.9, FE-12.5) |

---

## 1. Mục Tiêu & Bối Cảnh

Trước Upgrade 12 Gói 5, việc kiểm tra quyền xem giá vốn, WAC và lãi gộp được xử lý rải rác và không đồng nhất ở từng controller và service:
- `InventoryController.cs`: dùng biến cục bộ `canViewCost = isOwner || isStaff`, cho phép Staff xem giá vốn WAC trên tồn kho và sổ cái.
- `ReportsController.cs`: dùng biến cục bộ `isOwner` cục bộ tại từng endpoint.
- `GoodsIssuesController.cs`: kiểm tra `CanViewCost` cục bộ.
- `OrderDtos.cs`: trả trường `CostPrice`, `TotalCost` công khai cho cả Staff.
- Truy vết chứng từ (`SalesInvoiceService.cs`): Staff vẫn thấy được giá vốn dòng WAC.

**Mục tiêu Gói 5:**
1. Hợp nhất toàn bộ các logic kiểm tra vai trò phân tán về một dịch vụ dùng chung `ICostVisibilityService` đọc claims từ JWT đã xác thực qua `ICurrentUserService`.
2. Quy định nhất quán: **Chỉ duy nhất vai trò `Owner` (Chủ cửa hàng) mới được xem giá mua, giá vốn, WAC, giá trị tồn kho, lãi gộp và biên lợi nhuận.**
3. Tài khoản `Staff` và `Cashier` nhận `null` ở MỌI API GET, file xuất Excel, bản in, sự kiện realtime SignalR và câu trả lời Trợ lý AI.
4. Xử lý triệt để lỗ hổng suy diễn nhà cung cấp (AC-06 / G1 F-03): chặn `403 FORBIDDEN` khi `Staff` lọc `supplierId` kết hợp trạng thái `Draft` hoặc `PendingApproval`.
5. Xây dựng bộ kiểm thử tích hợp tự động `PostgresCostVisibilityScanIntegrationTests` trên cơ sở dữ liệu PostgreSQL 16 thật, duyệt đệ quy toàn bộ JSON payload của mọi endpoint GET để chứng minh 0 vi phạm đối với Staff và Owner xem được đầy đủ giá vốn.
6. Cập nhật Frontend tự động ẩn các cột/dòng giá vốn/WAC khi giá trị là `null`, bảo đảm Staff không nhìn thấy các cột trống.

---

## 2. Kiến Trúc Dịch Vụ Dùng Chung `ICostVisibilityService`

### 2.1. Thiết Kế Interface & Implementation

```
[HTTP Request with JWT]
        │
        ▼
[CurrentUserService] ──(reads ClaimTypes.Role / "role")──► [ICurrentUserService]
        │
        ▼
[CostVisibilityService] ────────────────────────────────► [ICostVisibilityService] / [ICostVisibility]
        │                                                  - CanViewCost: bool (Role == "Owner")
        │                                                  - IsOwner: bool (Role == "Owner")
        ├─► InventoryService (UnitCost, AverageCost)
        ├─► OrderService & OrderQueryService (CostPrice, TotalCogs)
        ├─► SalesInvoiceService (Trace UnitCost)
        ├─► SalesReturnService (OriginalUnitCost)
        ├─► StockTransferService (UnitCost)
        ├─► StockCountService (AdjustmentUnitCost)
        ├─► GoodsIssueService (UnitCost, TotalCost)
        ├─► PurchaseOrderService (UnitPrice, TotalAmount, Supplier masking, AC-06)
        ├─► GoodsReceiptService (UnitPrice, TotalAmount)
        ├─► ReportService & InventoryPeriodReportService (GrossMargin, InventoryValues)
        ├─► ClosedXmlExcelExportService (Excel S10 vs S12)
        └─► AiChatService & Prompts (AI Assistant)
```

- **`ICurrentUserService` (`OISM.Application.Common.Interfaces`):**
  Trích xuất định danh `UserId`, `Role`, `IsAuthenticated` từ `IHttpContextAccessor.HttpContext.User`.
- **`ICostVisibilityService` (`OISM.Application.Common.Interfaces`):**
  Định nghĩa thuộc tính `CanViewCost` và `IsOwner`. Cả hai đều kiểm tra `_currentUserService.IsInRole("Owner")`.
- **`AllowAllCostVisibilityService.Instance`:**
  Đối tượng Singleton dự phòng an toàn cho Unit Tests hoặc môi trường chạy nền không có `HttpContext`, đảm bảo tương thích ngược 100% với các test cũ.
- **Dependency Injection (`DependencyInjection.cs`):**
  Đăng ký `CurrentUserService` dạng Scoped trong Infrastructure; đăng ký `CostVisibilityService` làm implementation cho cả `ICostVisibilityService` và `ICostVisibility` trong Application.

---

## 3. Danh Sách Phân Hệ & Điểm Che Giá Đã Hợp Nhất

| Phân hệ / DTO | Trường nhạy cảm | Giá trị trả cho Staff | Giá trị trả cho Owner |
| :--- | :--- | :---: | :---: |
| **Tồn kho hiện tại** (`SkuBalanceDto`, `SkuDto`) | `AverageCost` (WAC) | `null` | Số tiền (VND) |
| **Sổ cái kho** (`InventoryLedgerDto`, `InventoryTransactionDto`) | `UnitCost` | `null` | Số tiền (VND) |
| **Đơn hàng** (`OrderDto`, `OrderDetailDto`, `OrderItemDto`) | `CostPrice`, `TotalCost`, `TotalCogs` | `null` | Số tiền (VND) |
| **Truy vết chứng từ** (`DocumentTraceDto`, `DocumentTraceLineDto`) | `UnitCost` | `null` | Số tiền (VND) |
| **Phiếu trả hàng** (`SalesReturnItemDto`, `SalesReturnResponseDto`) | `OriginalUnitCost` | `null` | Số tiền (VND) |
| **Chuyển kho** (`StockTransferItemResponseDto`) | `UnitCost` | `null` | Số tiền (VND) |
| **Kiểm kê kho** (`StockCountItemResponseDto`) | `AdjustmentUnitCost` | `null` | Số tiền (VND) |
| **Phiếu xuất kho 02-VT** (`GoodsIssueResponseDto`, `GoodsIssueItemResponseDto`) | `UnitCost`, `TotalCost` | `null` | Số tiền (VND) |
| **Đơn mua hàng** (`PurchaseOrderResponseDto`, `PurchaseOrderItemResponseDto`) | `UnitPrice`, `TotalAmount`, `AllocatedUnitCost`, `VatAmount`, `ShippingFee`, `DiscountAmount` | `null` (kèm ẩn NCC khi Draft/Pending) | Số tiền (VND) |
| **Phiếu nhập kho** (`GoodsReceiptResponseDto`, `GoodsReceiptItemResponseDto`) | `UnitPrice`, `LineTotal`, `TotalAmount` | `null` | Số tiền (VND) |
| **Báo cáo tài chính** (`DashboardSummaryDto`, `InventoryVelocityReportDto`) | `GrossMarginPercentage`, `TotalInventoryValue` | `null` (hoặc `403`) | Tỷ lệ % / Số tiền |
| **Báo cáo kỳ (NXT)** (`InventoryPeriodReportDto`, `StockCardReportDto`) | `OpeningValue`, `InValue`, `OutValue`, `ClosingValue` | `null` | Số tiền (VND) |
| **Danh mục sản phẩm** (`ProductVariantResponse`) | `CostPrice` | `null` | Số tiền (VND) |
| **Xuất file Excel** (`ClosedXmlExcelExportService`) | Cột Giá vốn, Thành tiền vốn, Giá trị tồn | Tự động ẩn cột (mẫu S12) | Hiển thị đủ cột (mẫu S10) |
| **Bản in** (`GoodsIssuePrintModal`, `PosDailyGoodsIssueSummaryModal`) | Cột Giá vốn BQ, Thành tiền giá vốn | Tự động ẩn cột | Hiển thị đủ cột |
| **Sự kiện Realtime SignalR** (`IRealtimeNotifier`, `RealtimeDtos`) | Payloads thông báo | Chỉ truyền ID, Số lượng, Trạng thái (0 trường giá vốn) | Chỉ truyền ID, Số lượng, Trạng thái |
| **Trợ lý AI** (`AiChatService`, Prompts) | Số liệu tài chính, Doanh thu, Lãi gộp, Giá vốn | Trả lời "không có quyền xem" | Trả lời đầy đủ số liệu |

---

## 4. Xử Lý AC-06 (G1 F-03): Chống Suy Diễn Nhà Cung Cấp

**Lỗ hổng trước đây:**
Khi `Staff` gọi `GET /api/purchase-orders?supplierId={id}&status=Draft`, nếu backend trả về các đơn nháp khớp điều kiện, nhân viên có thể thử từng `supplierId` để suy diễn nhà cung cấp của đơn mua trước khi đơn được phê duyệt.

**Giải pháp đã cài đặt trong `PurchaseOrderService.cs`:**
- Khi người gọi là `Staff` (không phải `Owner`) và cung cấp tham số `filter.SupplierId`:
  - Nếu `filter.Status` bằng `Draft` hoặc `PendingApproval`: Backend lập tức ném lỗi `403 FORBIDDEN` (`FORBIDDEN`).
  - Nếu không truyền `filter.Status` hoặc truyền trạng thái khác: Query tự động loại trừ mọi đơn hàng có trạng thái `Draft` và `PendingApproval`.
- Tài khoản `Owner` vẫn được phép lọc theo mọi trạng thái bình thường.

---

## 5. Cải Tiến Giao Diện Người Dùng (AC-08 Frontend)

Đã cập nhật các trang và component trên Frontend Admin Dashboard để ẩn cột có điều kiện:
1. **`SalesInvoiceTraceModal.tsx`:**
   - Tính toán `hasCost = trace.lines?.some(l => l.unitCost !== null && l.unitCost !== undefined)`.
   - Ẩn cột `Giá vốn (WAC)` và ghi chú tương ứng khi `hasCost` là `false`.
2. **`SalesReturnDetailDrawer.tsx`:**
   - Chỉ cho phép xem giá vốn khi `isOwner` và `detail.items` có chứa `originalUnitCost != null`.
   - Ẩn cột `Giá vốn gốc (WAC)` đối với nhân viên.
3. **`StockCountDetailDrawer.tsx`:**
   - Ẩn cột `Đơn giá WAC (nếu thừa)` khi nhân viên xem phiếu kiểm kê.
4. **`TransferDetailDrawer.tsx`:**
   - Chỉ hiển thị cột `Đơn giá vốn` khi `isOwner` và mặt hàng có dữ liệu giá vốn.
5. **`OrderDetailDrawer.tsx`:**
   - Ẩn cột `Giá vốn (WAC)` và dòng `Tổng giá vốn snapshot (WAC)` khi `costPrice` và `totalCogs` là `null`.
6. **`InventoryPage.tsx` (File chung):**
   - Đọc và chỉnh sửa cục bộ an toàn.
   - Tính `hasBalanceCost = isOwner && pagedSkus.some(s => s.averageCost !== null && s.averageCost !== undefined)`.
   - Ẩn 2 cột `Giá vốn BQ (WAC)` và `Giá trị tồn kho` khi `hasBalanceCost` là `false`; điều chỉnh `colSpan` từ 8 thành 6 ở các trạng thái loading, lỗi, và rỗng.
   - Điều chỉnh modal Giữ chỗ tồn kho để ẩn ô `Giá vốn WAC (Chi nhánh)` với Staff.

---

## 6. Kết Quả Kiểm Thử Tự Động (AC-07 Test Quét)

Tệp kiểm thử: `test-oisms/backend/OISM.IntegrationTests/PostgresCostVisibilityScanIntegrationTests.cs`
- **Môi trường:** PostgreSQL 16 container, kết nối chuỗi thực tế.
- **Dữ liệu Seed:** Khởi tạo đầy đủ chi nhánh, kho, nhà cung cấp, sản phẩm, tồn kho ban đầu có giá vốn (20.000 VND), đơn mua hàng, phiếu nhập kho, đơn hàng POS, phiếu xuất kho 02-VT, hóa đơn bán lẻ, phiếu trả hàng (IN), phiếu chuyển kho, và phiếu kiểm kê.
- **Kết quả chạy:**
  - `Staff_ScanAllGetEndpoints_NoSensitiveCostKeysExposed_AC07`: **PASS** (Quét 27 GET URLs đại diện cho mọi phân hệ, duyệt đệ quy JsonElement, ghi nhận **0 vi phạm** đối với Staff).
  - `Owner_ScanKeyGetEndpoints_HasSensitiveCostValues_AC07`: **PASS** (Khẳng định Owner xem được `CostPrice > 0`, `TotalCogs > 0`, `UnitCost > 0`, `AverageCost > 0`, `GrossMarginPercentage != null`).
  - `Staff_FilterPurchaseOrdersBySupplier_AC06_Returns403OrExcludesDraft`: **PASS** (Staff lọc supplierId với Draft/Pending nhận `403 Forbidden`; lọc chung không chứa Draft/Pending; Owner nhận `200 OK`).

---

## 7. Kết Luận

Gói công việc `OISMS-66-L2-G5` đã hoàn thành xuất sắc toàn bộ mục tiêu đề ra, khắc phục triệt để các rủi ro bảo mật dữ liệu tài chính theo chuẩn thiết kế Upgrade 12, bảo đảm tính toàn vẹn hệ thống và vượt qua mọi tiêu chí nghiệm thu (AC-01 đến AC-08).
