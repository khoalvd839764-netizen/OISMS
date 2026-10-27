# 02. TỪ ĐIỂN DANH MỤC MÃ LỖI TOÀN HỆ THỐNG (ERROR CODES CATALOG)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-ERR-02` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực** | `2026-10-25` |
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

---

### 2.2. Phân Hệ Danh Mục, Sản Phẩm & Biến Thể SKU (`PROD`)
*Liên kết hợp đồng API:* [`../05-api-contracts/02-catalog-endpoints.md`](../05-api-contracts/02-catalog-endpoints.md)  
*Liên kết màn hình:* [`../11-ui-screens/03-product-screens.md`](../11-ui-screens/03-product-screens.md)

| Mã lỗi (`ErrorCode`) | HTTP Status | Tiêu đề lỗi | Thông báo chi tiết mẫu | Nguyên nhân phát sinh | Hướng dẫn khắc phục trên UI |
| :--- | :---: | :--- | :--- | :--- | :--- |
| `SKU_NOT_FOUND` | `404` | Mặt hàng không tồn tại | *Mặt hàng biến thể SKU '{skuId}' không tồn tại trong hệ thống.* | ID hoặc mã SKU không tồn tại trong CSDL của Tenant. | Kiểm tra lại danh mục sản phẩm hoặc quét lại mã vạch. |
| `SKU_INACTIVE` | `422` | Mặt hàng ngừng hoạt động | *Mặt hàng SKU '{skuCode}' hoặc sản phẩm liên quan đang ngừng hoạt động.* | Thuộc tính `IsActive = false` trên SKU hoặc Product. | Kích hoạt lại sản phẩm trên màn hình Quản lý Sản phẩm trước khi bán. |
| `BARCODE_DUPLICATE` | `409` | Trùng lặp mã vạch | *Mã vạch '{barcode}' đã được sử dụng cho một SKU khác trong cửa hàng.* | Vi phạm ràng buộc duy nhất mã vạch `uq_skus_barcode`. | Nhập mã vạch khác hoặc bấm nút "Tự động sinh mã EAN-13". |
| `PRICE_MISMATCH` | `400` | Sai lệch đơn giá niêm yết | *Đơn giá của mặt hàng '{skuCode}' ({unitPrice}đ) không khớp với giá bán lẻ niêm yết ({retailPrice}đ).* | Giá gửi lên từ client bị sai lệch so với bảng giá niêm yết trong CSDL. | Tải lại thông tin sản phẩm và cập nhật lại giỏ hàng. |
| `CATEGORY_CYCLIC_DEPENDENCY` | `409` | Vòng lặp danh mục | *Không thể gán danh mục cha là chính nó hoặc danh mục con cháu.* | Phát hiện vòng tròn quan hệ cha - con trong cây danh mục. | Chọn danh mục cha hợp lệ không thuộc nhánh con hiện tại. |
| `BRAND_NOT_FOUND` | `404` | Thương hiệu không tồn tại | *Không tìm thấy thông tin thương hiệu được chỉ định.* | ID thương hiệu không tồn tại hoặc bị xóa. | Chọn thương hiệu có sẵn từ danh sách thả xuống. |

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

---

