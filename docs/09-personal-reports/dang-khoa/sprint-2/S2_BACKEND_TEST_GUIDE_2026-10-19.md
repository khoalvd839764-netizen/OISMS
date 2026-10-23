# Hướng dẫn kiểm thử backend S2

Phạm vi: Backend 1 và Backend 2 theo phân công S2. Làm theo thứ tự: test tự động → Swagger → kiểm thử lỗi/quyền/tenant → ghi nhận kết quả. Không cần frontend để kiểm thử các API này.

## 1. Chạy toàn bộ test tự động

Mở Docker Desktop, mở PowerShell tại thư mục dự án. Cần .NET 8 SDK.

```powershell
Set-Location 'D:\CNPM workbase'
dotnet --list-sdks
docker version
```

Đoạn sau tạo PostgreSQL riêng ở cổng 55439, chạy test rồi dọn đúng container vừa tạo. Bộ test tự tạo database và runtime role riêng. Nếu cổng đang bị chiếm, dừng để kiểm tra, không xóa container khác.

```powershell
$s2TestContainer = 'oisms-s2-tests-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$s2TestId = docker run --detach --rm --name $s2TestContainer --publish 127.0.0.1:55439:5432 --env POSTGRES_PASSWORD=S2TestOnly_2026 --env POSTGRES_DB=postgres postgres:16-alpine
if ($LASTEXITCODE -ne 0) { throw 'Không tạo được PostgreSQL kiểm thử.' }

try {
    $s2Ready = $false
    for ($s2Attempt = 0; $s2Attempt -lt 30; $s2Attempt++) {
        docker exec $s2TestId pg_isready -U postgres -d postgres *> $null
        if ($LASTEXITCODE -eq 0) { $s2Ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $s2Ready) { throw 'PostgreSQL kiểm thử chưa sẵn sàng.' }

    $env:OISM_TEST_HOST = '127.0.0.1'
    $env:OISM_TEST_PORT = '55439'
    $env:OISM_TEST_ADMIN_USER = 'postgres'
    $env:OISM_TEST_ADMIN_PASSWORD = 'S2TestOnly_2026'
    $env:OISM_TEST_APP_USER = 'oisms_s2_test_app'
    $env:OISM_TEST_APP_PASSWORD = 'S2AppOnly_2026'
    Remove-Item Env:OISM_TEST_DATABASE,Env:OISM_TEST_ADMIN_CONNECTION,Env:OISM_TEST_APP_CONNECTION -ErrorAction SilentlyContinue

    dotnet test src/backend/OISM.sln -c Release --collect:'XPlat Code Coverage' --logger trx --results-directory TestResults/S2
    if ($LASTEXITCODE -ne 0) { throw 'Build hoặc test thất bại. Xem output và TestResults/S2.' }
}
finally {
    docker stop $s2TestId
}
```

Mật khẩu trong đoạn này chỉ dành cho container test tạm. Đóng cửa sổ PowerShell sau khi chạy nếu không dùng tiếp cấu hình test. Không cần chạy backend Docker để chạy bộ test này; fixture tự khởi tạo ứng dụng trong quá trình test.

**Đọc kết quả:** bản đã review ngày 18/10 có 99 unit + 89 integration = 188 test, tất cả đạt. Sau khi thêm test, tổng số có thể tăng. Cần `Failed: 0`, kiểm tra cả `Skipped`; file `.trx` và `coverage.cobertura.xml` nằm trong `TestResults/S2`. Coverage Inventory trong lần review trước là 94,83%, không phải coverage toàn bộ backend.

Các ca khó như 50 request tranh 1 đơn vị tồn, rollback giữa transaction, ledger không thể sửa/xóa và refresh đồng thời nên kiểm tra bằng automated tests, không chỉ bấm Swagger nhiều lần.

## 2. Khởi động bản backend để test trên Swagger

Trong PowerShell tại gốc dự án, sử dụng cấu hình `.env` đang dùng của nhóm:

```powershell
docker compose up -d --build backend
docker compose ps
```

