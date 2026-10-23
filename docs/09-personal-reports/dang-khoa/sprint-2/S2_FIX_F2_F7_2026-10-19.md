# BÁO CÁO KỸ THUẬT & BÀN GIAO KHẮC PHỤC F2 & F7 (SPRINT 2) — ĐĂNG KHOA

**Dự án:** OISMS - Omnichannel Inventory & Sales Management System  
**Người thực hiện:** Đăng Khoa (Backend Lead - Persistence, Security & Inventory Ledger)  
**Ngày thực hiện:** 19/10/2026  
**Phạm vi:** Giới hạn nghiêm ngặt trong Sprint 2 (Khắc phục 2 vấn đề **F2** và **F7**)  
**Trạng thái:** **HOÀN THÀNH 100% — CODE, DOCKER & TẤT CẢ TEST ĐỀU ĐẠT CHUẨN**

---

## 1. TỔNG QUAN HAI VẤN ĐỀ ĐƯỢC GIAO KHẮC PHỤC

| Mã | Hạng mục | Vấn đề ban đầu | Giải pháp kỹ thuật đã triển khai | Trạng thái |
|---|---|---|---|---|
| **F2** | Backend / Inventory Reservation | Client gọi `POST /api/inventory/reserve` không truyền `OrderId`/`OrderItemId`. Nếu mất kết nối mạng sau commit và retry, backend tự tạo thêm Order và Reservation mới, tăng lặp `Reserved`. | Bổ sung cơ chế **Idempotency** dựa trên `RequestKey` / header `Idempotency-Key` / `X-Request-Key`. Sử dụng PostgreSQL Advisory Lock `pg_advisory_xact_lock(tenantId/request_key/key)` chống race condition; tận dụng cột `orders.request_key` và ràng buộc `uq_orders_3 (tenant_id, request_key)` có sẵn; trả lại kết quả cũ khi trùng payload; từ chối 409 Conflict khi khác payload hoặc reservation đã hết hạn; cách ly đa tenant tuyệt đối. | **ĐÃ HOÀN THÀNH & PASS 100% TEST** |
| **F7** | Docker / Nginx SPA Fallback | Khi truy cập trực tiếp URL con (`/login`, `/inventory`, `/products`, `/403`) hoặc F5 trên trình duyệt, Nginx trả về `404 Not Found` do dùng cấu hình mặc định thiếu fallback SPA. File tĩnh không tồn tại cần trả 404 thật chứ không trả file HTML giả làm JS/CSS. | Tạo file cấu hình `nginx.conf` cho cả `admin-dashboard` và `pos-app` với `try_files $uri $uri/ /index.html;`, đồng thời chặn riêng cụm static asset (`/assets/` và phần mở rộng `.js`, `.css`, `.png`,...) trả đúng `404 Not Found` khi file không tồn tại. Tích hợp `COPY nginx.conf` vào cả hai `Dockerfile` và rebuild image. | **ĐÃ HOÀN THÀNH & PASS 100% KIỂM THỬ CURL** |

---

## 2. CHI TIẾT KỸ THUẬT VẤN ĐỀ F2 (BACKEND IDEMPOTENCY)

### 2.1. Phân tích hiện trạng & Lựa chọn giải pháp
- **Hạ tầng cơ sở dữ liệu:** Schema PostgreSQL của repo (`docs/database/oisms_schema.sql`) đã có sẵn cột `orders.request_key uuid` cùng ràng buộc duy nhất:
  ```sql
  CONSTRAINT "uq_orders_3" UNIQUE ("tenant_id", "request_key")
  ```
  Do đó, **không cần chạy migration cơ sở dữ liệu**, bảo toàn 100% dữ liệu nghiệp vụ và không làm rủi ro volume database.
- **Ràng buộc nghiệp vụ hiện hành:**
  - Cơ chế retry theo `(OrderId, OrderItemId)` hiện có vẫn được bảo toàn nguyên vẹn cho các luồng đã có đơn hàng cụ thể.
  - Luồng giữ chỗ nhanh từ POS hoặc client không có sẵn đơn hàng (chỉ truyền `BranchId`, `WarehouseId`, `SkuId`, `Quantity`) sẽ sử dụng `RequestKey`.
  - Khóa Advisory Lock phân tán ở cấp độ PostgreSQL Transaction:
    ```sql
    SELECT pg_advisory_xact_lock(hashtextextended(tenantId || '/request_key/' || requestKey, 0));
    ```
    Đảm bảo 100% an toàn nguyên tử khi nhiều request cùng key đến đồng thời trong cùng một mili-giây.

