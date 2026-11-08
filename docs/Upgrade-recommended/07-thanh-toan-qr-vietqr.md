# 07. Kế hoạch thanh toán QR bằng VietQR (CS-05)

> **Loại tài liệu:** kế hoạch và đặc tả CS-05.
> **Trạng thái (07/11/2026):** giai đoạn (a) + (b) đã xong trên nhánh `feature/OISMS-48-shop-integrations-api` (commit `89e6172`, sửa theo review ở `2f85bb8`). Bằng chứng kiểm thử (PASS/FAIL/NOT RUN) ghi trong [review PR 39](../report-after-upgrade/vietqr-cs05-review-pr39.md). Giai đoạn (c) tạm gác.
> **Ngày khảo sát code:** 06/11/2026, nhánh `feature/OISMS-48-shop-integrations-api` (phần QR không phụ thuộc thay đổi chưa commit của nhánh này).

## 0. Quyết định đã chốt (06/11/2026)

| # | Quyết định | Ảnh hưởng tới kế hoạch |
| --- | --- | --- |
| Q1 | Làm giai đoạn **(a) + (b)**. Giai đoạn (c) chưa làm. | §3.3 chỉ giữ làm tham khảo. |
| Q2 | Tài khoản nhận dùng **chung cho cả shop** (theo tenant), không tách theo chi nhánh. | Bảng `payment_accounts` không có `branch_id`; POS bỏ việc tra theo chi nhánh. |
| Q3 | **Sửa** `06-pos-terminal-screens.md` §3.1 theo code: giữ `RequestKey` khi mở lại cùng giỏ và khi gửi lại. | Thêm vào danh sách docs của giai đoạn (a). |
| Q5 | Danh sách ngân hàng **gọi API** (`https://api.vietqr.io/v2/banks`). | Chỉ màn cấu hình của Owner gọi; DB lưu sẵn BIN và tên ngân hàng nên POS không phụ thuộc API này. |
| Q6 | Ảnh QR **tiếp tục lấy từ `img.vietqr.io`**. | Bỏ việc thêm thư viện vẽ QR. |
| Q7 | **Có** lưu tài khoản nhận và nội dung chuyển khoản vào `payments` khi checkout. | Giữ thiết kế ở §3.2. |
| Ý kiến thêm | Owner được **thêm (nhiều) tài khoản ngân hàng** cho shop. | Bảng cho phép nhiều tài khoản mỗi tenant, có một tài khoản mặc định (xem §3.2). Cách chọn tài khoản lúc thanh toán còn chờ chốt (Q11). |

Q4 (Cashier được xác nhận tay), Q10 (giữ cách tạo nội dung CK hiện có) và Q11 (thu ngân chọn tài khoản, mặc định chọn sẵn) đã chốt ở §4. Không còn câu hỏi mở cho giai đoạn (a)+(b). Q8, Q9 thuộc giai đoạn (c) nên tạm gác.

## 1. Hiện trạng

### 1.1. POS PWA (`src/frontend/pos-app`): đã có VietQR, thu ngân xác nhận tay

| Thành phần | Hiện trạng |
| --- | --- |
| `src/services/qrService.ts` (467 dòng) | Sinh chuỗi EMVCo/NAPAS 247 (tag 00, 01=`12` QR động, 38 với AID `A000000727` + BIN + số TK + `QRIBFTTA`, 53=`704`, 54 số tiền, 58=`VN`, 62/08 nội dung CK, 63 CRC-16/CCITT-FALSE), tự parse lại để kiểm CRC. Sinh URL ảnh `https://img.vietqr.io/image/{bin}-{stk}-{template}.png?amount=&addInfo=&accountName=`. Kiểm tra BIN 6 số, số TK 6–19 ký tự, số tiền ≤ 9.999.999.999.999, nội dung ≤ 50 ký tự, `RequestKey` là UUID v4. |
| Nội dung chuyển khoản | `OISMS POS <8 ký tự đầu của RequestKey>` (`extractShortReference`). |
| Ý định thanh toán (`PosQrIntent`) | Lưu `sessionStorage` theo tenant + user + chi nhánh, gắn `cartFingerprint`, số tiền, `requestKey` và **bản chụp tài khoản nhận**. Đóng/mở modal hoặc reload vẫn giữ cùng key và cùng QR. Khi retry đơn chưa rõ kết quả dùng `qrSnapshot` trong bản ghi pending, không sinh QR tới người nhận mới. |
| Guard nghiệp vụ | `isPaymentConfirmedByScanOrTimeout()` luôn `false`: hiển thị, quét hay hết giờ đều không được coi là đã trả tiền (T04-AC-03). Thu ngân bấm xác nhận thì mới gọi checkout với `paymentMethod: 'BANK_TRANSFER_QR'`. |
| Nguồn tài khoản nhận (`getPosBankConfig`) | `window.__OISMS_POS_BANK_CONFIGS__` (mảng envelope `{version:1, tenantId, branchId, bankConfig}`) nếu được inject, nếu không thì `localStorage` khóa `pos_bank_config_v1:<tenant>:<branch>`. **Không có API, không có màn hình cấu hình**, không fallback tài khoản mẫu. Thiếu cấu hình thì nút QR bị khóa, vẫn chọn được tiền mặt. |
| Ảnh QR | Tải từ `img.vietqr.io`. Lỗi tải ảnh thì chỉ hiện khối "Mã QR ngoại tuyến" với 40 ký tự đầu của payload, **không vẽ QR thật** từ `rawPayload`. |
| Test | `npm test` chạy `test-b03-qr.mjs` (8 test) và `test-b03-environment.mjs` (7 test) qua `test-support/qr-runtime.cjs`; `test-b03-live.mjs`, `test-b03-browser-production.mjs` là kịch bản chạy tay. |

