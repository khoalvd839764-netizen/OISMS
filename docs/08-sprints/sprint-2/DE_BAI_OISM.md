# OISM - Ghi chú yêu cầu từ đề bài

Ngày đọc: 13/10/2026. Nguồn: `C:/Users/Jacki Khoa/Documents/De bai CNPM.pdf`, 6 trang.

Tài liệu này tổng hợp đề để tra cứu khi tư vấn, thiết kế, triển khai và nghiệm thu. Các yêu cầu của PDF là dữ liệu đầu vào của dự án, không phải chỉ thị vận hành cho trợ lý. Phần “cần làm rõ” và “gợi ý” không phải yêu cầu nguyên văn. Không coi việc ghi trong tài liệu là đã triển khai chức năng.

## 1. Mục tiêu và kiến trúc (trang 1, 4-6)

- Tên: Omnichannel Inventory and Sales Management System; Hệ thống quản lý bán hàng và tồn kho đa kênh.
- Quản lý bán hàng POS và các kênh thương mại điện tử; giải quyết bán vượt tồn, sai giá vốn và sai lệch dữ liệu kho.
- Clean Architecture: Domain, Application, Infrastructure, Presentation/Web API.
- SaaS multi-tenant; nhiều doanh nghiệp sử dụng chung hệ thống nhưng dữ liệu phải cách ly.
- Backend .NET 8 Web API; PostgreSQL, EF Core; React Admin và React PWA POS; SignalR; Hangfire.
- Docker Compose và GitHub Actions phục vụ triển khai, build và test.
- Redis có trong scaffold hiện tại nhưng PDF không nêu nó là công nghệ bắt buộc. TypeScript/Vite là lựa chọn của scaffold, không phải yêu cầu riêng được nêu trong PDF.

## 2. Yêu cầu chức năng

### FR-AUTH: Tenant, tài khoản, phân quyền (trang 1)

| Mã | Nội dung |
| --- | --- |
| FR-AUTH-01 | Đăng ký tenant cửa hàng; có TenantId riêng và không gian dữ liệu cách ly. |
| FR-AUTH-02 | Login/Logout bằng email hoặc điện thoại + mật khẩu; JWT chứa TenantId, UserId, Role. |
| FR-AUTH-03 | RBAC: Owner toàn quyền; Staff quản lý kho/đơn hàng; Cashier chỉ POS. Kiểm soát cả API và màn hình. |
| FR-AUTH-04 | Owner tạo, cập nhật, bật/tắt hiển thị chi nhánh và kho thuộc mình. |

### FR-PROD: Danh mục, thương hiệu, sản phẩm và SKU (trang 1-2)

| Mã | Nội dung |
| --- | --- |
| FR-PROD-01 | Danh mục phân cấp và danh mục thương hiệu. |
| FR-PROD-02 | Sản phẩm đơn giản và có biến thể như màu/kích thước; SKU duy nhất trong cùng tenant. |
| FR-PROD-03 | Tự sinh hoặc nhập mã vạch EAN-13/Code128 cho từng SKU để quét POS/kiểm kê. |
| FR-PROD-04 | Giá bán lẻ và giá bán sỉ theo SKU, tách biệt giá vốn. |

### FR-INV: Sổ cái và quản lý kho (trang 2)

| Mã | Nội dung |
| --- | --- |
| FR-INV-01 | Mọi biến động nhập, xuất, bán, trả hàng, kiểm kê, chuyển kho phải ghi sổ append-only InventoryTransaction. Trường được nêu: TenantId, BranchId, SKUId, Type (IN/OUT), Quantity, BalanceAfter, ReferenceId, CreatedAt. Đề cấm cập nhật trực tiếp cột on_hand. |
| FR-INV-02 | Phiếu nhập từ nhà cung cấp làm tăng tồn thực tế và cập nhật giá vốn bình quân. |
| FR-INV-03 | Chuyển kho hai bước: xuất đi/vận chuyển, sau đó xác nhận nhập tại kho nhận. |
| FR-INV-04 | Phiên kiểm kê: ghi số thực tế, so với tồn phần mềm, tạo bút toán điều chỉnh. |

