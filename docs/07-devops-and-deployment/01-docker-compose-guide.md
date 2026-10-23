# 01. Hướng Dẫn Triển Khai Với Docker Compose (Docker Compose Deployment Guide)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-OPS-01` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `3.2.e`, `3.2.f`, `3.2.g` (Trang 5, 6) |
| **Phạm vi áp dụng** | `docker-compose.yml`, `docker/`, `src/backend/Dockerfile` |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả kiến trúc đóng gói Docker Compose trọn bộ và hướng dẫn khởi chạy hệ thống tức thì. | `INIT-OPS-01` |

---

## 1. Kiến Trúc Dịch Vụ Trong `docker-compose.yml` (`3.2.f`)

Gói cài đặt Demo (`Demo installation package`) đóng gói toàn bộ hệ sinh thái OISMS trong một lệnh khởi chạy duy nhất:

| Tên Service | Công nghệ / Image | Cổng ánh xạ (Host:Container) | Vai trò trong hệ thống |
| :--- | :--- | :---: | :--- |
| **`postgres`** | `postgres:16-alpine` | `5432:5432` | Cơ sở dữ liệu quan hệ PostgreSQL lưu trữ 29 bảng, Sổ cái Append-Only và dữ liệu Multi-tenant. |
| **`backend`** | `.NET 8 Web API` | `5000:8080` | Máy chủ API chính, Swagger UI (`/swagger`), SignalR Hub và Hangfire Worker. Tự động chạy Migration & Seed Data khi khởi động. |
| **`admin-dashboard`** | `React 19 + Nginx Alpine` | `5173:80` | Giao diện cổng đăng nhập tập trung và quản trị Web Admin Dashboard. |
| **`pos-app`** | `React 19 PWA + Nginx Alpine` | `5174:80` | Giao diện bán hàng tại quầy PWA POS độc lập dành cho Thu ngân. |

---

## 2. Lệnh Khởi Chạy & Kiểm Tra Trạng Thái

```powershell
# Khởi tạo và chạy toàn bộ hệ thống ở chế độ chạy nền
docker compose up -d --build

# Kiểm tra trạng thái các container và healthcheck
docker compose ps
```

- **Swagger API Documentation:** `http://localhost:5000/swagger`
- **Web Admin & Unified Login Portal:** `http://localhost:5173`
- **Standalone Web POS PWA:** `http://localhost:5174`
