# OISMS-66 — Upgrade 14: plan và prompt giao Antigravity

Đặc tả: [14-in-chung-tu-so-kho-va-pos.md](../../../docs/Upgrade-recommended/Done/14-in-chung-tu-so-kho-va-pos.md). Lập 21/11/2026.

## Plan

Định tuyến **CHIA** thành 4 task. Lý do: hơn 8 AC, trải ba tầng (backend, Admin, POS), có phần quyền Cashier (CRITICAL) và seed chạy qua luồng ghi sổ thật.

| Task | Nội dung | Mức | Phụ thuộc | Trần vòng sửa |
| --- | --- | --- | --- | --- |
| U14-A | Backend: endpoint phiếu xuất cho Cashier, che giá Staff ở API chi tiết, API phiếu điều chỉnh, trường in, test PostgreSQL | CRITICAL | — | 1 |
| U14-B | Admin: nút Chứng từ ở Sổ kho/Thẻ kho, 6 mẫu in mới, truy vết Xem/In và In tất cả | STANDARD | U14-A | 2 |
| U14-C | POS: nút In phiếu xuất kho sau checkout, mẫu A4/A5 02-VT | STANDARD | U14-A | 2 |
| U14-D | Seed chứng từ qua service thật, runbook reset local, rebuild Docker, kiểm chứng cuối | CRITICAL | U14-A, B, C | 1 |

Thứ tự gửi: A → (B, C chạy song song được vì khác file) → D. Mỗi task tối đa 3 prompt gửi Anti.

**Việc Claude giữ:** review từng bàn giao, tự chạy lại gate PostgreSQL của A và D, cập nhật `checklist.md`, `docs/Upgrade-recommended/README.md` và chuyển đặc tả sang `Done/` sau khi VERIFIED.

**Việc người dùng làm:** chuyển prompt; ở U14-D tự chạy lệnh xóa volume local (`docker compose down -v`) khi Anti báo sẵn sàng; đồng ý commit.

**Môi trường test PostgreSQL:** đặt `OISM_TEST_HOST`, `OISM_TEST_PORT`, `OISM_TEST_ADMIN_USER`, `OISM_TEST_ADMIN_PASSWORD`, `OISM_TEST_APP_USER`, `OISM_TEST_APP_PASSWORD` (xem `.github/workflows/ci.yml`).

## Prompt U14-A — Backend

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-66-U14-A — API in chứng từ và quyền Cashier   | Mức: CRITICAL | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2, commit 68090d1. Giữ nguyên các thay đổi chưa commit có sẵn (PurchaseOrderService, PurchaseOrderDetailModal, PurchaseOrderSetupWizardModal, PostgresPurchaseOrderG6IntegrationTests, docs Upgrade 12).

MỤC TIÊU: Backend cung cấp đủ dữ liệu in cho 8 loại chứng từ, Cashier đọc được phiếu xuất của đơn mình tạo, giá vốn và giá nhập bị che đúng vai trò ngay ở dữ liệu trả về.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/Pedding/14-in-chung-tu-so-kho-va-pos.md (mục 3 Q2, Q3, Q7, Q9; mục 4 Backend)
- Mẫu: src/backend/src/OISM.WebApi/Controllers/GoodsIssuesController.cs, src/backend/src/OISM.Application/Features/GoodsIssues/Services/GoodsIssueService.cs (cách dùng ICostVisibilityService)
- Mẫu test: test-oisms/backend/OISM.IntegrationTests/PostgresCostVisibilityScanIntegrationTests.cs
- InventoryAdjustment, InventoryAdjustmentItem (OISM.Domain/Entities); InventoryService.cs (map DocumentType sổ kho)

ĐƯỢC SỬA: OISM.Application/Features/{GoodsIssues,Inventory,StockTransfers,StockCounts,SalesReturns,GoodsReceipts}, OISM.WebApi/Controllers liên quan, DependencyInjection.cs nếu thêm service, test-oisms/backend/OISM.IntegrationTests (file test mới), docs liệt kê dưới.
KHÔNG ĐƯỢC SỬA: schema/migration, luồng ghi kho (checkout, confirm, nhập, chuyển, kiểm kê, trả), frontend, OismsDatabaseSeeder.

