# CHECKLIST TIẾN ĐỘ ĐỒ ÁN OISMS — SPRINT 2, SPRINT 3 & RÀ SOÁT SẢN PHẨM

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-CHK-S2` |
| **Phiên bản (Version)** | `v1.2.0` |
| **Ngày cập nhật** | `2026-10-29` (Asia/Saigon) |
| **Nguồn truy vết gốc** | `C:\Users\Jacki Khoa\Documents\De bai CNPM.pdf` (Trang 1 – 6) & Confluence Page ID `5275650` (Version 9) |
| **Bằng chứng kiểm thử Sprint 2** | **194/194 Tests Pass (100%)** — 103 Unit Tests + 91 Integration Tests trên PostgreSQL 16+ thật; Code Coverage Core Inventory đạt **`92.49% – 94.83%`** |
| **Nguồn cập nhật Sprint 3** | [Phân công Sprint 3 lưu trong repo, Confluence v11](docs/08-sprints/sprint-3/README.md) và [báo cáo tổng kết Đăng Khoa](docs/09-personal-reports/dang-khoa/sprint-3/SUMMARY.md); không tra cứu/cập nhật Confluence live trong lượt này |
| **Hiện trạng sản phẩm mới nhất** | [Rà soát 28–29/10/2026](docs/bug-fixaffter/README.md): **20 nhóm lỗi OPEN**, **8 mục chưa hoàn thiện**; xem mục **0A** trước khi kết luận sản phẩm đã chạy đầy đủ |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả nội dung & Chi tiết rà soát thay đổi (Change Check) | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | • Tổng hợp toàn bộ yêu cầu đề bài (`28 FR + AI`, `11 NFR`, `5 Task Packages`, `6 Deliverables`) và đánh dấu nghiệm thu `[x]` các hạng mục đã hoàn thành trong Sprint 2.<br/>• **Check thay đổi khi đối chiếu Confluence (`Page ID: 5275650`, Version 9):** Đồng bộ chính xác lịch trình (`S2: 13–17/10`, `S3: 18–22/10`, `S4: 23–27/10`, `S5: 28/10–01/11`) và phân công 5 thành viên cho các hạng mục chưa hoàn thành `[ ]`. | `INIT-SYNC-CHK-S2` |
| `v1.1.0` | `2026-10-27` | Lê Võ Đăng Khoa — Backend 1 | Cập nhật checklist BE1 Sprint 3 theo hồ sơ T01–T06, B-01 và PR #21; tách phần backend đã đạt khỏi UI, API đọc/báo cáo BE2 và công việc Sprint 4; đồng bộ phân công Simulator theo bản v11 đã lưu trong repo. | `SYNC-BE1-S3-CHECKLIST` |
| `v1.2.0` | `2026-10-29` | Rà soát sản phẩm | Bổ sung checklist hiện trạng Admin/POS, 20 nhóm lỗi và 8 mục Coming soon; giữ nguyên bằng chứng Sprint 2/BE1 lịch sử, không tự nâng thành nghiệm thu toàn sản phẩm. | `docs/bug-fixaffter/` |

---

## 0. Checklist Công Việc Đăng Khoa — Backend 1 Sprint 3

Chi tiết 42 hạng mục đã triển khai và 5 mục chưa xác nhận hoàn tất nằm tại [SUMMARY.md](docs/09-personal-reports/dang-khoa/sprint-3/SUMMARY.md). Các dấu `[x]` Sprint 2 bên dưới giữ ý nghĩa tại baseline lịch sử; không phải kết quả chạy kiểm thử mới ngày 27/10/2026.

- [x] **T01 — Nền tảng và hợp đồng API:** Domain entities, EF mapping/DbSet, DTO/service interface, RequestKey và schema bổ sung. Trạng thái DONE theo báo cáo gốc, chưa có biên bản nghiệm thu độc lập riêng.
- [x] **T02 — POS checkout nguyên tử:** Toàn giỏ trong một transaction; khóa/kiểm tra tồn khả dụng, trừ tồn, lưu giá vốn, payment/OUT và chống thanh toán trùng. VERIFIED v1.2.
- [x] **T03 — Vòng đời đơn online:** Giữ hàng, xác nhận xuất, hủy và hoàn tất; rollback, retry và tranh chấp confirm/cancel. VERIFIED v1.1.
- [x] **T04A — Chuyển kho hai bước:** Lập phiếu, dispatch/receive, giá vốn xuất chuyển, ledger và hủy Draft; chống trùng, concurrency và rollback. VERIFIED v1.2.
- [x] **T04B — Kiểm kê kho:** Snapshot tồn/sequence, ghi số đếm, tạo chứng từ điều chỉnh liên kết, kiểm tra drift và ghi IN/OUT đúng một lần. VERIFIED v1.3.
- [x] **T05 — Lõi webhook và SignalR:** Nhận payload ba kênh, xác thực/ánh xạ shop, chống trùng, giữ hàng bằng lõi chung và phát sự kiện sau commit. VERIFIED v1.4.
- [x] **T06 — Hết hạn giữ chỗ:** Hangfire PostgreSQL, quét reservation quá hạn, hủy/release nguyên tử, worker/concurrency/fault/cancellation tests. VERIFIED v1.2.
- [x] **B-01 — Đổi chi nhánh kho sạch:** Có fix `b5ba71e`; nhóm warehouse/B-01 đã chạy lại PASS theo bằng chứng trong báo cáo tổng kết.
- [x] **Bàn giao BE1:** Tài liệu theo task và PR #21; code đã vào develop tại merge commit `4dbfd8e`, có fix B-01.
- [ ] **Simulator đầy đủ:** Chưa có bằng chứng nghiệm thu UI/sender HTTP concurrent hoàn chỉnh; batch tạo đơn tuần tự trên nhánh tích hợp không chứng minh mục này.
- [ ] **UI E2E Admin/POS/realtime:** Cần bằng chứng tích hợp frontend riêng.
- [ ] **Thiết kế AI gợi ý nhập hàng:** Chưa có bằng chứng hoàn tất trong gói BE1 hiện được tổng kết.
- [ ] **Triển khai production và bật/provision Hangfire trên môi trường đích:** Chưa thuộc kết luận nghiệm thu local.

Quy ước: mục FR bao gồm nhiều người hoặc nhiều sprint chỉ được đánh dấu hoàn tất khi toàn bộ phạm vi có bằng chứng. Phần BE1 đã đạt được đánh dấu riêng ở các mục con; `[ ]` của nhóm không phủ nhận phần backend đã hoàn thành.

---

## 0A. Checklist hiện trạng sản phẩm — cập nhật 29/10/2026

**Cập nhật tích hợp:** develop đã nhận POS mới từ PR #24; phạm vi lỗi POS riêng và QR/PWA thay đổi so với audit gốc. Xem [tổng hợp Sprint 3 và tồn đọng sau tích hợp](docs/bug-fixaffter/SPRINT-3-RELEASE.md). BF-003/BF-015/BF-019 phần POS riêng và BF-020 cần kiểm chứng lại, không tự đóng; QR Coming soon còn áp dụng cho POS Admin.

**Chưa nghiệm thu toàn bộ sản phẩm.** Các dấu `[x]` ở phần Sprint 2 và báo cáo BE1 bên dưới phản ánh baseline/bằng chứng được ghi tại thời điểm đó. Khi đánh giá khả năng sử dụng hiện tại, phải đối chiếu các mục còn mở trong phần này. Trạng thái bàn giao READY_FOR_REVIEW chỉ có nghĩa chờ review.

Baseline rà soát: nhánh `feature/OISMS-43-orders-transfers`, HEAD `f276eeed72782f7272f32a62f4bb61ba761f3e89` cộng working tree chưa commit. Chi tiết tái hiện, ảnh hưởng, vị trí code và điều kiện đóng: [BUG-LIST.md](docs/bug-fixaffter/BUG-LIST.md).

### 0A.1. Nền tảng và các luồng cơ bản

- [x] Đã có báo cáo Markdown cho đợt rà soát, tách lỗi đang hoạt động, chức năng chưa hoàn thiện và kết quả kiểm tra.
- [ ] **Đăng nhập/đổi tài khoản:** hoàn thiện vòng đời HTTP, refresh và SignalR theo chủ sở hữu phiên; `BF-001`, `BF-002` còn mở.
- [ ] **Phiếu nhập hàng:** đã bổ sung tạo Draft, đọc danh sách server và thao tác xác nhận riêng ở lượt trước; cần nghiệm thu lại toàn luồng tạo → xác nhận → tồn/sổ kho → tải lại. Không suy diễn từ kiểm thử từng phần thành hoàn tất sản phẩm.
- [ ] **Chuyển kho:** đã có UI gọi backend; còn retry tạo phiếu, khóa thao tác và tồn theo kho nguồn (`BF-004`, `BF-008`, `BF-012`); cần nghiệm thu tạo → dispatch → receive → tồn hai kho → lịch sử.
- [ ] **Kiểm kê:** đã có UI và cập nhật phân loại lịch sử ở lượt trước; còn retry, ô đếm trống và khóa thao tác (`BF-005..008`); cần nghiệm thu lưu số đếm → xác nhận → tồn/điều chỉnh/lịch sử → tải lại.
- [ ] **Giữ chỗ và đơn hàng:** bảo toàn ý định khi kết quả chưa xác định (`BF-013`), nghiệm thu tạo/giữ → xác nhận hoặc hủy/hết hạn → tồn/sổ kho. In chứng từ Admin còn Coming soon (`CS-06`).
- [ ] **POS tích hợp `/pos`:** đã có giao diện catalog/giỏ và thanh toán tiền mặt; còn retry, hết phiên và kiểm tra response thành công (`BF-003`, `BF-015`, `BF-019`); QR chưa hoạt động thật (`CS-05`).
- [ ] **POS riêng:** ngoài các lỗi checkout chung, phải khóa/tách đúng giỏ và chi nhánh khi giao dịch đang chạy (`BF-020`); chưa coi hai POS có mức hoàn thiện tương đương.
- [ ] **Đồng bộ realtime:** hoàn thiện nhóm chi nhánh, sự kiện tồn và refetch sau reconnect (`BF-002`, `BF-016`); kiểm chứng nhiều phiên và chi nhánh trên môi trường test.
- [ ] **Báo cáo/cảnh báo:** sửa phản hồi cũ, dữ liệu cũ khi lỗi, ngày/múi giờ, link cảnh báo và kênh Admin (`BF-009..011`, `BF-017`, `BF-018`); các finding BE2-F03/F04 bên dưới vẫn cần xử lý/đối chiếu riêng.
- [ ] **Chi nhánh/kho:** không trình bày lỗi API như danh sách trống (`BF-014`); nghiệm thu tải, lưu và đọc lại.

### 0A.2. Checklist sửa lỗi còn mở

Tất cả mục dưới đây **OPEN**, chưa tự đánh dấu CLOSED/VERIFIED. P1 ưu tiên sai phiên, giao dịch hoặc số liệu; P2 ưu tiên chức năng và trải nghiệm. Mỗi ID có bằng chứng và tiêu chí đóng riêng trong [danh sách chi tiết](docs/bug-fixaffter/BUG-LIST.md).

- [ ] **P1 · BF-001:** Chặn refresh/HTTP cũ ghi đè phiên mới hoặc replay ý định cũ bằng token tài khoản mới.
- [ ] **P1 · BF-002:** Tạo/đóng và reset kết nối SignalR đúng phiên đăng nhập.
- [ ] **P1 · BF-003:** Giữ nguyên request key và snapshot khi retry thanh toán POS có kết quả chưa rõ.
- [ ] **P1 · BF-004:** Bảo toàn request key khi retry tạo phiếu chuyển kho.
- [ ] **P1 · BF-005:** Bảo toàn request key của form tạo kiểm kê.
- [ ] **P1 · BF-006:** Không chốt số đếm cũ sau khi xóa ô và đồng ý gán số sổ sách.
- [ ] **P2 · BF-007:** Không tự lưu ô đếm trống thành tồn sổ sách khi chưa xác nhận.
- [ ] **P2 · BF-008:** Không để request chứng từ cũ khóa nút trên chứng từ mới.
- [ ] **P1 · BF-009:** Không cho phản hồi báo cáo cũ ghi đè bộ lọc mới.
- [ ] **P2 · BF-010:** Xử lý dữ liệu báo cáo cũ rõ ràng khi truy vấn bộ lọc mới thất bại.
- [ ] **P1 · BF-011:** Thống nhất ngày Việt Nam, timezone nghiệp vụ và biên ngày báo cáo.
- [ ] **P2 · BF-012:** Hiển thị tồn của đúng kho nguồn khi lập chuyển kho.
- [ ] **P1 · BF-013:** Không làm mất ý định/key giữ chỗ pending khi sửa form.
- [ ] **P2 · BF-014:** Hiển thị lỗi đọc/lưu chi nhánh, kho đúng trạng thái.
- [ ] **P2 · BF-015:** Xử lý hết phiên và ProblemDetails khi POS checkout thất bại.
- [ ] **P2 · BF-016:** Nối đầy đủ sự kiện realtime tồn kho và refetch khi reconnect.
- [ ] **P2 · BF-017:** Mở đúng tab cảnh báo từ link `/reports?tab=alerts`.
- [ ] **P2 · BF-018:** Bổ sung bộ lọc kênh Admin khớp dữ liệu backend.
- [ ] **P2 · BF-019:** Không nhận checkout thành công khi response thiếu định danh đơn server.
- [ ] **P1 · BF-020:** Không để kết quả checkout POS riêng xóa giỏ hoặc cập nhật ngữ cảnh mới.

### 0A.3. Chức năng chưa hoàn thiện / Coming soon

Gắn nhãn hoặc khóa nút giúp phản ánh đúng hiện trạng; **không được đánh dấu hoàn thành yêu cầu** chỉ vì đã gắn Coming soon. Phạm vi và điều kiện sử dụng thật: [COMING-SOON.md](docs/bug-fixaffter/COMING-SOON.md).

- [ ] **CS-01:** Luồng nhận hàng trả từ khách, liên kết đơn gốc và ghi nhận tồn/lịch sử.
- [ ] **CS-02:** Danh sách, tạo/sửa nhân viên, phân quyền và phân công chi nhánh.
- [ ] **CS-03:** Lưu ngưỡng cảnh báo trên server và đồng bộ Kho/Báo cáo.
- [ ] **CS-04:** Tìm kiếm toàn hệ thống ở thanh đầu trang.
- [ ] **CS-05:** Thanh toán QR thật và xác nhận/đối soát theo phương án nghiệp vụ được chốt.
- [ ] **CS-06:** In chứng từ giao hàng và báo cáo Admin; in hóa đơn POS cần nghiệm thu riêng.
- [ ] **CS-07:** KPI bán hàng, đơn gần đây và biểu đồ Tổng quan từ dữ liệu server đầy đủ.
- [ ] **CS-08:** Dự báo và gợi ý nhập hàng AI có API/model/UI và bằng chứng đánh giá.

### 0A.4. Bằng chứng và điều kiện nghiệm thu tiếp theo

- [x] Backend unit tests trong đợt rà soát: **158 PASS, 0 FAIL**.
- [x] Frontend regression React/DOM trong đợt rà soát: **13 PASS, 0 FAIL**.
- [x] Đã ghi bằng chứng cho các kịch bản tái hiện có kiểm soát và link cảnh báo sai tab trên browser. Script tái hiện lỗi thoát 0 không có nghĩa sản phẩm PASS.
- [x] Backend integration suite đầy đủ đã xuất TRX sau lần kiểm tra trước: **285 PASS, 0 FAIL, 0 SKIP**; tổng backend **443 PASS**. Kết quả này không thay thế nghiệm thu frontend E2E hoặc đóng các finding còn mở.
- [ ] Nghiệm thu E2E các luồng cơ bản trên database test riêng, gồm tải lại trang, lỗi mạng/kết quả unknown, đổi tài khoản và đổi chi nhánh.
- [ ] Review và đóng từng finding sau bản sửa cùng bằng chứng hồi quy; giữ các finding bàn giao cũ ở trạng thái chờ review nếu chưa đủ bằng chứng.

Trước khi đẩy nhánh ngày 29/10, đã chạy lại build Admin và POS riêng: cả hai PASS. 14 kiểm thử ở lượt triển khai trước chỉ được dẫn lại, không nhận là vừa chạy lại trong audit. Xem [TEST-RESULTS.md](docs/bug-fixaffter/TEST-RESULTS.md) để biết lệnh, giới hạn và nguồn bằng chứng.

---

## 1. Checklist Yêu Cầu Chức Năng (Functional Requirements — `FR-*`)

### 1.1. Quản lý Đa khách thuê, Xác thực & Phân quyền (`FR-AUTH`) — *Phụ trách: Nhựt & Đăng Khoa*
- [x] **`FR-AUTH-01` (Tenant Registration — Hoàn thành Sprint 2):** Đăng ký cửa hàng mới (`POST /api/auth/register-tenant`), tạo không gian dữ liệu cô lập với `TenantId` riêng, khởi tạo nguyên tử Chi nhánh/Kho mặc định và Owner; chuẩn hóa số điện thoại `E.164` (`PhoneHelper.cs`).
- [x] **`FR-AUTH-02` (User Authentication — Hoàn thành Sprint 2):** Đăng nhập/Đăng xuất qua Email hoặc SĐT + Mật khẩu (`POST /api/auth/login`, `/logout`, `/refresh-token`, `/me`); trả về JWT chứa `TenantId`, `UserId`, `Role`; xoay vòng Refresh Token nguyên tử với khóa dòng `FOR UPDATE` và chặn Tenant/User bị khóa (`IsActive = false`).
- [x] **`FR-AUTH-03` (Role-Based Access Control - RBAC — Hoàn thành Sprint 2):** Phân quyền chặt chẽ 3 vai trò `Owner` (Toàn quyền), `Staff` (Kho/Đơn hàng), `Cashier` (Chỉ POS) trên cả Backend API (`[Authorize(Roles)]`) và cổng đăng nhập tập trung Frontend (`Smart RBAC` + chặn trang `/403`).
- [x] **`FR-AUTH-04` (Branch & Warehouse Management — Hoàn thành Sprint 2):** Cho phép `Owner` tạo, cập nhật, bật/tắt trạng thái hiển thị (`IsActive`) của Chi nhánh (`/api/branches`) và Kho hàng (`/api/warehouses`); chặn đổi chi nhánh đối với kho đã có lịch sử giao dịch sổ cái (`409 Conflict`).

### 1.2. Quản lý Danh mục, Sản phẩm, Biến thể SKU, Mã vạch & Giá (`FR-PROD`) — *Phụ trách: Nhựt, Phúc Hữu & Nam Nguyễn*
- [x] **`FR-PROD-01` (Category & Brand Management — Hoàn thành Sprint 2):** Quản lý cây danh mục phân cấp cha - con (`/api/categories`) và thương hiệu (`/api/brands`); bảo vệ chống tạo vòng lặp chu trình bằng `PostgreSQL Advisory Lock` và thuật toán duyệt tổ tiên.
- [x] **`FR-PROD-02` (Product & Variant/SKU Management — Hoàn thành Sprint 2):** Hỗ trợ sản phẩm đơn giản và đa biến thể (Màu sắc, Kích thước, JSON Attributes giữ nguyên kiểu dữ liệu và `null` lồng nhau); đảm bảo mã `SKU` duy nhất trong cùng một Tenant (`uq_skus_tenant_sku`).
- [x] **`FR-PROD-03` (Barcode Management — Hoàn thành Sprint 2):** Tự động sinh mã vạch chuẩn `EAN-13` (có cơ chế retry 10 lần chống trùng) hoặc nhập thủ công `EAN-13`/`Code128` cho từng SKU; bảo vệ ràng buộc duy nhất theo Tenant (`uq_skus_3`).
- [x] **`FR-PROD-04` (Listed Price Management — Hoàn thành Sprint 2):** Thiết lập và cập nhật Giá bán lẻ (`RetailPrice`) và Giá bán sỉ (`WholesalePrice`) theo từng SKU (`PUT /api/products/{pId}/variants/{vId}/prices`), tách biệt hoàn toàn với giá vốn (`UnitCost`).

### 1.3. Sổ cái tồn kho & Nghiệp vụ kho (`FR-INV`) — *Phụ trách: Đăng Khoa, Phúc Hữu & Nam Nguyễn*
- [x] **`FR-INV-01` (Append-Only Inventory Ledger — Hoàn thành Sprint 2):** Ghi nhận nghiêm ngặt biến động kho vào bảng sổ cái chỉ ghi thêm `InventoryTransaction` (`TenantId`, `BranchId`, `WarehouseId`, `SKUId`, `Type (IN/OUT)`, `Quantity`, `BalanceAfter`, `ReferenceId`, `CreatedAt`); cấm tuyệt đối sửa trực tiếp cột `on_hand` hoặc `UPDATE`/`DELETE` sổ cái; hoàn thiện giao diện tra cứu Sổ cái trên Web Admin.
- [x] **`FR-INV-02` (Purchase Receipt Management — Hoàn thành Sprint 2):** Lập phiếu nhập hàng đa dòng từ Nhà cung cấp (`Draft`) và xác nhận nhập kho nguyên tử (`POST /api/inventory/goods-receipts/{id}/confirm`) $\rightarrow$ Tăng `on_hand` $\rightarrow$ Ghi sổ cái `IN` $\rightarrow$ Cập nhật lại giá vốn bình quân (`WAC`). Hoàn thiện toàn diện cả Backend và giao diện Web Admin.
- [ ] **`FR-INV-03` (Stock Transfer Management — Sprint 3):** Nhóm chức năng chuyển kho hai bước, gồm backend và giao diện.
  - [x] **Đăng Khoa (BE1):** API lập phiếu, dispatch, receive và hủy Draft; kiểm chứng giá vốn, ledger, idempotency và rollback — Task 4A VERIFIED v1.2.
  - [ ] **Phúc Hữu & Nam Nguyễn:** Nối giao diện chuyển kho và nghiệm thu UI E2E; chưa có bằng chứng hoàn tất trong lượt cập nhật này.
- [ ] **`FR-INV-04` (Stocktake Management — Sprint 3):** Nhóm chức năng kiểm kê, gồm backend và giao diện.
  - [x] **Đăng Khoa (BE1):** Snapshot tồn/sequence, ghi số đếm, chốt kiểm kê tạo chứng từ điều chỉnh và kiểm tra drift — Task 4B VERIFIED v1.3.
  - [ ] **Phúc Hữu & Nam Nguyễn:** Nối giao diện kiểm kê và nghiệm thu UI E2E; chưa có bằng chứng hoàn tất trong lượt cập nhật này.

### 1.4. Tính Giá vốn bình quân gia quyền (`FR-COST`) — *Phụ trách: Đăng Khoa*
- [x] **`FR-COST-01` (Automated Batch COGS Calculation — Hoàn thành Sprint 2):** Tự động tính lại giá vốn đơn vị của SKU ngay khi xác nhận Phiếu nhập kho theo công thức bình quân gia quyền: $\text{NewCost} = \frac{(\text{OldStock} \times \text{OldCost}) + (\text{ImportQty} \times \text{ImportPrice})}{\text{OldStock} + \text{ImportQty}}$.
- [x] **`FR-COST-02` (COGS Snapshot on Dispatch — Backend hoàn thiện Sprint 3):** Lưu giá vốn tại thời điểm POS checkout hoặc xác nhận xuất đơn online vào `OrderItem.CostPrice`, dùng cho lịch sử đơn và ledger; không chốt khi đơn chỉ Reserved. Bằng chứng trong Task 2 v1.2 và Task 3 v1.1.

### 1.5. Cơ chế Giữ chỗ & Chống bán vượt (`FR-RSE`) — *Phụ trách: Đăng Khoa (Trưởng nhóm)*
- [x] **`FR-RSE-01` (Available Stock Calculation — Hoàn thành Sprint 2):** Duy trì động tồn khả dụng theo SKU tại từng chi nhánh/kho (`available = on_hand - reserved`) trên toàn bộ API tồn kho và API tra cứu POS.
- [x] **`FR-RSE-02` (Stock Reservation Execution — Hoàn thành nền tảng ở Sprint 2):** Hiện thực `ReservationService.cs` và endpoint `POST /api/inventory/reserve`: tăng `reserved` khi giữ chỗ, từ chối bằng `409 Conflict` nếu `available < Quantity`, kiểm tra `IsActive` 4 cấp (Branch/Warehouse/Product/SKU) và chống giữ chỗ lặp bằng `Idempotency-Key` / `RequestKey` + `PostgreSQL Advisory Lock`.
- [x] **`FR-RSE-03` (Deduct/Release Execution — Backend hoàn thiện Sprint 3):** Confirm trừ OnHand/Reserved và ghi OUT; cancel/expiry giải phóng giữ chỗ đúng một lần, không ghi IN bù khi hàng chưa xuất. Có bằng chứng rollback/concurrency tại Task 3 v1.1 và worker thật tại Task 6 v1.2; worker chỉ tự chạy khi được bật/cấu hình trên môi trường đích.

### 1.6. Trung tâm Đơn hàng đa kênh (`FR-ORD`) — *Phụ trách: Đăng Khoa, Nhựt & Phúc Hữu*
- [x] **`FR-ORD-01` (Canonical Order Model Foundation — Hoàn thành nền tảng CSDL & Entity ở Sprint 2):** Đã thiết kế và khởi tạo bảng `orders`, `order_items` chuẩn hóa đa kênh và hỗ trợ khởi tạo đơn nháp trong luồng giữ chỗ.
- [ ] **`FR-ORD-01..03` (Full Order Hub, State Machine & Order Processing — Hoàn thiện đủ ở Sprint 3: `18/10 – 22/10`):**
  - [x] **Đăng Khoa:** Máy trạng thái đơn hàng và lõi webhook canonical — Task 3 v1.1 / Task 5 v1.4 đã nghiệm thu; gồm Reserved, Confirmed, Completed, Cancelled và xử lý Draft legacy theo contract.
  - [ ] **Nhựt:** API `list / detail / filter` đơn hàng và chứng từ kho; T02 BE2 vẫn PLANNED theo hồ sơ hiện có.
  - [ ] **Phúc Hữu & Nam Nguyễn:** Form tạo/duyệt/hủy đơn và in phiếu; chưa xác nhận nghiệm thu UI E2E trong lượt này.

### 1.7. Bán hàng tại quầy PWA POS (`FR-POS`) — *Phụ trách: Phương Lê & Đăng Khoa*
- [x] **`FR-POS-01` (Fast Sales Interface — Hoàn thành Sprint 2):** Hoàn thiện ứng dụng `src/frontend/pos-app` (React 19 PWA) tối ưu cảm ứng và phím tắt, kết nối API thật tìm kiếm sản phẩm theo Tên, mã SKU (`/api/pos/search`) và quét mã vạch (`/api/pos/barcode/{code}`).
- [x] **`FR-POS-02` (Counter Sales Constraint — Hoàn thành Sprint 2):** Kiểm tra tức thì số lượng `available` khi chọn hàng vào giỏ, chặn tuyệt đối việc thêm/tăng số lượng vượt quá tồn khả dụng để bảo vệ phần hàng đã giữ chỗ cho đơn online.
- [x] **`FR-POS-03` (Receipt Printing & Payment UI — Hoàn thành giao diện & in hóa đơn ở Sprint 2):** Hỗ trợ chọn phương thức thanh toán Tiền mặt (tự tính tiền thừa) / QR Code và in hóa đơn nhiệt khổ `80mm`/`58mm` qua CSS `@media print` và `window.print()`.
- [ ] **`FR-POS-04` (Fast Checkout Atomic Workflow — Hoàn thiện đủ ở Sprint 3: `18/10 – 22/10` & Tối ưu ở Sprint 4):** Kết nối nút Thanh toán POS với API giao dịch toàn giỏ nguyên tử (`Reserve -> Confirm -> Deduct`), xử lý idempotency chống bấm đúp và hoàn thiện trải nghiệm PWA/QR ở Sprint 4.
  - [x] **Đăng Khoa (BE1):** `/api/pos/checkout` và alias `/api/orders/checkout` dùng cùng transaction toàn giỏ; tạo đơn/payment, trừ tồn, lưu giá vốn, ghi OUT và bảo vệ retry — Task 2 VERIFIED v1.2.
  - [ ] **Phương Lê / tích hợp:** Nghiệm thu frontend checkout với backend, lỗi mạng/kết quả chưa xác định, in hóa đơn và hoàn thiện PWA/QR; chưa đánh dấu hoàn tất toàn bộ nhóm.

### 1.8. Giả lập Webhook, Thời gian thực SignalR & Tác vụ nền Hangfire (`FR-SIM`) — *Đăng Khoa phụ trách lõi/Simulator; Nhựt phụ trách automated core tests theo phân công v11*
- [ ] **`FR-SIM-01` (E-commerce Webhook Simulator — Sprint 3):** **Đăng Khoa (BE1)** phụ trách Simulator ba kênh; phối hợp **Nhựt (BE2)** về automated core tests.
  - [x] Endpoint nhận payload canonical `/api/webhooks/simulator/{channel}`, xác thực shop, chống trùng và giữ tồn thật đã nghiệm thu trong Task 5.
  - [x] Trên nhánh tích hợp có API `/api/webhooks/simulator/batch` sinh đơn tuần tự và gọi service nội bộ; không ghi nhận là sender HTTP concurrent hoặc công việc triển khai riêng của BE1.
  - [ ] Công cụ sender/UI giả lập đầy đủ và runner HTTP concurrent; chưa có bằng chứng nghiệm thu toàn bộ trong hồ sơ cập nhật này.
- [ ] **`FR-SIM-02` (Real-time Push Notification SignalR — Sprint 3):** Gồm backend phát sự kiện và frontend nhận thông báo.
  - [x] **Đăng Khoa:** SignalR Hub xác thực JWT, cô lập nhóm tenant/chi nhánh và phát sự kiện sau commit — Task 5 VERIFIED v1.4.
  - [ ] **Phúc Hữu / Phương:** Nối giao diện nhận popup, chuông và refetch khi reconnect; chưa xác nhận nghiệm thu UI E2E.
- [ ] **`FR-SIM-03` (Background Processing Hangfire — Kế hoạch Sprint 3 & Sprint 4):**
  - [x] **Sprint 3 — Đăng Khoa:** Hangfire PostgreSQL tự hủy đơn đủ điều kiện có reservation quá hạn, giải phóng toàn giỏ nguyên tử; worker thật đã được kiểm chứng — Task 6 VERIFIED v1.2.
  - [ ] **Sprint 4 — Đăng Khoa:** Hangfire tổng hợp báo cáo hằng ngày (`daily report aggregations`); không thuộc nghiệm thu auto-expiry Task 6.

### 1.9. Báo cáo & Cảnh báo tồn kho (`FR-REP`) — *Phụ trách: Nhựt, Phúc Hữu, Nam Nguyễn & Đăng Khoa*
- [ ] **`FR-REP-01..03` (Báo cáo cơ bản & Cảnh báo — Kế hoạch Sprint 3: `18/10 – 22/10`):** **Nhựt** viết câu truy vấn báo cáo `net revenue`, `COGS`, `gross profit`, `inventory value`, `velocity`, `low-stock`, `threshold`; **Phúc Hữu & Nam Nguyễn** nối các báo cáo cơ bản vào Dashboard đúng session, filter và role.
  - [ ] **Fix sau — `BE2-F03` / T03:** API `GET /api/reports/revenue-profit` hiện cho `Owner,Staff`; giới hạn báo cáo tài chính về Owner và kiểm chứng HTTP Owner 200 / Staff, Cashier 403 / anonymous 401. [Task quyền truy cập](docs/09-personal-reports/nhut/sprint-3/codex-antigravity/task-03-access-isolation/brief.md).
  - [ ] **Fix sau — `BE2-F04` / T04:** Revenue hiện cộng cả đơn Confirmed; chỉ tính Completed theo yêu cầu đang lưu. Case đối chứng: Completed tổng 30 + Confirmed 100 phải cho revenue 30, không phải 130.
  - [ ] **Fix sau — `BE2-F04` / T04:** Bổ sung filter SKU; chỉ cộng doanh thu/COGS các dòng hàng khớp SKU, không cộng toàn bộ đơn nhiều SKU. Chốt thời điểm ghi nhận và biên ngày/múi giờ trước khi sửa. [Ghi nhận chi tiết và điều kiện đóng](docs/09-personal-reports/nhut/sprint-3/codex-antigravity/task-04-report-correctness/brief.md#ghi-nhận-để-fix-sau--27102026).
- [ ] **`FR-REP-01..03` (Báo cáo nâng cao & Biểu đồ — Kế hoạch Sprint 4: `23/10 – 27/10`):** Hoàn thiện toàn bộ bộ lọc filter, biểu đồ trực quan doanh thu/giá vốn/lãi gộp, tốc độ luân chuyển và cảnh báo tồn thấp.

### 1.10. Dự báo nhu cầu AI & Gợi ý nhập hàng (`AI` - Gói 5) — *Phụ trách: Đăng Khoa, Nhựt, Phúc Hữu & Nam Nguyễn*
- [ ] **Chốt thiết kế AI (Sprint 3: `18/10 – 22/10`) & Hiện thực chạy thật (Sprint 4: `23/10 – 27/10`):**
  - **Đăng Khoa:** Thiết kế và hiện thực lõi `AI forecasting & evaluation`.
  - **Nhựt:** Chuẩn bị dữ liệu lịch sử và xây dựng API `recommendation / report` theo model của Khoa.
  - **Phúc Hữu & Nam Nguyễn:** Xây dựng giao diện AI gợi ý nhập hàng trên Web Admin.

---

## 2. Checklist Yêu Cầu Phi Chức Năng (`NFR-*`) — *Sprint 2 – 3 (Xây dựng), Sprint 4 – 5 (Đo kiểm & Hồi quy)*

- [x] **`NFR-PERF-01` (API Response Time cho POS Search/Barcode & Write Transactions — Hoàn thành xây dựng ở Sprint 2, Benchmark chính thức ở Sprint 4):** Tối ưu truy vấn tra cứu POS/Barcode `< 200ms` và giao dịch ghi Nhập kho/Giữ chỗ `< 500ms`.
- [x] **`NFR-PERF-02` (Concurrency & Anti-Oversell Handling — Hoàn thành lõi ở Sprint 2, Demo UI ở Sprint 4):** Áp dụng khóa dòng bi quan `SELECT ... FOR UPDATE` kết hợp `pg_advisory_xact_lock` trong `ReservationService`, vượt qua bài kiểm thử đồng thời tranh mua 1 SKU khả dụng cuối cùng (`available` không bao giờ âm).
- [x] **`NFR-TENANT-01` (Multi-tenant Data Isolation — Hoàn thành Sprint 2):** Toàn bộ 29 bảng nghiệp vụ đều có `tenant_id`, thực thi **EF Core Global Query Filters** và khóa ngoại kép theo Tenant, đạt **0% rò rỉ dữ liệu chéo**.
- [x] **`NFR-TENANT-02` (Scalability Indexing — Hoàn thành Sprint 2):** Đã thiết lập đầy đủ Composite Indexes trên `(tenant_id, created_at)` và `(tenant_id, sku_id)` cho bảng Sổ cái và Đơn hàng.
- [x] **`NFR-SEC-01` (Encryption & Authentication — Hoàn thành Sprint 2):** Băm mật khẩu bằng `BCrypt`, JWT Access Token 60 phút và Refresh Token an toàn.
- [x] **`NFR-SEC-02` (ACID Compliance — Hoàn thành Sprint 2):** Toàn bộ luồng Đăng ký Tenant, Xác nhận Nhập kho + Tính WAC, và Giữ chỗ tồn kho đều thực thi trong Transaction nguyên tử, rollback 100% khi có lỗi.
- [x] **`NFR-SEC-03` (Append-Only Ledger Auditability — Hoàn thành Sprint 2):** Bảng `inventory_transactions` chỉ cho phép `INSERT`, cấm tuyệt đối `UPDATE`/`DELETE`.
- [x] **`NFR-USA-02` (POS Usability — Hoàn thành nền tảng ở Sprint 2, tối ưu thiết bị thật ở Sprint 4 & 5):** Giao diện PWA POS tối giản, hỗ trợ quét Barcode + Enter, đáp ứng mượt trên Desktop, Tablet và Mobile POS.
- [x] **`NFR-MAINT-01` (Clean Architecture — Hoàn thành Sprint 2):** Phân tách nghiêm ngặt 4 tầng `OISM.Domain`, `OISM.Application`, `OISM.Infrastructure`, `OISM.WebAPI`.
- [x] **`NFR-MAINT-02` (Automated Test Coverage >= 80% — Đạt sớm ngay từ Sprint 2):** Đạt **194/194 bài test Pass 100%**, độ phủ mã nguồn module lõi `OISM.Application.Features.Inventory` đạt **`92.49% – 94.83%`** (vượt xa ngưỡng $\ge 80\%$ của đề bài).

---

## 3. Checklist Hạ Tầng, DevOps & Chuẩn Hóa Tài Liệu (`docs/`)

- [x] **Đóng gói Docker Compose trọn bộ (`docker-compose.yml` — Hoàn thành Sprint 2):** Chạy đồng bộ 4 container `postgres`, `backend`, `admin-dashboard`, `pos-app` kèm cấu hình Nginx SPA Fallback (`try_files`).
- [x] **Tích hợp liên tục GitHub Actions CI (`.github/workflows/ci.yml` — Hoàn thành Sprint 2):** Tự động build và chạy toàn bộ Unit/Integration Tests với PostgreSQL service container trên Linux runner.
- [x] **Chuẩn hóa kiến trúc thư mục tài liệu (`docs/` — Hoàn thành & Đồng bộ Confluence):**
  - [x] [`docs/00-overview/`](docs/00-overview/README.md) — Tầm nhìn, khái niệm cốt lõi & đối chiếu đề bài gốc.
  - [x] [`docs/01-users-and-rbac/`](docs/01-users-and-rbac/README.md) — Đặc tả Actors, Ma trận RBAC & Vòng đời Tenant.
  - [x] [`docs/02-functional-requirements/`](docs/02-functional-requirements/README.md) — 12 file đặc tả chi tiết từng module chức năng (`FR-01` đến `FR-12`).
  - [x] [`docs/03-non-functional-requirements/`](docs/03-non-functional-requirements/README.md) — 5 file đặc tả yêu cầu phi chức năng (`NFR-*`).
  - [x] [`docs/04-architecture-and-design/`](docs/04-architecture-and-design/README.md) — Clean Architecture, Multi-tenancy, ERD, State Machines & 4 Sequence Diagrams.
  - [x] [`docs/05-api-contracts/`](docs/05-api-contracts/README.md) — 5 file đặc tả hợp đồng REST API Endpoints.
  - [x] [`docs/06-testing-and-qa/`](docs/06-testing-and-qa/README.md) — Ma trận RTM (đồng bộ Confluence), Kế hoạch Test & DoD.
  - [x] [`docs/07-devops-and-deployment/`](docs/07-devops-and-deployment/README.md) — Hướng dẫn Docker Compose, CI/CD & Biến môi trường.
  - [x] [`docs/08-sprints/`](docs/08-sprints/README.md) — Kế hoạch & phân công chi tiết `sprint-2`, `sprint-3`, `sprint-4`, `sprint-5` khớp 100% với Confluence Page `5275650`.
  - [x] [`docs/09-personal-reports/`](docs/09-personal-reports/README.md) — Báo cáo cá nhân chuẩn hóa tên tiếng Anh ngắn gọn của 4 thành viên (`dang-khoa`, `nhut`, `phuc-huu`, `phuong`).
