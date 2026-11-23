# Báo cáo rà soát bảo mật OISMS

| Thông tin | Giá trị |
| --- | --- |
| Ngày rà soát | 18/11/2026 |
| Nhánh | `feature/OISMS-59-hoan-thanh-nghiep-vu-con-lai` |
| Commit gốc | `b917822` (working tree có thay đổi chưa commit) |
| Phạm vi | Backend, cấu hình triển khai, frontend Admin, POS và app giả lập |
| Trạng thái | S1 đã vá trong code, còn chờ đổi khóa trên EC2. Các lỗi còn lại **chưa sửa** |

## 1. Giới hạn của đợt rà soát

| Hạng mục | Kết quả |
| --- | --- |
| Đọc và đối chiếu source | Đã làm |
| Chạy thử tấn công trên hệ thống đang chạy | **NOT RUN** |
| Kiểm tra `JWT_SECRET` thực tế trên EC2 | **NOT RUN** |
| Quét CVE thư viện .NET và npm | **NOT RUN** |
| Viết test tái hiện từng lỗi | **NOT RUN** |

Mỗi lỗi cần có test tái hiện trước khi sửa. Các thay đổi chạm auth, tenant, migration, transaction, reservation hoặc concurrency phải có bằng chứng trên PostgreSQL thật.

## 2. Tổng quan

| Mức | Số lượng | Mã lỗi |
| --- | --- | --- |
| Nghiêm trọng | 1 | S1 |
| Cao | 6 | H1, H2, H3, H4, H5, H6 |
| Trung bình | 6 | M1, M2, M3, M4, M5, M6 |
| Thấp | 3 | L1, L2, L3 |
| **Tổng** | **16** | |

| ID | Mức | Lỗi | Ai khai thác được | Đợt | Trạng thái |
| --- | --- | --- | --- | --- | --- |
| S1 | Nghiêm trọng | Khóa ký JWT công khai trong repo | Bất kỳ ai, nếu môi trường dùng khóa mẫu | 0 | Đã vá code, chờ đổi khóa EC2 |
| H1 | Cao | Cashier giữ chỗ toàn bộ tồn mọi chi nhánh | Cashier | 0 | Chưa sửa |
| H2 | Cao | Cashier tra cứu đơn và khách qua tìm kiếm toàn cục | Cashier | 0 | Chưa sửa |
| H3 | Cao | Staff tạo đơn sàn với giá tùy ý | Staff | 0 | Chưa sửa |
| H4 | Cao | Giữ chỗ gắn được vào đơn ở mọi trạng thái | Owner, Staff | 1 | Chưa sửa |
| H5 | Cao | Thiếu chống dò mật khẩu và lạm dụng API ẩn danh | Ẩn danh | 1 | Chưa sửa |
| H6 | Cao | Khóa tài khoản không có hiệu lực ngay | Nhân viên đã bị khóa hoặc hạ quyền | 2 | Chưa sửa |
| M1 | Trung bình | Không phát hiện refresh token bị dùng lại | Người lấy được token | 2 | Chưa sửa |
| M2 | Trung bình | Phân công chi nhánh chưa áp dụng đầy đủ | Cashier, Staff | 1 | Chưa sửa |
| M3 | Trung bình | Thanh toán chuyển khoản không cần chứng từ | Cashier | 3 | Chưa sửa |
| M4 | Trung bình | Hoàn tiền không có bước duyệt | Staff | 3 | Chưa sửa |
| M5 | Trung bình | Không có nhật ký bảo mật | Không áp dụng | 3 | Chưa sửa |
| M6 | Trung bình | Đăng ký không xác minh email | Ẩn danh | 3 | Chưa sửa |
| L1 | Thấp | Thiếu header bảo mật | Không áp dụng | 3 | Chưa sửa |
| L2 | Thấp | Cấu hình container và cổng chưa chặt | Người vào được máy chủ hoặc mạng | 3 | Chưa sửa |
| L3 | Thấp | Spam mail đặt lại mật khẩu, Cashier đọc nhà cung cấp | Ẩn danh, Cashier | 3 | Chưa sửa |

