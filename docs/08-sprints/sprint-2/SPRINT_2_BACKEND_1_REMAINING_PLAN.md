# Kế hoạch phần còn lại — Khoa / Backend 1

Ngày rà soát: **17/10/2026**. Mốc mã nguồn: `b21cf49`.

Phạm vi lấy từ `SPRINT_2_BACKEND_1.md`: hoàn thiện Sprint 2 cho Backend 1. Đây là kế hoạch dựa trên mã nguồn hiện tại, không phải xác nhận đã nghiệm thu. Chưa gán ngày hoàn thành vì lịch sprint cũ ghi 13–17/10 nhưng lại ghi D1–D6.

## 1. Kết quả kiểm tra

- CodeGraph báo index cập nhật: **59 files, 782 nodes, 1.244 edges, 30 routes**. Các số này mô tả phần được index, không phải tổng số file của repository.
- Đã có SQL 29 bảng, Docker init, runtime role, kiểm tra readiness và trigger bảo vệ ledger/tồn. Không cần thiết kế lại database từ đầu.
- Đã merge Auth/Catalog của TV2. Tuy nhiên model EF hiện chưa khớp SQL v0.5; không thể lấy việc merge hoặc readiness thành công làm bằng chứng API nghiệp vụ chạy đúng.
- Chưa thấy entity/service/controller cho phiếu nhập, hai cấp tồn, ledger, WAC và reservation trong cây mã nguồn backend hiện tại.
- Chưa có thư mục EF migrations hoặc seed dùng chung cho luồng nghiệp vụ thật trong danh sách file backend. Seed role hiện có không tương thích mô hình `users.role` của SQL.
- Working tree sạch trước khi thêm bản kế hoạch này.

### Các vấn đề cần xử lý trước nghiệp vụ kho

| Vấn đề | Bằng chứng hiện tại | Việc cần làm |
|---|---|---|
| EF chưa khớp schema | `AppDbContext.cs:24–34` có 11 DbSet; `OnModelCreating` chưa mapping tên bảng/cột snake_case. SQL dùng `skus`, không có bảng Role/UserRole. | Khoa chủ trì mapping theo SQL v0.5; phối hợp TV2 sửa Auth/Catalog. |
| Khác biệt mô hình dữ liệu | Code dùng `Tenant.Code`, Role/UserRole và `ProductVariant.CostPrice/Color/Size`; SQL dùng `users.role`, `skus.attributes`, WAC tại `inventory_balances.average_cost`. | Lập bảng đối chiếu property–column và sửa DTO/service liên quan; không chỉ đổi tên bảng. Rà thêm refresh token hash, trường bắt buộc, precision và unique/FK. |
| Kết nối API khác health check | `Program.cs:15–24` cấu hình EF bằng `DefaultConnection`; readiness dùng `BuildConnectionStringBuilder`, hỗ trợ `Database__*`. | Dùng chung nguồn cấu hình và runtime account; kiểm thử API thực tế cùng database với readiness. |
| Thiếu tenant vẫn đọc được | `AppDbContext.cs:49–71` dùng điều kiện `!TenantId.HasValue || ...`; `CurrentTenantService.cs:46–51` có fallback header. | Nghiệp vụ yêu cầu tenant từ JWT đã xác thực; thiếu tenant phải chặn. Login/register/refresh có đường truy cập riêng, giới hạn rõ. |
| Write guard chưa đầy đủ | `AppDbContext.cs:145–161` chỉ gán TenantId trống khi thêm và bỏ cập nhật cột TenantId khi sửa. | Kiểm tra tenant thực thể khi thêm/sửa/xóa và các FK; kiểm thử detached entity, tenant giả và tham chiếu chéo tenant. |
| Test chưa xác minh PostgreSQL | `CustomWebApplicationFactory.cs:30–34` dùng InMemory, bỏ qua cảnh báo transaction. | Thêm suite PostgreSQL thật cho mapping, constraint, rollback, lock và commit. |
| POS chưa có tồn | `PosSearchDtos.cs:3–14` chưa trả branch/onHand/reserved/available. | Khoa bàn giao query tồn; TV2 tích hợp vào tìm kiếm/quét mã POS. |

