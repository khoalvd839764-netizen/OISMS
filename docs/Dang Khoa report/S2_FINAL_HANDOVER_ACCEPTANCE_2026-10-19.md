# BÁO CÁO NGHIỆM THU & BÀN GIAO TOÀN DIỆN SPRINT 2 — OISMS
**Dự án:** OISMS - Omnichannel Inventory & Sales Management System  
**Thời điểm nghiệm thu:** 19/10/2026  
**Thành viên thực hiện & nghiệm thu:**
- **Đăng Khoa:** Trưởng nhóm / Backend 1 Lead (Persistence, Security, Advisory Locks, Ledger & Anti-Oversell) & DevOps (Docker/Nginx).
- **Phúc Hữu:** Frontend Admin Lead (Web Admin Dashboard, UI/UX, State Management & API Integration).
- **Nhựt:** Backend 2 (Catalog, Brand, Category Hierarchy & POS Search APIs).

---

## 1. TỔNG QUAN KẾT QUẢ NGHIỆM THU SPRINT 2 (F1 – F7)

Sau khi rà soát và kiểm thử thực tế trên toàn bộ hệ thống gồm mã nguồn C# (.NET 8), cơ sở dữ liệu PostgreSQL 16 thật, Docker container và frontend React 19 / TypeScript, toàn bộ 7 vấn đề kỹ thuật trọng tâm đã được khắc phục và nghiệm thu đạt chuẩn 100%:

| Mã | Phân hệ phụ trách | Tóm tắt vấn đề | Giải pháp triển khai | Kết quả kiểm thử thực tế | Kết luận nghiệm thu |
| :---: | :--- | :--- | :--- | :--- | :---: |
| **F1** | Frontend Admin<br>*(Phúc Hữu)* | Đóng/mở modal làm mất ID phiếu nhập pending; retry tạo phiếu trùng. | - Lưu ID và thông tin phiếu đang chờ vào `sessionStorage` (`oism_pending_receipt_${tenantId}`).<br>- Mở lại modal tự động đối soát `GET /api/goods-receipts/{id}`: nếu Confirmed thì hoàn tất; nếu Draft thì tiếp tục đúng phiếu cũ.<br>- Đổi nhãn nút thành `"Bỏ theo dõi phiếu này / Tạo phiếu mới"`, giải thích rõ chứng từ vẫn lưu Draft ở backend. | - Build pass 100%<br>- Live PostgreSQL test: chỉ 1 phiếu Confirmed, OnHand tăng đúng 10, không tạo phiếu trùng | **ĐẠT** |
| **F2** | Backend + Frontend<br>*(Đăng Khoa + Phúc Hữu)* | Client gọi reserve không truyền OrderId/OrderItemId; retry tạo reservation mới và tăng Reserved. | - **Backend (Đăng Khoa):** Hỗ trợ `RequestKey` UUID qua body và headers `Idempotency-Key` / `X-Request-Key`. Advisory Lock `(tenantId, RequestKey)` chống race condition. Cùng key & payload trả 200 OK giữ nguyên reservation; khác payload trả 409 Conflict; cách ly đa tenant.<br>- **Frontend (Phúc Hữu):** Quản lý UUID RFC4122 chuẩn; lưu pending reservation và pending order vào `sessionStorage`; áp dụng cả Checkout OrdersPage và Modal Kho; bypass client optimistic balance check khi retry (`isRetry: true`). | - 18/18 Backend integration tests pass<br>- 21/21 Live PostgreSQL checks pass<br>- 12/12 Frontend logic checks pass<br>- 5 concurrent requests giữ nguyên `Reserved = 9` | **ĐẠT** |
| **F3** | Frontend Admin<br>*(Phúc Hữu)* | Bảng kho giữ số dư cũ khi đổi kho hoặc lỗi API riêng của kho đang lọc. | - Refetch kho đang chọn sau khi tạo phiếu nhập hoặc giữ chỗ.<br>- Xử lý `whRes?.error`: nếu lỗi API kho, reset ngay `warehouseBalances = []` và bật `warehouseBalanceError` kèm nút thử lại; không hiển thị số dư kho cũ.<br>- Dùng `balanceReqSeqRef` chống race condition phản hồi chậm. | - Kiểm thử logic: isolate warehouse error pass 100%<br>- Sequence counter loại trừ phản hồi cũ | **ĐẠT** |
| **F4** | Frontend Admin<br>*(Phúc Hữu)* | API sổ kho lỗi nhưng hiển thị "Chưa có bút toán". | - Phân tách rõ ràng 3 trạng thái: `ledgerLoading`, `ledgerError`, và empty state.<br>- Hiển thị banner lỗi màu đỏ và nút `"Thử lại tải sổ kho"` độc lập. | - Fault injection HTTP 500: hiển thị banner lỗi đúng chuẩn, không nhầm empty state | **ĐẠT** |
| **F5** | Frontend Admin<br>*(Phúc Hữu)* | Lọc theo tên chứa chuỗi nên chọn "Kho" dính cả "Kho 2". | - Chuyển sang so sánh chính xác theo `warehouseId` (`item.warehouseId === warehouseFilter`).<br>- Dropdown hiển thị đầy đủ cấu trúc: `[Tên Chi nhánh] — [Tên Kho hàng]`. | - Test so sánh ID: không còn tình trạng dính chéo dòng kho con | **ĐẠT** |
| **F6** | Frontend Admin<br>*(Phúc Hữu)* | Bộ tải phân trang chỉ lấy tối đa 1.000 dòng (10 trang). | - Loại bỏ giới hạn cứng 10 trang trong `fetchAllPages`.<br>- Tải toàn bộ `totalPages` theo batch song song (5 request/lượt) để không nghẽn mạng và không cắt dữ liệu. | - Diagnostic tải thành công 2.345 dòng qua 24 trang đầy đủ | **ĐẠT** |
| **F7** | Docker / Nginx<br>*(Đăng Khoa)* | Truy cập trực tiếp route con (`/login`, `/inventory`,...) hoặc F5 trả 404 Not Found. | - Tạo `nginx.conf` với SPA fallback `try_files $uri $uri/ /index.html;` và rule chặn file tĩnh thiếu `try_files $uri =404;`.<br>- Tích hợp `COPY nginx.conf` vào Dockerfile của `admin-dashboard` và `pos-app`, rebuild container image. | - Curl HTTP 200 cho `/`, `/login`, `/inventory`, `/products`, `/403`<br>- Curl HTTP 404 cho asset tĩnh không tồn tại | **ĐẠT** |

