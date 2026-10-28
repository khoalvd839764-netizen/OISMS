# Báo Cáo Bàn Giao Hoàn Thiện Sản Phẩm Admin (Core Product Handover) — v2.0

- **Trạng thái:** `CORE_READY_FOR_REVIEW`
- **Người thực hiện:** Phúc Hữu / Antigravity
- **Thời gian hoàn thành:** 28/10/2026
- **Baseline Git:** Branch `feature/OISMS-43-orders-transfers`, Commit `f276eeed72782f7272f32a62f4bb61ba761f3e89` (+ working tree uncommitted)
- **Tài liệu tham chiếu:**
  - Kế hoạch: [PLAN.md](../PLAN.md) (mốc 1, C0–C5 và CORE-01..08)
  - Danh mục tồn đọng: [BACKLOG.md](../BACKLOG.md)
  - Handoff v1.4: [v1.4-review.md](../batch-01-contract-orders/reviews/v1.4-review.md)

---

## 1. Môi trường Runtime & Cơ sở Hạ Tầng (C0)

| Thành phần | URL / Port | Provider / Runtime | Trạng thái |
| :--- | :--- | :--- | :--- |
| **Admin Dashboard UI** | `http://localhost:5173` | Docker container `oism_admin_dashboard` (Vite / Nginx) | **HEALTHY** (200 OK) |
| **Backend Web API** | `http://localhost:5000` | Docker container `oism_backend` (.NET 8 Web API) | **HEALTHY** (200 OK tại `/health`) |
| **POS Web Application**| `http://localhost:5174` | Docker container `oism_pos_app` (React PWA) | **HEALTHY** (200 OK) |
| **PostgreSQL Database**| `localhost:5432` | Docker container `oism_postgres` (PostgreSQL 16+) | **HEALTHY** (Database `oisms`) |

### Xử lý Sự cố Runtime Cũ:
1. **Lỗi 503 Service Unavailable (`/health`):** Bảng `shop_integrations` thiếu quyền cho role `oisms_runtime`. Đã thực thi `GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE shop_integrations TO oisms_runtime;` trên CSDL `oisms`. Endpoint `/health` chuyển xanh (Healthy, 30 tables, triggers immutability verified).
2. **Lỗi 405 Method Not Allowed (`GET /api/stock-transfers` & `GET /api/stock-counts`):** Container `oism_backend` chạy image cũ trước khi code controller có `[HttpGet]`. Đã thực hiện `docker compose build backend` và `docker compose up -d --no-deps backend`. Cả 2 endpoint hoạt động chuẩn contract (`401 Unauthorized` khi chưa có token, `200 OK` khi có Bearer token hợp lệ).

---

## 2. Dữ liệu Thử nghiệm & Phân quyền (Fixtures)

Hệ thống kiểm thử sử dụng dữ liệu mẫu có sẵn trong database test `oisms`:
- **Tenant A (`11111111-1111-1111-1111-111111111111` - "Cửa hàng Thời trang ABC"):**
  - **Chủ cửa hàng (Owner):** `owner@shopa.local` / `YourStrongDemoPassword123!`
  - **Nhân viên (Staff):** `staff@shopa.local` / `YourStrongDemoPassword123!`
  - **Thu ngân (Cashier):** `cashier@shopa.local` / `YourStrongDemoPassword123!`
  - **Chi nhánh:** `11111111-0000-0000-0001-000000000001` (Chi nhánh Quận 1)
  - **Kho nguồn (Kho 2):** `c0e91acc-94ad-4261-91be-10d43103d94f`
  - **Kho đích (Kho Tổng Q1):** `11111111-0000-0000-0002-000000000001`
  - **SKU thử nghiệm:** `7b30393c-c78e-4fa1-8e90-461222daa31b` (`SKU-53318` - IPHONE 13)
- **Tenant B (`22222222-2222-2222-2222-222222222222` - "Cửa hàng B"):**
  - **Chủ cửa hàng (Owner):** `owner@shopb.local` / `YourStrongDemoPassword123!`

---

## 3. Bảng Kết Quả Kiểm Thử Khói (CORE-01..08 & C1..C5)

Tất cả 8 mục kiểm chứng đều được thực thi tự động qua test suite `scratch/test_core_suite.cjs` và xác thực giao diện qua Edge Headless `scratch/check_login.cjs`:

