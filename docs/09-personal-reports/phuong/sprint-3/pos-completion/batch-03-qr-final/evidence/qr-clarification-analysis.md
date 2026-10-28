# Báo cáo Phân tích & Làm rõ Hiện trạng QR (S3-POS-B03 / T04)

Ngày thực hiện: **28/10/2026**  
Phụ trách: **Phương Lê — Frontend POS**  
Phạm vi: Đối chiếu `T04-AC-01..05` với hợp đồng API backend, mã nguồn backend `OISM.Application`, giao diện POS `PaymentModal.tsx`, và lịch sử PR #24 (`PR24-F04`).

---

## 1. Kết quả Đối chiếu 4 Câu hỏi Cốt lõi

### Câu hỏi 1: QR hiện tại là mô phỏng hay có payload thanh toán thật?
- **Hiện trạng mã nguồn:**
  - Tại `src/frontend/pos-app/src/components/PaymentModal.tsx:420–503`, giao diện hiển thị một thẻ `<svg className="w-48 h-48" viewBox="0 0 200 200">` được vẽ cứng bằng các thẻ `<rect>` hình học giả lập các góc định vị QR và các chấm matrix, ở giữa có chữ "POS".
  - **Không có chuỗi dữ liệu (payload) VietQR thật** (chuẩn EMVCo / NAPAS / VietQR string hay data URL).
  - Ngay dưới hình vẽ có hộp thông báo cảnh báo rõ ràng:
    > *"Mã QR mô phỏng (Tính năng đang phát triển) — Cổng VietQR động và Webhook đối soát ngân hàng đang được đội Backend hoàn thiện. Vui lòng KHÔNG chuyển tiền thật vì hệ thống chưa thể tự động ghi nhận thanh toán."*
- **Nguyên nhân Finding PR24-F04:**
  - Trong báo cáo `S3_POS_REPORT_2026-10-27.md` trước đây đã ghi nhận claim "VietQR động ĐẠT 100%".
  - Reviewer R1 đã bắt lỗi `PR24-F04`: Code thực tế chỉ là SVG mô phỏng và có cảnh báo không chuyển tiền thật, do đó claim hoàn tất 100% là sai lệch so với hiện trạng.
- **Kết luận:** QR hiện tại **100% là MÔ PHỎNG HÌNH HỌC (STATIC SVG)**, chưa có payload mã hóa thanh toán thật.

---

### Câu hỏi 2: Cấu hình ngân hàng/tài khoản lấy từ đâu?
- **Khảo sát Backend OISMS:**
  - Không có bảng CSDL (`banks`, `tenant_bank_accounts`, `branch_payment_configs`), không có Entity hay migration nào trong `src/backend/` lưu thông tin tài khoản ngân hàng thụ hưởng.
  - Không có endpoint API nào (ví dụ `/api/pos/payment-config`, `/api/tenant/bank-account`) để lấy thông tin ngân hàng.
- **Khảo sát Frontend POS:**
  - Không có biến môi trường nào (`VITE_BANK_BIN`, `VITE_BANK_ACCOUNT`) trong `.env` hay `vite.config.ts`.
  - Đối tượng `PosUser`, `Branch`, `Tenant` trong `src/frontend/pos-app/src/types/pos.ts` không chứa trường tài khoản ngân hàng.
- **Kết luận:** **Hệ thống chưa có nguồn cấu hình ngân hàng / tài khoản thụ hưởng**. Chưa có quyết định từ Product Owner hay Backend về việc cấu hình tài khoản thuộc cấp Tenant, cấp Chi nhánh (Branch), hay cấu hình tĩnh toàn hệ thống.

---

### Câu hỏi 3: Số tiền, nội dung và định danh giao dịch được gắn thế nào?
- **Về số tiền (`amount`):**
  - Modal POS tính toán `totalAmount = sum(retailPrice * quantity)`.
  - Khi gửi request `PosCheckoutRequestDto`, `TotalAmount` mang giá trị này. Khi `paymentMethod === 'BANK_TRANSFER_QR'`, `AmountGiven` được backend chấp nhận giá trị `null` hoặc bằng `totalAmount` (`PosCheckoutIntegrationTests.cs:2196-2207`).
- **Về định danh giao dịch (`requestKey`):**
  - Khi thu ngân mở thanh toán và bắt đầu giao dịch, frontend sinh một UUID RFC 4122 hợp lệ làm `requestKey` (`PaymentModal.tsx:131-171`).
  - `requestKey` này được lưu vào `PendingCheckoutRecord` và giữ nguyên bất biến qua các lần retry do lỗi mạng hoặc timeout (tuân thủ `T01-AC-01`, `T01-AC-02`, và `PR24-F01`).
- **Về nội dung chuyển khoản:**
  - Hiện chưa có quy tắc sinh nội dung chuyển khoản chuẩn hóa trong contract.
  - Phân tích nghiệp vụ POS: Vì đơn hàng POS hoàn tất trong 1 transaction nguyên tử duy nhất trên server (`Draft -> Completed`), `OrderNumber` (ví dụ `ORD-20261028-0001`) chỉ sinh ra **sau khi checkout thành công**. Do đó, tại thời điểm khách hàng quét mã QR trên màn hình trước khi thu ngân bấm thanh toán, định danh duy nhất ổn định đã có là **`requestKey`**.
  - Quy tắc nội dung chuyển khoản khuyến nghị chuẩn POS: `OISMS POS <short_request_key>` (ví dụ: `OISMS POS A1B2C3D4`) để dễ dàng đối soát thủ công hoặc tự động.

