# 05. Nhiều đơn vị tính cho hàng hóa vật lý

> **Quyết định đã chốt trong trao đổi:** chọn **phương án B — một SKU có một đơn vị tồn kho gốc và nhiều đơn vị mua/bán quy đổi về đơn vị gốc**. Tài liệu này là kế hoạch triển khai đề xuất; nhóm dự án vẫn cần duyệt chi tiết hợp đồng API, độ chính xác và lịch phát hành trước khi viết mã.
>
> Phạm vi chỉ gồm hàng hóa vật lý. Dịch vụ, bản quyền theo thời gian, bao cân biến thiên và tồn độc lập cho bao nguyên không nằm trong đợt triển khai này.

## Trạng thái triển khai (02/11/2026)

**Tóm tắt:** mới xong phần nền (P1). Người dùng chỉ cấu hình được đơn vị cho SKU qua API; đơn hàng và phiếu nhập mới vẫn tính theo đơn vị gốc, hệ số 1.

### Đã xong (P1 — nền backend)

- [x] Bảng `units`, `sku_units`, `sku_unit_factor_history`, kèm FK ghép tenant.
- [x] `skus.base_unit_id`; `unit_id`, `unit_name_snapshot`, `factor_snapshot`, `base_quantity` trên `order_items` và `goods_receipt_items`.
- [x] Migration `docker/postgres/migrations/20261102_multi_unit.sql`, chạy lại an toàn; backfill hệ số 1, không sửa ledger.
- [x] Đồng bộ `oisms_schema.sql`, `init-oisms.sh`, `RequiredTables`, `DatabaseMigrationService`, quyền `oisms_runtime`.
- [x] Trigger: `oisms_check_stock` so giữ chỗ với `base_quantity`; đơn đã xác nhận không đổi được đơn vị và hệ số; lịch sử hệ số chỉ thêm.
- [x] Một đơn có thể chứa cùng SKU ở hai đơn vị (ở mức schema).
- [x] API `/api/units` và API đơn vị theo SKU, đúng RBAC; bộ đơn vị mặc định theo tenant.
- [x] Quy tắc danh mục: nhóm đo tương thích, hệ số hợp lệ, mặc định mua/bán, mã vạch không trùng, khóa đổi đơn vị gốc, lịch sử hệ số có người đổi.
- [x] `SkuUnitResolver` (bộ phân giải đơn vị, chưa được luồng nhập, bán gọi tới).
- [x] Docs: API, mã lỗi, RBAC, từ điển dữ liệu, ERD.

Kiểm chứng ngày 02/11/2026 trên PostgreSQL 16: unit 169/169, integration 300/300 trên bản làm việc; migration chạy 2 lần trên DB mẫu schema cũ, số liệu ledger, số dư, đơn hàng không đổi.

### Chưa xong

**P2 — lõi backend**

- [ ] Nhập kho theo đơn vị: `GoodsReceiptService` tính `BaseQuantity`, tồn và WAC theo đơn vị gốc (bước 5).
- [ ] Trigger chặn đổi hệ số, đơn vị trên phiếu nhập đã xác nhận.
- [ ] Đơn hàng và POS: chọn đơn vị trên từng dòng, tách dòng theo `(SKU, đơn vị)`, snapshot hệ số (bước 6).
- [ ] Giữ chỗ, xuất kho, COGS theo `BaseQuantity`: `OrderService`, `ReservationService` (bước 6).
- [ ] Giá bán theo đơn vị, báo lỗi khi đơn vị chưa có giá (giá tạm, xem bước 4).
- [ ] POS catalog: trả danh sách đơn vị, quét mã vạch quy cách chọn đúng đơn vị.
- [ ] `ReportService`, `TotalCogs` tính theo lượng gốc (bước 7).
- [ ] Rà lại webhook sàn, chuyển kho, kiểm kê, ghi rõ chỉ nhập theo đơn vị gốc (bước 7).
- [ ] Docs nghiệp vụ và API đơn hàng, phiếu nhập, POS.

**P3 — giao diện**