Giới hạn kiểm chứng hôm nay: `dotnet test --no-restore -c Release` thoát mã 1, không trả báo cáo test; `dotnet --info` báo bị từ chối truy cập Service Control Manager. Docker CLI không kết nối được pipe Docker Desktop Linux Engine; request localhost:5000 bị hạn chế socket. Vì vậy **chưa xác nhận lại build/test hoặc trạng thái runtime**. Các kết quả pass trong commit/tài liệu cũ là bằng chứng lịch sử.

## 2. Đối chiếu nhiệm vụ của Khoa

| Mã | Trạng thái theo mã nguồn | Phần còn lại |
|---|---|---|
| BE1-01 | Có nền SQL/Docker/readiness/CI; chưa hoàn tất tích hợp | Mapping EF, thống nhất kết nối, migration baseline/upgrade, seed và smoke test thật. |
| BE1-02 | Có tenant context/filter; cần siết lại | Chặn khi thiếu tenant, write guard, FK cùng tenant và test đọc/ghi. |
| BE1-03 | Có controller/DTO/validation từ TV2; cần thống nhất | Mẫu service, lỗi chuẩn, quyền, transaction/retry và hợp đồng kho. |
| BE1-04 | Chưa thấy triển khai backend | Tạo phiếu nhiều dòng, danh sách, chi tiết, xác nhận. |
| BE1-05 | SQL có guard hỗ trợ; thiếu posting service | Transaction nguyên tử, khóa trước đọc, idempotency và rollback. |
| BE1-06 | Có append-only ở SQL; thiếu nghiệp vụ/API | Ghi ledger đúng nguồn/sequence/BalanceAfter và API tra cứu. |
| BE1-07 | Có cột/ràng buộc SQL; thiếu phép tính backend | WAC theo chi nhánh/SKU, precision/rounding và unit test. |
| BE1-08 | Chưa thấy triển khai backend | API tồn hai cấp; nối tồn khả dụng cho Admin/POS. |
| BE1-09 | Có bảng/guard SQL; thiếu service | Giữ chỗ nguyên tử, giải phóng tối thiểu, retry và test cạnh tranh. |
| BE1-10 | Có test Auth/Catalog/health và script schema | Suite lõi kho/WAC/reservation trên PostgreSQL, coverage và CI. |

## 3. Thứ tự triển khai và tiêu chí hoàn thành

### Bước 1 — Đồng bộ nền tích hợp (ưu tiên P0)

Bao phủ BE1-01, phối hợp TV2.

1. Giữ SQL v0.5 làm nguồn cấu trúc hiện hành. Lập mapping explicit cho bảng/cột, kiểu dữ liệu, khóa ghép theo tenant và generated column.
2. Khoa chịu trách nhiệm DbContext/configuration/migration; TV2 chỉnh Auth/Catalog/DTO tương ứng: role trực tiếp trên user, token hash, SKU attributes, các trường không tồn tại trong SQL.
3. Dùng chung builder kết nối cho EF và readiness; bỏ fallback tài khoản quản trị ở đường chạy nghiệp vụ.
4. Chốt một đường migration: baseline phải mang đủ function/trigger/index/quyền; database đã có v0.5 cần quy trình nhận baseline đã kiểm tra, không chạy lại CREATE TABLE hoặc xóa volume. Thử cài mới và nâng cấp trên database riêng.
5. Seed dev/test có hai tenant, đủ Owner/Staff/Cashier, chi nhánh, kho, nhà cung cấp và SKU. Tồn ban đầu bằng 0; dữ liệu tồn có sổ cái/chứng từ hợp lệ.

**Nghiệm thu:** API register → login → tạo SKU → đọc SKU chạy trên PostgreSQL thật bằng runtime account; readiness và EF cùng trỏ một database; không phát sinh bảng PascalCase/Role/UserRole ngoài schema đã chốt. Seed có thể chạy lại an toàn.