`--build` giúp chạy code hiện tại thay vì image cũ. Lệnh cũng khởi động các service phụ thuộc. Nếu dự án chưa có `.env`, tạo từ `.env.example` và điền cấu hình trước; không ghi đè `.env` đang dùng.

- Swagger mặc định: http://localhost:5000/swagger
- Liveness: http://localhost:5000/health/live
- Readiness: http://localhost:5000/health/ready

Nếu `.env` đổi `BACKEND_PORT`, dùng cổng tương ứng. Health live/ready đều cần trả 200; riêng live 200 chưa chứng minh database hoạt động. Khi ready trả 503:

```powershell
docker compose logs --tail 100 backend postgres
```

Swagger thực hiện thao tác thật trên database của backend đang chạy. Tạo tenant QA riêng như bước dưới để dữ liệu kiểm thử dễ nhận diện. Không dùng `docker compose down -v` để làm sạch test vì sẽ xóa volume dự án.

## 3. Đăng ký và đăng nhập Owner để lấy token

Trong mỗi endpoint Swagger: chọn **Try it out → nhập JSON → Execute**. Đọc HTTP status và Response body.

`POST /api/auth/register-tenant`:

```json
{
  "tenantName": "Cua hang QA S2 A",
  "tenantCode": "QA-S2-A-001",
  "fullName": "Dang Khoa QA",
  "email": "qa.s2.a001@example.com",
  "password": "QaS2Test2026!"
}
```

Mong đợi 201. Mỗi lượt test mới đổi tenantCode/email sang mã chưa dùng. Registration tạo tenant và Owner; lấy token qua login tiếp theo.

`POST /api/auth/login`:

```json
{
  "email": "qa.s2.a001@example.com",
  "password": "QaS2Test2026!"
}
```

Mong đợi 200. Lưu `accessToken` và `refreshToken` riêng. Bấm **Authorize**, nhập đúng cấu hình Swagger hiện tại:

```text
Bearer <accessToken lấy từ login>
```

Gọi `GET /api/auth/me`: 200, đúng email/tenant, role Owner. Không dùng refreshToken ở ô Authorize.

## 4. Chuẩn bị dữ liệu để nhập kho

Dùng token Owner của tenant A. Tạo lần lượt và lưu ID thật từ response; không sao chép GUID minh họa trong tài liệu khác.

| Endpoint | JSON mẫu | Lưu kết quả |
|---|---|---|
| POST `/api/branches` | `{"name":"Chi nhanh QA S2"}` | `id` → BRANCH_ID |
| POST `/api/warehouses` | `{"branchId":"BRANCH_ID","name":"Kho QA S2"}` | `id` → WAREHOUSE_ID |
| POST `/api/suppliers` | `{"name":"Nha cung cap QA S2"}` | `id` → SUPPLIER_ID |
| POST `/api/categories` | `{"name":"Ao QA S2"}` | `id` → CATEGORY_ID |
| POST `/api/brands` | `{"name":"Brand QA S2"}` | `id` → BRAND_ID |

Thay các chữ `BRANCH_ID`... bằng GUID thật trước khi gửi. Mong đợi 201 và GET danh sách thấy dữ liệu đúng tenant.

`POST /api/products`:

```json
{
  "name": "Ao test S2",
  "categoryId": "CATEGORY_ID",
  "brandId": "BRAND_ID",
  "variants": [
    {
      "sku": "QA-S2-RED-M",
      "variantName": "Do M",
      "color": "Red",
      "size": "M",
      "retailPrice": 180000,
      "wholesalePrice": 160000,
      "attributes": {"material":"cotton"}
    },
    {
      "sku": "QA-S2-BLUE-L",
      "variantName": "Xanh L",
      "color": "Blue",
      "size": "L",
      "retailPrice": 190000,
      "wholesalePrice": 170000
    }
  ]
}
```

Mong đợi 201, lưu `productId`. Gọi `GET /api/products/{productId}`, lưu `variants[].id` cho hai SKU và barcode tự sinh. `SKU_ID` ở bước sau là GUID của biến thể, không phải chuỗi mã `QA-S2-RED-M`.

