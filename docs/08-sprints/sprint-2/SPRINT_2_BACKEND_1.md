# Sprint 2 — Backend 1: Khoa

> **Người phụ trách:** Khoa — Backend chính / Trưởng nhóm  
> **Ngày lập:** 16/10/2026  
> **Nguồn:** [Cẩm nang Sprint 2](SPRINT_2_COMPREHENSIVE_GUIDE.md)  
> **Phối hợp:** [Backend 2 — Nhựt](SPRINT_2_BACKEND_2.md)

> **Schema hiện hành:** [Thiết kế database rút gọn v0.5 — 29 bảng](OISMS_DATABASE_DESIGN.md). SQL/DBML trong `docs/database` thay cho bản đề xuất 42 bảng; triển khai entity/migration theo bản mới.

## 1. Mục tiêu và phạm vi

Phụ trách nền tảng dữ liệu, cô lập tenant và nghiệp vụ kho để hoàn thành luồng:

**Đăng nhập → tạo SKU → lập và xác nhận nhập kho → cập nhật tồn, sổ cái và WAC → Admin/POS đọc dữ liệu thật.**

Bảng dưới đây lưu phân công và các tiêu chí kỹ thuật đã phân tích. Tất cả nhiệm vụ đang ở trạng thái **chưa xác minh tiến độ**; đây không phải báo cáo kết quả triển khai.

## 2. Bảng nhiệm vụ Backend 1

| Mã việc | Nhóm việc | Công việc cụ thể | Tiêu chí hoàn thành |
|---|---|---|---|
| BE1-01 | Database và môi trường | Thiết kế entity, quan hệ, mapping PostgreSQL; tạo migration và seed; cấu hình kết nối, Docker Compose và CI. | Database sạch chạy migration + seed thành công; backend khởi động được. |
| BE1-02 | Nền tảng multi-tenant | Xây dựng TenantContext; cấu hình query filter; kiểm tra tenant khi ghi; ngăn tham chiếu SKU/kho khác tenant. Tenant lấy từ ngữ cảnh xác thực đã kiểm chứng. | Tenant A không đọc, sửa hoặc tạo dữ liệu gắn với tài nguyên tenant B; có kiểm thử đọc, ghi và tham chiếu ID. |
| BE1-03 | Chuẩn backend dùng chung | Chốt DTO, cấu trúc lỗi, mẫu controller/service, validation và transaction; thống nhất mã trạng thái API. | Backend 2 và frontend có hợp đồng API rõ để làm song song. |
| BE1-04 | Phiếu nhập kho | API tạo phiếu nhiều dòng, xem danh sách/chi tiết và xác nhận phiếu; kiểm tra kho, SKU, số lượng, giá nhập. | Tạo và xác nhận một phiếu nhập thật thành công; dữ liệu thuộc đúng tenant. |
| BE1-05 | Xác nhận nhập nguyên tử | Trong cùng transaction: kiểm tra trạng thái phiếu → cập nhật tồn kho và tổng tồn chi nhánh → tính WAC chi nhánh → ghi ledger → đánh dấu đã xác nhận. Kiểm soát gửi lặp và cập nhật đồng thời. | Một bước lỗi thì toàn bộ rollback; xác nhận lặp không tăng tồn lần nữa; hai phiếu cùng SKU không làm mất cập nhật. |
| BE1-06 | Sổ cái kho | Ghi InventoryTransaction, số lượng biến động, BalanceAfter và tham chiếu phiếu; ngăn sửa/xóa; cung cấp API tra cứu. | Mỗi biến động nhập có dấu vết; UPDATE/DELETE bị từ chối với tài khoản database ứng dụng. |
| BE1-07 | Giá vốn WAC | Tính lại giá vốn khi nhập; tính riêng theo chi nhánh/SKU; thống nhất kiểu số thập phân, làm tròn và điều kiện đầu vào. | Tồn 10 × 100.000đ, nhập 20 × 130.000đ cho WAC = 120.000đ; có test các trường hợp biên đã thống nhất. |
| BE1-08 | API tồn kho | Trả tồn thực tế, giữ chỗ và khả dụng theo SKU/chi nhánh (FR-RSE-01): available = on_hand - reserved. Phối hợp Backend 2 cung cấp dữ liệu cho POS. | Admin xem đúng tồn; POS tra cứu đúng tồn khả dụng. |
| BE1-09 | Nền tảng Reservation | Xây dựng thao tác giữ chỗ tối thiểu và kiểm soát đồng thời bằng khóa database hoặc version token; xác định hợp đồng API/harness kiểm thử. | Nếu giữ nguyên tiêu chí tài liệu: tồn 1, gửi đồng thời 50 yêu cầu độc lập, mỗi yêu cầu giữ 1 sản phẩm, chỉ 1 thành công; 49 bị từ chối hợp lệ; available = 0. |
| BE1-10 | Kiểm thử lõi | Unit/integration test cho ledger, WAC, transaction và reservation; thống nhất cách đo coverage. | Coverage ≥80% cho phần lõi đã thống nhất; các ca rollback, gửi lặp và đồng thời đều đạt. |

