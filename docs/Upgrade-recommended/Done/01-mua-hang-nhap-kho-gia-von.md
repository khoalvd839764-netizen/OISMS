# 01. Mua hàng, nhập kho và giá vốn

> Trạng thái ngày 12/11/2026. Giai đoạn 01-A đã triển khai phiếu mua, phiếu nhập gắn phiếu mua và cập nhật WAC. Giai đoạn 01-B đã triển khai ngày 12/11/2026: hóa đơn nhà cung cấp, đối chiếu ba bên và chênh lệch có duyệt ([báo cáo 01-B](../../report-after-upgrade/hoa-don-nha-cung-cap-u01b.md)). Yêu cầu phiếu nhập phải có phiếu mua được GVHD ghi nhận ngày 01/11/2026. [Báo cáo triển khai 01-A](../../report-after-upgrade/mua-hang-nhap-kho.md) · [Quyết định 01-B](#quyết-định-01-b-chốt-12112026)

## Quyết định 01-B (chốt 12/11/2026)

Nhóm tự chốt các quyết định dưới đây để triển khai, chưa chờ GVHD xác nhận; báo GVHD khi nghiệm thu. Mục này thay thế các đề xuất U01-B ở phần sau nếu có chỗ khác nhau.

### Phạm vi

- Làm: nhập hóa đơn nhà cung cấp, đối chiếu ba bên (phiếu mua ↔ phiếu nhập ↔ hóa đơn), chứng từ chênh lệch có duyệt.
- Không làm: công nợ nhà cung cấp, phiếu chi, chặn thanh toán. OISMS chưa có phân hệ thanh toán nhà cung cấp.

### Hai trường hợp hóa đơn

Hóa đơn thường đi kèm hàng, đôi khi đến sau. Hỗ trợ cả hai:

| Trường hợp | Cách xử lý | Ảnh hưởng giá vốn |
| :--- | :--- | :--- |
| **Đi kèm hàng** (phiếu nhập còn nháp) | Nhập hóa đơn trong chi tiết phiếu nhập. Khớp thì xác nhận nhập như 01-A. Lệch thì tạo chênh lệch chờ Owner duyệt. Owner duyệt thì phiếu nhập xác nhận theo **giá hóa đơn**. Owner từ chối thì hóa đơn chuyển `Rejected` và phiếu nhập nháp bị hủy (trả hàng) **trong cùng transaction**, dùng logic hủy của 01-A. | Có: dòng sổ kho `IN` và WAC dùng giá hóa đơn đã duyệt |
| **Đến sau** (phiếu nhập đã xác nhận theo giá phiếu mua) | Nhập hóa đơn gắn với phiếu nhập đã xác nhận, đối chiếu, chênh lệch chờ Owner duyệt hoặc từ chối. | **Không**: chỉ lưu làm bằng chứng. WAC, sổ kho, `OrderItem.CostPrice` và báo cáo giữ nguyên |

Hệ quả: không cần bút toán chỉ điều chỉnh giá và không đổi ràng buộc của `inventory_transactions` (`type IN ('IN','OUT')`, `quantity > 0`, một chứng từ nguồn). Giá phiếu mua đã duyệt vẫn giữ nguyên để truy vết.

### Quy tắc đối chiếu

- **Liên kết:** một hóa đơn gắn với đúng một phiếu nhập; mỗi dòng hóa đơn khớp một dòng phiếu nhập (qua đó tới dòng phiếu mua).
- **Đơn giá:** so và làm giá nhập bằng đơn giá **trước thuế** theo đơn vị trên dòng phiếu mua; quy về đơn vị gốc bằng `FactorSnapshot` như 01-A.
- **Thuế GTGT và phí vận chuyển:** chỉ lưu ở cấp hóa đơn, không vào giá vốn, không tính là chênh lệch.
- **Số lượng:** tồn kho luôn theo số thực nhận. Hóa đơn tính nhiều hoặc ít hơn số nhận thì ghi vào chênh lệch làm bằng chứng, không đổi tồn.
- **Ngưỡng:** mọi chênh lệch giá hoặc lượng, dù nhỏ, đều phải duyệt. Không có cấu hình ngưỡng.

### Trạng thái hóa đơn

| Trạng thái | Ý nghĩa | Được chuyển sang |
| :--- | :--- | :--- |
| `Draft` | Đang nhập, được sửa; không xóa hẳn, bỏ thì hủy | `Matched`, `PendingApproval`, `Cancelled` |
| `Matched` | Khớp phiếu mua và phiếu nhập | `Cancelled` |
| `PendingApproval` | Có chênh lệch chờ Owner | `Approved`, `Rejected` |
| `Approved` | Owner đã duyệt chênh lệch | `Cancelled` |
| `Rejected` | Owner từ chối chênh lệch | Không |
| `Cancelled` | Hủy kèm lý do để nhập lại | Không |

Hóa đơn không bị xóa khỏi database: trigger chặn `DELETE` giống `purchase_order_no_delete`, bỏ hóa đơn nháp bằng cách hủy. Gửi đối chiếu từ `Draft` sẽ khóa hóa đơn và tự chuyển `Matched` hoặc `PendingApproval`. Sau khi khóa chỉ hủy kèm lý do rồi nhập hóa đơn mới. Hóa đơn gắn với phiếu nhập **đã xác nhận** không được hủy để đổi giá vốn; hủy chỉ đổi trạng thái bằng chứng. `UNIQUE (tenant_id, supplier_id, ký hiệu, số hóa đơn)` chặn nhập trùng (hóa đơn đã hủy không tính); API tạo hóa đơn nhận `Idempotency-Key`.

### Xác nhận phiếu nhập

- Duyệt chênh lệch và xác nhận nhập là **hai bước**. Duyệt chỉ đổi trạng thái hóa đơn và chênh lệch.
- `ConfirmReceiptAsync`, trong cùng transaction: hóa đơn `PendingApproval` hoặc `Draft` thì chặn; `Approved` thì dùng giá hóa đơn; `Matched` hoặc không có hóa đơn thì dùng giá phiếu mua như 01-A.
- Thứ tự khóa: phiếu nhập → hóa đơn → phiếu mua → SKU. Mọi thao tác ghi hóa đơn (tạo, sửa, gửi đối chiếu, duyệt, từ chối, hủy) cũng khóa **phiếu nhập trước** rồi mới khóa hóa đơn, để không chạy chồng với xác nhận nhập.

### Phân quyền

- `Staff`, `Owner` nhập, sửa nháp và gửi đối chiếu hóa đơn.
- Chỉ `Owner` duyệt hoặc từ chối chênh lệch; Owner tự nhập hóa đơn vẫn phải duyệt riêng.
- `Cashier` bị từ chối. Staff được xem giá trên hóa đơn theo [ma trận quyền](../../01-users-and-rbac/02-rbac-matrix.md).

### Giao diện Admin

- Nút **Nhập hóa đơn NCC** trong chi tiết phiếu nhập; form điền sẵn dòng từ phiếu nhập; bảng so ba cột: phiếu mua / thực nhận / hóa đơn.
- Danh sách **Chênh lệch chờ duyệt** cho Owner.

### Việc kỹ thuật kèm theo

- Bảng mới `supplier_invoices`, `supplier_invoice_items`, `purchase_variances` (có `tenant_id`, query filter), migration mới, cập nhật `oisms_schema.sql`, `RequiredTables`, `init-oisms.sh` (44 → 47 bảng).
- Mã lỗi mới vào `docs/12-error-and-status-codes/` (ví dụ hóa đơn trùng, chênh lệch chưa duyệt, hóa đơn sai trạng thái).
- Integration test PostgreSQL thật: xác nhận nhập theo giá hóa đơn đã duyệt; chặn xác nhận khi chênh lệch chờ duyệt; duyệt/hủy chạy song song với xác nhận; hóa đơn đến sau không đổi WAC và sổ kho; tenant khác và Cashier bị từ chối.

## Cập nhật 02/11/2026: đề xuất triển khai

Mục này là **đề xuất tạm thời, chưa triển khai**, đã đối chiếu với code ngày 02/11/2026. Chỉ yêu cầu đầu tiên đến từ GVHD; các phần còn lại là đề xuất để nhóm chốt.

### Yêu cầu và hướng làm

- **Yêu cầu của GVHD:** người dùng nhập kho thì phải có phiếu mua hàng kèm theo để đối chiếu.
- **Hướng làm:** theo luồng của Odoo, tạo đơn mua trước rồi nhập kho từ đơn đó. Odoo không bắt buộc đơn mua trong mọi trường hợp; việc chặn nhập khi không có đơn mua là quy tắc riêng của OISMS.
- **Giá trên đơn mua là giá nhập** dùng để tính WAC, không phải giá bán. Giá bán đi theo bảng giá ở [tài liệu 04](../Done/04-bang-gia-theo-thoi-gian.md).

### Luồng đề xuất

| Bước | Người làm | Ví dụ |
| :--- | :--- | :--- |
| 1. Tạo đơn mua | Staff hoặc Owner | Chi nhánh Q1 mua 100 kg gạo A, giá 12.000đ/kg |
| 2. Duyệt đơn mua | Owner | Từ đây đơn không sửa được; giá và hệ số quy đổi được khóa làm căn cứ |
| 3. Nhập kho từ đơn mua | Staff hoặc Owner | Chọn đơn và kho nhận, ghi thực nhận 60 kg, không sửa được giá |
| 4. Xác nhận nhập | Backend | Tồn +60 kg, ghi sổ kho `IN` theo giá 12.000đ/kg, tính lại WAC |
| 5. Nhập đợt sau | Staff hoặc Owner | Còn được nhận tối đa 40 kg; nhập quá thì bị chặn |
| 6. Kết thúc đơn | Backend hoặc Owner | Nhận đủ thì tự chuyển `Received`; nhà cung cấp giao thiếu hẳn thì Owner đóng phần còn lại và ghi lý do |

### Trạng thái đơn mua

| Trạng thái | Ý nghĩa | Được chuyển sang |
| :--- | :--- | :--- |
| `Draft` | Đang lập, được sửa | `Approved`, `Cancelled` |
| `Approved` | Đã duyệt, chưa nhận hàng | `PartiallyReceived`, `Received`, `Closed`, `Cancelled` |
| `PartiallyReceived` | Đã nhận một phần | `Received`, `Closed` |
| `Received` | Đã nhận đủ | Không |
| `Closed` | Đóng phần chưa nhận, bắt buộc ghi lý do | Không |
| `Cancelled` | Hủy khi chưa nhận hàng | Không |

Chỉ xác nhận được phiếu nhập khi đơn mua đang ở `Approved` hoặc `PartiallyReceived`. Staff lập đơn thì Owner duyệt; Owner tự lập thì được duyệt ngay, giống cách làm của phiếu đổi giá ở Upgrade 04.

Khi giá thực tế khác giá trên đơn mua:

- **Biết trước khi nhập hàng:** không sửa đơn đã duyệt. Đóng hoặc hủy đơn cũ, tạo đơn mới và duyệt lại, rồi nhập theo đơn mới.
- **Chỉ biết khi hóa đơn đến, sau khi đã nhập:** không sửa phiếu nhập, sổ kho hay WAC đã ghi; ghi chứng từ chênh lệch có người duyệt. Phần này thuộc U01-B và vẫn cần GVHD chốt cách xử lý.

### Phạm vi

Tách Upgrade 01 thành hai phần để nghiệm thu riêng:

| Phần | Nội dung |
| :--- | :--- |
| **U01-A (làm trước)** | Đơn mua và các dòng hàng, bước duyệt, đóng và hủy đơn, liên kết dòng nhập với dòng đơn mua, hủy phiếu nhập nháp, kiểm tra ở backend, giao diện Admin |
| **U01-B (làm sau)** | Hóa đơn nhà cung cấp, đối chiếu ba bên, chứng từ chênh lệch và cách phân bổ chênh lệch vào giá vốn |

- **Đơn vị tính:** backend [tài liệu 05](../Done/05-nhieu-don-vi-tinh.md) đã có, nên dòng đơn mua có đơn vị và hệ số ngay từ U01-A. Mọi phép so lượng tính theo đơn vị gốc.
- **Một SKU một dòng:** U01-A cho mỗi SKU một dòng trên đơn mua, khớp ràng buộc `uq_goods_receipt_items_2` của phiếu nhập. Đổi lại, một đơn không mua được cùng SKU theo hai đơn vị.
- **Thứ tự:** làm sau Upgrade 05 và 04, đúng kế hoạch Sprint 4.
- **Giới hạn cần nói rõ:** U01-A bảo đảm giá nhập là **giá đã được duyệt**, chưa phải bằng chứng giá thực trả. Phần đối chiếu với giá thực trả đến ở U01-B, khi có hóa đơn.

### Ảnh hưởng đến code (ước lượng 02/11/2026)

Không cần tái cấu trúc lớn: chủ yếu thêm code mới và sửa ở luồng nhập kho.

| Phần | Mức thay đổi | Việc cần làm |
| :--- | :--- | :--- |
| Bảng, entity, service, API, trang đơn mua | Thêm mới | Làm theo mẫu của `SupplierService` và luồng duyệt phiếu đổi giá |
| `goods_receipt_items` | Sửa nhỏ | Thêm cột liên kết dòng đơn mua; chỉ dữ liệu cũ được để trống |
| `CreateGoodsReceiptDto`, `CreateReceiptAsync` | Sửa vừa | Nhận dòng đơn mua và lượng thực nhận; lấy nhà cung cấp, chi nhánh, giá, đơn vị và hệ số từ đơn, không dùng `ResolveForPurchase` và không tin client |
| `ConfirmReceiptAsync` | Sửa ít, khó nhất | Khóa đơn mua, kiểm tra trạng thái, chặn nhập vượt, cộng lượng đã nhận, cập nhật trạng thái đơn |
| `GoodsReceiptsController`, `IGoodsReceiptService` | Sửa nhỏ | Thêm API hủy phiếu nháp; `Confirm` phải trả đúng mã lỗi `OismException` |
| `InventoryPage.tsx`, `apiAdapter.ts` | Sửa vừa | Dòng phiếu lấy từ đơn mua; bỏ thêm SKU tự do và các chỗ tự điền giá; hiện đơn vị; khóa ô giá; thêm hàm gọi API |
| `WacCalculator`, sổ kho, `OrderService`, POS, chuyển kho | Không đổi | |

Ước lượng khối lượng: khoảng 5–7 ngày cho cả backend, frontend và test nếu quy tắc đã chốt.

Năm điểm cần cẩn thận:

1. **Test:** 12 file integration test gọi tạo hoặc xác nhận phiếu nhập, nhiều file dùng phiếu nhập để tạo sẵn tồn kho. Nên gom về một hàm dùng chung tự tạo đơn mua đã duyệt.
2. **Nhập đồng thời:** hai phiếu cùng nhập từ một đơn mua có thể vượt số lượng, và lệnh hủy hoặc đóng đơn có thể chạy song song với xác nhận nhập. Dùng advisory lock theo đơn mua qua `ISqlLockService` cho cả nhập, hủy và đóng đơn; thứ tự khóa là phiếu nhập → đơn mua → SKU. Thêm `CHECK` lượng đã nhận không vượt lượng đặt làm chốt chặn cuối, và kiểm chứng trên PostgreSQL thật vì EF InMemory không chạy khóa.
3. **Phiếu nháp chưa giữ lượng:** nhiều phiếu nháp có thể cùng trỏ một dòng đơn mua. Lúc lập phiếu chỉ kiểm tra để báo sớm; kết luận cuối cùng là lúc xác nhận, sau khi đã có khóa.
4. **Phiếu nháp cũ:** phiếu chưa gắn đơn mua sẽ không xác nhận được nữa, mà hiện chưa có API hủy phiếu nhập. Cần có API hủy trước khi bật quy tắc bắt buộc, và kiểm tra số phiếu nháp cũ trước khi nâng cấp.
5. **Cập nhật database đang chạy:** dự án không dùng EF Migrations mà dùng script SQL trong `docker/postgres/migrations/`, do admin chạy thủ công sau khi sao lưu (xem [hướng dẫn Sprint 3](../../07-devops-and-deployment/02-update-backend-sprint3.md)). Upgrade này cần thêm một script mới và cập nhật `oisms_schema.sql`, danh sách `RequiredTables` (lúc viết có ở cả `Program.cs` và `DatabaseMigrationService.cs`; hiện chỉ còn `DatabaseMigrationService.cs`), phần kiểm tra schema và `init-oisms.sh`.

### Câu hỏi cần hỏi GVHD

| Câu hỏi | Đề xuất |
| :--- | :--- |
| "Phiếu mua" là chứng từ tạo và duyệt trong hệ thống, hay chỉ cần đính kèm hoặc ghi số phiếu giấy? | Tạo và duyệt trong hệ thống, vì chỉ ghi số phiếu thì không đối chiếu tự động được |
| Giá hoặc số lượng thực nhận lệch so với phiếu mua thì xử lý thế nào? | Chặn nhập vượt số lượng và không cho sửa giá khi nhập; U01-A không có ngoại lệ, hàng dư hoặc giá khác thì lập phiếu mua mới |
| Có áp dụng cho mọi loại nhập không? | Chỉ nhập từ nhà cung cấp; chuyển kho và kiểm kê đã có chứng từ riêng, trả hàng sẽ có chứng từ riêng khi làm CS-01 |
| Mua gấp ngoài kế hoạch thì sao? | Owner tạo và duyệt nhanh một phiếu mua rồi nhập từ phiếu đó, không mở ngoại lệ nhập không có phiếu |
| Tồn đầu kỳ khi bắt đầu dùng hệ thống nhập thế nào? | Dùng kiểm kê làm đường chính thức, không cần phiếu mua. Lưu ý: khi SKU chưa có WAC, người kiểm kê tự nhập giá vốn, và chưa có bước duyệt cho đến khi làm Upgrade 03 |
| Giá trên phiếu mua gồm những gì? | U01-A dùng giá thực mua sau chiết khấu; thuế và phí vận chuyển để U01-B |
| Hàng khuyến mại giá 0 có được nhập không? | Có, nhưng Owner phải duyệt rõ trên phiếu mua, vì loại hàng này kéo WAC xuống |

## Vấn đề cần giải quyết

Giá vốn hiện được tính bằng WAC khi xác nhận phiếu nhập, nhưng đơn giá nhập do người lập phiếu cung cấp. Công thức WAC đúng về mặt số học vẫn cho kết quả sai nếu đơn giá đầu vào không được đối chiếu với giao dịch mua thực tế. Phải phân biệt **phương pháp tính giá vốn** với **nguồn chứng cứ của giá mua**.

## Hiện trạng đã kiểm tra

- `CreateGoodsReceiptDto` nhận `SupplierId`, SKU, số lượng và `UnitPrice`; chưa có tham chiếu phiếu mua hoặc hóa đơn nhà cung cấp. Xem [GoodsReceiptDtos.cs](../../../src/backend/src/OISM.Application/Features/Inventory/Dtos/GoodsReceiptDtos.cs).
- Khi xác nhận, `GoodsReceiptService` đưa `GoodsReceiptItem.UnitPrice` vào WAC và ghi cùng giá vào dòng sổ kho `IN`. Xem [GoodsReceiptService.cs](../../../src/backend/src/OISM.Application/Features/Inventory/Services/GoodsReceiptService.cs).
- Giao diện nhập kho cho sửa đơn giá và có thể gợi ý bằng WAC cũ hoặc một giá mặc định. Đây không phải chứng cứ giá mua. Xem [InventoryPage.tsx](../../../src/frontend/admin-dashboard/src/pages/InventoryPage.tsx).
- Tài liệu yêu cầu hiện có quy định WAC sau xác nhận phiếu nhập, chưa định nghĩa chuỗi đối chiếu mua hàng. Xem [FR-03](../../02-functional-requirements/fr-03-inventory-ledger.md) và [FR-04](../../02-functional-requirements/fr-04-cost-wac.md).

## Tham khảo Odoo và KiotViet

| Hệ thống | Cách xử lý được tài liệu chính thức mô tả | Điều cần rút ra |
| :--- | :--- | :--- |
| Odoo | Cho chọn chính sách lập hóa đơn nhà cung cấp theo lượng đặt hoặc lượng đã nhận. Khi dùng lượng đã nhận, tính năng đối chiếu ba bên kiểm tra phiếu mua, lượng nhận và hóa đơn trước **thanh toán**; tài liệu không nói mọi lần nhận hàng đều buộc có hóa đơn trước. | Tách thời điểm nhận hàng khỏi thời điểm hóa đơn đến và thời điểm duyệt thanh toán. |
| KiotViet | Có thể tạo phiếu nhập từ phiếu đặt hàng nhập, lấy hàng đã đặt làm nền rồi ghi lượng thực nhận. Tài liệu cũng hướng dẫn tạo phiếu nhập trực tiếp và nhập số lượng, đơn giá. | Liên kết phiếu mua là luồng được hỗ trợ, nhưng việc bắt buộc liên kết cho OISMS phải là quy tắc riêng của dự án. |

Nguồn: [Odoo — Manage vendor bills](https://www.odoo.com/documentation/19.0/applications/inventory_and_mrp/purchase/manage_deals/manage.html), [KiotViet — Đặt hàng nhập](https://www.kiotviet.vn/huong-dan-su-dung-kiotviet/retail-giao-dich/dat-hang-nhap/), [KiotViet — Nhập hàng](https://www.kiotviet.vn/huong-dan-su-dung-kiotviet/retail-giao-dich/nhap-hang/). Đối chiếu tài liệu ngày 29/10/2026.

**Đề xuất cho OISMS:** bắt buộc mọi phiếu nhập **mua từ nhà cung cấp** gắn với phiếu mua đã duyệt trước khi xác nhận; so SKU, nhà cung cấp, đơn vị quy đổi, lượng còn được nhận và giá dự kiến. Đây là quy tắc chặt hơn KiotViet và không nên áp sang nhập do trả hàng/chuyển kho/điều chỉnh kiểm kê, vì các loại nhập đó có chứng từ nguồn khác. Hóa đơn nhà cung cấp có thể đến sau; chỉ chốt đối chiếu và thanh toán khi khớp hoặc chênh lệch đã được duyệt.

### Hai hệ thống có dùng WAC không?

**Có.** Odoo gọi phương pháp này là **Average Cost (AVCO)**, có thể chọn theo nhóm sản phẩm bên cạnh FIFO và giá chuẩn. Khi xác nhận nhận hàng, Odoo tính lại bình quân từ lượng tồn và giá trị lô nhập; xuất hàng không tự làm đổi đơn giá bình quân. [Nguồn Odoo — Average price on returned goods](https://www.odoo.com/documentation/19.0/applications/finance/accounting/get_started/avg_price_valuation.html) và [Inventory valuation](https://www.odoo.com/documentation/19.0/applications/finance/accounting/get_started/inventory_valuation.html).

KiotViet gọi là **bình quân gia quyền tức thời/liên hoàn**, tính lại sau mỗi lần nhập hàng. Bài giới thiệu tính năng năm 2024 cũng nêu lựa chọn **giá vốn cố định/tiêu chuẩn**; do đó không nên nói KiotViet chỉ có một phương pháp trong mọi phiên bản/gói. [Nguồn KiotViet — Thêm lựa chọn phương pháp quản lý giá vốn](https://www.kiotviet.vn/them-lua-chon-phuong-phap-quan-ly-gia-von/).

Kết luận cho OISMS: **giữ WAC** nếu hàng cùng SKU có thể thay thế nhau; thay đổi quan trọng là khóa nguồn giá nhập bằng phiếu mua/đối chiếu hóa đơn, ghi chứng từ chênh lệch và dùng số lượng đã quy đổi về đơn vị tồn cơ sở. WAC không tự chứng minh lô hoặc hóa đơn mua cụ thể của từng đơn vị xuất; yêu cầu đó cần truy vết lô/serial và chính sách giá vốn riêng.

## Luồng nghiệp vụ đề xuất

Bước 1–3 thuộc U01-A; bước 4–5 thuộc U01-B.

1. **Phiếu mua hàng**: chọn nhà cung cấp, chi nhánh, SKU, đơn vị mua, số lượng, đơn giá đã thương lượng, chiết khấu và các điều khoản liên quan. Người có quyền duyệt; bản đã duyệt là nguồn giá dự kiến.
2. **Phiếu nhập kho**: chọn phiếu mua đã duyệt; hệ thống tải dòng hàng và giá từ phiếu mua. Staff hoặc Owner ghi số lượng thực nhận, kho nhận và tình trạng hàng. Hệ thống đối chiếu số lượng còn được nhận, SKU, nhà cung cấp và đơn vị quy đổi. Không cho nhập đơn giá tùy ý ngay tại bước xác nhận.
3. **Xác nhận nhập**: tăng tồn, ghi sổ kho `IN` và tính lại WAC theo giá vốn nhập đã được chấp nhận. Mỗi dòng nhập tham chiếu dòng phiếu mua.
4. **Hóa đơn nhà cung cấp**: ghi số hóa đơn, ngày, nhà cung cấp và từng dòng. Đối chiếu ba bên: phiếu mua ↔ lượng thực nhập ↔ hóa đơn. Hóa đơn đến sau hàng vẫn được ghi nhận; chênh lệch phải đi qua quy trình xử lý riêng.
5. **Chênh lệch**: nếu giá hoặc lượng ngoài ngưỡng, đưa vào trạng thái cần duyệt. Không sửa âm thầm phiếu nhập, WAC hoặc sổ kho đã xác nhận.

### Trường hợp hóa đơn đến sau hàng

Đề xuất cho phép nhập kho theo **giá tạm tính từ phiếu mua đã duyệt** để không chặn nhận hàng. Khi hóa đơn đến, tạo chứng từ chênh lệch có số tham chiếu và người duyệt. Cách phân bổ chênh lệch giữa hàng còn tồn và hàng đã bán, cũng như việc điều chỉnh báo cáo kỳ trước, cần chốt riêng với chính sách kế toán của dự án. Không ghi đè giá vốn snapshot của đơn hàng cũ mà không có chứng từ điều chỉnh.

> **Đã chốt 12/11/2026:** chênh lệch của hóa đơn đến sau chỉ được duyệt và lưu làm bằng chứng, không phân bổ vào giá vốn. Xem [Quyết định 01-B](#quyết-định-01-b-chốt-12112026).

## Dữ liệu và API dự kiến

| Thành phần | Phần | Thông tin tối thiểu |
| :--- | :--- | :--- |
| `purchase_orders` | U01-A | Tenant, số phiếu, nhà cung cấp, chi nhánh, trạng thái, người lập, người duyệt và thời điểm duyệt, người đóng và lý do đóng |
| `purchase_order_items` | U01-A | SKU, đơn vị và hệ số quy đổi snapshot, lượng đặt, giá mua theo đơn vị mua, lượng đã nhận theo đơn vị gốc; mỗi SKU một dòng trên một phiếu; `CHECK` lượng đã nhận không vượt lượng đặt quy đổi |
| `goods_receipt_items` | U01-A | Tham chiếu dòng phiếu mua (chỉ dữ liệu cũ được trống), lượng thực nhận; đơn vị, hệ số và đơn giá chép từ dòng phiếu mua. Khóa ngoại gồm `tenant_id` và `sku_id` để database cũng chặn lệch tenant, lệch SKU |
| `supplier_invoices`, `supplier_invoice_items` | U01-B | Số hóa đơn, nhà cung cấp, ngày, dòng tham chiếu phiếu mua/phiếu nhập, giá, lượng, trạng thái đối chiếu |
| `purchase_variances` | U01-B | Chứng từ chênh lệch, lý do, số tiền, trạng thái duyệt, liên kết các chứng từ nguồn |

Chi nhánh nằm trên phiếu mua vì WAC tính theo chi nhánh; kho nhận được chọn khi lập phiếu nhập và phải thuộc chi nhánh đó.

API của U01-A gồm tạo, sửa bản nháp, duyệt, đóng và hủy phiếu mua; lập phiếu nhập từ phiếu mua; hủy phiếu nhập nháp. U01-B thêm đối chiếu hóa đơn và xử lý chênh lệch. Ràng buộc phải nằm ở backend; khóa ô nhập trên giao diện không đủ bảo vệ dữ liệu. Phân quyền theo vai trò `Owner`, `Staff`; `Cashier` bị từ chối. Mã lỗi mới (ví dụ nhập vượt, phiếu mua sai trạng thái) phải thêm vào catalog `docs/12-error-and-status-codes/`.

## Quy tắc cần bảo vệ

- Không xác nhận phiếu nhập vượt lượng mua còn lại; U01-A không có ngoại lệ.
- Một phiếu mua có thể được giao nhiều lần; tổng lượng đã nhận theo đơn vị gốc không được vượt lượng đã duyệt.
- Phiếu mua đã duyệt không sửa được giá, lượng, đơn vị hay hệ số. Phiếu nhập dùng đơn vị, hệ số và giá của dòng phiếu mua, không tra lại cấu hình hiện tại.
- Phiếu mua đã `Closed` hoặc `Cancelled` thì phiếu nhập nháp trỏ tới nó không xác nhận được. Phiếu đã có hàng nhận chỉ được đóng, không được hủy.
- Giá mua, giá bán và WAC là ba khái niệm riêng. Giá bán không được dùng làm giá vốn nhập.
- Xử lý retry phải tránh cộng tồn hoặc hạch toán chênh lệch hai lần. Tạo phiếu mua nhận `Idempotency-Key`; retry nội bộ khi xác nhận nhập phải tính lại lượng đã nhận từ dữ liệu mới nhất.
- Chứng từ đã xác nhận bất biến; sửa sai bằng hủy/điều chỉnh có lịch sử.

## Tiêu chí nghiệm thu đề xuất

**U01-A**

1. Người lập phiếu nhập không thể xác nhận với SKU hoặc giá không được phiếu mua đã duyệt cho phép; gửi trực tiếp API với giá khác cũng không có tác dụng. *Từ U01-B, ngoại lệ duy nhất là giá trên hóa đơn nhà cung cấp có chênh lệch đã được Owner duyệt (tiêu chí 9).*
2. Giao hàng nhiều đợt cộng đúng lượng nhận và WAC theo đơn vị gốc, kể cả khi mua theo đơn vị quy đổi, và không vượt lượng đã mua.
3. Nhiều phiếu nhập xác nhận đồng thời từ một phiếu mua không vượt lượng đã mua; xác nhận chạy song song với hủy hoặc đóng phiếu cho kết quả nhất quán. Chứng minh bằng test trên PostgreSQL thật.
4. Đổi hệ số quy đổi của SKU sau khi duyệt không làm đổi lượng còn được nhận hay giá vốn của phiếu mua đó.
5. Từ một dòng sổ kho `IN` truy được phiếu nhập và phiếu mua tương ứng.
6. Tenant khác không xem hay dùng được phiếu mua; `Cashier` bị từ chối; Staff không tự duyệt được.

**U01-B**

7. Hóa đơn đến sau được đối chiếu; chênh lệch có người duyệt và không làm mất dấu giá nhập ban đầu.
8. Từ một dòng sổ kho `IN` truy được đến hóa đơn tương ứng nếu hóa đơn đã đến.
9. Hóa đơn đi kèm hàng lệch giá: phiếu nhập không xác nhận được khi chênh lệch còn chờ duyệt; Owner duyệt thì dòng `IN` và WAC dùng giá hóa đơn trước thuế; Owner từ chối thì phiếu nhập nháp bị hủy và tồn không đổi.
10. Hóa đơn đến sau lệch giá hoặc lượng: chênh lệch được duyệt hoặc từ chối nhưng WAC, sổ kho, giá vốn trên đơn bán và báo cáo không đổi.
11. Nhập trùng ký hiệu và số hóa đơn của cùng nhà cung cấp bị chặn; retry cùng `Idempotency-Key` trả kết quả cũ.
12. Staff không duyệt được chênh lệch, Cashier bị từ chối mọi API hóa đơn, tenant khác không thấy hóa đơn. Duyệt hoặc hủy chạy song song với xác nhận nhập cho kết quả nhất quán, chứng minh trên PostgreSQL thật.

## Quyết định còn mở

Ngưỡng chênh lệch, thuế và phí vận chuyển, thời điểm hóa đơn đã chốt ngày 12/11/2026 tại [Quyết định 01-B](#quyết-định-01-b-chốt-12112026). Còn lại:

- Giữ WAC theo yêu cầu hiện tại hay chuyển sang FIFO/giá đích danh nếu cần truy đúng lô mua của từng đơn vị hàng xuất.
