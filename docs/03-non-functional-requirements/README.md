# 03. ĐẶC TẢ YÊU CẦU PHI CHỨC NĂNG (NON-FUNCTIONAL REQUIREMENTS)

| Thông tin kiểm soát thư mục | Chi tiết |
| :--- | :--- |
| **Mã phân hệ (Module ID)** | `MOD-NFR-03` |
| **Phiên bản hiện hành** | `v1.2.0` |
| **Ngày cập nhật gần nhất** | `2026-11-12` |
| **Nguồn đối chiếu gốc** | `De bai CNPM.pdf` — Mục 3.2.d (Trang 3, 4) |

---

## Danh mục Tài liệu trong Phân hệ `03-non-functional-requirements/`

| STT | Mã tài liệu | Tên file | Mã NFR bao phủ | Phiên bản | Nội dung chính |
| :---: | :---: | :--- | :---: | :---: | :--- |
| **1** | `DOC-NFR-01` | [`01-performance-and-load.md`](./01-performance-and-load.md) | `NFR-PERF-01..02` | `v1.0.0` | Thời gian phản hồi API (`p95 < 200ms`/`500ms`/`2s`) và xử lý chịu tải đồng thời Flash Sale (`50 reqs / 1 SKU`). |
| **2** | `DOC-NFR-02` | [`02-security-and-isolation.md`](./02-security-and-isolation.md) | `NFR-TENANT-01..02`, `NFR-SEC-01` | `v1.0.0` | Cô lập dữ liệu Multi-tenant bằng Global Query Filters, tối ưu Index quy mô lớn, băm mật khẩu BCrypt & JWT. |
| **3** | `DOC-NFR-03` | [`03-reliability-and-acid.md`](./03-reliability-and-acid.md) | `NFR-SEC-02..03` | `v1.0.0` | Tuân thủ giao dịch nguyên tử ACID và bảo vệ tính bất biến chỉ ghi thêm của Sổ cái kho. |
| **4** | `DOC-NFR-04` | [`04-usability-and-pwa.md`](./04-usability-and-pwa.md) | `NFR-USA-01..02` | `v1.0.0` | Độ sẵn sàng `99.5%` và trải nghiệm thanh toán POS dưới 3 bước nhấp chuột / Quét mã vạch. |
| **5** | `DOC-NFR-05` | [`05-maintainability-testing.md`](./05-maintainability-testing.md) | `NFR-MAINT-01..02` | `v1.0.0` | Phân tách 4 tầng Clean Architecture và đảm bảo độ phủ kiểm thử tự động $\ge 80\%$. |

---

## Hiện Trạng Đáp Ứng (đối chiếu 12/11/2026)

Các file trong thư mục đặc tả yêu cầu. Xem [checklist NFR](../../checklist.md#4-yêu-cầu-phi-chức-năng-non-functional-requirements) để đối chiếu từng ngưỡng và bằng chứng.

| Mã | Cách đáp ứng hiện tại | Chưa có / cần lưu ý |
| :--- | :--- | :--- |
| `NFR-PERF-01` | Index `uq_skus_3` (barcode), index lịch sử sổ cái; đã có bộ đo `test-oisms/nfr/measure-latency.mjs` trên 100.000 `order_items` (`oisms_perf` local). | Sau tối ưu FIN-J và `shm_size: 256mb` (21/11/2026): **15/16 ca ĐẠT local** — POS search/barcode, tạo đơn Admin, checkout c=1, `revenue-profit` 4 ca (không lọc, 30 ngày, 1 SKU, 20 SKU) ở c=1 và c=10 đều dưới 1,2 s. Checkout POS 10 quầy bán SKU khác nhau p95 53–58 ms (ĐẠT, [đo](../06-testing-and-qa/evidence/goi-5/NFR-PERF-01/2026-11-21-checkout-multisku.md)); giới hạn đã biết: 10 quầy cùng bán 1 SKU p95 379–577 ms do xếp hàng qua khóa chống bán âm. **CHƯA ĐO TRÊN DEPLOY** ([`2026-11-21-shm256.md`](../06-testing-and-qa/evidence/goi-5/NFR-PERF-01/2026-11-21-shm256.md)). |
| `NFR-PERF-02` | Khóa bi quan `pg_advisory_xact_lock` và `SELECT ... FOR UPDATE`, CHECK `reserved <= on_hand`. Chạy lại `AC06` (50 req tranh 1 đv) trên PostgreSQL local 21/11/2026: **PASS 1/1** (1 thắng, 49 `409`, `available = 0`). | **CHƯA ĐO TRÊN DEPLOY** (`evidence/goi-5/NFR-PERF-02/`). |
| `NFR-TENANT-01..02` | 41 global query filter, tenant guard khi `SaveChanges`, khóa ngoại kép `(tenant_id, id)`. `EXPLAIN (ANALYZE, BUFFERS)` trên `oisms_perf` (100k dòng) xác nhận dùng index (`0.131ms`, `0.161ms`, `34.898ms`). | ĐẠT local (`evidence/goi-5/NFR-TENANT-02/`). **CHƯA ĐO TRÊN DEPLOY**. |
| `NFR-SEC-01` | BCrypt (`BCrypt.Net-Next`), JWT 60 phút + refresh token xoay vòng. `Caddyfile` đã ép `tls { protocols tls1.3 }` và chặn `/health/ready` (`BF-026`), kiểm chứng local. | ĐẠT local (`evidence/goi-5/NFR-SEC-01/`). Bản deploy AWS: **CHƯA ĐO TRÊN DEPLOY**. |
| `NFR-SEC-02` | Checkout POS và xác nhận nhập kho nguyên tử. Đơn online có giao dịch riêng cho từng bước giữ chỗ và xác nhận xuất. | Cần chốt cách diễn giải yêu cầu một giao dịch cho đơn online. |
| `NFR-SEC-03` | Trigger `ledger_append_only`, `/health/ready` kiểm trigger đang bật | — |
| `NFR-USA-01` | Container `restart: always`; đã có script `test-oisms/nfr/uptime-probe/` (lọc khung 07:00–22:00 UTC+7). | **CHƯA ĐO TRÊN DEPLOY** (`evidence/goi-5/NFR-USA-01/`). |
| `NFR-USA-02` | POS quét mã + Enter để thêm hàng, F4 để thanh toán, xác nhận trong 2 bước (`< 3` lần bấm, kiểm qua `test:live`). | ĐẠT phần logic bước; chưa chụp ảnh 3 kích thước do máy dev không có Chromium/Playwright và chưa kiểm thiết bị cầm tay thật (`evidence/goi-5/NFR-USA-02/`). |
| `NFR-MAINT-01` | 4 project Clean Architecture | Nợ đã biết: Application tham chiếu EF Core, một số service còn SQL thuần |
| `NFR-MAINT-02` | Đo lại bản Final 21/11/2026 (Unit 434/434 + Integration PostgreSQL 480/480): Ghi sổ `90.90%`, `ReservationService` `92.51%`, `WacCalculator` `100.00%`, `RevenueProfitCalculationHelper` `89.35%`. | ĐẠT `>= 80%` trên cả 4 nhóm cốt lõi (`evidence/goi-5/NFR-MAINT-02/`). |
