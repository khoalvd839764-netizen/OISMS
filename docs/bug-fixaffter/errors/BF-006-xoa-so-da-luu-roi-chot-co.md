# BF-006 — Xóa số đã lưu rồi chốt có thể xác nhận số cũ

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa |
| **Ưu tiên** | P1 |
| **Khu vực** | Kiểm kê |
| **Phát hiện** | 28–29/10/2026 (Tái hiện có kiểm soát; nhánh PR26-F03) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

## Mô tả lỗi (lúc phát hiện)

**Tái hiện:** Tồn sổ sách 8, số đếm đã lưu 10 → xóa ô đếm → bấm Chốt → đồng ý thông báo “gán bằng tồn sổ sách”.

**Kỳ vọng:** lưu 8 rồi xác nhận, hoặc chặn để người dùng lưu trước.

**Thực tế:** dirty check bỏ qua chuỗi rỗng; lời xác nhận gán số sổ sách không gọi record. Client gọi confirm với số server cũ 10. Probe quan sát **0 record, 1 confirm**.

**Vị trí:** `components/stockCounts/StockCountDetailDrawer.tsx:handleConfirmCount`, đoạn `hasDirtyEdits`, `uncounted` và POST confirm.

**Bằng chứng:** `count-cleared-value`.

**Điều kiện đóng:** xóa giá trị cũng là dirty; lựa chọn gán số sổ sách phải được ghi thành công trước confirm; record lỗi tuyệt đối không confirm. Test số 0, chuỗi rỗng và saved10 → expected8.

## Cách đã sửa

* **Thành phần:** `src/frontend/admin-dashboard/src/components/stockCounts/StockCountDetailDrawer.tsx`
* **Triệu chứng & Nguyên nhân:**
  * Nhân viên kiểm kê nhập số đếm là 10 (tồn sổ sách là 8), đã bấm lưu kết quả đếm (10). Sau đó nhân viên xóa trắng ô đếm (hoặc kèm sửa mặt hàng khác) và bấm "Chốt kiểm kê". Hệ thống hỏi "Có gán bằng tồn sổ sách không?", người dùng chọn "Đồng ý".
  * Lỗi: Nhánh xác nhận gán số sổ sách bỏ qua việc gửi các thay đổi chưa lưu và không gọi `recordCounts` cho ô vừa bị xóa trắng, dẫn đến gọi thẳng API `confirmStockCount` với con số 10 cũ đang lưu trên database.
* **Giải pháp khắc phục:**
  * Kiểm tra và lưu các thay đổi `hasNonBlankDirtyEdits` trước khi xử lý các dòng trống.
  * Khi người dùng đồng ý gán tồn sổ sách cho các ô trống (bao gồm cả ô đã lưu trước đó nhưng vừa bị xóa trắng): Hệ thống bắt buộc thực thi API `recordCounts` với số tồn sổ sách trước, đợi ghi nhận thành công vào database rồi mới thực thi tiếp lệnh `confirmStockCount`. Nếu ghi nhận thất bại, tuyệt đối hủy lệnh chốt.
* **Kiểm thử xác thực:** Pass kịch bản `count-cleared-value`: hệ thống ghi nhận đúng `recordCounts` với số lượng tồn sổ sách (8) trước khi gọi `confirmStockCount`.
