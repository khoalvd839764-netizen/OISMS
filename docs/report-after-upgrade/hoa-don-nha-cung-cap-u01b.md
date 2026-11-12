# Báo cáo sau nâng cấp · Hóa đơn nhà cung cấp và đối chiếu ba bên (U01-B)

| Thông tin | Giá trị |
| :--- | :--- |
| Task | `OISMS-47-U01B-P1` |
| Ngày | 12/11/2026 |
| Baseline | `feature/OISMS-58-nghiem-thu-sprint-5` tại `7e2e751` |
| Phạm vi | Backend, PostgreSQL, kiểm thử tích hợp và tài liệu hiện hành |

## Đã làm

- Owner và Staff có thể lập, sửa nháp, xem, gửi đối chiếu và hủy hóa đơn gắn với phiếu nhập. Lệnh tạo yêu cầu `Idempotency-Key` và nhận diện nội dung tạo ban đầu kể cả sau khi sửa nháp.
- Gửi hóa đơn so số lượng ghi trên hóa đơn với lượng thực nhận và so đơn giá trước thuế với giá phiếu mua. Mọi chênh lệch giá hoặc lượng đều chờ Owner quyết định riêng. Thuế và phí vận chuyển được lưu trên hóa đơn. Chi tiết trả ba cột phiếu mua, thực nhận và hóa đơn.
- Lưu loại chênh lệch `AtReceipt` hoặc `AfterReceipt` theo trạng thái phiếu nhập lúc gửi, kèm quyết định và lý do từ chối.

## Ảnh hưởng chức năng hiện có

- Xác nhận nhập trả `409 GOODS_RECEIPT_INVOICE_PENDING` khi hóa đơn còn `Draft` hoặc `PendingApproval`. Khi hóa đơn kèm hàng đã được duyệt, dòng nhập có trên hóa đơn dùng đơn giá hóa đơn trước thuế. Tồn luôn dùng lượng thực nhận và `FactorSnapshot` vẫn quy đổi về đơn vị gốc cho cả dòng `IN` và WAC.
- Dòng thực nhận thiếu trên hóa đơn sinh chênh lệch lượng. Nếu Owner duyệt, dòng này dùng giá phiếu mua vì hóa đơn không có đơn giá tương ứng.
- Từ chối chênh lệch `AtReceipt` đồng thời hủy phiếu nhập nếu phiếu còn nháp. Danh sách và chi tiết phiếu nhập có `invoiceId` và `invoiceStatus` của hóa đơn còn hiệu lực.
- Xác nhận nhập khóa phiếu nhập → hóa đơn còn hiệu lực và đọc lại → phiếu mua → SKU. Mọi thao tác ghi hóa đơn khóa phiếu nhập trước hóa đơn.

## Thay đổi DB và nâng cấp DB đang chạy

Ba bảng mới gồm `supplier_invoices`, `supplier_invoice_items` và `purchase_variances`. Schema có 47 bảng nghiệp vụ. Khóa ngoại ghép tenant và chỉ mục unique bảo vệ khóa retry, số hóa đơn theo nhà cung cấp và một hóa đơn còn hiệu lực trên mỗi phiếu nhập. Trigger chặn xóa hóa đơn và chứng từ chênh lệch, đồng thời khóa dòng hóa đơn khi header ra khỏi `Draft`.

Với DB đang ở baseline 44 bảng, sao lưu trước rồi chạy từ thư mục gốc bằng tài khoản quản trị schema.

```bash
docker compose exec -T postgres sh -c 'psql -X -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1' \
  < docker/postgres/migrations/20261112_supplier_invoices.sql
```

Sau khi cập nhật backend, `/health/ready` phải trả HTTP 200, `tablesCount: 47` và `triggerVerified: true`. DB cũ hơn cần chạy các migration còn thiếu theo thứ tự tên file. Volume trống lấy schema từ `docs/database/oisms_schema.sql`.

## Kiểm chứng

Lệnh .NET chạy từ root repo trên cây làm việc của task tại baseline `7e2e751`. PostgreSQL 16 chạy riêng trên loopback port `55447`. DB thử migration dùng schema baseline và bản ghi tenant kiểm chứng. Tài khoản Web API kiểm tra readiness chỉ có quyền runtime.