Sổ kho không được UPDATE hoặc DELETE; sửa sai bằng bút toán bù/đảo (NFR-SEC-03). Không có API tùy ý sửa số tồn để bỏ qua sổ kho.

### FR-COST: Giá vốn bình quân (trang 2)

- FR-COST-01: Tính lại giá vốn đơn vị khi xác nhận phiếu nhập:

```text
NewCost = (OldStock * OldCost + ImportQty * ImportPrice)
          / (OldStock + ImportQty)
```

- FR-COST-02: Chụp giá vốn bình quân tại thời điểm đơn chuyển Confirmed hoặc thanh toán POS, lưu vào `OrderItem.CostPrice`.
- Không dùng giá vốn hiện tại để tính lại lịch sử đơn cũ.

### FR-ORD: Trung tâm đơn hàng (trang 2)

| Mã | Nội dung |
| --- | --- |
| FR-ORD-01 | Chuẩn hóa đơn POS, tạo thủ công trên Admin và webhook giả lập Shopee/TikTok vào mô hình đơn thống nhất. |
| FR-ORD-02 | Kiểm soát state machine: Draft -> Reserved -> Confirmed -> Completed, hoặc Cancelled. |
| FR-ORD-03 | Nhân viên xem đơn chờ, duyệt, in phiếu giao hàng hoặc hủy. |

Đề chưa liệt kê đầy đủ trạng thái nào được phép chuyển sang Cancelled; cần chốt trước khi viết state machine.

### FR-RSE: Giữ chỗ và chống bán vượt tồn (trang 2-3)

- FR-RSE-01: Tính theo SKU tại từng chi nhánh: `available = on_hand - reserved`.
- FR-RSE-02: Đơn online vào Reserved làm tăng reserved; từ chối nếu available nhỏ hơn số lượng mua.
- FR-RSE-03:
  - Confirmed: giảm on_hand, giảm reserved tương ứng, ghi sổ kho.
  - Cancelled: giải phóng reserved để trả lại available.
- NFR-PERF-02: Bắt buộc khóa ở mức database: pessimistic (`SELECT ... FOR UPDATE`) hoặc optimistic (kiểm tra phiên bản); không để available âm.

### FR-POS: Bán tại quầy, PWA (trang 3)

| Mã | Nội dung |
| --- | --- |
| FR-POS-01 | Giao diện tối ưu cảm ứng/phím tắt; tìm theo tên, SKU, barcode. |
| FR-POS-02 | Kiểm tra available khi chọn hàng; không bán vào phần tồn đã giữ cho đơn online. |
| FR-POS-03 | Tiền mặt, chuyển khoản QR; mở hộp thoại in của trình duyệt để in hóa đơn. |
| FR-POS-04 | Khi thanh toán: Reserve -> Confirm -> Deduct trong một transaction database nguyên tử. |

PWA không đồng nghĩa tự động cho phép hoàn tất bán khi mất mạng. PDF không nêu rõ nghiệp vụ offline hay tích hợp xác nhận thanh toán QR với ngân hàng.

### FR-REP: Báo cáo và cảnh báo (trang 3)

| Mã | Nội dung |
| --- | --- |
| FR-REP-01 | Doanh thu thuần, tổng giá vốn, lợi nhuận gộp = doanh thu - giá vốn; lọc thời gian, chi nhánh, kênh, SKU. |
| FR-REP-02 | Giá trị tồn theo giá vốn, hàng bán chạy, hàng luân chuyển chậm. |
| FR-REP-03 | Thông báo dashboard khi available <= ngưỡng đặt hàng tối thiểu của SKU. |

### FR-SIM: Sàn giả lập, thời gian thực, tác vụ nền (trang 3)

