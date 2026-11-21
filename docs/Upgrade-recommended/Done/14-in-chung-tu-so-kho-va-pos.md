# Upgrade 14 — In chứng từ từ sổ kho, truy vết và POS

Chốt ngày 21/11/2026 qua phiên grill-me. Trạng thái: **đã triển khai và nghiệm thu 21/11/2026** ([nghiệm thu](../../report-after-upgrade/nghiem-thu-in-chung-tu-u14.md)). Liên quan: [Gói 4 — In chứng từ](./goi-4-in-chung-tu-va-thong-bao-pos.md) (CS-06), [Upgrade 02 — Truy vết chứng từ xuất kho](./02-truy-vet-chung-tu-xuat-kho.md), [Upgrade 12 — che giá Staff](../Pedding/12-don-mua-va-cau-hinh-gia.md), phiếu xuất kho 02-VT (`goods_issues`).

## 1. Yêu cầu

1. Thu ngân có nút in phiếu xuất kho.
2. Mỗi dòng IN/OUT trong sổ kho có nút xem và in chứng từ gốc: nhập hàng, chuyển kho, kiểm kê, xuất bán, trả hàng, điều chỉnh.
3. Màn truy vết có nút xem và in các chứng từ liên quan.
4. Dữ liệu cũ thiếu chứng từ: reset DB local và seed lại dữ liệu mẫu có đủ chứng từ.

## 2. Hiện trạng (21/11/2026)

| Phần | Đã có | Còn thiếu |
| --- | --- | --- |
| Phiếu xuất 02-VT | Sinh trong cùng transaction khi POS checkout (`PosCheckoutService`) và khi xác nhận đơn online (`OrderService`). In ở màn Đơn hàng Admin (`GoodsIssuePrintModal`) | `GoodsIssuesController` chỉ cho `Owner,Staff`; POS không in được |
| Sổ kho | `InventoryLedgerDto` đã trả `DocumentType`, `DocumentId`, `DocumentNumber`, `GoodsIssueId`, `SalesInvoiceId` | Frontend `LedgerEntry` (`types/domain.ts`) chỉ giữ chuỗi `reference`, không có nút chứng từ ở tab Sổ kho. Thẻ kho (`StockCardModal`) chỉ có link mở truy vết |
| Mẫu in | 02-VT, in lại hóa đơn, phiếu giao hàng, biên nhận POS 58/80 mm, `PrintArea` | Phiếu nhập 01-VT, phiếu chuyển kho (xuất/nhập), biên bản kiểm kê 05-VT, phiếu nhập hàng trả lại, phiếu điều chỉnh |
| API chi tiết | Phiếu nhập, đơn mua, chuyển kho, kiểm kê, trả hàng, hóa đơn, đơn hàng (`Owner,Staff`) | Phiếu điều chỉnh (`InventoryAdjustment`) chưa có API đọc |
| Truy vết | `SalesInvoiceTraceModal`: hóa đơn → đơn hàng → dòng OUT → trả hàng/dòng IN | Không có nút xem/in theo bước, không in gộp |
| Seed demo | `OismsDatabaseSeeder` tạo danh mục, giá, tồn `OnHand = 0`; chỉ chạy ở Development/Test (`DemoSeedPolicy`) | Không có chứng từ, sổ kho trống sau reset |

## 3. Quyết định đã chốt

### POS — Thu ngân in phiếu xuất kho

| # | Quyết định |
| --- | --- |
| Q1 | Nút **"In phiếu xuất kho"** ở màn checkout thành công, cạnh "In hóa đơn". Không thêm lịch sử đơn trên POS ở đợt này. |
| Q2 | Cashier chỉ đọc phiếu xuất của **đơn do chính mình tạo**, trong tenant của JWT. Đơn người khác hoặc tenant khác trả 404 (không tiết lộ tồn tại). Endpoint đọc riêng cho POS, không mở `[Authorize]` của cả `GoodsIssuesController`. |
| Q3 | Giá vốn (`UnitCost`, `TotalCost`) trả **`null` ở dữ liệu** cho Cashier, không chỉ ẩn trên UI. |
| Q4 | Khổ **A4/A5 theo mẫu 02-VT**, không có cột giá vốn. Đơn xuất từ nhiều kho: mỗi kho một trang, in tất cả trong một lần. |

### Sổ kho — nút "Chứng từ"

