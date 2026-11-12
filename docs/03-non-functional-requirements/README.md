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
| `NFR-PERF-01` | Index `uq_skus_3` (barcode), index lịch sử sổ cái | Script đo độ trễ POS `measure-pos-latency.mjs` (Sprint 3) không còn trong `pos-app` sau khi tách POS 08/11/2026; log đo cũ ở `docs/09-personal-reports/phuong/sprint-3/.../evidence/v1.3/`. Chưa đo lại p95 trên bản deploy AWS |
| `NFR-PERF-02` | Khóa bi quan `pg_advisory_xact_lock` và `SELECT ... FOR UPDATE`, CHECK `reserved <= on_hand`. Test 50 webhook tranh 1 đơn vị tồn trên PostgreSQL PASS ngày 12/11/2026. | Chưa có log stress test trên môi trường deploy. |
| `NFR-TENANT-01..02` | 41 global query filter (đếm `HasQueryFilter` trong `AppDbContext`, 11/11/2026), tenant guard khi `SaveChanges`, khóa ngoại kép `(tenant_id, id)` | — |
| `NFR-SEC-01` | BCrypt (`BCrypt.Net-Next`), JWT 60 phút + refresh token xoay vòng. Production: HTTPS do Caddy + Let's Encrypt (TLS 1.2/1.3) | Local chạy HTTP. `Program.cs` còn secret JWT fallback, production bắt buộc `JWT_SECRET` |
| `NFR-SEC-02` | Checkout POS và xác nhận nhập kho nguyên tử. Đơn online có giao dịch riêng cho từng bước giữ chỗ và xác nhận xuất. | Cần chốt cách diễn giải yêu cầu một giao dịch cho đơn online. |
| `NFR-SEC-03` | Trigger `ledger_append_only`, `/health/ready` kiểm trigger đang bật | — |
| `NFR-USA-01` | Container `restart: always` | Bản demo chạy một máy EC2 và có thể bị tắt khi không demo; chưa đo uptime |
| `NFR-USA-02` | POS quét mã + Enter để thêm hàng, F4 để thanh toán tiền mặt/QR. | Chưa có kết quả nghiệm thu số thao tác và thiết bị theo đề. |
| `NFR-MAINT-01` | 4 project Clean Architecture | Nợ đã biết: Application tham chiếu EF Core, một số service còn SQL thuần |
| `NFR-MAINT-02` | Coverage 92,49% / 94,83% đo ở Sprint 2 | Chưa đo lại sau các gói Upgrade |