### 2.2. Hợp đồng API (Contract Details)
- **Endpoint:** `POST /api/inventory/reserve`
- **Nhận diện Idempotency Key:**
  1. Header HTTP: `Idempotency-Key: <guid>` (chuẩn IETF draft) hoặc `X-Request-Key: <guid>`
  2. Hoặc Body JSON: `"requestKey": "<guid>"`
  Nếu client truyền qua header, controller tự động map vào `dto.RequestKey`.
- **Hành vi xử lý:**
  1. **Cùng Tenant, cùng Key, cùng Payload (BranchId, WarehouseId, SkuId, Quantity):**
     - Trả lại đúng thông tin Reservation ban đầu (`ReservationId`, `OrderId`, `OrderItemId`, `Quantity`, `Status`, `ExpiresAt`, `RemainingAvailableStock`).
     - Mã trạng thái: `200 OK`.
     - **Tuyệt đối KHÔNG tạo thêm Order, KHÔNG tạo OrderItem, KHÔNG tạo Reservation và KHÔNG tăng cột `Reserved` trong `inventory_balances` / `warehouse_inventory_balances`.**
  2. **Cùng Tenant, cùng Key nhưng khác Payload (ví dụ đổi SkuId hoặc Quantity):**
     - Ném `InvalidOperationException` $\rightarrow$ Trả về mã **HTTP 409 Conflict**.
     - Dữ liệu tồn kho và đơn hàng không đổi.
  3. **Cùng Key nhưng gửi lại sau khi Reservation đã hết hạn (`ExpiresAt <= nowUtc` hoặc `Status != "Active"`):**
     - Trả về mã **HTTP 409 Conflict** với thông báo rõ ràng (`Yêu cầu giữ chỗ với mã '...' đã kết thúc hiệu lực... Không thể tái sử dụng mã yêu cầu này để tạo giữ chỗ mới`).
     - Tuyệt đối không tự động sinh thêm giữ chỗ dở dang.
  4. **Cách ly đa Tenant (Tenant Isolation):**
     - Hai tenant khác nhau hoàn toàn có thể sử dụng cùng một `RequestKey` mà không bị xung đột, mỗi tenant sở hữu Order và Reservation riêng biệt.
  5. **Bảo toàn validation đã có:**
     - Giữ nguyên kiểm tra vượt tồn khả dụng (`OnHand - Reserved >= Quantity`).
     - Giữ nguyên kiểm tra trạng thái hoạt động (`IsActive`) 4 cấp: Chi nhánh, Kho hàng, Sản phẩm, Biến thể SKU.

### 2.3. Các file thay đổi
1. `src/backend/src/OISM.Application/Features/Inventory/Dtos/ReservationDtos.cs`:
   - Bổ sung trường `public Guid? RequestKey { get; set; }` vào `ReserveStockRequestDto` kèm tài liệu XML doc.
2. `src/backend/src/OISM.WebApi/Controllers/InventoryController.cs`:
   - Cập nhật action `Reserve`: Bổ sung `[FromHeader(Name = "Idempotency-Key")]` và `[FromHeader(Name = "X-Request-Key")]`, tự động gán vào `dto.RequestKey` nếu body chưa có.
3. `src/backend/src/OISM.Application/Features/Inventory/Services/ReservationService.cs`:
   - Lấy PostgreSQL Advisory Lock theo `(tenantId, RequestKey)`.
   - Tìm kiếm Order đã tồn tại theo `(TenantId, RequestKey)`.
   - Đối soát tính trùng khớp payload và trạng thái của Reservation cũ.
   - Gán `order.RequestKey = dto.RequestKey ?? Guid.NewGuid()`.

---

## 3. CHI TIẾT KỸ THUẬT VẤN ĐỀ F7 (DOCKER / NGINX SPA FALLBACK)