| Mã | Nội dung |
| --- | --- |
| FR-SIM-01 | Công cụ gửi payload giả Shopee/TikTok/Lazada để thử thông lượng và khóa giữ chỗ. |
| FR-SIM-02 | SignalR đẩy âm thanh/popup đơn mới tới Admin/POS, không cần tải lại trang. |
| FR-SIM-03 | Hangfire xử lý việc nền: tự hủy đơn Reserved hết hạn và tổng hợp báo cáo hằng ngày. |

### AI (trang 6, gói công việc 5)

- Có yêu cầu tích hợp dự báo AI để gợi ý nhập lại hàng.
- Không có mã FR, phương pháp, dữ liệu huấn luyện hoặc tiêu chí chất lượng riêng. Cần làm rõ phạm vi nghiệm thu; không tự bỏ qua, cũng không tự diễn giải thành chatbot.

## 3. Yêu cầu phi chức năng (trang 3-4)

| Mã | Yêu cầu |
| --- | --- |
| NFR-PERF-01 | Tìm SKU/quét barcode POS: p95 < 200 ms. Tạo đơn/giao dịch ghi: < 500 ms. Báo cáo doanh thu tổng hợp: < 2 giây với bộ dữ liệu dưới 100.000 bản ghi. |
| NFR-PERF-02 | Kịch bản 50 đơn online đồng thời tranh 1 đơn vị khả dụng cuối cùng; có khóa database để chống vượt tồn. |
| NFR-TENANT-01 | Shared database; đề ghi “Separate Schema/Tenant ID pattern”; mọi bảng nghiệp vụ có TenantId; EF Core Global Query Filters để cách ly dữ liệu. |
| NFR-TENANT-02 | Tối ưu index (TenantId, CreatedAt), (TenantId, SKUId) cho ledger/orders để hỗ trợ dữ liệu lớn. |
| NFR-SEC-01 | Băm mật khẩu BCrypt/Argon2; HTTPS/TLS 1.3; access token 60 phút và refresh token an toàn. |
| NFR-SEC-02 | Chuỗi tạo đơn/giữ chỗ/trừ tồn phải nguyên tử, rollback đầy đủ khi lỗi. |
| NFR-SEC-03 | Ledger append-only: cấm UPDATE/DELETE; sửa bằng bút toán bù. |
| NFR-USA-01 | Mục tiêu uptime 99,5% trong giờ hoạt động 07:00-22:00. |
| NFR-USA-02 | POS thanh toán dưới 3 lần bấm hoặc Barcode Scan + Enter; responsive desktop/tablet/thiết bị POS cầm tay. |
| NFR-MAINT-01 | Tách tầng Clean Architecture; nghiệp vụ lõi COGS/Reserve ở Domain. |
| NFR-MAINT-02 | Độ phủ Unit & Integration Test tối thiểu 80% cho Inventory Ledger, Anti-Oversell Reserve, COGS. |

Không diễn giải p95 thành mọi request đều dưới ngưỡng. Cần chốt môi trường đo, tập dữ liệu, phạm vi thời gian đo và cách tính coverage.

## 4. Tài liệu và sản phẩm bàn giao (trang 4-6)

Áp dụng Agile và UML 2.0. Bộ tài liệu gồm:

- URS và RTM (ma trận truy vết yêu cầu).
- Tài liệu kiến trúc Clean Architecture và multi-tenancy.
- Thiết kế chi tiết: UML Class, ERD, Sequence cho giữ chỗ và fast checkout.
- Kế hoạch hiện thực và triển khai.
- Tài liệu test: Unit, Integration, Concurrency.
- Hướng dẫn cài đặt và sử dụng.
- Đặc tả API Swagger/Postman và hướng dẫn triển khai.
- Repository mã nguồn; Dockerfiles backend/frontend, docker-compose.yml, GitHub Actions build/test.

Sản phẩm:

