# BF-007 — Lưu kết quả đếm tự gán ô trống

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa |
| **Ưu tiên** | P2 |
| **Khu vực** | Kiểm kê |
| **Phát hiện** | 28–29/10/2026 (Tái hiện có kiểm soát) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

## Mô tả lỗi (lúc phát hiện)

**Tái hiện:** Mở kiểm kê chưa đếm → không nhập gì → bấm Lưu kết quả đếm.

**Thực tế:** `handleSaveCounts` thay ô trống bằng `expectedQuantity`, gửi lên server như số thực đếm. Không hỏi người dùng có muốn gán số sổ sách.

**Ảnh hưởng:** phiếu có thể được coi là đã đếm dù chưa đếm thực tế; làm mất ý nghĩa “chưa đếm”.

**Vị trí:** `StockCountDetailDrawer.tsx:handleSaveCounts`, phép gán `countedVal`.

**Bằng chứng:** `count-save-blank`: người dùng chưa nhập nhưng request record có số 8.

**Điều kiện đóng:** giữ khác biệt chưa nhập/0; yêu cầu nhập đủ hoặc có hành động gán sổ sách được xác nhận riêng. Không tự điền khi chỉ bấm Lưu.

## Cách đã sửa

* **Thành phần:** `src/frontend/admin-dashboard/src/components/stockCounts/StockCountDetailDrawer.tsx`
* **Triệu chứng & Nguyên nhân:**
  * Khi mở phiếu kiểm kê chưa đếm, người dùng để trống các ô đếm và bấm "Lưu kết quả đếm". Hệ thống tự ý thay các ô trống bằng số tồn sổ sách và lưu lên server mà không hỏi ý kiến người dùng.
* **Giải pháp khắc phục:**
  * Trong hàm `handleSaveCounts`, phát hiện danh sách các mặt hàng đang để trống số đếm.
  * Hiển thị hộp thoại xác nhận rõ ràng: Nếu người dùng đồng ý mới gán số lượng sổ sách cho các ô trống; nếu từ chối, giữ nguyên trạng thái chưa nhập và không tự ý gửi số liệu lên database.
* **Kiểm thử xác thực:** Pass kịch bản `count-save-blank`: kích hoạt hộp thoại xác nhận khi có ô trống.