| Lệnh | Môi trường | Kết quả |
| :--- | :--- | :--- |
| `dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal` | .NET 8 | PASS, 0 lỗi, 0 cảnh báo |
| `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false` | .NET 8 | PASS 380/380, 0 skip |
| `dotnet test test-oisms/backend/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false --filter 'FullyQualifiedName~PostgresSupplierInvoice\|FullyQualifiedName~PurchaseOrder\|FullyQualifiedName~GoodsReceipt'` | PostgreSQL 16 qua `OISM_TEST_*` | PASS 39/39, gồm 11 ca U01-B, 0 skip |
| `psql -X -v ON_ERROR_STOP=1 -U postgres -d oisms_u01b_migration_test` nhận SQL từ migration, hai lượt | PostgreSQL 16, schema 44 bảng đã có dữ liệu | PASS cả hai lượt, 44 → 47 bảng và còn bản ghi kiểm chứng |
| `curl http://127.0.0.1:55448/health/ready` | Web API cục bộ và role runtime trên DB thử migration | PASS, HTTP 200 `Healthy`, `tablesCount: 47`, `missingTables: null`, `triggerVerified: true` |

Unit test schema giữ so sánh số bảng chính xác, cập nhật mốc từ 44 lên 47 và kiểm tra đủ tên ba bảng mới.

## Giao diện Admin (P2)

Tại **Kho & sổ kho → Phiếu nhập đã lưu → Xem phiếu**, Owner và Staff có thể nhập hóa đơn NCC ngay trong chi tiết phiếu. Form lấy SKU, đơn vị và lượng thực nhận từ phiếu nhập, lấy đơn giá mặc định từ phiếu mua. Lưu nháp và gửi đối chiếu là hai thao tác riêng. Kết quả hiển thị bảng **Phiếu mua / Thực nhận / Hóa đơn**, tô các dòng lệch và khóa **Xác nhận nhập kho** trong lúc hóa đơn còn nháp hoặc chờ duyệt.

Owner mở **Chênh lệch chờ duyệt**, bấm **Xem và xử lý**, sau đó duyệt hoặc từ chối có lý do. Owner và Staff có thể hủy hóa đơn ở trạng thái cho phép sau khi nhập lý do. Phiếu còn đủ điều kiện hiện lại nút nhập hóa đơn. Cashier không có phiên truy cập trang Admin.

### Chạy thử giao diện

Chạy Admin bằng Vite, Web API build từ source tại baseline `7e2e751` cùng thay đổi P1 trong working tree, và PostgreSQL 16 tạm trên loopback với schema 47 bảng. `/health/ready` trả HTTP 200. Phiếu mua và phiếu nhập dưới đây là dữ liệu thử, được tạo qua API trước khi thao tác trên trình duyệt.

