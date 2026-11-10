# 02. TỪ ĐIỂN DANH MỤC MÃ LỖI TOÀN HỆ THỐNG (ERROR CODES CATALOG)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-ERR-02` |
| **Phiên bản (Version)** | `v1.2.0` |
| **Ngày hiệu lực** | `2026-11-06` |
| **Nguồn truy vết thực tế** | Source code Backend C# (`OISM.Application`, `OISM.WebApi`, `OismException.cs`) |
| **Tài liệu liên quan** | [`01-error-response-spec.md`](./01-error-response-spec.md), [`03-color-system-tokens.md`](./03-color-system-tokens.md) |

---

## 1. Giới Thiệu Từ Điển Mã Lỗi

Tài liệu này là **từ điển tra cứu danh mục mã lỗi toàn hệ thống OISMS**. Toàn bộ mã lỗi được chuẩn hóa theo quy ước:
* Định dạng: Viết hoa phân tách bằng dấu gạch dưới (`UPPER_SNAKE_CASE`).
* Tính tường minh: Phản ánh trực tiếp bản chất nghiệp vụ gặp sự cố.
* Đi kèm với mỗi mã lỗi là: **Mã HTTP Status**, **Tiêu đề tiếng Việt**, **Nội dung thông báo mẫu**, **Nguyên nhân cốt lõi**, **Gợi ý khắc phục cho người dùng/thu ngân** và **Vị trí phát sinh trong source code**.

---

## 2. Bảng Tra Cứu Danh Mục Mã Lỗi Chi Tiết

### 2.1. Phân Hệ Xác Thực, Khách Thuê & Phân Quyền (`AUTH`)
*Liên kết hợp đồng API:* [`../05-api-contracts/01-auth-endpoints.md`](../05-api-contracts/01-auth-endpoints.md)  
*Liên kết màn hình:* [`../11-ui-screens/01-auth-screens.md`](../11-ui-screens/01-auth-screens.md)

| Mã lỗi (`ErrorCode`) | HTTP Status | Tiêu đề lỗi | Thông báo chi tiết mẫu | Nguyên nhân phát sinh | Hướng dẫn khắc phục trên UI |
| :--- | :---: | :--- | :--- | :--- | :--- |
| `VALIDATION_ERROR` | `400` | Lỗi xác thực dữ liệu | *Dữ liệu yêu cầu không hợp lệ.* | Thiếu trường bắt buộc, sai định dạng email/SĐT, mật khẩu quá ngắn. | Hiển thị viền đỏ và text cảnh báo dưới trường tương ứng. |
| `AUTH_INVALID_CREDENTIALS` | `400` | Sai thông tin đăng nhập | *Tài khoản hoặc mật khẩu không chính xác.* | Nhập sai email/SĐT hoặc mật khẩu đăng nhập. | Yêu cầu người dùng kiểm tra và nhập lại thông tin. |
| `AUTH_TENANT_NOT_FOUND` | `404` | Cửa hàng không tồn tại | *Không tìm thấy thông tin cửa hàng với mã gian hàng này.* | Tenant slug nhập không tồn tại trong hệ thống. | Kiểm tra lại URL đăng nhập hoặc mã định danh Tenant. |
| `AUTH_TENANT_LOCKED` | `403` | Cửa hàng đã bị khóa | *Cửa hàng của bạn đã bị tạm khóa. Vui lòng liên hệ quản trị viên.* | Cửa hàng bị `IsActive = false` do vi phạm điều khoản hoặc hết hạn cước. | Khóa thao tác và hiển thị banner đỏ liên hệ bộ phận hỗ trợ OISMS. |
| `AUTH_USER_LOCKED` | `403` | Tài khoản bị khóa | *Tài khoản của bạn đã bị vô hiệu hóa.* | Tài khoản nhân viên bị quản trị viên tắt `IsActive`. | Chặn đăng nhập và hiển thị thông báo liên hệ chủ cửa hàng (Owner). |
| `AUTH_UNAUTHORIZED` | `401` | Chưa xác thực | *Yêu cầu cần có JWT Token xác thực hợp lệ.* | Không gửi Authorization Header hoặc gửi Token rỗng. | Chuyển hướng người dùng về màn hình `/login`. |
| `AUTH_TOKEN_EXPIRED` | `401` | Phiên làm việc hết hạn | *Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.* | Access Token đã quá thời hạn sử dụng (`exp`). | Kích hoạt cơ chế làm mới token tự động hoặc mở Modal đăng nhập lại. |
| `FORBIDDEN_ACCESS` | `403` | Không có quyền truy cập | *Bạn không có quyền thực hiện thao tác này.* | Vai trò hiện tại (Role) không nằm trong danh sách cho phép (RBAC). | Chuyển hướng sang màn hình `/403` (Forbidden Access). |
| `USER_NOT_FOUND` | `404` | Không tìm thấy nhân viên | *Không tìm thấy thông tin nhân viên.* | ID nhân viên không tồn tại trong Tenant hiện tại. | Tải lại danh sách nhân viên. |
| `EMAIL_ALREADY_EXISTS` | `409` | Email đã tồn tại | *Email này đã được sử dụng bởi một tài khoản khác.* | Email trùng trên toàn hệ thống. | Dùng email khác. |
| `PHONE_ALREADY_EXISTS` | `409` | Số điện thoại đã tồn tại | *Số điện thoại này đã được sử dụng bởi một tài khoản khác.* | Số điện thoại sau chuẩn hóa E.164 trùng trên toàn hệ thống. | Dùng số điện thoại khác. |
| `INVALID_PHONE` | `400` | Số điện thoại không hợp lệ | *Định dạng số điện thoại không hợp lệ.* | Không chuẩn hóa được về E.164. | Nhập dạng 0901234567 hoặc +84901234567. |
| `CANNOT_DEACTIVATE_SELF` | `400` | Không thể tự khóa tài khoản | *Không thể tự vô hiệu hóa tài khoản của chính mình.* | Owner tự khóa tài khoản đang đăng nhập. | Giữ trạng thái hoạt động. |
| `CANNOT_DEMOTE_SELF` | `400` | Không thể tự hạ quyền | *Không thể tự hạ quyền của chính mình.* | Owner tự đổi vai trò của mình. | Giữ vai trò Owner. |
| `CANNOT_ASSIGN_OWNER` | `400` | Không thể gán vai trò Chủ shop | *Không thể gán vai trò Chủ shop cho nhân viên.* | Cố nâng nhân viên lên Owner. | Chọn Staff hoặc Cashier. |

