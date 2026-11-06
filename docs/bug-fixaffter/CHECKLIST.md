# Checklist lỗi, Coming Soon và Upgrade

Cập nhật: **06/11/2026**. Nhánh đối chiếu: `feature/OISMS-46-fix-loi-ton-dong-nghiem-thu-va-deploy`.

Ký hiệu: `[x]` đã xong · `[~]` đã sửa nhưng cần kiểm chứng lại hoặc mới xong một phần · `[ ]` chưa làm.
Chi tiết từng lỗi nằm trong thư mục [`errors/`](./errors/), mỗi lỗi một file.

## Tóm tắt

| Nhóm | Đã xong | Cần kiểm chứng / một phần | Chưa làm |
| :--- | :---: | :---: | :---: |
| Lỗi `BF-001..035` | 21 | 5 | 9 |
| Coming Soon `CS-01..08` + Quên mật khẩu | 3 | 3 | 3 |
| Upgrade (GVHD) | 6 | 0 | 1 |

---

## A. Lỗi

### A1. Đợt rà soát 28–30/10/2026 (BF-001..020)

| | Mã | Ưu tiên | Khu vực | Lỗi | Trạng thái |
| :---: | :--- | :---: | :--- | :--- | :--- |
| [x] | [BF-001](./errors/BF-001-http-va-refresh-khong-gan-voi-chu.md) | P1 | Đăng nhập/HTTP | Refresh và replay 401 không gắn với chủ phiên | Đã sửa |
| [x] | [BF-002](./errors/BF-002-ket-noi-signalr-khong-duoc-thay-theo.md) | P1 | Realtime | Kết nối SignalR không được thay theo phiên | Đã sửa |
| [~] | [BF-003](./errors/BF-003-pos-doi-request-key-khi-thu-lai.md) | P1 | POS | Retry thanh toán sinh `requestKey` mới | Đã sửa ở POS Admin; POS riêng cần kiểm chứng lại |
| [x] | [BF-004](./errors/BF-004-tao-phieu-chuyen-kho-doi-key-khi.md) | P1 | Chuyển kho | Retry tạo phiếu sinh key mới | Đã sửa |
| [x] | [BF-005](./errors/BF-005-form-tao-kiem-ke-khong-giu-dinh.md) | P1 | Kiểm kê | Form tạo kiểm kê không giữ key khi retry | Đã sửa |
| [x] | [BF-006](./errors/BF-006-xoa-so-da-luu-roi-chot-co.md) | P1 | Kiểm kê | Xóa số đã lưu rồi chốt vẫn chốt số cũ | Đã sửa |
| [x] | [BF-007](./errors/BF-007-luu-ket-qua-dem-tu-gan-o.md) | P2 | Kiểm kê | Lưu ô đếm trống tự gán số sổ sách | Đã sửa |
| [x] | [BF-008](./errors/BF-008-drawer-chung-tu-bi-ket-loading-cua.md) | P2 | Chuyển/kiểm kê | Drawer kẹt loading của thao tác cũ | Đã sửa |
| [x] | [BF-009](./errors/BF-009-bao-cao-bi-phan-hoi-cu-ghi.md) | P1 | Báo cáo | Phản hồi cũ ghi đè bộ lọc mới | Đã sửa |
| [x] | [BF-010](./errors/BF-010-loi-bao-cao-van-de-so-cu.md) | P2 | Báo cáo | API lỗi vẫn hiện số cũ | Đã sửa |
| [x] | [BF-011](./errors/BF-011-sai-ngay-va-bien-thoi-gian-bao.md) | P1 | Báo cáo | Sai ngày Việt Nam và biên cuối ngày | Đã sửa |
| [x] | [BF-012](./errors/BF-012-ton-tren-form-chuyen-kho-khong-theo.md) | P2 | Chuyển kho | Tồn không theo kho nguồn | Đã sửa |
| [x] | [BF-013](./errors/BF-013-giu-cho-co-the-mat-y-dinh.md) | P1 | Giữ chỗ kho | Sửa form làm mất ý định chưa rõ kết quả | Đã sửa |
| [x] | [BF-014](./errors/BF-014-trang-chi-nhanh-kho-nuot-loi-tai.md) | P2 | Chi nhánh/kho | Lỗi API hiện thành danh sách rỗng | Đã sửa |
| [~] | [BF-015](./errors/BF-015-pos-xu-ly-thieu-loi-het-phien.md) | P2 | POS | Checkout 401 và ProblemDetails | Đã sửa ở POS Admin; POS riêng cần kiểm chứng lại |
| [x] | [BF-016](./errors/BF-016-dong-bo-realtime-ton-kho-chua-noi.md) | P2 | Realtime | Không join nhóm chi nhánh, không cập nhật tồn | Đã sửa |
| [x] | [BF-017](./errors/BF-017-link-thong-bao-canh-bao-khong-mo.md) | P2 | Báo cáo | Link `?tab=alerts` mở sai tab | Đã sửa |
| [x] | [BF-018](./errors/BF-018-bao-cao-khong-loc-duoc-rieng-don.md) | P2 | Báo cáo | Thiếu bộ lọc kênh Admin | Đã sửa |
| [~] | [BF-019](./errors/BF-019-pos-nhan-http-200-sai-contract-la.md) | P2 | POS | HTTP 200 thiếu mã đơn bị coi là thành công | Đã sửa ở POS Admin; POS riêng cần kiểm chứng lại |
| [~] | [BF-020](./errors/BF-020-pos-rieng-chua-khoa-gio-va-ngu.md) | P1 | POS riêng | Checkout cũ xóa giỏ mới khi đổi chi nhánh | Bản sửa cũ không còn; POS mới có cơ chế pending riêng, cần kiểm chứng lại |

