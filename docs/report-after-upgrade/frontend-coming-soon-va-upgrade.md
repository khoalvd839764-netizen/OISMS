# Báo cáo nghiệm thu: frontend Coming Soon và Upgrade

| Mục | Nội dung |
| --- | --- |
| Task | `OISMS-46` |
| Ngày kiểm tra | 04/11/2026 |
| Baseline | `develop` tại `344dc89` (đã gộp PR #32, #33), cộng bản sửa trên nhánh `feature/OISMS-46-fix-loi-ton-dong-nghiem-thu-va-deploy` |
| Phạm vi | Admin, POS trong Admin, POS PWA; sửa nhỏ backend ở gợi ý nhập, cảnh báo tồn và `/health/ready` |
| Trạng thái | Các luồng trong bảng dưới đã chạy trên backend và PostgreSQL local. **Chưa phải nghiệm thu E2E toàn sản phẩm;** còn các mục ở phần Chưa làm và Chưa chạy. |

## Đã có trong sản phẩm

| Mục | Chức năng | Ghi chú |
| --- | --- | --- |
| CS-02 | Danh sách, tạo, sửa, khóa nhân viên | Chưa có phân công chi nhánh vì backend chưa có trường này |
| CS-03 | Ngưỡng cảnh báo lưu trên server | Kho, Sản phẩm, báo cáo cảnh báo và Tổng quan cùng số |
| CS-04 | Tìm kiếm toàn hệ thống | Đơn mở đúng chi tiết, sản phẩm lọc đúng; phiếu nhập mới về trang Kho |
| CS-07 | KPI, biểu đồ 7 ngày, đơn gần đây | Số liệu là lũy kế; API chưa lọc theo thời gian |
| CS-08 | Gợi ý nhập hàng | Dựa trên quy tắc; chưa có bằng chứng đánh giá độ chính xác dự báo. **Tab đang tạm ẩn**, xem phần Cập nhật sau nghiệm thu |
| U05 | Đơn vị gốc, quy đổi, chọn đơn vị trên chứng từ và hai POS | Tồn và WAC vẫn theo đơn vị gốc |
| U04 | Phiếu đổi giá, duyệt, lịch và lịch sử giá | Giá lấy từ server theo đơn vị và bảng giá |
| U01-A | Đơn mua, lập phiếu nhập từ đơn mua đã duyệt | Khóa giá, chặn nhập vượt |
| U02 | Hóa đơn, in lại, truy vết đến dòng sổ `OUT` | Biên nhận POS hiện số `INV-` |
| U03 | Nộp, duyệt, đếm lại, lịch và kỳ kiểm kê | Hangfire tắt thì kỳ không tự sinh |

## Kết quả nghiệm thu

Chạy trên một tenant test riêng, gồm hai chi nhánh, phiếu nhập và đơn thật.

| Kịch bản | Kết quả |
| --- | --- |
| Staff tạo phiếu đổi giá, Owner duyệt trên giao diện | PASS: phiếu `Pending` rồi `Approved`, lịch sử hiện đúng tên đơn vị |
| Đơn cũ giữ giá đã chụp sau khi đổi giá | PASS: đơn cũ giữ 230.000 ₫, đơn mới 240.000 ₫ |
| Bảng đơn vị và bảng giá trong phiếu đổi giá | PASS: hiện giá lẻ và sỉ theo từng đơn vị; đơn vị chưa có giá hiện rõ và có nút dùng giá gợi ý |
| Đơn Admin theo đơn vị quy đổi | PASS: 1 bao x5 giá 230.000 ₫, giữ chỗ 5 đơn vị gốc |
| Đơn vị chưa có giá | PASS: giao diện chặn; lỗi 422 hiện đúng, form không khóa |
| POS Admin và POS PWA: quét mã quy cách | PASS: chọn đúng đơn vị, tách dòng theo đơn vị |
| Biên nhận POS | PASS: có tên đơn vị và số `INV-` |
| `PRICE_MISMATCH` tại POS PWA | PASS: hiện giá mới, xác nhận rồi thanh toán 240.000 ₫ |
| Vượt tồn theo hệ số | PASS: POS PWA chặn thêm bao khi không đủ tồn gốc |
| Bán số lẻ tại POS Admin | PASS: bán 0,5, tổng 10.000 ₫, tồn giảm 0,5 |
| Kiểm kê: ô để trống | PASS: giữ trạng thái chưa đếm, không thành 0 |
| Kiểm kê: kho biến động giữa chừng | PASS: hiện cảnh báo lệch, đếm lại đúng SKU lệch |
| Kiểm kê có chênh lệch | PASS: chưa duyệt tồn giữ 3; duyệt xong còn 2; Staff bị 403; duyệt lần hai trả 409 |
| Lịch kiểm kê | PASS: chọn người phụ trách thật, tạo được lịch |
| Mở phiếu nhập từ đơn mua | PASS: tồn tăng đúng, đơn mua chuyển `Received` |
| Tạo đơn mua trên giao diện | PASS: thêm nhà cung cấp ngay trên form, chọn được SKU và đơn vị mua |
| SKU mới tạo ở trang Kho | PASS: hiện với nhãn "Chưa nhập kho"; có tồn sau khi nhập |
| Hóa đơn: bộ lọc | PASS: gõ 3 ký tự gửi 1 request; ngày 04/11 thành `03/11 17:00Z` đến `04/11 17:00Z` |
| Lỗi API ở kỳ kiểm kê | PASS: hiện lỗi, không hiện danh sách rỗng |
| Realtime tồn kho | PASS: trang Kho tự đổi 10 → 9 sau khi bán từ nơi khác |
| Gợi ý nhập hàng | PASS: lý do ghi số gọn; SKU ngừng kinh doanh bị loại (unit test) |

## Cập nhật sau nghiệm thu

Các thay đổi sau bảng kết quả trên, cùng nhánh `feature/OISMS-46-fix-loi-ton-dong-nghiem-thu-va-deploy`.

| Thay đổi | Kết quả |
| --- | --- |
| Báo cáo vận tốc kho tách tồn thực tế, giữ chỗ, khả dụng theo đơn vị gốc | PASS trên PostgreSQL local: tổng 74 tách thành 48 Cái và 26 hộp; thẻ báo cáo hiện đúng |
| Cột Tồn hiện tại cộng theo SKU trên mọi chi nhánh, mỗi SKU một dòng | PASS trên PostgreSQL local: SKU có 18 và 3 ở hai chi nhánh hiện 21 |
| Form đổi giá nhập nhiều đơn vị và bảng giá một lần | PASS trên giao diện: Owner lưu 2 giá, `POST /api/price-change-requests` trả 201, giá hiệu lực đọc lại đúng 160.000 ₫ |
| Tab Gợi ý nhập hàng tạm ẩn bằng comment trong `InventoryPage.tsx` | PASS: tab không còn trên giao diện. API và phần xử lý giữ nguyên |
| Bỏ tài khoản mẫu ở đăng nhập Admin và POS PWA | PASS: không còn email và mật khẩu mẫu trong màn hình và source |
| Nút Quên mật khẩu ở Admin và POS PWA | PASS: nút bị tắt, có nhãn Coming soon; chưa có luồng đặt lại mật khẩu |
| Gỡ màn Handoff kỹ thuật và mục menu | Build PASS; đặc tả vẫn giữ tại [07-handoff-screens.md](../11-ui-screens/07-handoff-screens.md) |

**PASS**

- `dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj`: 212/212
- `npm run build --prefix src/frontend/admin-dashboard`
- `npm run build --prefix src/frontend/pos-app`
- `npm test --prefix src/frontend/pos-app`: 63/63
- `docker compose up -d --build backend admin-dashboard pos-app`; `/health/ready` trả 200

**NOT RUN**

- Integration test trên bản cập nhật này; kết quả 364/365 bên dưới thuộc lần chạy trước. Thay đổi backend chỉ đọc dữ liệu báo cáo.
- Nút xem đủ đơn vị khi có trên 4 đơn vị, vì tenant test chỉ có 2 đơn vị.
- Số giữ chỗ khác 0 trên PostgreSQL thật; phần này chỉ có unit test.

## Lệnh kiểm chứng

**PASS**

- `npm run build --prefix src/frontend/admin-dashboard`
- `npm run build --prefix src/frontend/pos-app`
- `npm test --prefix src/frontend/pos-app`: 63/63, gồm `test-cart-units.mjs` chạy helper giỏ hàng của cả hai POS
- `dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj`: 210/210
- `dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj` trên PostgreSQL 16 riêng: 364/365
- `docker compose up -d --build backend admin-dashboard pos-app`; `/health/ready` trả 200, đủ 43 bảng

**FAIL: 1 test, do môi trường Windows**

`SalesInvoiceTraceIntegrationTests.AC01_UpgradeFromPreU02Schema_TwiceIdempotent_MatchesFreshSchemaExactly` so nguyên văn thân hàm SQL. Trên Windows, `core.autocrlf=true` đổi các file sang CRLF nên hai bản khác nhau. Chuyển toàn bộ file backend của bản `develop` sang LF thì test đạt 1/1. CI trên Linux của `344dc89` cũng đạt. Bản sửa này không đổi schema, migration hay test đó, nên chưa sửa test.

**NOT RUN**

- `PRICE_MISMATCH` trên POS Admin (đã chạy trên POS PWA, helper dùng chung logic có test)
- Bấm thử bằng tài khoản Staff ở màn lịch kiểm kê
- In ra giấy hoặc kiểm tra từng trang PDF
- Thông báo cảnh báo tồn thấp qua realtime (chỉ kiểm tra sự kiện đổi tồn)
- Đổi tài khoản giữa hai phiên, mất mạng ở bước checkout tại môi trường thật
- Stress test đồng thời, Hangfire bật, môi trường deploy

## Chưa làm

- **CS-05:** POS PWA và POS Admin đều có VietQR, thu ngân chọn tài khoản nhận tiền (mặc định chọn sẵn) và bấm xác nhận thủ công. Tài khoản nhận tiền lấy từ `GET /api/payment-accounts/active`; Owner quản lý ở màn "Tài khoản nhận tiền". Chưa có xác nhận tự động qua webhook ngân hàng. Chi tiết: [review PR 39](./vietqr-cs05-review-pr39.md).
- **CS-06:** in chứng từ giao hàng và in báo cáo.
- **CS-01:** nhận hàng trả, cần backend trước.
- **U01-B:** hóa đơn nhà cung cấp và chênh lệch, cần backend và GVHD chốt cách xử lý.
- **CS-02:** phân công chi nhánh, cần quyết định làm thêm backend hay bỏ khỏi yêu cầu.
- **Tìm kiếm:** kết quả phiếu nhập chưa mở đúng phiếu.
- **Quên mật khẩu:** mới có nút và nhãn Coming soon, chưa có API và luồng đặt lại mật khẩu.

## Ghi chú đối chiếu

- **Giá vốn ở truy vết:** Staff được xem theo [ma trận phân quyền](../01-users-and-rbac/02-rbac-matrix.md) và [quyết định U02](../Upgrade-recommended/02-truy-vet-chung-tu-xuat-kho.md). Mô tả "chỉ Owner" trong PR #33 không đúng; code giữ nguyên.
- **Doanh thu ở Tổng quan:** ma trận chỉ cấm Staff xem lãi gộp, nên Staff vẫn thấy doanh thu.
- **Số lẻ:** POS dùng `decimalPlaces` server trả về. Đơn vị mặc định đang cho phép 3 chữ số thập phân, nên POS nhận cả số lẻ với "Cái". Cần chốt nếu muốn cấm.
- **Gợi ý nhập hàng:** `FR-AI-01` vẫn là yêu cầu trong docs, chỉ giao diện đang tạm ẩn. Cần chốt thời điểm mở lại trước khi nghiệm thu.
- **Handoff:** [DOC-SCR-07](../11-ui-screens/07-handoff-screens.md) vẫn mô tả màn hình này. Cần chốt bỏ đặc tả hay làm lại.
- **Tổng số lượng trong API báo cáo:** các trường `total*Quantity` vẫn trả về để tương thích nhưng cộng gộp khác đơn vị; giao diện dùng `quantityByUnit`.
- **Tổng quan:** lấy `recentRevenueTrend` theo ngày có đơn; ngày không có đơn hiển thị 0 theo UTC+07:00.
