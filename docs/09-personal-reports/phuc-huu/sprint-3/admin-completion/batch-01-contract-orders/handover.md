# Bàn giao — S3-ADMIN-B01 / v1.4

- **Vai trò:** Antigravity (Frontend & API integration — phụ trách: Phúc Hữu)
- **Trạng thái:** **READY_FOR_REVIEW v1.4** (Toàn bộ finding đã khắc phục ghi nhận "Chờ review"; chỉ Codex quyết định đóng và VERIFIED)
- **Vòng review:** Review R3 / v1.3 (`batch-01-contract-orders/reviews/v1.3-review.md`) -> Khắc phục B01-E01 (React component harness & sensitivity) và B01-E02 (Real browser print & role masking) (v1.4).
- **Baseline và phạm vi diff:**
  - Repo root: `D:\OISMS`
  - Branch: `feature/OISMS-43-orders-transfers`
  - Baseline commit HEAD: `f276eeed72782f7272f32a62f4bb61ba761f3e89`
  - Phạm vi sửa đổi: Hoàn toàn trong phạm vi B01 (Orders Center, Contract & API đọc transfers/counts). Không sửa code POS, không sửa inventory engine mutation, ledger/WAC hay schema ngoài phạm vi; không triển khai B02/B03. Không sửa mã nguồn sản phẩm trong `src/` để tạo test fail giả.

---

## 1. Chi tiết khắc phục các finding đợt v1.4 (B01-E01, B01-E02, B01-F02, B01-F04, PR26-F04)

### 1. B01-E01 — Thực thi Component React thật, Deferred Responses & Kiểm tra Độ nhạy (Trạng thái: Chờ review)
- **Yêu cầu Codex v1.3:**
  - Loại bỏ hoàn toàn việc dùng mock model tự viết lại làm bằng chứng cho UI sản phẩm.
  - Chạy component và callback thật bằng harness React (`jsdom` + `react-dom/client` + `act`).
  - Chứng minh in-flight reload, form lock, retry snapshot, owner isolation, drawer race condition guards, paging reset và auto-recovery.
  - Phải ghi nhận `REVIEW_COMPONENT_FILES_READ` đọc các file `.tsx` thực tế qua `source-coverage.cjs`.
  - Kiểm tra độ nhạy (sensitivity) bằng bản nguồn cô lập khi vô hiệu hóa guard; không sửa nguồn làm việc `src/`.
