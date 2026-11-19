# Báo cáo sau nâng cấp — Báo cáo tồn kho theo kỳ (NXT), thẻ kho S12/S10 và xuất Excel (Upgrade 13 — Gói 2)

| Thông tin | Chi tiết |
| :--- | :--- |
| **Mã gói công việc** | `OISMS-66-L2-G2` |
| **Ngày thực hiện** | `2026-11-19` |
| **Tài liệu nguồn** | [`docs/Upgrade-recommended/Pedding/13-ton-kho-theo-ky.md`](../Upgrade-recommended/Pedding/13-ton-kho-theo-ky.md) (Mục 1, 2 [Q1–Q14], 3, 4, 6, 7; BE-13.1→13.3, FE-13.1, FE-13.2) |
| **Database Schema** | Không đổi schema (chỉ đọc trên `inventory_transactions`, `stock_transfers`, `inventory_balances`) |

---

## 1. Tóm tắt thay đổi nghiệp vụ và kiến trúc

1. **Báo cáo Nhập - Xuất - Tồn theo kỳ (AC-01, AC-02, AC-03, AC-04):**
   - Hỗ trợ xem theo khoảng ngày bất kỳ (`fromDate` đến `toDate`), quy đổi mốc thời gian kinh doanh múi giờ `Asia/Saigon` (UTC+07) với biên nửa mở `[startBoundaryUtc, endBoundaryUtc)`. Giới hạn tối đa 366 ngày, chặn yêu cầu ngày trong tương lai (`400 Bad Request`).
   - Hỗ trợ lọc theo Chi nhánh cụ thể hoặc Toàn hệ thống (`branchId = null`).
   - **Số dư đầu kỳ:** Lấy `balance_after` của dòng sổ cái cuối cùng trước mốc `startBoundaryUtc`. Nếu trước mốc chưa có giao dịch nào thì số dư đầu kỳ = 0.
   - **Số dư cuối kỳ:** Lấy `balance_after` của dòng sổ cái cuối cùng trước mốc `endBoundaryUtc`.
   - **Bóc tách nguồn gốc biến động Nhập - Xuất:**
     - **Nhập kho (`IN`):** Bóc tách thành Mua hàng (`Purchase`), Khách trả lại (`Return`), Chuyển kho đến (`TransferIn`), và Kiểm kê / Điều chỉnh thừa (`AdjustmentIn`).
     - **Xuất kho (`OUT`):** Bóc tách thành Bán POS, Bán Sàn TMĐT (Shopee, TikTok, Lazada), Bán Admin (phân định qua `OrderItem -> Order.Channel`), Chuyển kho đi (`TransferOut`), và Kiểm kê / Điều chỉnh thiếu (`AdjustmentOut`).
   - **Quy tắc xem Toàn hệ thống:** Tự động loại trừ các giao dịch chuyển kho nội bộ (`transfer_item_id != null`) khỏi cột Nhập và Xuất để không phóng đại quy mô doanh nghiệp; theo dõi riêng cột **Hàng đang chuyển (`InTransit`)** tại đầu kỳ và cuối kỳ. Công thức bảo toàn:
     $$\sum \text{Tồn các chi nhánh} + \text{Hàng đang chuyển} = \text{Tổng tồn hệ thống}$$
     $$\text{Đầu kỳ} + \text{Nhập} - \text{Xuất} = \text{Cuối kỳ}$$
   - **Hiển thị đầy đủ mọi SKU:** Luôn hiển thị đầy đủ SKU có trong danh mục (kể cả số dư và biến động bằng 0), hỗ trợ tìm kiếm theo mã/tên/barcode và lọc danh mục.
   - **Tối ưu hóa hiệu năng (< 2 giây/trang):** Phân trang danh mục SKU (`Skus`) trước (50 SKU/trang), sau đó mới truy vấn và tổng hợp sổ cái cho 50 SKU đó, tận dụng chỉ mục `ix_inventory_transactions_sku_history`.

2. **Phân quyền xem Giá trị & Bảo mật thông tin (AC-05):**
   - Giá trị tồn = $\sum (\text{Quantity} \times \text{UnitCost})$ có dấu.
   - **Vai trò `Owner`:** Xem đầy đủ số lượng và giá trị (đơn giá, thành tiền, tổng giá trị).
   - **Vai trò `Staff`:** Chỉ xem số lượng nhập, xuất, tồn. Toàn bộ trường giá trị (`openingValue`, `inValue`, `outValue`, `closingValue`, `totalOpeningValue`, `totalInValue`, `totalOutValue`, `totalClosingValue`) đều trả về `null`.
   - **Vai trò `Cashier`:** Tuyệt đối không được truy cập báo cáo kỳ và thẻ kho (`403 Forbidden`).

3. **Thẻ kho chi tiết S12 / S10 (AC-06):**
   - Thẻ kho chi tiết cho từng SKU theo Chi nhánh và Khoảng thời gian.
   - Cung cấp: Dòng Số dư đầu kỳ, từng dòng giao dịch sổ cái theo thứ tự thời gian (`SequenceNo`), và dòng Số dư cuối kỳ.
   - Trích xuất mã chứng từ gốc và `referenceId` để người dùng có thể bấm mở truy vết chuỗi chứng từ (`DocumentTraceDto`).
   - **Mẫu S12-DN (Nhân viên - Staff):** Chỉ hiển thị cột số lượng, các cột đơn giá và thành tiền trả `null`.
   - **Mẫu S10-DN (Chủ cửa hàng - Owner):** Hiển thị chi tiết số lượng, đơn giá và thành tiền. Khi kỳ báo cáo kết thúc ở thời điểm hiện tại, hiển thị dòng chênh lệch giá trị làm tròn WAC (`RoundDiff`) giữa tổng tích lũy sổ cái và giá trị sổ sách thực tế (`OnHand * CurrentUnitCost`).
   - Hỗ trợ in ấn chuyên dụng (`Print CSS`) chuẩn mẫu kế toán.

