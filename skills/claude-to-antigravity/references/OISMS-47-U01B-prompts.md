# Prompt giao Anti — OISMS-47-U01B

Gửi tuần tự: P1, rồi P2 sau khi P1 được VERIFIED. Mỗi lần chỉ dán một prompt. Sổ task: `tmp/claude-anti/OISMS-47-U01B.md`.

## P1 — Backend hóa đơn nhà cung cấp và chênh lệch

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-47-U01B-P1 — Backend hóa đơn NCC, đối chiếu ba bên, chênh lệch | Mức: CRITICAL | Prompt 1/3
BASELINE: branch feature/OISMS-58-nghiem-thu-sprint-5, commit 7e2e751. Giữ nguyên mọi thay đổi docs chưa commit có sẵn.

MỤC TIÊU: Nhập hóa đơn nhà cung cấp gắn 1 phiếu nhập, tự đối chiếu với phiếu mua và số thực nhận, chênh lệch chờ Owner duyệt. Hóa đơn kèm hàng đã duyệt thì phiếu nhập xác nhận theo giá hóa đơn; hóa đơn đến sau chỉ lưu bằng chứng, không đổi WAC/sổ kho. Không làm công nợ, thanh toán.

NGUỒN CẦN ĐỌC (chỉ các mục này):
- docs/Upgrade-recommended/Done/01-mua-hang-nhap-kho-gia-von.md: "Quyết định 01-B (chốt 12/11/2026)" và "Tiêu chí nghiệm thu đề xuất" 9–12
- docs/database/oisms_schema.sql: purchase_orders (kể cả trigger oisms_purchase_order_guard, purchase_order_no_delete), goods_receipts, goods_receipt_items
- Mẫu code bắt chước (đường dẫn OISM.* tính từ src/backend/src/):
  - Entity, trạng thái: OISM.Domain/Entities/PurchaseOrder.cs, OISM.Domain/Common/DocumentStatuses.cs
  - Service, idempotency: OISM.Application/Features/PurchaseOrders/Services/PurchaseOrderService.cs
  - Xác nhận nhập, WAC, khóa: OISM.Application/Features/Inventory/Services/GoodsReceiptService.cs (ConfirmReceiptAsync)
  - Controller, lỗi: OISM.WebApi/Controllers/PurchaseOrdersController.cs, OISM.WebApi/Http/ApiErrorTitles.cs, ApiErrorContractAttribute.cs
  - Migration: docker/postgres/migrations/20261102_purchase_orders.sql
  - Test: test-oisms/backend/OISM.IntegrationTests/PurchaseOrderIntegrationTests.cs, PurchaseOrderSeed.cs
- Skill kỹ thuật: skills/postgresql/SKILL.md, skills/csharp-pro/SKILL.md