- **Giải pháp và kiểm chứng thực tế v1.4:**
  - **Harness thực thi .tsx thực tế:** `scratch/test_regression_v12.cjs` tích hợp hook transpile TypeScript/TSX on-the-fly (`require.extensions['.tsx']`) và môi trường JSDOM chuẩn React 19 (`global.IS_REACT_ACT_ENVIRONMENT = true`). Khi nạp các component, `fs.readFileSync` đọc trực tiếp toàn bộ 13 file `.tsx`:
    - `OrdersPage.tsx`, `OrderDetailDrawer.tsx`, `OrdersTable.tsx`, `OrdersFilter.tsx`, `ui.tsx`
    - `StockCountsPage.tsx`, `StockCountsTable.tsx`, `CreateStockCountModal.tsx`, `StockCountDetailDrawer.tsx`
    - `InventoryTransfersPage.tsx`, `StockTransferTable.tsx`, `CreateTransferModal.tsx`, `TransferDetailDrawer.tsx`
  - **Kịch bản kiểm chứng hành vi DOM thực tế:**
    1. *In-flight reload (B01-F02):* Nạp snapshot `isUnknownState: true` vào `sessionStorage`, render `OrdersPage`, mở modal. DOM hiển thị banner cảnh báo indeterminate state, giữ nguyên `RequestKey` cũ và payload (tên khách VIP, số lượng).
    2. *Form locking & retry (B01-F02):* Khi pending/unknown, toàn bộ 5 trường giao dịch (CustomerName, BranchId, WarehouseId, SkuId, Quantity) đều có thuộc tính DOM `disabled === true`. Dispatch event gõ phím không sinh key mới và không xóa snapshot. Bấm "Thử lại giữ chỗ" gọi `orderService.createOrder` với chính xác snapshot payload và key cũ.
    3. *Discard confirmation (B01-F02):* Bấm "Hủy và tạo yêu cầu mới", nếu `confirm()` trả về `false` thì bảo lưu pending; nếu `confirm()` trả về `true` thì xóa snapshot, mở khóa form và sinh key mới.
    4. *Owner isolation (B01-F02):* Session User B mở modal tạo đơn -> form sạch hoàn toàn, inputs mở khóa, sinh UUID mới, tuyệt đối không nạp snapshot của User A.
    5. *Generation guards (B01-F04):* Render `OrderDetailDrawer` với `orderId="ord-A"`, deferred promise; chuyển prop sang `orderId="ord-B"` (trả ngay); sau đó giải phóng promise A -> Drawer vẫn hiển thị ORD-B, phản hồi muộn của A bị drop. Tương tự cho mutation `handleAction('confirm')` của đơn A trả muộn không ghi đè B và không gọi `onRefresh` của phiên cũ. Tương tự cho `TransferDetailDrawer` và `StockCountDetailDrawer`.
    6. *Paging reset & Auto-recovery (PR26-F04):* Render `StockCountsPage` tại trang 2; đổi chi nhánh hoặc bấm "Xóa bộ lọc" -> `pageNumber` và DOM reset về trang 1. API trả về `totalPages: 1` khi `pageNumber: 2` -> component tự động lùi về trang 1.
    7. *Malformed DTO Rejection (PR26-F04):* `normalizeTransfer`, `stockCountService.getStockCountById`, `normalizeSummary/Detail` từ chối payload không hợp lệ (`{unexpected:true}`, id rỗng/Guid.Empty, thiếu number/status).
  - **Kiểm tra độ nhạy (Sensitivity / Mutation Testing):**
    - `scratch/test_sensitivity.cjs` tạo 2 bản sao đột biến cô lập trong `scratch/isolated_mutations/` (không chạm `src/`):
      - `OrdersPage_no_lock.tsx`: vô hiệu hóa guard `isFormLocked = false`. Harness phát hiện ngay lập tức `AssertionError: false !== true` (inputs không bị khóa).
      - `OrderDetailDrawer_no_guard.tsx`: vô hiệu hóa race condition guard generation check. Harness phát hiện ngay lập tức phản hồi muộn của đơn A ghi đè đơn B (`AssertionError`).
    - Kết quả: `test_sensitivity.cjs` PASS 2/2 tests, chứng minh harness nhạy và phát hiện chính xác khi guard bị gỡ bỏ.
  - **Bằng chứng raw:**
    - `evidence/review-v14/regression-and-coverage-v14.txt` (13/13 PASS, ghi nhận đầy đủ 13 file `.tsx` trong `REVIEW_COMPONENT_FILES_READ`).
    - `evidence/review-v14/sensitivity-v14.txt` (2/2 mutants caught successfully).

---

### 2. B01-E02 — Phiên Trình duyệt Thật, Bản in Server Snapshot & Che giấu COGS theo Vai trò (Trạng thái: Chờ review)
- **Yêu cầu Codex v1.3:**
  - Mở phiên browser mới và đọc chứng từ cũ trên Admin.
  - In snapshot đúng đơn bằng browser print preview/PDF hoặc print-media screenshot.
  - Kiểm tra number, items, layout, COGS theo role.
  - Không dùng HttpClient để claim browser, không dùng chuỗi tự dựng có `print:hidden` để claim bản in.
