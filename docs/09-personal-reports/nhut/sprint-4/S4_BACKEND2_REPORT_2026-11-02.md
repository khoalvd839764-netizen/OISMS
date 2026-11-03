# BÁO CÁO CÔNG VIỆC & NGHIỆM THU SPRINT 4 (BACKEND 2)

**Dự án:** OISMS - Omnichannel Inventory & Sales Management System  
**Học phần:** Đồ án Chuyên ngành / Công nghệ Phần mềm — Trường ĐH Giao thông Vận tải TP.HCM (UTH)  
**Thành viên thực hiện:** Phùng Minh Nhựt (Backend 2 Lead / Backend phụ)  
**Trưởng nhóm phối hợp & Review:** Lê Võ Đăng Khoa (Backend 1 Lead)  
**Nhóm Frontend phối hợp:** Phúc Hữu (Admin Dashboard), Phương Lê (POS PWA)  
**Giai đoạn Sprint 4:** 23/10/2026 – 27/10/2026 (Hoàn tất toàn bộ Đợt 1 & Đợt 2: 02/11/2026)  
**Chủ đề Sprint:** Lõi Quản trị Coming Soon, Dự báo Nhu cầu AI & Chuẩn hóa API Hỗ trợ Nâng cấp  
**Trạng thái nghiệm thu:** **HOÀN THÀNH 100% TOÀN BỘ NHIỆM VỤ ĐƯỢC GIAO TRÊN JIRA**  
**Kết quả kiểm thử tự động:** **182 / 182 Unit Tests PASS (100%)** — 0 Errors, 0 Warnings  

---

## 1. TỔNG HỢP CÁC TÍNH NĂNG ĐÃ HIỆN THỰC & TIÊU CHUẨN NGHIỆM THU

### A. Đợt 1: Các API Độc Lập Coming Soon (CS-02, CS-04, CS-07, CS-03, CS-08)