---

### 2.2. Phân Hệ Danh Mục, Sản Phẩm & Biến Thể SKU (`PROD`)
*Liên kết hợp đồng API:* [`../05-api-contracts/02-catalog-endpoints.md`](../05-api-contracts/02-catalog-endpoints.md)  
*Liên kết màn hình:* [`../11-ui-screens/03-product-screens.md`](../11-ui-screens/03-product-screens.md)

| Mã lỗi (`ErrorCode`) | HTTP Status | Tiêu đề lỗi | Thông báo chi tiết mẫu | Nguyên nhân phát sinh | Hướng dẫn khắc phục trên UI |
| :--- | :---: | :--- | :--- | :--- | :--- |
| `SKU_NOT_FOUND` | `404` | Mặt hàng không tồn tại | *Mặt hàng biến thể SKU '{skuId}' không tồn tại trong hệ thống.* | ID hoặc mã SKU không tồn tại trong CSDL của Tenant. | Kiểm tra lại danh mục sản phẩm hoặc quét lại mã vạch. |
| `SKU_INACTIVE` | `422` | Mặt hàng ngừng hoạt động | *Mặt hàng SKU '{skuCode}' hoặc sản phẩm liên quan đang ngừng hoạt động.* | Thuộc tính `IsActive = false` trên SKU hoặc Product. | Kích hoạt lại sản phẩm trên màn hình Quản lý Sản phẩm trước khi bán. |
| `DUPLICATE_BARCODE` | `409` | Trùng lặp mã vạch | *Mã vạch '{barcode}' đã tồn tại trong cửa hàng.* | Trùng mã vạch trong tenant, tính cả `skus` (`uq_skus_3`) và mã vạch quy cách `sku_units` (`ux_sku_units_barcode`), kể cả với quy cách của chính SKU đó. | Nhập mã vạch khác hoặc bấm nút "Tự động sinh mã EAN-13". |
| `PRICE_MISMATCH` | `400` | Sai lệch đơn giá niêm yết | *Đơn giá của mặt hàng trong giỏ hàng không khớp với giá niêm yết của hệ thống.* | Giá client gửi khác giá đang hiệu lực trong bảng giá, kể cả khi giá mới vừa có hiệu lực lúc đơn chờ khóa. `extensions.currentPrices` liệt kê mọi dòng lệch. | Hiện giá mới để người bán xác nhận rồi gửi lại. |
| `CATEGORY_CYCLIC_DEPENDENCY` | `409` | Vòng lặp danh mục | *Không thể gán danh mục cha là chính nó hoặc danh mục con cháu.* | Phát hiện vòng tròn quan hệ cha - con trong cây danh mục. | Chọn danh mục cha hợp lệ không thuộc nhánh con hiện tại. |
| `BRAND_NOT_FOUND` | `404` | Thương hiệu không tồn tại | *Không tìm thấy thông tin thương hiệu được chỉ định.* | ID thương hiệu không tồn tại hoặc bị xóa. | Chọn thương hiệu có sẵn từ danh sách thả xuống. |
| `UNIT_NOT_FOUND` | `404` | Đơn vị tính không tồn tại | *Không tìm thấy đơn vị tính yêu cầu.* | ID hoặc mã đơn vị tính không tồn tại trong Tenant. | Chọn đơn vị tính có sẵn từ danh mục hoặc tạo mới. |
| `UNIT_CODE_DUPLICATE` | `409` | Trùng mã đơn vị tính | *Mã đơn vị tính đã tồn tại trong cửa hàng.* | Vi phạm tính duy nhất của mã đơn vị tính trong Tenant. | Nhập mã đơn vị tính khác. |
| `UNIT_GROUP_INCOMPATIBLE` | `422` | Nhóm đơn vị không tương thích | *Đơn vị tính không tương thích với đơn vị gốc của SKU.* | Đơn vị giao dịch khác nhóm đo lường với đơn vị gốc và không thuộc nhóm Bao gói (Packaging). | Chọn đơn vị cùng nhóm đo lường hoặc nhóm Bao gói. |
| `INVALID_UNIT_FACTOR` | `400` | Hệ số quy đổi không hợp lệ | *Hệ số quy đổi phải lớn hơn 0, tối đa 12 chữ số nguyên và 6 chữ số thập phân.* | Hệ số <= 0, vượt giới hạn `numeric(18,6)`, hoặc cố ý sửa hệ số của đơn vị gốc khác 1. | Nhập hệ số dương trong giới hạn; hệ thống không tự làm tròn. |
| `BASE_UNIT_LOCKED` | `409` | Đơn vị gốc đã khóa | *Không thể thay đổi đơn vị tính gốc khi SKU đã có lịch sử giao dịch hoặc số dư kho.* | SKU đã phát sinh đơn hàng, phiếu nhập, sổ cái hoặc tồn kho; hoặc còn đơn vị quy đổi đang bật (hệ số của chúng sẽ sai nghĩa nếu đổi gốc). | Giữ nguyên đơn vị gốc; nếu SKU chưa có giao dịch thì tắt các đơn vị quy đổi trước khi đổi. |
| `UNIT_NOT_ALLOWED` | `422` | Đơn vị không được phép | *Đơn vị tính chưa được kích hoạt hoặc không cho phép giao dịch.* | Đơn vị tính bị tắt hoạt động (`IsActive = false`) hoặc cấm mua/bán. | Bật kích hoạt đơn vị tính cho SKU trước khi giao dịch. |
| `INVALID_UNIT_QUANTITY` | `400` | Số lượng đơn vị không hợp lệ | *Số lượng hoặc số chữ số thập phân không hợp lệ cho đơn vị tính.* | Vượt quá số chữ số lẻ cho phép của đơn vị hoặc quy đổi vượt 3 số lẻ. | Nhập số lượng đúng với số chữ số thập phân của đơn vị. |
| `UNIT_PRICE_NOT_CONFIGURED` | `422` | Đơn vị chưa có giá bán | *Đơn vị của mặt hàng chưa được cấu hình giá bán.* | Đơn vị chưa có dòng giá hiệu lực trong bảng giá được chọn, gồm cả giá ban đầu của `Staff` đang chờ duyệt. | Tạo hoặc duyệt phiếu đổi giá; không tự suy ra từ giá đơn vị gốc. |
| `UNIT_IN_USE` | `409` | Đơn vị đang được sử dụng | *Không thể thay đổi mã hoặc nhóm đo lường khi đơn vị đã được sản phẩm sử dụng.* | Đơn vị tính đã được gán làm đơn vị gốc hoặc quy đổi của SKU. | Tạo đơn vị tính mới thay vì sửa mã/nhóm của đơn vị đang dùng. |
| `UNIT_ALREADY_EXISTS` | `409` | Đơn vị đã được cấu hình cho SKU | *Đơn vị tính đã được cấu hình cho SKU này.* | Thêm trùng cặp SKU và đơn vị, kể cả dòng đang tắt mềm. | Sửa dòng đã có bằng `PUT`, đặt `isActive = true` nếu cần bật lại. |
| `BASE_UNIT_NOT_FOUND` | `404` | Thiếu đơn vị gốc | *Không tìm thấy đơn vị gốc của SKU.* | `skus.base_unit_id` trỏ tới đơn vị không đọc được trong tenant. | Kiểm tra dữ liệu danh mục của SKU. |
| `INVALID_OPERATION` | `400` | Thao tác không hợp lệ trên đơn vị gốc | *Không thể tắt đơn vị gốc / đặt giá riêng cho đơn vị gốc.* | Thêm lại, tắt, hoặc đặt `sale_price` cho dòng đơn vị gốc. | Đơn vị gốc luôn bật và dùng giá bán lẻ của SKU. |
| `PRICE_VALID_FROM_IN_PAST` | `400` | Mốc thời gian hiệu lực ở quá khứ | *Thời điểm hiệu lực của phiếu đổi giá phải lớn hơn hoặc bằng thời điểm hiện tại.* | `requested_valid_from` được gửi lên nhỏ hơn thời điểm hiện tại của máy chủ. | Chọn thời điểm hiệu lực trong tương lai hoặc để trống để áp dụng ngay. |
| `PRICE_VALID_FROM_CONFLICT` | `409` | Trùng mốc thời gian hiệu lực | *Đã tồn tại giá có hiệu lực cùng mốc thời gian cho mặt hàng và đơn vị này.* | Vi phạm chỉ mục duy nhất một phần `ux_sku_unit_prices_active_key` khi có 2 giá cùng mốc `valid_from`. | Điều chỉnh lại thời điểm hiệu lực của phiếu đổi giá. |
| `PRICE_ALREADY_EFFECTIVE` | `409` | Giá đã có hiệu lực | *Không thể hủy giá đã đến thời điểm hoặc đang có hiệu lực.* | Cố gắng hủy một dòng giá trong `sku_unit_prices` mà mốc `valid_from <= now()`. | Tạo phiếu đổi giá mới để cập nhật lại giá thay vì hủy dòng giá cũ. |
| `PRICE_LIST_NOT_FOUND` | `404` | Bảng giá không tồn tại | *Không tìm thấy bảng giá được chỉ định trong cửa hàng.* | Mã bảng giá (`price_list_code`) không tồn tại trong Tenant (chỉ hỗ trợ `retail` và `wholesale`). | Chọn bảng giá hợp lệ có trong hệ thống. |
| `DIRECT_PRICE_UPDATE_NOT_ALLOWED` | `409`, `410` | Cấm cập nhật giá trực tiếp | *Hệ thống đã nâng cấp sang bảng giá theo thời gian. Mọi thay đổi giá phải thông qua phiếu đổi giá.* | Cố ý gọi API cập nhật giá cũ hoặc gửi giá khác giá hiện hành qua cập nhật SKU/đơn vị. | Sử dụng phân hệ Phiếu đổi giá (`/api/price-change-requests`) để điều chỉnh giá. |

