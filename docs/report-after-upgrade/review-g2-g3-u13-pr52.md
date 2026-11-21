# Review Gói 2 và Gói 3 — Upgrade 13 (PR #52)

| Thông tin | Chi tiết |
| --- | --- |
| PR | `#52` (`feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2` → `develop`) |
| Commit được review | G2 `cebf9f3` (tồn kho theo kỳ, thẻ kho S12/S10, Excel), G3 `0502705` (phiếu xuất kho 02-VT, bảng kê POS) |
| Commit sửa | `e0dc1ed`, rebase lên `bfda870`, `d979f40` của nhóm |
| Ngày review | 19/11/2026, cập nhật CI 21/11/2026 |
| Đặc tả | [`13-ton-kho-theo-ky.md`](../Upgrade-recommended/Pedding/13-ton-kho-theo-ky.md) |
| Môi trường | PostgreSQL 16 thật (container tạm, cấu hình như `.github/workflows/ci.yml`) |

## 1. Kết luận

| Gói | Kết luận | Ghi chú |
| --- | --- | --- |
| G2 | `CHANGES_REQUESTED` → đã sửa ở `e0dc1ed` | Lỗi chặn: cuối kỳ sai ở chế độ "Tất cả" khi có hàng đang chuyển; Excel chỉ xuất 100 SKU; 6/6 test FAIL trên PostgreSQL thật |
| G3 | `VERIFIED`, 2 điểm nhỏ đã sửa | Phiếu lập cùng transaction với dòng `OUT`, không thêm khóa; test G3, checkout, confirm, đồng thời PASS |

## 2. Findings G2 — tồn kho theo kỳ

| ID | Mức | Chặn | Vấn đề (kỳ vọng → thực tế) | Đã sửa |
| --- | --- | --- | --- | --- |
| G2-01 | P1 | Có | Chế độ "Tất cả" có phiếu chuyển `InTransit` trong kỳ: kỳ vọng cuối kỳ = Σ tồn chi nhánh, đối chiếu cùng cột đang chuyển (Q9). Thực tế cuối kỳ = đầu kỳ + nhập − xuất (đã bỏ chuyển nội bộ) nên lệch đúng lượng đang chuyển. Ví dụ nhập 50, gửi chuyển 10 chưa nhận: báo cuối kỳ 50 + đang chuyển 10 = 60, tổng cửa hàng thực tế 50 | Cuối kỳ lấy từ sổ cái tại mốc như đầu kỳ. Đối chiếu "Tất cả": `(đầu + đang chuyển đầu) + nhập − xuất = cuối + đang chuyển cuối`. Thêm `openingInTransitValue`, `closingInTransitValue` (chỉ Owner) để giá trị cũng khớp |
| G2-02 | P2 | Có | Xuất Excel kỳ vọng đủ SKU khớp bộ lọc; thực tế frontend gửi `pageSize=10000` nhưng service giới hạn 100 nên file chỉ có 100 SKU đầu | `GetInventoryPeriodReportForExportAsync` đi hết các trang 100 SKU, dòng tổng tính trên toàn bộ |
| G2-03 | P2 | Có | Test cũ chèn dòng sổ không nguồn hoặc nguồn giả, vi phạm `ck_inventory_transactions_one_source` và FK nên 6/6 FAIL trên PostgreSQL thật; test "100k" chỉ chèn 1.000 dòng; thiếu test hàng đang chuyển (mục 6) | Nhóm sửa fixture bằng chứng từ thật và đồng bộ số dư cho trigger `oisms_check_stock` (`bfda870`, `d979f40`). `e0dc1ed` thêm test hàng đang chuyển chạy luồng nhập mua và gửi phiếu chuyển thật, test Excel quá 100 SKU, và test hiệu năng chèn đúng 100.000 dòng sổ |
| G2-04 | P3 | Không | Đầu kỳ và phát sinh nạp từng dòng sổ lên bộ nhớ rồi mới cộng | Gom nhóm trên PostgreSQL |