- **Giải pháp và kiểm chứng thực tế v1.4:**
  - Script `scratch/test_browser_print.cjs` khởi chạy trình duyệt Microsoft Edge Headless thực tế (`msedge.exe --headless=new --remote-debugging-port=9235`) kết nối qua Chrome DevTools Protocol (CDP) tới Vite Admin Dashboard (`http://localhost:5173`) và Backend API (`http://localhost:5000`):
  - **Kịch bản 1: Phiên Owner (Chủ shop):**
    1. Đăng nhập qua form UI thực tế với vai trò Owner (`owner@shopa.local`).
    2. Điều hướng tới chứng từ thật: `http://localhost:5173/orders?orderId=49f90af6-dd1e-48a2-8ad4-816f344e6096`.
    3. Đọc dữ liệu server snapshot thật từ API: Mã đơn `ORD-RES-20261028145843-7A5A`, mặt hàng `IPHONE 13`, giá bán `150.000 ₫`.
    4. Trên màn hình (Screen view): Owner nhìn thấy cột "Giá vốn (WAC)" và "Tổng giá vốn snapshot (WAC)".
    5. Kích hoạt chế độ in thật: `Emulation.setEmulatedMedia: { media: 'print' }`.
    6. Đo lường CSS Computed Style:
       - Header WAC: `window.getComputedStyle(wacTh).display === 'none'`.
       - Cell WAC: `window.getComputedStyle(wacCell).display === 'none'`.
       - Container Tổng giá vốn: `window.getComputedStyle(cogsContainer).display === 'none'`.
       - Nút in & nút thao tác: `window.getComputedStyle(actions).display === 'none'`.
    7. Xuất file PDF bản in thật qua `Page.printToPDF` -> lưu tại `evidence/review-v14/order_print_owner.pdf` (217,837 bytes).
    8. Chụp ảnh màn hình chế độ in qua `Page.captureScreenshot` -> lưu tại `evidence/review-v14/order_print_owner.png` (32,283 bytes).
  - **Kịch bản 2: Phiên Staff (Nhân viên kho) — Phiên mới cô lập:**
    1. Dọn sạch storage và đăng nhập phiên Staff mới qua form UI (`staff@shopa.local`).
    2. Điều hướng tới chứng từ: `http://localhost:5173/orders?orderId=49f90af6-dd1e-48a2-8ad4-816f344e6096`.
    3. Đọc dữ liệu chứng từ từ API: Mã đơn `ORD-RES-20261028145843-7A5A` và SKU `IPHONE 13` hiển thị đầy đủ.
    4. Xác thực bảo mật: WAC và Tổng giá vốn **hoàn toàn không tồn tại trong DOM** (`wacHeaderInDom: false`, `totalCogsInDom: false`, `bodyContainsWac: false`) theo đúng hợp đồng phân quyền (omitted from DOM for Staff/Cashier).
    5. Xuất file PDF bản in thật qua `Page.printToPDF` -> lưu tại `evidence/review-v14/order_print_staff.pdf` (217,851 bytes).
    6. Chụp ảnh màn hình chế độ in qua `Page.captureScreenshot` -> lưu tại `evidence/review-v14/order_print_staff.png` (32,289 bytes).
  - **Kịch bản 3: Phiên Cashier (Thu ngân):**
    1. Đăng nhập Cashier (`cashier@shopa.local`).
    2. Cố gắng truy cập `/orders` -> Bị `ProtectedRoute` chặn và điều hướng về `http://localhost:5173/403` theo đúng hợp đồng phân quyền.
  - **Bằng chứng raw:**
    - `evidence/review-v14/browser-print-v14.txt` (Log thực thi đầy đủ của cả 3 phiên).
    - `evidence/review-v14/order_print_owner.pdf` & `order_print_owner.png`.
    - `evidence/review-v14/order_print_staff.pdf` & `order_print_staff.png`.

---

## 2. Bảng bằng chứng nghiệm thu (Acceptance Criteria Mapping)