---

### 2.3. Phân Hệ Sổ Cái, Tồn Kho & Giữ Chỗ (`INV` & `RSE`)
*Liên kết hợp đồng API:* [`../05-api-contracts/03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md)  
*Liên kết màn hình:* [`../11-ui-screens/04-inventory-screens.md`](../11-ui-screens/04-inventory-screens.md)

| Mã lỗi (`ErrorCode`) | HTTP Status | Tiêu đề lỗi | Thông báo chi tiết mẫu | Nguyên nhân phát sinh | Hướng dẫn khắc phục trên UI |
| :--- | :---: | :--- | :--- | :--- | :--- |
| `INSUFFICIENT_STOCK` | `409` | Tồn kho không đủ | *SKU '{skuCode}' chỉ còn {available} sản phẩm khả dụng, không đủ đáp ứng số lượng yêu cầu {requested}.* | Tồn khả dụng (`on_hand - reserved`) nhỏ hơn số lượng muốn xuất bán/giữ chỗ. | Điều chỉnh giảm số lượng mua, chọn chi nhánh khác hoặc nhập thêm hàng. |
| `NO_ACTIVE_WAREHOUSE` | `409` | Không có kho khả dụng | *Chi nhánh '{branchName}' không có kho hàng nào đang hoạt động để xuất hàng.* | Chi nhánh chưa được thiết lập kho hoặc tất cả kho đều bị tắt hoạt động. | Vào phần Quản lý Tổ chức & Chi nhánh để tạo/bật kho hàng. |
| `RESERVATION_EXPIRED` | `409` | Phiếu giữ chỗ hết hạn | *Phiếu giữ chỗ của đơn hàng đã hết thời gian hiệu lực và bị hệ thống tự động giải phóng.* | Đơn hàng để quá thời hạn thanh toán (vd: quá 15 phút), tồn giữ chỗ đã trả về quỹ chung. | Tạo lại đơn hàng mới để thực hiện giữ chỗ tồn kho lại từ đầu. |
| `INSUFFICIENT_STOCK_FOR_RESERVATION` | `409` | Không thể giữ chỗ | *Tồn khả dụng không đủ để tạo phiếu giữ chỗ tồn kho.* | Lúc bấm đặt hàng, sản phẩm đã bị đơn hàng khác giữ chỗ trước. | Báo cho khách hàng mặt hàng đã tạm thời hết suất. |
| `LEDGER_IMMUTABLE_VIOLATION` | `400` | Vi phạm tính bất biến | *Hệ thống cấm tuyệt đối chỉnh sửa hoặc xóa giao dịch sổ cái tồn kho.* | Cố ý thực hiện UPDATE hoặc DELETE trên bảng `inventory_transactions`. | Luôn sử dụng cơ chế ghi thêm (Append-Only) bút toán đảo để điều chỉnh. |
| `DUPLICATE_SKU` | `400` | SKU bị lặp | *Danh sách cấu hình có SKU bị lặp.* | Một SKU xuất hiện nhiều lần trong yêu cầu cập nhật ReorderPoint hàng loạt. | Giữ mỗi SKU một dòng. |
| `BRANCH_NOT_FOUND` | `404` | Không tìm thấy chi nhánh | *Không tìm thấy thông tin chi nhánh.* | ID chi nhánh không tồn tại trong Tenant hiện tại. | Chọn lại chi nhánh. |

---

### 2.3a. Phân Hệ Phiếu Mua Hàng (`PURCHASE_ORDER`, `OISMS-47-U01A`)
*Liên kết hợp đồng API:* [`../05-api-contracts/03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md)

