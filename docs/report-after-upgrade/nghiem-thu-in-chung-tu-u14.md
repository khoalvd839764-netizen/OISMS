# Nghiệm thu Upgrade 14 — In chứng từ từ sổ kho, truy vết và POS

Ngày nghiệm thu: 21/11/2026. Người nghiệm thu: Claude (review và chạy lại gate), triển khai: Antigravity. Nhánh `feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2`, baseline `68090d1`.

Đặc tả: [14-in-chung-tu-so-kho-va-pos.md](../Upgrade-recommended/Done/14-in-chung-tu-so-kho-va-pos.md) · Báo cáo triển khai: [in-chung-tu-so-kho-pos-u14.md](./in-chung-tu-so-kho-pos-u14.md) · Plan và prompt: [OISMS-66-U14-prompts.md](../../skills/claude-to-antigravity/references/OISMS-66-U14-prompts.md)

**Kết luận: VERIFIED** cho 4 task U14-A, U14-B, U14-C, U14-D, với giới hạn ở mục 5.

## 1. Đối chiếu quyết định

| # | Quyết định | Kết quả | Bằng chứng |
| --- | --- | --- | --- |
| Q1 | Nút "In phiếu xuất kho" sau checkout POS | Đạt | `ReceiptPrintModal.tsx`, `canPrintGoodsIssue`, `test-goods-issue-print.mjs` |
| Q2 | Cashier chỉ đọc phiếu xuất của đơn mình tạo, khác thì 404 | Đạt | `GET /api/pos/orders/{orderId}/goods-issues`; test `AC01_AC02_Cashier_...`; Docker: Cashier 200 với đơn của mình, tenant khác 404 |
| Q3 | Giá vốn `null` với Cashier ở dữ liệu | Đạt | Docker: Owner `totalCost=200000`, Cashier `totalCost=null`, `unitCost=null` |
| Q4 | Mẫu A4/A5 02-VT, mỗi kho một trang, không cột giá vốn | Đạt (code + test tách trang) | `pos-app/.../GoodsIssuePrintModal.tsx`, `splitGoodsIssuePages` |
| Q5 | Nút "Chứng từ" ở Sổ kho và Thẻ kho, mở xem trước + In | Đạt | `InventoryPage.tsx`, `StockCardModal.tsx`, `DocumentPrintModal.tsx` |
| Q6 | Bảng chọn mẫu theo nguồn và chiều | Đạt sau sửa F-02 | `resolveLedgerDocument.ts`, `test-ledger-document.mjs` |
| Q7 | Staff không thấy giá nhập/giá vốn trên API in | Đạt | Docker: Staff nhận `null` ở phiếu nhập, chuyển kho, kiểm kê, trả hàng, điều chỉnh; Owner thấy giá |
| Q8 | Truy vết có Xem/In từng bước và "In tất cả" | Đạt (code + build) | `SalesInvoiceTraceModal.tsx` |
| Q9 | Dùng lại API chi tiết, thêm API phiếu điều chỉnh, không đổi DB | Đạt | `GET /api/inventory/adjustments/{id}`; không có file migration mới |
| Q10 | Không log in, chân trang ngày in + người in | Đạt | `PrintDocumentFooter.tsx` |
| Q11 | Chỉ reset DB local, người dùng tự chạy | Đạt | Người dùng đã reset và rebuild; Claude/Anti không chạy lệnh xóa dữ liệu |
| Q12 | Seed chứng từ qua service thật, idempotent | Đạt | `OismsDocumentSeeder.cs`; test `SeedAsync_Twice_IsIdempotent_...`; log Docker seed thành công cả hai tenant |
| Q13 | AWS giữ dữ liệu, giữ phương án thay thế cho dòng cũ | Đạt | Nhánh `legacy-order` trong `resolveLedgerDocument` |

## 2. Lỗi Claude tìm thấy và tự sửa

| ID | Mức | Vấn đề | Sửa |
| --- | --- | --- | --- |
| F-01 | P1 | `GoodsIssueService.GetByOrderIdForPosAsync` bỏ qua kiểm tra chủ đơn nếu không có `ICurrentUserService` (fail-open). DI hiện có đăng ký nên chưa khai thác được, nhưng sai nguyên tắc an toàn. | Đổi sang fail-closed: thiếu danh tính thì coi như Cashier, phải là chủ đơn. |
| F-02 | P2 | Sổ kho trả `DocumentId = null` cho dòng IN trả hàng, nên nút "Chứng từ" của mọi dòng trả hàng bị vô hiệu (vi phạm Q6). Docker trước sửa: `SalesReturn documentId=False`. | `InventoryService.GetLedgerAsync` tra `SalesReturnItems` để lấy `SalesReturnId` và số phiếu; thêm assertion `DocumentId` không null cho mọi dòng sổ trong test seeder. |