ĐƯỢC SỬA:
- docker/postgres/migrations/20261112_supplier_invoices.sql (mới), docs/database/oisms_schema.sql, docker/postgres/init-oisms.sh (44 → 47)
- OISM.Domain (entity mới, DocumentStatuses), OISM.Infrastructure (Configurations, AppDbContext, SqlLockService, DatabaseMigrationService)
- OISM.Application/Common/Interfaces (IAppDbContext, ISqlLockService), Features/SupplierInvoices/ (mới), Features/Inventory/Services/GoodsReceiptService.cs, Dtos của phiếu nhập, DependencyInjection.cs
- OISM.WebApi/Controllers/SupplierInvoicesController.cs (mới), OISM.WebApi/Http/*
- Các file docs trong mục DOCS PHẢI CẬP NHẬT bên dưới
- test-oisms/backend/ (test mới, helper dùng chung)
KHÔNG ĐƯỢC SỬA: frontend, migration cũ, WacCalculator, ReportService, OrderService/POS, ràng buộc của inventory_transactions, tài liệu lịch sử (docs/08-sprints, docs/09-personal-reports, báo cáo cũ trong docs/report-after-upgrade), checklist.md và docs/Upgrade-recommended (Claude cập nhật sau khi VERIFIED).

ACCEPTANCE CRITERIA:
- AC-01 Schema: 3 bảng supplier_invoices, supplier_invoice_items, purchase_variances, đều có tenant_id, FK kèm tenant_id như purchase_orders.
  supplier_invoices: goods_receipt_id, supplier_id, invoice_series, invoice_number, invoice_date, subtotal, vat_amount, shipping_fee, total_amount, status, request_key, created_by, submitted_at, cancelled_by/at, cancel_reason.
  supplier_invoice_items: invoice_id, goods_receipt_item_id, sku_id, quantity, unit_price (trước thuế, theo đơn vị dòng phiếu mua), expected_quantity, expected_unit_price, quantity_diff, price_diff, amount_diff.
  purchase_variances: invoice_id (UNIQUE), goods_receipt_id, variance_type (AtReceipt/AfterReceipt), total_amount_diff, status (Pending/Approved/Rejected), decided_by/at, decision_note.
  Ràng buộc: UNIQUE(tenant_id, request_key); UNIQUE(tenant_id, supplier_id, invoice_series, invoice_number) WHERE status <> 'Cancelled'; UNIQUE(tenant_id, goods_receipt_id) WHERE status NOT IN ('Cancelled','Rejected'); UNIQUE(tenant_id, invoice_id, goods_receipt_item_id); CHECK trạng thái, số không âm; trigger chặn DELETE như purchase_order_no_delete; trigger chặn sửa dòng khi header không còn Draft. Migration chạy 2 lần không lỗi; RequiredTables, phần kiểm tra trigger/FK trong DatabaseMigrationService, init-oisms.sh = 47.
- AC-02 Domain/Infra: entity SupplierInvoice, SupplierInvoiceItem, PurchaseVariance (IMustHaveTenant, query filter); SupplierInvoiceStatus = Draft, Matched, PendingApproval, Approved, Rejected, Cancelled; PurchaseVarianceStatus, PurchaseVarianceType; ISqlLockService.LockSupplierInvoiceAsync (pg_advisory_xact_lock).
- AC-03 Tạo/sửa/gửi: Create (bắt buộc Idempotency-Key; cùng key cùng payload trả kết quả cũ, khác payload 409 IDEMPOTENCY_PAYLOAD_MISMATCH như PurchaseOrderService) tạo Draft từ goodsReceiptId; mỗi dòng phải thuộc phiếu nhập, supplier trùng phiếu nhập; phiếu nhập Cancelled thì 409. Update chỉ khi Draft. Submit: chụp expected_quantity = số thực nhận của dòng phiếu nhập, expected_unit_price = PurchaseOrderItem.UnitPrice, tính diff; mọi diff = 0 → Matched, ngược lại → PendingApproval + tạo purchase_variance Pending, variance_type = AtReceipt nếu phiếu nhập Draft, AfterReceipt nếu Confirmed. Thuế, phí ship không tính vào diff.
- AC-04 Duyệt/từ chối/hủy: Approve, Reject chỉ Owner (Staff 403), chỉ khi PendingApproval; Reject bắt buộc decision_note; Owner tự nhập vẫn phải gọi Approve riêng. Reject loại AtReceipt: cùng transaction hủy phiếu nhập nháp (dùng GoodsReceipt.Cancel). Cancel (Owner, Staff) cần lý do, từ Draft/Matched/Approved; KHÔNG được làm đổi giá vốn.
- AC-05 ConfirmReceiptAsync, thứ tự khóa BẮT BUỘC: LockGoodsReceiptAsync → (đọc hóa đơn còn hiệu lực của phiếu, nếu có) LockSupplierInvoiceAsync rồi đọc lại → LockPurchaseOrderAsync → khóa SKU như hiện tại. Hóa đơn Draft hoặc PendingApproval → 409 GOODS_RECEIPT_INVOICE_PENDING. Approved → trước khi tính WAC/ghi IN, gán GoodsReceiptItem.UnitPrice = unit_price hóa đơn của dòng đó (WAC và UnitCost dòng IN theo giá này). Matched hoặc không có hóa đơn → như 01-A. Mọi thao tác ghi của hóa đơn (create, update, submit, approve, reject, cancel) cũng khóa phiếu nhập trước rồi mới khóa hóa đơn.
- AC-06 Hóa đơn đến sau (phiếu Confirmed): submit/approve/reject/cancel không sinh InventoryTransaction, không đổi InventoryBalance.AverageCost/OnHand, không đổi GoodsReceiptItem.UnitPrice.
- AC-07 API + lỗi + docs: SupplierInvoicesController route api/supplier-invoices: GET / (lọc supplierId, status, ngày, goodsReceiptId, pendingOnly), GET /{id} (kèm bảng so ba cột), POST /, PUT /{id}, POST /{id}/submit, /approve, /reject, /cancel. Roles đúng AC-04; Cashier 403. GoodsReceiptResponseDto trả thêm invoiceId, invoiceStatus. Mã lỗi mới, ProblemDetails + errorCode: SUPPLIER_INVOICE_DUPLICATE 409, SUPPLIER_INVOICE_INVALID_STATE 409, SUPPLIER_INVOICE_RECEIPT_MISMATCH 422, SUPPLIER_INVOICE_ALREADY_EXISTS 409, GOODS_RECEIPT_INVOICE_PENDING 409.
- AC-08 Test PostgreSQL thật, class mới PostgresSupplierInvoiceIntegrationTests:
  (a) kèm hàng khớp → Matched, confirm theo giá phiếu mua;
  (b) kèm hàng lệch giá → confirm bị 409 khi Pending; Approve → confirm, dòng IN UnitCost và AverageCost theo giá hóa đơn (đơn vị quy đổi FactorSnapshot ≠ 1);
  (c) Reject → phiếu nhập Cancelled, tồn không đổi;
  (d) đến sau lệch → approve/reject không đổi số dòng sổ, OnHand, AverageCost;
  (e) trùng ký hiệu+số cùng NCC → 409; khác NCC thì được; replay cùng key không tạo bản ghi mới;
  (f) Approve chạy song song Confirm và Create invoice song song Confirm (Task.WhenAll): kết quả nhất quán, không có phiếu Confirmed theo giá chưa duyệt;
  (g) Staff Approve 403, Cashier 403, tenant khác 404.
- AC-09 Docs: mọi file trong DOCS PHẢI CẬP NHẬT đã sửa, nội dung khớp code (tên bảng, cột, trạng thái, endpoint, mã lỗi, thứ tự khóa); liệt kê trong mục DOCS ĐÃ CẬP NHẬT của bàn giao. Thiếu một file là chưa đạt.

INVARIANT PHẢI GIỮ: tenant chỉ từ JWT; inventory_transactions chỉ INSERT; khóa sau khi mở transaction trong ExecuteWithRetryAsync; idempotency; Available không âm; giá phiếu mua đã duyệt không bị sửa.

DOCS PHẢI CẬP NHẬT (luật: đổi chức năng hiện có hoặc database thì docs đi cùng):
- docs/database/oisms_schema.sql — 3 bảng, index, trigger mới
- docs/database/README.md — từ điển dữ liệu 3 bảng
- docs/04-architecture-and-design/03-database-design.md — ERD, quan hệ hóa đơn ↔ phiếu nhập ↔ phiếu mua, số bảng 47
- docs/04-architecture-and-design/04-state-machines.md — máy trạng thái SupplierInvoice, PurchaseVariance; điều kiện mới khi xác nhận GoodsReceipt
- docs/02-functional-requirements/fr-03-inventory-ledger.md, fr-04-cost-wac.md — nguồn giá nhập: giá phiếu mua, hoặc giá hóa đơn kèm hàng đã duyệt; hóa đơn đến sau không đổi WAC/sổ kho
- docs/05-api-contracts/03-inventory-endpoints.md — mục mới sau 1.1 cho api/supplier-invoices; confirm phiếu nhập thêm lỗi GOODS_RECEIPT_INVOICE_PENDING và trường invoiceId, invoiceStatus
- docs/12-error-and-status-codes/02-error-codes-catalog.md — 5 mã lỗi mới
- docs/01-users-and-rbac/02-rbac-matrix.md — dòng nhập/gửi/hủy hóa đơn (Owner, Staff), duyệt/từ chối chênh lệch (Owner)
- Số bảng 44 → 47 ở CLAUDE.md, docs/README.md, docs/07-devops-and-deployment/01-docker-compose-guide.md, docs/13-deploy-aws/04-quy-trinh-deploy-va-cap-nhat.md (thêm bước chạy 20261112_supplier_invoices.sql trên DB đang chạy), docs/13-deploy-aws/05-van-hanh-giam-sat-backup.md
- Báo cáo mới docs/report-after-upgrade/hoa-don-nha-cung-cap-u01b.md theo cấu trúc mua-hang-nhap-kho.md: Đã làm; Ảnh hưởng chức năng hiện có (xác nhận phiếu nhập có thể bị chặn 409, giá nhập có thể theo hóa đơn, phiếu nhập bị hủy khi từ chối); Thay đổi DB và cách nâng cấp DB đang chạy; Kiểm chứng (lệnh, PASS/FAIL/NOT RUN); Giới hạn (không công nợ, hóa đơn đến sau không đổi giá vốn)

GATE (chạy thật và ghi kết quả):
- dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal → 0 lỗi
- dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false → PASS toàn bộ
- dotnet test ...IntegrationTests.csproj -p:UseAppHost=false --filter 'FullyQualifiedName~PostgresSupplierInvoice|FullyQualifiedName~PurchaseOrder|FullyQualifiedName~GoodsReceipt' trên PostgreSQL thật (biến OISM_TEST_*) → PASS, ghi số test
- Chạy file migration 2 lần bằng psql trên DB đã có dữ liệu 44 bảng → không lỗi, thành 47 bảng, /health/ready không báo thiếu

GIỚI HẠN:
- Không commit/push. Không thêm dependency. Không sửa migration cũ.
- Một lỗi tối đa 2 lần thử; quá thì dừng, trả BLOCKED.
- Không nới assertion hay skip test. Không đổi thứ tự khóa.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```

## P2 — Giao diện Admin (gửi sau khi P1 VERIFIED)

```text
Đọc skills/claude-to-antigravity/references/antigravity.md rồi làm task sau.

TASK: OISMS-47-U01B-P2 — Giao diện hóa đơn NCC và chênh lệch | Mức: STANDARD | Prompt 1/3
BASELINE: branch feature/OISMS-58-nghiem-thu-sprint-5, commit 7e2e751 + thay đổi P1 chưa commit (backend U01-B đã VERIFIED, schema 47 bảng). Giữ nguyên mọi thay đổi có sẵn.

MỤC TIÊU: Staff/Owner nhập hóa đơn NCC ngay trong chi tiết phiếu nhập, xem bảng so ba cột; Owner duyệt/từ chối chênh lệch; nút xác nhận nhập bị khóa khi hóa đơn đang chờ.

NGUỒN CẦN ĐỌC:
- docs/Upgrade-recommended/Done/01-mua-hang-nhap-kho-gia-von.md: "Quyết định 01-B" mục Giao diện Admin, Phân quyền, Trạng thái hóa đơn
- docs/05-api-contracts/03-inventory-endpoints.md: mục hóa đơn NCC do P1 thêm; docs/12-error-and-status-codes/02-error-codes-catalog.md mã SUPPLIER_INVOICE_*, GOODS_RECEIPT_INVOICE_PENDING
- Mẫu code bắt chước: src/frontend/admin-dashboard/src/services/purchaseOrderService.ts (Idempotency-Key), components/purchase/*.tsx, types/purchaseOrder.ts, phần phiếu nhập trong pages/InventoryPage.tsx và services/goodsReceiptService.ts
- Skill kỹ thuật: skills/react-state-management/SKILL.md, skills/typescript-pro/SKILL.md

ĐƯỢC SỬA: src/frontend/admin-dashboard/src/{types/supplierInvoice.ts, services/supplierInvoiceService.ts, components/supplierInvoices/*, pages/InventoryPage.tsx, types/goodsReceipt.ts, services/goodsReceiptService.ts}; các file docs trong DOCS PHẢI CẬP NHẬT
KHÔNG ĐƯỢC SỬA: backend, pos-app, các màn khác, tài liệu lịch sử.

ACCEPTANCE CRITERIA:
- AC-01: chi tiết phiếu nhập có nút "Nhập hóa đơn NCC" khi phiếu chưa có hóa đơn hiệu lực; form điền sẵn dòng từ phiếu nhập (SKU, đơn vị, SL nhận, giá phiếu mua), nhập ký hiệu, số, ngày, đơn giá trước thuế, thuế, phí ship. Tạo giữ nguyên Idempotency-Key khi retry lúc kết quả chưa rõ.
- AC-02: Lưu nháp, sửa nháp, Gửi đối chiếu; sau gửi hiện trạng thái và bảng so ba cột (phiếu mua / thực nhận / hóa đơn), tô dòng lệch.
- AC-03: nút Xác nhận nhập bị tắt kèm lý do khi hóa đơn Draft/PendingApproval; lỗi GOODS_RECEIPT_INVOICE_PENDING từ API hiển thị đúng thông báo.
- AC-04: Owner thấy danh sách "Chênh lệch chờ duyệt" (GET pendingOnly) và Duyệt/Từ chối (bắt buộc lý do); Staff không thấy nút duyệt; Cashier không vào được.
- AC-05: Hủy hóa đơn cần lý do; hóa đơn đã hủy cho phép nhập hóa đơn mới.
- AC-06: Lỗi 409/422 hiển thị theo errorCode, có trạng thái đang tải và rỗng.
- AC-07 Docs: mọi file trong DOCS PHẢI CẬP NHẬT đã sửa và khớp giao diện thực tế; liệt kê trong DOCS ĐÃ CẬP NHẬT.

DOCS PHẢI CẬP NHẬT:
- docs/11-ui-screens/04-inventory-screens.md — form hóa đơn NCC trong chi tiết phiếu nhập, bảng so ba cột, nút xác nhận bị khóa, danh sách Chênh lệch chờ duyệt, quyền theo vai trò
- docs/report-after-upgrade/hoa-don-nha-cung-cap-u01b.md — thêm mục Giao diện Admin: màn hình, luồng thao tác, kết quả chạy thử từng ca

GATE:
- npm run build --prefix src/frontend/admin-dashboard → PASS
- Chạy thử trên backend chạy từ source hiện tại + PostgreSQL đã có schema 47 bảng: ca khớp, ca lệch → duyệt → xác nhận, ca từ chối; ghi lại bằng ảnh chụp hoặc mô tả từng bước và kết quả. Container oism_backend đang chạy là image cũ, chưa có API hóa đơn. Không chạy migration lên DB local oism_postgres khi chưa sao lưu; ưu tiên một DB thử riêng.

GIỚI HẠN: Không commit/push. Không thêm dependency. Một lỗi tối đa 2 lần thử. Không đổi contract backend; thiếu gì thì ghi vào BLOCKER.

BÀN GIAO: theo skills/claude-to-antigravity/assets/handoff.md.
```
