# 03. ĐẶC TẢ YÊU CẦU PHI CHỨC NĂNG (NON-FUNCTIONAL REQUIREMENTS)

| Thông tin kiểm soát thư mục | Chi tiết |
| :--- | :--- |
| **Mã phân hệ (Module ID)** | `MOD-NFR-03` |
| **Phiên bản hiện hành** | `v1.0.0` |
| **Ngày cập nhật gần nhất** | `2026-10-22` |
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
