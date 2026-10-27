# 04. HƯỚNG DẪN TRIỂN KHAI PHẢN HỒI THỊ GIÁC TRÊN GIAO DIỆN (UI FEEDBACK GUIDELINES)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-ERR-04` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực** | `2026-10-25` |
| **Thành phần UI liên quan** | [`ui.tsx`](file:///d:/OISMS/src/frontend/admin-dashboard/src/components/ui.tsx), [`NotificationCenter.tsx`](file:///d:/OISMS/src/frontend/admin-dashboard/src/components/NotificationCenter.tsx), [`PaymentModal.tsx`](file:///d:/OISMS/src/frontend/pos-app/src/components/PaymentModal.tsx) |
| **Tài liệu tham chiếu** | [`01-error-response-spec.md`](./01-error-response-spec.md), [`03-color-system-tokens.md`](./03-color-system-tokens.md) |

---

## 1. Nguyên Tắc Trải Nghiệm Người Dùng (UX Feedback Principles)

Khi xảy ra lỗi hoặc phát sinh biến động trạng thái trong OISMS, hệ thống phải tuân thủ 4 nguyên tắc phản hồi thị giác:
1. **Phản hồi tức thì (< 100ms):** Ngay khi người dùng thao tác hoặc API trả về kết quả, giao diện phải phản hồi ngay thông qua trạng thái nút (Loading), màu sắc (State Color) hoặc âm thanh (tại quầy POS).
2. **Không làm gián đoạn bất hợp lý:** Các cảnh báo thông thường hoặc thành công sử dụng Toast tự biến mất; chỉ dùng Modal chặn tương tác (Blocking Modal) đối với các hành động phá hủy dữ liệu (Xóa vĩnh viễn, Hủy đơn) hoặc lỗi nghiêm trọng (Phiên hết hạn, Xung đột tồn kho).
3. **Chỉ rõ vị trí lỗi:** Khi gửi form không hợp lệ, hệ thống phải đánh dấu màu đỏ trực tiếp tại ô nhập liệu bị sai và tự động cuộn (Auto-scroll/Focus) đến trường lỗi đầu tiên.
4. **Hướng dẫn bước tiếp theo:** Mọi thông điệp lỗi đều phải đi kèm giải pháp khắc phục cụ thể, không bao giờ dừng lại ở việc chỉ báo "Thất bại".

---

## 2. Quy Chuẩn Thông Báo Nổi (Toast Notifications)

### 2.1. Cấu Trúc Khối Toast

```text
┌─────────────────────────────────────────────────────────────┐
│ [Icon]  TIÊU ĐỀ THÔNG BÁO                              [X]  │
│         Nội dung mô tả chi tiết nguyên nhân & hướng dẫn.     │
└─────────────────────────────────────────────────────────────┘
```

* **Vị trí hiển thị:** Góc trên cùng bên phải (`Top-Right`), cách mép trên 24px, cách mép phải 24px. Khi hiển thị nhiều Toast, xếp chồng theo chiều dọc với khoảng cách 8px, Toast mới nhất nằm trên cùng.
* **Kích thước:** Chiều rộng cố định từ `360px` đến `420px`, bo tròn góc `8px`, bóng đổ mềm `box-shadow: 0 4px 16px rgba(0,0,0,0.08)`.

### 2.2. Thời Gian Tồn Tại & Phân Loại Toast

| Cấp độ | Biểu tượng Lucide | Màu sắc áp dụng | Thời gian tự đóng (`Auto-dismiss`) | Hành vi tương tác |
| :--- | :---: | :--- | :---: | :--- |
| **Success** | `<CheckCircle2 size={18} />` | Nền: `#e8f7f0`<br/>Viền: `#bbf7d0`<br/>Chữ: `#0d543a` | `4 giây` | Người dùng có thể bấm nút `[X]` để tắt sớm hoặc rê chuột vào để tạm dừng thời gian đếm ngược. |
| **Info** | `<Info size={18} />` | Nền: `#edf3ff`<br/>Viền: `#bfdbfe`<br/>Chữ: `#1e40af` | `5 giây` | Tự biến mất. Dùng cho thông báo đơn TMĐT mới từ sàn hoặc trạng thái đồng bộ SignalR. |
| **Warning** | `<AlertTriangle size={18} />` | Nền: `#fff5dc`<br/>Viền: `#fae1aa`<br/>Chữ: `#7a4900` | `6 giây` | Dùng cho cảnh báo tồn kho sắp hết, thời gian giữ hàng sắp hết hạn. |
| **Danger** | `<AlertCircle size={18} />` | Nền: `#fff0f1`<br/>Viền: `#f8cdd2`<br/>Chữ: `#8d3540` | **Không tự tắt** (Bắt buộc bấm `[X]`) hoặc `8 giây` | Dùng cho lỗi từ chối thanh toán, bán vượt tồn kho, mất kết nối mạng. |

---

## 3. Quy Chuẩn Báo Lỗi Form Nhập Liệu (Inline Field Validation)

### 3.1. Trạng Thái Ô Nhập Liệu (Input States)
* **Bình thường (Default):** Viền `#e6eaf0`, nền `#ffffff`, màu chữ `#172033`.
* **Đang trỏ (Focus):** Viền `#1769e0`, đổ bóng vòng ngoài `outline: 3px solid rgba(23, 105, 224, 0.22)`.
* **Bị lỗi (Invalid / Error):**
  * Đường viền chuyển sang màu đỏ: `border: 1px solid #c93c48`.
  * Hiệu ứng bóng đỏ: `box-shadow: 0 0 0 3px rgba(201, 60, 72, 0.12)`.
  * Văn bản thông báo lỗi xuất hiện ngay dưới input: Kích thước 12px, font-weight 500, màu `#c93c48`, kèm icon cảnh báo nhỏ phía trước:
    ```html
    <div class="field-error">
      <AlertCircle size={13} />
      <span>Đơn giá bán lẻ không được để trống hoặc âm.</span>
    </div>
    ```

### 3.2. Quy Tắc Kích Hoạt Lỗi (Validation Triggers)
* **Khi Blur (OnBlur):** Kiểm tra tính hợp lệ của trường ngay khi người dùng rời khỏi ô nhập liệu (nếu trường đó đã bị chạm vào - `touched`).
* **Khi Gửi Form (OnSubmit):** Nếu form còn trường lỗi:
  1. Chặn hành động gửi HTTP Request.
  2. Đánh dấu đỏ toàn bộ các trường không hợp lệ.
  3. Tự động `focus()` vào trường lỗi đầu tiên từ trên xuống dưới.
  4. Hiển thị Toast cảnh báo tổng quan: *"Vui lòng kiểm tra lại các trường thông tin chưa hợp lệ."*

---

## 4. Quy Chuẩn Hộp Thoại Xác Nhận Phá Hủy (Destructive Confirmation Dialogs)

Khi người dùng thực hiện các thao tác **không thể đảo ngược** (ví dụ: Hủy đơn hàng, Xóa sản phẩm, Đóng ca kiểm kê cân bằng kho):
1. **Modal Backdrop:** Nền đen mờ 50% (`rgba(17, 27, 46, 0.5)`), khóa cuộn trang (`overflow: hidden`).
2. **Biểu tượng tiêu đề:** Icon cảnh báo đặt trong khối tròn nền đỏ nhạt:
   ```html
   <div class="modal-icon-danger">
     <AlertTriangle size={24} color="#c93c48" />
   </div>
   ```
3. **Màu sắc nút hành động:**
   * Nút Hủy bỏ / Đóng: Nút phụ (`Button variant="secondary"`), màu viền xám, văn bản xám đậm.
   * Nút Xác nhận thực thi: Nút nguy hiểm (`Button variant="danger"`), nền đỏ `#c93c48`, chữ trắng. Khi đang gọi API, hiển thị hiệu ứng xoay spinner và `disabled = true`.

---

## 5. Quy Chuẩn Đặc Thù Tại Quầy Thu Ngân POS (`pos-app`)

Quầy POS đòi hỏi tốc độ cao và môi trường thao tác nhanh của nhân viên thu ngân:
1. **Âm thanh phản hồi (Audio Feedback):**
   * **Quét Barcode thành công:** Phát âm thanh "Beep" ngắn tần số cao (1200Hz - 80ms).
   * **Quét mã vạch không tồn tại (`SKU_NOT_FOUND`):** Phát 2 âm "Beep-Beep" trầm cảnh báo (400Hz - 200ms) kèm Toast đỏ nhấp nháy.
   * **Thanh toán thành công:** Phát âm thanh "Chime" vui tươi (Cash Register sound).
2. **Cảnh báo hết tồn khả dụng tại quầy:**
   * Khi thu ngân cố ý thêm sản phẩm có `Available == 0`:
     * Ngăn chặn đưa vào giỏ hàng.
     * Mở thông báo Toast đỏ: `Sản phẩm "{productName}" đã hết hàng trong kho chi nhánh (Tồn khả dụng = 0).`
     * Viền ô tìm kiếm nhấp nháy đỏ 2 lần (`shake animation`).
3. **Cảnh báo mất kết nối Internet (Offline Mode):**
   * Đổi huy hiệu kết nối trên thanh Header từ màu xanh `<Wifi /> ONLINE` sang màu đỏ nhấp nháy `<WifiOff /> OFFLINE`.
   * Khóa tạm thời nút "Thanh toán Chuyển khoản QR" (vì không thể kiểm tra biến động số dư ngân hàng theo thời gian thực), chỉ cho phép bán lẻ Tiền mặt có lưu trữ tạm tại LocalStorage.