---

### Câu hỏi 4: Ai xác nhận thanh toán, theo điều kiện nào?
- **Cơ chế Backend hiện hành (`OrderService.cs:141-156`, `:685-698`):**
  - Backend OISMS Sprint 3 **không có Webhook ngân hàng**, không có background worker tích hợp Open Banking / Napas / VietQR để tự động lắng nghe biến động số dư.
  - Khi nhận request `POST /api/pos/checkout` với `PaymentMethod = "BANK_TRANSFER_QR"`, backend xử lý như sau:
    1. Chuẩn hóa `normalizedMethod = "BankTransfer"`.
    2. Kiểm tra tồn kho khả dụng dưới PostgreSQL Advisory Lock.
    3. Trừ tồn kho và ghi sổ cái `OUT`.
    4. Tạo `Order` trạng thái `Completed`.
    5. Tạo bản ghi `Payment` với `Method = "BankTransfer"`, `Status = "Succeeded"`, `Amount = totalAmount`.
    6. Trả về `PosReceiptDto` chuẩn bán lẻ.
- **Ai xác nhận thanh toán tại quầy?**
  - **Thu ngân (Cashier) là người xác nhận:** Thu ngân nhìn thấy tiền đã về tài khoản cửa hàng (qua SMS/App ngân hàng trên điện thoại thu ngân hoặc âm thanh thông báo loa), sau đó bấm nút "Gửi Checkout đơn hàng (F4)" để hoàn tất.
  - Backend là nơi chốt giao dịch nguyên tử khi nhận lệnh từ thu ngân có token hợp lệ (`Cashier`, `Staff`, `Owner`).
- **Ràng buộc an toàn nghiêm ngặt (T04-AC-03):**
  - **Tuyệt đối KHÔNG coi việc hiển thị QR, quét mã QR hoặc hết thời gian đếm ngược (countdown) là thanh toán thành công.**
  - Chỉ khi có hành động xác nhận từ thu ngân và API trả về `200 OK` kèm `receipt snapshot` hợp lệ thì giao dịch mới được coi là thành công và xóa giỏ hàng / pending record.

---

## 2. Danh mục Quyết định & Thông tin Cần Bổ sung (Decision Gate)

Để đưa tính năng VietQR từ mức độc lập sang tích hợp chính thức toàn diện, Product Owner / Tech Lead cần phê duyệt các thông tin sau:

| STT | Hạng mục cần quyết định | Các lựa chọn khả dĩ | Trạng thái hiện tại |
| :---: | :--- | :--- | :---: |
| 1 | **Tài khoản thụ hưởng** | **Phương án A:** Cấu hình tài khoản cố định theo từng Chi nhánh (Branch Settings).<br>**Phương án B:** Cấu hình tài khoản chung theo Tenant (Tenant Settings).<br>**Phương án C:** Cấu hình tĩnh qua biến môi trường (`.env`). | **CHỜ QUYẾT ĐỊNH** (Tạm dùng Test Fixtures cho phần độc lập) |
| 2 | **Chuẩn QR sinh ra** | **Phương án 1:** Chuỗi EMVCo / VietQR Payload dạng chuỗi chuẩn NAPAS 247.<br>**Phương án 2:** Link ảnh QuickLink VietQR (`https://img.vietqr.io/image/...`).<br>**Phương án 3:** Hỗ trợ song song cả hai chuẩn. | **ĐÃ CHUẨN BỊ ĐỘC LẬP** (Module B03 hỗ trợ cả 2 chuẩn) |
| 3 | **Cú pháp nội dung chuyển khoản** | `OISMS POS <requestKey_8_chars>` hoặc `POS <order_ref>` | **ĐÃ THIẾT KẾ ĐỘC LẬP** (Dựa trên `requestKey`) |
| 4 | **Phạm vi nghiệm thu Sprint 3** | Ghi nhận đúng hiện trạng: VietQR có module mã hóa payload và kiểm tra tính hợp lệ dữ liệu, thanh toán tại quầy xác nhận bởi thu ngân; không claim có webhook đối soát tự động từ ngân hàng. | **CHỐT TRONG B03** |

---

## 3. Nguyên tắc Triển khai Phần Độc lập trong B03

1. **Không tự bịa tài khoản thật:** Toàn bộ dữ liệu ngân hàng trong module độc lập phải nằm trong `qr-fixtures.ts` và được gắn nhãn cảnh báo rõ ràng `DỮ LIỆU KIỂM THỬ (TEST FIXTURES ONLY)`.
2. **Bảo toàn Idempotency & Invariance:** Khi retry với cùng `requestKey`, module QR đảm bảo sinh ra cùng nội dung chuyển khoản và cùng mã QR, không thay đổi thông tin định danh giao dịch.
3. **Guard chặn hoàn tất giả:** Module chứa các assertion và guard chặn tuyệt đối việc tự động chuyển trạng thái thành `COMPLETED` do hiển thị, quét hoặc timeout.
4. **Không làm ảnh hưởng B02:** Toàn bộ code module, test và evidence nằm độc lập trong thư mục `batch-03-qr-final/`.
