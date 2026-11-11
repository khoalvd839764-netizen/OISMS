# BF-014 — Trang chi nhánh/kho nuốt lỗi tải dữ liệu

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa |
| **Ưu tiên** | P2 |
| **Khu vực** | Chi nhánh/kho |
| **Phát hiện** | 28–29/10/2026 (Tái hiện có kiểm soát) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

## Mô tả lỗi (lúc phát hiện)

**Tái hiện:** `getBranches()` hoặc `getWarehouses()` trả `{error:…}`.

**Thực tế:** `loadOrgData` chỉ xét `.data`, không render lỗi. Trang hiện “Chưa có chi nhánh nào khớp bộ lọc” như một kết quả rỗng hợp lệ. Các thao tác bật/tắt cũng bỏ qua kết quả lỗi từ adapter.

**Vị trí:** `pages/OrganizationPage.tsx:loadOrgData/handleToggleBranch/handleToggleWarehouse`.

**Bằng chứng:** `organization-hidden-error` xác nhận nhánh đọc; nhánh toggle được đối chiếu code, chưa gửi mutation thử trên localhost.

**Điều kiện đóng:** tách loading/error/empty; hiển thị ProblemDetails và retry; không nhận bật/tắt thành công khi API trả lỗi.

## Cách đã sửa

* **Thành phần:** `src/frontend/admin-dashboard/src/pages/OrganizationPage.tsx`
* **Triệu chứng & Nguyên nhân:**
  * Khi API đọc danh sách chi nhánh hoặc kho bị lỗi, hàm `loadOrgData` bỏ qua lỗi và hiển thị giao diện "Chưa có chi nhánh nào khớp bộ lọc" như thể dữ liệu rỗng.
* **Giải pháp khắc phục:**
  * Tách biệt rõ ràng 3 trạng thái: Đang tải (`loading`), Có lỗi (`error`), và Rỗng (`empty`).
  * Khi API trả lỗi, hiển thị banner thông báo lỗi chi tiết của backend kèm nút "Thử lại" và ẩn giao diện empty state. Khi thao tác bật/tắt trạng thái chi nhánh/kho thất bại, hiển thị banner lỗi nhưng vẫn giữ nguyên danh sách hiện tại trên màn hình.
* **Kiểm thử xác thực:** Pass kịch bản `organization-hidden-error`: hiển thị đúng banner lỗi và nút retry khi API giả lập trả lỗi.