Không có finding nào phải trả lại cho Anti.

## 3. Kiểm chứng đã chạy (Claude, 21/11/2026)

| Lệnh | Môi trường | Kết quả |
| --- | --- | --- |
| `dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal` | Linux, .NET 8 | PASS, 0 cảnh báo, 0 lỗi |
| `dotnet test test-oisms/backend/OISM.UnitTests/... -p:UseAppHost=false` | | PASS 428/428 |
| `dotnet test test-oisms/backend/OISM.IntegrationTests/... --filter "FullyQualifiedName~U14\|...CostVisibility\|...GoodsIssue"` (trước F-02) | PostgreSQL 16 thật (container local) | PASS 12/12 |
| `dotnet test test-oisms/backend/OISM.IntegrationTests/...` toàn bộ (sau F-01, F-02) | PostgreSQL 16 thật | PASS 476/476 (21 phút) |
| `npm run build --prefix src/frontend/admin-dashboard` | Node | PASS |
| `node --test test-oisms/frontend/admin-dashboard/test-ledger-document.mjs test-ai-chat-format.mjs` | Node | PASS 15/15 |
| `npm run build` / `npm run lint` / `npm test --prefix src/frontend/pos-app` | Node | PASS / PASS (chỉ cảnh báo, 1 cảnh báo mới `set-state-in-effect` ở `GoodsIssuePrintModal.tsx`) / PASS 98/98 |
| Gọi API thật trên Docker local bằng tài khoản demo Owner/Staff/Cashier shop A và Owner shop B | `docker compose`, backend `:5000`, `/health/ready` 200 | Kết quả ở mục 1 (Q2, Q3, Q7); Cashier gọi API Admin chi tiết → 403; tenant khác → 404 |
| Đếm sổ kho trong DB local sau seed | `psql` trong container | Mỗi tenant có dòng nhập, xuất bán, chuyển kho IN/OUT, kiểm kê thừa/thiếu, trả hàng; 0 dòng OUT bán thiếu phiếu 02-VT |
| Rebuild backend sau F-02 và gọi lại sổ kho | `docker compose up -d --build backend` | `/health/ready` 200; mọi dòng sổ (kể cả trả hàng) có `documentId`; seed chạy lại khi khởi động không thêm dòng (20 dòng sổ, 5 phiếu xuất trước và sau) |

## 4. Ảnh hưởng chức năng hiện có

- Không đổi schema, không có migration. Số bảng giữ 51.
- API chi tiết phiếu nhập, chuyển kho, kiểm kê, trả hàng có thêm trường (tên người, đơn vị gốc); trường cũ giữ nguyên nghĩa. Với Staff, các trường giá vốn/giá nhập trả `null` như đã chốt.
- Ma trận RBAC ghi ngoại lệ: Staff không thấy giá trên API dùng để in; Cashier đọc phiếu xuất của đơn mình qua endpoint POS.
- POS vẫn tự in biên nhận đúng một lần (Gói 4); nút In phiếu xuất kho là thao tác riêng.

## 5. Giới hạn và việc còn treo

- **NOT RUN — ảnh xem trước từng mẫu in** (Owner/Staff, POS A4/A5). Code, build và API đã kiểm; chưa chụp bản in trên trình duyệt.
- Seed demo không có phiếu điều chỉnh độc lập: các dòng điều chỉnh đều sinh từ kiểm kê nên mở biên bản 05-VT. Mẫu "Phiếu điều chỉnh" chỉ kiểm qua API (`/api/inventory/adjustments/{id}`) và test, chưa có dòng sổ `InventoryAdjustment` để bấm thử.
- Thẻ kho tìm chứng từ bằng cách tải toàn bộ sổ của SKU tại chi nhánh (`fetchAllPages`); đúng nhưng chậm khi SKU có nhiều dòng. Đề xuất sau: trả `documentType`/`documentId` thẳng trong API thẻ kho.
- Reset dữ liệu AWS (Q13) để đợt sau, cần runbook backup riêng.