4. **Xuất file Excel `.xlsx` bằng ClosedXML (AC-07):**
   - Thiết kế giao diện xuất Excel dạng luồng (`IExcelExportService` trong `Application`, hiện thực `ClosedXmlExcelExportService` trong `Infrastructure`).
   - Áp dụng thư viện `ClosedXML` v0.104.2 (thư viện duy nhất được bổ sung).
   - Tự động ẩn các cột Tiền/Giá trị đối với tài khoản `Staff` để đảm bảo an toàn dữ liệu trên cả kênh tải file.

5. **Giao diện Frontend Admin Dashboard (AC-08):**
   - Bổ sung tab **Tồn theo kỳ** trên trang Quản lý kho (`InventoryPage.tsx`).
   - Bộ chọn thời gian tiện lợi: Hôm nay, Hôm qua, 7 ngày qua, 30 ngày qua, Tháng này, Tháng trước, Tùy chọn khoảng ngày.
   - Chọn Chi nhánh / Tất cả chi nhánh.
   - Bảng hiển thị có thể mở rộng (expand) chi tiết phân rã Nhập và Xuất.
   - Modal Thẻ kho tích hợp in ấn và xuất Excel.
   - Tự động ẩn cột tiền khi đăng nhập bằng tài khoản `Staff`.

---

## 2. Danh sách file thay đổi & bổ sung

### Backend
- `src/backend/src/OISM.Infrastructure/OISM.Infrastructure.csproj`: Thêm package `ClosedXML` (v0.104.2).
- `src/backend/src/OISM.Application/Common/Interfaces/IExcelExportService.cs`: Interface xuất Excel dạng luồng.
- `src/backend/src/OISM.Application/Features/Reports/Dtos/InventoryPeriodReportDtos.cs`: Tập DTOs cho Báo cáo kỳ, Thẻ kho, phân rã nguồn gốc và tóm tắt tổng.
- `src/backend/src/OISM.Application/Features/Reports/Services/IInventoryPeriodReportService.cs`: Interface nghiệp vụ báo cáo kỳ và thẻ kho.
- `src/backend/src/OISM.Application/Features/Reports/Services/InventoryPeriodReportService.cs`: Service xử lý truy vấn sổ cái, cân đối đầu-nhập-xuất-cuối, hàng đang chuyển, che giá Staff.
- `src/backend/src/OISM.Infrastructure/Services/ClosedXmlExcelExportService.cs`: Service xuất Excel dùng ClosedXML định dạng bảng, viền, tiêu đề, công thức.
- `src/backend/src/OISM.Application/DependencyInjection.cs`: Đăng ký `IInventoryPeriodReportService`.
- `src/backend/src/OISM.Infrastructure/DependencyInjection.cs`: Đăng ký `IExcelExportService`.
- `src/backend/src/OISM.WebApi/Controllers/ReportsController.cs`: Thêm 4 endpoints báo cáo kỳ, thẻ kho và xuất Excel.

### Frontend
- `src/frontend/admin-dashboard/src/services/inventoryPeriodService.ts`: Client API gọi báo cáo kỳ, thẻ kho và tải file Excel blob.
- `src/frontend/admin-dashboard/src/components/inventory/StockCardModal.tsx`: Modal Thẻ kho S12/S10 với CSS in ấn và tải Excel.
- `src/frontend/admin-dashboard/src/components/inventory/InventoryPeriodTab.tsx`: Component tab Tồn theo kỳ với bộ lọc ngày, chi nhánh, tìm SKU, mở rộng phân rã, tổng cộng và xuất Excel.
- `src/frontend/admin-dashboard/src/pages/InventoryPage.tsx`: Tích hợp tab button `"period"` và render `<InventoryPeriodTab />`.

### Tests & Documentation
- `test-oisms/backend/OISM.IntegrationTests/PostgresInventoryPeriodReportIntegrationTests.cs`: Bộ test tích hợp toàn diện trên PostgreSQL thật.
- `docs/02-functional-requirements/fr-11-reports-analytics.md`: Bổ sung yêu cầu `FR-REP-04`, `FR-REP-05`.
- `docs/02-functional-requirements/fr-03-inventory-ledger.md`: Bổ sung quy tắc đọc sổ cái, thẻ kho và báo cáo kỳ.
- `docs/05-api-contracts/03-inventory-endpoints.md`: Đặc tả hợp đồng API cho 4 endpoint mới.
- `docs/01-users-and-rbac/02-rbac-matrix.md`: Bổ sung dòng ma trận phân quyền cho Báo cáo kỳ & Thẻ kho.
- `docs/11-ui-screens/04-inventory-screens.md`: Đặc tả màn hình tab Tồn theo kỳ và Modal Thẻ kho.
- `docs/03-non-functional-requirements/01-performance-and-load.md`: Đặc tả SLA < 2 giây/trang và mô hình phân trang SKU trước.
