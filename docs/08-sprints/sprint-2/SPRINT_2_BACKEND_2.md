# Sprint 2 — Backend 2: Nhựt

> **Người phụ trách:**  Nhựt — Backend phụ  
> **Ngày lập:** 16/10/2026  
> **Nguồn:** [Cẩm nang Sprint 2](SPRINT_2_COMPREHENSIVE_GUIDE.md)  
> **Phối hợp:** [Backend 1 — Khoa](SPRINT_2_BACKEND_1.md)

> **Schema hiện hành:** [Thiết kế database rút gọn v0.5 — 29 bảng](OISMS_DATABASE_DESIGN.md). Barcode và giá sỉ/lẻ nằm trên SKU; email/SĐT chuẩn hóa ngay trên cột chính. Unique toàn hệ thống là lựa chọn kỹ thuật hiện tại, chưa phải yêu cầu được đề chỉ rõ.

## 1. Mục tiêu và phạm vi

Phụ trách xác thực, phân quyền và dữ liệu danh mục để hoàn thành luồng chung:

**Đăng nhập → tạo SKU → lập và xác nhận nhập kho → cập nhật tồn, sổ cái và WAC → Admin/POS đọc dữ liệu thật.**

Backend 2 cung cấp dữ liệu đầu vào và quyền truy cập; Backend 1 xử lý xác nhận nhập, tồn kho, ledger và WAC.

Bảng dưới đây lưu phân công và các tiêu chí kỹ thuật đã phân tích. Tất cả nhiệm vụ đang ở trạng thái **chưa xác minh tiến độ**; đây không phải báo cáo kết quả triển khai.

## 2. Bảng nhiệm vụ Backend 2

| Mã việc | Nhóm việc | Công việc cụ thể | Tiêu chí hoàn thành |
|---|---|---|---|
| BE2-01 | Đăng ký cửa hàng | Tạo tenant và tài khoản Owner ban đầu; kiểm tra trùng và dữ liệu đầu vào; phối hợp Khoa rà soát. | Đăng ký xong đăng nhập được vào đúng cửa hàng; lỗi không để lại dữ liệu tạo dở. |
| BE2-02 | Xác thực | API login, refresh, logout, me; băm mật khẩu bằng BCrypt theo phân công; cấp JWT có TenantId, UserId, Role. Chốt lưu trữ, thu hồi và vòng đời refresh token. | Frontend đăng nhập, lấy thông tin người dùng và xử lý vòng đời token được; access token 60 phút theo tài liệu; hành vi logout/refresh được kiểm thử. |
| BE2-03 | Phân quyền RBAC | Cấu hình quyền Owner, Staff, Cashier trên backend; phối hợp Khoa áp dụng cho API kho. | Cashier gọi API nhập kho nhận 403; yêu cầu thiếu token/token không hợp lệ được xử lý đúng; quyền không chỉ kiểm tra ở frontend. |
| BE2-04 | Danh mục và thương hiệu | CRUD Category, quan hệ cha–con nhiều tầng và Brand; validation quan hệ hợp lệ, cùng tenant. | Admin tạo/sửa/đọc dữ liệu thật; danh mục không tạo vòng lặp cha–con hoặc tham chiếu chéo tenant. |
| BE2-05 | Sản phẩm và SKU | CRUD Product, biến thể/SKU; thuộc tính tùy ý qua Skus.Attributes (JSON object), chuẩn hóa và kiểm tra tổ hợp trùng trong sản phẩm; unique index SKU theo tenant. | Trùng SKU cùng tenant trả 409; tenant khác được dùng cùng mã; quan hệ sản phẩm/SKU đúng tenant. |
| BE2-06 | Giá bán | Quản lý giá bán lẻ và giá bán sỉ theo SKU; validation giá. | API trả đúng giá; giá bán tách biệt với giá vốn WAC do Backend 1 xử lý. |
| BE2-07 | Barcode và tìm kiếm POS | Nhập/tự sinh barcode theo phạm vi EAN-13/Code128; validation; tìm SKU theo mã/tên/barcode. Phối hợp Backend 1 thống nhất dữ liệu tồn trả cho POS. | POS quét/tìm được SKU thật, giá bán và tồn khả dụng đúng phạm vi tenant/chi nhánh; kiểm thử các quy tắc barcode đã chốt. |
| BE2-08 | Nhà cung cấp, chi nhánh và kho | API tra cứu nhà cung cấp; tạo/sửa/bật tắt chi nhánh và kho theo RTM; API tra cứu cho form nhập. | Frontend chọn được dữ liệu hợp lệ thuộc tenant hiện tại; Owner quản lý được chi nhánh/kho theo quyền. |
| BE2-09 | Validation, kiểm thử và tài liệu API | FluentValidation; test Auth/RBAC, cô lập tenant, unique SKU và barcode; cập nhật Swagger; bàn giao từng PR nhỏ. | Các trường hợp hợp lệ/sai được xử lý rõ; toàn bộ testcase đã chốt cho unique SKU/barcode đạt. Làm rõ “100% kiểm thử” là toàn bộ testcase hay chỉ tiêu coverage trước nghiệm thu. |

