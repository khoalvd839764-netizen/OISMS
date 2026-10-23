# Kiểm tra tích hợp Admin Dashboard và backend S2

Ngày kiểm tra: 18/10/2026. Revision mã nguồn: `460092f`, backend commit trước đó `20aa08b`. Working tree sạch khi bắt đầu. Đánh giá tập trung phần đã triển khai trong Sprint 2.

## Kết luận

Frontend đã gọi được backend thật; luồng đăng nhập, tạo dữ liệu và nhập kho chính hoạt động. Sau lần đối chiếu code bổ sung, danh sách hiện có **11 nhóm cần xử lý: A01–A10 là lỗi tích hợp/luồng đang có; A11 là các giao diện demo cần ẩn, vô hiệu hóa hoặc ghi rõ trạng thái trong S2**. Không mặc định yêu cầu triển khai đầy đủ các nghiệp vụ sprint sau. A01–A04 đã tái hiện trên UI; A05–A10 có bằng chứng mã nguồn và/hoặc chẩn đoán code với API giả lập như phần bổ sung bên dưới. Lỗi dữ liệu đơn giữa tenant cần sửa trước khi cho nhiều cửa hàng dùng chung một trình duyệt.

Không sửa mã nghiệp vụ trong lần kiểm tra này. Có tạo dữ liệu QA và một tài khoản tenant QA để thử cô lập dữ liệu trên giao diện. Không chạy lại toàn bộ backend test suite trong lần kiểm tra frontend này.

## Môi trường và kết quả thực hiện

- Admin đang chạy tại `http://localhost:5173`, backend tại `http://localhost:5000`.
- `npm run build` trong `src/frontend/admin-dashboard`: TypeScript và Vite build thành công, 2002 modules transformed.
- Thực hiện bằng trình duyệt thật, qua các form đang có; không gán token, giả response hoặc sửa state để làm ca test thành công.
- Dùng tài khoản QA đã có `khoa.test002@example.com`, tenant `Cua hang test Khoa`. Tạo tenant thứ hai qua form đăng ký để kiểm tra đổi phiên.

| Ca thực hiện | Kết quả quan sát |
|---|---|
| Login Owner bằng tài khoản QA đã có | Thành công, đúng tên và tenant |
| Nhập mật khẩu sai | Form hiển thị “Email hoặc mật khẩu không chính xác.” |
| Reload trang sau login | Phiên được khôi phục, dữ liệu vẫn có |
| Tạo category | Tạo và tải lại danh sách được |
| Tạo hai branch, mỗi branch một warehouse | Tạo và hiển thị được |
| Tạo product và SKU mới | Lưu đúng mã, barcode và giá bán |
| Tạo lại mã SKU đang tồn tại | Form hiển thị thông báo trùng SKU, không báo thành công giả |
| Sửa giá lẻ/sỉ | Giá lẻ từ 180000 thành 185000 hiển thị sau lưu; payload giá sỉ 165000 được gửi cùng thao tác |
| Tạo nhanh supplier trong form nhập | Tạo và chọn được supplier mới |
| Branch A: nhập 10 × 100000 | Tồn 10, WAC 100000; ledger IN +10 |
| Branch A: nhập tiếp 20 × 130000 | Tồn 30, WAC 120000; ledger IN +20, balanceAfter 30 |
| Branch B: cùng SKU, nhập 10 × 200000 | Sau confirm, tồn 10, WAC 200000 riêng branch B; branch A vẫn 30/WAC 120000 |
| Tạo đơn giữ chỗ một SKU qua Admin | Tạo được reservation; trang sản phẩm sau đó có onHand 40, reserved 1, available 39 |
| Logout A rồi đăng ký/login tenant B | Auth thành công, nhưng Dashboard B hiển thị đơn cached của A: lỗi A01 |

## Các lỗi cần sửa

### A01 — P1 — Đổi tenant vẫn thấy đơn của tenant trước

**Đã tái hiện trên UI.**