| # | Quyết định |
| --- | --- |
| Q5 | Mỗi dòng ở **tab Sổ kho** (`InventoryPage`) và **Thẻ kho** (`StockCardModal`) có một nút **"Chứng từ"**. Bấm vào mở modal xem trước mẫu in, trong modal có nút In. Dùng chung một bộ chọn mẫu theo `DocumentType` + chiều IN/OUT. |
| Q6 | Bảng chọn chứng từ: |

| Nguồn dòng sổ | Chiều | Mẫu mở ra |
| --- | --- | --- |
| `GoodsReceipt` | IN | **Phiếu nhập kho 01-VT**, có link mở đơn mua liên quan (nếu có) |
| `Order` | OUT | **Phiếu xuất kho 02-VT** của kho đó (`GoodsIssueId`) |
| `Order` không có `GoodsIssueId` (dữ liệu trước phiếu 02-VT) | OUT | Hóa đơn bán (`SalesInvoiceId`) hoặc đơn hàng, ghi chú "Chứng từ trước khi có phiếu xuất kho". Không sinh dữ liệu bù |
| `StockTransfer` | OUT | **Phiếu xuất kho kiêm vận chuyển nội bộ** (kho đi) |
| `StockTransfer` | IN | **Phiếu nhập kho** phần nhận chuyển kho (kho đến) |
| `StockCount` | IN/OUT | **Biên bản kiểm kê 05-VT** toàn phiếu: tồn sổ, tồn thực tế, chênh lệch từng SKU, người đếm, người duyệt; tô nổi dòng SKU đang bấm |
| `SalesReturn` | IN | **Phiếu nhập kho hàng trả lại** |
| `InventoryAdjustment` | IN/OUT | **Phiếu nhập/xuất điều chỉnh** theo chiều |
| `Unknown` | — | Nút bị vô hiệu, tooltip "Không xác định được chứng từ nguồn" |

| # | Quyết định |
| --- | --- |
| Q7 | Mẫu in: **Owner** thấy đủ giá nhập/giá vốn. **Staff không thấy mọi cột giá nhập và giá vốn**, backend trả `null` qua `ICostVisibilityService` (dịch vụ che giá dùng chung của Upgrade 12). Phiếu chỉ còn số lượng. |

> **Lệch docs cần cập nhật:** `docs/01-users-and-rbac/02-rbac-matrix.md` đang ghi Staff xem giá vốn ở truy vết. Q7 áp dụng cho mẫu in và các API chi tiết dùng để in; ghi ngoại lệ này vào ma trận RBAC.

### Truy vết

| # | Quyết định |
| --- | --- |
| Q8 | Giữ chuỗi bán hàng hiện có. Mỗi bước có nút **Xem/In**: hóa đơn (in lại), đơn hàng (phiếu giao hàng nếu là đơn online), phiếu xuất 02-VT theo từng kho, phiếu nhập hàng trả lại. Thêm nút **"In tất cả"** gộp bộ chứng từ trong một lần in, mỗi chứng từ sang trang mới. Chưa mở rộng truy vết cho nhập hàng, chuyển kho, kiểm kê. |

### Kỹ thuật chung

| # | Quyết định |
| --- | --- |
| Q9 | Dùng lại API chi tiết sẵn có, chỉ **bổ sung trường còn thiếu** cho mẫu in (tên người lập/duyệt/nhận, nhà cung cấp, đơn vị tính, số chứng từ liên quan). Thêm **một API đọc phiếu điều chỉnh**. Không tạo bảng mới, không migration. |
| Q10 | **Không ghi log in, không đánh dấu "bản in lại".** Chân trang ghi "Ngày in" (giờ UTC+07:00) và tên người in. |

### Dữ liệu cũ, reset và seed

| # | Quyết định |
| --- | --- |
| Q11 | **Chỉ reset DB local**, người dùng tự chạy lệnh xóa volume. Claude và Anti không tự chạy `docker compose down -v`. |
| Q12 | **Mở rộng seed demo bằng service thật** (Application service, trong transaction và khóa thật), chạy sau seed danh mục, chỉ ở Development/Test. Kịch bản tối thiểu cho mỗi tenant demo: đơn mua → duyệt → phiếu nhập xác nhận (WAC), POS checkout, đơn online confirm, chuyển kho 2 bước giữa hai kho, kiểm kê có chênh lệch thừa và thiếu được duyệt, trả hàng một phần, một phiếu điều chỉnh nếu có luồng service tạo được. Idempotent: chạy lại không tạo trùng (dùng `RequestKey` cố định hoặc kiểm tra số chứng từ). |
| Q13 | **AWS giữ dữ liệu, reset ở đợt sau.** Đến lúc đó vẫn giữ phương án thay thế ở Q6 cho dòng OUT cũ thiếu phiếu xuất. Reset AWS cần runbook riêng có bước backup, và vì Production không chạy seeder nên cần cách nạp dữ liệu mẫu khác. |