### 1.2. POS tích hợp trong Admin (`src/frontend/admin-dashboard`): chưa bật QR

- `src/components/pos/PaymentModal.tsx` (565 dòng, hai kiểu hiển thị: panel trong `PosPage.tsx` và modal): nút "Chuyển khoản QR" `disabled` kèm `ComingSoonBadge`; nhánh `BANK_TRANSFER_QR` còn vẽ **SVG QR mô phỏng** và cảnh báo "không chuyển tiền thật".
- Không có `qrService.ts`, `types/qr.ts` trong Admin. `types/pos.ts` đã có kiểu `paymentMethod: "CASH" | "BANK_TRANSFER_QR"`.
- Khóa idempotency: `oism_pos_intent_${branchId}` trong `sessionStorage`, **không gắn tenant và user**, fallback `req-${Date.now()}` khi thiếu `crypto.randomUUID` (không phải UUID v4, sẽ bị `qrService` từ chối). Chưa có cơ chế pending/unknown-result như POS PWA.
- Gọi `POST /api/orders/checkout` (`services/posApi.ts`), cùng lõi với `/api/pos/checkout`.

### 1.3. Backend checkout POS: QR chỉ là nhãn phương thức

- `PosCatalogController` (`POST /api/pos/checkout`) và `OrdersController` (`POST /api/orders/checkout`) cùng gọi `PosCheckoutActionHelper` → `PosCheckoutService`.
- `PosCheckoutService` chuẩn hóa `Cash`/`CASH` → `Cash`, `BankTransfer`/`BANK_TRANSFER_QR`/`QR` → `BankTransfer`; giá trị khác trả `400 INVALID_PAYMENT_METHOD`.
- Trong **một transaction**: khóa tồn, trừ `OnHand`, ghi `OUT`, chụp giá vốn, tạo `Payment` rồi `payment.Succeed(now)` ngay, tạo `SalesInvoice` với `PaymentMethodSnapshot`, đơn `Completed`. Với `BankTransfer`, `AmountGiven = totalAmount`, `CashReceived = null`.
- Idempotency: so khớp `RequestKey` với chi nhánh, giá, phương thức, số tiền; khác thì 409.
- Schema `payments` (`docs/database/oisms_schema.sql`): `method IN ('Cash','BankTransfer')`, `status IN ('Pending','Succeeded','Failed')`, ràng buộc `ck_payments_cash_received` buộc `BankTransfer` có `cash_received IS NULL`. **Không có cột nào lưu nội dung CK, mã tham chiếu hay tài khoản nhận.** Dòng 14 của schema còn ghi "QR confirmation remain open".
- Không có bảng, entity hay endpoint nào cho tài khoản ngân hàng nhận tiền. `branches` chỉ có `name`, `address`, `is_active`.

### 1.4. Tóm tắt

| Hạng mục | POS PWA | POS Admin | Backend |
| --- | --- | --- | --- |
| Sinh VietQR thật | Có | Không (SVG mô phỏng, nút tắt) | Không cần |
| Thu ngân xác nhận tay | Có | Không | Ghi `BankTransfer`, `Succeeded` ngay |
| Tài khoản nhận | Trình duyệt (`localStorage`/inject) | Không | Không có |
| Lưu nội dung CK, tài khoản nhận vào chứng từ | Chỉ ở client | Không | Không |
| Xác nhận tự động | Không | Không | Không |

## 2. Đối chiếu yêu cầu và các điểm mâu thuẫn

| # | Nguồn yêu cầu | Code hiện tại | Cần người dùng quyết |
| --- | --- | --- | --- |
| M1 | `06-pos-terminal-screens.md` §3.1: "Mỗi lần mở Modal thanh toán, hệ thống tạo một `RequestKey` mới". | POS PWA (và `CLAUDE.md`) giữ nguyên key khi đóng/mở lại với cùng giỏ hoặc khi retry lúc kết quả chưa rõ. Với QR bắt buộc phải giữ key vì nội dung CK lấy từ key. | Đề xuất sửa đặc tả theo code. |
| M2 | `fr-07` FR-POS-04: Reserve → Confirm → Deduct trong **một transaction** ngay khi thanh toán. | Đúng với xác nhận tay. Nếu làm giai đoạn (c) có trạng thái "chờ thanh toán" thì không còn một transaction nữa. | Chọn phương án ở §3.3. |
| M3 | `fr-07` FR-POS-03: hỗ trợ "Chuyển khoản QR Code", không nói ai xác nhận. Brief Sprint 3 (T04-AC-03) chốt "thu ngân xác nhận tại quầy". | Đúng với POS PWA. | Giai đoạn (c) có thay quy tắc này không, hay chỉ bổ sung. |
| M4 | T04-AC-02: "có nguồn cấu hình tài khoản thụ hưởng rõ ràng". Báo cáo frontend ghi "tài khoản nhận lưu trên từng trình duyệt". | `localStorage` theo từng máy, không ai quản trị được. | Giai đoạn (b) giải quyết. |
| M5 | Yêu cầu giao việc nói "theo tenant". | POS PWA đang tách theo **tenant + chi nhánh**. | Một tài khoản/tenant, hay mỗi chi nhánh một tài khoản (xem Q2). |
| M6 | §3.2 cùng file đặc tả: hóa đơn in có "Mã QR tra cứu hóa đơn điện tử". | Chưa có. Đây là QR khác, không phải QR thanh toán. | Ngoài phạm vi kế hoạch này. |