1. Login tenant A, tạo reservation qua “Tạo đơn Admin”, khách hàng `QA-KH-1810-A`, đơn hiển thị `ORD-3FBB080B`.
2. Bấm Đăng xuất.
3. Đăng ký/login tenant mới `QA Integration Tenant B 1810` trong cùng tab.
4. Dashboard B có 0 SKU nhưng lại hiện một đơn của khách `QA-KH-1810-A`, giá 180000, cùng mã đơn của A.

Nguồn: `src/frontend/admin-dashboard/src/services/apiAdapter.ts:648–657`, `:723–725` đọc/ghi `sessionStorage['oism_session_orders']` dùng chung. `src/frontend/admin-dashboard/src/context/AuthContext.tsx:161–173` logout chỉ xóa hai token, chưa dọn dữ liệu đơn. Không có tenantId/userId để kiểm tra chủ sở hữu cache.

Đây là dữ liệu frontend còn sót trong trình duyệt. Lần kiểm tra này không chứng minh backend query filter trả dữ liệu chéo tenant.

Sửa: tách cache theo tenant/user hoặc bỏ cache đơn khi backend chưa cung cấp danh sách; dọn dữ liệu tương ứng khi logout/đổi tài khoản; không hiển thị dữ liệu không xác định tenant hiện tại.

Kiểm tra lại: A tạo đơn → logout → B login/đăng ký → Dashboard và Orders của B không có đơn, khách hàng hay doanh thu của A. Thử cả reload và đăng nhập lại A.

Bằng chứng: `tmp/admin-integration-review/tenant-b-shows-tenant-a-order.png`.

### A02 — P2 — Dashboard tính sai giá trị tồn khi một SKU có nhiều WAC chi nhánh

**Đã tái hiện trên UI.**

- Branch A: 30 × 120000 = 3600000.
- Branch B: 10 × 200000 = 2000000.
- Đúng: tổng giá trị 5600000.
- Dashboard hiển thị 4800000 = 40 × 120000.

Nguồn: `apiAdapter.ts:19–27` cộng OnHand/Reserved nhưng ghi đè averageCost bằng WAC của một dòng; `DashboardPage.tsx:42` nhân tổng tồn SKU với WAC này. Giá trị sai tùy thứ tự các dòng trả về.

Sửa: tính tổng giá trị từ từng cặp branch/SKU; nếu cần WAC tổng để hiển thị, suy ra từ tổng giá trị/tổng tồn. Không dùng WAC của một chi nhánh để định giá toàn tenant. Các nghiệp vụ theo chi nhánh phải tiếp tục dùng WAC riêng.

Kiểm tra lại: ví dụ hai chi nhánh trên cho 5600000 bất kể thứ tự trả về của API; thử chi nhánh tồn 0 và WAC 0.

Bằng chứng: `inventory-correct-branches.png` và `dashboard-wrong-inventory-value.png` trong `tmp/admin-integration-review/`.

### A03 — P2 — WAC dự kiến trong form nhập lấy nhầm chi nhánh

**Đã tái hiện trên UI.**

- Branch A đang có 30/WAC 120000, branch B chưa có tồn.
- Chọn branch B, kho B, nhập 10 × 200000.
- Form dự kiến WAC 140000; sau confirm, backend lưu đúng WAC branch B là 200000.

Nguồn: `InventoryPage.tsx:568–578` tìm số dư chỉ bằng SKU ID, không có điều kiện `selectedBranch`. `skus.find` có thể lấy số dư của chi nhánh khác.

Sửa: đọc số dư theo branch/SKU và tính preview lại khi đổi chi nhánh. Chi nhánh chưa có số dư phải được xem là tồn 0. Đồng bộ quy tắc làm tròn/độ chính xác nếu preview được dùng để đối chiếu kết quả.

Kiểm tra lại: lần nhập đầu branch B dự kiến 200000; đổi về branch A phải tính dựa trên tồn A. Database trong ca đã chạy vẫn đúng, lỗi ở dự báo frontend.

