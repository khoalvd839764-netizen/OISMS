# 04. Thiết Kế Máy Trạng Thái Nghiệp Vụ (Business State Machines)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-ARC-04` |
| **Phiên bản (Version)** | `v1.6.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-15` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `FR-ORD-02`, `FR-INV-02..03`, `FR-RSE-02..03` (Trang 2, 3) |
| **Phạm vi áp dụng** | Đơn hàng (`Order`), Giữ chỗ (`Reservation`), Phiếu nhập (`GoodsReceipt`) & Chuyển kho (`StockTransfer`) |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả máy trạng thái chuẩn UML 2.0 cho Đơn hàng, Giữ chỗ, Phiếu nhập và Phiếu chuyển kho 2 bước. | `INIT-ARC-04` |
| `v1.1.0` | `2026-11-02` | Nhóm dự án OISMS | Thêm máy trạng thái Phiếu mua hàng. | `OISMS-47-U01A` |
| `v1.2.0` | `2026-11-03` | Nhóm dự án OISMS | Bổ sung máy trạng thái Đợt kiểm kê (StockCount) và Kỳ kiểm kê (StockCountPeriod). | `OISMS-47-U03` |
| `v1.3.0` | `2026-11-06` | Nhóm dự án OISMS | Đối chiếu với service: thêm máy trạng thái Giữ chỗ, `PendingApproval → Cancelled` của kiểm kê, kỳ kiểm kê bị hủy khi tắt lịch; ghi chú Hangfire mặc định tắt. | `OISMS-46` |
| `v1.4.0` | `2026-11-09` | Nhóm dự án OISMS | Bổ sung máy trạng thái Phiếu trả hàng từ khách (`SalesReturn`, CS-01). | `OISMS-58-CS01-P2` |
| `v1.5.0` | `2026-11-15` | Nhóm dự án OISMS | Bổ sung bảng ranh giới giao dịch theo kênh và ghi chú luồng giữ chỗ kỹ thuật POS (`Draft`). | `OISMS-59-G3A` |
| `v1.6.0` | `2026-11-19` | Nhóm dự án OISMS | Nâng cấp máy trạng thái Đơn mua hàng (`PendingApproval`, `Submit`, `Return`, phân bổ phí theo VAS 02) và gỡ luồng Hóa đơn nhà cung cấp (`U01-B`). | `OISMS-66-L2-G1` |
| `v1.7.0` | `2026-11-21` | Nhóm dự án OISMS | Siết quyền đơn nháp (chỉ người tạo được sửa và gửi duyệt `Submit`, người khác nhận `403 PURCHASE_ORDER_NOT_CREATOR`, Owner xem chỉ đọc và hủy được Q11) và bắt buộc thiết lập đơn mua trước khi duyệt (U12-G6 / OISMS-66-L2-G6). | `OISMS-66-L2-G6` |

---

## 1. Máy Trạng Thái Đơn Hàng Đa Kênh (`FR-ORD-02`, `FR-RSE-03`)

Các entity kiểm tra chuyển trạng thái tại Domain. Hằng số trong `OISM.Domain/Common/DocumentStatuses.cs` giữ nguyên giá trị đang lưu trong DB và trả qua API. Application kiểm tra các điều kiện cần dữ liệu từ cơ sở dữ liệu, như tồn kho khả dụng, quyền duyệt và các khóa giao dịch.

```mermaid
stateDiagram-v2
    [*] --> Draft : Tạo đơn mới (Admin / Webhook / POS)
    Draft --> Reserved : Reserve thành công (Tăng reserved, giảm available)
    Draft --> Cancelled : Không đủ tồn khả dụng / Hủy nháp
    Reserved --> Confirmed : Duyệt đơn / POS Checkout (Trừ on_hand, giảm reserved, ghi Ledger OUT, chụp CostPrice, lập SalesInvoice)
    Reserved --> Cancelled : Nhân viên hủy / job hết hạn giữ chỗ (Giảm reserved, không ghi Ledger IN bù)
    Confirmed --> Completed : Giao hàng thành công / Hoàn tất tại quầy POS
```