| Mã Luồng | Checkpoint | Kết Quả | Mã Chứng Từ / Bằng Chứng Thực Tế | Mô Tả & Quan Sát Nghiệp Vụ |
| :--- | :--- | :---: | :--- | :--- |
| **CORE-01** | C1 | **PASS** | `owner@shopa.local`<br>`staff@shopa.local`<br>`cashier@shopa.local`<br>`owner@shopb.local` | Đăng nhập Owner, Staff thành công nhận JWT. Thử vai trò Cashier truy cập `/api/stock-transfers` và `/api/stock-counts` bị chặn đúng HTTP **403 Forbidden**. Tenant B hoàn toàn cô lập với Tenant A. |
| **CORE-02** | C1 | **PASS** | Branch: `11111111-0000-0000-0001-000000000001`<br>SKU: `SKU-53318` (ID: `7b30393c...`) | Đọc danh mục sản phẩm, biến thể SKU, danh sách chi nhánh và kho hàng thật từ API `/api/products`, `/api/branches`, `/api/warehouses`, `/api/inventory/balances`. |
| **CORE-03** | C2 | **PASS** | Đơn hàng: **`ORD-20261028-5b78a51`**<br>(ID: `12227428-5f68-4b1e-b6cb-5333bb02cc18`) | Vòng đời đơn hàng trực tuyến hoàn chỉnh:<br>1. Tạo đơn & giữ chỗ: trạng thái `Reserved`, Reserved tăng 2, OnHand giữ nguyên 52.<br>2. Duyệt đơn (Confirm): trạng thái `Confirmed`, OnHand trừ 2 (còn 50), Reserved giải phóng (về 6), ghi Ledger OUT và snapshot WAC.<br>3. Hoàn tất (Complete): trạng thái `Completed`. Reload phiên mới xác nhận trạng thái máy chủ lưu chuẩn `Completed`. |
| **CORE-04** | C2 | **PASS** | Đơn hủy: `bcade4f2-5122-474c-9ecc-65c4d41c16b7`<br>Đơn idempotent: `7d51a89a-76a7-43de-9f05-f1c5b089ea53` | 1. Tạo đơn 1 sản phẩm -> Hủy đơn -> trạng thái `Cancelled`, giữ chỗ giải phóng hoàn toàn.<br>2. Đặt hàng vượt tồn kho (số lượng 999,999) -> bị từ chối với HTTP **409 Conflict** (`INSUFFICIENT_STOCK`).<br>3. Gửi lại request cùng `Idempotency-Key` -> API trả về đơn đã tạo (HTTP **200 OK**) với đúng Order ID cũ, không sinh đơn trùng. |
| **CORE-05** | C3 | **PASS** | Phiếu chuyển: **`TRF-20261028-00003`**<br>(ID: `a66d7646-69b2-4fd7-b54d-85e485d95b7d`) | Quy trình chuyển kho 2 bước thật:<br>1. Tạo phiếu: trạng thái `Draft`.<br>2. Xuất gửi (Dispatch): trạng thái `InTransit`, OnHand kho nguồn giảm 3 (từ 44 xuống 41).<br>3. Hủy khi InTransit bị từ chối với HTTP **409 Conflict** (đúng contract bảo vệ hàng đang đi đường).<br>4. Nhận hàng (Receive): trạng thái `Received`, OnHand kho đích tăng 3 (từ 6 lên 9).<br>5. Phiếu Draft khác hủy thành công `Cancelled`. Tenant B truy cập phiếu của Tenant A trả về **404 Not Found**. Không có fake success. |
| **CORE-06** | C4 | **PASS** | Đợt kiểm kê: **`CNT-20261028-00006`**<br>(ID: `2fd5abdb-e28c-4b3d-8910-9166bf7a8b48`) | Quy trình kiểm kê kho thật:<br>1. Tạo đợt kiểm kê: trạng thái `Draft`.<br>2. Ghi nhận số đếm 10, sau đó sửa thành 7 trên server.<br>3. Chốt kiểm kê: trạng thái `Confirmed`, hệ thống sinh chứng từ cân bằng kho điều chỉnh tồn chính xác về 7.<br>4. Dirty check trên Frontend chặn đứng việc sửa giao diện mà quên lưu.<br>5. Đợt Draft khác hủy thành công `Cancelled`. Tenant B bị chặn **404 Not Found**. |
| **CORE-07** | C5 | **PASS** | API: `/api/reports/revenue-profit`<br>`/api/reports/inventory-velocity`<br>`/api/reports/stock-alerts` | Đăng nhập Owner đọc số liệu báo cáo thật từ backend:<br>- Báo cáo Doanh thu & Lợi nhuận gộp trả về 200 OK.<br>- Báo cáo Vận tốc luân chuyển kho & Tổng giá trị tồn kho trả về 200 OK.<br>- Cảnh báo tồn kho tối thiểu trả về 200 OK. |
| **CORE-08** | C5 | **PASS** | Đơn POS: **`POS-20261028-d78036f`**<br>(ID: `5895d1c8-5d8c-4fd9-8323-2838db616663`) | Đồng bộ giữa POS và Admin:<br>1. Thu ngân thanh toán hóa đơn 1 sản phẩm tại quầy qua `POST /api/orders/checkout`.<br>2. Tải lại Admin Dashboard: đơn hàng xuất hiện ngay trên danh sách Đơn hàng của Admin.<br>3. Tồn kho SKU trên Admin giảm ngay 1 đơn vị (từ 16 xuống 15) khi làm mới. |

