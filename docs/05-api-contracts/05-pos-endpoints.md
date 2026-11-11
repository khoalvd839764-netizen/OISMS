# 05. Đặc Tả Hợp Đồng API Bán Hàng Tại Quầy POS (POS Counter Endpoints)

> Frontend 08/11/2026: quầy hiện tại ở `src/frontend/pos-app/src/pages/PosPage.tsx`, service `posApi.ts`; Admin `/pos` chỉ chuyển hướng. Contract backend và alias checkout giữ nguyên.

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-API-05` |
| **Phiên bản (Version)** | `v1.1.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-06` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-POS-01..04`, `NFR-PERF-01` (Trang 3) |
| **Hợp đồng chi tiết Sprint 3** | [`../08-sprints/sprint-3/01-api-contracts-sprint-3.md`](../08-sprints/sprint-3/01-api-contracts-sprint-3.md) |
| **Phạm vi áp dụng** | `PosCatalogController`, `OrdersController` & Ứng dụng `src/frontend/pos-app`, `src/frontend/admin-dashboard` |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.1.0` | `2026-11-06` | Nhóm dự án OISMS | Sửa route quét mã thành `/api/pos/barcode/{barcode}`; POS checkout không qua bước giữ chỗ, đơn lưu `Completed`; ghi rõ nguồn giá theo đơn vị. | `OISMS-46` |
| `v1.0.2` | `2026-10-24` | Đăng Khoa (Backend 1) | Tách biệt hoàn toàn dữ liệu phản hồi quầy thu ngân (`PosReceiptDto`), bảo đảm không để lộ `CostPrice` hay `COGS` cho vai trò `Cashier`. Cập nhật link tương đối. | `SEC-POS-RECEIPT` |
| `v1.0.1` | `2026-10-24` | Đăng Khoa (Backend 1) | Thống nhất cơ chế Dual-Route Mapping cho Checkout POS: Hỗ trợ đồng thời `POST /api/pos/checkout` (chuẩn canonical) và `POST /api/orders/checkout` (alias tương thích ngược cho cả `pos-app` và `admin-dashboard`). Liên kết tới `DOC-CTR-S3-01`. | `SYNC-CHECKOUT-ROUTING` |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả các Endpoints tìm kiếm nhanh, quét Barcode và thanh toán nguyên tử tại quầy POS. | `INIT-API-05` |

---

## 1. Danh Mục Endpoints Quầy Bán Hàng POS (`/api/pos`)

| Method | Endpoint | Quyền (RBAC) | Mã FR | Mô tả & Quy tắc xử lý |
| :---: | :--- | :---: | :---: | :--- |
| `GET` | `/api/pos/search` | `Owner, Staff, Cashier` | `FR-POS-01`, `FR-POS-02` | Tìm kiếm nhanh sản phẩm/SKU theo từ khóa (`keyword`) và chi nhánh (`branchId`). Trả về thông tin SKU, Giá bán lẻ/sỉ và số lượng tồn khả dụng thực tế (`Available = Math.Max(0, OnHand - Reserved)`). Độ trễ `p95 < 200ms`. |
| `GET` | `/api/pos/barcode/{barcode}` | `Owner, Staff, Cashier` | `FR-POS-01`, `FR-POS-02` | Tra cứu chính xác SKU theo mã vạch `EAN-13`/`Code128` kèm tồn khả dụng tại chi nhánh. Độ trễ `p95 < 200ms`. |
| `POST` | `/api/pos/checkout`<br/>*(Alias: `/api/orders/checkout`)* | `Owner, Staff, Cashier` | `FR-POS-03`, `FR-POS-04`, `FR-COST-02` | **Thanh toán nhanh tại quầy POS:** Khóa tồn, trừ `OnHand` trực tiếp (không tạo `reservations`), ghi Sổ cái `OUT`, lưu đơn `Completed` + `payments` + `sales_invoices` + Chụp `OrderItem.CostPrice` (WAC snapshot) trong **1 giao dịch CSDL nguyên tử duy nhất**. Hỗ trợ cả 2 đường dẫn tương thích 100%. Phản hồi trả về `PosReceiptDto` chuẩn bán lẻ, **tuyệt đối không để lộ giá vốn (CostPrice/COGS)**. Chi tiết xem [`DOC-CTR-S3-01`](../08-sprints/sprint-3/01-api-contracts-sprint-3.md). |

**Chuyển khoản VietQR (CS-05):**

- `paymentMethod`: `Cash`/`CASH` hoặc `BankTransfer`/`BANK_TRANSFER_QR`/`QR`. QR do thu ngân bấm "Đã nhận tiền" mới gọi checkout; hiển thị hay quét QR không được coi là đã trả tiền.
- Trường tùy chọn `paymentAccountId` (tài khoản đã dùng để sinh QR) và `transferReference` (nội dung chuyển khoản, tối đa 100 ký tự). Server tra tài khoản theo `paymentAccountId` trong tenant của JWT và lưu bản chụp BIN, số tài khoản, tên chủ tài khoản vào `payments`; không lấy số tài khoản do client gửi. Không gửi `paymentAccountId` thì bản chụp để trống, server không tự chọn tài khoản.
- Tài khoản không thuộc tenant: `404 PAYMENT_ACCOUNT_NOT_FOUND`; đã ngừng dùng: `409 PAYMENT_ACCOUNT_INACTIVE`. Kiểm tra trước khi trừ kho; replay cùng `RequestKey` đã thành công vẫn trả kết quả cũ.
- Idempotency: cùng `RequestKey` mà khác `paymentAccountId` hoặc khác `transferReference` (khi cả hai bên đều có) trả `409 IDEMPOTENCY_PAYLOAD_MISMATCH`.
- `receipt` có thêm `transferReference`, `receiverBankBin`, `receiverBankName`, `receiverAccountNumber`, `receiverAccountName`.

## 1a. Tài khoản nhận tiền (`/api/payment-accounts`, CS-05)

| Method | Endpoint | Quyền | Mô tả |
| :---: | :--- | :---: | :--- |
| `GET` | `/api/payment-accounts` | `Owner` | Toàn bộ tài khoản của cửa hàng, kể cả đã ngừng dùng. |
| `GET` | `/api/payment-accounts/active` | `Owner, Staff, Cashier` | Tài khoản đang dùng (mặc định đứng đầu). POS chỉ lấy tài khoản từ endpoint này. |
| `POST` | `/api/payment-accounts` | `Owner` | Thêm tài khoản (`bankBin` 6 số, `bankName`, `bankShortName`, `accountNumber` 6–19 ký tự chữ/số, `accountName`, `isDefault`). Tài khoản đầu tiên tự thành mặc định. Trùng BIN + số TK: `409 PAYMENT_ACCOUNT_DUPLICATE`. Trả `201`. |
| `PUT` | `/api/payment-accounts/{id}` | `Owner` | Chỉ đổi tên ngân hàng, tên viết tắt, tên chủ tài khoản. Gửi BIN hoặc số TK khác: `400 PAYMENT_ACCOUNT_IDENTITY_IMMUTABLE`. |
| `POST` | `/api/payment-accounts/{id}/set-default` | `Owner` | Đặt mặc định; tài khoản đã ngừng: `400 PAYMENT_ACCOUNT_INACTIVE`. Chạy trong transaction có khóa theo tenant; mỗi cửa hàng luôn có tối đa một mặc định. |
| `POST` | `/api/payment-accounts/{id}/deactivate` | `Owner` | Ngừng dùng (không xóa cứng). Nếu là mặc định thì chuyển mặc định sang tài khoản đang dùng mới nhất. |

Tài khoản của tenant khác luôn trả `404 PAYMENT_ACCOUNT_NOT_FOUND`. Thiếu claim user trả `401`.

**Nhiều đơn vị tính (Upgrade 05):**

- Mỗi dòng giỏ có thêm `unitId` tùy chọn; để trống là đơn vị gốc. Hệ số và lượng gốc do backend quyết định, client không gửi được.
- `unitPrice` phải khớp giá lẻ đang hiệu lực của đúng đơn vị, đọc sau khi đã khóa SKU. POS luôn dùng bảng giá `retail`. Sai trả `400 PRICE_MISMATCH` kèm `extensions.currentPrices` liệt kê mọi dòng lệch (`skuId`, `skuCode`, `unitId`, `currentPrice`); chưa có giá trả `422 UNIT_PRICE_NOT_CONFIGURED`. Giá `retailPrice`, `salePrice` khi tra cứu có thể là `null`.
- Đơn vị chưa bật hoặc không cho bán trả `422 UNIT_NOT_ALLOWED`; số lượng sai số lẻ trả `400 INVALID_UNIT_QUANTITY`.
- Cùng SKU khác đơn vị là hai dòng; cùng (SKU, đơn vị) được gộp. Tồn khả dụng kiểm theo tổng lượng gốc của SKU.
- `search`, `barcode` trả thêm `units[]` gồm đơn vị được bán (giá, hệ số, số lẻ). `barcode` quét trúng mã quy cách thì `matchedUnitId` là đơn vị đó; quét mã SKU thì `matchedUnitId = null`. `onHand`, `reserved`, `available` luôn theo đơn vị gốc.
- Hóa đơn (`receipt.items`) có thêm `unitId`, `unitName`.
- **Hóa đơn bán hàng & In lại (Upgrade 02):** `ReceiptNumber` nay chính là số hóa đơn bán hàng `INV-YYYYMMDD-{HEX8}` (thay vì số đơn hàng). Mỗi lần checkout quầy POS thành công sinh duy nhất 1 hóa đơn bán hàng trong cùng giao dịch CSDL với dòng `OUT`. Thu ngân và Quản trị có thể in lại hóa đơn bán lẻ qua `GET /api/sales-invoices/receipt?number={invoiceNumber}` hoặc `GET /api/sales-invoices/{id}/receipt` (không chứa giá vốn, bảo mật RBAC Cashier).
- **Nguồn giá ở client:** giá theo đơn vị phải lấy từ `/api/pos/search`, `/api/pos/barcode/{barcode}` hoặc `/api/sku-unit-prices/effective`; `salePrice` trong `/api/products` có thể rỗng. Client không tự nhân giá theo hệ số. Logic giỏ nằm ở `posCartUnits.ts` (một bản trong `pos-app`, một bản trong `admin-dashboard`).
- **Idempotency:** gửi `requestKey` (GUID) trong body; nếu kèm header `Idempotency-Key` hoặc `X-Request-Key` thì phải trùng `requestKey`, không thì `400 VALIDATION_ERROR`. Khi kết quả chưa rõ (mất mạng), client retry giữ nguyên key.
