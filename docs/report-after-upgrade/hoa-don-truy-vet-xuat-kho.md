# Báo cáo sau nâng cấp: Hóa đơn bán và truy vết chứng từ xuất (Upgrade 02)

| Mục | Nội dung |
| --- | --- |
| Task | `OISMS-47-U02`, phần backend (`P1`) |
| Ngày kiểm tra | 03/11/2026 |
| Đặc tả | [Tài liệu 02](../Upgrade-recommended/02-truy-vet-chung-tu-xuat-kho.md) |
| Phạm vi | Backend, test, tài liệu API. **Chưa có giao diện (`P2`).** |
| Trạng thái | Backend đã kiểm chứng trên PostgreSQL 16 riêng. Chưa kiểm chứng đầu cuối từ giao diện. Database đang chạy phải chạy migration trước, xem mục Triển khai. |

> **Cập nhật 04/11/2026:** giao diện của phần này đã có. Kết quả nghiệm thu xem [báo cáo frontend](frontend-coming-soon-va-upgrade.md). Các mục "chưa có giao diện" bên dưới là hiện trạng tại ngày kiểm tra ghi trong bảng.

## Đã làm

- **Dữ liệu:** thêm `sales_invoices` chỉ INSERT, `orders.confirmed_by`, `stock_transfers.dispatched_by`. Migration: [`20261103_sales_invoices.sql`](../../docker/postgres/migrations/20261103_sales_invoices.sql).
- **Hóa đơn:** POS checkout và Confirm đơn online tạo đúng một hóa đơn trong cùng giao dịch với dòng `OUT`. Không thêm khóa, không đổi thứ tự khóa.
- **Chống sửa:** đơn đã xác nhận không đổi được `confirmed_by`, `sku_code_snapshot`, `sku_name_snapshot`.
- **Giá vốn POS:** `OrderItem.CostPrice` và `OUT.UnitCost` cùng làm tròn 6 chữ số.
- **API:** danh sách, chi tiết, in lại hóa đơn; truy vết theo hóa đơn, đơn, dòng sổ kho. Dữ liệu in không có giá vốn. `ReceiptNumber` của POS nay là số hóa đơn `INV-`.

Contract chi tiết: [API tồn kho](../05-api-contracts/03-inventory-endpoints.md), [API đơn hàng](../05-api-contracts/04-order-endpoints.md), [API POS](../05-api-contracts/05-pos-endpoints.md).

## Kiểm chứng

Chạy trên PostgreSQL 16 riêng, cổng `55437`, biến `OISM_TEST_PORT`; không đụng database đang chạy.

| Lệnh hoặc phép kiểm | Kết quả |
| --- | --- |
| `dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal` | 0 lỗi |
| `dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false` | PASS 173/173 |
| `dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false` | PASS 348/348, 20 phút 20 giây |
| Lọc `SalesInvoiceConcurrencyIntegrationTests`, chạy 3 lần | PASS 2/2 cả 3 lần |
| Tạo database từ schema tại `41aff38`, chạy file migration 2 lần bằng `psql`, so `pg_dump -s` với `oisms_schema.sql` mới | Cả 2 lần thoát mã 0; 0 dòng khác biệt |
| Test `AC01_UpgradeFromPreU02Schema...`: đưa database về trước U02, chạy `UpgradeDatabaseSchemaAsync` 2 lần, so với database tạo mới | PASS: bảng, cột, ràng buộc, trigger, index, quyền, nội dung hàm; validator không báo lỗi |
| Chạy 5 file migration trên database Docker 30 bảng có dữ liệu, sau khi backup | Đều thoát mã 0; đủ 40 bảng, giữ nguyên 4 SKU, 3 dòng sổ kho, 6 người dùng, 2 tenant; `/health/ready` trả 200 |
| Chạy lại cả 5 file lần 2 trên bản sao từ backup đó | Đều thoát mã 0; vẫn 40 bảng, không sinh thêm `sku_units`, `sku_unit_prices` |

