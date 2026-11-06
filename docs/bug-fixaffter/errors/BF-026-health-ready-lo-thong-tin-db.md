# BF-026 — `/health/ready` công khai trả tên user database

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ❌ Chưa sửa (chấp nhận cho bản demo) |
| **Ưu tiên** | P3 |
| **Khu vực** | Backend / Deploy |
| **Phát hiện** | 06/11/2026, rà soát cách deploy AWS |

## Mô tả lỗi

Caddy chuyển `/health` và `/health/*` từ Internet vào backend. `/health/ready` trả JSON có `currentUser` (tên login PostgreSQL, ví dụ `oisms_app`), `tablesCount` và khi lỗi có `missingTables`, `errorType`. Không lộ mật khẩu hay chuỗi kết nối, nhưng cho người ngoài biết tên tài khoản DB và cấu trúc schema.

**Vị trí:** `src/backend/src/OISM.WebApi/Program.cs` (`NpgsqlDatabaseReadinessService`, `MapGet("/health/ready")`); `docker/caddy/Caddyfile` (`@backend path ... /health /health/*`).

## Điều kiện đóng

Chọn một trong hai:

- Caddy chỉ cho `/health/live` ra ngoài, `/health/ready` chỉ gọi trong máy (`curl http://localhost` qua container) hoặc từ IP quản trị.
- Response công khai chỉ trả `status`; chi tiết ghi log.

Cập nhật [docs/13-deploy-aws](../../13-deploy-aws/README.md) theo cách đã chọn.
