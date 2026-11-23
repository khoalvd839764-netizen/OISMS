---
title: "Báo Cáo Tổng Kết Đồ Án OISMS"
subtitle: "Omnichannel Inventory and Sales Management System (OISMS)"
author: "Lê Võ Đăng Khoa (Trưởng nhóm / Backend 1 Lead)"
instructor: "ThS. Nguyễn Văn Chiến"
date: "2026-11-23"
lang: "vi"
geometry: "margin=2cm"
---

# BÁO CÁO TỔNG KẾT ĐỒ ÁN OISMS

**Tên đề tài:** OISMS — Hệ thống Quản lý Bán hàng và Tồn kho Đa kênh (*Omnichannel Inventory and Sales Management System*)

---

## 1. Thông Tin Chung

| Hạng mục | Chi tiết |
| :--- | :--- |
| **Giảng viên hướng dẫn** | **ThS. Nguyễn Văn Chiến** |
| **Trưởng nhóm** | **Lê Võ Đăng Khoa** |
| **Mốc chốt số liệu** | `21/11/2026` (nhánh `feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2`, PR #52) |
| **Công nghệ** | .NET 8 (Clean Architecture 4 tầng), EF Core 8, PostgreSQL 16, React 19 + Vite, SignalR, Hangfire, Docker Compose |
| **Quy mô** | 51 bảng PostgreSQL · 29 API controller · 3 ứng dụng frontend (Admin, POS PWA, giả lập sàn) |
| **Bản demo (làm thêm)** | Admin: `https://oisms.khoadang.site` · POS: `https://pos.oisms.khoadang.site` |

| STT | Thành viên | Vai trò chính |
| :---: | :--- | :--- |
| 1 | **Lê Võ Đăng Khoa** | Trưởng nhóm / Backend 1 Lead |
| 2 | **Phùng Minh Nhựt** | Backend 2 Lead |
| 3 | **Phúc Hữu** | Frontend Admin Lead |
| 4 | **Phương Lê** | Frontend POS Lead |
| 5 | **Nam Nguyễn** | Hỗ trợ Nghiệp vụ & Frontend |

---

## 2. Lời Cảm Ơn

Kính gửi thầy **ThS. Nguyễn Văn Chiến**,

Nhóm thực hiện đồ án **OISMS** xin chân thành cảm ơn thầy đã tận tình hướng dẫn, định hướng kiến trúc và góp ý sát sao về nghiệp vụ trong suốt quá trình thực hiện đề tài. Các yêu cầu nâng cấp của thầy (mua hàng – nhập kho – phân bổ chi phí theo VAS 02, truy vết chứng từ xuất kho, kiểm kê theo kỳ, bảng giá theo hiệu lực, đa đơn vị tính, thanh toán VietQR, rà soát Clean Architecture và kiểm chứng hiệu năng) đã giúp nhóm đưa hệ thống đến gần bài toán vận hành thực tế.

Tài liệu này tóm tắt mức độ đáp ứng đề bài và kết quả kiểm thử để thầy tiện theo dõi.

---

## 3. Tổng Quan Mức Độ Đáp Ứng Đề Bài

| Nhóm yêu cầu | Kết quả | Còn lại |
| :--- | :---: | :--- |
| Yêu cầu chức năng (`30 FR` + `FR-AI-01`) | **30 / 31** | `FR-PROD-03`: chưa quét mã vạch ở màn kiểm kê |
| Yêu cầu phi chức năng (`11 NFR`) | **9 / 11** | `NFR-USA-01` chưa đo uptime dài giờ; `NFR-USA-02` chưa thử máy POS cầm tay |
| Gói công việc | **4 / 5** | Gói 5: đo uptime, thiết bị cầm tay, hoàn thiện hồ sơ |
| Nhóm hồ sơ nghiệm thu | **2 / 6** | URS/RTM, Kiến trúc, UML 2.0, HDSD đang rà soát đồng bộ với code |


---

## 4. Đối Chiếu Yêu Cầu Chức Năng (FR)

Cột "Yêu cầu theo đề bài" trích từ đề gốc (bản đối chiếu `docs/00-overview/03-de-bai-cnpm-reference.md`).

| Mã | Yêu cầu theo đề bài | OISMS đáp ứng | Kết quả |
| :--- | :--- | :--- | :---: |
| `FR-AUTH-01`<br>Tenant Registration | Cho phép tạo mới các Cửa hàng (Store Tenants) với không gian dữ liệu cô lập gắn với `TenantId` duy nhất. | Đăng ký Tenant + Owner trong 1 giao dịch; cô lập dữ liệu bằng Global Query Filter | **Đạt** |
| `FR-AUTH-02`<br>User Authentication | Đăng nhập/Đăng xuất qua Email hoặc SĐT + Mật khẩu; trả về JWT chứa `TenantId`, `UserId` và `Role`. | Đăng nhập email/SĐT, JWT 60 phút + refresh token xoay vòng, đăng xuất | **Đạt** |
| `FR-AUTH-03`<br>Role-Based Access Control (RBAC) | Cấu hình và kiểm soát quyền API/Màn hình theo 3 vai trò: `Owner` (Toàn quyền), `Staff` (Quản lý Kho/Đơn hàng), `Cashier` (Chỉ POS). | Phân quyền trên API (`[Authorize]`) và giao diện; che giá vốn với Staff/Cashier | **Đạt** |
| `FR-AUTH-04`<br>Branch Management | Cho phép `Owner` tạo, cập nhật hoặc bật/tắt hiển thị các chi nhánh và kho hàng thuộc sở hữu của mình. | Owner tạo, sửa, bật/tắt chi nhánh và kho; phân công nhân viên theo chi nhánh | **Đạt** |
| `FR-PROD-01`<br>Category & Brand Management | Quản lý cây danh mục sản phẩm phân cấp (cha - con) và danh mục thương hiệu. | Cây danh mục cha–con có chặn vòng lặp; quản lý thương hiệu | **Đạt** |
| `FR-PROD-02`<br>Product & Variant/SKU Management | Hỗ trợ sản phẩm đơn giản và sản phẩm có biến thể (Màu sắc, Kích thước...). Mỗi biến thể có mã `SKU` duy nhất trong cùng Tenant. | Sản phẩm đơn giản và có biến thể; SKU duy nhất trong tenant | **Đạt** |
| `FR-PROD-03`<br>Barcode Management | Tự động sinh hoặc nhập thủ công mã vạch (`EAN-13` / `Code128`) cho từng SKU để quét POS và kiểm kê kho. | Sinh/nhập mã vạch và quét tại POS đã có; **chưa quét ở màn kiểm kê** (không có thiết bị quét để kiểm chứng) | *Một phần* |
| `FR-PROD-04`<br>Listed Price Management | Thiết lập giá bán lẻ (Retail) và giá bán sỉ (Wholesale) theo từng SKU (tách biệt với giá vốn COGS). | Giá bán lẻ/sỉ theo SKU, bảng giá theo hiệu lực; giá vốn lưu riêng | **Đạt** |
| `FR-INV-01`<br>Inventory Ledger Recording | Ghi nhận nghiêm ngặt mọi biến động kho (Nhập, Xuất, Bán, Trả hàng, Kiểm kê, Chuyển kho) vào sổ cái chỉ ghi thêm `InventoryTransaction` (`TenantId`, `BranchId`, `SKUId`, `Type (IN/OUT)`, `Quantity`, `BalanceAfter`, `ReferenceId`, `CreatedAt`). Nghiêm cấm sửa trực tiếp cột `on_hand`. | Sổ cái `inventory_transactions` chỉ ghi thêm; trigger PostgreSQL chặn `UPDATE`/`DELETE`/`TRUNCATE` | **Đạt** |
| `FR-INV-02`<br>Purchase Receipt Management | Xử lý phiếu nhập hàng từ nhà cung cấp → Tăng tồn vật lý (`on_hand`) → Cập nhật lại giá vốn bình quân. | Đơn mua → phiếu nhập → tăng `on_hand`, ghi sổ `IN`, tính lại WAC | **Đạt** |
| `FR-INV-03`<br>Stock Transfer Management | Chuyển kho giữa Chi nhánh A và Chi nhánh B qua quy trình 2 bước: Xuất kho vận chuyển (`Out-bound transit`) → Xác nhận nhập kho nhận (`In-bound confirmation`). | `Draft → InTransit` (ghi `OUT` kho gửi) `→ Received` (ghi `IN` kho nhận) | **Đạt** |
| `FR-INV-04`<br>Stocktake Management | Tạo phiên kiểm kê kho, ghi nhận số đếm thực tế, đối chiếu với số dư phần mềm và sinh bút toán điều chỉnh (`Adjustment Ledger entries`) để cân bằng kho. | Phiên kiểm kê, nhập số đếm, Owner duyệt chênh lệch, ghi bút toán điều chỉnh | **Đạt** |
| `FR-COST-01`<br>Automated Batch COGS Calculation | Tự động tính lại giá vốn đơn vị SKU ngay khi xác nhận Phiếu nhập kho theo công thức bình quân gia quyền (WAC). | Tính WAC theo đơn vị gốc khi xác nhận phiếu nhập (phân bổ phí theo VAS 02) | **Đạt** |
| `FR-COST-02`<br>COGS Snapshot on Dispatch | Chụp (snapshot) chính xác giá vốn bình quân đơn vị vào dòng đơn hàng (`OrderItem.CostPrice`) khi đơn chuyển sang `Confirmed` hoặc thanh toán tại POS. | Chụp `OrderItem.CostPrice` khi `Confirmed` và khi thanh toán POS | **Đạt** |
| `FR-ORD-01`<br>Omnichannel Order Ingestion | Chuẩn hóa đơn hàng đa kênh (POS, tạo thủ công trên Admin, Webhook giả lập Shopee/TikTok) vào một Mô hình Đơn hàng Hợp nhất (`Canonical Order Model`). | Đơn POS, Admin và webhook sàn lưu chung bảng `orders`, phân biệt theo kênh | **Đạt** |
| `FR-ORD-02`<br>Order State Machine | Thực thi nghiêm ngặt vòng đời chuyển trạng thái đơn hàng: `Draft -> Reserved -> Confirmed -> Completed` (hoặc `Cancelled`). | Kiểm tra chuyển trạng thái; chuyển sai trả `409 INVALID_STATE_TRANSITION` | **Đạt** |
| `FR-ORD-03`<br>Order Processing | Cho phép nhân viên xem đơn chờ xử lý, duyệt đơn, in phiếu giao hàng hoặc hủy đơn. | Màn quản lý đơn: duyệt, hủy, in phiếu giao A4/80 mm | **Đạt** |
| `FR-RSE-01`<br>Available Stock Calculation | Duy trì động số lượng tồn khả dụng theo từng SKU tại mỗi chi nhánh: `available = on_hand - reserved`. | Tính ở cấp chi nhánh và kho; trigger chặn tồn âm | **Đạt** |
| `FR-RSE-02`<br>Stock Reservation Execution | Tăng `reserved` khi đơn online đi vào Order Hub ở trạng thái `Reserved`. Từ chối đơn nếu `available < Purchased Quantity`. | Khóa tồn rồi giữ chỗ; thiếu hàng trả `409 INSUFFICIENT_STOCK` | **Đạt** |
| `FR-RSE-03`<br>Deduct/Release Execution | Khi `Confirmed`: Trừ `on_hand`, giảm `reserved`, ghi giao dịch Sổ cái. Khi `Cancelled`: Giảm `reserved` để hoàn trả tồn về quỹ `available`. | Confirm trừ tồn + ghi sổ; hủy hoặc hết hạn chỉ giảm `reserved` | **Đạt** |
| `FR-POS-01`<br>Fast Sales Interface | Giao diện tối ưu cho màn hình cảm ứng và phím tắt, hỗ trợ tìm kiếm sản phẩm tức thì theo Tên / SKU / Barcode. | Tìm theo tên/SKU/mã vạch, phím tắt `F2`/`F4` | **Đạt** |
| `FR-POS-02`<br>Counter Sales Constraint | Kiểm tra tức thì tồn khả dụng (`available`) khi chọn hàng; chặn bán vượt tồn khả dụng để bảo vệ phần hàng đã giữ chỗ cho đơn online. | Giỏ hàng kiểm tra `available`; backend khóa và kiểm tra lại khi thanh toán | **Đạt** |
| `FR-POS-03`<br>Checkout & Receipt Printing | Hỗ trợ thanh toán Tiền mặt, Chuyển khoản QR, và tự động kích hoạt hộp thoại in hóa đơn qua Browser Print API. | Tiền mặt / VietQR, tự mở hộp thoại in biên nhận 58/80 mm | **Đạt** |
| `FR-POS-04`<br>Fast Checkout Workflow | Thực thi chuỗi `Reserve -> Confirm -> Deduct` trong một giao dịch cơ sở dữ liệu nguyên tử duy nhất (Single Atomic Database Transaction) khi thanh toán POS. | Giữ chỗ, xác nhận, trừ tồn, ghi sổ trong một giao dịch | **Đạt** |
| `FR-REP-01`<br>Revenue & Gross Profit Report | Tổng hợp Doanh thu thuần, Tổng giá vốn (COGS) và Lợi nhuận gộp (`Gross Profit = Revenue - COGS`). Lọc theo Thời gian, Chi nhánh, Kênh bán và SKU. | Doanh thu thuần, COGS, lãi gộp; lọc thời gian, chi nhánh, kênh, tối đa 20 SKU | **Đạt** |
| `FR-REP-02`<br>Inventory & Velocity Report | Theo dõi giá trị tồn kho theo giá vốn, danh sách mặt hàng bán chạy (Best-sellers) và hàng luân chuyển chậm (Slow-moving items). | Giá trị tồn theo giá vốn, hàng bán chạy, hàng chậm luân chuyển | **Đạt** |
| `FR-REP-03`<br>Stock Alert | Gửi thông báo cảnh báo tự động trên Dashboard khi `available <= Threshold` (ngưỡng đặt hàng tối thiểu của từng SKU). | Ngưỡng tồn theo SKU + chi nhánh, cảnh báo `LowStockWarning` | **Đạt** |
| `FR-SIM-01`<br>E-commerce Webhook Simulator | Công cụ giả lập cho phép Dev/Tester bắn các payload giả lập (Shopee/TikTok/Lazada) để kiểm thử thông lượng và khóa giữ chỗ. | Ứng dụng giả lập bắn 1–200 đơn Shopee/TikTok Shop/Lazada | **Đạt** |
| `FR-SIM-02`<br>Real-time Push Notification | Sử dụng **SignalR** đẩy cảnh báo âm thanh/popup đơn hàng mới trực tiếp lên Admin/POS mà không cần tải lại trang. | SignalR đẩy popup/âm thanh đơn mới lên Admin và POS theo chi nhánh | **Đạt** |
| `FR-SIM-03`<br>Background Processing | Sử dụng **Hangfire** cho các tác vụ bất đồng bộ nặng: tự động hủy các đơn `Reserved` quá hạn và tổng hợp báo cáo hằng ngày. | Job hủy giữ chỗ quá hạn và job tổng hợp doanh số ngày | **Đạt** |
| `FR-AI-01`<br>AI Stock Reorder Forecasting | Tích hợp dự báo AI để phân tích xu hướng và đề xuất gợi ý nhập lại hàng (Stock reorder recommendations). | Gợi ý nhập hàng theo tốc độ bán, tồn khả dụng, lead time; backtest WAPE 7,70% | **Đạt** |

---

## 5. Đối Chiếu Yêu Cầu Phi Chức Năng (NFR)

| Mã | Yêu cầu theo đề bài | OISMS đáp ứng | Kết quả |
| :--- | :--- | :--- | :---: |
| `NFR-PERF-01`<br>API Response Time | Tìm kiếm SKU / Quét barcode POS: `p95 < 200ms`. Tạo đơn / Giao dịch ghi: `< 500ms`. Báo cáo doanh thu tổng hợp: `< 2s` cho tập dữ liệu dưới `100,000` bản ghi. | Đo local trên 100.000 dòng hàng: tìm SKU 24,92 ms, tạo đơn 230,94 ms, báo cáo ≤ 1,16 s (mục 6.4) | **Đạt (local)** |
| `NFR-PERF-02`<br>Concurrency & Anti-Oversell | Xử lý kịch bản Flash Sale (`50 đơn online đồng thời tranh 1 SKU khả dụng cuối cùng`). Bắt buộc áp dụng khóa mức CSDL (`SELECT ... FOR UPDATE` hoặc `Version Check`) để ngăn chặn tuyệt đối tồn khả dụng âm. | Advisory lock + `SELECT … FOR UPDATE`; 50 đơn → 1 thành công, 49 nhận `409`, tồn = 0 | **Đạt** |
| `NFR-TENANT-01`<br>Data Isolation | Mô hình Shared Database, Separate Schema/Tenant ID pattern. Mọi bảng nghiệp vụ phải có `TenantId`. Bắt buộc dùng EF Core Global Query Filters để đảm bảo 0% rò rỉ dữ liệu giữa các Tenant. | `TenantId` trên 50/51 bảng (trừ bảng `tenants`) + Global Query Filter; test cô lập trên PostgreSQL | **Đạt** |
| `NFR-TENANT-02`<br>Scalability | Tối ưu hóa Index trên `(TenantId, CreatedAt)` và `(TenantId, SKUId)` cho bảng `InventoryLedger` và `Orders` để duy trì hiệu năng truy vấn ở quy mô hàng triệu dòng sổ cái. | Có các chỉ mục theo tenant; `EXPLAIN ANALYZE` trên 100.000 dòng: 0,13–34,9 ms | **Đạt (local)** |
| `NFR-SEC-01`<br>Encryption & Authentication | Mật khẩu băm qua `BCrypt` / `Argon2`. Giao tiếp API bảo mật qua `HTTPS/TLS 1.3`. JWT Access Token ngắn hạn (`60 phút`) đi kèm Refresh Token an toàn. | BCrypt, JWT 60 phút + refresh token, Caddy chỉ cho TLS 1.3 (kiểm chứng local) | **Đạt (local)** |
| `NFR-SEC-02`<br>ACID Compliance | Chuỗi `Order Creation -> Reserve -> Deduct` BẮT BUỘC thực thi bên trong một giao dịch CSDL nguyên tử (Atomic Database Transaction) với cơ chế rollback toàn phần khi gặp lỗi. | Giao dịch nguyên tử, test rollback giữa chừng trên PostgreSQL | **Đạt** |
| `NFR-SEC-03`<br>Append-Only Ledger Auditability | Bảng `InventoryLedger` chỉ cho phép ghi thêm (tuyệt đối cấm `UPDATE` hoặc `DELETE`). Mọi sai sót phải xử lý bằng giao dịch sổ cái bù trừ (Compensating ledger transactions). | Trigger `ledger_append_only`; sai sót xử lý bằng bút toán bù | **Đạt** |
| `NFR-USA-01`<br>Availability | Mục tiêu độ sẵn sàng (Uptime) đạt `99.5%` trong khung giờ hoạt động cao điểm (`07:00 AM – 10:00 PM`). | Đã có script đo, chưa thu thập dài giờ khung 07:00–22:00 | *Chưa đo* |
| `NFR-USA-02`<br>POS Usability | Giao diện POS tối giản cho phép hoàn tất thanh toán trong **dưới 3 cú nhấp chuột** hoặc qua **Quét Barcode + Enter**. Hỗ trợ hiển thị đáp ứng (Responsive) trên Desktop, Tablet và máy POS cầm tay. | Quét + `Enter` → `F4` → xác nhận (2 bước, `test:live` 8/8); chưa thử máy POS cầm tay | *Một phần* |
| `NFR-MAINT-01`<br>Clean Architecture | Phân tách nghiêm ngặt mối quan tâm qua 4 tầng: `Domain` (Logic lõi: COGS, Reserve), `Application` (Use cases), `Infrastructure` (EF Core, PostgreSQL, SignalR) và `Presentation` (Web API). | 4 project, quy tắc phụ thuộc khóa bằng `ArchitectureRulesTests` (5/5) | **Đạt** |
| `NFR-MAINT-02`<br>Automated Test Coverage | Các module cốt lõi (`Inventory Ledger`, `Anti-Oversell Reserve`, `COGS`) phải duy trì độ phủ **Unit & Integration Test tối thiểu 80%**. | Ledger 90,90% · Reserve 92,51% · WAC 100% · COGS 89,35% | **Đạt** |

---

## 6. Kết Quả Kiểm Thử (chốt 21/11/2026)

### 6.1. Môi trường kiểm thử

| Hạng mục | Thông số |
| :--- | :--- |
| Hệ điều hành | Ubuntu 26.04.1 LTS, Linux kernel 7.0.0-43-generic (x86_64) |
| CPU | AMD Ryzen 7 H 255, 16 luồng (vCPU) |
| RAM | 22 GiB |
| Nền tảng chạy | .NET SDK 8.0, Node.js 22, Docker 29 (Docker Compose) |
| Cơ sở dữ liệu | PostgreSQL 16 (`postgres:16-alpine`, `/dev/shm` 256 MB), DB cô lập riêng cho mỗi lần chạy test |
| Dữ liệu đo hiệu năng | DB `oisms_perf`: 50.000 đơn, 100.000 dòng hàng, 100.025 bút toán sổ kho, 25 SKU |
| Dữ liệu đánh giá dự báo | DB `oisms_forecast_eval`: 8 SKU × 90 ngày dữ liệu tổng hợp |
| Phạm vi | Mọi phép đo chạy trên máy local; bản demo AWS không dùng làm môi trường nghiệm thu |

### 6.2. Kiểm thử tự động

| Bộ kiểm thử | Môi trường | Kết quả |
| :--- | :--- | :---: |
| Backend Unit (`OISM.UnitTests`) | EF Core InMemory | **434 / 434 PASS** |
| Backend Integration (`OISM.IntegrationTests`) | PostgreSQL 16 thật, DB cô lập mỗi lần chạy | **481 / 481 PASS** |
| POS (`npm test`) | Node, chạy component thật | **101 / 101 PASS** |
| POS E2E (`npm run test:live`) | Docker | **8 / 8 PASS** |
| Giả lập sàn (`npm test`) | Node | **9 / 9 PASS** |
| Build 3 frontend + lint POS | Vite / oxlint | **PASS** |

### 6.3. Độ phủ kiểm thử phần lõi (`NFR-MAINT-02`, ngưỡng ≥ 80%)

| Phần lõi | Coverage |
| :--- | :---: |
| Sổ cái (Ledger) | **90,90%** |
| Giữ chỗ (Reserve) | **92,51%** |
| Giá vốn (WAC) | **100,00%** |
| Doanh thu / COGS / Lãi gộp | **89,35%** |

### 6.4. Hiệu năng (`NFR-PERF-01`, đo local trên 100.000 dòng hàng)

| Thao tác | Ngưỡng | p95 (10 luồng) | Kết quả |
| :--- | :---: | :---: | :---: |
| Tìm SKU (`/api/pos/search`) | < 200 ms | 24,92 ms | Đạt |
| Quét mã vạch (`/api/pos/barcode`) | < 200 ms | 10,05 ms | Đạt |
| Tạo đơn Admin (`POST /api/orders`) | < 500 ms | 230,94 ms | Đạt |
| POS checkout, 10 quầy khác SKU | < 500 ms | 53,08 – 58,35 ms | Đạt |
| Báo cáo doanh thu (8 kịch bản lọc) | < 2.000 ms | ≤ 1.160,97 ms | Đạt |
| POS checkout, 10 quầy **cùng 1 SKU** | < 500 ms | 379 – 577 ms | Giới hạn đã biết* |

\* Mười giao dịch cùng một SKU phải xếp hàng qua khóa chống bán âm; đây là đánh đổi có chủ đích để đảm bảo `NFR-PERF-02`.

### 6.5. Đồng thời và dự báo

| Kiểm thử | Kết quả |
| :--- | :--- |
| 50 đơn đồng thời tranh 1 SKU cuối (`NFR-PERF-02`) | **1 thành công, 49 HTTP 409, tồn khả dụng = 0** |
| Backtest gợi ý nhập hàng (`FR-AI-01`, dữ liệu tổng hợp 8 SKU × 90 ngày) | **WAPE 7,70%** (ngưỡng ≤ 35%), **Recall 100%** (7/7) |
| E2E chuyển kho hai bước và kiểm kê trên Admin | **8 / 8 bước PASS** |

---

## 7. Giới Hạn Đã Biết

1. **Môi trường đo:** hiệu năng, TLS 1.3 và uptime được đo trên Docker local. Bản demo AWS là phần nhóm làm thêm, không phải môi trường nghiệm thu.
2. **Thiết bị:** chưa có máy quét mã vạch cầm tay và máy POS cầm tay để kiểm chứng `FR-PROD-03` (phần kiểm kê) và `NFR-USA-02` (phần thiết bị).
3. **Dữ liệu dự báo:** backtest chạy trên dữ liệu tổng hợp, chưa đối chiếu dữ liệu bán hàng thực tế dài hạn.

---

## 8. Phần Làm Thêm Ngoài Đề Bài

- **Theo góp ý của GVHD:** mua hàng có duyệt và phân bổ phí VAS 02 (Upgrade 01, 12); truy vết và in phiếu xuất kho 02-VT (Upgrade 02, 14); lịch kiểm kê và duyệt chênh lệch (Upgrade 03); bảng giá theo hiệu lực (Upgrade 04); đa đơn vị tính (Upgrade 05); rà soát Clean Architecture (Upgrade 06); báo cáo tồn kho theo kỳ, thẻ kho S12/S10 (Upgrade 13).
- **Chức năng bổ sung:** VietQR tại POS, trả hàng gắn đơn gốc, quản lý nhân viên và phân công chi nhánh, tìm kiếm toàn hệ thống, Dashboard KPI, quên mật khẩu và đăng nhập bằng SĐT, mail chào mừng chủ shop, Trợ lý OISMS hỏi đáp chỉ đọc (`FR-AI-02`).
- **Triển khai:** bản demo trên AWS EC2 (Docker Compose, Caddy HTTPS), CI bằng GitHub Actions.