### Bước 2 — Cô lập tenant và hợp đồng kho (P0)

Bao phủ BE1-02, BE1-03.

- Tenant nghiệp vụ chỉ lấy từ identity đã xác thực. Loại bỏ header fallback khỏi đường API nghiệp vụ; đường test dùng context riêng có kiểm soát.
- Thiếu tenant: chặn truy cập nghiệp vụ. Tách rõ đường Auth chưa có tenant, tránh làm hỏng login/register khi đổi filter.
- Guard toàn bộ thêm/sửa/xóa và ID kho/SKU/nhà cung cấp; giữ FK ghép của database làm lớp bảo vệ bổ sung.
- Quyền đề xuất: Owner/Staff tạo/xác nhận nhập và đọc ledger; Cashier đọc tồn phục vụ POS. Xác nhận với hợp đồng TV2/frontend khi tích hợp.
- Chuẩn hóa lỗi có `code`, `message`, `traceId`, lỗi trường khi cần; 400 dữ liệu sai, 401 chưa xác thực, 403 sai quyền, 404 ID không thuộc phạm vi truy cập, 409 xung đột nghiệp vụ.
- Chốt DTO phân trang và endpoint đề xuất ở mục 4 trước khi frontend tích hợp.

**Nghiệm thu:** test hai tenant cho đọc, thêm, sửa, xóa và FK; Cashier nhập kho nhận 403; token thiếu tenant không mở bộ lọc toàn hệ thống.

### Bước 3 — Phiếu nhập nháp (P1)

Bao phủ BE1-04.

- Bổ sung Warehouse, GoodsReceipt, GoodsReceiptItem và mapping; kho/chi nhánh tra cứu phối hợp TV2.
- Tạo phiếu nhiều dòng: kho thuộc chi nhánh, supplier/SKU cùng tenant, tài nguyên hoạt động, quantity > 0, unit price >= 0.
- Mỗi SKU một dòng/phiếu theo unique của SQL; trả lỗi rõ khi gửi SKU trùng.
- Lưu `created_by` từ user xác thực, không nhận tùy ý từ client; có số phiếu chống trùng và trạng thái Draft.
- API danh sách có phân trang và API chi tiết; tạo phiếu nháp chưa làm thay đổi tồn.

**Nghiệm thu:** Admin tạo/đọc một phiếu nhiều SKU trên PostgreSQL; request sai không lưu phiếu dở; chưa xác nhận thì tồn/WAC/ledger không đổi.

### Bước 4 — Xác nhận nhập, ledger và WAC (P1, phần lõi)

Bao phủ BE1-05, BE1-06, BE1-07.

- Xây posting service chung trong Application/Infrastructure; controller chỉ nhận request, gọi service và ánh xạ kết quả.
- Một connection và một transaction **READ COMMITTED** theo hợp đồng SQL hiện có.
- Xác định bộ SKU bị tác động, lấy advisory lock theo tenant/branch/SKU với thứ tự cố định **trước khi đọc tồn/WAC**; dùng đúng biểu thức hash trong `docs/database/README.md`. Khóa và kiểm tra lại chứng từ sau khi lấy khóa; không để dòng phiếu thay đổi trong lúc confirm.
- Tạo số dư 0 nếu cần trong cùng transaction. Tính WAC theo tồn chi nhánh/SKU, cập nhật cả số dư kho và tổng chi nhánh.
- Ghi ledger IN theo từng receipt item; tăng sequence chi nhánh/SKU, cập nhật last sequence kho, BalanceAfter và unit cost đúng nghiệp vụ. `reference_id` do database sinh, không gán từ API.
- Cập nhật Confirmed, confirmed_by, confirmed_at; chỉ trả thành công sau COMMIT. Lỗi deferred constraint tại COMMIT phải rollback và trả lỗi phù hợp.
- Đề xuất confirm lặp trả lại kết quả đã xác nhận, không ghi thêm ledger/cộng tồn. Unique receipt item trên ledger là bảo vệ bổ sung.
- Phối hợp `EnableRetryOnFailure` của EF với transaction tường minh: retry có giới hạn cho toàn transaction bằng execution strategy; dùng cùng chứng từ/idempotency identity, xử lý cả tình huống commit thành công nhưng mất phản hồi.
- Số lượng numeric(18,3), giá nhập/WAC numeric(20,6). Đề xuất decimal và làm tròn 6 chữ số, MidpointRounding.AwayFromZero; ghi rõ quy tắc trong unit test trước triển khai.