Bằng chứng: `tmp/admin-integration-review/wac-preview-branch-b.png`.

### A04 — P2 — Form tự chọn chi nhánh và kho không cùng nhau

**Đã tái hiện trên UI.**

Sau khi tạo branch B/kho B và mở lại trang nhập, form chọn branch B nhưng kho A. Bấm xác nhận với mặc định này bị backend từ chối “Kho ... không thuộc Chi nhánh QA-ADMIN-1810-B”. Dropdown kho còn cho chọn kho của cả hai branch.

Nguồn: `InventoryPage.tsx:77–83` chọn độc lập branch đầu tiên và warehouse đầu tiên; `:532–550` đổi branch chỉ thay kho khi tìm được, danh sách kho chưa lọc theo branch. Branch không có kho còn có thể giữ warehouse cũ.

Sửa: chọn kho theo branch hiện tại ngay khi tải dữ liệu; chỉ liệt kê kho phù hợp; nếu không có kho thì xóa lựa chọn và yêu cầu tạo/chọn kho trước khi submit.

Kiểm tra lại: đảo thứ tự branch/warehouse trong API, branch có nhiều kho, branch không có kho; form không gửi cặp ID không khớp.

### A05 — P2 — Frontend chỉ đọc trang đầu của balances/ledger

**Xác định từ code/hợp đồng API; chưa tạo hơn 20 dòng dữ liệu để chạy ca biên này trên UI.**

`apiAdapter.ts:10`, `:315`, `:367` gọi balances/ledger không có tham số phân trang, lấy `items` rồi bỏ `totalCount`/`totalPages`. Backend mặc định pageIndex 1, pageSize 20 và thực thi Skip/Take. Frontend cũng lọc chi nhánh/tìm kiếm trên dữ liệu đã tải thay vì truyền đủ bộ lọc đến API.

Ảnh hưởng: khi vượt 20 dòng số dư/bút toán, trang kho, ledger, số tồn ở sản phẩm và số tổng hợp có thể thiếu dữ liệu. Một SKU có thể hiện tồn 0 vì số dư nằm ở trang sau; lọc một chi nhánh có dữ liệu vẫn có thể trả rỗng trên UI.

Sửa: phân trang phía server trên màn hình danh sách, truyền filter tương ứng; nếu màn hình cần tổng đầy đủ thì dùng API tổng hợp hoặc lấy đủ trang có kiểm soát. Chỉ tăng pageSize một lần không giải quyết được bộ dữ liệu lớn hơn giới hạn.

Kiểm tra lại: ít nhất 21 rồi hơn 100 balance/ledger; so số dòng/tổng với API; tìm và lọc dữ liệu chỉ nằm ở trang sau.

## Các quan sát khác chưa dùng để kết luận toàn bộ S2

- `DashboardPage.tsx:9`, `:92–98`: biểu đồ vẫn dùng mảng cố định và nhãn ngày tháng 6, trong khi mô tả “Đồng bộ trực tiếp”. Nếu chỉ là khung chờ sprint khác, cần ghi trạng thái đó, tránh trình bày là thống kê thật.
- `httpClient.ts:56–75`: mỗi request 401 tự refresh riêng, chưa có cơ chế dùng chung một lượt refresh. Với rotation chỉ dùng token một lần ở backend, nhiều request hết hạn cùng lúc có thể gây logout. Cần regression riêng; chưa ép token hết hạn trong phiên UI này.
- Trang đơn có trạng thái lưu sessionStorage, chưa nên coi thao tác đổi trạng thái đó là đã cập nhật backend. Checkout đầy đủ không bị yêu cầu bổ sung vào S2 trong báo cáo này.

## Giới hạn kiểm tra và bàn giao

Chưa nghiệm thu đầy đủ role Staff/Cashier, expiry/refresh đồng thời, tất cả CRUD, kích thước mobile hoặc toàn bộ 32 ca trong file tester. Không gọi build thành công là toàn bộ UI pass.