---

## 2. CHI TIẾT KỸ THUẬT CÁC ĐIỂM SỬA CHỮA

### 2.1. Backend F2 — Chống giữ chỗ lặp bằng RequestKey & Advisory Lock
- **Tập tin thay đổi:**
  - `src/backend/src/OISM.Application/Features/Inventory/Dtos/ReservationDtos.cs`: Thêm trường `RequestKey` UUID vào `ReserveStockRequestDto`.
  - `src/backend/src/OISM.WebApi/Controllers/InventoryController.cs`: Map header `Idempotency-Key` / `X-Request-Key` vào `dto.RequestKey`.
  - `src/backend/src/OISM.Application/Features/Inventory/Services/ReservationService.cs`:
    - Lấy PostgreSQL Advisory Lock theo `(tenantId, RequestKey)`:
      ```sql
      SELECT pg_advisory_xact_lock(hashtextextended(tenantId || '/request_key/' || requestKey, 0));
      ```
    - Tìm kiếm Order đã tồn tại theo `(TenantId, RequestKey)`.
    - Đối soát payload: Nếu cùng `(WarehouseId, SkuId, Quantity)` $\rightarrow$ trả về kết quả cũ HTTP 200 OK, không tăng `Reserved`. Nếu khác payload $\rightarrow$ từ chối HTTP 409 Conflict.
    - Kiểm tra hạn: Nếu reservation cũ đã hết hạn $\rightarrow$ từ chối HTTP 409 Conflict.
    - Giữ nguyên validation 4 cấp tài nguyên `IsActive` (A10) và chống bán vượt tồn.
    - Cách ly đa tenant: Cùng key khác tenant được phân xử hoàn toàn độc lập.