**Nghiệm thu:** 10 × 100.000 + 20 × 130.000 → tồn 30, WAC 120.000; phiếu nhiều dòng nguyên tử; lỗi giữa chừng và lỗi tại COMMIT không lưu một phần; confirm lặp không cộng đôi; hai phiếu cùng SKU không mất cập nhật; sequence liên tục; runtime không sửa/xóa ledger được.

### Bước 5 — API tồn và bàn giao Admin/POS (P1)

Bao phủ BE1-06, BE1-08.

- Query tồn theo tenant/chi nhánh/SKU: onHand, reserved, available = onHand − reserved; hỗ trợ chi tiết kho.
- Query ledger theo branch, SKU, khoảng thời gian, phân trang ổn định; không có API sửa/xóa ledger hoặc chỉnh onHand tùy ý.
- WAC trả cho vai trò quản lý kho theo hợp đồng; DTO POS chỉ cần thông tin phục vụ bán hàng, không mặc nhiên trả giá vốn.
- TV2 nối tồn vào search/barcode với branchId rõ ràng; Khoa cung cấp query/service và test đối chiếu.

**Nghiệm thu:** nhập xong Admin thấy tồn mới; POS cùng chi nhánh đọc đúng available; chi nhánh/tenant khác không bị cộng lẫn; SKU chưa có số dư xử lý nhất quán là tồn 0.

### Bước 6 — Nền tảng reservation (P1)

Bao phủ BE1-09; không tự mở rộng thành checkout/thanh toán đầy đủ.

- Reservation hiện có FK bắt buộc tới order/order_item: cần mapping và dữ liệu đơn/dòng đơn tối thiểu cho service/harness. Không chỉ tăng cột reserved hoặc tạo reservation không có nguồn.
- Giữ chỗ theo available của chi nhánh, phân bổ xuống kho với thứ tự xác định; cập nhật reservation và reserved hai cấp trong một transaction.
- Dùng cùng cơ chế khóa trước đọc như posting service; kiểm tra quantity, thời hạn, tenant, nguồn đơn và giới hạn lượng phân bổ.
- Chốt đường gọi service/API tối thiểu với TV2; cùng request/đơn retry không giữ chỗ thêm. Có thao tác giải phóng tối thiểu có thể kiểm thử; scheduler hết hạn đầy đủ chỉ triển khai nếu phạm vi sprint xác nhận cần.

**Nghiệm thu:** tồn hợp lệ 1, gửi đồng thời 50 yêu cầu độc lập bằng các connection/context riêng trong cửa sổ 1 giây: đúng 1 thành công, 49 bị từ chối nghiệp vụ, available = 0; không lấy lỗi timeout/500 làm kết quả từ chối hợp lệ. Retry cùng yêu cầu và release lặp không làm sai reserved.

### Bước 7 — Kiểm thử, CI và bàn giao (P1, làm cuốn chiếu)

Bao phủ BE1-10 và kiểm chứng lại BE1-01.