## 3. Kế hoạch theo giai đoạn

### 3.1. Giai đoạn (a): bật VietQR cho POS Admin, dùng chung logic với POS PWA

**Mục tiêu:** POS Admin có cùng hành vi QR như POS PWA: QR thật, ý định thanh toán giữ key, thu ngân xác nhận tay. Tài khoản nhận vẫn lấy theo nguồn hiện có cho tới khi có giai đoạn (b).

**File sẽ đổi**

| File | Việc |
| --- | --- |
| `admin-dashboard/src/services/qrService.ts` (mới) | Bản sao nguyên văn từ `pos-app/src/services/qrService.ts`, cùng cách làm với `posCartUnits.ts`. Đầu file ghi chú "sửa thì sửa cả hai bản". |
| `admin-dashboard/src/types/qr.ts` (mới) | Bản sao `pos-app/src/types/qr.ts`. |
| `admin-dashboard/src/components/pos/PaymentModal.tsx` | Bỏ `disabled` và `ComingSoonBadge` ở cả hai kiểu hiển thị; xóa SVG mô phỏng và cảnh báo mô phỏng; dùng `getOrCreatePosQrIntent`/`generateVietQrPayload`; khi chọn QR, `requestKey` của checkout lấy từ intent; thêm nút "Đã nhận tiền" (xác nhận tay); xóa intent khi checkout thành công. Sửa khóa `oism_pos_intent_*` để gắn tenant + user và bỏ fallback `req-${Date.now()}` (dùng hàm UUID v4 như POS PWA). |
| `admin-dashboard/src/pages/PosPage.tsx` | Truyền `user` (id, tenantId) xuống modal nếu chưa có. |
| `admin-dashboard/src/components/pos/ReceiptPrintModal.tsx` | Hiện "Chuyển khoản" và nội dung CK trên phiếu (nếu POS PWA đã làm thì làm giống). |
| `pos-app/test-b03-qr.mjs` hoặc file mới `pos-app/test-qr-parity.mjs` | Chạy cùng bộ test trên **cả hai bản** `qrService.ts` (giống `test-cart-units.mjs`) và thêm vào script `npm test`. |
| Docs | `CHECKLIST.md` CS-05, `frontend-coming-soon-va-upgrade.md`, `06-pos-terminal-screens.md` (sửa M1 nếu được duyệt). |

**Schema:** không đổi.

**Rủi ro**

- Tồn kho, giữ chỗ: không đổi, checkout vẫn là một transaction như tiền mặt.
- Idempotency: nếu Admin sinh QR bằng key A nhưng checkout gửi key B thì nội dung CK trên sao kê không khớp chứng từ. Bắt buộc dùng `requestKey` của intent; retry giữ nguyên key và payload. Đổi phương thức giữa chừng (QR → tiền mặt) phải ra key mới, vì backend trả 409 khi cùng key khác phương thức.
- Khóa intent cũ không gắn tenant: hai tài khoản đăng nhập lần lượt trên cùng trình duyệt có thể dùng lại key của nhau. Sửa trong giai đoạn này.
- Hai bản `qrService.ts` lệch nhau theo thời gian: chặn bằng test chạy cả hai bản.
- Riêng tư: URL ảnh gửi số TK, tên, số tiền sang `img.vietqr.io`. Đã chốt chấp nhận (Q6); mất mạng ra ngoài thì không có ảnh QR, thu ngân chuyển sang tiền mặt.

**Cách test**

- `npm test --prefix src/frontend/pos-app` (gồm test chạy cả hai bản), `npm run lint --prefix src/frontend/pos-app`, `npm run build --prefix src/frontend/admin-dashboard`.
- Chạy Admin local, vào `/pos`, nạp tài khoản kiểm thử (ghi rõ "Dữ liệu kiểm thử") vào `localStorage`, kiểm tra: QR hiển thị, đóng/mở và reload giữ nguyên nội dung CK, chưa có cấu hình thì nút QR khóa, xác nhận tay tạo đơn `BankTransfer` (xem trong chi tiết đơn và hóa đơn bán hàng). Không chuyển tiền thật.
- Backend không đổi nên không cần test PostgreSQL mới; vẫn chạy `dotnet test` unit để chắc không vỡ.

### 3.2. Giai đoạn (b): tài khoản nhận tiền theo tenant, chỉ Owner sửa

**Mục tiêu:** bỏ cấu hình trong trình duyệt; tài khoản nhận nằm trong DB, Owner quản trị, POS đọc qua API; chứng từ lưu lại tài khoản và nội dung CK đã dùng.

**Thiết kế đề xuất**