ACCEPTANCE CRITERIA:
- AC-01 (Q2): GET /api/pos/orders/{orderId}/goods-issues, [Authorize(Roles="Owner,Staff,Cashier")]. Cashier chỉ nhận phiếu khi Order.CreatedBy = user hiện tại; đơn Cashier khác hoặc tenant khác → 404 GOODS_ISSUE_NOT_FOUND → test PostgreSQL
- AC-02 (Q3): Cashier luôn nhận UnitCost/TotalCost = null ở mọi dòng và tổng → test PostgreSQL
- AC-03 (Q7): Staff nhận null mọi trường giá nhập/giá vốn ở GET chi tiết phiếu nhập, chuyển kho, kiểm kê, trả hàng, phiếu điều chỉnh; Owner nhận đủ → test PostgreSQL
- AC-04 (Q9): GET /api/inventory/adjustments/{id} (Owner,Staff), tenant theo JWT, tenant khác 404, có số phiếu, lý do, chiều, dòng SKU, đơn vị gốc, người lập → test PostgreSQL
- AC-05 (Q9): DTO chi tiết bổ sung trường in còn thiếu (tên người lập/duyệt/nhận, NCC, tên đơn vị, số chứng từ liên quan như đơn mua của phiếu nhập) mà không đổi nghĩa trường cũ → build + test hiện có PASS
- AC-06: Thêm route mới vào PostgresCostVisibilityScanIntegrationTests (quét GET Staff) và PASS
- AC-07: Controller chỉ điều phối HTTP, không dùng AppDbContext; lỗi theo ProblemDetails + errorCode

INVARIANT PHẢI GIỮ: tenant chỉ từ JWT (ICurrentTenantService), không IgnoreQueryFilters; API chỉ đọc, không mở transaction; che giá qua ICostVisibilityService, không tự kiểm vai trò rải rác.

DOCS PHẢI CẬP NHẬT:
- docs/05-api-contracts/ — 2 endpoint mới, trường mới, hành vi che giá
- docs/01-users-and-rbac/02-rbac-matrix.md — Cashier đọc phiếu xuất đơn mình; ngoại lệ Staff không thấy giá trên API in
- docs/12-error-and-status-codes/02-error-codes-catalog.md — nếu thêm mã lỗi (vd ADJUSTMENT_NOT_FOUND)
- Báo cáo: docs/report-after-upgrade/in-chung-tu-so-kho-pos-u14.md (mục Backend: đã làm, ảnh hưởng chức năng hiện có, không đổi DB, kiểm chứng, giới hạn)

GATE:
- dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal
- dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false
- dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --filter "FullyQualifiedName~U14|FullyQualifiedName~CostVisibility|FullyQualifiedName~GoodsIssue" (PostgreSQL thật, biến OISM_TEST_*)