| Ca | Thao tác trên giao diện | Kết quả |
| :--- | :--- | :--- |
| Khớp, `GR-P2-MATCH` | Owner mở phiếu có 2 Cái × 120.000 ₫. Form tự điền SKU, đơn vị, lượng và giá. Lưu nháp, sửa thuế 25.000 ₫ và phí vận chuyển 10.000 ₫, gửi đối chiếu rồi xác nhận nhập. | PASS. Nút xác nhận bị khóa khi hóa đơn nháp. Sau gửi, bảng ba cột đánh dấu **Khớp** và tổng hóa đơn là 275.000 ₫. Phiếu chuyển `Confirmed`, dòng `IN` có đơn giá 120.000 ₫. |
| Lệch → duyệt → xác nhận, `GR-P2-APPROVE` | Staff nhập giá 180.000 ₫ và gửi. Owner mở danh sách chờ duyệt, chọn hóa đơn, duyệt và xác nhận nhập. | PASS. Dòng lệch được tô, xác nhận bị khóa khi chờ duyệt. Staff không thấy danh sách hoặc nút quyết định. Sau duyệt, xác nhận thành công với giá 180.000 ₫ trên dòng nhập và dòng `IN`; giá trị phiếu 360.000 ₫. Tồn sau hai ca nhập là 4, WAC là 150.000 ₫. |
| Từ chối, `GR-P2-REJECT` | Owner gửi hóa đơn lệch giá 160.000 ₫ rồi bấm **Từ chối**. Thử bỏ trống, sau đó nhập lý do và xác nhận. | PASS. Lý do trống khóa nút xác nhận. Hóa đơn và chênh lệch thành `Rejected`, phiếu nhập thành `Cancelled`. Giao diện hiện ghi chú và không còn nút xác nhận nhập. Truy vấn DB xác nhận phiếu không có dòng sổ kho; tồn và WAC vẫn là 4 và 150.000 ₫. |
| Hủy và nhập lại, `GR-P2-CANCEL` | Hủy hóa đơn nháp có lý do rồi lập hóa đơn mới cùng ký hiệu và số trên phiếu đó. | PASS. Lý do trống khóa nút hủy. Hóa đơn cũ `Cancelled`; hóa đơn mới `Draft`. |
| Phản hồi lỗi | Gửi số hóa đơn trùng trên phiếu khác. Proxy thử nghiệm tạo hóa đơn nháp giữa bước đọc phiếu và xác nhận ở ca riêng, rồi chuyển tiếp HTTP 409 thật. Ở ca 422, proxy thay ID dòng bằng ID không hợp lệ và chuyển tiếp lỗi thật. | PASS. UI hiện thông báo theo `SUPPLIER_INVOICE_DUPLICATE`, `GOODS_RECEIPT_INVOICE_PENDING` và `SUPPLIER_INVOICE_RECEIPT_MISMATCH`. Khi gặp lỗi chờ hóa đơn, UI tải lại phiếu và khóa xác nhận. |
| Retry tạo hóa đơn, `GR-P2-RETRY` | Proxy thử nghiệm thay phản hồi thành 502 sau khi backend đã tạo và trả 201. Bấm **Thử lại lưu nháp hóa đơn**. | PASS. Form giữ nội dung, gửi lại cùng `Idempotency-Key`. Backend trả 200 với `isReplay: true`. DB có đúng một hóa đơn. |
| Quyền và trạng thái rỗng | Thử truy cập kho Admin sau khi đăng nhập bằng Cashier demo, gọi API hóa đơn với token Cashier, và mở lại danh sách chờ duyệt bằng Owner. | PASS. Admin đưa Cashier về đăng nhập, API trả 403. Danh sách Owner báo rỗng sau khi xử lý các hóa đơn chờ duyệt. |

`npm run build --prefix src/frontend/admin-dashboard` chạy từ root repo và PASS. Vite báo cảnh báo kích thước bundle JavaScript sau minify.

## Giới hạn

Phạm vi dừng ở đối chiếu và duyệt chênh lệch, chưa có nghiệp vụ công nợ, phiếu chi hoặc thanh toán nhà cung cấp. Hóa đơn đến sau chỉ bổ sung bằng chứng và không thay đổi đơn giá nhập, số dư, WAC hay sổ kho. Phiếu nhập lịch sử thiếu dòng liên kết phiếu mua không đủ dữ liệu để lập hóa đơn theo luồng này.

## Nghiệm thu backend (Claude, 12/11/2026)

Claude chạy lại trên PostgreSQL 16 tạm (cổng 55447, cấu hình như CI), cùng cây làm việc.

| Lệnh | Kết quả |
| :--- | :--- |
| `dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal` | PASS, 0 lỗi |
| `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false` | PASS 380/380 |
| Integration filter `PostgresSupplierInvoice\|PurchaseOrder\|GoodsReceipt` | PASS 39/39 |
| Toàn bộ `OISM.IntegrationTests` | Lần đầu FAIL 3/411: ba test trong `PostgresTenantIsolationTests` còn ghi cứng 44 bảng. Đã cập nhật lên 47 bảng nghiệp vụ (48 khi tính `__EFMigrationsHistory`), sau đó `PostgresTenantIsolationTests` PASS 35/35; 408 test còn lại đã PASS ở lần chạy toàn bộ |
| `psql -v ON_ERROR_STOP=1` chạy `20261112_supplier_invoices.sql` hai lần trên DB dựng từ schema HEAD (44 bảng, chưa có dữ liệu mẫu) | PASS cả hai lần, 47 bảng, có 4 trigger mới |

Giới hạn: chưa chạy migration trên DB có dữ liệu thật hoặc bản demo AWS; chưa có reviewer độc lập ngoài Claude.