- Bảng mới `payment_accounts` dùng chung cho cả shop (Q2), cho phép nhiều tài khoản: `id`, `tenant_id`, `bank_bin` (6 số), `bank_name`, `bank_short_name`, `account_number`, `account_name`, `is_default`, `is_active`, `created_at`, `updated_at`, `updated_by_user_id`. Ràng buộc `UNIQUE (tenant_id, id)`, `UNIQUE (tenant_id, bank_bin, account_number)`, unique index một phần `(tenant_id) WHERE is_default AND is_active` để mỗi shop có đúng một tài khoản mặc định. Check BIN 6 số, số TK 6–19 ký tự chữ/số. Không xóa cứng tài khoản đã dùng trong `payments`, chỉ tắt `is_active`.
- Entity `PaymentAccount : IMustHaveTenant` trong `OISM.Domain`, query filter tenant trong `AppDbContext`, cấu hình EF trong `Infrastructure/Persistence/Configurations`.
- Application: `Features/PaymentAccounts/` (service, DTO, validator). `TenantId` chỉ lấy từ `CurrentTenantService`.
- API (`PaymentAccountsController`):
  - `GET /api/payment-accounts`, `POST /api/payment-accounts`, `PUT /api/payment-accounts/{id}`, `POST /api/payment-accounts/{id}/set-default`, `POST /api/payment-accounts/{id}/deactivate`: `[Authorize(Roles = "Owner")]`. Đặt mặc định chạy trong một transaction (bỏ mặc định cũ rồi đặt cái mới) để không vi phạm unique index.
  - `GET /api/payment-accounts/active`: Owner, Staff, Cashier; trả danh sách tài khoản `is_active`, đánh dấu tài khoản mặc định. Danh sách rỗng thì POS khóa nút QR với thông báo `PAYMENT_ACCOUNT_NOT_CONFIGURED`.
- Chọn tài khoản lúc thanh toán (Q11):
  - Modal thanh toán ở cả hai POS có ô chọn tài khoản khi chọn QR, chọn sẵn tài khoản mặc định; chỉ có một tài khoản thì hiện dạng chữ, không cần ô chọn.
  - Ý định thanh toán (`PosQrIntent`) thêm `paymentAccountId` và chụp `bankConfig` của tài khoản đã chọn.
  - Đổi tài khoản sau khi QR đã hiện thì **tạo intent mới với `RequestKey` mới**, và hỏi lại thu ngân "Khách đã chuyển vào tài khoản cũ chưa?" trước khi đổi, vì khách có thể đã quét mã cũ.
  - Khi đang gửi lại đơn chưa rõ kết quả, ô chọn bị khóa theo bản chụp, không được đổi.
  - Checkout gửi `PaymentAccountId`. Server kiểm tra tài khoản thuộc tenant trong JWT và còn `is_active`, rồi lưu bản chụp (BIN, số TK, tên) từ DB vào `payments`; không lấy số TK do client gửi. Tài khoản bị Owner ngừng dùng giữa chừng thì trả lỗi `PAYMENT_ACCOUNT_INACTIVE`, trừ khi đó là replay của cùng `RequestKey` đã thành công.
- Danh sách ngân hàng (Q5): màn cấu hình của Owner gọi `https://api.vietqr.io/v2/banks` từ trình duyệt, lấy `bin`, `name`, `shortName`, `logo`. Khi lưu, backend chỉ kiểm tra định dạng BIN và lưu kèm tên ngân hàng, nên POS không cần gọi API này. API lỗi thì màn cấu hình báo lỗi và cho thử lại; tài khoản đã lưu vẫn dùng bình thường. Chưa có CSP chặn gọi ra ngoài (đã kiểm tra `Caddyfile`, `nginx.conf`, `index.html`); nếu sau này thêm CSP phải mở `connect-src api.vietqr.io` và `img-src img.vietqr.io`.
- Frontend:
  - Admin: màn "Tài khoản nhận tiền" trong phần cài đặt, chỉ hiện với Owner (route guard + ẩn menu): danh sách tài khoản, thêm, sửa, đặt mặc định, ngừng dùng, xem trước QR mẫu 1.000 đ.
  - Cả hai POS: `getPosBankConfig` gọi `/api/payment-accounts/active` thay cho `localStorage`/`window`, bỏ tham số chi nhánh. Intent vẫn chụp `bankConfig` lúc tạo, nên Owner đổi tài khoản giữa chừng không làm đổi QR của giao dịch đang chờ.
- Ghi lại trên chứng từ: thêm vào `payments` các cột NULL được `transfer_reference` (nội dung CK), `receiver_bank_bin`, `receiver_account_number`, `receiver_account_name`, `payment_account_id`; check chỉ có giá trị khi `method = 'BankTransfer'`. `PosCheckoutRequestDto` thêm `PaymentAccountId` và `TransferReference` tùy chọn (bắt buộc với đơn QR tạo sau khi deploy); server tra tài khoản theo `PaymentAccountId` và lưu bản chụp từ DB.

**File sẽ đổi**

| Lớp | File |
| --- | --- |
| DB | `docker/postgres/migrations/2026MMDD_payment_accounts.sql` (mới, không sửa file cũ), `docs/database/oisms_schema.sql`, `docs/database/README.md` |
| Infrastructure | `DatabaseMigrationService.RequiredTables` (+`payment_accounts`), danh sách constraint kỳ vọng nếu có, `AppDbContext`, cấu hình EF mới, cấu hình `Payment` |
| Docker | `docker/postgres/init-oisms.sh`: số bảng 43 → 44 (ba chỗ: dòng chú thích, `-ge`, `-ne`) |
| Domain | `Entities/PaymentAccount.cs`, `Entities/Payment.cs` (thêm trường) |
| Application | `Features/PaymentAccounts/*`, `PosCheckoutService` (lưu bản chụp), `Orders/Dtos/OrderDtos.cs`, `DependencyInjection.cs` |
| WebApi | `Controllers/PaymentAccountsController.cs` |
| Frontend | Màn cấu hình Admin, menu trong `AdminShell.tsx`, route trong `App.tsx`, `qrService.ts` hai bản, service API hai app |
| Docs | `05-api-contracts/`, `12-error-and-status-codes/02-error-codes-catalog.md`, `01-users-and-rbac/02-rbac-matrix.md`, `CLAUDE.md` (44 bảng) |