| Mã lỗi (`ErrorCode`) | HTTP Status | Tiêu đề lỗi | Thông báo chi tiết mẫu | Nguyên nhân phát sinh | Hướng dẫn khắc phục trên UI |
| :--- | :---: | :--- | :--- | :--- | :--- |
| `PURCHASE_ORDER_REQUIRED` | `422` | Yêu cầu phiếu mua hàng | *Phiếu nhập chưa gắn với phiếu mua hàng hợp lệ.* | Lập phiếu thiếu phiếu mua, hoặc xác nhận phiếu nháp cũ. | Chọn phiếu mua đã duyệt; hủy phiếu nháp cũ rồi lập lại. |
| `PURCHASE_ORDER_NOT_RECEIVABLE` | `409` | Phiếu mua không ở trạng thái nhận hàng | *Phiếu mua đang ở trạng thái '{status}', không thể xác nhận nhập kho.* | Phiếu chưa duyệt, đã đóng hoặc đã hủy. | Tải lại phiếu mua; cần thì lập phiếu mua mới. |
| `PURCHASE_ORDER_OVER_RECEIPT` | `409` | Vượt số lượng đặt mua | *Số lượng nhận vượt quá số lượng còn được nhận trên dòng phiếu mua.* | Nhập dư, hoặc phiếu khác đã nhận hết phần còn lại. | Tải lại lượng còn lại; hàng dư thì lập phiếu mua bổ sung. |
| `PURCHASE_ORDER_ITEM_MISMATCH` | `422` | Dòng hàng không khớp phiếu mua | *Dòng phiếu mua không thuộc phiếu mua đã chọn.* | Dòng không thuộc phiếu, bị trùng, hoặc lệch dữ liệu phiếu mua. | Tải lại phiếu mua và chọn lại dòng. |
| `PURCHASE_ORDER_ZERO_PRICE_CONFIRM_REQUIRED` | `422` | Yêu cầu xác nhận đơn giá 0 | *Phiếu mua có dòng giá 0, cần xác nhận trước khi duyệt.* | Duyệt phiếu có giá 0 với `confirmZeroPrice = false`. | Hỏi Owner xác nhận rồi gửi lại với `confirmZeroPrice = true`. |

---

