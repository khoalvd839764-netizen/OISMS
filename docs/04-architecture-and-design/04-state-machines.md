# 04. Thiết Kế Máy Trạng Thái Nghiệp Vụ (Business State Machines)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-ARC-04` |
| **Phiên bản (Version)** | `v1.3.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-06` |
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

---

## 1. Máy Trạng Thái Đơn Hàng Đa Kênh (`FR-ORD-02`, `FR-RSE-03`)

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
- Hủy đơn `Draft` chỉ áp dụng cho nháp chưa thanh toán; đơn đã `Confirmed`/`Completed` không hủy được, phải xử lý bằng trả hàng (CS-01, chưa làm).
- Job hết hạn giữ chỗ (`ReservationExpiryJob`, mỗi phút) chỉ chạy khi `Hangfire:Enabled=true`; cấu hình mặc định và Docker Compose đang tắt.

### 1.1. Máy Trạng Thái Phiếu Giữ Chỗ (`Reservation`)

```mermaid
stateDiagram-v2
    [*] --> Active : Reserve thành công (tăng reserved tại kho và chi nhánh)
    Active --> Consumed : Đơn được Confirm (trừ on_hand, ghi OUT)
    Active --> Released : Hủy đơn khi giữ chỗ còn hạn
    Active --> Expired : Hủy đơn / job quét khi expires_at đã qua
```

---

## 2. Máy Trạng Thái Phiếu Nhập Kho Nhà Cung Cấp (`FR-INV-02`, `FR-COST-01`)

```mermaid
stateDiagram-v2
    [*] --> Draft : Lập phiếu nhập nhiều dòng (Chưa tác động kho)
    Draft --> Confirmed : Xác nhận nhập kho (Atomic: Ghi Ledger IN + Tăng on_hand + Tính lại WAC)
    Draft --> Cancelled : Hủy phiếu nháp
```

Từ `OISMS-47-U01A`, xác nhận phiếu nhập yêu cầu phiếu mua còn nhận được và không vượt lượng còn lại.

### 2.1. Máy Trạng Thái Phiếu Mua Hàng (`OISMS-47-U01A`)

```mermaid
stateDiagram-v2
    [*] --> Draft : Staff hoặc Owner lập phiếu
    Draft --> Approved : Owner duyệt (Khóa giá, lượng, đơn vị, hệ số)
    Draft --> Cancelled : Owner hủy
    Approved --> Cancelled : Owner hủy khi chưa nhận hàng
    Approved --> PartiallyReceived : Xác nhận phiếu nhập, chưa đủ
    Approved --> Received : Xác nhận phiếu nhập, đủ mọi dòng
    PartiallyReceived --> Received : Xác nhận phiếu nhập, đủ mọi dòng
    Approved --> Closed : Owner đóng, bắt buộc lý do
    PartiallyReceived --> Closed : Owner đóng phần còn lại, bắt buộc lý do
```

---

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