### 3.1. Phân tích nguyên nhân
- Container `oism_admin_dashboard` và `oism_pos_app` được build từ base image `nginx:alpine` nhưng trước đây không copy file cấu hình Nginx tùy chỉnh.
- Cấu hình mặc định của Nginx chỉ tìm file tĩnh tương ứng với URL (ví dụ `/login` $\rightarrow$ tìm thư mục `/usr/share/nginx/html/login`). Do không tìm thấy, Nginx trả về `404 Not Found`.
- Nếu chỉ cấu hình `try_files $uri /index.html;` sơ sài, các file asset không tồn tại (như `/assets/bundle-err.js`) sẽ bị fallback trả về mã HTML của `index.html`. Trình duyệt khi tải script/stylesheet sẽ gặp lỗi cú pháp MIME type (`SyntaxError: Unexpected token '<'`).

### 3.2. Cấu hình Nginx chuẩn hóa
Đã tạo cấu hình `nginx.conf` cho cả `admin-dashboard` và `pos-app` với các chỉ thị:
```nginx
server {
    listen 80;
    server_name localhost;
    root /usr/share/nginx/html;
    index index.html;

    # 1. Định tuyến tài nguyên tĩnh /assets/ - Nếu không tồn tại TRẢ ĐÚNG 404, tuyệt đối không fallback về index.html
    location /assets/ {
        try_files $uri =404;
        expires 1y;
        add_header Cache-Control "public, immutable";
        access_log off;
    }

    # 2. Định tuyến các file tĩnh theo extension
    location ~* \.(?:ico|css|js|gif|jpe?g|png|woff2?|eot|ttf|svg|webmanifest)$ {
        try_files $uri =404;
        expires 30d;
        add_header Cache-Control "public";
        access_log off;
    }

    # 3. SPA Fallback cho toàn bộ application routes (/login, /inventory, /products, /403,...)
    location / {
        try_files $uri $uri/ /index.html;
    }

    gzip on;
    gzip_types text/plain text/css application/json application/javascript text/xml application/xml application/xml+rss text/javascript image/svg+xml;
}
```

### 3.3. Các file thay đổi
1. `src/frontend/admin-dashboard/nginx.conf`: Tạo mới cấu hình Nginx SPA fallback.
2. `src/frontend/pos-app/nginx.conf`: Tạo mới cấu hình Nginx SPA fallback cho POS.
3. `src/frontend/admin-dashboard/Dockerfile`: Thêm chỉ thị `COPY nginx.conf /etc/nginx/conf.d/default.conf`.
4. `src/frontend/pos-app/Dockerfile`: Thêm chỉ thị `COPY nginx.conf /etc/nginx/conf.d/default.conf`.

---

## 4. KẾT QUẢ KIỂM THỬ THỰC TẾ (TEST EVIDENCE)

### 4.1. Bộ kiểm thử Backend (F2) — 18/18 Tests Pass 100%
Đã bổ sung bài kiểm thử tích hợp chuyên sâu trên PostgreSQL thật trong file:  
`src/backend/tests/OISM.IntegrationTests/InventoryAndGoodsReceiptIntegrationTests.cs`:
`Postgres_Reservation_F2_Idempotency_RequestKey_Retry_Concurrency_And_TenantIsolation_Regressions`

Bao phủ 7 kịch bản kiểm thử bắt buộc:
1. **CA 1: Gửi lần đầu với RequestKey:** Thành công `200 OK`, tạo Reservation mới, tăng `Reserved` từ 0 lên 3m.
2. **CA 2: Retry cùng RequestKey & cùng payload (qua Body):** Trả về `200 OK`, trả đúng Reservation cũ, database xác nhận `Reserved` vẫn là 3m, số lượng Order vẫn là 1.
3. **CA 3: Retry cùng RequestKey qua HTTP Header (`Idempotency-Key`):** Trả về `200 OK`, trả đúng Reservation cũ, không tăng `Reserved`.
4. **CA 4: Concurrency test (5 request đồng thời gửi cùng RequestKey):** Tất cả 5 request đều hoàn thành `200 OK`, dữ liệu database xác nhận `Reserved` chỉ tăng đúng 2m một lần duy nhất (từ 3m lên 5m, tuyệt đối không thành 13m!), chỉ 1 Order và 1 Reservation duy nhất được tạo.
5. **CA 5: Cùng RequestKey nhưng payload khác (Quantity khác):** Backend từ chối với mã `409 Conflict`, dữ liệu tồn kho được bảo toàn nguyên vẹn không đổi.
6. **CA 6: Tenant Isolation:** Tenant B sử dụng **CÙNG** `RequestKey` với Tenant A vẫn tạo ra Order và Reservation độc lập thuộc Tenant B (`200 OK`), hai Order mang hai ID khác nhau hoàn toàn.
7. **CA 7: Retry sau khi Reservation đã hết hạn:** Backend từ chối `409 Conflict`, không tự ý tạo thêm Order hay Reservation mới.