### 2.4. Phân Hệ Chuyển Kho 2 Bước (`TRANSFER`)
*Liên kết hợp đồng API:* [`../05-api-contracts/03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md)  
*Liên kết màn hình:* [`../11-ui-screens/04-inventory-screens.md`](../11-ui-screens/04-inventory-screens.md)

| Mã lỗi (`ErrorCode`) | HTTP Status | Tiêu đề lỗi | Thông báo chi tiết mẫu | Nguyên nhân phát sinh | Hướng dẫn khắc phục trên UI |
| :--- | :---: | :--- | :--- | :--- | :--- |
| `TRANSFER_NOT_FOUND` | `404` | Không tìm thấy phiếu chuyển | *Không tìm thấy phiếu chuyển kho với mã định danh được yêu cầu.* | ID phiếu chuyển không tồn tại trong Tenant. | Kiểm tra lại danh sách phiếu chuyển kho. |
| `SAME_WAREHOUSE_TRANSFER` | `400` | Trùng lặp kho xuất nhập | *Kho xuất và kho nhận không được trùng nhau.* | Chọn cùng một kho cho cả hai chiều xuất và nhận. | Chọn hai kho hàng khác nhau. |
| `INVALID_STATE_TRANSITION` | `409` | Sai chuyển đổi trạng thái | *Không thể xuất chuyển phiếu ở trạng thái '{status}'. Chỉ cho phép xuất chuyển từ 'Draft'.* | Thực hiện thao tác không đúng vòng đời (vd: Tiếp nhận phiếu đang ở Draft). | Tuân thủ đúng luồng chuyển kho: `Draft -> InTransit -> Received` hoặc `Draft -> Cancelled`. |
| `DUPLICATE_TRANSFER_SKU` | `400` | Trùng mặt hàng trong phiếu | *Danh sách sản phẩm chuyển kho không được chứa SKU trùng lặp.* | Một mã SKU xuất hiện ở 2 dòng khác nhau trong cùng một phiếu. | Gộp số lượng của các dòng cùng mã SKU vào một dòng duy nhất. |

---

### 2.5. Phân Hệ Kiểm Kê Kho (`STOCK_COUNT`)
*Liên kết hợp đồng API:* [`../05-api-contracts/03-inventory-endpoints.md`](../05-api-contracts/03-inventory-endpoints.md)  
*Liên kết màn hình:* [`../11-ui-screens/04-inventory-screens.md`](../11-ui-screens/04-inventory-screens.md)

| Mã lỗi (`ErrorCode`) | HTTP Status | Tiêu đề lỗi | Thông báo chi tiết mẫu | Nguyên nhân phát sinh | Hướng dẫn khắc phục trên UI |
| :--- | :---: | :--- | :--- | :--- | :--- |
| `STOCK_COUNT_NOT_FOUND` | `404` | Không tìm thấy đợt kiểm kê | *Không tìm thấy đợt kiểm kê trong phạm vi doanh nghiệp.* | ID đợt kiểm kê không tồn tại. | Kiểm tra lại danh sách đợt kiểm kê. |
| `STOCK_COUNT_DRIFT_DETECTED` | `409` | Tồn kho phần mềm thay đổi | *Số lượng tồn kho trên phần mềm của các SKU đã bị thay đổi trong quá trình kiểm kê.* | Có đơn bán hoặc phiếu nhập/chuyển kho làm biến động tồn trong lúc đang đếm. | Tải lại dữ liệu số dư sổ sách mới nhất và đối chiếu lại trước khi chốt. |
| `INCOMPLETE_STOCK_COUNT` | `400` | Chưa hoàn tất đếm | *Tất cả các mặt hàng phải được nhập số đếm thực tế trước khi chốt kiểm kê.* | Còn dòng SKU bị để trống số lượng thực đếm (`ActualQuantity = null`). | Nhân viên kiểm đếm cần nhập đủ số lượng thực tế cho toàn bộ danh sách. |
| `ADJUSTMENT_COST_REQUIRED` | `400` | Thiếu đơn giá điều chỉnh | *Mặt hàng thừa kiểm kê '{skuCode}' chưa có giá vốn, bắt buộc phải nhập đơn giá điều chỉnh.* | Phát hiện thừa hàng nhưng SKU này chưa từng có lịch sử nhập kho (WAC = 0). | Bắt buộc nhập đơn giá ước tính để tính toán giá trị tài sản thừa. |
| `INVALID_COUNT_ITEM` | `400` | Mặt hàng ngoài đợt kiểm kê | *SKU '{skuId}' không thuộc phạm vi đợt kiểm kê này.* | Ghi nhận số đếm của SKU không có trong danh mục kiểm kê ban đầu. | Chỉ kiểm đếm các SKU đã khai báo trong phiếu. |
| `STOCK_COUNT_SEQUENCE_DRIFT` | `409` | Sai lệch sequence kiểm kê | *Phát hiện giao dịch phát sinh xen giữa tại kho kiểm kê cho các SKU: 'SKU-001', 'SKU-002'.* | Phát sinh giao dịch xuất/nhập kho làm thay đổi sequence sổ sách trong lúc đếm hoặc trước khi nộp/chốt. | Gọi API đếm lại (recount) cho các SKU bị lệch để lấy snapshot sequence mới. |
| `STOCK_COUNT_SELF_APPROVAL` | `409` | Không được tự duyệt phiếu kiểm kê | *Người kiểm đếm không được tự phê duyệt chốt kiểm kê khi hệ thống còn Owner khác.* | Owner trực tiếp tham gia ghi số đếm trên một dòng hàng và cố gắng tự duyệt phiếu khi còn Owner khác. | Cần một Owner khác trong cửa hàng kiểm tra và phê duyệt phiếu. |
| `STOCK_COUNT_APPROVAL_REASON_REQUIRED` | `422` | Bắt buộc nhập lý do duyệt | *Owner duy nhất tự phê duyệt phiếu kiểm kê do chính mình đếm bắt buộc phải nhập ghi chú giải trình (Note).* | Duy nhất 1 Owner trong tenant tự duyệt phiếu có dòng do mình đếm nhưng không cung cấp Note giải trình. | Nhập lý do/ghi chú giải trình trong trường Note khi chốt duyệt. |
| `STOCK_COUNT_SKU_NOT_DRIFTED` | `409` | Sản phẩm không bị lệch sequence | *SKU '{skuCode}' không bị sai lệch sequence, không thể yêu cầu đếm lại.* | Gửi yêu cầu đếm lại cho SKU vẫn giữ nguyên sequence sổ sách. | Chỉ chọn các SKU bị cảnh báo lệch sequence để đếm lại. |
| `STOCK_COUNT_SCHEDULE_DUPLICATE` | `409` | Lịch kiểm kê bị trùng lặp | *Kho này đã có cấu hình lịch kiểm kê cho loại kỳ '{periodType}'.* | Tạo trùng cấu hình lịch cho cùng một kho và loại kỳ. | Chỉnh sửa lịch hiện có thay vì tạo mới. |

---

### 2.6. Phân Hệ Đơn Hàng & POS Checkout (`ORDER` & `POS`)
*Liên kết hợp đồng API:* [`../05-api-contracts/04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md), [`../05-api-contracts/05-pos-endpoints.md`](../05-api-contracts/05-pos-endpoints.md)  
*Liên kết màn hình:* [`../11-ui-screens/05-order-screens.md`](../11-ui-screens/05-order-screens.md), [`../11-ui-screens/06-pos-terminal-screens.md`](../11-ui-screens/06-pos-terminal-screens.md)