Dữ liệu QA đã tạo, giữ lại để người dùng đối chiếu:

- Trong tenant `Cua hang test Khoa`: branch `QA-ADMIN-1810-A/B`, warehouse `QA-KHO-1810-A/B`, category `QA-DANHMUC-1810`, supplier `QA-NCC-1810`, product `QA-AO-1810`, SKU `QA-AO-1810-A`.
- Ba phiếu nhập đã confirm: A nhập 10 và 20, B nhập 10. Một reservation số lượng 1 đã tạo qua Admin.
- Tenant mới `QA Integration Tenant B 1810`, code `QA-ADMIN-B-1810`, email `qa.admin.b1810@example.com` để tái hiện A01. Không ghi mật khẩu trong báo cáo.
- Sau kiểm tra đã đăng nhập lại tài khoản QA A. Không xóa dữ liệu QA hoặc thay đổi database schema.

Ưu tiên bàn giao sửa: A01 trước; hoàn thiện A02–A10 và xử lý cách trình bày A11 trong phạm vi đã chốt; chạy lại các ca UI tương ứng trên revision mới và hoàn thiện kiểm thử RBAC/refresh.

## Đối chiếu code bổ sung cùng revision 460092f

Không có thay đổi mã nghiệp vụ sau lần test UI. Lần bổ sung này thực thi nguyên mã `apiAdapter.ts`, `httpClient.ts` và các handler trích bằng TypeScript AST trong môi trường Node với HTTP/storage giả lập. Đây là chẩn đoán logic frontend, **không phải** kết quả gọi backend thật hay kiểm thử UI mới. Script và kết quả: `tmp/admin-integration-review/source-audit.cjs`, `source-audit-results.json`.

Chẩn đoán cũng xác nhận A02 phụ thuộc thứ tự API: đảo thứ tự số dư cho giá trị tổng 8000000 thay vì 5600000 (UI trước đó cho 4800000). A05 dùng 21 số dư giả lập, SKU thứ 21 bị ánh xạ tồn 0 dù dữ liệu gốc có tồn 1; adapter chỉ gửi hai GET (products và balances trang đầu).

### A06 — P2 — Refresh token đồng thời có thể tự xóa phiên vừa làm mới

- `httpClient.ts:56–75`: mỗi response 401 tự đọc token cũ và tự POST refresh, không dùng chung một promise/hàng chờ.
- Backend `AuthController.cs:263–307` khóa và rotate token một lần, nên một refresh token không hợp lệ cho hai lần dùng đồng thời.
- Chẩn đoán tạo hai lỗi 401 cùng lúc: có hai request refresh; request đầu ghi token mới thành công, request sau bị từ chối rồi xóa cả token mới và chuyển `/session-expired`.
- Sửa: chỉ một lượt refresh cho các request cùng phiên, các request khác chờ và retry bằng token mới; không để response lỗi của token cũ xóa phiên mới hoặc phiên tài khoản đã đổi. Đặt timeout cho refresh.
- Kiểm tra lại: nhiều API 401 đồng thời, chỉ một refresh; request đều retry; lỗi refresh thực sự mới logout. Test thêm logout/đổi account trong lúc refresh.

### A07 — P2 — Thử lại confirm sau lỗi mạng tạo phiếu mới

- `InventoryPage.tsx:214–234`: mỗi lần bấm luôn gọi create rồi confirm. ID phiếu đã tạo không lưu lại để retry.
- Chẩn đoán: create r1, confirm r1 trả lỗi mạng; bấm lại → create r2 và confirm r2. Hai lần create, hai ID khác nhau.
- Nếu lần confirm đầu chưa commit, r1 sẽ bị bỏ lại dạng Draft. Nếu backend đã commit nhưng response bị mất, lần thử lại có thể nhập hàng lần hai qua r2; khóa chống confirm lặp trên cùng ID không bảo vệ hai phiếu khác ID.
- Sửa: lưu ID chứng từ đã tạo, kiểm tra lại trạng thái/retry trên đúng ID. Xử lý kết quả không xác định của create bằng request key hoặc cơ chế tra cứu đã chốt, không mù quáng tạo mới.
- Kiểm tra lại: mất response sau create, sau confirm, lỗi confirm có thể sửa rồi retry; chỉ một chứng từ nghiệp vụ và một lần cộng tồn.