Ghi chú test hiệu năng: 100.000 dòng sổ được chèn bằng `generate_series`, mỗi dòng trỏ tới một phiếu điều chỉnh `Confirmed` thật nên FK và CHECK đều thỏa. Chỉ trigger theo dòng bị tắt lúc chèn (`session_replication_role = replica`), vì trigger tồn kho cộng lại toàn bộ sổ cái cho từng dòng (O(n²)). Sau đó test đồng bộ số dư qua EF để trigger kiểm tra toàn bộ sổ một lần khi commit.

## 3. Findings G3 — phiếu xuất kho

| ID | Mức | Chặn | Vấn đề | Đã sửa |
| --- | --- | --- | --- | --- |
| G3-01 | P3 | Không | Thẻ kho chưa hiện số phiếu xuất cho dòng `OUT` bán hàng (mục 5, Đối chiếu) | Dòng thẻ kho trả `goodsIssueId`, `goodsIssueNumber`; modal và Excel hiện `PXK-...` (`e0dc1ed`) |
| G3-02 | P3 | Không | `by-orders` chưa chặn quá 100 đơn như hợp đồng API | Trả `400 VALIDATION_ERROR` (`e0dc1ed`) |
| G3-03 | P3 | Không | Bảng kê POS lặp tiền hóa đơn trên mỗi phiếu khi đơn xuất nhiều kho, cộng cột sẽ trùng | Đã sửa ở `01821fb` |

Đã kiểm tra, không có lỗi: chỉ checkout POS (`PosCheckoutService`) và xác nhận đơn (`OrderService`) ghi dòng `OUT` có `order_item_id`, cả hai lập phiếu sau dòng sổ trong cùng transaction; replay idempotent trả kết quả cũ không lập phiếu mới; Staff không thấy giá vốn, Cashier `403`.

## 4. Lệnh và kết quả

| Lệnh | Commit | Kết quả |
| --- | --- | --- |
| `dotnet build src/backend/OISM.sln` | `e0dc1ed` | PASS, 0 warning |
| `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj` | `e0dc1ed` | PASS 428/428 |
| `dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj` (toàn bộ) | `0502705`, trước sửa | FAIL 452/458; 6 FAIL đều là `PostgresInventoryPeriodReportIntegrationTests` (`DbUpdateException`) |
| `... --filter "FullyQualifiedName~InventoryPeriod\|FullyQualifiedName~GoodsIssue\|FullyQualifiedName~Report"` | `e0dc1ed` | PASS 14/14 (G2 8/8 gồm 2 test mới; 100.000 dòng sổ dưới 2 giây/trang) |
| `npm run build --prefix src/frontend/admin-dashboard` | `e0dc1ed` | PASS |
| CI `Build & Test Backend (.NET 8)`, `Build Frontend` | `e0dc1ed` | PASS |
| CI | `68090d1` (head ngày 21/11/2026) | PASS |
| Thử giao diện bằng tay | — | NOT RUN |

## 5. File thay đổi trong `e0dc1ed`

- `src/backend/src/OISM.Application/Features/Reports/Services/InventoryPeriodReportService.cs`, `IInventoryPeriodReportService.cs`, `Dtos/InventoryPeriodReportDtos.cs`
- `src/backend/src/OISM.WebApi/Controllers/ReportsController.cs`
- `src/backend/src/OISM.Infrastructure/Services/ClosedXmlExcelExportService.cs`
- `src/backend/src/OISM.Application/Features/GoodsIssues/Services/GoodsIssueService.cs`
- `src/frontend/admin-dashboard/src/components/inventory/StockCardModal.tsx`, `src/services/inventoryPeriodService.ts`
- `test-oisms/backend/OISM.IntegrationTests/PostgresInventoryPeriodReportIntegrationTests.cs`
- Docs: `docs/05-api-contracts/03-inventory-endpoints.md` (`v1.6.2`), mục "Sửa sau review" trong [`ton-kho-theo-ky-u13.md`](./ton-kho-theo-ky-u13.md) và [`phieu-xuat-kho-u13.md`](./phieu-xuat-kho-u13.md)

Comment PR: <https://github.com/khoalvd839764-netizen/OISMS/pull/52#issuecomment-7011639620>