| Mã lỗi (`ErrorCode`) | HTTP Status | Tiêu đề lỗi | Thông báo chi tiết mẫu | Nguyên nhân phát sinh | Hướng dẫn khắc phục trên UI |
| :--- | :---: | :--- | :--- | :--- | :--- |
| `IDEMPOTENCY_PAYLOAD_MISMATCH` | `409` | Xung đột khóa Idempotency | *RequestKey đã được sử dụng cho một yêu cầu thanh toán khác với nội dung giỏ hàng khác.* | Bấm thanh toán lại với cùng một khóa `RequestKey` nhưng đã sửa sản phẩm trong giỏ hàng. | Khởi tạo lại `RequestKey` mới (GUID) khi thay đổi nội dung đơn. |
| `INSUFFICIENT_CASH_RECEIVED` | `400` | Tiền khách đưa chưa đủ | *Số tiền khách đưa ({amountGiven}đ) không đủ để thanh toán tổng tiền đơn ({totalAmount}đ).* | Nhân viên thu ngân nhập số tiền khách đưa nhỏ hơn tổng tiền phải trả. | Nhập số tiền khách đưa lớn hơn hoặc bằng tổng tiền đơn hàng. |
| `INVALID_PAYMENT_METHOD` | `400` | Sai phương thức thanh toán | *Phương thức thanh toán '{method}' không được hỗ trợ. Chỉ chấp nhận 'Cash' hoặc 'BankTransfer'.* | Chọn phương thức thanh toán ngoài danh mục quy định. | Chọn lại Tiền mặt (`Cash`) hoặc Chuyển khoản QR (`BankTransfer`). |
| `PAYMENT_ACCOUNT_DUPLICATE` | `409` | Trùng tài khoản ngân hàng | *Tài khoản ngân hàng {bin} - {account} đã tồn tại trong cửa hàng.* | Thêm hoặc sửa tài khoản ngân hàng trùng số BIN và số tài khoản với tài khoản khác trong cùng shop. | Kiểm tra lại số tài khoản ngân hàng hoặc cập nhật tài khoản đã có. |
| `PAYMENT_ACCOUNT_NOT_FOUND` | `404` | Không tìm thấy tài khoản nhận | *Không tìm thấy tài khoản thanh toán '{id}'.* | Gửi ID tài khoản ngân hàng không tồn tại hoặc thuộc tenant khác (cả ở API quản lý và ở checkout QR). | Kiểm tra lại ID tài khoản. |
| `PAYMENT_ACCOUNT_INACTIVE` | `409` (checkout) / `400` (đặt mặc định) | Tài khoản nhận đã ngừng dùng | *Tài khoản nhận tiền đã ngừng sử dụng. Vui lòng chọn tài khoản khác và tạo lại mã QR.* | Checkout QR với `paymentAccountId` đã bị Owner ngừng dùng, hoặc đặt tài khoản đã ngừng làm mặc định. Replay cùng `RequestKey` đã thành công không bị lỗi này. | Chọn tài khoản đang dùng và tạo lại mã QR. |
| `PAYMENT_ACCOUNT_IDENTITY_IMMUTABLE` | `400` | Không được đổi ngân hàng hoặc số tài khoản | *Không được đổi ngân hàng hoặc số tài khoản của tài khoản đã tạo...* | `PUT /api/payment-accounts/{id}` gửi BIN hoặc số tài khoản khác bản đã lưu. Chỉ được đổi tên hiển thị. | Thêm tài khoản mới rồi ngừng dùng tài khoản cũ. |
| `PAYMENT_ACCOUNT_CONFLICT` | `409` | Xung đột cập nhật tài khoản mặc định | *Tài khoản mặc định vừa được thay đổi bởi thao tác khác...* | Hai thao tác đặt mặc định chạm nhau và DB chặn bằng index `uq_payment_accounts_tenant_default_active`. | Tải lại danh sách và thử lại. |
| `ORDER_NOT_FOUND` | `404` | Không tìm thấy đơn hàng | *Không tìm thấy đơn hàng với mã '{orderId}'.* | Mã đơn hàng không tồn tại trong Tenant. | Kiểm tra lại danh sách đơn hàng. |
| `SALES_INVOICE_NOT_FOUND` | `404` | Không tìm thấy hóa đơn bán hàng | *Không tìm thấy hóa đơn bán hàng với mã định danh được yêu cầu.* | Mã hóa đơn hoặc số hóa đơn không tồn tại trong Tenant. | Kiểm tra lại số hóa đơn hoặc mã định danh. |
| `LEDGER_ENTRY_NOT_FOUND` | `404` | Không tìm thấy dòng sổ kho | *Không tìm thấy dòng sổ kho với mã định danh được yêu cầu.* | ID dòng sổ kho không tồn tại trong Tenant. | Kiểm tra lại mã dòng sổ kho cần truy vết. |
| `BRANCH_INACTIVE_OR_INVALID` | `404` | Chi nhánh không hợp lệ | *Chi nhánh '{branchId}' không tồn tại hoặc đã ngừng hoạt động.* | Chi nhánh bị xóa hoặc bị tắt kích hoạt. | Chọn chi nhánh khác đang hoạt động trên Header. |
| `SHOP_INTEGRATION_NOT_FOUND` | `404` | Không tìm thấy kết nối gian hàng | *Không tìm thấy kết nối gian hàng.* | ID kết nối không tồn tại trong Tenant hiện tại. | Tải lại danh sách kết nối sàn. |
| `SHOP_INTEGRATION_DUPLICATE` | `409` | Gian hàng đã được kết nối | *Gian hàng '{externalShopId}' trên kênh {channel} đã được kết nối.* | Trùng `(tenant_id, channel, external_shop_id)` (`uq_shop_integrations_2`). | Sửa kết nối đã có hoặc dùng mã gian hàng khác. |
| `CONCURRENCY_CONFLICT` | `409` | Xung đột dữ liệu đồng thời | *Dữ liệu đã được cập nhật bởi một người dùng khác trong lúc bạn đang thao tác.* | Lạc quan khóa phiên bản (Optimistic Concurrency Control) phát hiện phiên bản cũ. | Bấm nút "Tải lại trang" để lấy dữ liệu mới nhất rồi thực hiện lại. |