## 5. Kiểm thử luồng nhập kho, ledger và WAC

Dùng hai SKU mới để tồn ban đầu bằng 0.

`POST /api/goods-receipts`:

```json
{
  "branchId": "BRANCH_ID",
  "warehouseId": "WAREHOUSE_ID",
  "supplierId": "SUPPLIER_ID",
  "number": "QA-S2-GR-001",
  "items": [
    {"skuId":"RED_SKU_ID","quantity":10,"unitPrice":100000},
    {"skuId":"BLUE_SKU_ID","quantity":5,"unitPrice":80000}
  ]
}
```

Mong đợi 201, trạng thái Draft; lưu `id` của phiếu. Gọi GET chi tiết phiếu, đối chiếu đủ hai dòng. Trước confirm, tồn chưa tăng (có thể chưa có balance, API chi tiết trả 404 hoặc danh sách rỗng).

Gọi `POST /api/goods-receipts/{id}/confirm` không cần JSON body. Mong đợi 200 và Confirmed.

Kiểm tra:

- `GET /api/inventory/balances?branchId=BRANCH_ID`: SKU đỏ onHand 10, reserved 0, available 10, averageCost 100000; SKU xanh onHand 5, averageCost 80000.
- Thêm `warehouseId=WAREHOUSE_ID`: số lượng tương ứng ở kho đúng.
- `GET /api/inventory/ledger?branchId=BRANCH_ID`: hai dòng nhập, đúng SKU, số lượng, tham chiếu phiếu và balanceAfter.
- Confirm lại cùng phiếu: 409; tồn và số dòng ledger không tăng thêm.

Tạo phiếu thứ hai, số `QA-S2-GR-002`, cùng kho và chỉ SKU đỏ: quantity 20, unitPrice 130000. Confirm phiếu thứ hai.

**Kết quả bắt buộc của SKU đỏ:** onHand 30, averageCost 120000 vì `(10 × 100000 + 20 × 130000) / 30 = 120000`. Giá bán retail/wholesale của SKU không đổi theo WAC.

## 6. Kiểm thử POS và giữ chỗ

- `GET /api/pos/search?query=QA-S2-RED-M&branchId=BRANCH_ID`: tìm được SKU đỏ, retailPrice 180000, available 30.
- `GET /api/pos/barcode/{barcode}?branchId=BRANCH_ID`: dùng barcode vừa đọc ở product; đúng SKU và số dư.

`POST /api/inventory/reserve`:

```json
{
  "branchId": "BRANCH_ID",
  "warehouseId": "WAREHOUSE_ID",
  "skuId": "RED_SKU_ID",
  "quantity": 2,
  "durationMinutes": 15
}
```

Mong đợi 200; lưu reservationId/orderId/orderItemId. Gọi lại balances và POS: onHand 30, reserved 2, available 28. Ledger nhập không được phát sinh thêm dòng chỉ vì giữ chỗ.

Gửi lại đúng request, thêm orderId/orderItemId của response cũ: mong đợi trả cùng reservation và reserved vẫn 2. Nếu bỏ hai ID, API coi là yêu cầu giữ chỗ độc lập, không phải retry cùng đơn.

Giữ chỗ lớn hơn số khả dụng: 409, số dư không đổi. Ca gửi lại sau khi giữ hết tồn là lỗi đã biết H02, cần test lại khi sửa.

## 7. Kiểm tra phần quản lý và các nhánh lỗi

Thực hiện trên dữ liệu QA; thao tác vô hiệu hóa nên để sau luồng nhập/POS ở trên.