- POS checkout (`POST /api/pos/checkout`, `POST /api/orders/checkout`) đi thẳng từ giỏ hàng tới `Completed` trong một transaction: trừ `on_hand`, ghi `OUT`, chụp `OrderItem.CostPrice`, lập `SalesInvoice`.
- `POST /api/orders/{id}/process` (xử lý đóng gói) gọi lại luồng Confirm cho đơn `Reserved`; đơn đã `Confirmed` thì trả nguyên kết quả. Không có trạng thái `Processing` trong DB (CHECK của `orders.status` chỉ có 5 giá trị trên).
- Hủy đơn `Draft` chỉ áp dụng cho nháp chưa thanh toán (luồng giữ chỗ kỹ thuật của `POST /api/inventory/reserve` sinh đơn `POS` ở `Draft` kèm `Reservation` `Active` để thỏa `Order.CanCancel` và `ReservationExpiryJob`); đơn đã `Confirmed`/`Completed` không hủy được, phải xử lý bằng phiếu trả hàng (CS-01, `/api/sales-returns`, PR #41): phiếu trả gắn đơn gốc, không đổi trạng thái đơn.
- Job hết hạn giữ chỗ (`ReservationExpiryJob`, mỗi phút) chỉ chạy khi `Hangfire:Enabled=true`; cấu hình mặc định và Docker Compose đang tắt.

### 1.1. Máy Trạng Thái Phiếu Giữ Chỗ (`Reservation`)

```mermaid
stateDiagram-v2
    [*] --> Active : Reserve thành công (tăng reserved tại kho và chi nhánh)
    Active --> Consumed : Đơn được Confirm (trừ on_hand, ghi OUT)
    Active --> Released : Hủy đơn khi giữ chỗ còn hạn
    Active --> Expired : Hủy đơn / job quét khi expires_at đã qua
```

### 1.2. Ranh Giới Giao Dịch Theo Kênh

| Kênh / Thao tác | Giao dịch 1 | Giao dịch 2 |
| :--- | :--- | :--- |
| **POS** (`POST /api/pos/checkout`, `POST /api/orders/checkout`) | Tạo đơn + giữ chỗ + trừ tồn `on_hand` + ghi sổ `OUT` + chụp `CostPrice` + lập `SalesInvoice` + `Completed` | — |
| **Admin, sàn** (`Admin`, `Shopee`, `TikTok`, `Lazada`) | Tạo đơn (đi qua `Draft -> Reserved` trong cùng giao dịch, không lưu `Draft`) + giữ chỗ (`Active`) | Xác nhận (`Confirm`): trừ `OnHand`, giảm `Reserved`, ghi sổ `OUT`, chụp `CostPrice`, lập `SalesInvoice` -> `Confirmed` |
| **Hủy / hết hạn** (`Cancel`, `ReservationExpiryJob`) | Chỉ giảm `Reserved`, chuyển giữ chỗ sang `Released`/`Expired` và đơn sang `Cancelled`, không ghi bút toán `IN` bù | — |

Đơn online không thể gộp xác nhận vào giao dịch tạo đơn vì `FR-ORD-03` yêu cầu nhân viên duyệt. Mỗi giao dịch tự rollback 100% khi có lỗi.

---

## 2. Máy Trạng Thái Phiếu Nhập Kho Nhà Cung Cấp (`FR-INV-02`, `FR-COST-01`)

```mermaid
stateDiagram-v2
    [*] --> Draft : Lập phiếu nhập nhiều dòng (Chưa tác động kho)
    Draft --> Confirmed : Xác nhận nhập kho (Atomic: Ghi Ledger IN + Tăng on_hand + Tính lại WAC)
    Draft --> Cancelled : Hủy phiếu nháp
```

Xác nhận phiếu nhập yêu cầu phiếu mua còn nhận được (`Approved` hoặc `PartiallyReceived`) và không vượt lượng còn lại. Từ Upgrade 12 (`OISMS-66-L2-G1`), hệ thống bỏ hoàn toàn bước đối chiếu và ghi đè giá từ hóa đơn nhà cung cấp (`U01-B`): giá nhập đưa vào tính `WAC` và dòng sổ cái `IN` luôn lấy từ `allocated_unit_cost / FactorSnapshot` đã chốt cố định trên dòng đơn mua lúc duyệt.

### 2.1. Máy Trạng Thái Phiếu Mua Hàng (`OISMS-47-U01A`, `OISMS-66-L2-G1`)

```mermaid
stateDiagram-v2
    [*] --> Draft : Staff lập đề nghị mua (chỉ SKU, đơn vị, số lượng; không NCC, không giá)
    [*] --> PendingApproval : Owner tự tạo đơn mua (vào thẳng chờ duyệt)
    Draft --> PendingApproval : Staff gửi duyệt (Submit)
    Draft --> Cancelled : Staff (người tạo) hoặc Owner hủy đề nghị nháp
    PendingApproval --> Draft : Owner trả về (Return, bắt buộc lý do)
    PendingApproval --> Approved : Owner duyệt (Approve: khóa phiên bản, chốt NCC, giá & phân bổ phí/chiết khấu)
    PendingApproval --> Cancelled : Owner hủy đơn chờ duyệt
    Approved --> Cancelled : Owner hủy khi chưa nhận hàng
    Approved --> PartiallyReceived : Xác nhận phiếu nhập, chưa đủ
    Approved --> Received : Xác nhận phiếu nhập, đủ mọi dòng
    PartiallyReceived --> Received : Xác nhận phiếu nhập, đủ mọi dòng
    Approved --> Closed : Owner đóng, bắt buộc lý do
    PartiallyReceived --> Closed : Owner đóng phần còn lại, bắt buộc lý do
```

- **Tách vai trò đề nghị và chốt giá (U12-G1 & U12-G6 / OISMS-66):**
  - `Staff` tạo đơn mua ở trạng thái `Draft` (mọi trường `supplierId`, `unitPrice`, phí gửi lên đều bị bỏ qua); **CHỈ người tạo đơn (`CreatedBy == userId`) mới được sửa đơn nháp hoặc bấm `Submit` (`Draft -> PendingApproval`)**; người khác (kể cả `Owner`) cố tình sửa hoặc gửi duyệt sẽ nhận lỗi `403 PURCHASE_ORDER_NOT_CREATOR` (AC-01). `Owner` xem đơn `Draft` của người khác ở chế độ chỉ đọc ("Staff đang soạn — chỉ xem"), vẫn có quyền Hủy (`Cancel`) theo Q11. Đơn do `Owner` tự tạo, nếu bị Trả về thành `Draft` thì `Owner` là người tạo nên sửa và gửi duyệt bình thường. Khi đơn đã sang `PendingApproval`, `Staff` không thể sửa hay hủy (`409 INVALID_STATE_TRANSITION`).
  - `Owner` tạo đơn mua thì vào thẳng `PendingApproval`. Ở `PendingApproval`, `Owner` chọn Nhà cung cấp, ngày đặt hàng (`order_date <= hôm nay`), nhập đơn giá từng dòng, phí vận chuyển (`shipping_fee`), chiết khấu thương mại (`discount_amount`), thuế VAT (`vat_amount`), sửa số lượng hoặc bỏ dòng và lưu nhiều lần qua quy trình "Thiết lập đơn mua" 3 bước (AC-04, AC-05).
  - Khi `Owner` bấm `Approve` (`PendingApproval -> Approved`): bắt buộc gửi `version` (thiếu trả `400 VALIDATION_ERROR`) và kiểm tra khớp (`409 PURCHASE_ORDER_VERSION_MISMATCH`), kiểm tra `approvalReadiness.isReady` (bắt buộc có `supplier_id`, ngày đặt hợp lệ và đủ `unit_price` trên mọi dòng; nếu thiếu trả `422 PURCHASE_ORDER_INCOMPLETE_FOR_APPROVAL`), kiểm tra `discount_amount <= subtotal` (`422 PURCHASE_ORDER_DISCOUNT_EXCEEDS_SUBTOTAL`), xác nhận nếu có dòng giá `0` (`confirmZeroPrice = true`), xác nhận bán lỗ nếu có cấu hình giá thấp hơn WAC (`confirmLossPrice = true`), và tính cố định `allocated_unit_cost` cho từng dòng. Từ `Approved` trở đi, giá mua và giá vốn phân bổ không bao giờ thay đổi.
  - **Cấu hình giá bán (U12-G4 & U12-G6 / OISMS-66):** Ở `PendingApproval`, `Owner` thiết lập cấu hình giá bán cho từng đơn vị bán (`purchase_order_sale_price_configs`) theo các chế độ `KEEP`, `WAC_MARGIN` (tính giá dự kiến = WAC dự kiến ÷ (1 - %), khoảng hợp lệ `0 <= marginPercent < 100`, làm tròn lên bội 1.000đ; trên FE hỗ trợ nhập theo lãi gộp hoặc cộng thêm trên giá vốn), hoặc `FIXED`. Nếu có cấu hình có giá dự kiến nhỏ hơn WAC dự kiến thì khi Approve bắt buộc có cờ `confirmLossPrice = true` (thiếu trả `422 PURCHASE_ORDER_LOSS_PRICE_CONFIRM_REQUIRED`).

---

### 2.2. Ghi chú ngừng sử dụng Máy trạng thái Hóa đơn nhà cung cấp (`U01-B` -> Thay bởi `Upgrade 12`)

Từ `Upgrade 12` (`OISMS-66-L2-G1`, ngày 19/11/2026), luồng hóa đơn nhà cung cấp và đối chiếu ba bên (`SupplierInvoice`, `PurchaseVariance`) đã được gỡ bỏ khỏi tầng API, giao diện và luồng xác nhận phiếu nhập vì trùng lặp với đơn mua hàng. Ba bảng `supplier_invoices`, `supplier_invoice_items`, `purchase_variances` vẫn được giữ trong schema (đủ 48 bảng, nay là 51 bảng) và thu hồi quyền `INSERT, UPDATE, DELETE` của `oisms_app` / `oisms_runtime` để lưu trữ lịch sử chỉ đọc. Trong transaction xác nhận phiếu nhập (`ConfirmReceiptAsync`), thứ tự khóa chuẩn là: **phiếu nhập → phiếu mua → tồn SKU (sắp theo `SkuId`) → giá bán (sắp theo `SkuId`, `UnitId`, `PriceListId`)**. Khi xác nhận phiếu nhập đầu tiên của dòng đơn mua, hệ thống tự động sinh `PriceChangeRequest` (`FROM_PURCHASE_ORDER`, `Approved`) và dòng `SkuUnitPrice` mới trong cùng giao dịch.

## 3. Máy Trạng Thái Chuyển Kho 2 Bước (`FR-INV-03`)

```mermaid
stateDiagram-v2
    [*] --> Draft : Lập phiếu yêu cầu chuyển kho (Branch A -> Branch B)
    Draft --> InTransit : Bước 1 - Xuất kho gửi (Ghi Ledger OUT tại Kho A, giảm on_hand Kho A)
    InTransit --> Received : Bước 2 - Xác nhận nhập tại Kho B (Ghi Ledger IN tại Kho B, tăng on_hand Kho B)
    Draft --> Cancelled : Hủy phiếu khi chưa xuất kho
```

---

## 4. Máy Trạng Thái Đợt Kiểm Kê & Kỳ Kiểm Kê (`FR-INV-04`, `OISMS-47-U03`)

### 4.1. Máy Trạng Thái Đợt Kiểm Kê (`StockCount`)

```mermaid
stateDiagram-v2
    [*] --> Draft : Tạo phiếu kiểm kê (Đột xuất hoặc bắt đầu Kỳ định kỳ)
    Draft --> Counting : Nhân viên ghi số đếm thực tế (Record)
    Counting --> PendingApproval : Nộp phiếu kiểm kê (Submit - Đối soát sequence drift)
    PendingApproval --> Counting : Yêu cầu đếm lại / Đếm lại SKU lệch (RequestRecount / Recount)
    PendingApproval --> Confirmed : Owner phê duyệt chốt kiểm kê (Atomic: Bút toán sổ cái + Cân bằng tồn)
    Draft --> Cancelled : Hủy phiếu kiểm kê nháp
    Counting --> Cancelled : Hủy phiếu khi đang đếm
    PendingApproval --> Cancelled : Hủy phiếu đang chờ duyệt
```

### 4.2. Máy Trạng Thái Kỳ Kiểm Kê Định Kỳ (`StockCountPeriod`)

```mermaid
stateDiagram-v2
    [*] --> Planned : Tác vụ nền Hangfire tự động sinh kỳ theo lịch
    Planned --> InProgress : Bắt đầu đếm kỳ (StartPeriod - Chụp snapshot & tạo phiếu kiểm kê)
    InProgress --> Completed : Phiếu kiểm kê được duyệt chốt (Confirmed)
    InProgress --> Planned : Phiếu kiểm kê bị hủy (Cancelled - Cho phép bắt đầu lại)
    Planned --> Cancelled : Owner tắt lịch kiểm kê (các kỳ Planned trong tương lai)
```

Kỳ kiểm kê được sinh bởi `StockCountPeriodJob` (phút thứ 5 mỗi giờ) khi `Hangfire:Enabled=true`. Khi Hangfire tắt, lịch vẫn lưu được nhưng kỳ mới không tự sinh.

---

## 5. Máy Trạng Thái Phiếu Trả Hàng Từ Khách (`SalesReturn`, `CS-01`)

Áp dụng cho quy trình nhận lại hàng trả từ khách gắn với đơn gốc đã xuất kho (`Confirmed` hoặc `Completed`).

```mermaid
stateDiagram-v2
    [*] --> Draft : Tạo phiếu trả hàng mới (Gắn đơn Confirmed/Completed)
    Draft --> Confirmed : Xác nhận trả hàng (Ghi nhận hoàn tiền, ghi Sổ cái IN, tăng OnHand, tính lại WAC)
    Draft --> Cancelled : Hủy phiếu trả hàng khi còn nháp
```

- Phiếu chỉ được tạo ở trạng thái `Draft`, kiểm tra số lượng trả không vượt số lượng đã mua trừ đi các phiếu đã `Confirmed` trước đó.
- Khi chuyển sang `Confirmed`: kiểm tra lại số lượng dưới khóa giao dịch; ghi tăng `OnHand` chi nhánh và kho; ghi dòng sổ cái `IN` với `unit_cost = OrderItem.CostPrice`; tính lại WAC chi nhánh bằng `WacCalculator`; ghi nhận phương thức và thời điểm hoàn tiền.
- Phiếu đã `Confirmed` không được phép hủy hay chỉnh sửa (Terminal State). Nếu có sai sót nghiệp vụ, xử lý qua phiếu điều chỉnh kho (`InventoryAdjustment`).
- Phiếu `Cancelled` cũng là trạng thái kết thúc (Terminal State).

---

## 6. Máy Trạng Thái Phiếu Đổi Giá Bán (`PriceChangeRequest`, `FR-PROD-04`, `U04`, `U12-G4`)

```mermaid
stateDiagram-v2
    [*] --> Pending : Staff tạo phiếu đổi giá tay (bắt buộc reason_type)
    [*] --> Approved : Owner tạo phiếu đổi giá tay / Sinh tự động từ đơn mua (FROM_PURCHASE_ORDER)
    Pending --> Approved : Owner phê duyệt (Áp dụng giá mới: ghi SkuUnitPrice)
    Pending --> Rejected : Owner từ chối (bắt buộc lý do)
```

- **Lý do bắt buộc (`reason_type`):** Mọi phiếu đổi giá tay bắt buộc có `reason_type` (`SHORTAGE`, `PROMOTION`, `FROM_PURCHASE_ORDER`, `OTHER`; `OTHER` bắt buộc nhập ghi chú chi tiết).
- **Sinh tự động từ đơn mua:** Khi xác nhận phiếu nhập đầu tiên (`ConfirmReceiptAsync`), phiếu đổi giá được tạo thẳng ở trạng thái `Approved` với `reason_type = FROM_PURCHASE_ORDER`, Reason = "Theo đơn mua <PO_NUMBER>", ReviewedBy = người duyệt đơn mua.
- **Xử lý xung đột giá chờ từ đơn mua (Q19):** Khi lập hoặc duyệt phiếu đổi giá tay trùng SKU × đơn vị × bảng giá với cấu hình đơn mua đang chờ áp dụng (`Approved` hoặc `PartiallyReceived`), client bắt buộc chọn hướng giải quyết `poConflictResolution`:
  - `KEEP_MANUAL`: Hủy cấu hình giá trên đơn mua (chuyển sang `KEEP`), ưu tiên giá đổi tay.
  - `APPLY_PO_LATER`: Giữ nguyên cấu hình trên đơn mua, khi nhập kho đơn mua sẽ áp dụng đè lên giá đổi tay.
  - Nếu thiếu: hệ thống từ chối với mã lỗi `409 PRICE_CHANGE_PO_CONFLICT`.
- **Thứ tự khóa giao dịch (Q25):** Khi duyệt phiếu đổi giá tay, lấy khóa `pg_advisory_xact_lock` theo SKU + đơn vị + bảng giá (sắp xếp tăng dần).