GIỚI HẠN: Không commit/push. Không thêm dependency, không migration. Một lỗi tối đa 2 lần thử rồi BLOCKED. Không nới assertion, không skip test.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```

## Prompt U14-B — Admin

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-66-U14-B — Nút Chứng từ ở sổ kho và in từ truy vết   | Mức: STANDARD | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2 sau khi U14-A VERIFIED.

MỤC TIÊU: Mỗi dòng sổ kho mở được chứng từ gốc để xem và in; màn truy vết in được từng chứng từ và in gộp.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/Pedding/14-in-chung-tu-so-kho-va-pos.md (Q5–Q8, Q10, mục 4 Frontend Admin)
- docs/05-api-contracts/ phần U14-A vừa cập nhật
- Mẫu: src/frontend/admin-dashboard/src/components/orders/GoodsIssuePrintModal.tsx, components/print/PrintArea.tsx, components/salesInvoices/SalesInvoiceReprintModal.tsx
- Màn sửa: pages/InventoryPage.tsx (tab ledger), components/inventory/StockCardModal.tsx, components/salesInvoices/SalesInvoiceTraceModal.tsx, services/apiAdapter.ts (map ledger ~dòng 580), types/domain.ts (LedgerEntry)
- Mẫu test: test-oisms/frontend/admin-dashboard/test-ai-chat-format.mjs

ĐƯỢC SỬA: src/frontend/admin-dashboard/src/**, test-oisms/frontend/admin-dashboard/ (file test mới), docs dưới.
KHÔNG ĐƯỢC SỬA: backend, pos-app.

ACCEPTANCE CRITERIA:
- AC-01 (Q6): hàm thuần resolveLedgerDocument(entry) trả đúng mẫu cho mọi dòng bảng Q6, kể cả Order thiếu goodsIssueId và Unknown → node --test test-oisms/frontend/admin-dashboard/test-ledger-document.mjs
- AC-02 (Q5): LedgerEntry giữ documentType, documentId, documentNumber, goodsIssueId, salesInvoiceId; tab Sổ kho và Thẻ kho có nút "Chứng từ" mở DocumentPrintModal (xem trước + In) → build + ảnh chụp
- AC-03 (Q6): 6 mẫu mới trong components/print/: phiếu nhập 01-VT (có link đơn mua), xuất kho kiêm vận chuyển nội bộ, nhập kho nhận chuyển, biên bản kiểm kê 05-VT (tô nổi SKU đang bấm), nhập kho hàng trả lại, phiếu nhập/xuất điều chỉnh
- AC-04 (Q7): cột giá ẩn khi API trả null (Staff), không tự suy giá ở frontend
- AC-05 (Q8): SalesInvoiceTraceModal có Xem/In ở mỗi bước và "In tất cả" (mỗi chứng từ sang trang mới, dùng PrintArea)
- AC-06 (Q10): chân trang "Ngày in" giờ UTC+07:00 và tên người in
- AC-07: trạng thái đang tải, lỗi 403/404, Unknown vô hiệu nút có tooltip

DOCS PHẢI CẬP NHẬT:
- docs/11-ui-screens/04-inventory-screens.md, docs/11-ui-screens/05-order-screens.md — nút Chứng từ, truy vết
- Báo cáo docs/report-after-upgrade/in-chung-tu-so-kho-pos-u14.md — mục Admin

GATE:
- npm run build --prefix src/frontend/admin-dashboard
- node --test test-oisms/frontend/admin-dashboard/test-ledger-document.mjs test-oisms/frontend/admin-dashboard/test-ai-chat-format.mjs
- Ảnh xem trước từng mẫu với tài khoản Owner và Staff (ghi rõ nếu chưa có dữ liệu loại nào, NOT RUN kèm lý do; U14-D sẽ seed đủ)

GIỚI HẠN: Không commit/push. Không thêm dependency. Một lỗi tối đa 2 lần thử rồi BLOCKED. Không nới assertion.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```

## Prompt U14-C — POS

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-66-U14-C — Thu ngân in phiếu xuất kho   | Mức: STANDARD | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2 sau khi U14-A VERIFIED.

MỤC TIÊU: Sau checkout thành công, thu ngân bấm "In phiếu xuất kho" để in phiếu 02-VT của đơn, mỗi kho một trang, không có giá vốn.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/Pedding/14-in-chung-tu-so-kho-va-pos.md (Q1–Q4, mục 4 Frontend POS)
- docs/05-api-contracts/ phần GET /api/pos/orders/{orderId}/goods-issues
- Mẫu: src/frontend/pos-app/src/components/pos/ReceiptPrintModal.tsx, src/frontend/admin-dashboard/src/components/orders/GoodsIssuePrintModal.tsx (bố cục 02-VT)
- src/frontend/pos-app/src/pages/PosPage.tsx, services/posApi.ts (checkout trả orderId ~dòng 223), types/pos.ts (ReceiptData)
- Test: test-oisms/frontend/pos-app/test-receipt.mjs

