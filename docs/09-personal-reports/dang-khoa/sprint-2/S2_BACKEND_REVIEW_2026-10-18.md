# Rà soát backend S2 trước bàn giao — 18/10/2026

Người nhận: Đăng Khoa. Phạm vi đối chiếu: [`docs/08-sprints/sprint-2/SPRINT_2_BACKEND_1.md`](../../../08-sprints/sprint-2/SPRINT_2_BACKEND_1.md) và [`docs/08-sprints/sprint-2/SPRINT_2_BACKEND_2.md`](../../../08-sprints/sprint-2/SPRINT_2_BACKEND_2.md).

**Kết luận: đã triển khai đủ các nhóm chức năng chính của BE1/BE2, nhưng chưa nên chốt nghiệm thu backend S2 vì còn 5 nhóm lỗi đã tái hiện.** Đây là lỗi trong các chức năng hiện có, không phải yêu cầu mở rộng sprint.

Bản kiểm tra là working tree trên HEAD `2cc35c2`, bao gồm các thay đổi chưa commit và các file mới chưa được Git theo dõi. Kết quả không áp dụng riêng cho commit HEAD hoặc cho image Docker backend đang chạy của nhóm. Không sửa mã nghiệp vụ trong lần review này.

## 1. Bằng chứng kiểm chứng

- Build Release thành công qua `dotnet test src/backend/OISM.sln --no-restore -c Release`.
- **188/188 test đạt: 99 unit + 89 integration; 0 fail, 0 skip.** Bộ integration có cả kiểm thử PostgreSQL/HTTP và các kiểm thử khác; không gọi toàn bộ 188 test là HTTP hoặc PostgreSQL.
- Coverage riêng namespace `OISM.Application.Features.Inventory.*`: **843/889 dòng = 94,83%**. Cách tính: hợp nhất các dòng theo `filename:line` từ hai báo cáo Cobertura, dòng có hits > 0 ở ít nhất một báo cáo được tính đã chạy. Đây không phải coverage toàn bộ backend.
- PostgreSQL 16 chạy trong container tạm riêng, cổng `127.0.0.1:55439`; không thao tác database làm việc cổng 5432.
- `/health/ready` trả 200, runtime role không phải admin, đủ 29 bảng và trigger ledger hiện diện. Swagger JSON trả 200.
- Các ca bổ sung gọi HTTP qua `WebApplicationFactoryWithPostgres` trên PostgreSQL thật, độc lập với số lượng 188 test của solution.

Bằng chứng cục bộ:

- Log toàn bộ test (`tmp/s2-handover-audit/tests.log`).
- Log các ca API bổ sung (`tmp/s2-handover-audit/api-final.log`).
- Mã harness tái hiện (`tmp/s2-handover-audit/Program.cs`).
- Project harness (`tmp/s2-handover-audit/Review.csproj`).
- Coverage: `tmp/s2-handover-audit/test-results/`.
- Dấu vân tay source đã review: `tmp/s2-handover-audit/backend-source-manifest.json`.

188 test xanh và 5 nhóm lỗi bên dưới có thể cùng tồn tại: những tình huống này chưa được bộ test hiện tại kiểm tra đầy đủ. Coverage dòng cao không chứng minh đã kiểm tra mọi tổ hợp đầu vào và thứ tự request.

## 2. Năm nhóm lỗi còn mở

### S2-H01 — P2 — Tạo phiếu nhập đồng thời cùng mã trả 500

- Phạm vi: BE1-03/04, Đăng Khoa.
- Vị trí: `GoodsReceiptService.cs:98` kiểm tra tồn tại trước khi ghi; `:138–139` thêm phiếu và SaveChanges không chuyển vi phạm unique sang lỗi nghiệp vụ. `GoodsReceiptsController.Create` không bắt `DbUpdateException` này.
- Tái hiện: hai request hợp lệ `POST /api/goods-receipts`, cùng tenant và `number`, cùng vượt qua kiểm tra tồn tại trước khi INSERT. Harness dùng khóa bảng tạm trong chính database thử nghiệm để điều khiển thứ tự thực thi; không thay đổi schema hay logic nghiệp vụ.
- Thực tế: **201 + 500**, lỗi PostgreSQL `23505`, constraint `uq_goods_receipts_2`. Database vẫn chỉ có **1 phiếu**, không có bằng chứng nhân đôi tồn kho.
- Cần sửa: xử lý chính xác xung đột unique mã phiếu thành 409, giữ transaction rollback; không bắt mọi lỗi database rồi biến thành 409.
- Tiêu chí đóng: hai request cùng mã → một 201, một 409; một phiếu và đủ các dòng đúng, không có dòng mồ côi. Giữ test tạo phiếu khác mã thành công và test xác nhận đồng thời đã có.

