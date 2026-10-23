# 02. Kế Hoạch Kiểm Thử Đơn Vị & Tích Hợp (Unit & Integration Testing Guide)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-QA-02` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `NFR-MAINT-02`, `3.2.e` (Trang 4, 5) |
| **Phạm vi áp dụng** | `src/backend/tests/OISM.UnitTests` & `src/backend/tests/OISM.IntegrationTests` |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả phương pháp kiểm thử tự động trên PostgreSQL thật và hướng dẫn đo độ phủ mã nguồn (`Code Coverage`). | `INIT-QA-02` |

---

## 1. Kiến Trúc Bộ Kiểm Thử Tự Động (`194/194 Tests Pass`)

1. **Unit Tests (`OISM.UnitTests` — 103 bài test):**
   - Kiểm thử cô lập các thuật toán tính Giá vốn bình quân (`WAC`), quy tắc ghi Sổ cái, chuẩn hóa số điện thoại `E.164` (`PhoneHelper`), chuẩn hóa thuộc tính JSON SKU (`SkuAttributeHelper`) và các bộ `FluentValidation`.
2. **Integration Tests (`OISM.IntegrationTests` — 91 bài test trên PostgreSQL 16+ thật):**
   - Chạy trực tiếp trên cơ sở dữ liệu PostgreSQL thật (`oisms_test`) với tài khoản runtime không có quyền superuser (`oisms_test_app`) để kiểm chứng:
     - Cô lập đa người thuê (`Multi-tenant Isolation`).
     - Tính nguyên tử ACID và Rollback khi lỗi.
     - Khóa dòng `SELECT ... FOR UPDATE` và `pg_advisory_xact_lock`.
     - Toàn bộ 33+ REST API Endpoints và phân quyền RBAC (`Owner`, `Staff`, `Cashier`).

---

## 2. Lệnh Thực Thi Kiểm Thử & Đo Độ Phủ (`NFR-MAINT-02`)

```powershell
# Chạy toàn bộ Unit Tests & Integration Tests
dotnet test src/backend/OISM.sln --configuration Release

# Chạy kèm thu thập báo cáo Code Coverage (Cobertura)
dotnet test src/backend/OISM.sln --collect:"XPlat Code Coverage"
```

- **Chỉ tiêu đề bài:** Tối thiểu `80%` cho `Inventory Ledger`, `Anti-Oversell Reserve`, `COGS`.
- **Kết quả đạt được tại Sprint 2:** **`92.49%` Line Coverage** trong Integration Tests và **`94.83%`** khi kết hợp Unit Tests.
