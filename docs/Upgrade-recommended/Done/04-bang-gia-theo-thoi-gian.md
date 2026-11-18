# 04. Bảng giá bán có thời gian hiệu lực

> Trạng thái ngày 12/11/2026. Đã triển khai backend và giao diện Admin để lập, duyệt phiếu đổi giá và tra cứu lịch sử giá. Các câu hỏi nghiệp vụ được lưu tại mục [Cần xác nhận](#cần-xác-nhận). Giá mua và WAC được xử lý ở [tài liệu 01](../Done/01-mua-hang-nhap-kho-gia-von.md). [Báo cáo triển khai](../../report-after-upgrade/bang-gia-theo-thoi-gian.md)

## Tóm tắt quyết định

- Không sửa giá trực tiếp. Mọi lần đổi giá đều đi qua **phiếu đổi giá**.
- Giá lưu trong một bảng lịch sử chỉ thêm dòng mới. Dòng cũ không bị sửa giá.
- Mỗi dòng giá chỉ có `valid_from`. Ngày kết thúc được suy ra từ dòng kế tiếp.
- Bản đầu có giá lẻ, giá sỉ, giá theo từng đơn vị, lịch giá tương lai, lịch sử giá và chụp giá vào đơn.
- Chưa làm khuyến mại và giá theo chi nhánh, kênh hoặc nhóm khách.

## Vấn đề cần giải quyết

Giá bán hiện bị ghi đè trực tiếp, không có kỳ hiệu lực, người duyệt hay lý do đổi. Vì vậy không lên lịch được giá tương lai và không xem lại được giá của một thời điểm.

## Hiện trạng đã kiểm tra

Đối chiếu code ngày 02/11/2026:

- **Giá nằm ở hai nơi:** `skus.retail_price`, `skus.wholesale_price` cho SKU và `sku_units.sale_price` cho đơn vị không phải đơn vị gốc.
- **Có nhiều đường ghi giá:** tạo và sửa biến thể, `PUT /api/products/{productId}/variants/{variantId}/prices` trong [ProductService.cs](../../../src/backend/src/OISM.Application/Features/Catalog/Products/Services/ProductService.cs), tạo và sửa đơn vị trong [UnitService.cs](../../../src/backend/src/OISM.Application/Features/Catalog/Units/Services/UnitService.cs).
- **Đọc giá chưa qua một đầu mối:** [SkuUnitResolver.cs](../../../src/backend/src/OISM.Application/Features/Catalog/Units/Services/SkuUnitResolver.cs), [PosCatalogService.cs](../../../src/backend/src/OISM.Application/Features/Pos/PosCatalogService.cs) và [ReservationService.cs](../../../src/backend/src/OISM.Application/Features/Inventory/Services/ReservationService.cs). Riêng `ReservationService` đọc thẳng `RetailPrice`.
- **Giá sỉ chưa được dùng khi bán:** `OrderService`, `ReservationService`, `SkuUnitResolver` và `PosCatalogService` không đọc `WholesalePrice`.
- **Đổi giá không có dấu vết:** `UpdateVariantPricesAsync` ghi đè giá, không nhận người sửa. CodeGraph không tìm thấy test nào phủ hàm này.
- **`PRICE_MISMATCH` chỉ xảy ra lúc tạo đơn hoặc checkout:** lỗi này được ném trong `BuildOrderLines` của [OrderService.cs](../../../src/backend/src/OISM.Application/Features/Orders/Services/OrderService.cs), không chạy lúc xác nhận đơn. Đơn từ sàn được bỏ qua bước so giá.
- **Chưa có dữ liệu khách hàng:** schema không có bảng khách hàng hay nhóm khách.

## Tham khảo các hệ thống khác

| Tiêu chí | Odoo | KiotViet | Sapo | ERPNext | OISMS |
| :--- | :--- | :--- | :--- | :--- | :--- |
| Phạm vi giá | Sản phẩm, danh mục, số lượng tối thiểu | Chi nhánh, nhóm khách, người tạo giao dịch | Chi nhánh, nhóm khách, kênh bán | Khách hoặc nhà cung cấp, số lượng tối thiểu, lô | Bản đầu: chung cho tenant |
| Ngày hiệu lực | Có | Có | Chưa xác nhận được | Có | Có `valid_from` |
| Cách đặt giá | Công thức: giảm %, làm tròn, phụ phí | Giá theo công thức từ bảng gốc | % hoặc giá cố định | Giá cố định, kèm quy tắc giá riêng | Giá cố định |
| Giá theo đơn vị | Chưa xác nhận được | Giá riêng cho đơn vị lớn | Chưa xác nhận được | Mỗi giá gắn với một đơn vị | Mỗi đơn vị một giá |
| Kiểm soát giá | Giá trên đơn có thể bị ghi đè | Phân quyền theo vai trò; đổi được giá từ màn hình hàng hóa và nhập hàng | Khóa bảng giá, nhân viên không tự đổi | Có độ ưu tiên giữa các quy tắc | Đổi giá qua phiếu, không ghi đè trên đơn |

Trong tài liệu đã đọc, không hệ thống nào được mô tả là bắt buộc duyệt từng dòng giá. Phiếu đổi giá và bước duyệt là **lựa chọn riêng của OISMS**. Phần về Sapo chỉ dựa trên các bài giới thiệu tính năng.

Nguồn, đối chiếu ngày 02/11/2026: [Odoo — Pricelists](https://www.odoo.com/documentation/19.0/applications/sales/sales/products_prices/prices/pricing.html), [KiotViet — Thiết lập giá](https://www.kiotviet.vn/huong-dan-su-dung-kiotviet/retail-hang-hoa/thiet-lap-gia/), [Sapo — Quản lý bảng giá](https://www.sapo.vn/cap-nhat-tinh-nang/quan-ly-bang-gia), [Sapo — Giá theo kênh và nhóm khách](https://www.sapo.vn/cap-nhat-tinh-nang/thiet-lap-bang-gia-linh-hoat-theo-kenh-va-nhom-khach-hang-khi-tao-san-pham), [Sapo — Chính sách giá cho nhóm lựa chọn](https://www.sapo.vn/cap-nhat-tinh-nang/chinh-sach-gia-cho-nhom-lua-chon), [ERPNext — Item Price](https://docs.frappe.io/erpnext/item-price.md), [ERPNext — Pricing Rule](https://docs.frappe.io/erpnext/pricing-rule.md). Thông tin giá theo đơn vị của KiotViet lấy từ nguồn đã dẫn ở [tài liệu 05](./05-nhieu-don-vi-tinh.md).

## Phạm vi

| Giai đoạn | Nội dung |
| :--- | :--- |
| Bản đầu | Bảng giá lẻ và sỉ, giá theo đơn vị, phiếu đổi giá, lịch giá tương lai, lịch sử giá, chụp giá vào đơn, chuyển đổi dữ liệu cũ |
| Làm sau | Cảnh báo giá bán thấp sau khi nhập kho (cần Upgrade 01), giá theo chi nhánh và kênh, khuyến mại |
| Chưa làm | Giá theo nhóm khách, giá theo bậc số lượng, nhập Excel |

Cần giảm giá tạm thời khi chưa có khuyến mại thì tạo 2 phiếu: một phiếu giảm và một phiếu trả lại giá cũ.

## Mô hình dữ liệu

| Bảng | Nội dung | Quy tắc |
| :--- | :--- | :--- |
| `price_lists` | Bảng giá của tenant: Giá lẻ, Giá sỉ | Giá sỉ là một bảng giá, không phải cột loại giá |
| `price_change_requests` | Phiếu đổi giá: người tạo, lý do, `valid_from`, trạng thái, người duyệt | Trạng thái: `Pending`, `Approved`, `Rejected` |
| `price_change_request_items` | Dòng phiếu: bảng giá, SKU, đơn vị, giá mới | Một phiếu có nhiều dòng và được duyệt một lần |
| `sku_unit_prices` | Giá áp dụng: bảng giá, SKU, đơn vị, giá, `valid_from`, dòng phiếu nguồn, thông tin hủy | Chỉ sinh ra khi phiếu được duyệt; trigger chặn sửa giá, chặn xóa |
| `order_items` (thêm cột) | ID dòng giá và nguồn giá: `PriceList`, `Marketplace`, `Legacy` | `unit_price` hiện có vẫn là số tiền giao dịch |

**Giá đang áp dụng** là dòng chưa hủy có `valid_from` mới nhất và không vượt quá thời điểm hiện tại, theo khóa `tenant + bảng giá + SKU + đơn vị`.

- **Không chồng kỳ:** mỗi khóa tại một thời điểm chỉ ra đúng một giá, nên không cần lưu `valid_to`.
- **Ràng buộc database:** `UNIQUE (tenant_id, price_list_id, sku_id, unit_id, valid_from)` chỉ áp cho dòng chưa hủy, để có thể đặt lại giá cho một mốc đã hủy.
- **Giá theo đơn vị độc lập:** giá `kg` và giá `bao 25 kg` là hai dòng riêng; không bắt buộc giá bao bằng 25 lần giá/kg.
- **Thời gian:** nhập theo múi giờ cửa hàng (`Asia/Saigon`), lưu UTC, và luôn dùng đồng hồ của backend.

### Ví dụ

| `valid_from` | Giá gạo A/kg | Khoảng hiệu lực suy ra |
| :--- | :--- | :--- |
| 01/11 | 15.000đ | `[01/11, 10/11)` |
| 10/11 | 16.000đ | `[10/11, chưa có ngày kết thúc)` |

Đơn chốt ngày 09/11 giữ giá 15.000đ; đơn chốt ngày 10/11 lấy 16.000đ. Nếu bán `bao 25 kg`, hệ thống tra dòng giá riêng của bao tại cùng thời điểm.

## Quy trình đổi giá

### Giá ban đầu

Người dùng nhập giá trên form tạo SKU hoặc đơn vị, và hệ thống tự sinh phiếu ở phía sau. Nhờ vậy, mọi dòng giá đều truy được về một phiếu, và Staff không thể tạo đơn vị mới với giá tùy ý để né bước duyệt.

Đơn vị chưa có giá được duyệt thì chưa bán được và báo `UNIT_PRICE_NOT_CONFIGURED`.

### Đổi giá

1. Người dùng tạo phiếu cho một hoặc nhiều SKU, ghi lý do, chọn áp dụng ngay hoặc từ một ngày trong tương lai.
2. Backend kiểm tra SKU và đơn vị, giá không âm, độ chính xác tiền tệ, `valid_from` không nằm trong quá khứ.
3. Khi phiếu được duyệt, backend ghi các dòng `sku_unit_prices` trong một transaction.

Đổi giá khẩn cấp không có luồng riêng; đó chỉ là phiếu áp dụng ngay.

### Sửa và hủy

- **Giá đã lên lịch:** được hủy trước ngày áp dụng, bắt buộc ghi lý do. Dòng giá vẫn còn trong lịch sử.
- **Giá đã có hiệu lực mà sai:** tạo phiếu mới để thay, không sửa dòng cũ.

### Phân quyền

| Vai trò | Giá ban đầu | Đổi giá | Duyệt phiếu |
| :--- | :--- | :--- | :--- |
| Owner | Có hiệu lực ngay | Phiếu được duyệt ngay khi lưu | Có |
| Staff | Chờ Owner duyệt; trong thời gian này chưa bán được | Tạo phiếu, chờ Owner duyệt; trong thời gian này vẫn bán giá cũ | Không |
| Cashier | Không | Không | Không |

Phiếu tự duyệt của Owner vẫn lưu người tạo, lý do và thời điểm. [Ma trận RBAC](../../01-users-and-rbac/02-rbac-matrix.md) đã được cập nhật theo bảng này.

## Khi bán hàng

Mọi luồng lấy giá qua **một resolver dùng chung**, không đọc trực tiếp các cột giá trên SKU.

| Luồng | Bảng giá | Thời điểm chốt giá | Ghi chú |
| :--- | :--- | :--- | :--- |
| POS | Giá lẻ | Lúc checkout | So với giá client gửi lên |
| Đơn Admin | Giá lẻ hoặc giá sỉ, người tạo đơn chọn | Lúc tạo đơn | Xác nhận đơn dùng giá đã chụp |
| Đơn từ sàn | Không dùng | Lúc nhận đơn | Dùng giá do sàn gửi, nguồn giá `Marketplace` |

- **Chụp giá:** dòng đơn lưu đơn giá và ID dòng giá. Đơn cũ, hóa đơn in lại và đơn đang giữ chỗ không đổi theo giá mới.
- **Giá đổi đúng lúc đang bán:** vẫn báo `PRICE_MISMATCH`, và màn hình hiện giá mới để người bán xác nhận lại.
- **Gửi lại cùng `Idempotency-Key`:** trả về đơn đã tạo với giá cũ, kể cả khi giá đã đổi.
- **Ghi đè giá trên đơn:** không cho phép. Giảm giá cho từng đơn nằm ngoài phạm vi bản đầu.

## Quan hệ với phiếu mua

Phiếu mua chỉ quyết định giá mua và WAC, **không tự đổi giá bán**. Giá mua lấy theo quy trình ở [tài liệu 01](../Done/01-mua-hang-nhap-kho-gia-von.md).

Sau khi có Upgrade 01, có thể thêm cảnh báo: sau khi xác nhận nhập kho, nếu giá bán thấp hơn WAC hoặc dưới ngưỡng lãi thì hiện nút tạo phiếu đổi giá đã điền sẵn SKU và đơn vị. Giá mới vẫn do người dùng nhập, phiếu vẫn qua bước duyệt, và cảnh báo không chặn nhập kho.

Khi so sánh phải quy về đơn vị gốc, vì WAC tính theo đơn vị gốc. Ví dụ bao 25 kg bán 350.000đ tương đương 14.000đ/kg, nên so 14.000đ với WAC/kg. Phần này không bắt buộc cho bản đầu.

## Thay đổi cần làm

### Dữ liệu cũ

Script chuyển đổi tạo phiếu "Giá đầu kỳ" cho mỗi tenant, rồi chép `retail_price`, `wholesale_price`, `sale_price` thành các dòng giá đầu tiên. Đây là trường hợp duy nhất không theo quy tắc `valid_from` không nằm trong quá khứ. Đơn cũ có nguồn giá `Legacy`.

Sau khi chuyển, luồng bán không đọc các cột giá cũ nữa. Việc xóa các cột này để sang một đợt sau.

### Backend

- Thêm 4 bảng mới theo đúng quy trình đổi schema trong `.claude/CLAUDE.md`, gồm script SQL tăng dần và cập nhật `RequiredTables`.
- Đóng `PUT .../prices`; thay bằng API tạo, duyệt, từ chối phiếu, hủy giá đã lên lịch, tra giá tại thời điểm và xem lịch sử. API tạo phiếu nhận `Idempotency-Key`.
- API sửa biến thể và đơn vị không còn đổi được giá.
- Chuyển `SkuUnitResolver`, `PosCatalogService`, `ReservationService` sang resolver giá mới.
- Duyệt phiếu chạy trong transaction, có khóa, và giữ tenant isolation.

### Frontend

- Bỏ ô sửa giá trên form sửa SKU, chỉ giữ ô giá ban đầu khi tạo mới.
- Thêm màn hình phiếu đổi giá, danh sách chờ duyệt, lịch giá sắp tới và lịch sử giá theo SKU.
- Xử lý `PRICE_MISMATCH` bằng cách hiện giá mới trên POS và form đơn Admin.

## Tiêu chí nghiệm thu

1. Tại một thời điểm, mỗi khóa bảng giá–SKU–đơn vị chỉ ra đúng một giá.
2. Giá chuyển đúng tại mốc `valid_from`; đơn cũ, hóa đơn in lại và báo cáo vẫn giữ giá đã chụp.
3. Không đổi được giá hiện hành bằng cách sửa SKU, sửa đơn vị hoặc gọi API trực tiếp; mọi dòng giá đều truy được về một phiếu.
4. Staff tạo phiếu thì giá chưa đổi cho đến khi Owner duyệt; Cashier không tạo được phiếu.
5. Hủy được giá đã lên lịch trước ngày áp dụng; không sửa hay xóa được dòng giá đã duyệt.
6. Đơn Admin đang giữ chỗ không lỗi khi giá mới bắt đầu có hiệu lực.
7. Gửi lại cùng `Idempotency-Key` sau khi giá đổi vẫn trả về đơn cũ, không tạo đơn mới.
8. Đơn vị chưa có giá hợp lệ thì báo `UNIT_PRICE_NOT_CONFIGURED`, không bán theo giá 0.
9. Tenant khác không xem và không dùng được phiếu hay dòng giá của nhau.

## Cần xác nhận

Hỏi thầy:

- Có chấp nhận `valid_to` được suy ra từ dòng kế tiếp thay vì lưu thành cột không?
- Có cần giá theo nhóm khách không? Nếu có thì phải bổ sung dữ liệu khách hàng trước.

## Quyết định đã áp dụng khi triển khai

Đây là các đề xuất mặc định nhóm phát triển đã áp dụng; có thể đổi nếu thầy hoặc nhóm yêu cầu.

- **Giá sỉ cũ bằng 0:** không tạo dòng giá, bán sỉ báo `UNIT_PRICE_NOT_CONFIGURED`. Giá lẻ cũ bằng 0 vẫn chép nguyên và được script liệt kê để rà soát.
- **Phiếu của Staff duyệt muộn:** `valid_from` là thời điểm muộn hơn giữa ngày yêu cầu và lúc duyệt, vì không cho áp dụng hồi tố.
- **Đổi đơn vị gốc:** không tự chép giá; đơn vị mới chưa có giá thì chưa bán được.
- **Hủy giá đã lên lịch:** chỉ Owner.
- **Chuyển tiếp frontend:** `PUT .../prices` trả `410`. Sửa SKU hoặc đơn vị với giá khác giá hiện hành trả `409 DIRECT_PRICE_UPDATE_NOT_ALLOWED`; gửi trùng giá, hoặc gửi 0 khi chưa có giá, thì bỏ qua.
