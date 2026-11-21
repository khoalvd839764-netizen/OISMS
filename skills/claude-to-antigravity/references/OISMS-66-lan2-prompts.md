# Prompt giao Anti — OISMS-66 lần 2 (Upgrade 12 + 13)

Ba gói **chạy song song**, mỗi gói một phiên Antigravity, cùng nhánh `feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2`, cùng thư mục repo (người dùng chọn). PR draft vào `develop` đã tạo sẵn từ nhánh này; mỗi gói xong thì Anti tự test, commit, push và comment báo cáo lên PR; chỉ merge khi cả 3 gói (và Gói 4, 5) được Claude `VERIFIED`.

Gói 4 (cấu hình giá bán khi nhập kho) và Gói 5 (che giá dùng chung) viết prompt sau khi Gói 1–3 xong.

Đặc tả: `docs/Upgrade-recommended/Pedding/12-don-mua-va-cau-hinh-gia.md`, `docs/Upgrade-recommended/Pedding/13-ton-kho-theo-ky.md`.

## Luật chung cho cả 3 gói (dán kèm mỗi prompt)

```text
LUẬT LÀM SONG SONG (3 phiên Anti cùng thư mục, cùng nhánh):
- Chỉ sửa file trong mục ĐƯỢC SỬA của gói mình. File của gói khác: không mở để sửa, không format, không revert.
- FILE CHUNG (nhiều gói cùng sửa): docs/database/oisms_schema.sql, src/backend/src/OISM.Infrastructure/Persistence/DatabaseMigrationService.cs,
  src/backend/src/OISM.Application/DependencyInjection.cs, src/backend/src/OISM.Infrastructure/DependencyInjection.cs,
  src/backend/src/OISM.WebApi/Http/ApiErrorTitles.cs, docs/01-users-and-rbac/02-rbac-matrix.md,
  docs/12-error-and-status-codes/02-error-codes-catalog.md, src/frontend/admin-dashboard/src/pages/InventoryPage.tsx.
  Với file chung: đọc lại file ngay trước khi sửa, chỉ sửa đúng đoạn của mình bằng edit nhỏ, không ghi đè cả file, không sắp xếp lại.
- Build hoặc test lỗi ở file của gói khác: không sửa, ghi vào bàn giao mục "Lỗi ngoài gói".

KHI LÀM XONG — TEST, PUSH, BÁO CÁO LÊN PR (người dùng đã cho phép trong chat ngày 19/11/2026, chỉ áp dụng cho 3 gói này):
1. Chạy đủ mục GATE của gói. Chỉ đi tiếp khi mọi gate PASS. Có FAIL hoặc NOT RUN không lý do thì KHÔNG push; trả BLOCKED trong chat cho người dùng.
2. Commit: `git add` đúng file của gói (không `git add -A`, không `git add .`). File chung: chỉ stage phần của gói mình
   (kiểm `git diff --cached <file>`); nếu trong file có thay đổi của gói khác chưa xong thì KHÔNG add file đó, ghi vào báo cáo để Claude gom.
   Không add .env, file dump, scratch/, tmp/, ảnh .png trong src/. Quét file sắp commit, không có API key/secret.
   Message: `[OISMS-66] <type>(<scope>): <mô tả tiếng Việt>`. KHÔNG có Co-Authored-By, KHÔNG có dòng ghi công AI.
3. Push: `git push origin feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2`. Bị từ chối do nhánh đã có commit mới
   thì `git pull --rebase origin <nhánh>` rồi chạy lại build trước khi push. Không force-push.
4. Comment báo cáo lên PR của nhánh này: đang đứng ở nhánh lần 2 thì chạy `gh pr comment --body-file <file tạm ngoài repo>` (gh tự chọn PR của nhánh hiện tại), nội dung theo mẫu BÁO CÁO PR dưới đây.
5. KHÔNG merge, KHÔNG chuyển PR khỏi draft, KHÔNG sửa mô tả PR. Merge chỉ khi Claude VERIFIED và người dùng đồng ý.

MẪU BÁO CÁO PR (tiếng Việt, không ghi công AI):
## Gói <số> — <tên> | Trạng thái: READY_FOR_REVIEW
- Commit: <hash> (<số file>)
- File đã đổi: <danh sách ngắn theo nhóm BE / FE / migration / test / docs>
- AC: AC-01 ĐẠT/CHƯA ĐẠT — <bằng chứng> … (đủ mọi AC)
- Kiểm tra đã chạy: <lệnh> | cwd | PostgreSQL thật/InMemory | PASS x/y | FAIL | NOT RUN (lý do)
- Migration và cách nâng cấp DB: <file, chạy mấy lần, kết quả>
- Docs đã cập nhật: <danh sách>
- File chung chưa commit (nếu có) và lý do
- Lỗi ngoài gói, giới hạn, việc còn lại
```

## Gói 1 — Đơn mua `PendingApproval`, phân bổ phí, bỏ hóa đơn NCC

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.
[Dán LUẬT LÀM SONG SONG ở trên]

TASK: OISMS-66-L2-G1 — Đơn mua PendingApproval + phân bổ phí + bỏ hóa đơn NCC | Mức: CRITICAL | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2, commit mới nhất của nhánh khi bắt đầu (ghi lại bằng `git log -1 --format=%h`).

MỤC TIÊU: Staff đề nghị mua (chỉ SKU/đơn vị/SL), Owner chọn NCC + giá + phí rồi duyệt; giá vốn nhập lấy từ giá đã phân bổ của đơn mua; gỡ hóa đơn NCC; Staff không thấy giá trên đơn mua và phiếu nhập.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/Pedding/12-don-mua-va-cau-hinh-gia.md: "Tóm tắt luồng" A–C, E, F; mục 1, 2, 3 (Q1–Q14, Q20–Q24); mục 7 BE-12.1→12.6, FE-12.1, FE-12.2
- Mẫu: Features/PurchaseOrders/Services/PurchaseOrderService.cs, Domain/Entities/PurchaseOrder.cs, Features/Inventory/Services/GoodsReceiptService.cs (ConfirmReceiptAsync),
  docker/postgres/migrations/20261114_user_branches.sql, test-oisms/backend/OISM.IntegrationTests/PurchaseOrderIntegrationTests.cs, PurchaseOrderSeed.cs
- Skill: skills/postgresql/SKILL.md, skills/csharp-pro/SKILL.md

