# Biên bản kiểm thử E2E giao diện — Chuyển kho 2 bước (`FR-INV-03`) & Kiểm kê (`FR-INV-04`)

- **Ngày thực hiện:** 2026-11-21
- **Task:** `OISMS-66-FIN-H`
- **Commit nền:** `879a1ef`
- **Môi trường:** Docker Compose local (`http://localhost:5173` Admin Dashboard, `http://localhost:5174` POS, `http://localhost:5000` Backend, PostgreSQL 16 `oism_postgres`).
- **Tài khoản vai trò:** `owner@shopa.local` (Owner), `staff@shopa.local` (Staff), `cashier@shopa.local` (Cashier).
- **Phạm vi:** Không thực hiện quét barcode kiểm kê (đã quyết định không làm ở gói này).

## 1. Chứng từ đã tạo trong phiên chạy

| Luồng | Mã phiếu | Phạm vi | Trạng thái cuối |
| --- | --- | --- | --- |
| AC-01 Chuyển kho 2 bước | `TRF-20261121-00003` | `Kho Tổng Quận 1` → `Kho Phụ Quận 1` (`POLO-NAM-DEN-M`, SL `2`) | `Received` (Đã nhận hàng) |
| AC-02 Hủy chuyển kho | `TRF-20261121-00004` | `Kho Tổng Quận 1` → `Kho Phụ Quận 1` (`POLO-NAM-TRANG-L`, SL `1`) | `Cancelled` (Đã hủy) |
| AC-03 Kiểm kê cân kho | `CNT-20261121-00002` | `Kho Tổng Quận 1` (`POLO-NAM-DEN-M` +2, `POLO-NAM-TRANG-L` -2) | `Confirmed` (Đã cân bằng) |
| AC-04 Kiểm kê giữ ở `PendingApproval` | `CNT-20261121-00003` | `Kho Phụ Quận 1` (2 SKU) | `PendingApproval` (Chờ duyệt) |

## 2. Chi tiết từng bước kiểm thử

| AC | Bước | Giá trị kỳ vọng | Giá trị thực tế | Kết quả | Ảnh |
| --- | --- | --- | --- | --- | --- |
| AC-01 | Ghi nhận tồn ban đầu tại `/inventory` | Có tồn theo kho và sổ kho | `Kho Tổng`: `POLO-NAM-DEN-M = 33`, `POLO-NAM-TRANG-L = 32`; `Kho Phụ`: `10` / `10`; Sổ kho: `15` dòng | **PASS** | — |
| AC-01 | Tạo & Xuất kho (`Dispatch`) `TRF-20261121-00003` | Trạng thái `InTransit`; `Kho Tổng` giảm `33 -> 31`; `Kho Phụ` giữ `10`; Sổ kho có dòng `OUT -2` | Trạng thái `InTransit`; `Kho Tổng = 31`, `Kho Phụ = 10`; Sổ kho (16 dòng) có `OUT -2` | **PASS** | [`01-transfer-intransit.png`](./screenshots/01-transfer-intransit.png) · [`02-transfer-out-ledger.png`](./screenshots/02-transfer-out-ledger.png) |
| AC-01 | Xác nhận nhận (`Receive`) `TRF-20261121-00003` | Trạng thái `Received`; `Kho Phụ` tăng `10 -> 12`; Sổ kho có dòng `IN +2` | Trạng thái `Received`; `Kho Phụ = 12`, `Kho Tổng = 31`; Sổ kho (17 dòng) có `IN +2` | **PASS** | [`03-transfer-received.png`](./screenshots/03-transfer-received.png) · [`04-transfer-in-ledger.png`](./screenshots/04-transfer-in-ledger.png) |
| AC-02 | Tạo Draft `TRF-20261121-00004` & Hủy (`Cancel`) | Trạng thái `Cancelled`; tồn kho và sổ kho giữ nguyên | Trạng thái `Cancelled`; tồn `Kho Tổng` (`31`/`32`) và `Kho Phụ` (`12`/`10`) không đổi; Sổ kho vẫn `17` dòng | **PASS** | [`05-transfer-cancelled.png`](./screenshots/05-transfer-cancelled.png) |
| AC-03 | Tạo kiểm kê `CNT-20261121-00002`, nhập lệch tăng (+2) & lệch giảm (-2), gửi duyệt (`Submit`) | Trạng thái `PendingApproval`, khóa số đếm, tồn chưa đổi | Trạng thái `PendingApproval` | **PASS** | [`06-stockcount-pending.png`](./screenshots/06-stockcount-pending.png) |
| AC-03 | Owner phê duyệt (`Confirm`) `CNT-20261121-00002` | Trạng thái `Confirmed`; Sổ kho có `IN +2` và `OUT -2`; tồn khớp `33` / `30` | Trạng thái `Confirmed`; Sổ kho (19 dòng) có `IN +2` (`POLO-NAM-DEN-M`) và `OUT -2` (`POLO-NAM-TRANG-L`); tồn khớp `33` / `30` | **PASS** | [`07-stockcount-confirmed.png`](./screenshots/07-stockcount-confirmed.png) · [`08-stockcount-ledger.png`](./screenshots/08-stockcount-ledger.png) |
| AC-04 | `staff@shopa.local` mở phiếu `PendingApproval` (`CNT-20261121-00003`) | Không được phép duyệt chênh lệch | Hiện cảnh báo chỉ Owner được duyệt, nút `Chờ Owner phê duyệt` disabled | **PASS** | [`09-staff-no-approve.png`](./screenshots/09-staff-no-approve.png) |
| AC-04 | `cashier@shopa.local` vào Admin / `/inventory/transfers` / `/inventory/counts` | Bị chặn khỏi màn kho Admin | Đăng nhập bị redirect sang POS (`5174/login`); truy cập trực tiếp `/inventory/transfers` và `/inventory/counts` bị redirect về `/login` | **PASS** | [`10-cashier-redirect-pos.png`](./screenshots/10-cashier-redirect-pos.png) · [`11`](./screenshots/11-cashier-blocked-transfers.png) · [`12`](./screenshots/12-cashier-blocked-counts.png) |

## 3. Lỗi phát hiện & Giới hạn

- **Lỗi phát hiện:** Không có lỗi chức năng nào trong kịch bản đã chạy.
- **Giới hạn:** Không kiểm thử nhận thiếu chuyển kho (hệ thống hiện tại thực hiện nhận đủ theo phiếu xuất 2 bước `Draft -> InTransit -> Received`).
