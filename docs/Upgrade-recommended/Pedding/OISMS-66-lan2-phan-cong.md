# Phân công OISMS-66 lần 2 — Upgrade 12 và 13

Lập ngày 19/11/2026. Ba người (Phúc Hữu: phần nhiều code nhất; Phương Lê: phần vừa; Nam Nguyễn: phần ít code nhất), năm gói công việc (`G1`–`G5`). Không nhầm với "Gói 1–5" của đề bài trong [README Pedding](./README.md).

- **Nhánh:** `feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2` (mọi gói push chung nhánh này).
- **PR:** draft vào `develop`. Chỉ merge khi mọi gói được Claude `VERIFIED` và nhóm đồng ý.
- **Prompt giao AI:** [`skills/claude-to-antigravity/references/OISMS-66-lan2-prompts.md`](../../../skills/claude-to-antigravity/references/OISMS-66-lan2-prompts.md). Mỗi người dán **mục "Luật làm song song" + prompt của gói mình** vào phiên AI riêng.

## Bảng phân công

| Người | Gói | Nội dung | Khi nào làm | Phụ thuộc | Trạng thái |
| --- | --- | --- | --- | --- | --- |
| **Phúc Hữu** | **G1** | Đơn mua `PendingApproval` (Staff đề nghị, Owner chọn NCC + giá rồi duyệt), phân bổ phí/chiết khấu vào giá vốn, bỏ hóa đơn NCC, Staff không thấy giá trên đơn mua và phiếu nhập | **Đợt 1 — bắt đầu ngay** | Không | Chưa bắt đầu |
| **Nam Nguyễn** | **G2** | Tồn kho theo kỳ (đầu kỳ, nhập, xuất, cuối kỳ; ngày/tháng/khoảng), hàng đang chuyển, thẻ kho S12/S10, xuất Excel. Chỉ đọc, không đổi DB | **Đợt 1 — bắt đầu ngay** | Không | Chưa bắt đầu |
| **Phương Lê** | **G3** | Phiếu xuất kho 02-VT lưu kèm dòng sổ `OUT` (2 bảng mới, lập cùng transaction checkout/xác nhận đơn), in phiếu, in hàng loạt, bảng kê POS theo ngày | **Đợt 1 — bắt đầu ngay** | Không | Chưa bắt đầu |
| Phúc Hữu | G4 | Cấu hình giá bán khi nhập kho (Giữ cũ / WAC + lãi / Giá mới, áp ngay / từ ngày), lý do bắt buộc trên phiếu đổi giá, hộp thoại xung đột giá (Q19), khóa giá | **Đợt 2** — sau khi G1 `VERIFIED` | G1 | Chưa có prompt |
| Phương Lê | G5 | Che giá dùng chung toàn hệ thống (đơn hàng `CostPrice`, truy vết, báo cáo, tìm kiếm, in, AI, SignalR) + test quét mọi endpoint bằng Staff + cập nhật RBAC | **Đợt 3** — sau khi G1, G2, G3 `VERIFIED` | G1, G2, G3 | Chưa có prompt |
| Nam Nguyễn | Kiểm thử chéo | Sau khi xong G2: chạy lại test và thử giao diện của G1, G3 trên máy mình, ghi lỗi vào comment PR | **Đợt 2** | G1, G3 đã push | — |

Prompt G4, G5 do Claude viết khi gói phụ thuộc xong; khi đó cập nhật bảng này.

## File AI của từng người phải đọc

Đường dẫn tính từ gốc repo. AI đọc **theo thứ tự**, chỉ đọc mục được chỉ.

### Chung cho mọi gói

| Thứ tự | File | Đọc gì |
| --- | --- | --- |
| 1 | `CLAUDE.md` | Quy tắc không được phá, lệnh build/test, quy ước commit |
| 2 | `skills/claude-to-antigravity/references/antigravity.md` | Vai trò AI triển khai, cách bàn giao |
| 3 | `skills/claude-to-antigravity/references/OISMS-66-lan2-prompts.md` | Mục "Luật làm song song" + prompt của gói mình |
| 4 | `skills/claude-to-antigravity/assets/handoff.md` | Mẫu bàn giao |
| 5 | `skills/claude-to-antigravity/references/oisms.md` | Invariant và bảng "Thay đổi → docs" |

### G1 (và G4)

| File | Đọc gì |
| --- | --- |
| `docs/Upgrade-recommended/Pedding/12-don-mua-va-cau-hinh-gia.md` | Tóm tắt luồng A–F; mục 1–3 (Q1–Q14, Q20–Q24); mục 7 BE-12.1→12.6, FE-12.1, FE-12.2. G4 thêm: mục 4, Q15–Q19, Q25, BE-12.7, BE-12.8, FE-12.3, FE-12.4 |
| `docs/04-architecture-and-design/04-state-machines.md` | Trạng thái đơn mua hiện tại |
| `docs/Upgrade-recommended/Done/01-mua-hang-nhap-kho-gia-von.md` | Hiện trạng U01-A/B (bị thay thế) |
| `docs/database/oisms_schema.sql` | Bảng `purchase_orders`, `purchase_order_items`, `goods_receipts`, bảng hóa đơn NCC |
| Code mẫu | `src/backend/src/OISM.Application/Features/PurchaseOrders/`, `src/backend/src/OISM.Domain/Entities/PurchaseOrder.cs`, `src/backend/src/OISM.Application/Features/Inventory/Services/GoodsReceiptService.cs`, `src/frontend/admin-dashboard/src/pages/PurchaseOrdersPage.tsx`, `src/frontend/admin-dashboard/src/components/purchase/` |
| Test mẫu | `test-oisms/backend/OISM.IntegrationTests/PurchaseOrderIntegrationTests.cs`, `PurchaseOrderSeed.cs` |
| G4 thêm | `docs/Upgrade-recommended/Done/04-bang-gia-theo-thoi-gian.md`, `src/backend/src/OISM.Application/Features/Pricing/`, `src/frontend/admin-dashboard/src/components/pricing/` |