ĐƯỢC SỬA: migration mới docker/postgres/migrations/20261119_purchase_order_pending_approval.sql; phần đơn mua trong oisms_schema.sql (file chung);
Domain PurchaseOrder*, EF Configuration đơn mua; Features/PurchaseOrders/; GoodsReceiptService.cs; Features/SupplierInvoices/ (gỡ service);
PurchaseOrdersController.cs; SupplierInvoicesController.cs (xóa); ApiErrorTitles.cs, DatabaseMigrationService.cs, DependencyInjection.cs (file chung);
FE: pages/PurchaseOrdersPage.tsx, components/purchase/*, components/supplierInvoices/* (gỡ), components/GoodsReceiptList.tsx,
services/purchaseOrderService.ts, types/purchaseOrder.ts, phần phiếu nhập trong pages/InventoryPage.tsx (file chung); test mới.
KHÔNG ĐƯỢC SỬA: OrderService, PosCheckoutService, InventoryService, ReportService, Pricing (thuộc Gói 3, 2, 4); không làm cấu hình giá bán (Gói 4).

ACCEPTANCE CRITERIA:
- AC-01 (BE-12.1, Q2, Q12, Q14, Q20–Q22): migration chạy 2 lần không lỗi: trạng thái PendingApproval; supplier_id, unit_price NULL được khi Draft/PendingApproval,
  CHECK bắt buộc có khi Approved trở đi; order_date (mặc định ngày tạo), shipping_fee, discount_amount, vat_amount >= 0; return_reason/returned_by/returned_at;
  allocated_unit_cost theo dòng; cột phiên bản; Draft cũ đủ NCC + giá → PendingApproval; REVOKE INSERT/UPDATE/DELETE bảng hóa đơn NCC khỏi oisms_app.
- AC-02 (Q3, Q5, Q11): Staff tạo Draft (giá/NCC gửi lên bị bỏ qua), sửa/hủy Draft của mình, Submit → PendingApproval rồi không sửa được (409).
  Owner tạo → PendingApproval; Owner sửa NCC/giá/phí/SL ở PendingApproval; Return (bắt buộc lý do) → Draft; Cancel. Cashier 403.
- AC-03 (Q2, Q3, Q20, Q21): Approve chỉ từ PendingApproval, thiếu NCC hoặc còn giá NULL → 422; giá 0 cần xác nhận như cũ; phiên bản lệch → 409.
  Khi duyệt tính allocated_unit_cost: phí cộng, chiết khấu trừ theo tỷ lệ SL × giá; tổng phân bổ = Σ dòng + phí − chiết khấu (dư làm tròn dồn dòng cuối); chiết khấu > tổng hàng → 422.
- AC-04 (Q21, Q23): xác nhận phiếu nhập dùng allocated_unit_cost / FactorSnapshot làm giá nhập cho WAC; không còn đoạn áp giá hóa đơn NCC; thứ tự khóa giữ nguyên.
- AC-05 (Q6): GET gợi ý giá (Owner): giá đơn đã duyệt gần nhất cùng NCC + SKU + đơn vị, kèm WAC chi nhánh quy theo đơn vị.
- AC-06 (Q1): không còn endpoint /api/supplier-invoices*, không còn đăng ký DI; entity/bảng giữ để đọc.
- AC-07 (Q9): Staff gọi list/detail đơn mua và phiếu nhập: mọi trường giá, tổng tiền, phí, VAT, allocated_unit_cost là null; supplier null khi Draft/PendingApproval.
  (Kiểm tra vai trò cục bộ theo mẫu ReportsController isOwner; Gói 5 sẽ gộp.)
- AC-08 FE (FE-12.1, FE-12.2): màn đơn mua đúng luồng từng vai trò, nút Gửi/Lưu/Duyệt/Trả về/Hủy, hiện giá gợi ý + WAC, phí/chiết khấu/VAT, giá vốn sau phân bổ;
  phiếu nhập Staff không có cột giá; không còn UI hóa đơn NCC → npm run build PASS.

INVARIANT: ledger chỉ INSERT; WAC qua WacCalculator; transaction + khóa sau khi mở transaction, thứ tự như luồng cũ; tenant từ JWT; idempotency tạo đơn giữ nguyên.

DOCS PHẢI CẬP NHẬT: 04-architecture-and-design/04-state-machines.md (đơn mua); 02-functional-requirements/fr-04-cost-wac.md (phân bổ, VAT, bỏ hóa đơn NCC);
05-api-contracts/03-inventory-endpoints.md; 12-error-and-status-codes/02-error-codes-catalog.md; 01-users-and-rbac/02-rbac-matrix.md (đơn mua);
11-ui-screens/04-inventory-screens.md; database/README.md, 04-architecture-and-design/03-database-design.md;
Upgrade-recommended/Done/01-mua-hang-nhap-kho-gia-von.md (ghi U01-B bị thay bởi Upgrade 12);
báo cáo mới docs/report-after-upgrade/don-mua-pending-approval-u12.md.

GATE: dotnet build; dotnet test UnitTests; IntegrationTests --filter "PurchaseOrder|GoodsReceipt" trên PostgreSQL thật (OISM_TEST_*);
test mới PostgresPurchaseOrderU12IntegrationTests: chuyển trạng thái × vai trò, CHECK DB, phân bổ + WAC sau nhập, phiên bản lệch, Staff null, migration chạy 2 lần;
npm run build admin-dashboard.

GIỚI HẠN: không thêm dependency; chỉ commit/push theo mục "KHI LÀM XONG"; một lỗi tối đa 2 lần thử rồi BLOCKED; không nới assertion, không skip test.
BÀN GIAO: làm theo mục "KHI LÀM XONG" (test, commit, push, comment PR), rồi trả bàn giao trong chat theo skills/claude-to-antigravity/assets/handoff.md kèm link comment PR.
```

## Gói 2 — Tồn kho theo kỳ, thẻ kho, xuất Excel

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.
[Dán LUẬT LÀM SONG SONG ở trên]

TASK: OISMS-66-L2-G2 — Báo cáo NXT theo kỳ + thẻ kho S12/S10 + Excel | Mức: CRITICAL (quyền xem giá trị) | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2, commit mới nhất của nhánh khi bắt đầu (ghi lại bằng `git log -1 --format=%h`).

MỤC TIÊU: Tab "Tồn theo kỳ" trong màn Tồn kho: đầu kỳ, nhập, xuất, cuối kỳ theo chi nhánh hoặc toàn cửa hàng; thẻ kho từng SKU; xuất Excel; chỉ đọc sổ cái, không đổi schema.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/Pedding/13-ton-kho-theo-ky.md: mục 1, 2 (Q1–Q14), 3, 4, 6, 7 BE-13.1→13.3, FE-13.1, FE-13.2
- docs/database/oisms_schema.sql: inventory_transactions, stock_transfers; index ix_inventory_transactions_sku_history
- Mẫu: Features/Reports/Services/ReportService.cs + ReportsController.cs (isOwner, mốc UTC+7), Features/Inventory/Services/InventoryService.cs (đọc sổ, chỉ tham khảo),
  frontend pages/ReportsPage.tsx (in), test-oisms/backend/OISM.IntegrationTests/PostgresAiStockMovementIntegrationTests.cs
- Skill: skills/sql-optimization-patterns/SKILL.md, skills/csharp-pro/SKILL.md

ĐƯỢC SỬA: Features/Reports/ (service, DTO mới cho NXT và thẻ kho), ReportsController.cs; interface xuất Excel ở Application/Common/Interfaces + cài đặt ClosedXML ở OISM.Infrastructure
(thêm package ClosedXML vào OISM.Infrastructure.csproj — dependency duy nhất được thêm); DependencyInjection.cs (file chung);
FE: component mới cho tab và thẻ kho, services/types mới, chèn tab tối thiểu vào pages/InventoryPage.tsx (file chung: chỉ thêm import + mục tab); test mới.
KHÔNG ĐƯỢC SỬA: InventoryService.cs, OrderService, PosCheckoutService, schema/migration, đơn mua, phiếu nhập.

ACCEPTANCE CRITERIA:
- AC-01 (Q2, mục 1): GET báo cáo kỳ theo from/to (ngày, UTC+7, nửa mở), branchId hoặc tất cả; đầu kỳ/cuối kỳ = balance_after dòng cuối trước mốc (không có = 0); khoảng > 366 ngày hoặc ngày tương lai → 400.
- AC-02 (Q4): nhập tách mua/khách trả/chuyển đến/kiểm kê-điều chỉnh; xuất tách bán POS/TMĐT/Admin (theo Order.Channel, kiểm tra giá trị kênh thực tế trong code)/chuyển đi/kiểm kê-điều chỉnh.
- AC-03 (Q9): chế độ tất cả bỏ chuyển nội bộ khỏi nhập/xuất, có cột hàng đang chuyển đầu kỳ và cuối kỳ (OUT chuyển trước mốc mà IN tương ứng chưa có trước mốc);
  một chi nhánh có cột "đang chuyển đến". Σ chi nhánh + đang chuyển = tổng.
- AC-04 (Q7, Q12): luôn có mọi SKU (kể cả 0), tìm theo tên/mã, lọc danh mục, phân trang SKU TRƯỚC rồi mới tổng hợp sổ, có dòng tổng.
- AC-05 (mục 3, Q3): giá trị = Σ quantity × unit_cost có dấu; chỉ Owner; Staff nhận null; Cashier 403.
- AC-06 (Q6, Q14): GET thẻ kho SKU × chi nhánh × kỳ: dòng đầu kỳ, từng dòng sổ (ngày, loại, chứng từ, reference_id để mở truy vết, nhập, xuất, tồn sau), dòng cuối kỳ;
  Owner có giá trị (S10) + dòng chênh lệch làm tròn khi kỳ tới hiện tại; Staff chỉ số lượng (S12).
- AC-07 (Q5, Q11): GET .xlsx cho báo cáo kỳ và thẻ kho, cùng quy tắc che giá, ghi dạng luồng.
- AC-08 FE (FE-13.1, FE-13.2): tab Tồn theo kỳ với bộ chọn Hôm nay/Hôm qua/ngày/Tháng này/Tháng trước/tháng/khoảng, chi nhánh/Tất cả, mở chi tiết loại/kênh, thẻ kho, nút Excel và In;
  Staff không thấy cột giá trị → npm run build PASS.

INVARIANT: chỉ đọc; tenant filter; mốc Asia/Saigon nửa mở; controller không dùng AppDbContext.

DOCS PHẢI CẬP NHẬT: 02-functional-requirements/fr-11-reports-analytics.md và fr-03-inventory-ledger.md (tồn theo kỳ, thẻ kho);
05-api-contracts/03-inventory-endpoints.md; 01-users-and-rbac/02-rbac-matrix.md (dòng báo cáo kỳ); 11-ui-screens/04-inventory-screens.md;
03-non-functional-requirements/01-performance-and-load.md (mục tiêu < 2 giây/trang); báo cáo mới docs/report-after-upgrade/ton-kho-theo-ky-u13.md.

GATE: dotnet build; UnitTests; test mới PostgresInventoryPeriodReportIntegrationTests trên PostgreSQL thật:
đầu + nhập − xuất = cuối (SL và giá trị) mọi SKU; cuối kỳ hôm nay = inventory_balances.OnHand; cuối ngày D = đầu ngày D+1; giao dịch 23:59/00:00 UTC+7;
chuyển kho InTransit vắt qua mốc; Staff null + file Excel không có cột giá trị; tenant khác không thấy; seed ~100.000 dòng sổ, một trang < 2 giây (ghi số đo);
npm run build admin-dashboard.

GIỚI HẠN: chỉ thêm ClosedXML; chỉ commit/push theo mục "KHI LÀM XONG"; một lỗi tối đa 2 lần thử rồi BLOCKED; không nới assertion, không skip test.
BÀN GIAO: làm theo mục "KHI LÀM XONG" (test, commit, push, comment PR), rồi trả bàn giao trong chat theo skills/claude-to-antigravity/assets/handoff.md kèm link comment PR.
```

## Gói 3 — Phiếu xuất kho bán hàng gắn dòng sổ OUT

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.
[Dán LUẬT LÀM SONG SONG ở trên]

TASK: OISMS-66-L2-G3 — Phiếu xuất kho 02-VT lưu kèm dòng sổ OUT | Mức: CRITICAL | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2, commit mới nhất của nhánh khi bắt đầu (ghi lại bằng `git log -1 --format=%h`).

MỤC TIÊU: Mỗi lần POS checkout hoặc xác nhận đơn, lập phiếu xuất kho trong cùng transaction, mỗi dòng sổ OUT bán hàng có đúng một dòng phiếu; in phiếu, in hàng loạt, bảng kê POS theo ngày.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/Pedding/13-ton-kho-theo-ky.md: mục 5 (P1–P6), Q10, Q13; mục 6 (test phiếu xuất); mục 7 BE-13.4→13.6, FE-13.3
- docs/Upgrade-recommended/Done/02-truy-vet-chung-tu-xuat-kho.md (hóa đơn bán lập ở đâu, quy tắc truy vết)
- Mẫu: PosCheckoutService.cs (đoạn lập hóa đơn quanh `_context.SalesInvoices.Add`), OrderService.cs (đoạn tương tự), Features/SalesInvoices/,
  docker/postgres/migrations/20261110_sales_returns_request_key.sql, frontend components/orders/ShippingSlipPrintModal.tsx,
  test-oisms/backend/OISM.IntegrationTests/SalesInvoiceIntegrationTests.cs, SalesInvoiceConcurrencyIntegrationTests.cs, PosCheckoutIntegrationTests.cs
- Skill: skills/postgresql/SKILL.md, skills/csharp-pro/SKILL.md

ĐƯỢC SỬA: migration mới docker/postgres/migrations/20261119_goods_issues.sql; phần bảng mới trong oisms_schema.sql (file chung); docker/postgres/init-oisms.sh;
Domain (entity mới), EF Configuration mới, AppDbContext + IAppDbContext (DbSet, query filter), DatabaseMigrationService.cs (RequiredTables 48 → 50, file chung);
PosCheckoutService.cs, OrderService.cs (chỉ thêm bước lập phiếu cạnh chỗ lập hóa đơn); Features/GoodsIssues/ (mới); controller mới;
InventoryService.cs (DTO sổ cái/truy vết thêm số phiếu xuất); ApiErrorTitles.cs, DependencyInjection.cs (file chung);
FE: component in phiếu mới, pages/OrdersPage.tsx (nút in, in hàng loạt, bảng kê POS), services/types mới; test mới.
KHÔNG ĐƯỢC SỬA: đơn mua, phiếu nhập, ReportService, Pricing, thứ tự khóa hiện có.

ACCEPTANCE CRITERIA:
- AC-01 (P1, P6, Q10): migration tạo goods_issues + goods_issue_items (tenant_id, UNIQUE (tenant_id, number), UNIQUE (tenant_id, inventory_transaction_id), FK đơn/hóa đơn/kho/dòng sổ),
  quyền cho oisms_app; nếu DB đã có dòng inventory_transactions có order_item_id thì RAISE EXCEPTION hướng dẫn reset; chạy 2 lần trên DB trống không lỗi;
  oisms_schema.sql, init-oisms.sh, RequiredTables cùng là 50 bảng.
- AC-02 (P1): POS checkout và Confirm đơn: sau khi ghi dòng OUT, cùng transaction lập phiếu + dòng phiếu (SL, SL gốc, unit_cost = unit_cost dòng sổ); không thêm khóa mới.
- AC-03 (P5, Q13): số PXK-<số hóa đơn>; đơn xuất nhiều kho → PXK-<số hóa đơn>-1, -2… theo thứ tự id kho; tên kho trên phiếu.
- AC-04: replay cùng Idempotency-Key trả kết quả cũ, không thêm phiếu; lỗi giữa chừng rollback, không còn phiếu mồ côi.
- AC-05 (P2, P4): API xem phiếu theo đơn, chi tiết phiếu, bảng kê xuất bán POS theo ngày (UTC+7) + chi nhánh; Owner có đơn giá vốn/thành tiền, Staff null, Cashier 403.
- AC-06: DTO sổ cái và truy vết có số phiếu xuất cho dòng OUT bán hàng.
- AC-07 FE (FE-13.3): đơn đã xác nhận có nút In phiếu xuất kho (mẫu 02-VT, chữ ký người lập/thủ kho/người nhận), chọn nhiều đơn in một lượt, in bảng kê POS theo ngày;
  không tự bật hộp thoại in khi xác nhận; bản Staff không giá → npm run build PASS.

INVARIANT: ledger chỉ INSERT, không đổi cấu trúc inventory_transactions; transaction + thứ tự khóa checkout/confirm giữ nguyên; idempotency; tenant từ JWT; SignalR phát sau commit.

DOCS PHẢI CẬP NHẬT: database/oisms_schema.sql, database/README.md, 04-architecture-and-design/03-database-design.md; số bảng 48 → 50 ở CLAUDE.md, docs/README.md,
docs/07-devops-and-deployment/01-docker-compose-guide.md, docs/13-deploy-aws/04-quy-trinh-deploy-va-cap-nhat.md (kèm bước reset DB demo);
02-functional-requirements/fr-06-omnichannel-orders.md, fr-07-pos-checkout.md, fr-03-inventory-ledger.md; sequence-diagrams/seq-pos-fast-checkout.md, seq-online-reservation.md;
05-api-contracts/04-order-endpoints.md, 05-pos-endpoints.md; 12-error-and-status-codes/02-error-codes-catalog.md (nếu có mã mới);
01-users-and-rbac/02-rbac-matrix.md; 11-ui-screens/05-order-screens.md; báo cáo mới docs/report-after-upgrade/phieu-xuat-kho-u13.md.

GATE: dotnet build; UnitTests; IntegrationTests --filter "PosCheckout|SalesInvoice|OrderLifecycle|Concurrency" trên PostgreSQL thật (stress 50 request/1 SKU phải PASS như cũ);
test mới PostgresGoodsIssueIntegrationTests: mọi OUT bán có đúng 1 dòng phiếu, rollback, replay, nhiều kho, Staff null, Cashier 403, bảng kê khớp tổng xuất POS trong ngày, migration guard;
npm run build admin-dashboard.

GIỚI HẠN: không thêm dependency; chỉ commit/push theo mục "KHI LÀM XONG"; một lỗi tối đa 2 lần thử rồi BLOCKED; không nới assertion, không skip test.
BÀN GIAO: làm theo mục "KHI LÀM XONG" (test, commit, push, comment PR), rồi trả bàn giao trong chat theo skills/claude-to-antigravity/assets/handoff.md kèm link comment PR.
```

---

# Đợt 2 và 3 (lập ngày 21/11/2026)

G1, G2, G3 đã `VERIFIED` (G1: `a4416e8` + `f029c83` + `adf04ae`; G2: `cebf9f3` + `bfda870` + `d979f40` + `e0dc1ed`; G3: `0502705` + `e0dc1ed` + `01821fb`). Ba việc còn lại làm **song song**:

| Việc | Người | Đụng chung |
| --- | --- | --- |
| G4 — Cấu hình giá bán khi nhập kho | Phúc Hữu | G5 cũng sửa DTO giá; G4 chỉ sửa Pricing, PurchaseOrders, GoodsReceiptService |
| G5 — Che giá dùng chung toàn hệ thống | Phương Lê | Không sửa Pricing và GoodsReceiptService.ConfirmReceiptAsync (của G4) |
| Kiểm thử chéo G1–G3 | Nam Nguyễn | Không sửa code; ghi lỗi vào comment PR |

Vẫn dùng mục "Luật làm song song" và "KHI LÀM XONG" ở đầu file. File chung bổ sung cho đợt này: `src/backend/src/OISM.Application/Features/PurchaseOrders/Dtos/PurchaseOrderDtos.cs` (G4 thêm trường cấu hình giá, G5 chuyển phần che giá sang dịch vụ chung).

## Gói 4 — Cấu hình giá bán khi nhập kho

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.
[Dán LUẬT LÀM SONG SONG và KHI LÀM XONG ở đầu file]

TASK: OISMS-66-L2-G4 — Cấu hình giá bán trên đơn mua, sinh giá khi nhập kho, lý do đổi giá, xung đột giá | Mức: CRITICAL | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2, commit mới nhất khi bắt đầu (ghi bằng `git log -1 --format=%h`).

MỤC TIÊU: Owner cấu hình giá bán lẻ/sỉ cho từng đơn vị bán trên dòng đơn mua (Giữ cũ / WAC + lãi / Giá mới; áp ngay khi nhập hoặc từ ngày chọn).
Lúc xác nhận phiếu nhập ĐẦU TIÊN của dòng, trong cùng transaction, hệ thống sinh phiếu đổi giá + dòng giá. Phiếu đổi giá tay có loại lý do bắt buộc và hỏi khi trùng giá chờ từ đơn mua.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/Pedding/12-don-mua-va-cau-hinh-gia.md: "Tóm tắt luồng" B, C bước 4, D; mục 4 (Q15–Q19, Q25, "Mặc định tự chọn"); mục 7 BE-12.7, BE-12.8, FE-12.3, FE-12.4
- docs/Upgrade-recommended/Done/04-bang-gia-theo-thoi-gian.md (bảng giá theo valid_from, phiếu đổi giá, SourceItemId)
- Mẫu: Features/Pricing/Services/PriceChangeService.cs (ApproveAsync tạo SkuUnitPrice), Domain/Entities/PriceChangeRequest.cs, SkuUnitPrice.cs,
  Features/Inventory/Services/GoodsReceiptService.cs (ConfirmReceiptAsync: thứ tự khóa phiếu nhập → đơn mua → SKU, cập nhật WAC),
  Features/PurchaseOrders/Services/PurchaseOrderService.cs (SaveDraft/Approve ở PendingApproval, version),
  Infrastructure/Services/SqlLockService.cs, docker/postgres/migrations/20261119_purchase_order_pending_approval.sql,
  FE components/purchase/*, components/pricing/CreatePriceChangeModal.tsx, PriceManagementModal.tsx
- Test mẫu: test-oisms/backend/OISM.IntegrationTests/PostgresPurchaseOrderU12IntegrationTests.cs
- Skill: skills/postgresql/SKILL.md, skills/csharp-pro/SKILL.md

ĐƯỢC SỬA: migration mới docker/postgres/migrations/20261121_purchase_order_sale_price_configs.sql; oisms_schema.sql, DatabaseMigrationService.cs (RequiredTables 50 → 51),
docker/postgres/init-oisms.sh, số bảng ở CLAUDE.md, README.md, docs/README.md, docs/database/README.md, docs/07-devops-and-deployment/01-docker-compose-guide.md,
docs/13-deploy-aws/04-quy-trinh-deploy-va-cap-nhat.md; Domain (entity cấu hình mới, PriceChangeRequest.ReasonType), EF Configuration, AppDbContext/IAppDbContext;
Features/PurchaseOrders/ (DTO + service: lưu/đọc cấu hình ở PendingApproval, kiểm tra khi Approve); GoodsReceiptService.cs (chỉ thêm bước sinh giá trong ConfirmReceiptAsync);
Features/Pricing/ (lý do, API giá chờ theo SKU, xử lý Q19); ISqlLockService + SqlLockService (khóa giá mới); ApiErrorTitles.cs; DependencyInjection.cs;
FE: components/purchase/* (bảng cấu hình giá bán), services/types đơn mua và pricing, components/pricing/*; test mới.
KHÔNG ĐƯỢC SỬA: dịch vụ che giá dùng chung và DTO ngoài đơn mua/pricing (thuộc G5); GoodsIssue, Reports, OrderService, PosCheckoutService.

ACCEPTANCE CRITERIA:
- AC-01 (BE-12.7, schema): bảng purchase_order_sale_price_configs (tenant_id, purchase_order_item_id, unit_id, price_list_id, mode KEEP|WAC_MARGIN|FIXED,
  margin_percent, fixed_price, price_source SUPPLIER|OWNER, apply_mode ON_RECEIPT|FROM_DATE, apply_from, applied_at, price_change_request_id; UNIQUE theo dòng × đơn vị × bảng giá;
  CHECK theo mode; query filter tenant); price_change_requests.reason_type NOT NULL (dữ liệu cũ = OTHER); migration chạy 2 lần trên DB mới và DB 50 bảng; 51 bảng đồng bộ mọi nơi.
- AC-02 (Q15, Q16, mục 4): Owner lưu/sửa cấu hình ở PendingApproval cho từng đơn vị BÁN của SKU (không tự tính theo hệ số), lẻ và sỉ riêng; mặc định KEEP.
  Đơn chưa có giá bán hoặc WAC = 0 mà chọn WAC_MARGIN thì bắt buộc margin_percent. API trả giá dự kiến = WAC dự kiến ÷ (1 − %), làm tròn LÊN bội 1.000đ,
  margin mặc định = lãi gộp hiện tại của SKU × đơn vị. Giá dự kiến < WAC → cờ cảnh báo; Approve phải có xác nhận bán lỗ, thiếu → 422.
- AC-03 (Q17, Q18, Tóm tắt C bước 4): trong ConfirmReceiptAsync, sau khi cập nhật WAC chi nhánh nhận và trong cùng transaction: với mỗi cấu hình mode ≠ KEEP và applied_at NULL
  của dòng có trong phiếu: tính giá (WAC_MARGIN dùng WAC vừa cập nhật), tạo PriceChangeRequest (Kind Change, Status Approved, reason_type FROM_PURCHASE_ORDER,
  Reason "Theo đơn mua <số>", ReviewedBy = người duyệt đơn mua) + SkuUnitPrice valid_from = max(apply_from, lúc xác nhận); ghi applied_at, price_change_request_id.
  Đợt nhập sau không sinh thêm. Lỗi bất kỳ → rollback cả phiếu nhập.
- AC-04 (Q25): khóa pg_advisory_xact_lock theo SKU + đơn vị + bảng giá (sắp xếp tăng dần), lấy SAU khóa tồn SKU trong ConfirmReceiptAsync và trong duyệt phiếu đổi giá tay.
  Thứ tự: phiếu nhập → đơn mua → tồn SKU → giá. Không đổi thứ tự khóa cũ.
- AC-05 (Q19, BE-12.8): phiếu đổi giá tay bắt buộc reason_type (SHORTAGE | PROMOTION | FROM_PURCHASE_ORDER | OTHER, OTHER cần ghi chú) → thiếu 400.
  API GET giá chờ theo SKU trả các cấu hình chưa áp của đơn mua Approved/PartiallyReceived. Khi Owner tạo/duyệt phiếu đổi giá tay trùng SKU × đơn vị × bảng giá đang chờ:
  request bắt buộc chọn KEEP_MANUAL (cấu hình đơn mua chuyển KEEP) hoặc APPLY_PO_LATER (giữ cấu hình); thiếu → 409 kèm danh sách đơn mua.
- AC-06 (Q9): Staff không thấy cấu hình giá bán, giá dự kiến, margin trên đơn mua (null). Cashier 403 như cũ.
- AC-07 FE (FE-12.3, FE-12.4): bảng cấu hình giá bán theo dòng × đơn vị bán × lẻ/sỉ, giá dự kiến + cảnh báo bán lỗ, ngày áp dụng; hộp xác nhận bán lỗ khi duyệt;
  phiếu đổi giá có chọn loại lý do và hộp thoại Q19 → npm run build PASS.

INVARIANT: ledger chỉ INSERT; WAC qua WacCalculator; giá chỉ thêm dòng theo valid_from, không sửa dòng cũ; transaction + khóa sau khi mở transaction; tenant từ JWT; idempotency giữ nguyên.

DOCS PHẢI CẬP NHẬT: fr-02-product-catalog.md hoặc file FR bảng giá hiện hành (giá sinh từ đơn mua, lý do); fr-04-cost-wac.md; 04-state-machines.md (nếu đổi trạng thái phiếu đổi giá);
05-api-contracts (đơn mua, pricing); 12-error-and-status-codes/02-error-codes-catalog.md; 01-users-and-rbac/02-rbac-matrix.md; 11-ui-screens/03-product-screens.md, 04-inventory-screens.md;
database docs + số bảng 51; Upgrade-recommended/Done/04-bang-gia-theo-thoi-gian.md (ghi bổ sung); báo cáo mới docs/report-after-upgrade/cau-hinh-gia-ban-u12-g4.md.

GATE: dotnet build; UnitTests; test mới PostgresPurchaseOrderSalePriceIntegrationTests trên PostgreSQL thật:
mỗi mode × apply_mode; WAC_MARGIN dùng WAC sau nhập và làm tròn lên 1.000đ; sinh giá đúng 1 lần qua 2 đợt nhập; valid_from = max; lỗi giữa chừng rollback cả phiếu nhập và giá;
đồng thời: xác nhận nhập và duyệt phiếu đổi giá tay cùng SKU chạy song song không deadlock, kết quả đúng lựa chọn Q19; Staff null; migration 2 lần;
IntegrationTests --filter "PurchaseOrder|GoodsReceipt|Price" PASS; npm run build admin-dashboard.

GIỚI HẠN: không thêm dependency; chỉ commit/push theo mục "KHI LÀM XONG"; một lỗi tối đa 2 lần thử rồi BLOCKED; không nới assertion, không skip test.
BÀN GIAO: làm theo mục "KHI LÀM XONG", rồi trả bàn giao trong chat theo skills/claude-to-antigravity/assets/handoff.md kèm link comment PR.
```

## Gói 5 — Che giá dùng chung toàn hệ thống

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.
[Dán LUẬT LÀM SONG SONG và KHI LÀM XONG ở đầu file]

TASK: OISMS-66-L2-G5 — Dịch vụ che giá dùng chung + test quét endpoint Staff | Mức: CRITICAL (RBAC dữ liệu) | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2, commit mới nhất khi bắt đầu (ghi bằng `git log -1 --format=%h`).

MỤC TIÊU: Giá mua, giá vốn, WAC, giá trị tồn, lãi gộp chỉ Owner thấy ở MỌI API, file xuất, bản in, sự kiện SignalR và tool AI. Gom các kiểm tra vai trò cục bộ của G1–G3
về một dịch vụ dùng chung; có test tự quét mọi endpoint bằng tài khoản Staff.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/Pedding/12-don-mua-va-cau-hinh-gia.md: Q9, Q10, Q24, "Tóm tắt luồng" E; mục 7 BE-12.9, FE-12.5
- docs/01-users-and-rbac/02-rbac-matrix.md (Staff đang được xem giá vốn ở truy vết — phải đổi)
- Điểm che hiện có: InventoryController.cs (`canViewCost` đang cho cả Staff), ReportsController.cs (`isOwner`), GoodsIssuesController.cs (`CanViewCost`),
  PurchaseOrderService.cs và GoodsReceiptService.cs (che cục bộ của G1), Features/Reports (G2), Features/Orders/Dtos/OrderDtos.cs (`CostPrice`, `TotalCost` trả cho Staff)
- Phải tự rà thêm bằng CodeGraph/grep các trường cost/Cost/AverageCost/UnitCost/Wac/GrossProfit/Margin/Value trong DTO: truy vết chứng từ (SalesInvoices trace), sổ cái,
  trả hàng (SalesReturns), chuyển kho, kiểm kê, tìm kiếm toàn hệ thống (Features/Search), tool AI (Features/Ai/Services/Tools), payload SignalR (Features/Realtime, Hubs), Excel G2
- Skill: skills/security-auditor/SKILL.md, skills/csharp-pro/SKILL.md

ĐƯỢC SỬA: Application/Common (interface + cài đặt dịch vụ, ví dụ ICostVisibility lấy vai trò từ JWT qua dịch vụ người dùng hiện tại, không nhận từ client);
DTO + service trả giá ở các phân hệ trên (trừ phần cấu hình giá bán và Pricing của G4); controller bỏ biến canViewCost/isOwner cục bộ khi đã chuyển;
payload SignalR; tool AI; frontend: ẩn cột giá vốn/WAC/lãi khi giá trị null ở các trang Orders, SalesInvoices/truy vết, Inventory (tab hiện tại), SalesReturns, StockCounts, Transfers;
test mới; docs liên quan.
KHÔNG ĐƯỢC SỬA: Features/Pricing, GoodsReceiptService.ConfirmReceiptAsync, cấu hình giá bán trên đơn mua (G4); không đổi nghiệp vụ tính toán, chỉ đổi phần trả về.

ACCEPTANCE CRITERIA:
- AC-01: có một dịch vụ quyết định "được xem giá vốn" theo vai trò JWT (chỉ Owner). Mọi chỗ che giá dùng dịch vụ này; không còn `canViewCost = Owner || Staff`.
- AC-02 (Q10): Staff nhận null cho CostPrice/TotalCost/UnitCost/AverageCost/WAC/giá trị tồn/lãi gộp ở: đơn hàng, truy vết chứng từ, sổ cái, tồn hiện tại, trả hàng,
  chuyển kho, kiểm kê, tìm kiếm, phiếu xuất, báo cáo kỳ, thẻ kho, đơn mua, phiếu nhập. Số lượng và giá bán vẫn trả đủ.
- AC-03: file Excel và bản in của Staff không có cột giá vốn/giá trị (kiểm cả nội dung file).
- AC-04: payload SignalR không chứa giá vốn/WAC/giá trị (chỉ id, số lượng, trạng thái).
- AC-05: tool AI trả dữ liệu đã che theo vai trò người hỏi; Staff hỏi giá vốn nhận câu trả lời "không có quyền".
- AC-06: G1 F-03 — Staff lọc `supplierId` trên danh sách đơn mua Draft/PendingApproval không suy ra được NCC (bộ lọc bị bỏ qua hoặc 403).
- AC-07 (test quét): test tích hợp đăng nhập Staff, gọi mọi endpoint GET (lấy danh sách route từ EndpointDataSource) với dữ liệu seed có giá vốn, duyệt JSON đệ quy:
  mọi khóa thuộc danh sách tên nhạy cảm phải null/không có; có danh sách ngoại lệ ghi lý do (ví dụ giá bán). Owner cùng endpoint thấy giá trị khác null.
- AC-08 FE: các trang trên ẩn cột khi giá trị null; Staff không thấy cột rỗng → npm run build PASS.

INVARIANT: không đổi kết quả tính toán; tenant từ JWT; vai trò lấy từ claims đã xác thực; controller không dùng AppDbContext.

DOCS PHẢI CẬP NHẬT: 01-users-and-rbac/02-rbac-matrix.md (Staff không xem giá vốn ở mọi nơi, ghi lịch sử phiên bản); 05-api-contracts (ghi trường null với Staff);
03-non-functional-requirements/02-security-and-isolation.md; fr-13-ai-assistant.md; 11-ui-screens liên quan;
src/backend/src/OISM.Infrastructure/Ai/Knowledge/* nếu nói Staff xem được giá vốn; báo cáo mới docs/report-after-upgrade/che-gia-dung-chung-u12-g5.md.

GATE: dotnet build; UnitTests; test mới PostgresCostVisibilityScanIntegrationTests trên PostgreSQL thật (Staff 0 vi phạm, Owner thấy giá); toàn bộ IntegrationTests PASS
(G5 đụng nhiều DTO — chạy full suite, khoảng 25 phút); npm run build admin-dashboard.

GIỚI HẠN: không thêm dependency; chỉ commit/push theo mục "KHI LÀM XONG"; một lỗi tối đa 2 lần thử rồi BLOCKED; không nới assertion, không skip test.
BÀN GIAO: làm theo mục "KHI LÀM XONG", rồi trả bàn giao trong chat theo skills/claude-to-antigravity/assets/handoff.md kèm link comment PR.
```

## Kiểm thử chéo G1–G3 — Nam Nguyễn

Không sửa code. Chạy trên máy mình, ghi kết quả vào **một** comment PR theo mẫu cuối mục.

```text
Đọc skills/claude-to-antigravity/references/antigravity.md. Đây là việc KIỂM THỬ, không sửa code, không commit.

TASK: OISMS-66-L2-QA — Kiểm thử chéo G1 (đơn mua), G3 (phiếu xuất kho) và hồi quy G2 | Mức: STANDARD
BASELINE: nhánh feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2, commit mới nhất (ghi hash).

CHUẨN BỊ: reset DB local (docker compose down -v CHỈ khi bạn đồng ý mất dữ liệu local) → docker compose up -d --build → đăng nhập Owner, Staff, Cashier của dữ liệu seed.
NGUỒN: docs/Upgrade-recommended/Pedding/12-don-mua-va-cau-hinh-gia.md "Tóm tắt luồng" A–F; 13-ton-kho-theo-ky.md mục 1, 5.

KỊCH BẢN (ghi ĐẠT/LỖI + ảnh chụp hoặc response cho từng dòng):
G1 đơn mua
1. Staff tạo đơn: không có ô NCC, ô giá → Gửi → không sửa được nữa.
2. Owner mở đơn: thấy giá gợi ý + WAC; nhập NCC, giá, phí vận chuyển, chiết khấu, VAT; Lưu nhiều lần; Trả về kèm lý do → Staff thấy lý do, sửa, gửi lại.
3. Owner duyệt: giá vốn sau phân bổ hiện đúng (tính tay 1 ví dụ có phí + chiết khấu). Hai tab cùng duyệt một đơn: tab sau báo xung đột phiên bản.
4. Staff nhận hàng: không thấy giá; nhập 2 đợt; tồn và WAC (xem bằng Owner) đúng công thức với giá sau phân bổ.
5. Menu/URL hóa đơn NCC không còn; API /api/supplier-invoices trả 404.
G3 phiếu xuất kho
6. Bán POS 1 đơn → Admin in phiếu xuất PXK-<số hóa đơn>; Owner thấy giá vốn, Staff không.
7. Đơn Admin/TMĐT xuất từ 2 kho → 2 phiếu -1, -2; in hàng loạt nhiều đơn một lượt.
8. Bảng kê POS theo ngày: tổng SL khớp tab Tồn theo kỳ (xuất bán POS cùng ngày); tổng tiền = tổng hóa đơn.
9. Cashier không vào được phiếu xuất/bảng kê.
G2 hồi quy
10. Tab Tồn theo kỳ: Hôm nay khớp tồn hiện tại; một phiếu chuyển kho đã gửi chưa nhận → chế độ Tất cả có cột đang chuyển và tổng khớp; Excel mở được, Staff không có cột giá trị.

GIỚI HẠN: không sửa code; lỗi thì mô tả bước tái hiện, kỳ vọng, thực tế.
BÀN GIAO: một comment PR (`gh pr comment --body-file <file>`), mẫu:
## Kiểm thử chéo G1–G3 | commit <hash> | <ngày>
| # | Kịch bản | Kết quả | Ghi chú/bằng chứng |
Lỗi tìm thấy: <danh sách, mỗi lỗi: bước tái hiện, kỳ vọng → thực tế, mức P1–P3>
```

---

## Đợt 3 (lập 21/11/2026) — G6: Quyền đơn nháp, thiết lập trước khi duyệt, % lãi

Nguồn: feedback người dùng khi thử trên docker ngày 21/11/2026 (Owner gửi duyệt hộ đơn nháp của Staff; nút Duyệt bấm được khi thiếu NCC/giá, lỗi hiện khuất ở đầu hộp thoại; nhập lãi 100% ra giá như 0%).
Dán kèm "Luật chung" ở đầu file (một phiên Anti duy nhất, mục "FILE CHUNG" vẫn áp dụng).

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-66-L2-G6 — Chỉ người tạo gửi duyệt, Owner phải thiết lập đơn mua trước khi duyệt, % lãi gộp | Mức: CRITICAL (RBAC + chuyển trạng thái) | Prompt 1/1
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2, commit mới nhất khi bắt đầu (`git log -1 --format=%h`).

HIỆN TRẠNG ĐÃ KIỂM (21/11/2026):
- SubmitAsync/UpdateDraftAsync (PurchaseOrderService.cs) bỏ qua kiểm tra người tạo khi người gọi là Owner → Owner sửa và gửi duyệt hộ đơn nháp của Staff. Lệch Q11 (Staff gửi; Owner chỉ Duyệt/Trả về/Hủy).
- PurchaseOrderDetailModal.tsx: Draft hiện nút Sửa/Gửi duyệt cho `isOwner || isCreator`; PendingApproval luôn hiện nút "Duyệt đơn mua" bấm được; handleApprove chặn thiếu NCC/giá nhưng setError hiện ở ĐẦU hộp thoại (người dùng đang ở cuối, tưởng không có gì xảy ra); thông báo nhắc nút "Cập nhật giá & NCC" trong khi nút thật là "Lưu / Sửa NCC & Giá"; giá 0đ dùng window.confirm, bán lỗ dùng modal bật ra SAU khi gọi API.
- Backend ApproveAsync đã chặn thiếu NCC/giá (422), giá 0 và bán lỗ cần xác nhận, khớp version. Staff sửa đơn nháp thì backend xóa NCC và giá (UpdateDraftAsync gán null) → đơn Staff gửi lên luôn thiếu NCC/giá, nên KHÔNG cần thêm cột/migration để "bắt Owner thiết lập".
- SalePriceConfigTable.tsx: % lãi = lãi gộp trên giá bán (Q16: giá = WAC ÷ (1 − %)). Nhập ≥ 100 thì FE lặng lẽ lấy giá = WAC (trông như 0%), backend trả 400 khi lưu. Backend đang cho % âm.

MỤC TIÊU (đã chốt với người dùng):
1. Đơn nháp (Draft) chỉ người tạo được sửa và gửi duyệt. Owner xem đơn nháp của người khác ở chế độ chỉ đọc, vẫn được Hủy (Q11).
2. Owner phải đi qua "Thiết lập đơn mua" trước khi duyệt: checklist + trình hướng dẫn 3 bước, nút Duyệt chỉ ở bước cuối.
3. % lãi: GIỮ công thức Q16 (lãi gộp trên giá bán), chặn 0 ≤ % < 100 ở cả FE và BE, có chế độ nhập "% cộng thêm trên giá vốn" để người dùng gõ 100% = bán gấp đôi (quy đổi sang lãi gộp trước khi lưu, KHÔNG đổi công thức, KHÔNG đổi DB).

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/Pedding/12-don-mua-va-cau-hinh-gia.md: Tóm tắt luồng A, B; Q3, Q4, Q5, Q11; mục 4 (Q15, Q16); mục 7 FE-12.1, FE-12.3
- docs/04-architecture-and-design/04-state-machines.md (đơn mua), docs/01-users-and-rbac/02-rbac-matrix.md (đơn mua)
- Code: Features/PurchaseOrders/Services/PurchaseOrderService.cs (CreateAsync, UpdateDraftAsync, SubmitAsync, ApproveAsync, MapToResponseDtoAsync, ValidateConfigDto),
  Features/PurchaseOrders/Dtos/PurchaseOrderDtos.cs, Domain/Entities/PurchaseOrder.cs, WebApi/Controllers/PurchaseOrdersController.cs,
  FE components/purchase/PurchaseOrderDetailModal.tsx, CreatePurchaseOrderModal.tsx (form "Cập nhật đơn mua"), SalePriceConfigTable.tsx, pages/PurchaseOrdersPage.tsx,
  services/purchaseOrderService.ts, types/purchaseOrder.ts, components/ui (NumberField, Button)
- Test mẫu: test-oisms/backend/OISM.IntegrationTests/PostgresPurchaseOrderU12IntegrationTests.cs, PostgresPurchaseOrderSalePriceIntegrationTests.cs, PurchaseOrderIntegrationTests.cs
- Skill: skills/csharp-pro/SKILL.md, skills/postgresql/SKILL.md

ĐƯỢC SỬA: các file code ở NGUỒN CẦN ĐỌC (BE PurchaseOrders + FE purchase), ApiErrorTitles.cs nếu thêm mã lỗi, test mới/sửa test đơn mua, docs liệt kê ở mục DOCS.
KHÔNG ĐƯỢC SỬA: GoodsReceiptService, Pricing (trừ khi chỉ đọc), Inventory/Reports/Orders; KHÔNG thêm migration, KHÔNG đổi schema.

ACCEPTANCE CRITERIA:
A. Quyền đơn nháp
- AC-01 (BE): SubmitAsync chỉ cho `po.CreatedBy == userId` (mọi vai trò). Người khác (kể cả Owner) → 403 mã `PURCHASE_ORDER_NOT_CREATOR`
  (thêm vào ApiErrorTitles + catalog). UpdateDraftAsync với đơn Draft: chỉ người tạo sửa được, người khác → 403 cùng mã.
  Owner vẫn sửa được đơn PendingApproval (Q4) và Hủy được Draft của bất kỳ ai (Q11). Đơn do Owner tạo, bị Trả về thành Draft thì Owner là người tạo → sửa/gửi bình thường.
- AC-02 (FE): Draft của người khác: Owner thấy nhãn "Staff đang soạn — chỉ xem", KHÔNG có nút Sửa/Gửi duyệt, chỉ có Hủy và Đóng. Người tạo thấy Sửa/Gửi duyệt/Hủy như cũ.
B. Thiết lập trước khi duyệt
- AC-03 (BE): PurchaseOrderResponseDto thêm `approvalReadiness` (chỉ Owner, Staff = null):
  { isReady, missing: ["ITEMS"|"SUPPLIER"|"UNIT_PRICE"|"ORDER_DATE"], hasZeroPrice, hasLossPrice }.
  Tính bằng MỘT hàm dùng chung với ApproveAsync (tách hàm kiểm tra điều kiện duyệt; ApproveAsync gọi lại hàm này) để FE và BE không lệch luật.
  Hành vi ApproveAsync giữ nguyên (422/409/xác nhận giá 0, bán lỗ, version).
- AC-04 (FE): Đơn PendingApproval, vai trò Owner: đầu hộp thoại có checklist ✓/✗: NCC, giá mua đủ mọi dòng, ngày đặt hợp lệ, đã xem cấu hình giá bán.
  Nút chính "Thiết lập đơn mua". Nút "Duyệt đơn mua" ở màn chi tiết bị khóa (disabled + dòng lý do ngay cạnh nút) cho tới khi `approvalReadiness.isReady`.
- AC-05 (FE): "Thiết lập đơn mua" mở trình hướng dẫn 3 bước:
  B1 NCC, ngày đặt, giá từng dòng (giá gợi ý + WAC), phí/chiết khấu/VAT → "Tiếp" = lưu (PUT, gửi version) rồi mới sang B2; lỗi hiện tại bước.
  B2 cấu hình giá bán (SalePriceConfigTable) → "Tiếp" = lưu cấu hình; đánh dấu "đã xem cấu hình giá bán" (trạng thái FE trong phiên, không lưu DB).
  B3 Xác nhận: tóm tắt tổng tiền hàng, phí, chiết khấu, VAT, giá vốn sau phân bổ từng dòng; nếu hasZeroPrice/hasLossPrice thì hiện ô tích xác nhận NGAY trên màn
  (bỏ window.confirm và modal bật ra sau); nút "Duyệt đơn mua" chỉ bật khi đã tích đủ → gọi approve với confirmZeroPrice/confirmLossPrice/version.
  Đóng giữa chừng không mất dữ liệu đã lưu ở bước trước. Bị 409 version → báo "Đơn vừa được sửa ở nơi khác, tải lại" và tải lại.
- AC-06 (FE): Mọi thông báo lỗi của hộp thoại chi tiết hiện ngay trên thanh nút (hoặc cuộn tới chỗ lỗi); sửa tên nút trong thông báo cho khớp nút thật.
C. % lãi gộp
- AC-07 (BE): ValidateConfigDto: WAC_MARGIN bắt buộc 0 ≤ marginPercent < 100, ngoài khoảng → 400 thông điệp rõ "Lãi gộp tính trên giá bán, phải từ 0% đến dưới 100%".
  (Muốn bán dưới giá vốn thì dùng "Giá mới" — cảnh báo bán lỗ vẫn như cũ.) Kiểm tra test G4 hiện có không dùng % âm; nếu có thì báo lại, không tự nới.
- AC-08 (FE): Ô WAC + lãi có công tắc "Nhập theo: Lãi gộp trên giá bán | Cộng thêm trên giá vốn".
  Chế độ cộng thêm: người dùng nhập k (k ≥ 0, cho phép > 100), FE quy đổi lãi gộp m = k ÷ (100 + k) × 100 (làm tròn 4 chữ số) để lưu; ví dụ 100% cộng thêm → 50% lãi gộp → giá = WAC × 2 (trước làm tròn lên 1.000đ).
  Chế độ lãi gộp: chặn ≥ 100 và < 0 ngay trên ô (viền đỏ + dòng lỗi), KHÔNG tính giá, ô giá dự kiến hiện "—", nút lưu bị khóa.
  Dưới ô luôn hiện dòng quy đổi: "Lãi gộp m% = cộng thêm k% trên giá vốn" (k = m ÷ (100 − m) × 100).
  Giá dự kiến trên FE phải khớp giá BE tính (cùng công thức, làm tròn lên 1.000đ).

INVARIANT: không đổi công thức giá Q16, không đổi DB/migration, không đổi luồng nhập kho/sinh giá của G4, tenant từ JWT, Staff vẫn không thấy giá (G5), version/khóa đơn mua giữ nguyên.

DOCS PHẢI CẬP NHẬT: 12-don-mua-va-cau-hinh-gia.md (thêm Q27 quyền đơn nháp, Q28 thiết lập trước khi duyệt, Q29 % lãi + chế độ cộng thêm; sửa Tóm tắt luồng A, B);
04-state-machines.md (Submit chỉ người tạo); 02-rbac-matrix.md (Owner xem Draft của người khác chỉ đọc); 05-api-contracts đơn mua (approvalReadiness, 403 PURCHASE_ORDER_NOT_CREATOR, khoảng margin);
12-error-and-status-codes/02-error-codes-catalog.md; 11-ui-screens màn đơn mua (checklist + 3 bước); báo cáo mới docs/report-after-upgrade/thiet-lap-duyet-don-mua-u12-g6.md.

GATE:
- dotnet build src/backend/OISM.sln; UnitTests PASS.
- Test mới PostgresPurchaseOrderG6IntegrationTests trên PostgreSQL thật:
  (1) Owner gửi duyệt/sửa Draft của Staff → 403 PURCHASE_ORDER_NOT_CREATOR, đơn không đổi; Owner Hủy Draft của Staff → OK; Staff gửi đơn của mình → OK;
  (2) đơn Owner tạo, Trả về, Owner sửa/gửi lại → OK;
  (3) approvalReadiness: đơn Staff vừa gửi → isReady=false, missing có SUPPLIER, UNIT_PRICE; Owner lưu đủ → isReady=true; có giá 0 → hasZeroPrice; cấu hình lỗ → hasLossPrice; Staff nhận null;
  (4) approve khi isReady=false → 422 (giữ hành vi cũ);
  (5) margin -1, 100, 150 → 400; 0 và 99,99 → lưu được; 50 → giá = ceil(WAC ÷ 0,5 / 1000) × 1000.
- **Chạy TOÀN BỘ IntegrationTests (khoảng 22 phút) trên DB PostgreSQL mới** — đợt 2 CI đỏ vì chỉ chạy filter. Có test cũ kỳ vọng Owner gửi hộ đơn nháp thì sửa theo Q11/AC-01 và ghi rõ trong báo cáo, không xóa test.
- npm run build admin-dashboard.
- Thử tay trên docker local (docker compose up -d --build, KHÔNG down -v): Staff tạo + gửi; Owner mở đơn nháp Staff thấy chỉ xem; Owner đi 3 bước và duyệt; nhập 100% ở chế độ cộng thêm ra giá gấp đôi WAC; nhập 100 ở chế độ lãi gộp bị chặn. Ghi kết quả vào báo cáo PR.

GIỚI HẠN: không thêm dependency; chỉ commit/push theo mục "KHI LÀM XONG"; một lỗi tối đa 2 lần thử rồi BLOCKED; không nới assertion, không skip test.
BÀN GIAO: làm theo mục "KHI LÀM XONG", rồi trả bàn giao trong chat theo skills/claude-to-antigravity/assets/handoff.md kèm link comment PR.
```