| Mã Task | Yêu cầu chức năng (FR) | Thành phần hiện thực (Mã nguồn & API) | Tiêu chuẩn nghiệm thu & Kết quả kiểm chứng |
|---|:---:|---|---|
| **CS-02** | **Quản lý Nhân sự & Phân quyền RBAC** (`FR-AUTH`) | • `UsersController.cs` (`/api/users`)<br/>• `IUserService.cs` & `UserService.cs`<br/>• `UserDtos.cs`<br/>• `UserServiceTests.cs` (7/7 tests pass) | **ĐẠT (PASS 100%):**<br/>• Phân quyền 3 Roles chuẩn: `Owner`, `Staff`, `Cashier`.<br/>• Bảo mật tài khoản: Mã hóa mật khẩu BCrypt, cô lập Multi-tenant Guard tuyệt đối.<br/>• Ràng buộc nghiệp vụ an toàn cao: Chặn tự hạ quyền (`CANNOT_DEMOTE_SELF`), chặn tự khóa tài khoản (`CANNOT_DEACTIVATE_SELF`).<br/>• Giao diện Admin: Trang Nhân sự kết nối API thật, hỗ trợ tạo nhân viên, phân công chi nhánh và đổi trạng thái hoạt động. |
| **CS-04** | **Tìm kiếm Toàn hệ thống Đa thực thể** (`FR-SEARCH`) | • `SearchController.cs` (`GET /api/search?q=...`)<br/>• `ISearchService.cs` & `SearchService.cs`<br/>• `SearchDtos.cs`<br/>• `GlobalSearchTests.cs` (3/3 tests pass) | **ĐẠT (PASS 100%):**<br/>• Quét song song 3 thực thể: Sản phẩm SKU, Đơn hàng Orders, Phiếu nhập kho Goods Receipts.<br/>• RBAC Guard nghiêm ngặt: Vai trò Thu ngân (`Cashier`) tự động bị chặn xem phiếu kho để bảo vệ bí mật giá vốn và nhà cung cấp.<br/>• Giao diện Admin: Tích hợp thanh tìm kiếm trên Header AdminShell với debounce 250ms, popup phân nhóm rõ ràng, click chuyển trang tức thì. |
| **CS-03** | **Lưu Cấu hình Ngưỡng an toàn ReorderPoint** (`FR-INV`) | • `InventoryController.cs` (`PUT /api/inventory/reorder-point`, `PUT .../bulk`)<br/>• `InventoryService.cs`<br/>• `InventoryBalanceDtos.cs`<br/>• `ReorderPointTests.cs` (4/4 tests pass) | **ĐẠT (PASS 100%):**<br/>• Lưu ngưỡng tồn kho an toàn theo SKU tại từng Chi nhánh cụ thể (Branch-scoped).<br/>• Kiểm tra validation chặt chẽ: Ngưỡng $ReorderPoint \ge 0$, chuẩn hóa đơn vị tồn gốc BaseUnit.<br/>• Đồng bộ realtime qua SignalR Hub khi ngưỡng được cập nhật.<br/>• Giao diện Admin: Modal cấu hình nhanh trên trang Kho hàng, click trực tiếp vào nút ngưỡng trên bảng để đổi giá trị. |
| **CS-07** | **Báo cáo KPI Tổng quan Dashboard** (`FR-REP-01`) | • `ReportsController.cs` (`GET /api/reports/dashboard-summary`)<br/>• `IReportService.cs` & `ReportService.cs`<br/>• `ReportDtos.cs`<br/>• `ReportServiceTests.cs` | **ĐẠT (PASS 100%):**<br/>• Chuẩn hóa dữ liệu Dashboard: Doanh thu thuần (`Net Revenue`), Tổng số đơn, Lãi gộp (`Gross Profit`), Tỷ lệ biên lãi (`Profit Margin %`) và danh sách 5 đơn hàng gần nhất.<br/>• Bảo mật vai trò: Tự động ẩn số liệu Lãi gộp và Tỷ suất lãi nếu tài khoản đăng nhập là `Staff`. |
| **CS-08** | **AI Gợi ý Nhập hàng & Lịch sử Xuất kho** (`FR-AI-01`) | • `AiController.cs` (`GET /api/ai/reorder-recommendations`, `GET /api/ai/export-history`)<br/>• `InventoryController.cs` (`GET /api/inventory/export-history`)<br/>• `IAiForecastingService.cs` & `AiForecastingService.cs`<br/>• `AiDtos.cs`<br/>• `AiForecastingTests.cs` (4/4 tests pass) | **ĐẠT (PASS 100%):**<br/>• Trích xuất chuỗi thời gian lịch sử xuất kho (Sổ cái kho OUT kết hợp OrderItems) theo đơn vị tính gốc (`BaseUnit`).<br/>• Tính toán vận tốc bán trung bình ngày ($DailySalesVelocity$), dự báo số ngày cạn tồn kho ($DaysUntilStockout$).<br/>• Thuật toán gợi ý nhập hàng thông minh theo công thức: $SuggestedQuantity = \max(1, \lceil DailyVelocity \times (LeadTime + SafetyStock) + \max(ReorderPoint, 5) - Available \rceil)$.<br/>• Độ tin cậy dự báo (`ConfidenceLevel`: High, Medium, Low) và lời giải thích trực quan (`Reason`). |

---

### B. Đợt 2: Các API CRUD / Query Hỗ Trợ Module Nâng Cấp (Upgrade Modules)

