# BF-012 — Tồn trên form chuyển kho không theo kho nguồn

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa |
| **Ưu tiên** | P2 |
| **Khu vực** | Chuyển kho |
| **Phát hiện** | 28–29/10/2026 (Tái hiện có kiểm soát) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

## Mô tả lỗi (lúc phát hiện)

**Tái hiện:** Chọn kho B, kho B có tồn riêng 3 → dropdown SKU vẫn hiện “Tồn: 50” là tổng tenant. Đổi kho không phát sinh lần đọc warehouse balances nào.

**Ảnh hưởng:** người dùng chọn/số lượng dựa trên tồn sai phạm vi; có thể tạo phiếu rồi chỉ phát hiện thiếu hàng khi xuất gửi. Server còn kiểm tra tồn nên chưa khẳng định bán/xuất vượt tồn thành công.

**Vị trí:** `CreateTransferModal.tsx` effect `getSkus`, đổi `fromWarehouseId`, option hiển thị `sku.onHand`; `apiAdapter.ts:getSkus` tổng hợp số dư nhiều chi nhánh.

**Bằng chứng:** `transfer-source-stock`.

**Điều kiện đóng:** tải/hiển thị OnHand, Reserved, Available đúng kho nguồn; thay kho vô hiệu response cũ và các dòng không còn hợp lệ; SKU ngừng hoạt động có xử lý rõ ràng.

## Cách đã sửa

* **Thành phần:** `src/frontend/admin-dashboard/src/components/transfers/CreateTransferModal.tsx`
* **Triệu chứng & Nguyên nhân:**
  * Trong modal tạo chuyển kho, khi người dùng chọn kho xuất (kho nguồn), danh sách chọn mặt hàng (SKU picker) hiển thị tồn tổng của toàn bộ tenant thay vì số tồn tại kho nguồn đó, hoặc fallback về tồn tổng khi SKU không có tồn ở kho nguồn.
* **Giải pháp khắc phục:**
  * Khi `fromWarehouseId` thay đổi, xóa các dòng mặt hàng thuộc kho cũ và gọi API `getWarehouseBalances(fromWarehouseId)` lưu đầy đủ `{ onHand, reserved, available }`.
  * Hiển thị chính xác số tồn khả dụng `available` (và tồn thực tế `onHand`) của kho nguồn đã chọn; nếu SKU không có bản ghi tại kho nguồn thì hiển thị `0` chứ tuyệt đối không fallback về tồn tổng tenant. Vô hiệu hóa các SKU có `isActive === false`.
* **Kiểm thử xác thực:** Pass kịch bản `transfer-source-stock`: dropdown mặt hàng hiển thị chính xác số tồn 3 của kho nguồn.
