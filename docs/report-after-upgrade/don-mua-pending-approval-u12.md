# Báo cáo sau nâng cấp — Đơn mua PendingApproval, phân bổ chi phí mua hàng và gỡ hóa đơn NCC (Upgrade 12 — Gói 1)

| Thông tin | Chi tiết |
| :--- | :--- |
| **Mã gói công việc** | `OISMS-66-L2-G1` |
| **Ngày thực hiện** | `2026-11-19` |
| **Tài liệu nguồn** | [`docs/Upgrade-recommended/Pedding/12-don-mua-va-cau-hinh-gia.md`](../Upgrade-recommended/Pedding/12-don-mua-va-cau-hinh-gia.md) (Luồng A–C, E, F; Q1–Q14, Q20–Q24; BE-12.1→12.6, FE-12.1, FE-12.2) |
| **Migration SQL** | `docker/postgres/migrations/20261119_purchase_order_pending_approval.sql` |

---

## 1. Tóm tắt thay đổi nghiệp vụ và kiến trúc

1. **Tách đề nghị mua (`Draft`) và chờ duyệt giá (`PendingApproval`):**
   - `Staff` tạo đơn đề nghị mua ở `Draft` chỉ gồm chi nhánh, SKU, đơn vị mua và số lượng; mọi trường `supplierId`, `unitPrice`, phí gửi lên đều bị bỏ qua. `Staff` sửa/hủy được `Draft` của mình và bấm **Gửi duyệt** (`POST /api/purchase-orders/{id}/submit`) để chuyển sang `PendingApproval`. Khi đã ở `PendingApproval`, `Staff` không thể sửa hay hủy (`409 INVALID_STATE_TRANSITION`).
   - `Owner` tạo đơn mua thì vào thẳng `PendingApproval`. Ở `PendingApproval`, `Owner` chọn Nhà cung cấp, ngày đặt hàng (`order_date <= hôm nay`), nhập đơn giá từng dòng, phí vận chuyển (`shipping_fee`), chiết khấu thương mại (`discount_amount`), thuế VAT (`vat_amount`), sửa số lượng hoặc bỏ dòng và lưu nhiều lần.
   - `Owner` có thể **Trả về** (`POST /api/purchase-orders/{id}/return`, bắt buộc lý do) đưa đơn về `Draft` cho `Staff` sửa, **Hủy** (`Cancelled`), hoặc **Duyệt** (`POST /api/purchase-orders/{id}/approve`).

2. **Phân bổ chi phí mua hàng theo VAS 02 lúc duyệt (`Approve`):**
   - Duyệt chỉ cho phép từ `PendingApproval`, kiểm tra khớp phiên bản (`version`, lệch trả `409 PURCHASE_ORDER_VERSION_MISMATCH`), bắt buộc có `supplier_id` và đủ `unit_price` trên mọi dòng (`422 PURCHASE_ORDER_INCOMPLETE_FOR_APPROVAL`), kiểm tra `discount_amount <= subtotal` (`422 PURCHASE_ORDER_DISCOUNT_EXCEEDS_SUBTOTAL`) và xác nhận nếu có dòng giá `0`.
   - Tính cố định `allocated_unit_cost` trên từng dòng: phí vận chuyển cộng vào giá vốn, chiết khấu thương mại trừ khỏi giá vốn theo tỷ lệ `Quantity * UnitPrice`; phần dư làm tròn được dồn vào dòng cuối để tổng phân bổ khớp đúng `Subtotal + ShippingFee - DiscountAmount`. Thuế `vat_amount` chỉ cộng vào tổng thanh toán (`TotalAmount`), không vào giá vốn.

3. **Xác nhận phiếu nhập kho và tính WAC:**
   - Khi xác nhận phiếu nhập (`GoodsReceiptService.ConfirmReceiptAsync`), đơn giá vốn theo đơn vị gốc đưa vào `WacCalculator` và ghi bút toán sổ cái `IN` bằng `allocated_unit_cost / FactorSnapshot`.
   - Gỡ bỏ toàn bộ bước kiểm tra và áp giá từ hóa đơn nhà cung cấp (`U01-B`). Giữ nguyên thứ tự khóa: **phiếu nhập → phiếu mua → tồn SKU (sắp theo `SkuId`)**.

4. **Gợi ý giá mua cho Owner (`GET /api/purchase-orders/price-suggestion`):**
   - Trả về đơn giá mua (`suggestedUnitPrice`) và giá vốn phân bổ (`lastAllocatedUnitCost`) của đơn đã duyệt gần nhất cùng Nhà cung cấp + SKU + Đơn vị mua, kèm WAC hiện tại của chi nhánh nhận hàng (`currentBaseWac` và `currentUnitWac`).

5. **Gỡ API & giao diện Hóa đơn nhà cung cấp (`U01-B`):**
   - Xóa `SupplierInvoicesController`, `SupplierInvoiceService`, `SupplierInvoiceDtos`, đăng ký DI và các component `components/supplierInvoices/*`.
   - Giữ 3 bảng `supplier_invoices`, `supplier_invoice_items`, `purchase_variances` trong schema (đủ 48 bảng) và thu hồi quyền `INSERT, UPDATE, DELETE, TRUNCATE` khỏi `oisms_runtime` / `oisms_app` (chỉ giữ `SELECT`).

6. **Che giá với `Staff` trên đơn mua và phiếu nhập:**
   - Khi `Staff` gọi danh sách hoặc chi tiết đơn mua và phiếu nhập: mọi trường giá, thành tiền, tổng tiền, phí vận chuyển, chiết khấu, VAT, `allocatedUnitCost` đều trả `null`; trên đơn mua ở `Draft` hoặc `PendingApproval`, `supplierId` và `supplierName` cũng trả `null`.
   - Trên giao diện Admin (`PurchaseOrdersPage`, `CreatePurchaseOrderModal`, `PurchaseOrderDetailModal`, `InventoryPage`, `GoodsReceiptList`, `GoodsReceiptDetailView`), `Staff` thực hiện nhận hàng mù (không có cột giá/tổng tiền).

---

## 2. Chuyển đổi dữ liệu cũ khi chạy Migration (`Q12`)

File `docker/postgres/migrations/20261119_purchase_order_pending_approval.sql` được thiết kế idempotent (chạy lặp nhiều lần không lỗi):
- Điền `order_date` cho các đơn mua cũ từ `created_at` theo múi giờ `Asia/Saigon`.
- Điền `allocated_unit_cost = unit_price` cho các dòng của đơn mua đã duyệt trước đây (`Approved`, `PartiallyReceived`, `Received`, `Closed`).
- Tự động chuyển các đơn `Draft` cũ đã có `supplier_id IS NOT NULL` và mọi dòng đều có `unit_price IS NOT NULL` sang trạng thái `PendingApproval`. Các đơn `Draft` chưa có NCC hoặc chưa đủ giá giữ nguyên ở `Draft`.
- Các phiếu nhập `Draft` đang tồn tại sẽ xác nhận theo giá vốn phân bổ của đơn mua; hóa đơn NCC cũ chỉ còn lưu trong DB để tra cứu lịch sử.