## Lỗi phát hiện khi nghiệm thu và đã sửa

Các test Anti bàn giao đều đạt nhưng không chạm tới những lỗi này. Thêm [`SalesInvoiceTraceIntegrationTests.cs`](../../src/backend/tests/OISM.IntegrationTests/SalesInvoiceTraceIntegrationTests.cs) mới thấy.

| Lỗi | Hậu quả | Kiểm chứng |
| --- | --- | --- |
| `Include` tạo vòng lặp trong truy vấn `AsNoTracking` khi truy vết từ dòng sổ kho | `GET /api/inventory/ledger/{id}/trace` trả 500 với phiếu nhập; chuyển kho và điều chỉnh dùng cùng mẫu đó | Test FAIL ở phiếu nhập trước khi sửa; PASS đủ phiếu nhập, chuyển kho, kiểm kê sau khi sửa |
| Bộ lọc `fromDate`, `toDate` gửi thẳng mốc có múi giờ vào Npgsql | `GET /api/sales-invoices` trả 500 khi mốc là `+07:00` | Test FAIL trước khi sửa, PASS sau khi sửa |
| In lại đọc địa chỉ chi nhánh và phương thức thanh toán hiện tại khi bản chụp trống | Hóa đơn cũ có thể đổi sau khi lập; đơn online chưa thanh toán không còn đúng bản chụp | Thấy khi đọc code; test so nguyên phản hồi in trước và sau khi đổi dữ liệu gốc PASS |
| Test AC-01 cũ không đi từ schema trước U02 và chỉ tìm tên cột trong hàm | Không chứng minh được đường nâng cấp | Thay bằng test mới ở trên |
| Thiếu test đơn sàn, `/process`, truy vết ngoài `Order`, tenant khác ở truy vết đơn, sổ kho và in theo số, `LEDGER_ENTRY_NOT_FOUND`, lý do bị chặn của trigger | AC-02, AC-05, AC-07 được báo đạt khi chưa có bằng chứng | Bổ sung, PASS |

Các lỗi trên do người nghiệm thu vừa sửa vừa tự kiểm, chưa qua reviewer độc lập.

## Điểm còn lưu ý

- **Số điện thoại chi nhánh:** `Branch.Phone` bị EF bỏ qua nên `BranchPhone` trên dữ liệu in luôn trống.
- **Bộ lọc thời gian ở API khác:** `InventoryService.cs:233` và `OrderService.cs:2531` truyền mốc thời gian theo cùng cách, nên nhiều khả năng cũng trả 500 với `+07:00`. Có từ trước U02, chưa thử và chưa sửa.
- **Số hóa đơn:** phần ngày trong `INV-yyyyMMdd-` theo UTC, cùng cách với số đơn POS; từ 00:00 đến 07:00 giờ Việt Nam sẽ mang ngày hôm trước.
- **Thông báo `/health/ready`:** phần `description` vẫn ghi `(29 tables)` dù đã kiểm tra đủ 40 bảng; số đúng ở trường `tablesCount`.
- **Quy tắc chưa có GVHD xác nhận:** các mục "đề xuất của nhóm" trong tài liệu 02, nhất là lập hóa đơn online lúc Confirm.

## Triển khai

Backend này cần đủ 40 bảng. Database cũ phải backup rồi chạy lần lượt các file trong `docker/postgres/migrations/` theo thứ tự tên file, nếu không `/health/ready` trả 503 và Admin không tải được dữ liệu. Lệnh cho cả nhóm: [Nâng cấp database đã có dữ liệu](../database/README.md#nâng-cấp-database-đã-có-dữ-liệu-cả-nhóm-đọc-khi-kéo-code-mới).

## Còn lại

- `P2`: giao diện Admin và POS (số hóa đơn trên biên nhận đang bị `PaymentModal.tsx:176` ghi đè bằng số đơn).
- Cập nhật `checklist.md` sau khi xong `P2`.