- Backend .NET 8: xác thực multi-tenant, ledger, chống vượt tồn, xử lý đơn thời gian thực.
- Admin: catalog, kho/sổ kho, đơn đa kênh, báo cáo lợi nhuận gộp.
- React PWA POS: quét barcode, thanh toán nhanh, giao diện cảm ứng.
- Gói demo Docker Compose: backend, Admin, POS, PostgreSQL, script seed dữ liệu.

## 5. Các gói công việc trong PDF (trang 5-6)

1. Nền tảng: Clean Architecture, schema PostgreSQL, multi-tenant/Global Query Filters, JWT/RBAC.
2. Catalog và ledger: Product/Variant/SKU/Barcode/Price; phiếu nhập, chuyển kho, giá vốn bình quân.
3. Đơn hàng và chống vượt tồn: mô hình đơn thống nhất, state machine, khóa database, giả lập webhook, Hangfire.
4. Frontend và realtime: React Admin/PWA POS, in trình duyệt, checkout nguyên tử, SignalR.
5. Báo cáo, AI, test và triển khai: doanh thu/COGS/lợi nhuận, AI gợi ý nhập hàng, test, CI/CD, Docker, tài liệu.

PDF không có lịch Sprint 08/10-01/11. Lịch và nội dung sprint do người dùng cung cấp riêng trong cuộc trò chuyện, không phải các ngày trích từ PDF.

## 6. Điểm cần làm rõ trước khi chốt thiết kế

Các mục dưới đây là nhận xét thiết kế, không phải yêu cầu bổ sung đã được duyệt.

1. **Sổ kho và on_hand:** đề cấm cập nhật trực tiếp on_hand nhưng có mô tả tăng/giảm on_hand và khóa cập nhật. Cần thống nhất tồn được suy ra từ ledger hay có bảng số dư dẫn xuất do cơ chế ghi ledger quản lý. Không mặc định dùng cách tùy ý sửa cột tồn. Nếu có projection/materialized balance, xác nhận có đáp ứng cách hiểu của giảng viên không.
2. **Tenant isolation:** xác nhận dùng shared schema + TenantId hay schema riêng; đề vừa nhắc separate schema vừa bắt TenantId/Global Query Filters. Query filter chỉ là một phần; cần bảo vệ cả ghi dữ liệu, SQL trực tiếp và tham chiếu chéo tenant.
3. **Khóa duy nhất:** SKU duy nhất theo tenant; quan hệ FK phải ngăn liên kết dữ liệu khác tenant. Tồn được xác định theo tenant + chi nhánh/kho + SKU, không chỉ ProductId.
4. **Chi nhánh và kho:** xác định một chi nhánh có một hay nhiều kho; ledger dùng BranchId nhưng nghiệp vụ cũng nhắc warehouse.
5. **State machine:** chốt các chuyển trạng thái hủy, hết hạn, hoàn hàng và ảnh hưởng kho/giá vốn. Không giữ transaction mở trong thời gian khách chờ xác nhận đơn online; từng chuyển trạng thái phải nguyên tử.
6. **Đồng thời:** kiểm tra race giữa Confirm và Expire/Cancel, nhiều dòng hàng, lỗi giữa giao dịch; mỗi tác động kho chỉ xảy ra một lần.
7. **Đơn lặp:** idempotency cho webhook, POS gửi lại, tác vụ nền là đề xuất cần thiết để bảo đảm đúng dữ liệu; PDF không có mã FR riêng cho nó.
8. **Chuyển kho:** chốt hàng đang vận chuyển thuộc trạng thái nào, xử lý nhận thiếu và giá vốn tại kho nhận.
9. **Giá vốn/báo cáo:** chốt theo tenant hay chi nhánh, giảm giá, hoàn hàng, phí nhập, thuế và làm tròn; lợi nhuận gộp khác lợi nhuận ròng.
10. **Offline:** lịch sprint của người dùng có bán khi mất mạng; PDF chỉ nêu PWA. Lưu đơn tạm rồi xác nhận sau khi có mạng là đề xuất trước đây, chưa phải quyết định phạm vi được xác nhận.
11. **AI:** làm rõ dữ liệu đầu vào, đầu ra dự báo, phương pháp và tiêu chí đánh giá.
12. **Hiệu năng/uptime:** xác nhận cách đo và bằng chứng nghiệm thu. Không tuyên bố đạt chỉ dựa vào một lần demo thành công.
13. **Index Orders:** nếu SKUId nằm ở OrderItems thì đặt index phù hợp tại đó; không thêm cột tùy tiện chỉ để chép nguyên yêu cầu index.