### A2. Phát hiện sau nghiệm thu, 04–06/11/2026 (BF-021..027)

| | Mã | Ưu tiên | Khu vực | Lỗi | Trạng thái |
| :---: | :--- | :---: | :--- | :--- | :--- |
| [ ] | [BF-021](./errors/BF-021-bao-cao-lai-gop-lo-cho-staff.md) | P1 | Backend/Báo cáo | `revenue-profit` trả giá vốn, lãi gộp cho Staff | Chưa sửa |
| [ ] | [BF-022](./errors/BF-022-ton-kho-lo-gia-von-cho-cashier.md) | P1 | Backend/Tồn kho | `inventory/balances` trả `averageCost` cho Cashier | Chưa sửa |
| [ ] | [BF-023](./errors/BF-023-staff-duoc-tao-sua-kho.md) | P2 | Backend/Tổ chức | Staff được tạo, sửa, bật/tắt kho | Chưa sửa, cần quyết định |
| [ ] | [BF-024](./errors/BF-024-tim-kiem-phieu-nhap-khong-mo-dung-phieu.md) | P2 | Admin/Tìm kiếm | Kết quả phiếu nhập không mở đúng phiếu | Chưa sửa |
| [ ] | [BF-025](./errors/BF-025-integration-test-crlf-tren-windows.md) | P2 | Test | Test AC01 fail trên Windows do CRLF | Chưa sửa |
| [ ] | [BF-026](./errors/BF-026-health-ready-lo-thong-tin-db.md) | P3 | Deploy | `/health/ready` công khai trả tên user DB | Chưa sửa |
| [ ] | [BF-027](./errors/BF-027-tong-so-luong-bao-cao-cong-khac-don-vi.md) | P3 | Backend/Báo cáo | `total*Quantity` cộng gộp khác đơn vị | Chưa sửa |

### A3. Rà soát lại Clean Architecture, 06/11/2026 (BF-028..035)