- Unit test WAC: tồn 0, số lẻ, giá nhập 0, đầu vào sai, làm tròn, độc lập chi nhánh; không dùng CostPrice toàn SKU làm nguồn giá vốn.
- Integration PostgreSQL riêng: mapping thực, API luồng nhập, tenant/FK, RBAC, ledger bất biến, rollback, lỗi COMMIT, confirm đồng thời và reservation 50 request.
- Giữ test Auth/Catalog hiện có; thêm PostgreSQL regression cho các thay đổi tích hợp với TV2. InMemory không thay bộ kiểm thử transaction/constraint.
- Thêm PostgreSQL service/fixture vào CI, tạo schema và runtime role tương đương; lưu test report và coverage artifact.
- Chốt phạm vi coverage lõi Application/Infrastructure của posting, ledger, WAC, reservation; mục tiêu line coverage ≥80%, báo cáo branch coverage kèm theo. Không loại lớp nghiệp vụ để làm đẹp tỷ lệ.
- Kiểm tra tái tạo DB và giữ dữ liệu sau restart trên môi trường riêng; không xóa volume làm việc.
- Cập nhật Swagger, hướng dẫn chạy, seed, request mẫu và bằng chứng E2E cho tester/frontend.

**Nghiệm thu:** build 0 error/0 warning, toàn bộ test đã chốt pass, coverage đạt phạm vi thống nhất, chạy trọn luồng đăng nhập → SKU → nhập → tồn/ledger/WAC → POS bằng dữ liệu thật.

## 4. Hợp đồng API dự kiến để bàn giao

Các endpoint dưới đây là đề xuất, chưa tồn tại và chưa phải cam kết đã chốt với frontend.

| Method | Endpoint | Nội dung |
|---|---|---|
| POST | `/api/inventory/receipts` | Tạo phiếu nháp gồm branchId, warehouseId, supplierId, note và items(skuId, quantity, unitPrice). |
| GET | `/api/inventory/receipts` | Danh sách có branch/status/date filter và phân trang. |
| GET | `/api/inventory/receipts/{id}` | Chi tiết phiếu, dòng hàng và trạng thái. |
| POST | `/api/inventory/receipts/{id}/confirm` | Xác nhận nguyên tử; confirm lặp không thay đổi số dư thêm. |
| GET | `/api/inventory/balances` | Tồn theo branchId/skuId, có tùy chọn warehouseId và phân trang. |
| GET | `/api/inventory/transactions` | Tra ledger theo branchId/skuId/kho/thời gian. |

Reservation ưu tiên service + harness tích hợp trước; API công khai phải gắn hợp đồng đơn/dòng đơn, quyền và idempotency với TV2. API tra cứu kho thuộc phần phối hợp TV2, không mặc định là đã có.

## 5. Chia bàn giao và phạm vi tiếp theo

Thứ tự gói thay đổi đề xuất:

1. **Gói A:** kết nối + EF mapping + baseline/seed + regression Auth/Catalog PostgreSQL.
2. **Gói B:** tenant guard + chuẩn lỗi/quyền + test cô lập.
3. **Gói C:** entity kho/phiếu + API phiếu nháp và hợp đồng frontend.
4. **Gói D:** confirm nguyên tử + ledger/WAC + test rollback/idempotency/concurrency.
5. **Gói E:** query tồn/ledger + phối hợp POS.
6. **Gói F:** reservation tối thiểu + test 50 request + coverage/CI/E2E còn lại.

Mỗi gói có kiểm thử phù hợp ngay khi bàn giao; không dồn việc kiểm thử tới gói F. Gói A là điểm bắt đầu vì các phần sau phụ thuộc mapping/database chạy thật. Mã Jira cần đối chiếu ticket thực tế trước khi gắn; không suy đoán từ ví dụ trong cẩm nang.

Tài liệu database có nêu Khoa phụ trách chuyển kho, kiểm kê, điều chỉnh ở phạm vi rộng hơn. Bản kế hoạch này tập trung phần Sprint 2 đã phân công: nhập kho và nền reservation. Chuyển kho/định giá hàng đang vận chuyển, kiểm kê, điều chỉnh, nghiệp vụ trả hàng, checkout hoàn chỉnh, marketplace, AI và QR cần backlog/giai đoạn riêng; sự tồn tại của bảng SQL không có nghĩa các tính năng đó đã hoàn thành.

Nếu đưa chuyển kho vào cùng đợt, phải mở rộng quy tắc WAC cho lượng/giá trị đang vận chuyển và kiểm thử tương ứng trước nghiệm thu; không tái dùng công thức chỉ dựa trên onHand một cách mặc định.