### G2

| File | Đọc gì |
| --- | --- |
| `docs/Upgrade-recommended/Pedding/13-ton-kho-theo-ky.md` | Mục 1–4 (định nghĩa, Q1–Q14, cách tính, ngoài phạm vi), mục 6, mục 7 BE-13.1→13.3, FE-13.1, FE-13.2 |
| `docs/database/oisms_schema.sql` | Bảng `inventory_transactions`, `stock_transfers`, index `ix_inventory_transactions_sku_history` |
| `docs/02-functional-requirements/fr-11-reports-analytics.md` | Quy tắc báo cáo, múi giờ |
| Code mẫu | `src/backend/src/OISM.Application/Features/Reports/Services/ReportService.cs`, `src/backend/src/OISM.WebApi/Controllers/ReportsController.cs`, `src/backend/src/OISM.Application/Features/Inventory/Services/InventoryService.cs` (chỉ đọc), `src/frontend/admin-dashboard/src/pages/ReportsPage.tsx`, `src/frontend/admin-dashboard/src/pages/InventoryPage.tsx` |
| Test mẫu | `test-oisms/backend/OISM.IntegrationTests/PostgresAiStockMovementIntegrationTests.cs` |

### G3 (và G5)

| File | Đọc gì |
| --- | --- |
| `docs/Upgrade-recommended/Pedding/13-ton-kho-theo-ky.md` | Mục 5 (P1–P6), Q10, Q13, mục 6 (test phiếu xuất), mục 7 BE-13.4→13.6, FE-13.3 |
| `docs/Upgrade-recommended/Done/02-truy-vet-chung-tu-xuat-kho.md` | Hóa đơn bán lập ở đâu, quy tắc truy vết |
| `docs/04-architecture-and-design/sequence-diagrams/seq-pos-fast-checkout.md`, `seq-online-reservation.md` | Thứ tự bước và khóa trong checkout/xác nhận |
| Code mẫu | `src/backend/src/OISM.Application/Features/Orders/Services/PosCheckoutService.cs`, `OrderService.cs` (đoạn `_context.SalesInvoices.Add`), `src/backend/src/OISM.Application/Features/SalesInvoices/`, `docker/postgres/migrations/20261110_sales_returns_request_key.sql`, `docker/postgres/init-oisms.sh`, `src/frontend/admin-dashboard/src/components/orders/ShippingSlipPrintModal.tsx`, `src/frontend/admin-dashboard/src/pages/OrdersPage.tsx` |
| Test mẫu | `test-oisms/backend/OISM.IntegrationTests/SalesInvoiceIntegrationTests.cs`, `SalesInvoiceConcurrencyIntegrationTests.cs`, `PosCheckoutIntegrationTests.cs` |
| G5 thêm | `docs/01-users-and-rbac/02-rbac-matrix.md`, `docs/Upgrade-recommended/Pedding/12-don-mua-va-cau-hinh-gia.md` (Q9, Q10, Q24, BE-12.9, FE-12.5), `src/backend/src/OISM.WebApi/Controllers/InventoryController.cs` (`canViewCost`), `src/backend/src/OISM.Application/Features/Orders/Dtos/OrderDtos.cs` |

## Lịch làm

| Đợt | Bắt đầu khi | Việc |
| --- | --- | --- |
| Chuẩn bị | Ngay | Claude commit docs, push nhánh, tạo draft PR (đã xong 19/11/2026). Mỗi người `git fetch` và checkout nhánh lần 2 trên máy mình |
| Đợt 1 | Sau bước chuẩn bị | Phúc Hữu làm G1, Nam Nguyễn làm G2, Phương Lê làm G3, song song |
| Review đợt 1 | Mỗi gói comment báo cáo lên PR | Claude review từng gói, gói CRITICAL chạy lại test PostgreSQL; `VERIFIED` hoặc gửi prompt sửa |
| Đợt 2 | G1 `VERIFIED` | Phúc Hữu làm G4; Nam Nguyễn kiểm thử chéo G1, G3 |
| Đợt 3 | G1, G2, G3 `VERIFIED` | Phương Lê làm G5 |
| Kết thúc | Mọi gói `VERIFIED` | Reset DB demo và seed lại (bắt buộc vì G3), nhóm đồng ý thì merge PR vào `develop` |

## Quy tắc khi làm

- Mỗi người làm trên **máy riêng**, cùng nhánh lần 2. Trước khi push: `git pull --rebase`, build lại; không force-push.
- File dùng chung giữa các gói (`oisms_schema.sql`, `DatabaseMigrationService.cs`, `ApiErrorTitles.cs`, `DependencyInjection.cs`, `InventoryPage.tsx`, `02-rbac-matrix.md`, `02-error-codes-catalog.md`): chỉ sửa đúng phần của gói; ai rebase sau thì gộp xung đột.
- Xong gói: AI chạy đủ GATE → commit đúng file của gói → push → comment báo cáo lên PR theo mẫu trong file prompt. Không merge.
- Commit và comment **không** ghi Claude hay AI là tác giả. Không commit `.env`, file dump, API key.
- Gặp lỗi lặp hai lần không có hướng mới, hoặc đặc tả thiếu/mâu thuẫn: dừng, báo `BLOCKED` kèm câu hỏi, không tự đổi yêu cầu.