Chia thành nhiều đợt fix, xem [mục E](#e-kế-hoạch-hoàn-thiện-clean-architecture-bf-028035).

| | Mã | Ưu tiên | Tầng | Lỗi | Đợt |
| :---: | :--- | :---: | :--- | :--- | :---: |
| [x] | [BF-028](./errors/BF-028-hub-signalr-truy-van-thang-dbcontext.md) | P2 | WebApi | `NotificationHub` truy vấn thẳng `IAppDbContext` và `IgnoreQueryFilters` | 2 |
| [x] | [BF-029](./errors/BF-029-readiness-check-npgsql-nam-trong-webapi.md) | P2 | WebApi | Readiness check Npgsql (~180 dòng) nằm trong `Program.cs`, WebApi tham chiếu `Npgsql` | 2 |
| [x] | [BF-030](./errors/BF-030-controller-bat-loi-postgresql.md) | P2 | WebApi | `GoodsReceiptsController` bắt `PostgresException` 23505 | 1 |
| [x] | [BF-031](./errors/BF-031-controller-tu-anh-xa-exception-trung-lap.md) | P2 | WebApi | 19 controller tự ánh xạ exception, body lỗi không đồng nhất; 25 bản sao `GetCurrentUserId`; 14 kiểm tra `ModelState` thừa | 1 |
| [ ] | [BF-032](./errors/BF-032-application-chua-sql-postgresql-tho.md) | P2 | Application | SQL PostgreSQL thô trong 6 service (nợ đã biết) | 3 |
| [x] | [BF-033](./errors/BF-033-application-bat-postgresexception.md) | P2 | Application | Bắt `PostgresException` ở 8 chỗ, 7 service | 2 |
| [ ] | [BF-034](./errors/BF-034-iappdbcontext-lo-chi-tiet-ha-tang.md) | P2 | Application | `IAppDbContext` lộ SQL/transaction EF; 38 nhánh `IsRelationalDatabase` | 3 |
| [~] | [BF-035](./errors/BF-035-domain-anemic-trang-thai-chuoi.md) | P3 | Domain | Entity anemic, 59 chỗ so trạng thái bằng chuỗi, `MediatR` không dùng | 4 |

---

## B. Chức năng Coming Soon

Không phải lỗi: đây là chức năng chưa làm xong, đang có nhãn Coming soon hoặc bị ẩn. Nguồn: [báo cáo nghiệm thu frontend](../report-after-upgrade/frontend-coming-soon-va-upgrade.md).

| | Mã | Chức năng | Hiện trạng | Còn thiếu |
| :---: | :--- | :--- | :--- | :--- |
| [ ] | CS-01 | Nhận hàng trả từ khách | Chưa làm. Schema có `sales_returns`, `sales_return_items`, chưa có API/UI | Chọn đơn gốc, kiểm lượng đã bán/đã trả, ghi `IN` bù theo `BaseQuantity`, `FactorSnapshot` của đơn gốc; chốt chính sách hoàn tiền |
| [~] | CS-02 | Nhân viên, phân quyền, phân công chi nhánh | Có danh sách, tạo, sửa, khóa nhân viên (`/api/users`) | Phân công chi nhánh: chưa có trường ở backend, cần quyết định làm hay bỏ khỏi yêu cầu |
| [x] | CS-03 | Ngưỡng cảnh báo tồn (`ReorderPoint`) | Đã có API và UI | Chưa kiểm tra thông báo tồn thấp qua realtime |
| [x] | CS-04 | Tìm kiếm toàn hệ thống | Đã có `GET /api/search` và ô tìm kiếm | Lỗi mở phiếu nhập: [BF-024](./errors/BF-024-tim-kiem-phieu-nhap-khong-mo-dung-phieu.md) |
| [~] | CS-05 | Thanh toán QR | POS PWA có VietQR, thu ngân xác nhận thủ công | POS Admin chưa bật QR; không có webhook ngân hàng, đối soát tự động |
| [ ] | CS-06 | In chứng từ giao hàng, in báo cáo | Chưa làm | Bố cục A4/80mm, cô lập vùng in, ẩn giá vốn theo vai trò |
| [x] | CS-07 | KPI, biểu đồ, đơn gần đây ở Tổng quan | Đã có dữ liệu thật | Số liệu lũy kế, API chưa lọc theo thời gian |
| [~] | CS-08 | Dự báo, gợi ý nhập hàng (`FR-AI-01`) | Có API `GET /api/ai/reorder-recommendations`; **tab đang tạm ẩn** trong `InventoryPage.tsx` | Bằng chứng đánh giá dự báo; chốt thời điểm mở lại tab |
| [ ] | — | Quên mật khẩu | Nút tắt có nhãn Coming soon ở Admin và POS PWA | API và luồng đặt lại mật khẩu |

---

## C. Upgrade nghiệp vụ theo GVHD

Đặc tả: [`docs/Upgrade-recommended/`](../Upgrade-recommended/README.md). Báo cáo: [`docs/report-after-upgrade/`](../report-after-upgrade/frontend-coming-soon-va-upgrade.md).

| | Gói | Nội dung | Backend | Frontend | Báo cáo |
| :---: | :--- | :--- | :---: | :---: | :--- |
| [x] | Upgrade 06 | Rà soát Clean Architecture: tách nghiệp vụ khỏi 8 controller, `IAppDbContext` sạch, `IPasswordHasher`, `ISqlLockService` | ✅ | — | [clean-architecture.md](../report-after-upgrade/clean-architecture.md) |
| [x] | Upgrade 05 | Nhiều đơn vị tính: `units`, `sku_units`, `BaseQuantity = Quantity × FactorSnapshot`, WAC theo đơn vị gốc | ✅ | ✅ | [frontend-coming-soon-va-upgrade.md](../report-after-upgrade/frontend-coming-soon-va-upgrade.md) |
| [x] | Upgrade 04 | Bảng giá theo thời gian, phiếu đổi giá, đóng API sửa giá trực tiếp | ✅ | ✅ | [bang-gia-theo-thoi-gian.md](../report-after-upgrade/bang-gia-theo-thoi-gian.md) |
| [x] | Upgrade 01-A | Phiếu mua hàng, phiếu nhập bắt buộc gắn phiếu mua đã duyệt, chặn nhập vượt | ✅ | ✅ | [mua-hang-nhap-kho.md](../report-after-upgrade/mua-hang-nhap-kho.md) |
| [ ] | Upgrade 01-B | Hóa đơn nhà cung cấp (`supplier_invoices`) và chứng từ chênh lệch (`purchase_variances`) | ❌ | ❌ | Chờ GVHD chốt cách xử lý |
| [x] | Upgrade 02 | Hóa đơn bán, in lại, truy vết đến dòng sổ `OUT` | ✅ | ✅ | [hoa-don-truy-vet-xuat-kho.md](../report-after-upgrade/hoa-don-truy-vet-xuat-kho.md) |
| [x] | Upgrade 03 | Lịch kiểm kê, kỳ kiểm kê, duyệt chênh lệch (`PendingApproval`) | ✅ | ✅ | [lich-kiem-ke-duyet-chenh-lech.md](../report-after-upgrade/lich-kiem-ke-duyet-chenh-lech.md) |

Lưu ý: kỳ kiểm kê chỉ tự sinh khi bật Hangfire (`Hangfire:Enabled=true`). Mặc định và trên bản demo AWS đang tắt.

---

## D. Việc cần nhóm quyết định

- [ ] BF-021: chặn Staff ở endpoint hay ẩn trường lãi gộp trong dữ liệu trả về.
- [ ] BF-023: Staff có được quản lý kho không (sửa code hay sửa yêu cầu).
- [ ] CS-02: làm phân công chi nhánh hay bỏ khỏi yêu cầu.
- [ ] CS-08: thời điểm mở lại tab Gợi ý nhập hàng.
- [ ] Upgrade 01-B: cách xử lý chênh lệch hóa đơn nhà cung cấp.
- [ ] Màn Handoff: bỏ đặc tả [07-handoff-screens.md](../11-ui-screens/07-handoff-screens.md) hay làm lại.
- [ ] Đơn vị mặc định cho phép 3 chữ số lẻ, nên POS nhận cả số lẻ với "Cái": có cấm hay không.
- [ ] Kiểm chứng lại BF-003, BF-015, BF-019, BF-020 trên `pos-app` hiện tại.

---

## E. Kế hoạch hoàn thiện Clean Architecture (BF-028..035)

Kết quả rà soát lại ngày 06/11/2026: phụ thuộc giữa các project đã đúng (Domain không phụ thuộc gì; Application → Domain; Infrastructure → Application, Domain; WebApi → Application, Infrastructure), không controller nào còn gọi `DbContext`. Nhưng bên trong vẫn còn các chỗ lệch ở trên. Kế hoạch ban đầu chia mỗi đợt một nhánh và PR riêng. Đợt 1 và 2 được đưa vào PR #38 theo chỉ đạo cập nhật, với các hợp đồng API hiện hành.

| Đợt | Lỗi | Nội dung | Rủi ro | Bằng chứng cần có |
| :---: | :--- | :--- | :---: | :--- |
| [x] 1 | BF-030, BF-031 | Dùng bộ lọc MVC và middleware chung cho phản hồi lỗi, bỏ các `try/catch` trong controller và kiểm tra `ModelState` lặp, dùng chung cách đọc user ID, chuyển bắt lỗi trùng mã phiếu nhập vào service | Thấp | Toàn bộ integration test, `npm test` POS và đối chiếu hợp đồng lỗi |
| [x] 2 | BF-028, BF-029, BF-033, gỡ MediatR (BF-035) | Hub gọi service, readiness check chuyển sang Infrastructure và dùng chung danh sách bảng với migration, Infrastructure triển khai `IDbErrorTranslator` | Thấp–TB | Build, unit test, integration test auth, sản phẩm, kho, giá và readiness |
| [ ] 3 | BF-032, BF-034 | Gom SQL khóa vào `ISqlLockService` có ngữ nghĩa, thêm `IUnitOfWork`, bỏ `ExecuteSqlRawAsync` và nhánh `IsRelationalDatabase`. Làm từng service, giữ nguyên thứ tự khóa | **Cao** | Test đồng thời và idempotency trên **PostgreSQL thật** cho từng service |
| [ ] 4 | BF-035 | Kiểu trạng thái, luật chuyển trạng thái trong entity, tách `OrderService` | TB | Unit test entity, integration test hiện có |

Đợt 1 và 2 đã qua build backend .NET 8, toàn bộ `dotnet test src/backend/OISM.sln --no-build -c Release --verbosity normal` với PostgreSQL 16, build Admin/POS và test POS trên commit `c95c21c`. [GitHub Actions #134](https://github.com/khoalvd839764-netizen/OISMS/actions/runs/42185858352)

Nguyên tắc:
- Đợt 3 chạm transaction, khóa, ledger, reservation, idempotency: cần nhóm đồng ý trước (CLAUDE.md đang ghi đây là nợ đã biết, "không tiện tay refactor"), nên làm sau khi sửa BF-021, BF-022 và sau buổi nghiệm thu.
- Mỗi đợt không đổi hợp đồng API (status, `errorCode`, hình dạng body) trừ khi đã chốt với frontend; nếu đổi thì cập nhật `docs/05-api-contracts` và `docs/12-error-and-status-codes` cùng PR.
- Báo kết quả theo PASS/FAIL/NOT RUN, ghi rõ lệnh đã chạy.