> Nhánh OISMS-48 hiện chưa thêm bảng. Nếu nhánh nào merge trước có thêm bảng thì số bảng phải tính lại khi rebase.

**Rủi ro**

- Tenant isolation: mọi endpoint lấy tenant từ JWT; `PUT`/`set-default` với `id` của tenant khác phải trả 404 nhờ query filter. Không đọc tenant từ header/body.
- Hai Owner cùng đặt mặc định một lúc: unique index chặn trạng thái hai tài khoản mặc định; một request nhận lỗi và phải thử lại.
- Phân quyền: Staff/Cashier không được sửa; chặn ở controller và test 403. Số TK hiện trên QR nên Cashier được đọc.
- Idempotency: thêm `PaymentAccountId` và `TransferReference` vào payload làm đổi điều kiện so khớp. Cùng `RequestKey` mà khác `PaymentAccountId` thì trả 409. Request cũ không có hai trường này phải replay được (coi NULL là khớp với bản đã lưu), nếu không đơn đang ở trạng thái "chưa rõ kết quả" sẽ bị 409 sau khi deploy.
- Chọn nhầm tài khoản: tiền vào tài khoản khác với chứng từ, phải đối chiếu tay. Giảm rủi ro bằng tên ngân hàng và 4 số cuối hiện rõ trên modal và hóa đơn.
- Transaction: tra tài khoản hiệu lực đọc trong transaction checkout, không cần khóa thêm; không đổi thứ tự khóa tồn hiện có.
- Migration trên DB đã có dữ liệu: `/health/ready` sẽ 503 nếu chưa chạy migration; ghi bước chạy vào `docs/13-deploy-aws/04-quy-trinh-deploy-va-cap-nhat.md`.
- Tồn kho, giữ chỗ, sổ cái: không đổi.

**Cách test**

- Unit: validator (BIN, số TK, tên), đổi tài khoản mặc định.
- Integration trên **PostgreSQL thật** (biến `OISM_TEST_*` như CI): Owner thêm/sửa/đặt mặc định/ngừng dùng được, Staff/Cashier 403; tenant A không đọc hay sửa được tài khoản tenant B kể cả khi đoán `id`; hai request đặt mặc định song song vẫn chỉ còn một tài khoản mặc định; checkout với `PaymentAccountId` của tenant khác hoặc đã ngừng dùng bị từ chối; cùng `RequestKey` khác `PaymentAccountId` trả 409; checkout `BankTransfer` lưu đúng bản chụp; replay cùng key có và không có `TransferReference`; check constraint chặn bản chụp trên đơn `Cash`.
- Chạy migration lên bản sao DB có dữ liệu, rồi `/health/ready` trả 200; volume trống chạy `init-oisms.sh` ra 44 bảng.
- Frontend: build ba app, `npm test` POS, kiểm tra tay màn cấu hình bằng tài khoản Owner và Cashier.

### 3.3. Giai đoạn (c) (tùy chọn): xác nhận tự động qua webhook ngân hàng hoặc cổng trung gian

**Mục tiêu:** Casso, SePay hoặc dịch vụ tương tự gọi webhook khi tài khoản nhận có tiền vào; OISMS đối soát theo nội dung CK và số tiền rồi tự đánh dấu đã thanh toán. Xác nhận tay vẫn giữ làm đường dự phòng.

**Phần chung của mọi phương án**

- Mã đối soát: không dùng 8 ký tự đầu `RequestKey` (chỉ 32 bit, không được DB đảm bảo duy nhất). Sinh mã riêng, ví dụ `OIS` + 8–10 ký tự chữ số, có unique index theo `(tenant_id, code)`. Ngân hàng có thể bỏ dấu cách hoặc thêm chữ vào nội dung, nên khi đối soát chỉ tìm mã trong chuỗi bằng regex.
- Bảng `bank_transactions`: `tenant_id`, `provider`, `provider_txn_id` (unique theo provider), `payment_account_id`, `amount`, `content`, `transacted_at`, `raw_payload_hash`, `status` (`Unmatched`/`Matched`/`Ignored`/`Duplicate`), `matched_payment_id`, `received_at`.
- Webhook `POST /api/webhooks/bank/{provider}`: không có JWT nên chạy trong `OpenPrivilegedScope` như webhook sàn. Xác thực bằng secret/HMAC của nhà cung cấp (secret theo tenant, lưu ở `payment_accounts` hoặc bảng tích hợp; không ghi log secret). **Tenant xác định từ tài khoản nhận** (BIN + số TK → `payment_accounts`), không từ trường nào trong body. Trả 200 cho giao dịch trùng `provider_txn_id` (idempotent), không xử lý lại.
- Đối soát: khớp mã và **số tiền đúng bằng** số phải thu thì `Matched`; thiếu, thừa, không có mã, mã của đơn đã hủy hoặc hết hạn thì `Unmatched`, chờ Owner xử lý tay (hoàn tiền nằm ngoài hệ thống).
- Sự kiện SignalR `PaymentSucceeded` tới nhóm chi nhánh, phát **sau commit**; POS hiện "Đã nhận tiền" và in hóa đơn.
- Bảng mới (`bank_transactions`, và `payment_intents` nếu chọn C1) đều đi theo đúng quy trình migration như giai đoạn (b).

**Hai phương án về tồn kho (cần chốt, xem Q8)**

