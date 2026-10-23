# 03. Cấu Hình Biến Môi Trường & Định Tuyến Nginx SPA (Environment & Nginx Configuration)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-OPS-03` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `De bai CNPM.pdf` — Mục `NFR-SEC-01`, `3.2.e`, `3.2.f` (Trang 4, 5) |
| **Phạm vi áp dụng** | `.env.example`, `docker-compose.yml` & Cấu hình Nginx cho React SPA |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Đặc tả các biến môi trường trọng yếu và cấu hình Nginx SPA Fallback chống lỗi 404 khi F5 trang con. | `INIT-OPS-03` |

---

## 1. Danh Mục Biến Môi Trường Trọng Yếu (`.env.example`)

| Tên biến môi trường | Giá trị mẫu (Development) | Mô tả mục đích |
| :--- | :--- | :--- |
| `POSTGRES_DB` | `oisms_db` | Tên cơ sở dữ liệu PostgreSQL chính. |
| `POSTGRES_USER` / `POSTGRES_PASSWORD` | `oisms_user` / `***` | Thông tin xác thực kết nối PostgreSQL. |
| `JwtSettings__SecretKey` | `Chuỗi khóa bí mật >= 32 ký tự` | Khóa ký số HMAC-SHA256 cho JWT Access Token (`NFR-SEC-01`). |
| `JwtSettings__AccessTokenExpirationMinutes` | `60` | Thời hạn hiệu lực của Access Token là 60 phút theo đúng đề bài (`NFR-SEC-01`). |
| `VITE_API_BASE_URL` | `http://localhost:5000/api` | Địa chỉ kết nối REST API từ Web Admin và Web POS. |

---

## 2. Cấu Hình Nginx SPA Fallback (`try_files`)

Để đảm bảo các ứng dụng Single Page Application (`admin-dashboard` và `pos-app` dùng React Router) không bị lỗi `404 Not Found` khi người dùng tải lại trang (`F5`) tại các đường dẫn sâu như `/login`, `/pos`, `/inventory`, `/403`, Nginx trong container được cấu hình:

```nginx
location / {
    root   /usr/share/nginx/html;
    index  index.html index.htm;
    try_files $uri $uri/ /index.html;
}
```