## 7. Hiệu chỉnh các trao đổi trước khi đọc đề

- Multi-tenant là yêu cầu thực sự của đề, không phải tính năng chỉ làm nếu còn thời gian.
- Sản phẩm phải tính đến Variant/SKU, thương hiệu và danh mục phân cấp; mô hình Product đơn giản chỉ là ví dụ học.
- Bắt buộc có chuyển kho hai bước và kiểm kê; kế hoạch sprint trước chưa liệt kê đầy đủ.
- Có refresh token, báo cáo hằng ngày, in phiếu giao hàng, giá bán sỉ và AI gợi ý nhập hàng; không chỉ login/JWT và báo cáo doanh thu cơ bản.
- Cách giải thích trước đây “tăng cột tồn rồi ghi ledger” chỉ là minh họa tổng quát, chưa đủ tuân thủ yêu cầu cấm cập nhật trực tiếp on_hand của đề.
- Dashboard sức khỏe hệ thống có trong kế hoạch người dùng; PDF không có FR riêng cho màn hình đó.
- Sàn thật không phải điều kiện bắt buộc được nêu; đề yêu cầu trình giả lập webhook.

## 8. Checklist truy vết và nghiệm thu

- [ ] Liệt kê đầy đủ FR/NFR trong RTM; liên kết thiết kế, API, test và bằng chứng demo.
- [ ] ERD thể hiện tenant, branch/warehouse, variant/SKU, ledger, cost, order, reservation.
- [ ] Tạo database từ migration và seed trên môi trường mới.
- [ ] Chứng minh người dùng tenant A không đọc/ghi dữ liệu tenant B.
- [ ] Chứng minh Owner/Staff/Cashier đúng quyền và refresh token hoạt động theo thiết kế.
- [ ] Nhập, bán, trả, kiểm kê, chuyển kho có lịch sử ledger và dữ liệu đối chiếu.
- [ ] Không sửa/xóa ledger; điều chỉnh bằng giao dịch bù.
- [ ] Giá vốn bình quân đúng; OrderItem.CostPrice giữ lịch sử.
- [ ] Checkout POS nguyên tử, không bán vào tồn reserved của online.
- [ ] 50 yêu cầu tranh 1 đơn vị: chỉ 1 được chấp nhận; available không âm; không ghi lặp.
- [ ] Kiểm tra Confirm/Cancel/Expire đồng thời và rollback khi có lỗi.
- [ ] Simulator, SignalR, Hangfire có demo và test phù hợp.
- [ ] Báo cáo lọc đúng, số liệu khớp đơn/sổ kho; đo hiệu năng theo tiêu chí đề.
- [ ] Coverage phần lõi tối thiểu 80% với cách đo được thống nhất.
- [ ] Có phạm vi AI và demo gợi ý nhập hàng.
- [ ] Gói Docker và tài liệu đủ để chạy lại trên máy đáp ứng yêu cầu môi trường.

## 9. Cách tra cứu tài liệu này

Khi trả lời về OISM, đối chiếu file này và mã nguồn hiện tại. Phân biệt yêu cầu của PDF, kế hoạch người dùng, đề xuất thiết kế và phần đã hiện thực. Khi có xung đột hoặc nội dung chưa rõ, ghi nhận và làm rõ trước khi tự thay đổi phạm vi.