## 4. Phạm vi kỹ thuật

### Backend

- Endpoint Cashier: `GET /api/pos/orders/{orderId}/goods-issues` (`Owner,Staff,Cashier`). Với Cashier: kiểm tra đơn do user hiện tại tạo (`CreatedBy`/người thu ngân), sai thì 404 `GOODS_ISSUE_NOT_FOUND`; luôn che giá vốn.
- Che giá Staff qua `ICostVisibilityService` cho API chi tiết phục vụ in: phiếu nhập, chuyển kho, kiểm kê, trả hàng, phiếu điều chỉnh (đơn mua đã che theo Upgrade 12).
- API mới `GET /api/inventory/adjustments/{id}` (`Owner,Staff`), tenant theo JWT.
- Bổ sung trường in còn thiếu vào DTO chi tiết, không đổi ý nghĩa trường cũ.
- Thêm route mới vào test quét endpoint Staff (che giá) hiện có.

### Frontend Admin

- `LedgerEntry` giữ thêm `documentType`, `documentId`, `documentNumber`, `goodsIssueId`, `salesInvoiceId` (map trong `apiAdapter.ts`).
- Một hàm thuần `resolveLedgerDocument(entry)` trả loại mẫu theo Q6, có test `node --test` trong `test-oisms/frontend/admin-dashboard/`.
- `DocumentPrintModal` dùng `PrintArea`, nạp dữ liệu theo loại, các mẫu mới ở `components/print/`.
- Nút "Chứng từ" ở tab Sổ kho và Thẻ kho; nút Xem/In và "In tất cả" ở `SalesInvoiceTraceModal`.
- Trạng thái đang tải, lỗi 403/404, chứng từ thiếu.

### Frontend POS

- Nút "In phiếu xuất kho" sau checkout thành công, gọi endpoint Cashier, mẫu A4/A5 02-VT không có cột giá vốn, mỗi kho một trang.
- Không ảnh hưởng tự in biên nhận một lần của Gói 4 (`shouldAutoPrint`).

### Seed và Docker

- Seeder chứng từ dùng service thật trong scope có `ICurrentTenantService.SetTenant` và user demo phù hợp vai trò.
- Runbook reset local và rebuild Docker trong báo cáo.

## 5. Nghiệm thu

- Integration test PostgreSQL thật: Cashier đọc phiếu đơn mình được, giá vốn `null`; đơn Cashier khác 404; tenant khác 404; Staff nhận `null` giá ở mọi API chi tiết phục vụ in; Owner thấy đủ; API phiếu điều chỉnh đúng tenant.
- Seeder: chạy hai lần trên DB trống, lần hai không tạo thêm chứng từ hay dòng sổ; mọi dòng sổ sau seed có `DocumentType` khác `Unknown` và dòng OUT bán hàng có `GoodsIssueId`; `on_hand` khớp `balance_after` cuối.
- Frontend: test `resolveLedgerDocument` đủ các nhánh ở Q6; `npm run build` Admin và POS; `npm run lint` và `npm test` POS.
- Docker: reset local, `docker compose up -d --build`, `/health/ready` 200, đăng nhập tài khoản demo, sổ kho có dòng cho đủ 6 loại nguồn.
- Bằng chứng thủ công: ảnh xem trước từng mẫu in (Owner và Staff), phiếu xuất trên POS.

## 6. Docs phải cập nhật khi triển khai

- `docs/05-api-contracts/` — endpoint Cashier phiếu xuất, API phiếu điều chỉnh, trường mới.
- `docs/01-users-and-rbac/02-rbac-matrix.md` — Cashier đọc phiếu xuất đơn mình; Staff không thấy giá trên mẫu in.
- `docs/11-ui-screens/04-inventory-screens.md`, `05-order-screens.md` — nút Chứng từ, truy vết, POS.
- `docs/02-functional-requirements/` — FR in chứng từ/POS liên quan.
- `docs/07-devops-and-deployment/01-docker-compose-guide.md` — reset local và seed chứng từ.
- Báo cáo mới `docs/report-after-upgrade/in-chung-tu-so-kho-pos-u14.md`.