| | C1: Ý định thanh toán, chưa đụng tồn | C2: Giữ chỗ khi chờ thanh toán |
| --- | --- | --- |
| Luồng | POS tạo `payment_intent` (`Pending`, `requestKey`, số tiền, mã, `expires_at`), không đổi tồn. Webhook đánh dấu intent `Paid`. POS nhận sự kiện rồi gọi checkout như hiện nay với cùng `requestKey`; server kiểm tra intent đã `Paid` và số tiền khớp. | Checkout QR tạo đơn `Reserved` (tăng `Reserved`, chưa ghi sổ) + `Payment Pending`. Webhook (hoặc thu ngân) xác nhận: trừ `OnHand`, ghi `OUT`, chụp giá vốn, `Payment Succeeded`, đơn `Completed`. Hết hạn: hủy, chỉ giảm `Reserved`, không ghi `IN`. |
| Ưu | Lõi checkout và FR-POS-04 giữ nguyên; ít đụng transaction. | Đúng máy trạng thái và quy tắc giữ chỗ; khách đã trả tiền thì chắc chắn có hàng. |
| Nhược | Trong lúc chờ, hàng có thể bị bán hết; khách đã trả tiền mà checkout báo hết hàng, phải hoàn tiền tay. | Đổi FR-POS-04 cho đơn QR; cần job hết hạn trong khi `Hangfire:Enabled` mặc định `false` (phải bật hoặc hết hạn lười khi đọc); thêm luồng xác nhận chạm transaction, khóa, sổ cái. |
| Rủi ro đồng thời | Webhook và checkout cùng lúc trên một intent: khóa `FOR UPDATE` trên intent. | Webhook, thu ngân bấm tay và job hết hạn tranh nhau một đơn: khóa đơn rồi tới tồn **theo đúng thứ tự** của `OrderService.Expiry`/luồng xác nhận hiện có; chỉ một bên thắng, bên còn lại thấy trạng thái đã đổi. Tiền về sau khi đơn đã hết hạn thì `Unmatched`. |

Đề xuất: **C2** nếu nhóm muốn đúng nghiệp vụ tồn kho, **C1** nếu muốn rủi ro triển khai thấp. Với đồ án, C1 kèm cảnh báo "hàng chưa được giữ" trên màn POS là phương án ít rủi ro nhất.

**File sẽ đổi (ước lượng)**

- DB: 1–2 file migration mới, `oisms_schema.sql`, `RequiredTables`, số bảng trong `init-oisms.sh`.
- Application: `Features/BankPayments/` (đối soát, service webhook), sửa `PosCheckoutService` (C1: kiểm tra intent; C2: nhánh tạo đơn `Reserved` và luồng xác nhận), `OrderService.Expiry` (C2).
- Infrastructure: xác thực chữ ký, cấu hình EF, job hết hạn (C2), thêm khóa vào `SqlLockService` nếu cần.
- WebApi: `BankWebhooksController`, sự kiện trong `NotificationHub`.
- Frontend: trạng thái "Chờ thanh toán" và đếm ngược ở hai POS (vẫn giữ guard: hết giờ không phải là đã trả); màn "Giao dịch ngân hàng chưa khớp" cho Owner; app giả lập (`marketplace-simulator`) thêm tab bắn webhook ngân hàng để demo không cần tiền thật.

**Cách test (bắt buộc PostgreSQL thật vì chạm transaction và đồng thời)**

- Cùng `provider_txn_id` gửi song song 10 lần: đúng 1 `Matched`, phần còn lại `Duplicate`, một `Payment Succeeded`, (C2) một dòng `OUT` cho mỗi SKU.
- Webhook và thu ngân xác nhận tay cùng lúc; webhook và job hết hạn cùng lúc (C2): không âm `Available`, không ghi sổ hai lần, `Reserved` về đúng.
- Sai chữ ký: 401, không ghi gì. Tài khoản nhận không thuộc tenant nào: `Ignored`. Body có `tenantId` giả: bị bỏ qua.
- Thiếu/thừa tiền, sai mã, tiền về sau khi hết hạn: `Unmatched`, đơn không đổi.
- Idempotency checkout (C1): cùng key + intent `Paid` replay trả kết quả cũ; intent chưa `Paid` bị từ chối với mã lỗi riêng.
- Sandbox: kiểm tra nhà cung cấp có môi trường thử hay không; nếu không, chỉ test bằng payload giả lập ký đúng secret. Webhook thật cần URL public (máy EC2 demo).

## 4. Câu hỏi cần chốt

| # | Câu hỏi | Trạng thái |
| --- | --- | --- |
| Q1 | Làm những giai đoạn nào? | **Đã chốt:** (a)+(b). |
| Q2 | Tài khoản nhận theo tenant hay theo chi nhánh? (M5) | **Đã chốt:** chung cả shop. |
| Q3 | Sửa §3.1 đặc tả màn hình theo code? (M1) | **Đã chốt:** có. |
| Q4 | Ai được bấm "Đã nhận tiền" bằng tay: mọi vai trò bán hàng (gồm Cashier) hay chỉ Owner/Staff? | **Đã chốt:** có, Cashier được bấm như POS PWA hiện nay; đơn ghi lại người xác nhận (thu ngân của đơn). |
| Q5 | Danh sách ngân hàng lấy từ đâu? | **Đã chốt:** gọi `api.vietqr.io/v2/banks`. |
| Q6 | Ảnh QR lấy từ `img.vietqr.io` hay vẽ tại máy? | **Đã chốt:** tiếp tục lấy từ `img.vietqr.io`. |
| Q7 | Lưu tài khoản nhận và nội dung CK vào `payments`? | **Đã chốt:** có; server tự tra tài khoản, client chỉ gửi `TransferReference`. |
| Q8, Q9 | Phương án C1/C2 và nhà cung cấp cho giai đoạn (c). | Tạm gác vì chưa làm (c). |
| Q10 | Giữ nội dung CK `OISMS POS <8 ký tự RequestKey>` hay đổi sang mã riêng có unique index ngay ở (b)? | **Đã chốt** (người dùng để nhóm chọn cách tiện): giữ nguyên cách tạo hiện có trong (b), không thêm cột hay unique index cho mã; chỉ đổi sang mã đối soát riêng khi làm (c). |
| Q11 | Shop có nhiều tài khoản thì lúc thanh toán dùng tài khoản nào: luôn tài khoản mặc định, hay thu ngân được chọn trong danh sách? | **Đã chốt:** thu ngân được chọn trong các tài khoản đang dùng; tài khoản mặc định được chọn sẵn. Quy tắc xem §3.2 "Chọn tài khoản lúc thanh toán". |

