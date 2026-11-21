# 02. Cô Lập Đa Khách Thuê, Khả Năng Mở Rộng & Bảo Mật (Multi-tenancy, Scalability & Security)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-NFR-02` |
| **Phiên bản (Version)** | `v1.1.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-11-06` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `NFR-TENANT-01`, `NFR-TENANT-02`, `NFR-SEC-01` (Trang 4) |
| **Phạm vi áp dụng** | PostgreSQL Schema, EF Core DbContext, JWT Authentication & HTTPS |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả yêu cầu cô lập dữ liệu Multi-tenant, tối ưu hóa chỉ mục hàng triệu bản ghi và mã hóa xác thực. | `INIT-NFR-02` |
| `v1.1.0` | `2026-11-06` | Nhóm dự án OISMS | Ghi chú hiện thực HTTPS trên bản deploy AWS (Caddy + Let's Encrypt). | `OISMS-46` |
| `v1.2.0` | `2026-11-21` | Nhóm dự án OISMS | Bổ sung đặc tả Dịch vụ che giá dùng chung `ICostVisibilityService` (`NFR-SEC-02`) bảo vệ toàn diện dữ liệu tài chính (giá vốn, WAC, lãi gộp) chỉ cho `Owner`. | `OISMS-66-L2-G5` |

---

## 1. Cô Lập Dữ Liệu Đa Khách Thuê & Mở Rộng (`NFR-TENANT-01`, `NFR-TENANT-02`)

| Mã NFR | Tên yêu cầu | Đặc tả kỹ thuật bắt buộc |
| :---: | :--- | :--- |
| **`NFR-TENANT-01`** | **Data Isolation** (Cô lập dữ liệu) | - Áp dụng mẫu **Shared Database, Separate Schema/Tenant ID pattern**.<br/>- Tất cả các bảng dữ liệu vận hành bắt buộc phải có cột `TenantId`.<br/>- Thực thi **EF Core Global Query Filters** tự động trên toàn bộ thực thể `ITenantEntity` để đảm bảo **0% rò rỉ dữ liệu (`Zero data leakage`)** giữa các Tenant. |
| **`NFR-TENANT-02`** | **Scalability** (Khả năng mở rộng) | - Thiết lập Composite Index tối ưu trên `(TenantId, CreatedAt)` và `(TenantId, SKUId)` cho bảng `InventoryLedger` (`inventory_transactions`) và `Orders` (`orders` / `order_items`) nhằm duy trì tốc độ truy vấn ổn định ở quy mô hàng triệu dòng sổ cái.<br/>- **Bằng chứng local (21/11/2026 — `FIN-D`):** `EXPLAIN (ANALYZE, BUFFERS)` trên `oisms_perf` (`100.000` `order_items`, `100.025` dòng sổ cái) xác nhận sử dụng `ix_inventory_transactions_sku_history` (`0.131 ms`), `ix_orders_tenant_created_at` (`0.161 ms`), `ix_order_items_sku_orders` (`34.898 ms`): [`evidence/goi-5/NFR-TENANT-02/`](../06-testing-and-qa/evidence/goi-5/NFR-TENANT-02/2026-11-21-FIN-D.md). |

---

## 2. Mã Hóa & Bảo Mật Xác Thực (`NFR-SEC-01`)

- **Băm mật khẩu (`Password Hashing`):** Mật khẩu người dùng bắt buộc băm một chiều bằng thuật toán **BCrypt** (hoặc Argon2), tuyệt đối không lưu mật khẩu dạng rõ (`Plaintext`).
- **Bảo mật đường truyền:** Giao tiếp API qua giao thức **HTTPS / TLS 1.3**.
- **Quản lý phiên JWT:** Sử dụng **Access Token ngắn hạn (60 phút)** đi kèm cơ chế **Refresh Token an toàn** (có khóa dòng chống race condition khi xoay vòng token).

> **Hiện thực (21/11/2026 — `FIN-E`):** mật khẩu băm bằng BCrypt. `docker/caddy/Caddyfile` đã cấu hình `tls { protocols tls1.3 }` cho mọi site (`ADMIN_DOMAIN`, `POS_DOMAIN`, `SIM_DOMAIN`) và chặn `/health/ready` ra ngoài Internet (`403`, đóng `BF-026`), chỉ mở `/health/live`. Kiểm chứng Caddy local (`*.localhost` + `openssl s_client -tls1_3` thành công, `-tls1_2` bị từ chối): [`evidence/goi-5/NFR-SEC-01/`](../06-testing-and-qa/evidence/goi-5/NFR-SEC-01/2026-11-21-FIN-E.md). **Trên bản deploy AWS:** chưa áp dụng và chưa đo (`CHƯA ĐO TRÊN DEPLOY`). Chi tiết: [`13-deploy-aws/`](../13-deploy-aws/README.md).

---

## 3. Che Giấu Thông Tin Tài Chính Dùng Chung Cấp Hệ Thống (`NFR-SEC-02`, `ICostVisibilityService`)

- **Bảo mật giá vốn và biên lợi nhuận:** Giá mua, giá vốn (WAC), giá trị tồn kho, lãi gộp và biên lợi nhuận gộp là dữ liệu tài chính tối mật, chỉ duy nhất vai trò `Owner` (Chủ cửa hàng) được phép tiếp cận.
- **Dịch vụ che giá dùng chung (`ICostVisibilityService`):**
  - Quyết định quyền xem giá vốn phía server dựa trên claim `Role` trong JWT đã xác thực (`ICurrentUserService`), tuyệt đối không nhận tham số vai trò từ client.
  - Áp dụng đồng bộ và thống nhất xuyên suốt mọi phân hệ: Đơn hàng, Hóa đơn bán lẻ, Truy vết chứng từ, Sổ cái kho, Số dư tồn kho, Phiếu xuất kho 02-VT, Phiếu trả hàng, Chuyển kho, Kiểm kê, Tìm kiếm toàn hệ thống, Báo cáo tài chính, Công cụ Trợ lý AI, Bản in, Tệp xuất Excel và Payload sự kiện thời gian thực SignalR.
  - Toàn bộ các trường nhạy cảm (`costPrice`, `totalCost`, `unitCost`, `averageCost`, `totalCogs`, `grossProfit`, `grossMarginPercentage`, `totalInventoryValue`, v.v.) tự động trả về `null` hoặc bị ẩn với tài khoản `Staff` và `Cashier`.
  - Kiểm thử tích hợp tự động `PostgresCostVisibilityScanIntegrationTests` duyệt đệ quy toàn bộ các API GET bằng tài khoản `Staff` trên cơ sở dữ liệu PostgreSQL thực tế để bảo đảm 0% rò rỉ dữ liệu tài chính.