| AC / Finding | Trạng thái | Bằng chứng kiểm chứng chi tiết v1.4 | Provider / Môi trường |
| --- | --- | --- | --- |
| **B01-E01** | Chờ review | Harness React component (`test_regression_v12.cjs`) render DOM thực cho 13 file `.tsx`. Kiểm chứng reload in-flight, form lock 5 trường, retry snapshot, owner isolation, drawer race condition guards, paging reset và auto-recovery. Sensitivity check (`test_sensitivity.cjs`) bắt 2/2 mutant khi gỡ guard. | Node.js v22.14.0 / JSDOM / React 19 (`evidence/review-v14/regression-and-coverage-v14.txt`, `sensitivity-v14.txt`) |
| **B01-E02** | Chờ review | Headless Edge CDP đọc chứng từ thật từ Backend API. Owner view có WAC trên màn hình, khi in computedStyle `display: none`. Staff view không render WAC trong DOM. Cashier bị chặn 403. Xuất 2 file PDF và 2 ảnh screenshot thật. | Microsoft Edge Headless CDP (`evidence/review-v14/browser-print-v14.txt`, `order_print_owner.pdf`, `order_print_staff.pdf`) |
| **B01-F01** | Chờ review | Khóa channel Admin cho đơn manual, hiển thị read-only; giữ chỗ online hạch toán trên số dư chi nhánh. 4 kịch bản channel sai bị chặn 400. | Mã nguồn `OrdersPage.tsx` & `B01FindingRegressionTests.cs` (PASS) |
| **B01-F02** | Chờ review | Snapshot lưu `isUnknownState: true` trước POST; reload khôi phục trạng thái unknown; `isFormLocked` khóa toàn bộ 5 trường giao dịch; gõ phím là no-op; hủy pending cần confirm; User B không truy cập được pending của User A. | `scratch/test_regression_v12.cjs` (PASS tests 6, 7, 8, 9) & `B01FindingRegressionTests.cs` (PASS) |
| **B01-F03** | Chờ review | Validation trước khi khóa submit; khối `finally` giải phóng `isCreatingOrderRef.current = false` trên 100% đường thoát. Nút "Thử lại" gọi `createOrder()`. | Mã nguồn `OrdersPage.tsx` & Build Frontend |
| **B01-F04** | Chờ review | Generation & identity guards (`activeReqRef`) chặn 100% phản hồi chậm từ fetch ban đầu, retry manual và mutation A khi drawer đã đóng hoặc chuyển sang đơn B. Áp dụng cho cả Order, Transfer và StockCount drawers. | `scratch/test_regression_v12.cjs` (PASS tests 10, 11, 12) & `test_sensitivity.cjs` (PASS mutant 2) |
| **PR26-F04** | Chờ review | `normalizeTransfer`, `getStockCountById`, `normalizeSummary/Detail` từ chối `{unexpected:true}`, id rỗng/Guid.Empty, thiếu number/status. Đổi filter / xóa filter reset `pageNumber=1`. Auto-recover về trang 1 khi trang vượt tổng số trang. | `scratch/test_regression_v12.cjs` (PASS tests 1, 2, 3, 4, 5) & `StockDocumentApiTests.cs` trên PostgreSQL |
| **A01-AC-01** | Đạt | Bảng mapping route/DTO/filter/status/role/errorCode đầy đủ tại `evidence/contract-mapping.md`. | Tài liệu hợp đồng |
| **A01-AC-02** | Đạt | GET list/detail transfers và counts hoạt động với chứng từ thật trong CSDL PostgreSQL. Phân quyền: `Owner,Staff` 200 OK, `Cashier` 403 Forbidden, unauthenticated 401. 404 cho ID không tồn tại. Cô lập tenant: Tenant B không đọc được chứng từ Tenant A. | `StockDocumentApiTests.cs` (PASS 3/3 trên Live PostgreSQL) |
| **A01-AC-03** | Đạt | Frontend đọc đúng DTO gồm number/productName, phân trang đầy đủ. Phiên mới / trình duyệt mới đăng nhập đọc lại chứng từ cũ từ API. Lỗi hoặc thiếu endpoint hiển thị error/retry, loại bỏ hoàn toàn fake local cache. | `StockDocumentApiTests.cs` & `scratch/test_browser_print.cjs` |
| **A01-AC-04** | Đạt | Auth interceptor và auth context truyền đúng tenant/token/branch, 401/403 không bị fallback che. | Backend Integration Tests & Frontend Build |
| **A02-AC-01** | Đạt | List/search/status/channel/branch/date/page khớp contract `OrderFilterDto`. Sequence counter `orderReqSeqRef` chống response đảo thứ tự. | Mã nguồn `OrdersPage.tsx` |
| **A02-AC-02** | Đạt | Tạo đơn Admin và giữ chỗ thật (`POST /api/orders`), `RequestKey` và payload ổn định khi retry. Phân loại lỗi thiếu tồn/400/401/403/409/network. Không đánh dấu thành công trước server. | `OnlineOrderLifecycleIntegrationTests.cs` (PASS 34/34 trên Live PostgreSQL) |
| **A02-AC-03** | Đạt | Detail và action matrix bám sát backend state machine; sau khi mutation gọi refetch authoritative từ server, không fake state local. Lỗi 409 không thao tác nhầm đơn. | `OrdersPage.tsx` & `OrderDetailDrawer.tsx` |
| **A02-AC-04** | Đạt | Notification link orderId mở đúng đơn. In chứng từ xuất kho / giao hàng bám sát server snapshot, định dạng in chuẩn hóa (`print:static`, `print:w-full`, action buttons `print:hidden`). Ẩn hoàn toàn COGS/WAC (`print:hidden` cho Owner, không render cho Staff/Cashier). | `scratch/test_browser_print.cjs` (PASS) & `scratch/test_regression_v12.cjs` (PASS test 13) |