**Kết quả chạy lệnh thực tế:**
```text
Passed!  - Failed: 0, Passed: 18, Skipped: 0, Total: 18, Duration: 58 s - OISM.IntegrationTests.dll (net8.0)
Unit Tests: 103 / 103 Passed 100%
```

### 4.2. Kiểm thử Docker / Nginx (F7) & API Backend thực tế
Kiểm tra tự động trên toàn bộ các container Docker đang chạy:

```text
PASS: [200] Admin Root (Content-Type: text/html)
PASS: [200] Admin /login (Content-Type: text/html)
PASS: [200] Admin /inventory (Content-Type: text/html)
PASS: [200] Admin /products (Content-Type: text/html)
PASS: [200] Admin /403 (Content-Type: text/html)
PASS: [404] Admin Missing Asset (Correctly returned expected HTTP 404)
PASS: [200] Admin Real Asset (Content-Type: application/javascript)
PASS: [200] POS Root (Content-Type: text/html)
PASS: [200] POS /pos (Content-Type: text/html)
PASS: [404] POS Missing Asset (Correctly returned expected HTTP 404)
PASS: [200] Swagger UI (Content-Type: text/html;charset=utf-8)
PASS: [204] CORS Preflight OPTIONS /api/inventory/reserve (Access-Control-Allow-Origin: http://localhost:5173)
```

---

## 5. HƯỚNG DẪN TÍCH HỢP CHO FRONTEND (BẮT BUỘC)

> [!IMPORTANT]
> **Phân định trách nhiệm hoàn thành:**  
> Backend đã hoàn thành 100% hạ tầng kiểm soát Idempotency và tài liệu Swagger. Tuy nhiên, theo nguyên lý kỹ thuật, **Backend không thể tự đoán biết hai request HTTP là một nếu Frontend không gửi cùng một định danh**.  
> Do đó, F2 chỉ được giải quyết trọn vẹn toàn luồng khi Frontend tích hợp truyền key theo hướng dẫn dưới đây.

### Cách thức Frontend gửi Idempotency Key:
Khi thực hiện thao tác giữ chỗ tồn kho (ví dụ khi người dùng nhấn "Thêm vào giỏ" hoặc bước đầu tạo đơn bán hàng):
1. **Sinh mã UUID v4 duy nhất:**
   ```typescript
   const clientRequestId = crypto.randomUUID();
   ```
2. **Gửi kèm trong request gọi API:**
   - **Cách 1 (Khuyên dùng - qua Header HTTP):**
     ```typescript
     const response = await fetch('/api/inventory/reserve', {
       method: 'POST',
       headers: {
         'Content-Type': 'application/json',
         'Idempotency-Key': clientRequestId, // Hoặc 'X-Request-Key': clientRequestId
         'Authorization': `Bearer ${accessToken}`
       },
       body: JSON.stringify({
         branchId: selectedBranchId,
         warehouseId: selectedWarehouseId,
         skuId: selectedSkuId,
         quantity: quantity
       })
     });
     ```
   - **Cách 2 (Qua Request Body):**
     ```typescript
     const response = await fetch('/api/inventory/reserve', {
       method: 'POST',
       headers: {
         'Content-Type': 'application/json',
         'Authorization': `Bearer ${accessToken}`
       },
       body: JSON.stringify({
         branchId: selectedBranchId,
         warehouseId: selectedWarehouseId,
         skuId: selectedSkuId,
         quantity: quantity,
         requestKey: clientRequestId
       })
     });
     ```
3. **Xử lý Retry khi mất mạng hoặc timeout:**
   Nếu request gặp lỗi mạng (Network Error / Timeout), frontend thực hiện retry bằng cách gửi lại **CHÍNH XÁC `clientRequestId` CŨ**. Backend sẽ an toàn trả về kết quả thành công của lần trước mà không giữ hàng hai lần.
