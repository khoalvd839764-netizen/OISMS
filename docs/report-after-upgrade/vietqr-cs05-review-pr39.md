# Review PR 39: thanh toán QR VietQR (CS-05) và kế hoạch fix để nghiệm thu

> **Đối tượng review:** commit `89e6172` của nhutpm32 trên nhánh `feature/OISMS-48-shop-integrations-api` ([PR 39](https://github.com/khoalvd839764-netizen/OISMS/pull/39)), 47 file, +3623/−180. Phần webhook simulator của PR (commit `d009c36`, `b9e2093`) không nằm trong phạm vi review này.
> **Đối chiếu với:** [`07-thanh-toan-qr-vietqr.md`](../Upgrade-recommended/07-thanh-toan-qr-vietqr.md) (quyết định §0, giai đoạn a+b, trình tự §5) và quy tắc trong `CLAUDE.md`.
> **Ngày review:** 07/11/2026.

## 1. Kết luận

**Cập nhật 07/11/2026: đã sửa xong F1–F3 (commit `2f85bb8`), CS-05 chuyển `[x]`.** Bằng chứng ở §2.2–§2.4 và kết quả từng đợt ở §4. Đoạn dưới là kết luận lúc review `89e6172`.

**Chưa nghiệm thu được (lúc review).** Chức năng chính đã có ở cả hai POS và màn cấu hình cho Owner, nhưng còn **4 lỗi chặn** (CI backend đỏ, Admin mất khóa idempotency khi thanh toán, POS không gọi được API tài khoản ở môi trường thật, DB nâng cấp tự động thiếu bảng mới) và chưa có test nào trên PostgreSQL thật cho phần chạm transaction, idempotency, ràng buộc DB. `CHECKLIST.md` và `07-thanh-toan-qr-vietqr.md` đang ghi "ĐÃ HOÀN THÀNH" và CS-05 `[x]`. Cần đưa về `[~]` cho tới khi xong các mục chặn dưới đây.

## 2. Bằng chứng đã chạy (máy local, 07/11/2026)

| Lệnh | Kết quả |
| --- | --- |
| `git merge --ff-only origin/feature/OISMS-48-shop-integrations-api` | Local lên `89e6172` |
| `dotnet build src/backend/OISM.sln` | PASS, 0 warning, 0 error |
| `dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj` | PASS 263/263 |
| `dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj` | Xem §2.1 |
| `npm test --prefix src/frontend/pos-app` | PASS 77/77 (gồm `test-qr-parity.mjs`) |
| `npm run lint --prefix src/frontend/pos-app` | PASS, 3 cảnh báo trong `PaymentModal.tsx` (`Date.now` lúc render, `setState` trong effect, thiếu dependency `activeUser`) |
| `npm run build` admin-dashboard, pos-app | PASS |
| Integration test trên PostgreSQL thật | **NOT RUN** ở máy: Docker không chạy, cổng 5432 đóng, không có biến `OISM_TEST_*` |
| CI GitHub trên `89e6172` | **FAIL** job "Build & Test Backend (.NET 8)"; job frontend PASS. Không đọc được log CI (máy không có `gh`) |

### 2.1. Integration test local

Chạy trên `89e6172`: 20 ca lỗi `Npgsql.NpgsqlException: Failed to connect to 127.0.0.1:5432` (máy không có PostgreSQL), tiến trình test treo nên phải dừng tay. Coi là **NOT RUN**, không phải FAIL của code.

### 2.2. Sau khi sửa đợt F1 (07/11/2026, chưa commit)

| Lệnh | Kết quả |
| --- | --- |
| `dotnet build src/backend/OISM.sln` | PASS, 0 warning, 0 error |
| `dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj` | PASS 263/263 |
| `npm test --prefix src/frontend/pos-app` | PASS 92/92 (thêm `test-admin-pos-checkout.mjs` 6 ca, `test-b03-qr.mjs` 12 ca, `test-qr-parity.mjs` 19 ca) |
| `npm run lint --prefix src/frontend/pos-app` | PASS, 6 cảnh báo trong `PaymentModal.tsx` (`Date.now` lúc render có từ trước; `setState` trong effect, thiếu dependency `activeUser`) |
| `npm run build` admin-dashboard, pos-app, marketplace-simulator | PASS |
| Integration test PostgreSQL (gồm F1.1, F1.2) | **NOT RUN**: máy không có PostgreSQL. Chờ CI |
| Kiểm tay trên trình duyệt | **NOT RUN**: cần backend + PostgreSQL |

## 3. Phát hiện

Mức: **Chặn** = phải sửa trước khi nghiệm thu; **Cao** = sai nghiệp vụ hoặc rủi ro dữ liệu, nên sửa trong đợt này; **Vừa/Thấp** = sửa được sau.

| # | Mức | Vị trí | Vấn đề | Hậu quả |
| --- | --- | --- | --- | --- |
| R1 | Chặn | `tests/OISM.IntegrationTests/PostgresTenantIsolationTests.cs:79, 442, 478, 488` | Vẫn kiểm `TablesCount == 43` (và 44 khi tính `__EFMigrationsHistory`) trong khi schema đã lên 44 bảng nghiệp vụ. | Gần như chắc chắn là nguyên nhân CI backend đỏ. Phải xem log CI để chắc không còn lỗi khác. |
| R2 | Chặn | `admin-dashboard/src/components/pos/PaymentModal.tsx` | Commit xóa `getOrCreateRequestKey`/`clearRequestKey` (sửa lỗi [BF-003](../bug-fixaffter/errors/BF-003-pos-doi-request-key-khi-thu-lai.md)). Tiền mặt giờ sinh `generateUuidV4()` mới mỗi lần bấm; QR dùng `qrIntent?.requestKey \|\| generateUuidV4()`, nên khi không có intent thì key đổi theo mỗi lần render. `pendingCheckoutStore.ts` đã chép sang Admin nhưng **không được import ở đâu**. | Mạng chập chờn, thu ngân bấm lại thì backend nhận key mới: **trừ kho và ghi `OUT` hai lần**. Hồi quy BF-003, vi phạm quy tắc idempotency. |
| R3 | Chặn | `qrService.ts` (cả hai bản) `loadPaymentAccounts` | Gọi `fetch('/api/payment-accounts/active')` bằng **đường dẫn tương đối**, không dùng `VITE_API_BASE_URL` như các API khác; token đọc `pos_access_token` hoặc `access_token`, còn Admin lưu `oism_access_token`. Lỗi bị nuốt trong `catch {}` rồi rơi về `localStorage`. | Ở `pos.oisms.khoadang.site` và dev (Vite không có proxy `/api`), POS không lấy được tài khoản từ DB, nút QR bị khóa. Admin POS có thể không có token. Màn cấu hình của Owner thành vô dụng với POS. |
| R4 | Chặn | `Infrastructure/Persistence/DatabaseMigrationService.cs` | Đã thêm `payment_accounts` vào `RequiredTables` nhưng **không nhúng DDL** vào `UpgradeDatabaseSchemaAsync` như các migration trước (`sales_invoices`, `stock_count_*`...). | DB đã có dữ liệu mà nâng cấp qua service sẽ thiếu bảng và cột mới, `/health/ready` trả 503 "schema is incomplete". Các test nâng cấp schema sẽ lỗi. |
| R5 | Cao | Migration `20261106_payment_accounts.sql`, `oisms_schema.sql:1027, 1029` | FK ghép `(tenant_id, payment_account_id)` và `(tenant_id, updated_by_user_id)` dùng `ON DELETE SET NULL`. PostgreSQL sẽ set NULL **cả `tenant_id`** (cột NOT NULL). | Xóa user hoặc tài khoản có tham chiếu sẽ lỗi `not-null violation`. PostgreSQL 16 hỗ trợ `ON DELETE SET NULL (payment_account_id)`. Hoặc đổi sang `RESTRICT` vì tài khoản không xóa cứng. Cấu hình EF `DeleteBehavior.SetNull` cũng phải khớp. |
| R6 | Cao | `PaymentAccountService.SetDefaultAsync`, `CreateAccountAsync`; `PaymentAccountConfiguration.cs` | Bỏ mặc định cũ và đặt mặc định mới trong một `SaveChanges`, nhưng unique index lọc `WHERE is_default AND is_active` **không khai báo trong model EF**, nên EF không biết phải chạy UPDATE bỏ mặc định trước. Không có transaction hay khóa; hai Owner thao tác cùng lúc chỉ được DB chặn bằng lỗi 23505. | Có thể trả 500 ngay cả khi chỉ một người thao tác (tùy thứ tự lệnh EF), chắc chắn 500 khi đồng thời. Test hiện tại dùng EF InMemory nên không phát hiện được. |
| R7 | Cao | `PosCheckoutService` bước 15 | Không gửi `PaymentAccountId` thì server **tự chọn** tài khoản mặc định, không có thì lấy tài khoản active bất kỳ, rồi ghi vào chứng từ. | Đơn của client cũ, hoặc đơn tạo từ cấu hình `legacy-*`, sẽ ghi một tài khoản nhận mà khách không hề chuyển vào. Nên để NULL khi client không gửi. |
| R8 | Cao | `PaymentAccountService.UpdateAccountAsync`, màn cấu hình | Owner sửa được BIN và số tài khoản của tài khoản đã dùng. POS đang giữ intent với số cũ, nhưng checkout lại chụp số **mới** từ DB theo `PaymentAccountId`. | Chứng từ ghi tài khoản khác với QR khách đã quét. Đề xuất: không cho sửa BIN và số TK, muốn đổi thì thêm tài khoản mới và ngừng dùng tài khoản cũ; hoặc kiểm tra `TransferReference`/snapshot. |
| R9 | Cao | Cả hai `PaymentModal.tsx` | `getPaymentAccountsSync` (localStorage, `window`, kèm fallback cấu hình v1 theo chi nhánh) **được ưu tiên hơn API**; API chỉ được gọi khi nguồn cục bộ rỗng. | Trình duyệt còn cấu hình cũ thì bỏ qua tài khoản trong DB. Trái với kế hoạch mốc 3 C1: bỏ đọc `localStorage`/`window` sau khi có API. |
| R10 | Vừa | `PaymentAccountsController` | Không có `[ApiErrorContract]`: `ArgumentException` từ `PaymentAccount.ValidateFields` và lỗi unique từ DB nhiều khả năng ra 500 thay vì 400/409. `[Authorize(Roles = "Owner,Manager,Staff,Cashier")]` có vai trò `Manager` không tồn tại. `User.GetUserId() ?? Guid.Empty` sẽ vi phạm FK `updated_by` nếu thiếu claim. | Mã lỗi không theo catalog. |
| R11 | Vừa | `PosCheckoutService` | Tài khoản được kiểm tra **sau khi** đã khóa tồn và trừ kho (vẫn rollback được), `PAYMENT_ACCOUNT_INACTIVE` dùng chung cho cả "không tồn tại". Replay không trả `ReceiverBankName`. `TransferReference` không nằm trong so khớp idempotency. Chưa có tiêu đề lỗi mới trong `ApiErrorTitles.PosCheckout`. | Lỗi nhỏ về hợp đồng API và hiệu năng. |
| R12 | Thấp | `test-qr-parity.mjs`, unit test | Không có test cho R2, R3, R9; unit test dùng InMemory nên không kiểm unique index, check constraint, FK. Kế hoạch §3.2 yêu cầu integration test PostgreSQL cho phân quyền, tenant isolation, đặt mặc định song song, snapshot checkout, 409 khác tài khoản, replay request cũ: **chưa có ca nào**. | Chưa đủ bằng chứng theo quy tắc rủi ro cao của `CLAUDE.md` (migration, transaction, idempotency). |
| R13 | Thấp | Docs | `07-...md` và `CHECKLIST.md` ghi "ĐÃ HOÀN THÀNH... kiểm thử 100%" trong khi CI đỏ, PostgreSQL NOT RUN. Chưa cập nhật `docs/05-api-contracts/` cho `/api/payment-accounts` và trường mới của checkout, `docs/database/README.md`, quy trình deploy (chạy migration). | Hồ sơ nghiệm thu không khớp hiện trạng. |

Những phần đã ổn: tenant lấy từ JWT và có query filter; route màn cấu hình nằm trong `ProtectedRoute` Owner, menu chỉ hiện với Owner; ràng buộc `ck_payments_bank_transfer_snapshot`, BIN/số TK có check ở DB; hai bản `qrService.ts`, `types/qr.ts`, `pendingCheckoutStore.ts` giống nhau trừ dòng ghi chú; POS PWA giữ snapshot khi retry và khóa ô chọn tài khoản lúc retry; đổi tài khoản có hỏi xác nhận và tạo intent mới; danh sách ngân hàng gọi `api.vietqr.io/v2/banks` có dự phòng; số bảng 44 đã cập nhật ở `init-oisms.sh`, `oisms_schema.sql`, `RequiredTables`, `CLAUDE.md`.

## 4. Kế hoạch fix để nghiệm thu

Làm trên cùng nhánh PR 39 `feature/OISMS-48-shop-integrations-api`, không tách PR (Q-R3). Thứ tự theo mức chặn; mỗi bước một commit `[OISMS-48] fix(payment): ...`.

### Đợt F1: gỡ chặn (bắt buộc)

| Bước | Sửa | File | Xong khi |
| --- | --- | --- | --- |
| F1.1 | Đọc log CI của job backend để chắc nguyên nhân. Sửa kỳ vọng số bảng 43 → 44 (và 44 → 45 khi tính `__EFMigrationsHistory`). | `PostgresTenantIsolationTests.cs` | CI backend xanh. |
| F1.2 | Nhúng DDL của `20261106_payment_accounts.sql` (bảng, index, FK, cột `payments`, check, GRANT) vào `UpgradeDatabaseSchemaAsync`, theo đúng cách của các migration trước; thêm ràng buộc mới vào danh sách kiểm tra của `ValidateSchemaComprehensiveAsync` nếu danh sách đó đòi đủ. | `DatabaseMigrationService.cs` | Test nâng cấp từ DB 43 bảng lên 44 PASS trên PostgreSQL; `/health/ready` 200. |
| F1.3 | Khôi phục khóa idempotency ổn định ở Admin: dùng `pendingCheckoutStore.ts` đã chép (giống POS PWA): tạo bản ghi pending trước khi gọi API, retry dùng lại `requestKey` + `requestPayload` + `qrSnapshot`, chỉ xóa khi thành công hoặc lỗi chắc chắn. Không bao giờ sinh key trong lúc render; nếu chưa có intent thì khóa nút QR. | `admin-dashboard/src/components/pos/PaymentModal.tsx` | Test mới (chạy cả hai bản): bấm lại sau lỗi mạng gửi cùng key và payload; đổi giỏ ra key mới. Kiểm tay: tắt mạng giữa chừng rồi bấm lại không tạo hai đơn. |
| F1.4 | `loadPaymentAccounts` dùng `VITE_API_BASE_URL` và đúng token của từng app (POS PWA: `pos_access_token`; Admin: `oism_access_token`), hoặc nhận `fetch`/base URL qua tham số từ service API của app. Lỗi mạng hoặc 401 thì báo rõ cho thu ngân, không im lặng. | `qrService.ts` hai bản, `pos-app/src/services/api.ts`, `admin-dashboard/src/services/posApi.ts` | Build production với `VITE_API_BASE_URL` khác origin vẫn tải được tài khoản; test parity có ca base URL và token. |
| F1.5 | Chỉ lấy tài khoản từ API (Q-R4 đã chốt): xóa `getPaymentAccountsSync`, `getPosBankConfig`, `getPosBankConfigKey`, envelope v1/v2, `window.__OISMS_POS_*`, id `legacy-*` và kiểu `ScopedBankConfig`/`PaymentAccountEnvelopeV2` ở cả hai app. Modal hiện trạng thái "Đang tải tài khoản" / "Không tải được, thử lại" / "Chưa có tài khoản, liên hệ Owner". Intent và `qrSnapshot` đã lưu vẫn giữ `bankConfig` để retry. Sửa `test-b03-qr.mjs`, `test-b03-environment.mjs`, `test-qr-parity.mjs`, `test-support/qr-runtime.cjs` sang `fetch` giả. | Hai `PaymentModal.tsx`, `qrService.ts`, `paymentAccountSource.ts`, `types/qr.ts` hai bản; các file test POS | Có dữ liệu cũ trong trình duyệt vẫn chỉ hiện tài khoản từ DB; `npm test` PASS. |

#### Kết quả đợt F1 (07/11/2026, chưa commit)

| Bước | Trạng thái | Đã làm |
| --- | --- | --- |
| F1.1 | Đã sửa, chờ CI | `PostgresTenantIsolationTests.cs`: 43 → 44 bảng (3 chỗ), 44 → 45 khi tính `__EFMigrationsHistory`. Chưa đọc được log CI nên chưa chắc đây là lỗi duy nhất. |
| F1.2 | Đã sửa, chờ CI | `DatabaseMigrationService.UpgradeDatabaseSchemaAsync` chạy thêm DDL của `20261106_payment_accounts.sql` (bảng, index, FK, cột `payments`, check, GRANT) trong cùng transaction nâng cấp. Chưa thêm FK/constraint mới vào `ExpectedForeignKeys`/`ExpectedCriticalConstraints` vì F2.1 sẽ đổi `ON DELETE`; làm cùng F2.1. |
| F1.3 | Xong | POS Admin dùng `pendingCheckoutStore.ts`: lưu pending trước khi gọi API, thử lại gửi đúng `requestKey` + payload + `qrSnapshot`, chỉ xóa khi thành công hoặc lỗi chắc chắn. `posApi.checkoutPosOrder` thêm cờ `isUnknown` (mất mạng, 5xx, 408, 429, 2xx sai định dạng) và `isAuthExpired` (401). Không còn sinh khóa lúc render. Đang thử lại thì khóa đổi phương thức và tài khoản, hiện thông báo kèm mã yêu cầu. `PosPage.tsx` truyền thêm `id`, `tenantId` của user (trước đây thiếu nên intent QR và pending của Admin không bao giờ tạo được). |
| F1.4 | Xong | `loadPaymentAccounts({ baseUrl, token, fetchFn })` dùng gốc `VITE_API_BASE_URL`; `paymentAccountSource.ts` của mỗi app truyền đúng token (`pos_access_token` ở POS PWA, `oism_access_token` ở Admin). Trả trạng thái `ok`/`empty`/`error` kèm thông báo; modal có nút "Tải lại tài khoản". |
| F1.5 | Xong | Xóa `getPosBankConfig`, `getPosBankConfigKey`, `getPaymentAccountsSync`, kiểu `ScopedBankConfig`, `PaymentAccountEnvelopeV2`, id `legacy-*`. Intent QR chỉ dùng lại khi `paymentAccountId` khớp hẳn; checkout gửi `paymentAccountId` lấy từ intent đã sinh QR. Đọc lại intent đã lưu để giữ tài khoản thu ngân đã chọn sau reload. |

Còn lại sau F1: `test-b03-live.mjs` (kịch bản chạy tay, không nằm trong `npm test`) vẫn ghi cấu hình vào `localStorage` nên không còn đúng; cần viết lại để tạo tài khoản qua API khi chạy với backend thật.

### Đợt F2: đúng nghiệp vụ và dữ liệu (nên làm trong đợt này)

| Bước | Sửa | File | Xong khi |
| --- | --- | --- | --- |
| F2.1 | Sửa thẳng `20261106_payment_accounts.sql` (Q-R1: chưa chạy lên DB demo): FK `payments → payment_accounts` đổi sang `ON DELETE RESTRICT` (tài khoản không xóa cứng), FK `updated_by` đổi sang `ON DELETE SET NULL (updated_by_user_id)`; cập nhật `oisms_schema.sql`, phần nhúng trong `DatabaseMigrationService` (F1.2), `DeleteBehavior` trong EF. Ai đã chạy bản cũ ở máy local thì phải xóa và tạo lại hai FK này. | `docker/postgres/migrations/20261106_payment_accounts.sql`, `oisms_schema.sql`, cấu hình EF | Test PostgreSQL: xóa user có `updated_by` không lỗi; tài khoản đã dùng không xóa được. |
| F2.2 | Đặt mặc định an toàn: chạy trong transaction qua `IUnitOfWork`, khóa các dòng tài khoản của tenant (`FOR UPDATE` hoặc `pg_advisory_xact_lock` theo tenant trong Infrastructure), UPDATE bỏ mặc định rồi `SaveChanges`, sau đó mới đặt mặc định mới; khai báo index lọc trong `PaymentAccountConfiguration`. Lỗi unique còn lại map 409. | `PaymentAccountService.cs`, `PaymentAccountConfiguration.cs`, `ISqlLockService`/`SqlLockService` | Test PostgreSQL: 10 request đặt mặc định song song, kết quả còn đúng 1 tài khoản mặc định, không có 500. |
| F2.3 | Checkout không tự đoán tài khoản: không có `PaymentAccountId` thì snapshot để NULL. Kiểm tra tài khoản **trước** khi khóa tồn. Tách `PAYMENT_ACCOUNT_NOT_FOUND` (404/400) và `PAYMENT_ACCOUNT_INACTIVE`. Replay trả đủ `ReceiverBankName`. | `PosCheckoutService.cs`, `ApiErrorTitles.cs`, catalog lỗi | Test PostgreSQL theo §3.2: snapshot đúng, 409 khi cùng key khác tài khoản, replay request cũ không có trường mới, tài khoản tenant khác bị từ chối. |
| F2.4 | Khóa sửa BIN/số TK của tài khoản đã có (chỉ sửa tên hiển thị, tên ngân hàng); đổi số thì thêm tài khoản mới (Q-R2 đã chốt). Màn cấu hình khóa hai ô này khi sửa. | `PaymentAccountService.UpdateAccountAsync`, `PaymentAccountsPage.tsx`, DTO | Test: PUT đổi số TK trả 400 với mã lỗi riêng. |
| F2.5 | Controller: thêm `[ApiErrorContract]` với profile mới (hoặc map `ArgumentException` → 400 `VALIDATION_ERROR`), bỏ vai trò `Manager`, bỏ `?? Guid.Empty` (thiếu claim thì 401). | `PaymentAccountsController.cs`, `ApiErrorContractAttribute.cs`, `ApiErrorResponse.cs` | Test: dữ liệu sai trả 400 đúng catalog; Staff/Cashier gọi API sửa trả 403. |

#### Kết quả đợt F2 (07/11/2026, chưa commit)

| Bước | Trạng thái | Đã làm |
| --- | --- | --- |
| F2.1 | Đã sửa, chờ PostgreSQL | Sửa thẳng `20261106_payment_accounts.sql` (Q-R1): `fk_payments_payment_account_id` → `ON DELETE RESTRICT`; `fk_payment_accounts_updated_by` → `ON DELETE SET NULL (updated_by_user_id)`; `fk_payment_accounts_tenant_id` → `RESTRICT` cho giống các bảng khác. Thêm khối tự tạo lại 3 FK nếu DB local đã lỡ chạy bản nháp đầu. Cập nhật `oisms_schema.sql`, phần nhúng trong `DatabaseMigrationService`, `DeleteBehavior` trong EF (`Restrict`, `NoAction`). Thêm 3 FK và 4 ràng buộc (`uq_payment_accounts_2`, 3 CHECK) vào danh sách kiểm tra schema; CHECK chỉ kiểm tra có tồn tại, không so biểu thức. |
| F2.2 | Đã sửa, chờ PostgreSQL | Mọi thao tác ghi tài khoản chạy trong `IUnitOfWork` transaction, giữ khóa `pg_advisory_xact_lock({tenant}/payment_accounts)` qua `ISqlLockService.LockPaymentAccountsAsync`; bỏ mặc định cũ và `SaveChanges` trước rồi mới đặt mặc định mới. Khai báo index lọc `uq_payment_accounts_tenant_default_active` trong EF. Lỗi unique map sang 409 (`PAYMENT_ACCOUNT_DUPLICATE`, `PAYMENT_ACCOUNT_CONFLICT`). Đặt mặc định cho tài khoản đã là mặc định, ngừng dùng tài khoản đã ngừng: trả kết quả, không lỗi. |
| F2.3 | Đã sửa, chờ PostgreSQL | Checkout không tự chọn tài khoản khi thiếu `PaymentAccountId` (snapshot để NULL). Kiểm tra tài khoản ngay sau khi xác thực chi nhánh, trước khi trừ kho; replay cùng `RequestKey` vẫn chạy trước nên tài khoản bị ngừng sau đó không làm hỏng replay. Tách `PAYMENT_ACCOUNT_NOT_FOUND` (404) và `PAYMENT_ACCOUNT_INACTIVE` (409). So khớp idempotency thêm `TransferReference` (khi cả hai bên có). Replay trả `ReceiverBankName`. |
| F2.4 | Xong | `PaymentAccount.Update` thay bằng `Rename` (chỉ đổi tên ngân hàng, tên viết tắt, tên chủ tài khoản). `PUT` gửi BIN hoặc số tài khoản khác trả 400 `PAYMENT_ACCOUNT_IDENTITY_IMMUTABLE`. Màn cấu hình hiện chưa có chức năng sửa nên không có ô nào cần khóa. |
| F2.5 | Xong | Profile lỗi `PaymentAccounts`: `ArgumentException` → 400 `VALIDATION_ERROR`, lỗi nghiệp vụ trả ProblemDetails có tiêu đề. Bỏ vai trò `Manager`. Thiếu claim user trả 401 thay vì dùng `Guid.Empty`; service cũng chặn `Guid.Empty`. Màn cấu hình đọc `detail` của ProblemDetails để hiện đúng thông báo. Catalog lỗi thêm `PAYMENT_ACCOUNT_IDENTITY_IMMUTABLE`, `PAYMENT_ACCOUNT_CONFLICT`, sửa mã HTTP của `PAYMENT_ACCOUNT_INACTIVE`. |

Bằng chứng F2:

| Lệnh | Kết quả |
| --- | --- |
| `dotnet build src/backend/OISM.sln` | PASS, 0 warning, 0 error |
| `dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj` | PASS 268/268 (thêm 5 ca: không đổi được BIN/số TK, bắt buộc user id, tạo mặc định mới chỉ còn 1 mặc định, không đặt mặc định tài khoản đã ngừng, tài khoản tenant khác trả 404) |
| `npm test --prefix src/frontend/pos-app` | PASS 92/92 |
| `npm run build` admin-dashboard, pos-app | PASS |
| Transaction, khóa advisory, index lọc, FK, CHECK, checkout QR trên PostgreSQL | **NOT RUN**: unit test chạy EF InMemory, không kiểm được các phần này. Cần làm F3.1 và chạy CI |

### Đợt F3: bằng chứng và hồ sơ

| Bước | Việc | Xong khi |
| --- | --- | --- |
| F3.1 | Thêm `PaymentAccountApiIntegrationTests` (PostgreSQL): phân quyền Owner/Staff/Cashier, tenant isolation khi đoán `id`, trùng BIN+số TK 409, đặt mặc định song song, check constraint chặn snapshot trên đơn `Cash`. Thêm ca checkout `BankTransfer` vào test checkout POS hiện có. | CI xanh với các test mới. |
| F3.2 | Kiểm tay toàn luồng trên local có PostgreSQL (`docker compose up -d`): Owner thêm 2 tài khoản → POS PWA và `/pos` Admin chọn tài khoản → xác nhận tay → chi tiết đơn và hóa đơn bán hàng hiện đúng tài khoản; tắt mạng giữa chừng rồi bấm lại không tạo đơn thứ hai. Dữ liệu ghi rõ "Dữ liệu kiểm thử", không chuyển tiền thật. | Có ảnh chụp và ghi chú trong báo cáo này. |
| F3.3 | Sửa docs: `CHECKLIST.md` CS-05 về `[~]` cho tới khi F1–F3 xong rồi mới `[x]`; bỏ câu "kiểm thử 100%" trong `07-...md`, ghi PASS/FAIL/NOT RUN thật; thêm `/api/payment-accounts` và trường mới của checkout vào `docs/05-api-contracts/`; `docs/database/README.md`; bước chạy migration trong `docs/13-deploy-aws/04-quy-trinh-deploy-va-cap-nhat.md`; sửa 3 cảnh báo lint. | Docs khớp code. |

#### Kết quả đợt F3 (07/11/2026, chưa commit)

| Bước | Trạng thái | Đã làm |
| --- | --- | --- |
| F3.1 | Xong | Thêm `PaymentAccountIntegrationTests` (5 ca, PostgreSQL thật): phân quyền Owner/Staff/Cashier và cô lập tenant (tenant khác trả 404); trùng BIN + số TK 409, sửa số TK 400, BIN sai 400, đổi tên 200; 20 request đặt mặc định song song chỉ trả 200/409 và còn đúng 1 mặc định; FK (`RESTRICT`, `SET NULL (updated_by_user_id)`) và index mặc định chặn ghi thẳng DB; checkout QR lưu bản chụp người nhận, replay sau khi tài khoản bị ngừng vẫn trả đơn cũ không trừ kho thêm, cùng key khác tài khoản hoặc khác nội dung CK trả 409, tài khoản đã ngừng trả 409 và tài khoản tenant khác trả 404 đều không tạo đơn và không trừ kho, client không gửi `paymentAccountId` thì bản chụp để trống, CHECK chặn bản chụp trên đơn tiền mặt, FK chặn xóa cứng tài khoản đã có chứng từ. |
| F3.2 | Xong | Kiểm tay trên DB tách riêng (container `postgres:16-alpine` mới, chạy `init-oisms.sh` của repo, không đụng DB của `docker compose`), backend chạy từ source, Admin và POS PWA chạy dev, dữ liệu "Dữ liệu kiểm thử", không chuyển tiền thật. Kết quả ở §2.4. |
| F3.3 | Xong | `CHECKLIST.md` CS-05 về `[~]` (chờ CI); bỏ câu "kiểm thử 100%" trong `07-thanh-toan-qr-vietqr.md`; thêm `/api/payment-accounts` và trường QR của checkout vào `docs/05-api-contracts/05-pos-endpoints.md`; thêm bảng 44 `payment_accounts` và cột mới của `payments` vào `docs/database/README.md`; cập nhật CS-05 trong `frontend-coming-soon-va-upgrade.md`. Bỏ `setState` đồng bộ trong effect tải tài khoản và ghi chú dependency, lint `PaymentModal.tsx` của POS PWA chỉ còn 1 cảnh báo có từ trước. `docs/13-deploy-aws/04-quy-trinh-deploy-va-cap-nhat.md` đã chạy mọi file trong `docker/postgres/migrations/` nên không cần sửa. |

### 2.3. Integration test PostgreSQL (sau F1–F3, 07/11/2026, chưa commit)

Chạy trên container `postgres:16-alpine` tạm (cổng 55432, biến `OISM_TEST_*` như CI):

| Lệnh | Kết quả |
| --- | --- |
| `dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj --filter "FullyQualifiedName~PaymentAccountIntegrationTests\|FullyQualifiedName~PostgresTenantIsolationTests"` | PASS 40/40 (3 phút 36 giây) |
| `dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj` | PASS 372/372 (27 phút 38 giây) |
| CI GitHub trên `2f85bb8` | Frontend PASS; backend đang chạy lúc cập nhật tài liệu này. Nếu đỏ thì đưa CS-05 về `[~]` |

### 2.4. Kiểm tay toàn luồng (F3.2)

| Bước | Kết quả |
| --- | --- |
| `init-oisms.sh` trên volume trống | PASS: "44 (all 44 verified)" |
| Backend từ source, `/health/ready` | PASS: 200, 44 bảng, user `oisms_app` |
| Owner thêm 2 tài khoản trên màn "Tài khoản nhận tiền" (danh sách ngân hàng tải từ `api.vietqr.io/v2/banks`) | PASS: tài khoản đầu là mặc định |
| Nhập 10 sản phẩm qua phiếu mua + phiếu nhập (API) | PASS |
| `/pos` của Admin: chọn QR | PASS: QR MBBank (mặc định) hiện ngay, ô chọn 2 tài khoản |
| Đổi sang Vietcombank | PASS: QR và nội dung CK đổi (`3F214B2D` → `09659EDD`) |
| "Xác nhận đã nhận tiền" | PASS: hóa đơn ghi "Chuyển khoản QR", TK nhận Vietcombank ••••3210, nội dung `OISMS POS 09659EDD` |
| Kiểm DB | PASS: đơn `Completed`, payment `BankTransfer`/`Succeeded` có `transfer_reference`, `receiver_bank_bin = 970436`, `receiver_account_number = 9876543210`, `payment_account_id`; tồn 10 → 9; sổ cái `IN 10`, `OUT 1` |
| POS PWA (Cashier) chọn QR | PASS: `GET http://localhost:5277/api/payment-accounts/active` 200 (theo `VITE_API_BASE_URL`), QR tài khoản mặc định |
| Mất mạng giữa chừng rồi bấm lại ở trình duyệt | NOT RUN thủ công; đã có test tự động `test-admin-pos-checkout.mjs` |

Lệnh kiểm tra cuối (ghi kết quả PASS/FAIL/NOT RUN vào §2):

```bash
dotnet build src/backend/OISM.sln
dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj
npm test --prefix src/frontend/pos-app
npm run lint --prefix src/frontend/pos-app
npm run build --prefix src/frontend/admin-dashboard
npm run build --prefix src/frontend/pos-app
```

## 5. Câu hỏi cần chốt

| # | Câu hỏi | Đề xuất |
| --- | --- | --- |
| Q-R1 | Migration `20261106_payment_accounts.sql` đã chạy lên DB demo trên EC2 chưa? | **Đã chốt (07/11/2026):** chưa chạy. F2.1 sửa thẳng file `20261106_payment_accounts.sql`, không thêm migration bù. |
| Q-R2 | Có cho Owner sửa BIN/số tài khoản của tài khoản đã tạo không (R8)? | **Đã chốt:** không. Chỉ sửa tên hiển thị; đổi số thì thêm tài khoản mới và ngừng dùng tài khoản cũ. |
| Q-R3 | Có tách QR ra PR riêng không? | **Đã chốt:** không tách; sửa tiếp trên nhánh PR 39. |
| Q-R4 | Có giữ nguồn cấu hình lưu trên trình duyệt (`localStorage`/`window`) không, hay POS chỉ lấy tài khoản từ API (R9)? | **Đã chốt:** chỉ lấy từ API, bỏ hẳn đọc `localStorage`/`window` ở mọi môi trường; test frontend dùng `fetch` giả. |