### A08 — P2 — Giữ chỗ hiển thị tồn toàn tenant nhưng chọn một kho tự động

- `apiAdapter.getSkus:19–55` gộp tồn theo SKU và bỏ branchId/warehouseId.
- `apiAdapter.checkout:674–678` tự chọn branch đầu tiên/kho đầu tiên phù hợp; `OrdersPage` không cho chọn branch/kho trong form tạo giữ chỗ.
- Chẩn đoán: UI được cấp tổng available 40, yêu cầu 20; adapter chọn branch B/kho B chỉ có 10, thất bại dù branch A có 30.
- Sửa: đưa branch/kho vào ngữ cảnh giữ chỗ, hiển thị available đúng phạm vi và gửi đúng ID; không chọn địa điểm theo thứ tự danh sách. POS trong cùng app cũng dùng adapter này.
- Kiểm tra lại: nhiều branch và nhiều kho trong một branch, tồn lệch giữa các kho, đổi thứ tự danh sách; số khả dụng hiển thị khớp nguồn sẽ giữ chỗ.

### A09 — P2 — Lỗi tải API bị biến thành dữ liệu rỗng hoặc tồn 0

- `apiAdapter.ts:10`: lỗi balances bị đổi thành items rỗng; `getSkus` vẫn trả thành công với toàn bộ tồn bằng 0.
- Các getSuppliers/getBranches/getWarehouses/getGoodsReceipts cũng catch rồi trả data rỗng. `ProductsPage.loadData`/`InventoryPage.loadData` chưa hiển thị lỗi trả về đầy đủ.
- Chẩn đoán: products thành công, balances ném lỗi HTTP 500 → getSkus trả SKU onHand 0, không có error.
- Sửa: phân biệt “chưa có dữ liệu” với “tải thất bại”; hiển thị lỗi/retry, không ghi đè số dư thành 0 khi không xác minh được. Có thể giữ dữ liệu cũ với trạng thái chưa cập nhật rõ ràng.
- Kiểm tra lại: lỗi 500, timeout, mất mạng từng API; UI không kết luận hết hàng/không có chứng từ khi chưa tải được dữ liệu.

### A10 — P2 — Mất trạng thái ngừng hoạt động của Product/SKU

- Backend `ProductsController.GetProducts` trả cả product/variant với isActive; điều này hợp lệ cho màn hình quản lý.
- `apiAdapter.getSkus:32–55` không ánh xạ isActive và không lọc theo trạng thái. `Sku` trong domain.ts thiếu trường này; ProductsPage quyết định “Đang bán” chỉ dựa vào tồn, PosPage sử dụng cùng danh sách.
- Chẩn đoán: product và variant isActive=false, có tồn 10 → vẫn có một dòng SKU trong adapter, cờ isActive bị mất.
- `ReservationService.cs:66–103` mới kiểm tra warehouse/SKU tồn tại, chưa chặn giữ chỗ mới cho product/SKU ngừng hoạt động. Đây là điểm cần phối hợp backend, không chỉ ẩn nút frontend. Lần bổ sung này chưa gọi API thật với SKU ngừng hoạt động.
- Sửa: giữ các cờ hoạt động cho quản lý, lọc theo đúng hợp đồng bán/giữ chỗ, chặn thao tác mới không hợp lệ ở backend nhưng giữ lịch sử. Áp dụng chính sách đã chốt cho branch/warehouse ngừng hoạt động.
- Kiểm tra lại: vô hiệu product hoặc SKU có tồn → màn quản lý thể hiện đúng trạng thái; POS không đề nghị bán; gọi reserve trực tiếp không vượt được quy tắc; dữ liệu lịch sử vẫn đọc được.

