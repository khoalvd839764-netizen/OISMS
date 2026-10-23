# 02. Quy Trình Tích Hợp Liên Tục CI/CD Với GitHub Actions (CI/CD Pipeline Specification)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-OPS-02` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `3.2.e`, `3.2.g` (Trang 5, 6) |
| **Phạm vi áp dụng** | `.github/workflows/ci.yml` & Quy tắc nhánh Git (`main`, `develop`, `feature/*`) |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả luồng tự động hóa kiểm tra Build và Test trên GitHub Actions CI. | `INIT-OPS-02` |

---

## 1. Cấu Trúc Pipeline Tự Động (`.github/workflows/ci.yml`)

Mỗi khi có sự kiện `push` hoặc tạo `pull_request` vào nhánh `develop` hoặc `main`, GitHub Actions tự động kích hoạt các bước kiểm chứng:

1. **Backend Build & Test Job (`.NET 8 SDK` + `PostgreSQL Service Container`):**
   - Khởi tạo container dịch vụ `postgres:16` sạch trên môi trường Ubuntu runner.
   - Chạy `dotnet restore` và `dotnet build --configuration Release`.
   - Thực thi toàn bộ **Unit Tests & Integration Tests** (`dotnet test`) đối chiếu trực tiếp với PostgreSQL.
2. **Frontend Build Verification Job (`Node.js 20+`):**
   - Kiểm tra cài đặt dependencies (`npm ci`) và biên dịch TypeScript + Vite (`npm run build`) cho cả 2 ứng dụng `src/frontend/admin-dashboard` và `src/frontend/pos-app`.
   - Đảm bảo tương thích đa nền tảng (Windows Local & Linux CI/Docker).