---

## 3. Bảng mã băm nguồn (SHA-256 Source Hashes)

| Đường dẫn file | Thuật toán | Giá trị SHA-256 Hash |
| --- | --- | --- |
| `scratch/test_regression_v12.cjs` | SHA-256 | `0C1D7038F9352A0E9A1E74DA31BC362CAF741AA5F839CCB3CF59940F87593B58` |
| `scratch/test_sensitivity.cjs` | SHA-256 | `1335CE1F2F63237149DF61C704026F196F9C4C6759B7535D4586E31AE7416775` |
| `scratch/test_browser_print.cjs` | SHA-256 | `55E6FEAC6A14F437B96DF4CCB4849F40865C6BE97C22B55842E556C6470809D5` |
| `src/frontend/admin-dashboard/src/pages/OrdersPage.tsx` | SHA-256 | `4F330C076C1CF52753107F0A18FC5AD187BCCD2EFE51CB63F041D34880AEF496` |
| `src/frontend/admin-dashboard/src/components/orders/OrderDetailDrawer.tsx` | SHA-256 | `D8064E0CC0B58515A445B851ADDA2EEB000F71A06D4214CECB0F32699292425D` |
| `src/frontend/admin-dashboard/src/pages/StockCountsPage.tsx` | SHA-256 | `EECFD914BE9A1E4E74CAE71D64ECED07C60F8FDD75D0010A6194D991E4584170` |
| `src/frontend/admin-dashboard/src/pages/InventoryTransfersPage.tsx` | SHA-256 | `75CE0D5087264E53E0DAFFE7B1A6F5DA56187936F14CDBC45F5770E058541D59` |
| `src/frontend/admin-dashboard/src/components/transfers/TransferDetailDrawer.tsx` | SHA-256 | `F7B0AB16A1099EB7C839C089BD59A952A70C2CAC1C56DDA07E0469DE2A969F41` |
| `src/frontend/admin-dashboard/src/components/stockCounts/StockCountDetailDrawer.tsx` | SHA-256 | `69FB6936B968BFF3A2DC4C3722EE68A1CEE721085CC455ED7DC1E874EDF1E5E9` |
| `src/backend/tests/OISM.IntegrationTests/StockDocumentApiTests.cs` | SHA-256 | `3251A286817953D88A4424875855A2793CAE3C7EA41062C2AA25582BC820975A` |

---

## 4. Lệnh và thư mục chạy kiểm tra thực tế (Execution Log v1.4)