| Nhóm | Tình huống cần thử | Mong đợi |
|---|---|---|
| Category | Tạo con/cháu, đổi cha hợp lệ, gán cha là chính nó hoặc hậu duệ | Cây đúng; vòng lặp bị từ chối 400 |
| Brand/Category | Sửa tên, đọc lại, vô hiệu hóa bằng endpoint tương ứng trên Swagger | Trạng thái/giá trị lưu đúng |
| Product/SKU | Sửa sản phẩm, thêm/sửa variant, sửa giá, bật/tắt variant | GET trả đúng; không làm đổi WAC |
| SKU unique | Tạo lại mã SKU đã tồn tại trong tenant | 409 |
| Thuộc tính | Hai SKU trong cùng product có cùng tổ hợp thuộc tính | 409 |
| JSON | Object/array/number/boolean và null lồng nhau | Đọc lại giữ đúng kiểu và dữ liệu |
| Barcode | Barcode đã tồn tại trong tenant | 409 |
| Branch/warehouse | Sửa tên, bật/tắt, đọc lại | Lưu đúng; Owner thao tác được |
| Warehouse | Đổi chi nhánh kho đã có chứng từ/tồn | 409 |
| Phiếu nhập | SKU/kho/supplier không tồn tại hoặc kho sai chi nhánh | Bị từ chối 4xx, không tạo phiếu dở |
| Số lượng/giá | Quantity 0/âm, giá âm | 400, không thay đổi database |
| API bảo vệ | Xóa Authorization rồi gọi products/inventory | 401 |
| Quyền Cashier | Dùng tài khoản Cashier test gọi tạo/confirm phiếu nhập hoặc ledger | 403; POS vẫn truy cập được |

Swagger đăng ký tenant chỉ tạo Owner. Không có tài khoản Cashier QA thì dùng regression Auth/RBAC đã có hoặc tài khoản Cashier seed được nhóm cung cấp; không đổi role trên dữ liệu làm việc chỉ để thử.

**Tenant isolation:** đăng ký tenant B với email/code khác, login B, thay token trong Authorize. Dùng ID product/phiếu/kho/SKU của A để đọc/sửa/tạo tham chiếu: không được xem hoặc sửa dữ liệu A; chi tiết thường 404, danh sách rỗng, tham chiếu sai bị từ chối 4xx. Không được có response thành công kèm dữ liệu A. Kiểm tra cả đọc lẫn ghi.

**Refresh/logout:** làm sau các thao tác nghiệp vụ để tránh lẫn token.

1. Gọi `POST /api/auth/refresh-token` với `{"refreshToken":"TOKEN_CU"}` → 200, lưu cặp token mới.
2. Dùng lại refresh token cũ → 401.
3. Dùng access token mới gọi me → 200.
4. Gọi `POST /api/auth/logout` với refresh token mới → 200.
5. Dùng refresh token vừa logout để refresh → 401. Access token đã cấp có thể vẫn dùng đến khi hết hạn; không tự xem đây là lỗi logout vì endpoint hiện thu hồi refresh token.

## 8. Các ca lỗi đã biết và cách ghi kết quả

Đọc [review bàn giao ngày 18/10](S2_BACKEND_REVIEW_2026-10-18.md) để tái hiện H01–H05. Đây là danh sách lỗi còn mở của bản đã review, không đánh PASS chỉ vì đã biết lỗi.

Mỗi testcase ghi:

| Test ID | Revision | Role/Tenant | Endpoint & request | Kỳ vọng | Thực tế/status | Kết quả | Bug ID |
|---|---|---|---|---|---|---|---|
| INV-01 | Commit + mô tả working tree | Owner/A | Confirm GR-001 | 200, stock 10/5 | Điền sau chạy | Pass/Fail/Blocked | Nếu có |

Kèm response JSON, ảnh Swagger và log cần thiết; che access/refresh token khi chia sẻ. Không chỉ nhìn status 200: đọc lại dữ liệu sau thao tác, so sánh số dư và ledger.

Backend được coi là sẵn sàng nghiệm thu khi: automated tests đạt, các luồng và quyền/tenant ở trên đạt, lỗi đã biết được sửa hoặc có quyết định chấp nhận rõ ràng của nhóm, CI đạt trên revision bàn giao và frontend/tester đã nghiệm thu tích hợp. Các bài test hiện có không chứng minh mọi tình huống của hệ thống đều hết lỗi.