ĐƯỢC SỬA: src/frontend/pos-app/src/**, test-oisms/frontend/pos-app/ (thêm test, thêm vào script test trong package.json), docs dưới.
KHÔNG ĐƯỢC SỬA: backend, admin-dashboard.

ACCEPTANCE CRITERIA:
- AC-01 (Q1): ReceiptData/kết quả checkout giữ orderId; nút "In phiếu xuất kho" cạnh "In hóa đơn" chỉ hiện khi checkout thành công (không hiện ở phiếu tạm tính, lỗi, kết quả chưa rõ)
- AC-02 (Q4): modal A4/A5 mẫu 02-VT, không có cột giá vốn, nhiều kho thì mỗi kho một trang, in một lần
- AC-03: 404 hiện thông báo "Không tìm thấy phiếu xuất kho của đơn này", lỗi mạng cho thử lại; không gọi lại checkout
- AC-04 (Gói 4): tự in biên nhận đúng một lần vẫn giữ (shouldAutoPrint không đổi); test-receipt.mjs PASS
- AC-05: test mới cho điều kiện hiện nút và tách trang theo kho → node --test

DOCS PHẢI CẬP NHẬT:
- docs/02-functional-requirements/fr-07-pos-checkout.md — in phiếu xuất kho
- Báo cáo docs/report-after-upgrade/in-chung-tu-so-kho-pos-u14.md — mục POS

GATE:
- npm run build --prefix src/frontend/pos-app
- npm run lint --prefix src/frontend/pos-app
- npm test --prefix src/frontend/pos-app

GIỚI HẠN: Không commit/push. Không thêm dependency. Một lỗi tối đa 2 lần thử rồi BLOCKED. Không nới assertion.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```

## Prompt U14-D — Seed, reset và rebuild Docker

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-66-U14-D — Seed chứng từ demo, reset local, rebuild Docker   | Mức: CRITICAL | Prompt 1/3
BASELINE: branch feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2 sau khi U14-A, B, C VERIFIED.

MỤC TIÊU: DB local sau reset có dữ liệu demo đủ 6 loại chứng từ nguồn sổ kho, tạo qua service thật, để kiểm tra mọi mẫu in.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/Pedding/14-in-chung-tu-so-kho-va-pos.md (Q11–Q13, mục 4 Seed và Docker, mục 5)
- src/backend/src/OISM.Infrastructure/Persistence/OismsDatabaseSeeder.cs, DemoSeedPolicy.cs; Program.cs (~dòng 195–215)
- CurrentTenantService.SetTenant (cách job Hangfire đặt tenant); các service: PurchaseOrderService, GoodsReceiptService, PosCheckoutService, OrderService, StockTransferService, StockCountService, SalesReturnService
- docker-compose.yml, docker/postgres/init-oisms.sh, docs/07-devops-and-deployment/01-docker-compose-guide.md

ĐƯỢC SỬA: Infrastructure/Persistence (seeder chứng từ mới, ví dụ OismsDocumentSeeder.cs), Program.cs (gọi sau seed danh mục), test-oisms/backend/OISM.IntegrationTests (test seeder), docs dưới.
KHÔNG ĐƯỢC SỬA: logic nghiệp vụ trong các service, schema, DemoSeedPolicy (giữ chỉ Development/Test).

ACCEPTANCE CRITERIA:
- AC-01 (Q12): mỗi tenant demo có: đơn mua duyệt → phiếu nhập xác nhận, POS checkout, đơn online confirm, chuyển kho 2 bước, kiểm kê có thừa và thiếu được duyệt, trả hàng một phần, phiếu điều chỉnh nếu có luồng tạo được (không có thì ghi rõ). Tất cả gọi service thật, không INSERT sổ kho trực tiếp
- AC-02 (Q12): idempotent: chạy seeder hai lần trên DB trống, lần hai không thêm chứng từ hay dòng inventory_transactions → test PostgreSQL
- AC-03: sau seed, mọi dòng sổ có DocumentType khác Unknown; dòng OUT bán có GoodsIssueId; on_hand = balance_after cuối mỗi chi nhánh+SKU; Available không âm → test PostgreSQL
- AC-04 (Q11): KHÔNG tự chạy docker compose down -v. Viết runbook reset local; dừng lại, báo người dùng chạy lệnh xóa volume, chờ xác nhận rồi mới chạy tiếp
- AC-05: docker compose up -d --build; /health/ready 200; log có dòng seed thành công; đăng nhập tài khoản demo Owner thấy sổ kho đủ 6 loại nguồn; mở thử mỗi loại chứng từ (ảnh chụp)
- AC-06 (Q13): Production không seed (DemoSeedPolicy không đổi) → test hiện có PASS

INVARIANT PHẢI GIỮ: ledger append-only; transaction + khóa đi qua service sẵn có; tenant đặt qua SetTenant trong scope riêng từng tenant; không đọc/in .env.

DOCS PHẢI CẬP NHẬT:
- docs/07-devops-and-deployment/01-docker-compose-guide.md — reset local, seed chứng từ, tài khoản demo
- Báo cáo docs/report-after-upgrade/in-chung-tu-so-kho-pos-u14.md — mục Seed/Docker, kiểm chứng tổng, việc treo: reset AWS (Q13)

GATE:
- dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal
- dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false
- dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false (full suite, PostgreSQL thật)
- npm run build --prefix src/frontend/admin-dashboard; npm run build/lint/test --prefix src/frontend/pos-app
- Docker local như AC-05

GIỚI HẠN: Không commit/push. Không migration. Không chạy lệnh xóa dữ liệu. Một lỗi tối đa 2 lần thử rồi BLOCKED. Không nới assertion.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```