## 5. Trình tự triển khai: làm POS trước

Nguyên tắc: làm xong phần POS (cả POS PWA và POS trong Admin) trên frontend trước, **chưa đụng backend**, để có mốc demo được sớm. Nguồn tài khoản nhận được tách thành một lớp riêng (`paymentAccountSource.ts`). Lúc đầu lớp này đọc cấu hình cục bộ; khi backend xong thì chỉ đổi lớp này sang gọi API, không phải sửa lại modal.

Nhánh đề xuất: `feature/OISMS-<id>-pos-vietqr` tách từ `develop`, độc lập với `feature/OISMS-48-shop-integrations-api`. Cần chốt số `<id>`.

### Mốc 1: POS dùng QR với cấu hình cục bộ (chỉ frontend)

| Bước | Việc | File | Xong khi |
| --- | --- | --- | --- |
| P1 | **Tách nguồn tài khoản ở POS PWA.** Thêm kiểu `PaymentAccountOption` (`id`, `bankBin`, `bankName`, `bankShortName`, `accountNumber`, `accountName`, `isDefault`). Thêm `paymentAccountSource.ts` với hàm `loadPaymentAccounts(tenantId)` trả danh sách. Bản tạm đọc envelope **v2 theo tenant** (`{version:2, tenantId, accounts:[...]}`) từ `window.__OISMS_POS_PAYMENT_ACCOUNTS__` hoặc `localStorage` khóa `pos_payment_accounts_v2:<tenant>`, không còn theo chi nhánh (Q2). Bỏ `getPosBankConfig` theo chi nhánh. Không fallback tài khoản mẫu. | `pos-app/src/types/qr.ts`, `pos-app/src/services/qrService.ts`, `pos-app/src/services/paymentAccountSource.ts` (mới) | Thiếu cấu hình thì nút QR khóa như cũ; test cũ chạy được sau khi sửa fixture sang v2. |
| P2 | **Thu ngân chọn tài khoản ở POS PWA (Q11).** Thêm ô chọn khi chọn QR, chọn sẵn tài khoản mặc định, chỉ có một tài khoản thì hiện chữ. `PosQrIntent` thêm `paymentAccountId`; đổi tài khoản thì hỏi xác nhận rồi tạo intent mới với `RequestKey` mới. Đơn đang gửi lại thì khóa ô chọn theo `qrSnapshot`. Hiện tên ngân hàng + 4 số cuối trên modal và phiếu in. | `pos-app/src/components/PaymentModal.tsx`, `pos-app/src/components/ReceiptPrintModal.tsx`, `pos-app/src/services/receiptMapper.ts` | Đóng/mở, reload giữ đúng tài khoản và nội dung CK; đổi tài khoản ra key mới; retry không đổi được tài khoản. |
| P3 | **Chép sang POS Admin.** Chép nguyên văn `qrService.ts`, `types/qr.ts`, `paymentAccountSource.ts`, và `pendingCheckoutStore.ts` (Admin chưa có cơ chế "kết quả chưa rõ", mà QR cần bản chụp người nhận khi retry). Đầu mỗi file ghi "có bản ở cả hai app, sửa thì sửa cả hai". | `admin-dashboard/src/services/qrService.ts`, `paymentAccountSource.ts`, `pendingCheckoutStore.ts`, `admin-dashboard/src/types/qr.ts` (đều mới) | Hai bản giống hệt nhau trừ đường dẫn import. |
| P4 | **Bật QR trong `PaymentModal` của Admin**, ở cả kiểu nhúng trong `PosPage` và kiểu modal: bỏ `disabled`, `ComingSoonBadge`, SVG mô phỏng và cảnh báo mô phỏng; dùng intent, ô chọn tài khoản, nút "Đã nhận tiền" như P2. Thay `oism_pos_intent_${branchId}` bằng khóa có tenant + user và dùng UUID v4, bỏ `req-${Date.now()}`. Truyền `user` (id, tenantId) từ `PosPage.tsx` xuống modal. Phiếu in hiện "Chuyển khoản" và tài khoản nhận. | `admin-dashboard/src/components/pos/PaymentModal.tsx`, `pages/PosPage.tsx`, `components/pos/ReceiptPrintModal.tsx`, `types/pos.ts` | Luồng QR ở `/pos` của Admin chạy giống POS PWA; tiền mặt không đổi. |
| P5 | **Test chạy cả hai bản.** Thêm `pos-app/test-qr-parity.mjs` nạp `qrService.ts` và `paymentAccountSource.ts` của cả hai app (giống `test-cart-units.mjs`), chạy chung các ca: payload EMVCo + CRC, giữ key khi mở lại, đổi tài khoản ra key mới, thiếu cấu hình, sai tenant, envelope v1 cũ bị bỏ qua. Thêm vào script `npm test` của `pos-app`. | `pos-app/test-qr-parity.mjs` (mới), `pos-app/package.json`, `pos-app/test-b03-qr.mjs`, `test-b03-environment.mjs`, `test-support/qr-runtime.cjs` (fixture v2) | `npm test` PASS ở cả hai bản. |
| P6 | **Docs mốc 1.** Sửa `06-pos-terminal-screens.md` §3.1 (Q3) và thêm ô chọn tài khoản vào khung modal; cập nhật `CHECKLIST.md` CS-05, `frontend-coming-soon-va-upgrade.md` (ghi rõ tài khoản vẫn cấu hình cục bộ cho tới mốc 2). | Docs | Docs khớp code. |