## 3. Các quyết định phải chốt cùng Backend 2

- **Schema và migration:** Backend 1 phụ trách nền tảng; Backend 2 thống nhất entity Auth/Catalog trước khi tạo migration để tránh xung đột.
- **Quyền theo endpoint:** Chốt vai trò được xem, tạo, sửa và xác nhận phiếu nhập; thực thi quyền tại backend.
- **Hợp đồng API:** Request/response, phân trang, lỗi validation và cách dùng 401/403/404/409; bổ sung các mã thành công phù hợp.
- **Phạm vi tồn và WAC:** Tồn khả dụng theo SKU tại từng chi nhánh (FR-RSE-01). Nhóm đã chọn WAC theo chi nhánh/SKU; tồn và giữ chỗ chi tiết theo kho, tổng chi nhánh cập nhật cùng transaction.
- **Seed:** Ít nhất hai tenant, các vai trò và dữ liệu SKU/kho để kiểm thử cô lập. Tài khoản mẫu chỉ phục vụ môi trường phát triển/kiểm thử.
- **Gửi lặp và đồng thời:** Chốt cách xử lý xác nhận phiếu đã xác nhận, thứ tự khóa và retry khi áp dụng kiểm soát đồng thời.

## 4. Thứ tự bàn giao chung

| Mốc | Backend 1 — Khoa | Backend 2 — Nhựt | Frontend có thể làm |
|---|---|---|---|
| 1 | Database, tenant, chuẩn API. | Đăng ký, login, me, RBAC. | Nối đăng nhập. |
| 2 | Chốt cấu trúc tồn kho và phiếu nhập. | Catalog, SKU, giá, barcode, tra cứu kho/nhà cung cấp. | Nối sản phẩm và form nhập. |
| 3 | Xác nhận nhập, ledger, WAC, API tồn. | Hoàn thiện refresh/logout, validation và tìm kiếm POS. | Chạy luồng nhập kho và tra cứu POS. |
| 4 | Test rollback, gửi lặp, đồng thời và reservation. | Test tenant, quyền, SKU và barcode. | E2E, sửa lỗi, nghiệm thu. |

Các mốc là thứ tự phụ thuộc, chưa gán ngày cụ thể. Tài liệu nguồn ghi 13–17/10/2026 nhưng gọi D1–D6: khoảng ngày này chỉ có 5 ngày, cần thống nhất lịch trước khi đặt hạn chót.

## 5. Checklist bàn giao

- [ ] Database sạch khởi tạo được bằng migration và seed.
- [ ] Các API phiếu nhập, ledger và tồn chạy thật trên Swagger.
- [ ] Phiếu nhập nhiều dòng xác nhận thành công và cập nhật đúng tồn/WAC/ledger.
- [ ] Xác nhận lặp không cộng tồn hai lần; lỗi giữa chừng rollback toàn bộ.
- [ ] Test cô lập tenant và phân quyền API kho đạt.
- [ ] Test WAC, ledger và reservation đạt; có báo cáo coverage theo phạm vi thống nhất.
- [ ] Có bằng chứng kiểm thử đồng thời cho tiêu chí Reservation nếu giữ nguyên yêu cầu này.
- [ ] Build/test đạt theo cẩm nang; đính kèm kết quả CI/test và bằng chứng Swagger vào PR.
- [ ] Swagger/DTO cập nhật; frontend và tester nhận được hướng dẫn chạy.
- [ ] Các lỗi Blocker/Critical trong phạm vi bàn giao đã đóng; kiểm thử nghiệm thu hoàn tất trước khi đánh dấu Done.

## 6. Giới hạn phạm vi cần thống nhất

- Checkout bán hàng hoàn chỉnh chưa được tài liệu nguồn xác định rõ là đầu ra bắt buộc. Không tự xem việc chuẩn bị DTO là đã hoàn thành checkout.
- Reservation được gọi là “nền tảng”, nhưng testcase Flash Sale và coverage đòi hỏi luồng giữ chỗ thực thi được nếu giữ nguyên yêu cầu.
- Sprint tập trung nhập kho. Ledger có thể được thiết kế hỗ trợ xuất/bán/trả, nhưng cần chốt riêng việc triển khai các nghiệp vụ đó trong sprint này.
- Sai sót ledger phải có hướng điều chỉnh bằng giao dịch bù; không sửa/xóa bản ghi cũ.

> Database v0.5 có guard tồn/ledger và kiểm tra snapshot tại COMMIT. Hai thành viên tích hợp theo [Quy ước database](../../database/README.md); các nhiệm vụ API/nghiệp vụ chưa được coi là hoàn thành bởi bản SQL này.