## 3. Phụ thuộc và cách phối hợp với Backend 1

- Nhận TenantContext, mẫu controller/service, cấu trúc lỗi và transaction từ Khoa; mọi API nghiệp vụ phải tuân thủ cơ chế tenant chung.
- Thống nhất entity Auth/Catalog và thay đổi schema trước khi tạo migration; không tự thay đổi cấu trúc tồn kho hoặc cách tính WAC.
- Chốt request/response, phân trang và lỗi validation trước khi bàn giao cho frontend/tester.
- Lấy tenant từ ngữ cảnh xác thực đã kiểm chứng; kiểm tra quyền và các ID liên quan, không tin TenantId do client gửi.
- Phối hợp seed tối thiểu hai tenant, các vai trò và dữ liệu SKU/kho phục vụ kiểm thử.
- Phối hợp Khoa thống nhất API tra cứu SKU và tồn khả dụng; không tự triển khai công thức tồn khác với module kho.

## 4. Thứ tự bàn giao chung

| Mốc | Backend 1 — Khoa | Backend 2 — Nhựt | Frontend có thể làm |
|---|---|---|---|
| 1 | Database, tenant, chuẩn API. | Đăng ký, login, me, RBAC. | Nối đăng nhập. |
| 2 | Chốt cấu trúc tồn kho và phiếu nhập. | Catalog, SKU, giá, barcode, tra cứu kho/nhà cung cấp. | Nối sản phẩm và form nhập. |
| 3 | Xác nhận nhập, ledger, WAC, API tồn. | Hoàn thiện refresh/logout, validation và tìm kiếm POS. | Chạy luồng nhập kho và tra cứu POS. |
| 4 | Test rollback, gửi lặp, đồng thời và reservation. | Test tenant, quyền, SKU và barcode. | E2E, sửa lỗi, nghiệm thu. |

Các mốc là thứ tự phụ thuộc, chưa gán ngày cụ thể. Tài liệu nguồn ghi 13–17/10/2026 nhưng gọi D1–D6: khoảng ngày này chỉ có 5 ngày, cần thống nhất lịch trước khi đặt hạn chót.

## 5. Checklist bàn giao

- [ ] Đăng ký tenant và Owner thành công; không để lại dữ liệu tạo dở khi lỗi.
- [ ] Login/refresh/logout/me chạy thật; mật khẩu được băm; không trả thông tin bí mật trong response.
- [ ] JWT chứa đúng TenantId, UserId, Role; các tình huống hết hạn và refresh/logout được kiểm thử.
- [ ] RBAC kiểm tra tại API; Cashier không gọi được nghiệp vụ nhập kho.
- [ ] CRUD Category/Brand/Product/SKU hoạt động đúng tenant.
- [ ] Unique SKU theo tenant được bảo vệ tại database và kiểm thử.
- [ ] Giá sỉ/lẻ và barcode hoạt động; POS tra cứu SKU thật được.
- [ ] API tra cứu nhà cung cấp và quản lý/tra cứu chi nhánh, kho đủ cho form nhập.
- [ ] Test đọc/ghi và tham chiếu ID chéo tenant bị chặn.
- [ ] Build/test đạt theo cẩm nang; có bằng chứng CI/test và Swagger trong PR.
- [ ] Swagger/DTO cập nhật; frontend/tester nhận được endpoint, quyền và dữ liệu mẫu.
- [ ] Các lỗi Blocker/Critical trong phạm vi bàn giao đã đóng; kiểm thử nghiệm thu hoàn tất trước khi đánh dấu Done.

## 6. Giới hạn phạm vi cần thống nhất

- Nhà cung cấp: tài liệu mới giao API tra cứu, chưa yêu cầu CRUD đầy đủ. Cần có dữ liệu seed hợp lệ để chạy luồng nhập kho.
- Chi nhánh/kho: phần phân công chỉ nhắc tra cứu, nhưng RTM yêu cầu tạo/sửa/bật tắt; bảng này giữ yêu cầu đầy đủ theo RTM.
- “Gỡ bỏ tài khoản Owner mặc định” ở frontend có nghĩa loại bỏ trạng thái đăng nhập giả; không loại bỏ tài khoản Owner thật tạo khi đăng ký tenant.
- Checkout bán hàng hoàn chỉnh chưa được chốt là đầu ra bắt buộc; Backend 2 tập trung dữ liệu catalog, giá bán và tra cứu phục vụ POS.

> Database v0.5 có guard tồn/ledger và kiểm tra snapshot tại COMMIT. Hai thành viên tích hợp theo [Quy ước database](../../database/README.md); các nhiệm vụ API/nghiệp vụ chưa được coi là hoàn thành bởi bản SQL này.