## 3. Chi tiết phát hiện

### S1. Khóa ký JWT công khai trong repo

- **Trạng thái:** đã vá trong code ngày 18/11/2026. Còn phải tạo khóa mới trên EC2 trước khi deploy.
- **Nguyên nhân:** `Program.cs`, `JwtTokenService.cs` có khóa dự phòng viết cứng và `appsettings.json` chứa cùng giá trị. `docker-compose.yml` không truyền `Jwt__Secret`, chỉ [docker-compose.prod.yml](../../docker-compose.prod.yml) bắt buộc biến này.
- **Hậu quả:** người biết khóa tự ký được token Owner với `tenantId` bất kỳ, từ đó đọc và ghi dữ liệu của mọi cửa hàng.
- **Đã sửa:**
  1. Bỏ khóa viết cứng trong code và `appsettings.json`, `.env.example` không còn giá trị mẫu.
  2. Thêm [JwtSigningKeyPolicy.cs](../../src/backend/src/OISM.Infrastructure/Services/JwtSigningKeyPolicy.cs): ngoài Development và Test, backend dừng khởi động nếu khóa thiếu, ngắn hơn 32 byte hoặc trùng khóa mẫu cũ.
  3. Development và Test thiếu khóa hợp lệ thì dùng khóa ngẫu nhiên theo tiến trình.
  4. [docker-compose.yml](../../docker-compose.yml) truyền `Jwt__Secret` từ `JWT_SECRET`.
- **Việc còn lại:** tạo khóa mới cho EC2 bằng `openssl rand -hex 32`. Khóa cũ đã nằm trong lịch sử git nên không được dùng lại. Đổi khóa sẽ đăng xuất mọi phiên hiện tại.
- **Kiểm chứng:** [JwtSigningKeyPolicyTests.cs](../../test-oisms/backend/OISM.UnitTests/JwtSigningKeyPolicyTests.cs) và [JwtSigningKeyStartupTests.cs](../../test-oisms/backend/OISM.IntegrationTests/JwtSigningKeyStartupTests.cs).

### H1. Cashier giữ chỗ toàn bộ tồn mọi chi nhánh

