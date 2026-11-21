# BF-026 — `/health/ready` công khai trả tên user database

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa ở `Caddyfile` (kiểm chứng local, review `FIN-E` + `FIN-E-fix1` 21/11/2026); có hiệu lực trên bản demo sau khi deploy Caddyfile mới |
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

## Cách sửa & Kiểm chứng (`OISMS-66-FIN-E`, 21/11/2026)

- **Cách sửa:** Giữ nguyên `/health/ready` trong `Program.cs` cho healthcheck nội bộ; cập nhật `docker/caddy/Caddyfile` để chặn `/health/ready` từ bên ngoài (`respond 403`) trên mọi site (`ADMIN_DOMAIN`, `POS_DOMAIN`, `SIM_DOMAIN`), chỉ forward `/health/live`.
- **Kiểm chứng local với `caddy:2.10-alpine`:**
  - `curl -sk -i https://admin.localhost:8443/health/live` → `HTTP/2 200` (`{"status":"Live",...}`).
  - `curl -sk -i https://admin.localhost:8443/health/ready` → `HTTP/2 403` (0 bytes, không lộ `currentUser`/`tablesCount`).
  - `curl http://localhost:5000/health/ready` nội bộ → `200 Healthy`.
  - Bằng chứng: `docs/06-testing-and-qa/evidence/goi-5/NFR-SEC-01/`.
