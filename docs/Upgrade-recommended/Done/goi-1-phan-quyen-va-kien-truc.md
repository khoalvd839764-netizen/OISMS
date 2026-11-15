# Gói 1 — Phân quyền và kiến trúc (đã đóng 14/11/2026)

## Hiện trạng

Ứng dụng có ba vai trò, route guard và kiến trúc backend bốn tầng. API số dư tồn vẫn cho Cashier gọi và có thể trả `AverageCost`. Quyền Staff với kho và xóa danh mục còn lệch giữa code và đặc tả. CS-02 có quản lý nhân viên, còn thiếu phân công chi nhánh. Backend đã hoàn thành đợt refactor Upgrade 06, với mức phụ thuộc EF Core trong Application cần đối chiếu theo `NFR-MAINT-01`. [Ma trận RBAC và chênh lệch](../../01-users-and-rbac/02-rbac-matrix.md#21-lệch-giữa-yêu-cầu-và-code-hiện-tại-đối-chiếu-06112026) · [Báo cáo refactor](../../report-after-upgrade/clean-architecture.md)

## Việc cần làm

- [x] Đối chiếu quyền Owner, Staff, Cashier giữa route Admin/POS, API và dữ liệu trả về. Ưu tiên ngăn Cashier nhận giá vốn mà vẫn giữ được tra cứu tồn khả dụng phục vụ POS.
- [x] Chốt quyền ghi kho của Staff, quyền xóa danh mục và phạm vi chi nhánh cho CS-02. Sau khi có quyết định, đồng bộ policy API, giao diện và ma trận quyền.
- [x] Rà chiều tham chiếu project và các điểm Application dùng EF Core hoặc chứa logic WAC/Reserve. Ghi rõ điểm cần sửa và cách nghiệm thu từng điểm theo đặc tả phân tầng.

## Bằng chứng nghiệm thu

- [x] Test cho phép và từ chối theo vai trò, gồm gọi API trực tiếp và dữ liệu trả về cho Cashier. Có ca đối chiếu tenant và chi nhánh nếu triển khai phân công.
- [x] Có bản đối chiếu phụ thuộc bốn tầng, tham chiếu tới code và kết quả test hồi quy ở các luồng được sửa.

Đăng nhập bằng SĐT và quên mật khẩu theo đặc tả riêng. [Upgrade 10](./10-quen-mat-khau-dang-nhap-sdt.md) · [Đặc tả phân tầng](../../03-non-functional-requirements/05-maintainability-testing.md)

## Kết quả (14/11/2026, OISMS-59-G1)

| Phần | Quyết định | Kết quả | Bằng chứng |
| --- | --- | --- | --- |
| G1A — Giá vốn và quyền kho | Ghi kho chỉ Owner; xóa danh mục, thương hiệu, sản phẩm chỉ Owner (sửa docs) | Cashier nhận `averageCost = null`; Staff ghi kho trả `403` | [Báo cáo G1A](../../report-after-upgrade/phan-quyen-gia-von-va-kho-g1a.md) · `InventoryAndWarehouseRbacIntegrationTests` |
| G1B — CS-02 phân công chi nhánh | Gán nhiều chi nhánh; nhân viên chưa gán dùng mọi chi nhánh; chỉ giới hạn Cashier ở danh sách chi nhánh và POS checkout | Bảng `user_branches` (48 bảng), `branchIds` ở `/api/users`, checkout ngoài phạm vi trả `403 BRANCH_NOT_ASSIGNED` | [Báo cáo CS-02](../../report-after-upgrade/phan-cong-chi-nhanh-cs02.md) · `UserBranchAssignmentIntegrationTests` (PostgreSQL) |
| G1C — NFR-MAINT-01 | Application dùng EF Core qua `IAppDbContext` là ngoại lệ có chủ đích; không refactor sang repository | `ArchitectureRulesTests` 5/5 PASS | [Đối chiếu bốn tầng](../../report-after-upgrade/doi-chieu-kien-truc-nfr-maint-01.md) |

Giới hạn còn lại: chưa lọc dữ liệu Admin theo chi nhánh được gán; DB đang chạy (local, AWS) phải chạy `docker/postgres/migrations/20261114_user_branches.sql` trước khi deploy backend mới.