- **Vị trí:** [InventoryController.cs:96](../../src/backend/src/OISM.WebApi/Controllers/InventoryController.cs#L96), [ReservationDtos.cs:39](../../src/backend/src/OISM.Application/Features/Inventory/Dtos/ReservationDtos.cs#L39).
- **Nguyên nhân:** `POST /api/inventory/reserve` cho phép Cashier, không kiểm tra chi nhánh được phân công, không giới hạn tổng lượng giữ. Thời hạn giữ chỗ tối đa 1440 phút do client chọn.
- **Hậu quả:** một tài khoản Cashier làm tồn khả dụng về 0 ở mọi chi nhánh, khiến POS và đơn sàn không bán được. Khi Hangfire tắt, giữ chỗ không tự hết hạn.
- **Cách sửa:** bỏ `Cashier` khỏi endpoint này vì `pos-app` không gọi nó. Giới hạn thời hạn phía server.
- **Kiểm chứng:** Cashier nhận 403, Owner và Staff vẫn giữ chỗ được.
- **Cần quyết định:** xem mục 5, câu 1.

### H2. Cashier tra cứu đơn và khách qua tìm kiếm toàn cục

- **Vị trí:** [SearchController.cs:27](../../src/backend/src/OISM.WebApi/Controllers/SearchController.cs#L27), [SearchService.cs](../../src/backend/src/OISM.Application/Features/Search/Services/SearchService.cs).
- **Nguyên nhân:** `GET /api/search` tìm đơn theo mã, tên hoặc số điện thoại khách cho mọi vai trò, trong khi `GET /api/orders` chỉ dành cho Owner và Staff. Controller còn mặc định vai trò `Staff` khi thiếu claim.
- **Hậu quả:** Cashier xem được tên khách, tổng tiền và trạng thái đơn của mọi chi nhánh, đồng thời dò được số điện thoại thuộc khách nào.
- **Cách sửa:** chỉ trả nhóm đơn hàng cho Owner và Staff. Thiếu claim vai trò thì từ chối thay vì coi là `Staff`.
- **Kiểm chứng:** test Cashier nhận danh sách đơn rỗng, Owner và Staff giữ nguyên kết quả.

### H3. Staff tạo đơn sàn với giá tùy ý

- **Vị trí:** [WebhookService.cs:119](../../src/backend/src/OISM.Application/Features/Webhooks/Services/WebhookService.cs#L119), [MarketplaceOrderService.cs:28](../../src/backend/src/OISM.Application/Features/Orders/Services/MarketplaceOrderService.cs#L28), [WebhookCanonicalValidator.cs:156](../../src/backend/src/OISM.Application/Features/Webhooks/Services/WebhookCanonicalValidator.cs#L156), [SimulatorController.cs:30](../../src/backend/src/OISM.WebApi/Controllers/SimulatorController.cs#L30).
- **Nguyên nhân:** webhook nhận JWT của Staff và tin đơn giá trong payload, kể cả 0đ. Endpoint `batch` cũng mở ở production.
- **Hậu quả:** khi cửa hàng có kết nối sàn đang bật, Staff tạo đơn 0đ rồi xác nhận xuất kho, bỏ qua quy trình Owner duyệt giá và làm sai doanh thu.
- **Cách sửa:** ở production chỉ nhận `X-Webhook-Secret`, tắt JWT mode và `batch` bằng cấu hình. Secret chỉ Owner lấy được.
- **Kiểm chứng:** test production từ chối JWT và `batch`, luồng secret vẫn tạo đơn được.
- **Cần quyết định:** xem mục 5, câu 2.

### H4. Giữ chỗ gắn được vào đơn ở mọi trạng thái

- **Vị trí:** [ReservationService.cs:146](../../src/backend/src/OISM.Application/Features/Inventory/Services/ReservationService.cs#L146), [OrderService.Expiry.cs:242](../../src/backend/src/OISM.Application/Features/Orders/Services/OrderService.Expiry.cs#L242), [OrderService.cs:190](../../src/backend/src/OISM.Application/Features/Orders/Services/OrderService.cs#L190).
- **Nguyên nhân:** khi truyền `OrderId` và `OrderItemId`, service không kiểm tra trạng thái đơn và không đối chiếu số lượng với dòng đơn. Chỉ trùng kho mới bị chặn, nên chọn kho khác cùng chi nhánh là qua được.
- **Hậu quả:**
  - Đơn đã hoàn tất hoặc đã hủy: job hết hạn bỏ qua đơn này, `Reserved` bị khóa vĩnh viễn.
  - Đơn đang `Reserved`: tổng giữ chỗ lệch dòng đơn nên xác nhận đơn trả 409.
- **Cách sửa:** chỉ nhận đơn còn hợp lệ để giữ chỗ, tổng giữ chỗ không vượt `BaseQuantity` của dòng đơn. Kiểm tra dưới khóa theo đúng thứ tự khóa hiện có.
- **Kiểm chứng:** integration test PostgreSQL cho từng trạng thái đơn và trường hợp đồng thời.

### H5. Thiếu chống dò mật khẩu và lạm dụng API ẩn danh

- **Vị trí:** [AuthController.cs:32](../../src/backend/src/OISM.WebApi/Controllers/AuthController.cs#L32), [AuthController.cs:72](../../src/backend/src/OISM.WebApi/Controllers/AuthController.cs#L72), [WebhooksController.cs:30](../../src/backend/src/OISM.WebApi/Controllers/WebhooksController.cs#L30), [RateLimitPolicies.cs:19](../../src/backend/src/OISM.WebApi/Http/RateLimitPolicies.cs#L19), [RegisterTenantValidator.cs:31](../../src/backend/src/OISM.Application/Features/Auth/RegisterTenant/RegisterTenantValidator.cs#L31).
- **Nguyên nhân:** đăng ký, refresh token và webhook không có rate limit. Đăng nhập chỉ giới hạn theo IP, không khóa theo tài khoản. Mật khẩu tối thiểu 6 ký tự.
- **Hậu quả:** dò mật khẩu từ nhiều IP, tạo cửa hàng rác không giới hạn và tiêu tốn CPU do BCrypt.
- **Cách sửa:** thêm rate limit cho các endpoint ẩn danh, khóa tạm theo tài khoản sau nhiều lần sai, nâng độ dài mật khẩu tối thiểu cho tài khoản mới.
- **Kiểm chứng:** test vượt ngưỡng nhận 429, tài khoản bị khóa tạm rồi tự mở lại.

### H6. Khóa tài khoản không có hiệu lực ngay

- **Vị trí:** [UserService.cs:307](../../src/backend/src/OISM.Application/Features/Users/Services/UserService.cs#L307), [Program.cs:56](../../src/backend/src/OISM.WebApi/Program.cs#L56).
- **Nguyên nhân:** xác thực JWT không đối chiếu trạng thái người dùng trong DB. Khóa tài khoản không thu hồi refresh token.
- **Hậu quả:** nhân viên bị khóa, bị hạ quyền hoặc cửa hàng bị ngưng vẫn dùng access token cũ tới 60 phút. Kết nối SignalR vẫn mở. Khi mở khóa lại, refresh token cũ dùng tiếp được.
- **Cách sửa:** thêm `token_version` cho người dùng và đưa vào JWT, kiểm tra trong `OnTokenValidated` có cache ngắn. Tăng version khi khóa, đổi vai trò hoặc đổi mật khẩu, đồng thời thu hồi refresh token và ngắt SignalR.
- **Lưu ý:** đây là thay đổi schema, phải làm đủ migration, `oisms_schema.sql` và tài liệu liên quan.
- **Kiểm chứng:** test PostgreSQL token cũ nhận 401 ngay sau khi khóa hoặc đổi vai trò.

### M1. Không phát hiện refresh token bị dùng lại

- **Vị trí:** [AuthService.cs:242](../../src/backend/src/OISM.Application/Features/Auth/Services/AuthService.cs#L242), [posSession.ts:37](../../src/frontend/pos-app/src/services/posSession.ts#L37), [httpClient.ts:188](../../src/frontend/admin-dashboard/src/services/httpClient.ts#L188).
- **Nguyên nhân:** token đã xoay vòng bị dùng lại chỉ nhận 401, các phiên liên quan vẫn sống. Token được lưu trong `localStorage`.
- **Hậu quả:** kẻ lấy được token có thể giữ phiên lâu dài mà hệ thống không phát hiện.
- **Cách sửa:** lưu chuỗi token theo `family_id`, phát hiện dùng lại thì thu hồi cả chuỗi. Xử lý trường hợp hai tab refresh cùng lúc để không đăng xuất nhầm.
- **Kiểm chứng:** test PostgreSQL dùng lại token cũ làm token mới mất hiệu lực.

### M2. Phân công chi nhánh chưa áp dụng đầy đủ

- **Vị trí:** [SalesInvoiceService.cs:137](../../src/backend/src/OISM.Application/Features/SalesInvoices/Services/SalesInvoiceService.cs#L137), [SalesInvoiceService.cs:153](../../src/backend/src/OISM.Application/Features/SalesInvoices/Services/SalesInvoiceService.cs#L153), [InventoryController.cs:39](../../src/backend/src/OISM.WebApi/Controllers/InventoryController.cs#L39), [InventoryController.cs:62](../../src/backend/src/OISM.WebApi/Controllers/InventoryController.cs#L62), [BranchAccessService.cs:44](../../src/backend/src/OISM.Application/Features/Branches/Services/BranchAccessService.cs#L44).
- **Nguyên nhân:** chỉ checkout, danh sách chi nhánh và nhóm realtime kiểm tra phân công. Cashier chưa được gán thì vào được mọi chi nhánh, Staff chưa bị lọc theo chi nhánh.
- **Hậu quả:** Cashier đọc hóa đơn kèm tên, số điện thoại khách và tồn kho của chi nhánh khác.
- **Cách sửa:** áp kiểm tra phân công cho in lại hóa đơn và xem tồn. Phạm vi của Staff làm cùng phần còn thiếu của CS-02.
- **Kiểm chứng:** test Cashier nhận 403 khi truy cập chi nhánh ngoài phân công.
- **Cần quyết định:** xem mục 5, câu 4.

### M3. Thanh toán chuyển khoản không cần chứng từ

- **Vị trí:** [PosCheckoutService.cs:389](../../src/backend/src/OISM.Application/Features/Orders/Services/PosCheckoutService.cs#L389).
- **Nguyên nhân:** tài liệu chốt thu ngân xác nhận thủ công, nhưng backend cho phép bỏ trống cả `PaymentAccountId` và `TransferReference`.
- **Hậu quả:** gọi API trực tiếp có thể hoàn tất đơn chuyển khoản mà không để lại dấu vết đối soát.
- **Cách sửa:** bắt buộc hai trường này với `BankTransfer`, thêm báo cáo đối soát theo tài khoản nhận.
- **Kiểm chứng:** test thiếu trường nhận 400, idempotency replay của đơn cũ vẫn hoạt động.

### M4. Hoàn tiền không có bước duyệt

- **Vị trí:** [SalesReturnService.cs:312](../../src/backend/src/OISM.Application/Features/SalesReturns/Services/SalesReturnService.cs#L312).
- **Nguyên nhân:** một Staff tự lập và tự xác nhận phiếu trả có hoàn tiền. Ma trận phân quyền hiện cho phép việc này.
- **Hậu quả:** có thể rút tiền mặt bằng phiếu trả không có thật, đồng thời làm tăng tồn trên sổ.
- **Cách sửa:** phiếu từ ngưỡng đã chốt trở lên phải do Owner xác nhận, người duyệt khác người lập khi cửa hàng có nhiều Owner.
- **Kiểm chứng:** test Staff bị chặn khi vượt ngưỡng.
- **Cần quyết định:** xem mục 5, câu 3.

### M5. Không có nhật ký bảo mật

- **Vị trí:** toàn hệ thống, `oisms_schema.sql` không có bảng nhật ký kiểm toán.
- **Hậu quả:** khó điều tra đăng nhập sai, đổi quyền, đặt lại mật khẩu, đổi tài khoản nhận tiền và hoàn tiền.
- **Cách sửa:** thêm bảng nhật ký chỉ ghi thêm, có `tenant_id` và query filter, không lưu mật khẩu hay token.
- **Kiểm chứng:** test mỗi sự kiện quan trọng sinh đúng một dòng nhật ký và không đọc được chéo tenant.

### M6. Đăng ký không xác minh email

- **Vị trí:** [AuthService.cs:55](../../src/backend/src/OISM.Application/Features/Auth/Services/AuthService.cs#L55).
- **Hậu quả:** chiếm trước email của người khác và dò được email đã đăng ký qua mã `EMAIL_ALREADY_EXISTS`.
- **Cách sửa:** kích hoạt cửa hàng sau khi xác minh email, phản hồi đăng ký không tiết lộ email đã tồn tại.
- **Kiểm chứng:** test tài khoản chưa xác minh không đăng nhập được.

### L1. Thiếu header bảo mật

- **Vị trí:** [Caddyfile](../../docker/caddy/Caddyfile), [nginx.conf](../../src/frontend/admin-dashboard/nginx.conf).
- **Hậu quả:** thiếu lớp phòng thủ khi có XSS hoặc clickjacking. Đợt rà soát chưa thấy điểm chèn HTML nguy hiểm trong frontend.
- **Cách sửa:** thêm CSP, HSTS, `X-Content-Type-Options`, `Referrer-Policy` và `frame-ancestors` tại Caddy. Thử CSP ở chế độ report-only trước.

### L2. Cấu hình container và cổng chưa chặt

- **Vị trí:** [Dockerfile](../../src/backend/Dockerfile), [docker-compose.yml](../../docker-compose.yml).
- **Hậu quả:** backend chạy bằng root. Compose gốc mở cổng PostgreSQL, Redis và pgAdmin, Redis không có mật khẩu. File prod đã đóng các cổng này.
- **Cách sửa:** chạy backend bằng user không phải root, bind cổng dev vào `127.0.0.1`, đặt mật khẩu Redis và ghim phiên bản image.

### L3. Spam mail đặt lại mật khẩu, Cashier đọc nhà cung cấp

- **Vị trí:** [AuthController.cs:130](../../src/backend/src/OISM.WebApi/Controllers/AuthController.cs#L130), [SuppliersController.cs:23](../../src/backend/src/OISM.WebApi/Controllers/SuppliersController.cs#L23).
- **Hậu quả:** quên mật khẩu chỉ giới hạn theo IP nên có thể spam một người từ nhiều IP. Cashier đọc được danh sách nhà cung cấp.
- **Cách sửa:** thêm giới hạn theo tài khoản và giới hạn `GET /api/suppliers` cho Owner, Staff.

## 4. Kế hoạch fix

| Đợt | Mục tiêu | Lỗi | Thay đổi schema | Kiểm chứng tối thiểu |
| --- | --- | --- | --- | --- |
| 0 | Chặn lỗi dễ khai thác, sửa nhỏ | S1, H1, H2, H3 | Không | Unit test, integration test RBAC, test khởi động |
| 1 | Chặn lạm dụng và lỗi toàn vẹn tồn kho | H4, H5, M2 | Không | Integration test PostgreSQL |
| 2 | Thu hồi phiên đăng nhập ngay lập tức | H6, M1 | Có | Migration và integration test PostgreSQL |
| 3 | Kiểm soát nghiệp vụ và siết cấu hình | M3, M4, M5, M6, L1, L2, L3 | Có (M5, có thể cả M4, M6) | Test theo từng mục |

Quy tắc khi thực hiện:

1. Viết test tái hiện lỗi trước, sửa sau.
2. Mỗi đợt một PR vào `develop`, cập nhật cột trạng thái trong báo cáo này.
3. Đổi quyền hoặc API thì cập nhật `docs/01-users-and-rbac/02-rbac-matrix.md` và `docs/05-api-contracts/`.
4. Sau Đợt 0 phải đổi `JWT_SECRET` trên EC2. Mọi phiên đăng nhập hiện tại sẽ bị đăng xuất.

## 5. Quyết định cần người dùng chốt

| # | Câu hỏi | Liên quan | Đề xuất |
| --- | --- | --- | --- |
| 1 | Cashier có còn được gọi `POST /api/inventory/reserve` không? Tài liệu API đang cho phép. | H1 | Bỏ quyền và cập nhật tài liệu |
| 2 | Bản demo có cần gọi webhook bằng JWT không? | H3 | Chỉ dùng `X-Webhook-Secret` |
| 3 | Hoàn tiền từ mức nào phải có Owner duyệt? | M4 | Chốt một ngưỡng theo cửa hàng |
| 4 | Cashier chưa được gán chi nhánh thì được vào đâu? Tài liệu đang cho vào mọi chi nhánh. | M2 | Không vào chi nhánh nào cho đến khi được gán |

## 6. Các phần đã kiểm tra, chưa thấy lỗi

- Mọi `DbSet` nghiệp vụ đều có query filter tenant, `SaveChanges` có chốt chặn ghi chéo tenant.
- SQL thô đều truyền tham số.
- Giá POS và đơn Admin được đối chiếu với bảng giá ở server.
- Nhập kho bắt buộc theo phiếu mua đã duyệt, không nhập vượt số lượng.
- Đổi giá, duyệt phiếu mua, duyệt chênh lệch hóa đơn và chốt kiểm kê đều do Owner thực hiện.
- Token đặt lại mật khẩu có chữ ký HMAC, hết hạn và mất hiệu lực sau khi đổi mật khẩu.
- Webhook secret sinh ngẫu nhiên 256 bit, CSDL chỉ lưu bản băm.
- Tool doanh thu của Trợ lý OISMS bị chặn với Staff ở phía server.
- Git không chứa API key theo các mẫu Groq, Google, AWS và private key đã quét.