### S2-H02 — P2 — Retry reservation đã thành công bị từ chối khi hết tồn khả dụng

- Phạm vi: BE1-09, Đăng Khoa.
- Vị trí: `ReservationService.cs:86–104` kiểm tra tồn trước nhánh tìm reservation cũ ở `:145–169`.
- Tái hiện: giữ toàn bộ tồn khả dụng, sau đó gửi lại cùng `OrderId`, `OrderItemId`, branch, warehouse, SKU và quantity của lần thành công, reservation vẫn Active/chưa hết hạn.
- Thực tế: lần đầu **200**, lần gửi lại **409**, thông báo tồn khả dụng 0. Nhánh trả lại reservation cũ không chạy. Ca cũ kiểm tra retry khi còn nhiều tồn nên vẫn xanh.
- Ảnh hưởng: frontend retry sau timeout sẽ nhận báo thất bại dù giữ chỗ của chính yêu cầu đó đã thành công. Không phát hiện tăng Reserved lần hai.
- Cần sửa: trong khóa hiện có, kiểm tra quyền/quan hệ và reservation cũ trước khi yêu cầu đủ tồn cho một reservation mới.
- Tiêu chí đóng: giữ 1 đơn vị cuối rồi gửi lại cùng yêu cầu → 200 cùng ReservationId, Reserved không đổi; yêu cầu khác tranh đơn vị cuối vẫn bị từ chối; kiểm thử cả tuần tự và đồng thời.

### S2-H03 — P2 — Tìm kiếm bỏ sót barcode Code128 có chữ hoa

- Phạm vi: BE2-07, Nhựt/Đăng Khoa phụ trách sửa bổ sung.
- Vị trí: `PosCatalogController.cs:45–53` và `ProductsController.cs:37–40` chuyển từ khóa sang chữ thường nhưng so sánh với barcode nguyên bản bằng phép chứa phân biệt hoa/thường.
- Tái hiện: tạo SKU `BARCODE-A`, tên sản phẩm `BARCODE-A`, barcode `CODE128-REVIEW`. Tìm chính `CODE128-REVIEW` qua `/api/pos/search?query=...` hoặc `/api/products?search=...`.
- Thực tế: cả hai trả **200 với []**; `/api/pos/barcode/CODE128-REVIEW` trả **200 đúng SKU**. Vì vậy dữ liệu có tồn tại; lỗi nằm ở nhánh tìm kiếm.
- Cần sửa: giữ từ khóa nguyên bản cho barcode hoặc áp dụng quy tắc so sánh nhất quán đã chốt; không chỉ lowercase một phía. Endpoint quét barcode chính xác vẫn cần giữ đúng hợp đồng hiện tại.
- Tiêu chí đóng: barcode chữ hoa, chữ thường, hỗn hợp và EAN-13 tìm được theo quy tắc đã chọn; tên/SKU không trùng từ khóa barcode để tránh test đi qua nhánh khác; vẫn cô lập tenant.

### S2-H04 — P2 — Phần tử null trong danh sách request gây 500

- Phạm vi: BE1-03/04 và BE2-05/09.
- Vị trí: `ProductsController.cs:141` dereference `v.SKU`; `ProductDtos.cs:92` chỉ SetValidator cho item, chưa chặn item null. `GoodsReceiptService.cs:74` dereference `i.SkuId`; DTO phiếu nhập chỉ kiểm tra danh sách required/min length.
- Tái hiện: `POST /api/products` với tên hợp lệ và `variants: [null]`; hoặc `POST /api/goods-receipts` với các ID hợp lệ và `items: [null]`.
- Thực tế: cả hai trả **500 / NullReferenceException**.
- Cần sửa: từ chối phần tử null tại validation trước khi xử lý LINQ; giữ chính sách hiện có đối với danh sách variant rỗng, không tự thêm yêu cầu nghiệp vụ ngoài S2.
- Tiêu chí đóng: `[null]` và danh sách trộn item hợp lệ/null trả 400 có vị trí lỗi; không tạo dở product/receipt/items; payload hợp lệ vẫn thành công.

### S2-H05 — P2 — Validation SKU chưa khớp giới hạn cột database