| Phân hệ nâng cấp | Endpoints HTTP | Thành phần hiện thực (Mã nguồn) | Đặc tả chức năng |
|---|---|---|---|
| **1. Đa đơn vị tính** (`units`, `sku_units`) | • `GET /api/units`<br/>• `POST /api/units`<br/>• `GET /api/units/skus/{skuId}`<br/>• `POST /api/units/skus/{skuId}` | • `UnitsController.cs`<br/>• `IUnitService.cs` & `UnitService.cs`<br/>• `UnitDtos.cs` | Quản lý danh mục đơn vị tính (Cái, Hộp, Thùng, Lốc) và thiết lập tỷ lệ quy đổi (`ConversionRate`), mã vạch, giá bán theo đơn vị cho từng SKU. |
| **2. Bảng giá** (`price_lists`, `price_versions`) | • `GET /api/price-lists`<br/>• `POST /api/price-lists`<br/>• `GET /api/price-lists/{id}/versions`<br/>• `POST /api/price-lists/{id}/versions` | • `PriceListsController.cs`<br/>• `IPriceListService.cs` & `PriceListService.cs`<br/>• `PriceListDtos.cs` | Quản lý bảng giá theo kênh bán (Bán lẻ Retail, Bán buôn Wholesale, Sàn TMĐT) và phiên bản giá theo khoảng thời gian hiệu lực (`EffectiveFrom`, `EffectiveTo`). |
| **3. Mua hàng** (`purchase_orders`) | • `GET /api/purchase-orders`<br/>• `GET /api/purchase-orders/{id}`<br/>• `POST /api/purchase-orders`<br/>• `PATCH /api/purchase-orders/{id}/status` | • `PurchaseOrdersController.cs`<br/>• `IPurchaseOrderService.cs` & `PurchaseOrderService.cs`<br/>• `PurchaseOrderDtos.cs` | Quản lý đơn đặt hàng nhà cung cấp (PO), tự động sinh mã `PO-yyyyMMdd-XXXXX`, tính tổng tiền, theo dõi tiến độ giao hàng và chuyển trạng thái (`Draft` $\rightarrow$ `Ordered` $\rightarrow$ `Completed` / `Cancelled`). |
| **4. Lịch kiểm kê** (`stock_count_schedules`) | • `GET /api/stock-count-schedules`<br/>• `POST /api/stock-count-schedules` | • `StockCountSchedulesController.cs`<br/>• `IStockCountScheduleService.cs` & `StockCountScheduleService.cs`<br/>• `StockCountScheduleDtos.cs` | Thiết lập lịch kiểm kê định kỳ (Hàng ngày Daily, Hàng tuần Weekly, Hàng tháng Monthly) theo từng Chi nhánh và tính toán thời điểm kiểm kê kế tiếp (`NextRunAt`). |

---

## 2. KẾT QUẢ KIỂM THỬ TỰ ĐỘNG (AUTOMATED TEST SUITE)

```text
Test run for /Users/bo/OISMS/src/backend/tests/OISM.UnitTests/bin/Debug/net8.0/OISM.UnitTests.dll (.NETCoreApp,Version=v8.0)
VSTest version 17.11.1 (arm64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   182, Skipped:     0, Total:   182, Duration: 2 s - OISM.UnitTests.dll (net8.0)
```

- **Tỷ lệ Pass:** 182 / 182 tests (100%).
- **Thời gian chạy:** ~2 giây trên môi trường macOS Apple Silicon.
- **Phạm vi kiểm thử mới bổ sung:**
  - `AiForecastingTests` (4 tests): Kiểm tra trích xuất lịch sử xuất kho, thuật toán gợi ý đặt hàng an toàn/khẩn cấp, cô lập Multi-tenant Guard.
  - `UpgradeModulesTests` (5 tests): Kiểm tra CRUD Units & quy đổi tỷ lệ SKU, Bảng giá & phiên bản giá, Đơn đặt mua PO tự sinh mã & đổi trạng thái, Lịch kiểm kê định kỳ.

---

## 3. LIÊN KẾT MINH CHỨNG & HÀNH TRÌNH BÀN GIAO

- **Repository:** `https://github.com/khoalvd839764-netizen/OISMS`
- **Branch:** `feature/sprint-4-backend-2`
- **Target Branch (PR):** `develop`
- **Jira Issue:** `[Backend 02 - Nhựt] Phát triển các API chức năng Coming Soon (CS-01 → CS-08) & API Query hỗ trợ Upgrade`
- **Tình trạng:** Sẵn sàng Review & Merge vào `develop`.