---

## 4. Các Sửa Đổi Mã Nguồn Trong Đợt (Diffs & Fixes)

1. **`src/frontend/admin-dashboard/src/services/stockTransferService.ts`:**
   - Xóa bỏ toàn bộ mock fallback và nuốt lỗi trong các khối `catch` của `createTransfer`, `dispatchTransfer`, `receiveTransfer`.
   - Ném ngoại lệ thật để giao diện hiển thị lỗi từ ProblemDetails của backend, triệt tiêu hoàn toàn lỗi **Fake Success**.
   - Bổ sung hàm `cancelTransfer(id, reason)` gọi `POST /api/stock-transfers/${id}/cancel`.
2. **`src/frontend/admin-dashboard/src/components/transfers/TransferDetailDrawer.tsx`:**
   - Chuẩn hóa Action Matrix theo đúng hợp đồng nghiệp vụ:
     - Khi trạng thái `Draft`: Hiển thị nút `Xuất gửi hàng (Dispatch)` và `Hủy phiếu (Cancel)`.
     - Khi trạng thái `InTransit`: Chỉ hiển thị nút `Xác nhận Nhận hàng (Receive)`. Bỏ nút `Từ chối nhận (Reject)` do backend không có endpoint reject cho InTransit (gọi `/cancel` sẽ bị lỗi 409).
3. **`src/frontend/admin-dashboard/src/components/stockCounts/StockCountDetailDrawer.tsx`:**
   - Thêm cơ chế **Dirty Check**: Trước khi gọi `handleConfirmCount`, hệ thống so sánh các giá trị đang nhập trong `countedMap` và `costMap` với dữ liệu đã lưu `detail.items`. Nếu có thay đổi chưa lưu, cảnh báo và chặn chốt kiểm kê, bắt buộc người dùng nhấn "Lưu kết quả đếm" trước khi chốt, giải quyết dứt điểm ca lỗi 10->7.
4. **Build & Bundle:**
   - Đã chạy `npm run build` (`tsc -b && vite build`) hoàn tất không có bất kỳ lỗi biên dịch hay type error nào.
   - Container `oism_admin_dashboard` đã được build và deploy lại thành công với image mới nhất.

---

## 5. Hạng Mục Tồn Đọng Chuyển Vào Backlog (Theo Chỉ Đạo v2.0)

Theo định hướng ưu tiên luồng cơ bản chạy thật của kế hoạch v2.0, các hạng mục phụ không chặn flow chính được lưu giữ tại [BACKLOG.md](../BACKLOG.md):

| Mã Backlog | Mức Ưu Tiên | Mô Tả & Điều Kiện Đóng Đầy Đủ |
| :--- | :---: | :--- |
| **B01-E02 (Print Layout)** | `DEFERRED — P1-NEXT` | Hoàn thiện bản in phiếu xuất/giao hàng khổ 80mm và A4, bảo đảm phân quyền không lộ giá vốn WAC cho thu ngân/khách hàng. Hiện tại giữ trạng thái chưa nghiệm thu; chức năng xem chi tiết trên màn hình đáp ứng tra cứu. |
| **PR26-F02 / F07 (Realtime Hub)** | `DEFERRED — P1-NEXT` | Chuẩn hóa toàn diện vòng đời kết nối SignalR (nhóm chi nhánh, chuyển đổi phiên an toàn không rò chuông giữa các tenant). Phiên bản hiện tại khuyến nghị dùng nút Tải lại (Refetch) thủ công để đảm bảo an toàn tuyệt đối dữ liệu đa người thuê. |
| **Mutation Testing & Deep Evidence** | `DEFERRED — P2-LATER` | Bổ sung các bài kiểm thử lỗi ngắt mạng giữa chừng (chaos injection) và tự động hóa đột biến sâu (mutation-sensitive tests) cho luồng first-submit. Các bài kiểm thử tích hợp thực tế đã đủ bảo đảm vận hành. |

---

## 6. Lệnh Tái Kiểm Tra (Reproduction Command)

Người đánh giá (Codex hoặc Người dùng) có thể tái chạy toàn bộ suite kiểm chứng bất cứ lúc nào qua lệnh:
```powershell
node scratch/test_core_suite.cjs
```
Tất cả 8 ca CORE-01 đến CORE-08 sẽ chạy trực tiếp trên CSDL PostgreSQL và trả về kết quả `ALL CORE FLOWS PASS! READY FOR REVIEW.`