- Phạm vi: BE2-05/06/09.
- Vị trí: `ProductDtos.cs:118–126`, `:144–152`, `:160–164`: giá chỉ kiểm tra không âm; barcode không giới hạn độ dài. Mapping `ProductVariantConfiguration.cs` quy định barcode tối đa 128 ký tự, giá `numeric(20,2)`.
- Tái hiện qua tạo product/SKU: barcode 300 ký tự ASCII; hoặc retailPrice = `1000000000000000000` (10^18, vượt số nguyên tối đa của numeric(20,2)).
- Thực tế: **500**, tương ứng PostgreSQL `22001` và `22003`. Đây là lỗi dữ liệu đầu vào, cần phản hồi 400 để frontend hiển thị chính xác.
- Cần sửa: đồng bộ giới hạn barcode và miền giá cho tạo SKU, sửa SKU và cập nhật bảng giá; chốt rõ xử lý phần thập phân với cột scale 2. Không nới schema chỉ để cho qua test.
- Tiêu chí đóng: test ngay sát/đúng/vượt ngưỡng, create và update, retail/wholesale, không có thay đổi dữ liệu khi bị từ chối. Hai ca 500 đã tái hiện trực tiếp ở create; các đường update dùng cùng loại quy tắc thiếu giới hạn cần bổ sung regression.

## 3. Đối chiếu phạm vi S2

| Nhóm | Trạng thái sau review |
|---|---|
| BE1-01/02: database, mapping, migration/baseline, seed, tenant guard | Các test hiện có đạt; readiness 200 trên database mới. CI bản bàn giao chưa xác minh. |
| BE1-03/04: hợp đồng lỗi, tạo/đọc phiếu nhập | Có chức năng; cần đóng H01 và H04. |
| BE1-05/06/07/08: confirm nguyên tử, ledger, WAC, API tồn | Các test rollback, deferred constraint, gửi lặp confirm, confirm đồng thời, ledger bất biến, số dư/WAC đạt. |
| BE1-09: reservation | Test 50 request tranh 1 đơn vị đạt; cần đóng H02. |
| BE1-10: kiểm thử lõi | Coverage đạt ngưỡng 80%; phải thêm regression cho lỗi mới, không chỉ dựa vào số test cũ. |
| BE2-01/02/03: đăng ký, Auth, RBAC | Các regression hiện tại đạt, gồm phone, refresh đồng thời, tenant bị khóa và Cashier bị chặn nghiệp vụ kho. |
| BE2-04: cây Category và Brand | Regression đạt; ca cập nhật cha đối ứng đồng thời trả 400/200, database không có vòng lặp. |
| BE2-05/06/07: Product/SKU, giá, barcode/POS | Có chức năng; cần đóng H03/H04/H05. Kiểu JSON/null lồng nhau, tổ hợp thuộc tính, barcode trùng vẫn đạt. |
| BE2-08: supplier lookup, branch/warehouse | Các regression hiện có đạt; đổi chi nhánh kho có lịch sử trả 409, kho sạch đổi được. |
| BE2-09: validation/test/tài liệu | Cần regression mới và đồng bộ trạng thái bàn giao. |

Không yêu cầu thêm checkout hoàn chỉnh hoặc supplier CRUD đầy đủ để đóng review này.

## 4. Việc cần làm trước khi chốt bàn giao

1. Đóng H01–H05 bằng regression HTTP/PostgreSQL tương ứng, chạy lại bộ test đầy đủ và coverage lõi.
2. Cập nhật báo cáo Đăng Khoa/Nhựt theo kết quả thực tế. README Nhựt hiện đã ghi 188; không còn cần sửa số 187. README Đăng Khoa còn `131/131` tại dòng 390 và câu dòng 528 mô tả toàn bộ 188 test là HTTP/PostgreSQL; cần phân biệt kết quả lịch sử và loại test. Những câu “hoàn tất toàn bộ lỗi” cần phản ánh 5 nhóm đang mở.
3. Bàn giao một revision cụ thể có đủ file mới chưa tracked (PhoneHelper, SkuAttributeHelper, PostgresBackend2RegressionTests) và chạy CI trên revision đó. Không đồng nhất working tree này với HEAD `2cc35c2`.
4. Frontend/tester nghiệm thu luồng đăng nhập → tạo SKU → lập/xác nhận phiếu nhập → đọc tồn/WAC/ledger → tìm kiếm POS. Review này đã kiểm tra API nhưng chưa kiểm tra thao tác qua UI frontend.

Không có thay đổi mã nghiệp vụ, commit hoặc push trong lần rà soát này. Các lỗi được báo dựa trên hành vi thực tế đã tái hiện; đây không phải khẳng định đã tìm được mọi lỗi có thể tồn tại.