### 2.6. Phân Hệ Đơn Hàng & POS Checkout (`ORDER` & `POS`)
*Liên kết hợp đồng API:* [`../05-api-contracts/04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md), [`../05-api-contracts/05-pos-endpoints.md`](../05-api-contracts/05-pos-endpoints.md)  
*Liên kết màn hình:* [`../11-ui-screens/05-order-screens.md`](../11-ui-screens/05-order-screens.md), [`../11-ui-screens/06-pos-terminal-screens.md`](../11-ui-screens/06-pos-terminal-screens.md)

| Mã lỗi (`ErrorCode`) | HTTP Status | Tiêu đề lỗi | Thông báo chi tiết mẫu | Nguyên nhân phát sinh | Hướng dẫn khắc phục trên UI |
| :--- | :---: | :--- | :--- | :--- | :--- |
| `IDEMPOTENCY_PAYLOAD_MISMATCH` | `409` | Xung đột khóa Idempotency | *RequestKey đã được sử dụng cho một yêu cầu thanh toán khác với nội dung giỏ hàng khác.* | Bấm thanh toán lại với cùng một khóa `RequestKey` nhưng đã sửa sản phẩm trong giỏ hàng. | Khởi tạo lại `RequestKey` mới (GUID) khi thay đổi nội dung đơn. |
| `INSUFFICIENT_CASH_RECEIVED` | `400` | Tiền khách đưa chưa đủ | *Số tiền khách đưa ({amountGiven}đ) không đủ để thanh toán tổng tiền đơn ({totalAmount}đ).* | Nhân viên thu ngân nhập số tiền khách đưa nhỏ hơn tổng tiền phải trả. | Nhập số tiền khách đưa lớn hơn hoặc bằng tổng tiền đơn hàng. |
| `INVALID_PAYMENT_METHOD` | `400` | Sai phương thức thanh toán | *Phương thức thanh toán '{method}' không được hỗ trợ. Chỉ chấp nhận 'Cash' hoặc 'BankTransfer'.* | Chọn phương thức thanh toán ngoài danh mục quy định. | Chọn lại Tiền mặt (`Cash`) hoặc Chuyển khoản QR (`BankTransfer`). |
| `ORDER_NOT_FOUND` | `404` | Không tìm thấy đơn hàng | *Không tìm thấy đơn hàng với mã '{orderId}'.* | Mã đơn hàng không tồn tại trong Tenant. | Kiểm tra lại danh sách đơn hàng. |
| `BRANCH_INACTIVE_OR_INVALID` | `404` | Chi nhánh không hợp lệ | *Chi nhánh '{branchId}' không tồn tại hoặc đã ngừng hoạt động.* | Chi nhánh bị xóa hoặc bị tắt kích hoạt. | Chọn chi nhánh khác đang hoạt động trên Header. |
| `CONCURRENCY_CONFLICT` | `409` | Xung đột dữ liệu đồng thời | *Dữ liệu đã được cập nhật bởi một người dùng khác trong lúc bạn đang thao tác.* | Lạc quan khóa phiên bản (Optimistic Concurrency Control) phát hiện phiên bản cũ. | Bấm nút "Tải lại trang" để lấy dữ liệu mới nhất rồi thực hiện lại. |

---

### 2.7. Phân Hệ Hệ Thống & Hạ Tầng (`SYSTEM`)

| Mã lỗi (`ErrorCode`) | HTTP Status | Tiêu đề lỗi | Thông báo chi tiết mẫu | Nguyên nhân phát sinh | Hướng dẫn khắc phục trên UI |
| :--- | :---: | :--- | :--- | :--- | :--- |
| `DATABASE_CONNECTION_ERROR` | `500` | Lỗi kết nối CSDL | *Không thể kết nối đến máy chủ cơ sở dữ liệu.* | Mất kết nối mạng CSDL hoặc CSDL đang khởi động lại. | Hiển thị màn hình lỗi chờ kết nối và tự động thử lại sau 5 giây. |
| `INTERNAL_SERVER_ERROR` | `500` | Lỗi hệ thống nội bộ | *Đã xảy ra lỗi không mong muốn trên máy chủ.* | Ngoại lệ chưa được bắt (Unhandled Exception). | Hiển thị thông báo xin lỗi người dùng và ghi nhật ký Log với CorrelationId. |
| `NETWORK_DISCONNECTED` | `0` (Client) | Mất kết nối Internet | *Mất kết nối mạng Internet. Hệ thống đang chuyển sang chế độ dự phòng Offline.* | Trình duyệt mất kết nối mạng (`navigator.onLine === false`). | Đổi trạng thái Header sang màu đỏ "OFFLINE", chặn các thao tác đồng bộ nhạy cảm. |
