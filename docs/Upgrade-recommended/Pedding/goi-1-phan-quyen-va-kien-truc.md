# Gói 1 — Phân quyền và kiến trúc còn mở

## Hiện trạng

Ứng dụng có ba vai trò, route guard và kiến trúc backend bốn tầng. API số dư tồn vẫn cho Cashier gọi và có thể trả `AverageCost`. Quyền Staff với kho và xóa danh mục còn lệch giữa code và đặc tả. CS-02 có quản lý nhân viên, còn thiếu phân công chi nhánh. Backend đã hoàn thành đợt refactor Upgrade 06, với mức phụ thuộc EF Core trong Application cần đối chiếu theo `NFR-MAINT-01`. [Ma trận RBAC và chênh lệch](../../01-users-and-rbac/02-rbac-matrix.md#21-lệch-giữa-yêu-cầu-và-code-hiện-tại-đối-chiếu-06112026) · [Báo cáo refactor](../../report-after-upgrade/clean-architecture.md)

## Việc cần làm

- [ ] Đối chiếu quyền Owner, Staff, Cashier giữa route Admin/POS, API và dữ liệu trả về. Ưu tiên ngăn Cashier nhận giá vốn mà vẫn giữ được tra cứu tồn khả dụng phục vụ POS.
- [ ] Chốt quyền ghi kho của Staff, quyền xóa danh mục và phạm vi chi nhánh cho CS-02. Sau khi có quyết định, đồng bộ policy API, giao diện và ma trận quyền.
- [ ] Rà chiều tham chiếu project và các điểm Application dùng EF Core hoặc chứa logic WAC/Reserve. Ghi rõ điểm cần sửa và cách nghiệm thu từng điểm theo đặc tả phân tầng.

## Bằng chứng nghiệm thu

- [ ] Test cho phép và từ chối theo vai trò, gồm gọi API trực tiếp và dữ liệu trả về cho Cashier. Có ca đối chiếu tenant và chi nhánh nếu triển khai phân công.
- [ ] Có bản đối chiếu phụ thuộc bốn tầng, tham chiếu tới code và kết quả test hồi quy ở các luồng được sửa.

Đăng nhập bằng SĐT và quên mật khẩu theo đặc tả riêng. [Upgrade 10](10-quen-mat-khau-dang-nhap-sdt.md) · [Đặc tả phân tầng](../../03-non-functional-requirements/05-maintainability-testing.md)