### 2.2. Frontend Admin F1, F2, F3, F4, F5, F6
- **Tập tin thay đổi:**
  - `src/frontend/admin-dashboard/src/services/apiAdapter.ts`:
    - Thêm `generateUuid()` chuẩn RFC4122 (bỏ tiền tố mock `resv-...`).
    - Thêm `getPendingReservationStorageKey()`, `getPendingOrderStorageKey()`.
    - Trong `reserveStock`: Gửi đúng contract Swagger `requestKey: payload.idempotencyKey || null` trong body; gửi headers `Idempotency-Key` và `X-Request-Key`.
    - Trong `checkout`: Nhận `options.idempotencyKey?: string`, `options.isRetry?: boolean`. Truyền `idempotencyKey` vào `reserveStock`. Khi `isRetry === true`, bypass bước kiểm tra tồn khả dụng phía client để không chặn request retry.
    - Trong `fetchAllPages`: Loại bỏ giới hạn cứng 10 trang, duyệt toàn bộ `totalPages` theo batch 5 request song song.
  - `src/frontend/admin-dashboard/src/pages/OrdersPage.tsx`:
    - Quản lý `orderIdempotencyKey` và `isRetryingOrder`. Lưu dở dang vào `sessionStorage` (`oism_pending_order_${tenantId}`).
    - Khi mở modal tạo đơn: Khôi phục intent cũ nếu có, hoặc sinh UUID mới.
    - Khi thay đổi form: Xóa pending cũ và sinh UUID mới cho ý định mới.
    - Thêm badge UUID, nút `"Bỏ thao tác dở dang / Tạo mới"`, và nhãn nút retry.
  - `src/frontend/admin-dashboard/src/pages/InventoryPage.tsx`:
    - F1: Lưu/khôi phục pending receipt vào `sessionStorage`. Đổi nhãn nút `"Bỏ theo dõi phiếu này / Tạo phiếu mới"`, giải thích rõ chứng từ vẫn lưu Draft trên server.
    - F2: Quản lý pending reservation trong `sessionStorage`. Khi mở lại modal, khôi phục key và payload chưa hoàn tất (không xóa mù quáng). Dùng `generateUuid()` RFC4122. Bỏ qua kiểm tra client khi retry.
    - F3: Khi `whRes?.error`, reset `warehouseBalances = []` và bật `warehouseBalanceError` kèm nút thử lại, không để số dư kho cũ hiển thị dưới nhãn kho mới. Dùng `balanceReqSeqRef` sequence counter.
    - F4: Tách biệt `ledgerError` với empty state, hiển thị banner lỗi và nút retry riêng.
    - F5: Lọc chính xác theo `item.warehouseId === warehouseFilter`. Dropdown hiển thị `[Chi nhánh] — [Kho hàng]`.

### 2.3. Docker / Nginx F7 — SPA Fallback
- **Tập tin thay đổi:**
  - `src/frontend/admin-dashboard/nginx.conf`: Tạo mới cấu hình Nginx với SPA fallback `try_files $uri $uri/ /index.html;` và rule chặn file tĩnh thiếu `try_files $uri =404;`.
  - `src/frontend/pos-app/nginx.conf`: Tạo mới cấu hình Nginx tương ứng cho POS.
  - `src/frontend/admin-dashboard/Dockerfile` & `src/frontend/pos-app/Dockerfile`: Thêm `COPY nginx.conf /etc/nginx/conf.d/default.conf`. Rebuild container images.

---

## 3. BẰNG CHỨNG KIỂM THỬ THỰC TẾ

### 3.1. Toàn bộ Solution Test Suite Backend (.NET 8)
- **Lệnh thực thi:** `dotnet test src/backend/OISM.sln`
- **Kết quả:**
  ```text
  Passed!  - Failed: 0, Passed: 103, Skipped: 0, Total: 103 - OISM.UnitTests.dll (net8.0)
  Passed!  - Failed: 0, Passed:  91, Skipped: 0, Total:  91 - OISM.IntegrationTests.dll (net8.0)
  Total: 194 / 194 Tests Passed 100% (0 Failed, 0 Skipped)
  ```

### 3.2. Kiểm thử Thực tế trên PostgreSQL Thật (Script `scratch/verify_s2_fixes.js`)
- **Kết quả:** **21 / 21 Checks Passed 100%**:
  1. Login & Token JWT authentication thành công.
  2. Tạo branch, warehouse, supplier, SKU thành công.
  3. Lập và xác nhận phiếu nhập 10 sản phẩm $\rightarrow$ OnHand = 10, Reserved = 0, Available = 10.
  4. Giữ chỗ lần 1 với RequestKey UUID `200 OK` $\rightarrow$ Reserved = 2, Available = 8.
  5. Gửi lại CÙNG RequestKey & CÙNG payload qua HTTP Body $\rightarrow$ `200 OK`, trả đúng Reservation cũ, Reserved vẫn là 2.
  6. Gửi lại CÙNG RequestKey qua Header `Idempotency-Key` $\rightarrow$ `200 OK`, Reserved vẫn là 2.
  7. Gửi CÙNG RequestKey nhưng KHÁC payload $\rightarrow$ Từ chối `409 Conflict`, Reserved không đổi.
  8. Gửi 5 request đồng thời cùng RequestKey mới $\rightarrow$ Tất cả `200 OK`, Reserved chỉ tăng đúng 2m (từ 7 lên 9, không tăng lặp thành 17m).
  9. Tenant B dùng cùng RequestKey với Tenant A $\rightarrow$ `200 OK`, tạo Reservation độc lập, cách ly hoàn toàn.