- [ ] Admin: cấu hình đơn vị gốc và đơn vị quy đổi trên `ProductsPage` (bước 3).
- [ ] Admin: chọn đơn vị trên phiếu nhập và đơn hàng; hiển thị cả lượng giao dịch và lượng gốc (bước 5, 6).
- [ ] Admin: nhãn "theo đơn vị gốc" ở chuyển kho, kiểm kê, sổ kho (bước 7).
- [ ] POS: chọn đơn vị trên giỏ, khóa dòng theo `variantId + unitId`, in hóa đơn đúng đơn vị (bước 6).

**Phát hành (bước 8)**

- [ ] Chạy thử migration trên bản sao dữ liệu thật và đối soát.
- [ ] Kiểm thử E2E Admin, POS và backend.

**Phụ thuộc việc khác**

- [ ] Hoàn trả theo đơn vị: chờ CS-01.
- [ ] Bảng giá theo thời gian thay `sku_units.sale_price`: chờ [Upgrade 04](./04-bang-gia-theo-thoi-gian.md).

**Lỗi nhỏ còn lại (không chặn)**

- [ ] Đặt mặc định trên đơn vị đang tắt làm SKU mất mặc định.
- [ ] Bật lại đơn vị quy đổi sau khi đổi gốc vẫn giữ hệ số cũ.
- [ ] Dòng được chọn làm gốc mới đổi hệ số về 1 mà không ghi lịch sử.
- [ ] Đơn vị đang tắt trong danh mục vẫn thêm được vào SKU.

### Tiêu chí nghiệm thu mục 8

| # | Nội dung ngắn | Trạng thái |
| --- | --- | --- |
| 1 | Nhập 1 tấn, bán 2,5 kg và một bao, còn 972,5 kg | Chưa, cần P2 |
| 2 | Dòng bán bao đúng số lượng, giá, lượng gốc, giá vốn | Chưa, cần P2 |
| 3 | Giỏ POS hai đơn vị, quét mã vạch quy cách | Chưa, cần P2 và P3 |
| 4 | Không tin hệ số từ client, từ chối đơn vị chưa bật | Chưa; mới có resolver, chưa nối vào luồng bán |
| 5 | Đổi hệ số không ảnh hưởng chứng từ cũ | Một phần: trigger bảo vệ đơn đã xác nhận; chưa có phiếu nhập và hoàn trả |
| 6 | Mọi luồng dùng đúng lượng gốc | Chưa, cần P2 |
| 7 | Retry không tạo thêm tồn hoặc dòng sổ kho | Chưa kiểm với đơn vị khác gốc, cần P2 |
| 8 | Dữ liệu cũ giữ nguyên sau migration | Đạt trên DB mẫu về tồn, WAC, ledger, tiền; chưa đối soát trên bản sao dữ liệu thật |

### Cần chốt trước P2

Mặc định kỹ thuật đang áp dụng, chưa được xác nhận từng điểm:

- [ ] Giá theo đơn vị tạm lưu ở `sku_units.sale_price` đến khi có Upgrade 04; đơn vị gốc dùng giá bán lẻ của SKU.
- [ ] `base_unit_id`, `unit_id` cho phép NULL trong giai đoạn chuyển tiếp, NULL hiểu là đơn vị gốc.
- [ ] Mỗi SKU một dòng trên một phiếu nhập.
- [ ] SKU còn đơn vị quy đổi đang bật thì không được đổi đơn vị gốc.

## 1. Mục tiêu và ví dụ chuẩn

Mỗi SKU có một số dư tồn và một WAC theo **đơn vị gốc**. Người dùng được mua hoặc bán bằng các đơn vị đã cấu hình cho SKU; backend tự quy đổi. Giá bán được định nghĩa riêng theo đơn vị và thời gian hiệu lực.

| Giao dịch gạo A | Số lượng giao dịch | Hệ số về kg | Biến động kho | Giá trên chứng từ |
| :--- | ---: | ---: | ---: | :--- |
| Nhập | 1 tấn | 1.000 | +1.000 kg | 12.000.000đ/tấn, tương đương 12.000đ/kg trước chi phí khác |
| Bán lẻ | 2,5 kg | 1 | −2,5 kg | 15.000đ/kg |
| Bán bao | 1 bao 25 kg | 25 | −25 kg | 350.000đ/bao, không buộc bằng 25 lần giá/kg |

