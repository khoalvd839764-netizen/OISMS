# SKILL-03: BẢO MẬT XÁC THỰC JWT, XOAY VÒNG REFRESH TOKEN & PHÂN QUYỀN RBAC 2 LỚP

> **Người thực hiện:** Nhựt Phùng Minh (Backend 2 Lead)  
> **Áp dụng tại:** Dự án OISMS — Hệ thống Quản lý Bán hàng & Tồn kho Đa kênh  
> **Cơ sở đối chiếu lý thuyết:** Ch14 - Security & Information Hiding, NFR-SEC-01 & FR-AUTH-02, FR-AUTH-03.

---

### 1. BÀI TOÁN THỰC TẾ & NỖI ĐAU HỆ THỐNG
* Nếu Access Token có thời hạn vô tận hoặc không có cơ chế thu hồi, khi bị hacker đánh cắp trên đường truyền mạng (Man-in-the-Middle), tài khoản cửa hàng sẽ bị chiếm đoạt vĩnh viễn.
* Nếu chỉ phân quyền trên giao diện Frontend (ẩn nút bấm), người dùng am hiểu kỹ thuật có thể dùng Postman hoặc Swagger để gọi thẳng API quản trị của Sếp (`GET /api/reports/revenue`).

---

### 2. GIẢI PHÁP KỸ THUẬT ĐÃ ÁP DỤNG

#### 2.1. Kiến trúc JWT Ngắn hạn kết hợp Xoay vòng Refresh Token (Token Rotation)
* **Access Token:** Có thời hạn ngắn (60 phút), được ký điện tử bằng thuật toán `HmacSha256`, chứa các Claims: `UserId`, `TenantId`, `Role`, `BranchId`.
* **Refresh Token Rotation nguyên tử trên PostgreSQL:**
  - Mỗi khi client yêu cầu cấp Access Token mới qua API `POST /api/auth/refresh-token`, hệ thống khóa dòng token bằng `FOR UPDATE`:
    1. Kiểm tra nếu token đã bị thu hồi (`is_revoked == true`) hoặc hết hạn &rarr; Chặn ngay lập tức và phát hiện tấn công dùng lại token (Replay Attack).
    2. Đánh dấu token cũ là đã thu hồi (`is_revoked = true`).
    3. Sinh ra một Refresh Token mới toanh, lưu mã băm an toàn vào CSDL và trả về cho client.
  - Xử lý triệt để Race Condition khi người dùng mở nhiều tab trình duyệt gửi request refresh đồng thời.

#### 2.2. Cơ chế Bảo vệ Phân quyền 2 Tầng (Two-Tier RBAC Protection)
* **Tầng 1 (Frontend Route Guard & Menu Filter):**
  - Component `ProtectedRoute.tsx` bọc các tuyến đường nhạy cảm:
    ```tsx
    <Route element={<ProtectedRoute allowedRoles={["Owner"]} />}>
      <Route path="/reports" element={<ReportsPage />} />
      <Route path="/organization" element={<OrganizationPage />} />
    </Route>
    ```
  - Nếu tài khoản `Staff` hoặc `Cashier` cố tình truy cập, Route Guard lập tức chuyển hướng về trang `/403` (Access Denied).
* **Tầng 2 (Chốt chặn Backend API Controller):**
  - Mọi Controller đều gắn thuộc tính `[Authorize(Roles = "...")]`:
    * `POST /api/warehouses`: Bắt buộc `Roles = "Owner,Staff"`.
    * `GET /api/reports/...`: Bắt buộc duy nhất `Roles = "Owner"`.
  - Nếu `Cashier` bypass giao diện gọi API trực tiếp, Middleware ASP.NET Core từ chối ngay lập tức với mã lỗi `403 Forbidden`.

---

### 3. BẰNG CHỨNG KIỂM CHỨNG TRONG MÃ NGUỒN
* Mã nguồn cài đặt: [`src/backend/src/OISM.WebApi/Controllers/AuthController.cs`](file:///Users/bo/OISMS/src/backend/src/OISM.WebApi/Controllers/AuthController.cs) và `JwtTokenService.cs`.
* Tài liệu đặc tả bàn giao: [`docs/CONFLUENCE_3_1_3_SCREEN_AUTHORIZATION_MATRIX.md`](file:///Users/bo/OISMS/docs/CONFLUENCE_3_1_3_SCREEN_AUTHORIZATION_MATRIX.md) (Ma trận phân quyền 39 màn hình).
* Bài kiểm thử tự động: `TC_AUTH_02_CashierRole_ShouldBeForbidden_WhenAccessingOwnerEndpoints` kiểm chứng mã `403 Forbidden` được trả về chính xác 100%.