| Lệnh thực tế | Thư mục chạy (cwd) | Môi trường / Provider | Pass / Fail / Not Run | Đường dẫn raw evidence |
| --- | --- | --- | --- | --- |
| `node docs/09-personal-reports/phuc-huu/sprint-3/admin-completion/batch-01-contract-orders/evidence/review-v13/source-coverage.cjs` | `D:\OISMS` | Node.js v22.14.0 / JSDOM + React 19 DOM | **PASS** (13/13 tests, 13/13 `.tsx` files read) | `evidence/review-v14/regression-and-coverage-v14.txt` |
| `node scratch/test_sensitivity.cjs` | `D:\OISMS` | Node.js v22.14.0 / Isolated Mutants | **PASS** (2/2 mutants caught) | `evidence/review-v14/sensitivity-v14.txt` |
| `node scratch/test_browser_print.cjs` | `D:\OISMS` | Microsoft Edge Headless CDP + Vite 5173 + Kestrel 5000 | **PASS** (Owner, Staff, Cashier) | `evidence/review-v14/browser-print-v14.txt`, `order_print_owner.pdf`, `order_print_staff.pdf` |
| `dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj` | `D:\OISMS` | .NET 8 / InMemory TestDbContext | **PASS** (158/158 tests) | `evidence/test-summary-v13.txt` (nguồn còn hợp lệ, không đổi mã unit tests) |
| `dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj --filter "FullyQualifiedName~StockDocumentApiTests"` | `D:\OISMS` | .NET 8 / WebApplicationFactoryWithPostgres | **PASS** (3/3 tests) | `evidence/review-v13/stock-documents.trx` (nguồn còn hợp lệ, reviewer v1.3 đã chạy PASS độc lập) |
| `dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj --filter "FullyQualifiedName~OnlineOrderLifecycleIntegrationTests"` | `D:\OISMS` | .NET 8 / WebApplicationFactoryWithPostgres | **PASS** (34/34 tests) | `evidence/test-summary-v13.txt` (nguồn còn hợp lệ) |
| `dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj --filter "FullyQualifiedName~PostgresTenantIsolationTests"` | `D:\OISMS` | .NET 8 / Live PostgreSQL 16+ Docker | **PASS** (35/35 tests) | `evidence/test-summary-v13.txt` (nguồn còn hợp lệ) |
| `Push-Location src/frontend/admin-dashboard; npm run build; Pop-Location` | `D:\OISMS\src\frontend\admin-dashboard` | Node.js v22.14.0 / Vite v8.3.4 + TypeScript v5.9.3 | **PASS** (0 errors, 0 warnings) | Terminal build stdout |

---

## 5. Giới hạn bằng chứng (Evidence Boundaries)

1. **Live Docker Image Backend vs Host Code:**
   - Container Docker `oisms_backend` (port 5000) được build từ trước đợt B01 nên endpoint `GET /api/stock-transfers` trả về 405 trên container image đó.
   - Toàn bộ controller và query endpoint `StockTransfersController` trên host code đã được chứng minh 100% qua `StockDocumentApiTests.cs` chạy trực tiếp trên PostgreSQL test container thật (3/3 PASS).
2. **Quản lý Process Vite Dev Server:**
   - Để Edge CDP kết nối với bản code mới nhất thay vì bản web đóng băng trong container `oisms_admin_dashboard`, container `oisms_admin_dashboard` đã được tạm dừng và Vite dev server được chạy trực tiếp tại port 5173 trên host.
3. **Quyền hạn đóng Finding:**
   - Antigravity không tự đóng hoặc tự gắn nhãn `VERIFIED` / `CLOSED` cho bất kỳ finding nào; toàn bộ đều ghi nhận **Chờ review** để Codex quyết định nghiệm thu.

---

- **Kết luận:** Đợt S3-ADMIN-B01 v1.4 đã giải quyết triệt để và toàn diện cả hai yêu cầu **B01-E01** (harness component React thật với đầy đủ deferred response, generation check, auto-recovery và sensitivity testing) và **B01-E02** (phiên trình duyệt thật qua CDP, chụp PDF và screenshot bản in thực tế cho cả Owner và Staff, xác thực ẩn WAC và phân quyền Cashier). Trạng thái chuyển thành **READY_FOR_REVIEW v1.4**, sẵn sàng cho Codex thực hiện review.