Sau ba giao dịch, tồn gạo A là **972,5 kg**. Hóa đơn vẫn phải hiển thị `1 bao 25 kg` hoặc `2,5 kg`, không thay bằng `25 kg` trên dòng bán bao.

## 2. Lý do chọn phương án B

| Cách xử lý | Kết luận cho đợt này |
| :--- | :--- |
| Người dùng tự quy đổi, hệ thống chỉ có một đơn vị/SKU | Không chọn: dễ nhập sai và mất đơn vị gốc trên chứng từ. |
| **Một SKU, tồn theo đơn vị gốc, nhiều đơn vị giao dịch** | **Chọn:** khớp mô hình tồn và WAC theo SKU hiện có; không tạo tồn kép; mở rộng số đơn vị theo từng SKU. |
| Mỗi quy cách là một SKU tồn riêng | Để sau: chỉ cần khi cửa hàng phải quản lý số bao nguyên độc lập và có nghiệp vụ đóng/mở bao. |
| Ghi khối lượng thực tế từng bao | Để sau: chỉ cần cho hàng cân biến thiên; hệ số cố định không đại diện được từng bao. |

[KiotViet](https://www.kiotviet.vn/huong-dan-su-dung-kiotviet/hotel-hang-hoa/hang-hoa-thuong/) mô tả đơn vị cơ bản, hệ số quy đổi và giá riêng cho đơn vị lớn; nhập `1 thùng = 24 chai` thì cộng 24 chai. [Odoo](https://www.odoo.com/documentation/19.0/applications/inventory_and_mrp/inventory/product_management/configure/uom.html) tách đơn vị tồn kho khỏi đơn vị mua/bán và quy đổi trên chứng từ. Đây là tham khảo về cách vận hành, không phải yêu cầu sao chép nguyên giao diện của hai hệ thống.

## 3. Hiện trạng OISMS ảnh hưởng đến triển khai

- `ProductVariant.Unit` hiện là chuỗi, mặc định `piece`; chưa có danh sách đơn vị và hệ số theo SKU. Xem [ProductVariant.cs](../../src/backend/src/OISM.Domain/Entities/ProductVariant.cs).
- Dòng nhập và dòng đơn có `Quantity`, `UnitPrice`, nhưng chưa có đơn vị giao dịch, hệ số snapshot hoặc `BaseQuantity`. Xem [GoodsReceiptItem.cs](../../src/backend/src/OISM.Domain/Entities/GoodsReceiptItem.cs) và [OrderItem.cs](../../src/backend/src/OISM.Domain/Entities/OrderItem.cs).
- `GoodsReceiptService` đang dùng trực tiếp `Quantity` để tăng tồn và `UnitPrice` để tính WAC; `OrderService` dùng lượng dòng đơn để giữ/trừ tồn và tính giá vốn. Hai luồng phải được sửa đồng bộ. Xem [GoodsReceiptService.cs](../../src/backend/src/OISM.Application/Features/Inventory/Services/GoodsReceiptService.cs) và [OrderService.cs](../../src/backend/src/OISM.Application/Features/Orders/Services/OrderService.cs).
- Số lượng của phiếu nhập, đơn và số dư dùng `numeric(18,3)`; kiểm kê/chuyển kho cũng kiểm tra tối đa ba chữ số thập phân. Với đơn vị gốc kg, mức nhỏ nhất hiện có thể biểu diễn là 0,001 kg = 1 g. Xem [GoodsReceiptItemConfiguration.cs](../../src/backend/src/OISM.Infrastructure/Persistence/Configurations/GoodsReceiptItemConfiguration.cs), [OrderItemConfiguration.cs](../../src/backend/src/OISM.Infrastructure/Persistence/Configurations/OrderItemConfiguration.cs) và [InventoryBalanceConfiguration.cs](../../src/backend/src/OISM.Infrastructure/Persistence/Configurations/InventoryBalanceConfiguration.cs).
- POS hiện nhận diện dòng giỏ bằng `variantId`; khi cùng SKU được bán theo kg và bao, khóa dòng phải gồm cả SKU và đơn vị. Xem [App.tsx](../../src/frontend/pos-app/src/App.tsx) và [CartTable.tsx](../../src/frontend/pos-app/src/components/CartTable.tsx).

## 4. Hợp đồng dữ liệu đã chọn

### 4.1. Danh mục và cấu hình SKU

| Thành phần | Trường/chức năng cần thêm | Quy tắc |
| :--- | :--- | :--- |
| `units` | Mã ổn định, tên, ký hiệu, nhóm đo lường, số chữ số lẻ cho phép, trạng thái | Ví dụ `kg`, `g`, `tấn`, `bao`; mã không phụ thuộc tên hiển thị. |
| SKU (`ProductVariant`) | `BaseUnitId`; giữ `Unit` cũ trong giai đoạn chuyển tiếp | Một SKU chỉ có một đơn vị gốc; không đổi đơn vị gốc sau khi có giao dịch nếu chưa có quy trình chuyển đổi dữ liệu. |
| `sku_units` | `SkuId`, `UnitId`, `FactorToBase`, cho mua/bán, mặc định mua/bán, mã vạch quy cách nếu dùng | Khóa duy nhất theo SKU và đơn vị đang hiệu lực; đơn vị gốc có hệ số 1. |
| Lịch sử hệ số | Phiên bản hoặc hiệu lực của cấu hình, người sửa, thời điểm sửa | Dòng chứng từ luôn lưu hệ số snapshot; không sửa lại giao dịch cũ. |

Đơn vị chuẩn có thể tái dùng mã chung; `bao`, `thùng`, `hộp` phải có hệ số **theo SKU**. Ví dụ bao gạo A 25 kg và bao gạo B 10 kg. Backend không cho quy đổi giữa hai nhóm đo lường không tương thích như kg và lít. Nhân viên bán chỉ thấy đơn vị đã bật cho SKU, không nhập hệ số bằng tay.

### 4.2. Dòng chứng từ và ý nghĩa của số lượng

Chọn hợp đồng rõ ràng để phép tính tiền không bị lẫn với phép tính kho:

| Trường | Ý nghĩa |
| :--- | :--- |
| `Quantity` | Số lượng theo **đơn vị giao dịch**: 1 bao, 2,5 kg, 1 tấn. Dòng tiền vẫn tính `Quantity × UnitPrice`. |
| `UnitId` và tên đơn vị snapshot | Đơn vị người dùng đã mua/bán; giữ để in chứng từ cũ. |
| `FactorSnapshot` | Số đơn vị gốc trong một đơn vị giao dịch tại thời điểm chốt. |
| `BaseQuantity` | `Quantity × FactorSnapshot`, được backend tính; dùng cho tồn, giữ chỗ, sổ kho và WAC. |
| `UnitPrice` | Giá của **một đơn vị giao dịch**, ví dụ đồng/bao; không lấy giá này nhân trực tiếp với lượng kg. |
| `CostPrice` trên dòng đơn | WAC chụp theo **một đơn vị gốc**; giá vốn dòng = `BaseQuantity × CostPrice`. |

Với phiếu nhập, giá trị dòng = `Quantity × UnitPrice`; đơn giá vốn để đưa vào WAC = giá trị vốn được chấp nhận của dòng / `BaseQuantity`. Nếu áp dụng phiếu mua hàng theo [tài liệu 01](./01-mua-hang-nhap-kho-gia-von.md), giá nhập và đơn vị nhập phải đến từ dòng phiếu mua đã duyệt, không do nhân viên kho tự đặt.

Backend tự tra `FactorToBase` từ cấu hình theo SKU và đơn vị; không tin `FactorSnapshot` hoặc `BaseQuantity` do client gửi. Phiếu nháp chụp hệ số khi tạo; nếu cấu hình đổi trước khi xác nhận, backend chặn và yêu cầu người lập rà soát/tạo lại phiếu thay vì lặng lẽ tính lượng mới. Đơn giữ chỗ chụp hệ số lúc tạo; POS chụp lúc checkout. Dòng đã chốt giữ snapshot bất biến kể cả khi cấu hình đơn vị thay đổi.

## 5. Các bước triển khai theo thứ tự phụ thuộc

| Bước | Phần phụ trách | Việc phải làm | Điều kiện hoàn thành |
| :--- | :--- | :--- | :--- |
| **1. Chốt quy tắc & hợp đồng API** | Nghiệp vụ + backend + frontend | Chốt đơn vị gốc, đơn vị cho phép, số lẻ, công thức tiền/WAC, ý nghĩa `Quantity`; thống nhất việc API cũ không gửi `UnitId` sẽ hiểu là đơn vị gốc trong giai đoạn chuyển tiếp. | Có ví dụ nhập tấn/bán kg/bao và các trường request/response được thống nhất. |
| **2. Cơ sở dữ liệu** | Backend/DB | Tạo `units`, `sku_units`, khóa/constraint theo tenant–SKU, thêm `BaseUnitId` cho SKU và các cột snapshot ở dòng nhập/đơn; tạo migration có thể chạy trên dữ liệu cũ. | Dữ liệu cũ vẫn đọc được; không có SKU hoặc đơn vị bị gán nhầm tenant. |
| **3. Danh mục sản phẩm** | Backend API + Admin UI | API quản lý đơn vị chung và đơn vị được phép theo SKU; kiểm tra nhóm đo, hệ số dương, mặc định mua/bán; giao diện ở trang sản phẩm để chọn đơn vị gốc và thêm quy cách. | Tạo được SKU gạo `kg`, cho phép `tấn` và `bao 25 kg`; đổi hệ số có lưu lịch sử. |
| **4. Giá bán theo đơn vị** | Backend giá + Admin UI | Gắn giá với `SKU + UnitId + thời gian hiệu lực` theo [tài liệu 04](./04-bang-gia-theo-thoi-gian.md); POS/đơn lấy giá từ backend. Không suy ra giá bao bằng giá/kg × 25 trừ khi chính sách quy định. | Tồn tại giá 15.000đ/kg và 350.000đ/bao cùng lúc, không chồng kỳ cho cùng khóa giá. |
| **5. Nhập kho và WAC** | Backend kho + Admin UI | Phiếu nhập cho chọn đơn vị đã bật; backend tính `BaseQuantity`, giá vốn trên đơn vị gốc; xác nhận ghi tồn và sổ kho bằng lượng gốc. Hiển thị cả lượng giao dịch và lượng quy đổi để người xác nhận kiểm tra. | Nhập 1 tấn tăng đúng 1.000 kg, WAC/kg đúng, retry không cộng tồn hai lần. |
| **6. Đơn hàng, giữ chỗ và POS** | Backend đơn hàng/POS + hai frontend | Cho mỗi dòng chọn đơn vị bán; tra giá đúng đơn vị; tiền dùng lượng giao dịch, giữ chỗ/trừ kho/COGS dùng lượng gốc. Giỏ POS phân biệt `(SKU, UnitId)`; hóa đơn giữ đơn vị đã bán. | Một giỏ có cùng SKU ở đơn vị kg và bao mà không gộp sai dòng hoặc trừ kho hai lần. |
| **7. Chuyển kho, kiểm kê, sổ kho, báo cáo và tích hợp** | Backend kho/báo cáo + Admin UI | Giai đoạn này chuyển kho và kiểm kê **nhập theo đơn vị gốc**, nhãn UI phải ghi rõ; ledger và số dư chỉ hiển thị lượng gốc, có tùy chọn quy đổi để xem. Rà lại báo cáo lợi nhuận dùng `BaseQuantity × CostPrice`. Kênh/webhook cũ mặc định đơn vị gốc; muốn bán quy cách khác phải có ánh xạ SKU–đơn vị riêng. | Không có đường ghi tồn nào còn coi `Quantity` giao dịch là lượng gốc; báo cáo và đơn cũ không đổi số. |
| **8. Di trú, kiểm thử và phát hành** | Backend + frontend + QA/DevOps | Backfill hệ số 1 và lượng gốc cho chứng từ cũ; kiểm tra SKU đang mặc định `piece`; chạy thử trên dữ liệu sao chép, đối soát tổng tồn và giá trị tồn; triển khai tương thích trước khi bật UI mới. | Sổ kho, tồn, WAC và doanh thu trước/sau di trú khớp; có phương án rollback trước khi mở giao dịch đa đơn vị. |

### Các vị trí mã cần rà khi triển khai

- **Catalog:** [ProductVariant.cs](../../src/backend/src/OISM.Domain/Entities/ProductVariant.cs), [ProductDtos.cs](../../src/backend/src/OISM.Application/Features/Catalog/Products/ProductDtos.cs), [ProductsController.cs](../../src/backend/src/OISM.WebApi/Controllers/ProductsController.cs), [ProductsPage.tsx](../../src/frontend/admin-dashboard/src/pages/ProductsPage.tsx).
- **Nhập và sổ kho:** [GoodsReceiptDtos.cs](../../src/backend/src/OISM.Application/Features/Inventory/Dtos/GoodsReceiptDtos.cs), [GoodsReceiptService.cs](../../src/backend/src/OISM.Application/Features/Inventory/Services/GoodsReceiptService.cs), [InventoryPage.tsx](../../src/frontend/admin-dashboard/src/pages/InventoryPage.tsx).
- **Đơn, giữ chỗ, POS:** [OrderDtos.cs](../../src/backend/src/OISM.Application/Features/Orders/Dtos/OrderDtos.cs), [OrderService.cs](../../src/backend/src/OISM.Application/Features/Orders/Services/OrderService.cs), [ReservationService.cs](../../src/backend/src/OISM.Application/Features/Inventory/Services/ReservationService.cs), [App.tsx](../../src/frontend/pos-app/src/App.tsx), [PaymentModal.tsx](../../src/frontend/pos-app/src/components/PaymentModal.tsx), [OrdersPage.tsx](../../src/frontend/admin-dashboard/src/pages/OrdersPage.tsx).
- **Các luồng kho khác:** [StockTransferService.cs](../../src/backend/src/OISM.Application/Features/StockTransfers/Services/StockTransferService.cs), [StockCountService.cs](../../src/backend/src/OISM.Application/Features/StockCounts/Services/StockCountService.cs), [InventoryService.cs](../../src/backend/src/OISM.Application/Features/Inventory/Services/InventoryService.cs), [ReportService.cs](../../src/backend/src/OISM.Application/Features/Reports/Services/ReportService.cs), [WebhookService.cs](../../src/backend/src/OISM.Application/Features/Webhooks/Services/WebhookService.cs).

## 6. Ràng buộc và cách xử lý trường hợp biên

- **Độ chính xác:** các cột lượng hiện có ba chữ số lẻ. Giai đoạn đầu chỉ cho phép giao dịch mà `BaseQuantity` biểu diễn chính xác trong giới hạn đó; backend từ chối lượng quá nhỏ hoặc kết quả quy đổi phải làm tròn. Không âm thầm làm tròn lượng kho. Mở rộng precision là migration riêng nếu cần nhỏ hơn 1 g khi gốc là kg.
- **Số lẻ:** SKU bán theo bao nguyên có thể yêu cầu số lượng bao là số nguyên; SKU bán theo kg có thể nhận 2,5 kg. Quy tắc này nằm trên `sku_units`, được backend kiểm tra.
- **Thay hệ số:** không sửa hệ số trên chứng từ đã chốt. Cấu hình mới áp dụng cho giao dịch mới; nếu thay quy cách làm thay đổi bản chất hàng tồn thì phải tạm dừng SKU và đối soát trước khi bật.
- **Đổi đơn vị gốc:** bị từ chối (`BASE_UNIT_LOCKED`) khi SKU đã có giao dịch hoặc số dư, và khi còn đơn vị quy đổi đang bật, vì hệ số của chúng tính theo đơn vị gốc cũ. Quy tắc thứ hai đang là mặc định, chưa được xác nhận.
- **Mã vạch:** mã vạch của đơn vị quy cách phải ánh xạ đến `(SKU, UnitId)`; quét bao phải chọn đúng đơn vị bao, không chỉ tìm SKU. Chặn trùng mã vạch trong tenant.
- **Hoàn trả/hủy:** dùng đơn vị và hệ số snapshot của dòng gốc để đảo lượng kho; không tra hệ số hiện tại. Giá tiền trả dựa trên dòng giao dịch gốc và quy tắc trả hàng.
- **Đa tenant và retry:** mọi đơn vị/cấu hình/giá phải được giới hạn theo tenant. Xác nhận lại cùng chứng từ không được ghi thêm lượng gốc lần hai.
- **Một tồn duy nhất:** các đơn vị của cùng SKU chỉ là cách biểu diễn của một lượng tồn. `1 bao 25 kg` và `25 kg` không được tạo hai số dư độc lập.

## 7. Di trú dữ liệu và tương thích

1. Trước migration, xuất báo cáo đối soát tồn và WAC theo `Tenant + Branch + Warehouse + SKU` cùng tổng tiền/giá vốn của đơn đã chốt.
2. Tạo đơn vị gốc cho mỗi SKU từ `ProductVariant.Unit`; đánh dấu SKU đang dùng giá trị mặc định `piece` để kiểm tra thủ công trước khi cho dùng đa đơn vị.
3. Thêm bản ghi `sku_units` của đơn vị gốc với hệ số 1. Với chứng từ cũ, điền `UnitId` là đơn vị gốc, `FactorSnapshot = 1`, `BaseQuantity = Quantity`; sổ kho lịch sử không viết lại.
4. Triển khai backend đọc được cả request cũ không có đơn vị và request mới; trong giai đoạn chuyển tiếp, request cũ được hiểu là đơn vị gốc. Sau đó cập nhật Admin/POS và mới mở tính năng đa đơn vị.
5. Đối chiếu lại tồn, giá trị tồn, doanh thu và COGS. Nếu sai, dừng bật tính năng và khôi phục theo kế hoạch rollback; không chạy script sửa số dư trực tiếp để che chênh lệch.

## 8. Tiêu chí nghiệm thu bắt buộc

1. Nhập 1 tấn gạo giá 12.000.000đ tạo tồn 1.000 kg và WAC 12.000đ/kg; bán 2,5 kg và một bao 25 kg còn 972,5 kg.
2. Dòng bán bao ghi `Quantity = 1`, `UnitPrice = 350.000đ`, `BaseQuantity = 25`; thành tiền 350.000đ và giá vốn = `25 × WAC/kg`.
3. Một giỏ POS có cùng SKU ở hai đơn vị tạo hai dòng đúng giá; quét mã vạch quy cách chọn đúng đơn vị.
4. Client tự gửi hệ số hoặc lượng gốc sai không thay đổi kết quả backend; đơn vị chưa được bật cho SKU bị từ chối.
5. Thay hệ số sau ngày bán không làm thay đổi hóa đơn, hoàn trả, lượng kho hoặc giá vốn của đơn cũ.
6. Tất cả nhập, bán, giữ chỗ, chuyển kho, kiểm kê, báo cáo dùng đúng lượng gốc hoặc ghi rõ chỉ cho nhập đơn vị gốc; không phát sinh tồn âm do quy đổi.
7. Retry xác nhận phiếu nhập/checkout không tạo thêm tồn hoặc dòng sổ kho.
8. Dữ liệu và báo cáo trước migration giữ nguyên số tiền, số lượng và WAC sau migration.

## 9. Phạm vi chưa làm trong quyết định này

- Không tạo SKU tồn kho riêng cho `bao 25 kg`, không theo dõi số bao nguyên. Nếu cần, đó là dự án đóng/mở bao với chứng từ chuyển đổi và giá vốn riêng.
- Không hỗ trợ bao cân thực tế thay đổi theo từng bao; phải thiết kế trường lượng thực đo và quy trình cân riêng.
- Không tự quy đổi giữa các nhóm đo không tương thích như kg và lít.
- Không áp dụng cho dịch vụ hoặc bản quyền theo thời gian.

Các phần trên có thể bổ sung về sau mà không làm thay đổi nguyên tắc một đơn vị gốc cho các SKU hàng rời hiện tại.
