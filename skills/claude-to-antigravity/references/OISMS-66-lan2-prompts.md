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