### A11 — UI demo phải thể hiện đúng trạng thái, không yêu cầu mở rộng nghiệp vụ S2

Các điểm đã có trong giao diện nhưng chưa có luồng lưu backend tương ứng:

- `OrdersPage.tsx:74–81,269–276`: hủy giữ chỗ/xác nhận/hoàn tất chỉ đổi state và sessionStorage. Chẩn đoán gọi handler hủy: cache ghi Cancelled, không có API backend. UI không được làm người dùng tin Reserved đã được giải phóng.
- `PosPage.tsx:78–90`: tổng tiền tính toàn cart nhưng checkout chỉ gửi `cart[0]`; còn hiển thị “Thanh toán & giữ chỗ thành công” dù adapter mới reserve, chưa thu tiền. Nếu chưa chốt checkout S2 thì ẩn/vô hiệu thao tác hoặc giới hạn đúng một SKU và ghi rõ là giữ chỗ.
- `DashboardPage.tsx:31–38`, `ReportsPage.tsx:27–41`: đơn Reserved cũng được tính thành doanh thu/lợi nhuận; Reports còn dùng giá bán và WAC hiện tại cho dữ liệu quá khứ. Trong S2 nên thể hiện rõ số liệu dự kiến/giữ chỗ, hoặc ẩn phần báo cáo tài chính chưa có nguồn giao dịch bán hoàn tất.
- `DashboardPage.tsx:9,92–98`, `realtimeAdapter.ts:40,48–74`: biểu đồ/ngày cố định và trạng thái connected giả lập đang được trình bày như dữ liệu realtime.
- `OrganizationPage.tsx` nút lưu nhân viên chỉ đóng modal; `InventoryPage.tsx` nút tạo bút toán hoàn kho chỉ đóng modal, nút chuyển kho chưa có xử lý, ngưỡng cảnh báo chỉ lưu React state. Với chức năng chưa thuộc đầu ra S2, ghi “chưa hỗ trợ”/vô hiệu hóa thay vì ngụ ý đã lưu thành công.

Không tính mỗi nút demo là một lỗi nghiệp vụ backend còn thiếu. Đóng A11 bằng việc trình bày đúng phạm vi hoặc tích hợp nếu nhóm đã nhận chức năng đó trong sprint.

## Danh sách tổng hợp sau đối chiếu

| ID | Nhóm cần xử lý | Bằng chứng chính |
|---|---|---|
| A01 | Cache đơn sót giữa tenant | UI thật |
| A02 | Tổng giá trị tồn sai khi nhiều WAC | UI thật + code giả lập |
| A03 | Preview WAC sai chi nhánh | UI thật |
| A04 | Mặc định branch/warehouse không khớp | UI thật |
| A05 | Bỏ trang sau balances/ledger | Code + dữ liệu giả lập 21 dòng |
| A06 | Race refresh làm mất phiên | Code interceptor + HTTP giả lập |
| A07 | Retry confirm tạo chứng từ mới | Handler thật + API giả lập |
| A08 | Reserve chọn kho khác phạm vi available | Adapter thật + API giả lập |
| A09 | Nuốt lỗi tải thành rỗng/0 | Adapter thật + lỗi giả lập |
| A10 | Mất cờ ngừng hoạt động, thiếu kiểm tra khi reserve mới | Adapter thật + dữ liệu giả lập và đối chiếu backend |
| A11 | UI demo trình bày như đã ghép thật | Source; handler hủy chạy giả lập |

Đây là danh sách đã xác định trên bản code này, không phải cam kết không thể còn lỗi khác. Các lỗi backend thuộc snapshot review cũ không tự cộng dồn vào danh sách mới; muốn đóng lỗi phải kiểm tra đúng revision và đúng ca regression.