Kiểm tra mốc 1:

```bash
npm test --prefix src/frontend/pos-app
npm run lint --prefix src/frontend/pos-app
npm run build --prefix src/frontend/pos-app
npm run build --prefix src/frontend/admin-dashboard
```

Thêm kiểm tra tay trên trình duyệt (POS PWA cổng 5174 và `/pos` của Admin cổng 5173), dùng tài khoản fixture ghi rõ "Dữ liệu kiểm thử": hiện QR, chọn tài khoản, đổi tài khoản, reload, xác nhận tay, đơn và hóa đơn ghi `BankTransfer`. Không chuyển tiền thật. Backend không đổi nên không cần test PostgreSQL mới; vẫn chạy `dotnet test` unit để chắc không vỡ.

Mốc 1 có thể merge vào `develop` riêng. Hạn chế cần ghi rõ: tài khoản cấu hình trên từng trình duyệt, chứng từ chưa lưu tài khoản nhận.

### Mốc 2: backend tài khoản nhận và lưu chứng từ

| Bước | Việc | Xong khi |
| --- | --- | --- |
| B1 | Migration `payment_accounts` và các cột bản chụp trên `payments` theo §3.2; cập nhật `oisms_schema.sql`, `RequiredTables`, số bảng trong `init-oisms.sh` (43 → 44, tính lại nếu nhánh khác merge trước có thêm bảng). | `/health/ready` 200 sau khi chạy migration trên DB có dữ liệu; volume trống ra 44 bảng. |
| B2 | Entity, cấu hình EF, query filter tenant, `Features/PaymentAccounts/`, `PaymentAccountsController` (Owner sửa; Owner/Staff/Cashier đọc `active`). | Integration test PostgreSQL thật: phân quyền, tenant isolation, đặt mặc định song song. |
| B3 | `PosCheckoutService`: nhận `PaymentAccountId`, `TransferReference`; kiểm tra tài khoản thuộc tenant và còn dùng; lưu bản chụp từ DB; thêm vào so khớp idempotency, NULL ở request cũ vẫn replay được. Thêm mã lỗi `PAYMENT_ACCOUNT_NOT_CONFIGURED`, `PAYMENT_ACCOUNT_INACTIVE` vào catalog. | Integration test PostgreSQL thật: lưu bản chụp, 409 khi khác tài khoản, từ chối tài khoản của tenant khác hoặc đã ngừng, replay request cũ. |

### Mốc 3: nối POS vào API và màn cấu hình cho Owner

| Bước | Việc | Xong khi |
| --- | --- | --- |
| C1 | Đổi `paymentAccountSource.ts` ở **cả hai app** sang gọi `GET /api/payment-accounts/active`; bỏ đọc `localStorage`/`window`. Checkout gửi `PaymentAccountId` và `TransferReference`. Test parity cập nhật theo nguồn API (mock fetch). | Hai POS lấy tài khoản từ DB; Owner đổi mặc định thì giao dịch mới thấy ngay, giao dịch đang chờ không đổi. |
| C2 | Màn "Tài khoản nhận tiền" trong Admin chỉ cho Owner: gọi `api.vietqr.io/v2/banks` để chọn ngân hàng, thêm, sửa, đặt mặc định, ngừng dùng, xem trước QR mẫu. Route guard, menu trong `AdminShell.tsx`, route trong `App.tsx`. | Staff/Cashier không thấy menu và bị chặn route; API ngoài lỗi thì báo và cho thử lại. |
| C3 | Docs: API contract, catalog lỗi, ma trận RBAC, `docs/database/README.md`, `CLAUDE.md` (44 bảng), quy trình deploy (bước chạy migration). Đóng CS-05 phần (a)+(b) trong `CHECKLIST.md`. | Docs khớp code. |

Kiểm tra cuối (mốc 2 + 3): `dotnet build`, `dotnet test` unit và integration trên PostgreSQL thật, build ba frontend, `npm test` POS và app giả lập, rồi chạy tay toàn luồng: Owner thêm hai tài khoản → thu ngân ở cả hai POS chọn tài khoản → xác nhận tay → chi tiết đơn và hóa đơn hiện đúng tài khoản. Báo cáo kết quả tách PASS, FAIL, NOT RUN.

> Thứ tự phụ thuộc: P1 → P2 → P3 → P4 → P5 → P6; B1 → B2 → B3; C1 cần B2 + B3 và P1–P5; C2 cần B2. Mốc 2 có thể làm song song với mốc 1 nếu có người làm backend, vì không chạm cùng file.