---

### 2.6a. Phân Hệ Trả Hàng Từ Khách (`SALES_RETURNS`, `CS-01`)
*Liên kết hợp đồng API:* [`../05-api-contracts/04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md)  
*Liên kết đặc tả nghiệp vụ:* [`../Upgrade-recommended/08-tra-hang-cs01.md`](../Upgrade-recommended/08-tra-hang-cs01.md)

| Mã lỗi (`ErrorCode`) | HTTP Status | Tiêu đề lỗi | Thông báo chi tiết mẫu | Nguyên nhân phát sinh | Hướng dẫn khắc phục trên UI |
| :--- | :---: | :--- | :--- | :--- | :--- |
| `SALES_RETURN_NOT_FOUND` | `404` | Không tìm thấy phiếu trả hàng | *Không tìm thấy phiếu trả hàng.* | Phiếu trả hàng không tồn tại hoặc thuộc tenant khác. | Kiểm tra lại số phiếu trả hàng. |
| `ORDER_NOT_RETURNABLE` | `409` | Đơn hàng không thể trả | *Đơn hàng chưa xuất kho hoặc đã bị hủy.* | Đơn hàng không ở trạng thái Confirmed hoặc Completed. | Chỉ lập phiếu trả cho đơn hàng đã xuất kho thành công. |
| `RETURN_QUANTITY_EXCEEDED` | `422` | Vượt số lượng được trả | *Số lượng trả vượt quá số lượng còn được phép trả.* | Tổng số lượng trả trên dòng đơn vượt quá (Quantity - đã trả trước đó). | Giảm số lượng trả xuống mức cho phép. |
| `REFUND_AMOUNT_EXCEEDED` | `422` | Tiền hoàn vượt mức cho phép | *Tiền hoàn vượt quá giá trị dòng sản phẩm hoặc tổng đơn hàng.* | Tiền hoàn nhập cao hơn đơn giá × số lượng trả hoặc vượt tổng tiền đơn. | Giảm số tiền hoàn lại cho khách. |
| `RETURN_WAREHOUSE_MISMATCH` | `422` | Kho nhận không thuộc chi nhánh đơn hàng | *Kho nhận hàng trả không thuộc chi nhánh của đơn hàng gốc.* | Chọn kho không thuộc chi nhánh đã xuất bán đơn hàng. | Chọn kho hàng thuộc đúng chi nhánh của đơn. |

---

### 2.7. Phân Hệ Hệ Thống & Hạ Tầng (`SYSTEM`)

| Mã lỗi (`ErrorCode`) | HTTP Status | Tiêu đề lỗi | Thông báo chi tiết mẫu | Nguyên nhân phát sinh | Hướng dẫn khắc phục trên UI |
| :--- | :---: | :--- | :--- | :--- | :--- |
| `DATABASE_CONNECTION_ERROR` | `500` | Lỗi kết nối CSDL | *Không thể kết nối đến máy chủ cơ sở dữ liệu.* | Mất kết nối mạng CSDL hoặc CSDL đang khởi động lại. | Hiển thị màn hình lỗi chờ kết nối và tự động thử lại sau 5 giây. |
| `INTERNAL_SERVER_ERROR` | `500` | Lỗi hệ thống nội bộ | *Đã xảy ra lỗi không mong muốn trên máy chủ.* | Ngoại lệ chưa được bắt (Unhandled Exception). | Hiển thị thông báo xin lỗi người dùng và ghi nhật ký Log với CorrelationId. |
| `NETWORK_DISCONNECTED` | `0` (Client) | Mất kết nối Internet | *Mất kết nối mạng Internet. Hệ thống đang chuyển sang chế độ dự phòng Offline.* | Trình duyệt mất kết nối mạng (`navigator.onLine === false`). | Đổi trạng thái Header sang màu đỏ "OFFLINE", chặn các thao tác đồng bộ nhạy cảm. |

---

### 2.8. Trợ Lý OISMS (`AI`)

Các lỗi nghiệp vụ từ `POST /api/ai/chat` trả `application/problem+json` với trường `errorCode`.

| Mã lỗi | HTTP | Tiêu đề | Xử lý trên giao diện |
| :--- | :---: | :--- | :--- |
| `AI_INVALID_REQUEST` | 400 | Yêu cầu trợ lý AI không hợp lệ | Hiển thị chi tiết về lịch sử, chi nhánh hoặc khoảng ngày cần sửa. |
| `AI_RATE_LIMITED` | 429 | Vượt giới hạn yêu cầu trợ lý AI | Giới hạn của OISMS: 10 câu/phút mỗi người dùng. Hiển thị thông báo và khóa gửi 30 giây. |
| `AI_MODEL_QUOTA` | 429 | Model AI đã hết lượt | Nhà cung cấp của model đã chọn báo hết lượt (theo phút hoặc ngày) sau khi backend đã tự chờ và thử lại một lần nếu chỉ cần chờ ≤ 10 giây. Hiển thị `detail` và nút hỏi lại bằng model khác; không khóa gửi. |
| `AI_DISABLED` | 503 | Trợ lý AI chưa được bật | Hiển thị lý do trong panel và ẩn nút mở đến hết phiên. |
| `AI_UPSTREAM_ERROR` | 502 | Lỗi nhà cung cấp AI | Hiển thị lỗi tạm thời và cho phép gửi lại tin cuối. |

---

## 3. Đối Chiếu Với Code (06/11/2026)

Quét mọi `new OismException("<MÃ>", ..., <HTTP>)`, `CreateProblemResult("<MÃ>", ...)` trong `src/backend/src` và `ExceptionHandlingMiddleware`.

### 3.1. Mã backend đang trả nhưng chưa có ở mục 2

| Mã lỗi | HTTP | Nơi phát sinh | Khi nào |
| :--- | :---: | :--- | :--- |
| `BRANCH_MISMATCH` | `400` | `SimulatorService` | Chi nhánh trong yêu cầu mô phỏng không khớp cấu hình gian hàng |
| `CHANNEL_MISMATCH` | `400` | `WebhookCanonicalValidator` | Kênh trong payload khác kênh trên URL webhook |
| `INTEGRATION_NOT_FOUND` | `404` | `SimulatorService` | Không có `shop_integrations` phù hợp |
| `WEBHOOK_PAYLOAD_MISMATCH` | `409` | `OrderService` | Cùng mã đơn sàn nhưng nội dung khác lần trước |
| `TOTAL_AMOUNT_MISMATCH` | `400` | `OrderService` | `totalAmount` client gửi khác tổng tính lại |
| `INCONSISTENT_STOCK` | `409` | `OrderService` | Số dư giữ chỗ ở kho/chi nhánh không đủ để giải phóng (dữ liệu lệch) |
| `CATEGORY_NOT_FOUND` | `400` | `ProductService` | Danh mục gán cho sản phẩm không tồn tại trong tenant |
| `PRODUCT_NOT_FOUND`, `VARIANT_NOT_FOUND` | `404` | `ProductService` | Không tìm thấy sản phẩm / biến thể |
| `EMPTY_VARIANTS`, `NULL_VARIANTS` | `400` | `ProductService` | Tạo sản phẩm không có biến thể |
| `DUPLICATE_INPUT_SKU` | `400` | `ProductService` | Trùng mã SKU ngay trong request |
| `DUPLICATE_INPUT_BARCODE`, `DUPLICATE_COMBINATION` | `409` | `ProductService` | Trùng barcode trong request / trùng tổ hợp thuộc tính biến thể |
| `EMPTY_BARCODE` | `400` | `PosCatalogService` | Quét mã rỗng |
| `EMPTY_WAREHOUSE_STOCK_COUNT` | `400` | `StockCountService` | Kho không có mặt hàng nào để kiểm kê |
| `WAREHOUSE_HAS_DEPENDENCIES`, `FK_VIOLATION` | `409` | `WarehouseService` | Kho đã có tồn/sổ cái hoặc bị tham chiếu |
| `TARGET_BRANCH_INVALID` | `400` | `WarehouseService` | Chi nhánh đích khi đổi chi nhánh của kho không hợp lệ |
| `TENANT_INACTIVE`, `USER_INACTIVE`, `INVALID_TOKEN` | `401` | `AuthService` | Cửa hàng/tài khoản bị khóa, refresh token sai hoặc hết hạn |
| `EMPTY_TOKEN`, `CONSTRAINT_VIOLATION` | `400` | `AuthService` | Thiếu refresh token / vi phạm ràng buộc khi đăng ký |
| `NOT_FOUND` | `404` | Nhiều controller (`CreateProblemResult`) | Không tìm thấy bản ghi theo ID (phiếu chuyển kho, kiểm kê...) |
| `UNAUTHORIZED` | `401` | `ExceptionHandlingMiddleware` | Service ném `UnauthorizedAccessException` (thiếu tenant context) |

### 3.2. Mã có ở mục 2 nhưng backend chưa trả

| Mã | Ghi chú |
| :--- | :--- |
| `AUTH_INVALID_CREDENTIALS`, `AUTH_TENANT_NOT_FOUND`, `AUTH_TENANT_LOCKED`, `AUTH_USER_LOCKED`, `AUTH_UNAUTHORIZED`, `AUTH_TOKEN_EXPIRED` | `AuthController` trả JSON `{ "message": "..." }` (không phải Problem Details) với 400/401/409; tài khoản hoặc cửa hàng bị khóa trả `401` kèm `USER_INACTIVE`/`TENANT_INACTIVE` ở tầng service. Token hết hạn do middleware JWT trả `401` không có body. Frontend Admin quy về mã `AUTH_FAILED`. |
| `NETWORK_DISCONNECTED` | Mã phía client, không do server trả. |
| `CATEGORY_CYCLIC_DEPENDENCY`, `LEDGER_IMMUTABLE_VIOLATION`, `CONCURRENCY_CONFLICT`, `DATABASE_CONNECTION_ERROR` | Quy ước dự kiến. Hiện vòng lặp danh mục, vi phạm trigger sổ cái, lỗi kết nối DB rơi vào mã khác hoặc `INTERNAL_SERVER_ERROR`. |
| `SAME_WAREHOUSE_TRANSFER`, `DUPLICATE_TRANSFER_SKU`, `TRANSFER_NOT_FOUND`, `STOCK_COUNT_NOT_FOUND` | Service kiểm tra các trường hợp này nhưng trả mã chung `VALIDATION_ERROR` (400) hoặc `NOT_FOUND` (404) kèm `detail` cụ thể; cần đối chiếu từng endpoint trước khi dựa vào mã. |

Khi thêm mã mới vào code, bổ sung vào mục 2 và xóa khỏi bảng 3.1.
