# 06. ĐẶC TẢ MÀN HÌNH BÁN HÀNG TẠI QUẦY POS (POS TERMINAL SCREENS)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SCR-06` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực** | `2026-10-25` |
| **Đường dẫn URL Routes** | `http://localhost:5174/` (`pos-app`), `/pos` (`admin-dashboard`) |
| **Quyền truy cập (RBAC)** | `Owner`, `Staff`, `Cashier` |
| **Source code Frontend** | [`App.tsx`](file:///d:/OISMS/src/frontend/pos-app/src/App.tsx), [`CartTable.tsx`](file:///d:/OISMS/src/frontend/pos-app/src/components/CartTable.tsx), [`ProductSearch.tsx`](file:///d:/OISMS/src/frontend/pos-app/src/components/ProductSearch.tsx), [`PaymentModal.tsx`](file:///d:/OISMS/src/frontend/pos-app/src/components/PaymentModal.tsx), [`ReceiptPrintModal.tsx`](file:///d:/OISMS/src/frontend/pos-app/src/components/ReceiptPrintModal.tsx) |
| **Controllers Backend** | [`PosCatalogController.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/PosCatalogController.cs), [`PosCheckoutActionHelper.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/PosCheckoutActionHelper.cs), [`OrdersController.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/OrdersController.cs) |

---

## 1. Mục Tiêu & Luồng Nghiệp Vụ

Giao diện **Quầy Thu Ngân Bán Lẻ (POS Counter)** được thiết kế chuyên dụng cho nhân viên thu ngân và bán hàng trực tiếp tại quầy vật lý:
1. **Tốc độ & Trải nghiệm tối thượng:** Hỗ trợ điều khiển 100% bằng phím tắt (`F2` tìm kiếm/quét mã, `F4` thanh toán, `Esc` đóng modal), độ trễ phản hồi tìm kiếm $p95 < 200ms$.
2. **Kiểm tra Tồn khả dụng nghiêm ngặt:** Hiển thị số lượng khả dụng tức thời (`Available = OnHand - Reserved`). Chặn ngay lập tức khi cố ý đưa sản phẩm có tồn khả dụng $\le 0$ vào giỏ hàng.
3. **Thanh toán Nguyên tử (Atomic Checkout):** Thực hiện chuỗi nghiệp vụ `Reserve -> Confirm -> Deduct -> Ledger OUT` và chụp giá vốn trong **1 giao dịch CSDL duy nhất**.
4. **Bảo mật giá vốn:** Dữ liệu hóa đơn trả về (`PosReceiptDto`) **tuyệt đối không để lộ giá vốn (CostPrice/COGS)** cho nhân viên thu ngân.
5. **Cơ chế Dual-Route Mapping:** Tương thích đồng thời 2 đường dẫn `POST /api/pos/checkout` (chuẩn canonical) và `POST /api/orders/checkout` (alias).

---

## 2. Bố Cục Giao Diện Wireframe (ASCII Wireframe)

```text
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ HEADER POS: Cửa Hàng Thời Trang OISMS | Chi nhánh: Q.1 ▼ | Thu ngân: Lê An | 🟢 ONLINE | [⛶ F11]│
├───────────────────────────────────────────────────────────────────┬────────────────────────────┤
│ KHU VỰC TÌM KIẾM & QUÉT MÃ VẠCH (PHÍM TẮT F2):                    │ GIỎ HÀNG BÁN LẺ (CART)     │
│ [ 🔍 [F2] Quét mã vạch hoặc gõ tên SKU, áo polo, jean...        ] │ [ Xóa giỏ ]    3 sản phẩm  │
│ ───────────────────────────────────────────────────────────────── ├────────────────────────────┤
│ KẾT QUẢ GỢI Ý MẶT HÀNG / DANH MỤC NHANH:                          │ 1. Áo Polo Trắng - L       │
│ ┌─────────────────────────┐  ┌─────────────────────────┐         │    250,000 đ x 2 = 500,000đ│
│ │ Áo Polo Trắng - L       │  │ Quần Jean Slimfit - 31  │         │    [ - ] [ 2 ] [ + ]   [🗑]│
│ │ SKU: AO-POLO-TR-L       │  │ SKU: QN-JEAN-X-31       │         │ 2. Giày Sneaker Đen - 42   │
│ │ Barcode: 8935012345685  │  │ Barcode: 8935098765432  │         │    850,000 đ x 1 = 850,000đ│
│ │ Giá: 250,000 đ          │  │ Giá: 480,000 đ          │         │    [ - ] [ 1 ] [ + ]   [🗑]│
│ │ 🟢 Tồn kho: 12 cái      │  │ 🔴 HẾT HÀNG (Tồn: 0)    │         ├────────────────────────────┤
│ └─────────────────────────┘  └─────────────────────────┘         │ Tạm tính:       1,350,000 đ│
│ ┌─────────────────────────┐  ┌─────────────────────────┐         │ Thuế VAT:                0 đ│
│ │ Giày Sneaker Đen - 42   │  │ Áo Thun Basic - Trắng   │         │ Giảm giá:                0 đ│
│ │ SKU: GY-SNK-D-42        │  │ SKU: AO-THUN-BSC-W      │         │ ────────────────────────── │
│ │ Giá: 850,000 đ          │  │ Giá: 150,000 đ          │         │ TỔNG CỘNG:      1,350,000 đ│
│ │ 🟡 Sắp hết: 3 đôi       │  │ 🟢 Tồn kho: 45 cái      │         ├────────────────────────────┤
│ └─────────────────────────┘  └─────────────────────────┘         │ [  F4: THANH TOÁN (PAY)  ] │
└───────────────────────────────────────────────────────────────────┴────────────────────────────┘
```

---

## 3. Các Modal Thao Tác Tại Quầy POS

### 3.1. Modal Thanh Toán Đơn Hàng (Payment Modal - Phím tắt `F4`)
```text
┌────────────────────────────────────────────────────────┐
│ THANH TOÁN ĐƠN HÀNG                   TỔNG: 1,350,000 đ│
├────────────────────────────────────────────────────────┤
│ Phương thức thanh toán:                                │
│ (•) Tiền mặt (Cash)          ( ) Chuyển khoản VietQR   │
│                                                        │
│ Gợi ý mệnh giá tiền mặt khách đưa:                     │
│ [ 1,350,000 đ ]  [ 1,400,000 đ ]  [ 1,500,000 đ ]      │
│ [ 2,000,000 đ ]  [ Nhập số khác: 1,500,000 đ         ] │
│                                                        │
│ TIỀN THỐI LẠI KHÁCH:                         150,000 đ │
│                                                        │
│ Số điện thoại khách hàng (Tích điểm):                  │
│ [ 0988.123.456                                       ] │
│                                                        │
│ [ HỦY BỎ (Esc) ]           [ XÁC NHẬN THANH TOÁN (↵) ] │
└────────────────────────────────────────────────────────┘
```
* **Bảo vệ Idempotency:** Mỗi lần mở Modal thanh toán, hệ thống tạo một `RequestKey` mới dạng GUID duy nhất và gắn vào header `Idempotency-Key` để bảo đảm không trừ tiền hoặc xuất kho hai lần nếu nhân viên bấm nhiều lần.

### 3.2. Modal In Hóa Đơn Nhiệt (Receipt Print Modal)
* Hỗ trợ chuẩn in nhiệt khổ giấy `58mm` (K80 / K58).
* Tự động hiển thị: Tên gian hàng, Chi nhánh, Tên thu ngân, Ngày giờ lập, Chi tiết các mặt hàng, Mã đơn hàng, Mã vạch Barcode để khách trả hàng sau này, Mã QR tra cứu hóa đơn điện tử.

### 3.3. Modal Mở Khóa Ca / Đăng Nhập (Login Modal)
* Tự động xuất hiện khi phiên làm việc của thu ngân hết hạn (`pos-auth-expired`).
* Thu ngân chỉ cần nhập Mật khẩu cá nhân để tiếp tục phiên bán hàng mà không bị mất giỏ hàng đang phục vụ khách.

---

## 4. Bảng Hàm API Backend & Tài Liệu Tham Chiếu

| Method | Endpoint Backend | Hàm xử lý Controller / Service | Tài liệu Hợp đồng API tham chiếu | Mã lỗi liên quan |
| :---: | :--- | :--- | :--- | :--- |
| `GET` | `/api/pos/search` | `PosCatalogController.Search` | [`DOC-API-05 - 05-pos-endpoints.md`](../05-api-contracts/05-pos-endpoints.md#1-danh-mục-endpoints-quầy-bán-hàng-pos-apipos) | `AUTH_UNAUTHORIZED`, `BRANCH_INACTIVE_OR_INVALID` (Xem [`DOC-ERR-02`](../12-error-and-status-codes/02-error-codes-catalog.md)) |
| `GET` | `/api/pos/barcode/{code}` | `PosCatalogController.GetByBarcode` | [`DOC-API-05 - 05-pos-endpoints.md`](../05-api-contracts/05-pos-endpoints.md#1-danh-mục-endpoints-quầy-bán-hàng-pos-apipos) | `SKU_NOT_FOUND`, `SKU_INACTIVE` |
| `POST` | `/api/pos/checkout`<br/>*(Alias: `/api/orders/checkout`)* | `PosCheckoutActionHelper.ExecuteCheckoutAsync` $\rightarrow$ `OrderService.CheckoutPosAsync` | [`DOC-API-05 - 05-pos-endpoints.md`](../05-api-contracts/05-pos-endpoints.md#1-danh-mục-endpoints-quầy-bán-hàng-pos-apipos) | `INSUFFICIENT_STOCK`, `IDEMPOTENCY_PAYLOAD_MISMATCH`, `INSUFFICIENT_CASH_RECEIVED`, `PRICE_MISMATCH`, `NO_ACTIVE_WAREHOUSE`, `INVALID_PAYMENT_METHOD` |
| `GET` | `/api/branches` | `BranchController.GetBranches` | [`DOC-API-01 - 01-auth-endpoints.md`](../05-api-contracts/01-auth-endpoints.md#3-endpoints-quản-lý-chi-nhánh-kho-hàng--nhà-cung-cấp-fr-auth-04) | `AUTH_UNAUTHORIZED` |

---

## 5. Danh Sách Phím Tắt Tiêu Chuẩn Tại Quầy POS

| Phím tắt | Tác vụ thực hiện | Hành vi giao diện |
| :---: | :--- | :--- |
| **`F2`** | Tìm kiếm / Quét mã vạch | Tự động di chuyển con trỏ chuột và bôi đen ô `[ 🔍 Quét mã vạch... ]`. |
| **`F4`** | Thanh toán giỏ hàng | Mở Modal Thanh toán (chỉ kích hoạt khi giỏ hàng có ít nhất 1 sản phẩm). |
| **`Esc`** | Hủy thao tác / Đóng Modal | Đóng Modal thanh toán, đóng Modal in bill hoặc hủy tìm kiếm. |
| **`Enter`** | Xác nhận thanh toán | Kích hoạt nút "Xác nhận thanh toán" khi đang ở Modal thanh toán. |
| **`F11`** | Toàn màn hình | Bật/tắt chế độ toàn màn hình để loại bỏ thanh công cụ trình duyệt. |