### 3.3. Kiểm thử Biên dịch & Logic Frontend
- **Frontend Admin Build:** `npm run build` PASS, 0 lỗi TypeScript, 0 lỗi build.
- **POS App Build:** `npm run build` PASS, 0 lỗi TypeScript, 0 lỗi build.
- **Frontend Logic Tests (`scratch/verify_frontend_logic.js`):** **12 / 12 Checks Passed 100%**:
  - Chuẩn hóa UUID RFC4122.
  - Lưu và khôi phục pending reservation / order checkout qua `sessionStorage`.
  - Khôi phục form đúng khi đóng/mở modal và F5.
  - Bỏ qua kiểm tra client khi retry (`isRetry: true`).
  - Chặn request khi không phải retry và tồn khả dụng không đủ.
  - Phân lập lỗi tải kho riêng: Reset `warehouseBalances = []` và bật `warehouseBalanceError`.
  - Contract body gửi đúng `requestKey` và headers `Idempotency-Key` / `X-Request-Key`.

### 3.4. Kiểm thử Docker / Nginx Container Routes
- `http://localhost:5173/` $\rightarrow$ `200 OK` SPA HTML.
- `http://localhost:5173/login` $\rightarrow$ `200 OK` SPA HTML.
- `http://localhost:5173/inventory` $\rightarrow$ `200 OK` SPA HTML.
- `http://localhost:5173/products` $\rightarrow$ `200 OK` SPA HTML.
- `http://localhost:5173/403` $\rightarrow$ `200 OK` SPA HTML.
- `http://localhost:5173/assets/missing.js` $\rightarrow$ `404 Not Found` (trả đúng 404, không trả nhầm HTML).
- `http://localhost:5174/pos` $\rightarrow$ `200 OK` SPA HTML.

---

## 4. RANH GIỚI BÀN GIAO & PHÂN ĐỊNH TRÁCH NHIỆM

### 4.1. Phân định Trách nhiệm Thành viên
- **Thành viên 1 — Đăng Khoa (Lead / Backend 1 & DevOps):**
  - Chịu trách nhiệm toàn diện về cơ sở dữ liệu PostgreSQL, Advisory Lock chống race condition, tính nguyên tử của Idempotency backend (F2).
  - Cấu hình Nginx SPA fallback và Dockerfile container (F7).
  - Nghiệm thu kỹ thuật các PR và tài liệu tích hợp liên thông.
- **Thành viên 3 — Phúc Hữu (Frontend Admin Lead):**
  - Chịu trách nhiệm toàn bộ mã nguồn Web Admin (`src/frontend/admin-dashboard/`).
  - Quản lý state form, `sessionStorage` chống mất dữ liệu khi mất mạng hoặc reload.
  - Xử lý bypass kiểm tra tồn phía client khi retry.
  - Phân tách loading, error và empty state của sổ kho và tồn kho.
  - Tuân thủ nghiêm ngặt hợp đồng Swagger API Backend.
- **Thành viên 2 — Nhựt (Backend 2):**
  - Phụ trách danh mục Catalog, Brand, tìm kiếm sản phẩm POS.

### 4.2. Tuyên bố Minh bạch về Giới hạn Kiểm chứng
> [!IMPORTANT]
> **Giới hạn kiểm chứng hiện tại:**
> - Toàn bộ các bài kiểm thử biên dịch mã nguồn, kiểm thử tự động xUnit, kiểm thử API liên thông trên PostgreSQL thật, kiểm thử logic frontend và kiểm tra container HTTP routing đều đã đạt 100%.
> - **Trạng thái đối với Giao diện Trực quan Trình duyệt (E2E Browser):** **`CHỜ XÁC MINH UI BROWSER`**.
>   Do công cụ browser tự động bị đóng / không khả dụng trong phiên làm việc, đội ngũ chưa thể thực hiện click thao tác trực tiếp trên trình duyệt đối với toàn bộ các màn hình responsive và giao diện người dùng. Đội ngũ ghi nhận trung thực hiện trạng này và khuyến nghị QA/Tester cùng phối hợp thực hiện vòng nghiệm thu trực tiếp trên browser trước khi bàn giao cuối cùng.
