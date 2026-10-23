# 03. Đối Chiếu Nguyên Văn Đề Bài & Danh Mục Truy Vết (Original Proposal Reference)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-OVW-03` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-22` |
| **Nguồn truy vết (Source Traceability)** | `C:\Users\Jacki Khoa\Documents\De bai CNPM.pdf` (Toàn văn Trang 1 – Trang 6) |
| **Phạm vi áp dụng** | Chuẩn đối chiếu nghiệm thu cho toàn bộ dự án OISMS |

---

## Lịch sử Phiên bản (Version History)

| Phiên bản | Ngày cập nhật | Tác giả / Vai trò | Mô tả thay đổi | Mã tham chiếu |
| :---: | :---: | :--- | :--- | :--- |
| `v1.0.0` | `2026-10-22` | Nhóm dự án OISMS | Chuẩn hóa bảng tham chiếu toàn bộ 28 Yêu cầu chức năng (FR), 11 Yêu cầu phi chức năng (NFR), 5 Gói công việc và Checklist nghiệm thu từ `De bai CNPM.pdf`. | `INIT-OVW-03` |

---

## 1. Bảng Tổng Hợp Truy Vết Yêu Cầu Chức Năng (Functional Requirements Catalog)

> *Truy vết nguồn: Mục 3.2.c - Functional Requirements (Trang 1, 2, 3, 6, `De bai CNPM.pdf`)*

| Nhóm | Mã yêu cầu | Tên yêu cầu (Nguyên gốc tiếng Anh) | Mô tả chi tiết (Chuẩn hóa tiếng Việt) | Trang PDF | Tài liệu đặc tả chi tiết |
| :---: | :---: | :--- | :--- | :---: | :--- |
| **AUTH** | `FR-AUTH-01` | Tenant Registration | Cho phép tạo mới các Cửa hàng (Store Tenants) với không gian dữ liệu cô lập gắn với `TenantId` duy nhất. | Trang 1 | `02-functional-requirements/fr-01-auth-tenant.md` |
| **AUTH** | `FR-AUTH-02` | User Authentication | Đăng nhập/Đăng xuất qua Email hoặc SĐT + Mật khẩu; trả về JWT chứa `TenantId`, `UserId` và `Role`. | Trang 1 | `02-functional-requirements/fr-01-auth-tenant.md` |
| **AUTH** | `FR-AUTH-03` | Role-Based Access Control (RBAC) | Cấu hình và kiểm soát quyền API/Màn hình theo 3 vai trò: `Owner` (Toàn quyền), `Staff` (Quản lý Kho/Đơn hàng), `Cashier` (Chỉ POS). | Trang 1 | `01-users-and-rbac/02-rbac-matrix.md` |
| **AUTH** | `FR-AUTH-04` | Branch Management | Cho phép `Owner` tạo, cập nhật hoặc bật/tắt hiển thị các chi nhánh và kho hàng thuộc sở hữu của mình. | Trang 1 | `02-functional-requirements/fr-01-auth-tenant.md` |
| **PROD** | `FR-PROD-01` | Category & Brand Management | Quản lý cây danh mục sản phẩm phân cấp (cha - con) và danh mục thương hiệu. | Trang 1 | `02-functional-requirements/fr-02-product-catalog.md` |
| **PROD** | `FR-PROD-02` | Product & Variant/SKU Management | Hỗ trợ sản phẩm đơn giản và sản phẩm có biến thể (Màu sắc, Kích thước...). Mỗi biến thể có mã `SKU` duy nhất trong cùng Tenant. | Trang 1-2 | `02-functional-requirements/fr-02-product-catalog.md` |
| **PROD** | `FR-PROD-03` | Barcode Management | Tự động sinh hoặc nhập thủ công mã vạch (`EAN-13` / `Code128`) cho từng SKU để quét POS và kiểm kê kho. | Trang 2 | `02-functional-requirements/fr-02-product-catalog.md` |
| **PROD** | `FR-PROD-04` | Listed Price Management | Thiết lập giá bán lẻ (Retail) và giá bán sỉ (Wholesale) theo từng SKU (tách biệt với giá vốn COGS). | Trang 2 | `02-functional-requirements/fr-02-product-catalog.md` |
| **INV** | `FR-INV-01` | Inventory Ledger Recording | Ghi nhận nghiêm ngặt mọi biến động kho (Nhập, Xuất, Bán, Trả hàng, Kiểm kê, Chuyển kho) vào sổ cái chỉ ghi thêm `InventoryTransaction` (`TenantId`, `BranchId`, `SKUId`, `Type (IN/OUT)`, `Quantity`, `BalanceAfter`, `ReferenceId`, `CreatedAt`). Nghiêm cấm sửa trực tiếp cột `on_hand`. | Trang 2 | `02-functional-requirements/fr-03-inventory-ledger.md` |
| **INV** | `FR-INV-02` | Purchase Receipt Management | Xử lý phiếu nhập hàng từ nhà cung cấp $\rightarrow$ Tăng tồn vật lý (`on_hand`) $\rightarrow$ Cập nhật lại giá vốn bình quân. | Trang 2 | `02-functional-requirements/fr-03-inventory-ledger.md` |
| **INV** | `FR-INV-03` | Stock Transfer Management | Chuyển kho giữa Chi nhánh A và Chi nhánh B qua quy trình 2 bước: Xuất kho vận chuyển (`Out-bound transit`) $\rightarrow$ Xác nhận nhập kho nhận (`In-bound confirmation`). | Trang 2 | `02-functional-requirements/fr-03-inventory-ledger.md` |
| **INV** | `FR-INV-04` | Stocktake Management | Tạo phiên kiểm kê kho, ghi nhận số đếm thực tế, đối chiếu với số dư phần mềm và sinh bút toán điều chỉnh (`Adjustment Ledger entries`) để cân bằng kho. | Trang 2 | `02-functional-requirements/fr-03-inventory-ledger.md` |
| **COST** | `FR-COST-01` | Automated Batch COGS Calculation | Tự động tính lại giá vốn đơn vị SKU ngay khi xác nhận Phiếu nhập kho theo công thức bình quân gia quyền (WAC). | Trang 2 | `02-functional-requirements/fr-04-cost-wac.md` |
| **COST** | `FR-COST-02` | COGS Snapshot on Dispatch | Chụp (snapshot) chính xác giá vốn bình quân đơn vị vào dòng đơn hàng (`OrderItem.CostPrice`) khi đơn chuyển sang `Confirmed` hoặc thanh toán tại POS. | Trang 2 | `02-functional-requirements/fr-04-cost-wac.md` |
| **ORD** | `FR-ORD-01` | Omnichannel Order Ingestion | Chuẩn hóa đơn hàng đa kênh (POS, tạo thủ công trên Admin, Webhook giả lập Shopee/TikTok) vào một Mô hình Đơn hàng Hợp nhất (`Canonical Order Model`). | Trang 2 | `02-functional-requirements/fr-06-omnichannel-orders.md` |
| **ORD** | `FR-ORD-02` | Order State Machine | Thực thi nghiêm ngặt vòng đời chuyển trạng thái đơn hàng: `Draft -> Reserved -> Confirmed -> Completed` (hoặc `Cancelled`). | Trang 2 | `02-functional-requirements/fr-06-omnichannel-orders.md` |
| **ORD** | `FR-ORD-03` | Order Processing | Cho phép nhân viên xem đơn chờ xử lý, duyệt đơn, in phiếu giao hàng hoặc hủy đơn. | Trang 2 | `02-functional-requirements/fr-06-omnichannel-orders.md` |
| **RSE** | `FR-RSE-01` | Available Stock Calculation | Duy trì động số lượng tồn khả dụng theo từng SKU tại mỗi chi nhánh: `available = on_hand - reserved`. | Trang 2 | `02-functional-requirements/fr-05-stock-reservation.md` |
| **RSE** | `FR-RSE-02` | Stock Reservation Execution | Tăng `reserved` khi đơn online đi vào Order Hub ở trạng thái `Reserved`. Từ chối đơn nếu `available < Purchased Quantity`. | Trang 2-3 | `02-functional-requirements/fr-05-stock-reservation.md` |
| **RSE** | `FR-RSE-03` | Deduct/Release Execution | Khi `Confirmed`: Trừ `on_hand`, giảm `reserved`, ghi giao dịch Sổ cái. Khi `Cancelled`: Giảm `reserved` để hoàn trả tồn về quỹ `available`. | Trang 3 | `02-functional-requirements/fr-05-stock-reservation.md` |
| **POS** | `FR-POS-01` | Fast Sales Interface | Giao diện tối ưu cho màn hình cảm ứng và phím tắt, hỗ trợ tìm kiếm sản phẩm tức thì theo Tên / SKU / Barcode. | Trang 3 | `02-functional-requirements/fr-07-pos-checkout.md` |
| **POS** | `FR-POS-02` | Counter Sales Constraint | Kiểm tra tức thì tồn khả dụng (`available`) khi chọn hàng; chặn bán vượt tồn khả dụng để bảo vệ phần hàng đã giữ chỗ cho đơn online. | Trang 3 | `02-functional-requirements/fr-07-pos-checkout.md` |
| **POS** | `FR-POS-03` | Checkout & Receipt Printing | Hỗ trợ thanh toán Tiền mặt, Chuyển khoản QR, và tự động kích hoạt hộp thoại in hóa đơn qua Browser Print API. | Trang 3 | `02-functional-requirements/fr-07-pos-checkout.md` |
| **POS** | `FR-POS-04` | Fast Checkout Workflow | Thực thi chuỗi `Reserve -> Confirm -> Deduct` trong một giao dịch cơ sở dữ liệu nguyên tử duy nhất (Single Atomic Database Transaction) khi thanh toán POS. | Trang 3 | `02-functional-requirements/fr-07-pos-checkout.md` |
| **REP** | `FR-REP-01` | Revenue & Gross Profit Report | Tổng hợp Doanh thu thuần, Tổng giá vốn (COGS) và Lợi nhuận gộp (`Gross Profit = Revenue - COGS`). Lọc theo Thời gian, Chi nhánh, Kênh bán và SKU. | Trang 3 | `02-functional-requirements/fr-11-reports-analytics.md` |
| **REP** | `FR-REP-02` | Inventory & Velocity Report | Theo dõi giá trị tồn kho theo giá vốn, danh sách mặt hàng bán chạy (Best-sellers) và hàng luân chuyển chậm (Slow-moving items). | Trang 3 | `02-functional-requirements/fr-11-reports-analytics.md` |
| **REP** | `FR-REP-03` | Stock Alert | Gửi thông báo cảnh báo tự động trên Dashboard khi `available <= Threshold` (ngưỡng đặt hàng tối thiểu của từng SKU). | Trang 3 | `02-functional-requirements/fr-11-reports-analytics.md` |
| **SIM** | `FR-SIM-01` | E-commerce Webhook Simulator | Công cụ giả lập cho phép Dev/Tester bắn các payload giả lập (Shopee/TikTok/Lazada) để kiểm thử thông lượng và khóa giữ chỗ. | Trang 3 | `02-functional-requirements/fr-08-marketplace-sync.md` |
| **SIM** | `FR-SIM-02` | Real-time Push Notification | Sử dụng **SignalR** đẩy cảnh báo âm thanh/popup đơn hàng mới trực tiếp lên Admin/POS mà không cần tải lại trang. | Trang 3 | `02-functional-requirements/fr-09-realtime-signalr.md` |
| **SIM** | `FR-SIM-03` | Background Processing | Sử dụng **Hangfire** cho các tác vụ bất đồng bộ nặng: tự động hủy các đơn `Reserved` quá hạn và tổng hợp báo cáo hằng ngày. | Trang 3 | `02-functional-requirements/fr-10-background-hangfire.md` |
| **AI** | `FR-AI-01` | AI Stock Reorder Forecasting | Tích hợp dự báo AI để phân tích xu hướng và đề xuất gợi ý nhập lại hàng (Stock reorder recommendations). | Trang 6 | `02-functional-requirements/fr-12-ai-forecasting.md` |

---

## 2. Bảng Tổng Hợp Truy Vết Yêu Cầu Phi Chức Năng (Non-Functional Requirements Catalog)

> *Truy vết nguồn: Mục 3.2.d - Non-Functional Requirements (Trang 3, 4, `De bai CNPM.pdf`)*

| Nhóm | Mã NFR | Tên yêu cầu | Chỉ tiêu kỹ thuật bắt buộc | Trang PDF | Tài liệu đặc tả chi tiết |
| :---: | :---: | :--- | :--- | :---: | :--- |
| **PERF** | `NFR-PERF-01` | API Response Time | Tìm kiếm SKU / Quét barcode POS: `p95 < 200ms`. Tạo đơn / Giao dịch ghi: `< 500ms`. Báo cáo doanh thu tổng hợp: `< 2s` cho tập dữ liệu dưới `100,000` bản ghi. | Trang 3 | `03-non-functional-requirements/01-performance-and-load.md` |
| **PERF** | `NFR-PERF-02` | Concurrency & Anti-Oversell | Xử lý kịch bản Flash Sale (`50 đơn online đồng thời tranh 1 SKU khả dụng cuối cùng`). Bắt buộc áp dụng khóa mức CSDL (`SELECT ... FOR UPDATE` hoặc `Version Check`) để ngăn chặn tuyệt đối tồn khả dụng âm. | Trang 3-4 | `03-non-functional-requirements/01-performance-and-load.md` |
| **TENANT** | `NFR-TENANT-01` | Data Isolation | Mô hình Shared Database, Separate Schema/Tenant ID pattern. Mọi bảng nghiệp vụ phải có `TenantId`. Bắt buộc dùng EF Core Global Query Filters để đảm bảo 0% rò rỉ dữ liệu giữa các Tenant. | Trang 4 | `03-non-functional-requirements/02-security-and-isolation.md` |
| **TENANT** | `NFR-TENANT-02` | Scalability | Tối ưu hóa Index trên `(TenantId, CreatedAt)` và `(TenantId, SKUId)` cho bảng `InventoryLedger` và `Orders` để duy trì hiệu năng truy vấn ở quy mô hàng triệu dòng sổ cái. | Trang 4 | `03-non-functional-requirements/02-security-and-isolation.md` |
| **SEC** | `NFR-SEC-01` | Encryption & Authentication | Mật khẩu băm qua `BCrypt` / `Argon2`. Giao tiếp API bảo mật qua `HTTPS/TLS 1.3`. JWT Access Token ngắn hạn (`60 phút`) đi kèm Refresh Token an toàn. | Trang 4 | `03-non-functional-requirements/02-security-and-isolation.md` |
| **SEC** | `NFR-SEC-02` | ACID Compliance | Chuỗi `Order Creation -> Reserve -> Deduct` BẮT BUỘC thực thi bên trong một giao dịch CSDL nguyên tử (Atomic Database Transaction) với cơ chế rollback toàn phần khi gặp lỗi. | Trang 4 | `03-non-functional-requirements/03-reliability-and-acid.md` |
| **SEC** | `NFR-SEC-03` | Append-Only Ledger Auditability | Bảng `InventoryLedger` chỉ cho phép ghi thêm (tuyệt đối cấm `UPDATE` hoặc `DELETE`). Mọi sai sót phải xử lý bằng giao dịch sổ cái bù trừ (Compensating ledger transactions). | Trang 4 | `03-non-functional-requirements/03-reliability-and-acid.md` |
| **USA** | `NFR-USA-01` | Availability | Mục tiêu độ sẵn sàng (Uptime) đạt `99.5%` trong khung giờ hoạt động cao điểm (`07:00 AM – 10:00 PM`). | Trang 4 | `03-non-functional-requirements/04-usability-and-pwa.md` |
| **USA** | `NFR-USA-02` | POS Usability | Giao diện POS tối giản cho phép hoàn tất thanh toán trong **dưới 3 cú nhấp chuột** hoặc qua **Quét Barcode + Enter**. Hỗ trợ hiển thị đáp ứng (Responsive) trên Desktop, Tablet và máy POS cầm tay. | Trang 4 | `03-non-functional-requirements/04-usability-and-pwa.md` |
| **MAINT** | `NFR-MAINT-01` | Clean Architecture | Phân tách nghiêm ngặt mối quan tâm qua 4 tầng: `Domain` (Logic lõi: COGS, Reserve), `Application` (Use cases), `Infrastructure` (EF Core, PostgreSQL, SignalR) và `Presentation` (Web API). | Trang 4 | `03-non-functional-requirements/05-maintainability-testing.md` |
| **MAINT** | `NFR-MAINT-02` | Automated Test Coverage | Các module cốt lõi (`Inventory Ledger`, `Anti-Oversell Reserve`, `COGS`) phải duy trì độ phủ **Unit & Integration Test tối thiểu 80%**. | Trang 4 | `03-non-functional-requirements/05-maintainability-testing.md` |

---

## 3. Danh Mục Hồ Sơ Nghiệm Thu Theo Đề Bài (Mandatory Deliverables Checklist)

> *Truy vết nguồn: Mục 3.2.e & 3.2.f (Trang 4, 5, `De bai CNPM.pdf`)*

- [ ] **1. User Requirement Specification (URS) & Requirements Traceability Matrix (RTM):** Phân hệ `01-users-and-rbac/`, `02-functional-requirements/`, `03-non-functional-requirements/` và `06-testing-and-qa/01-requirements-matrix-rtm.md`.
- [ ] **2. Architecture Design Document:** Phân hệ `04-architecture-and-design/01-clean-architecture.md` & `02-multi-tenant-design.md`.
- [ ] **3. Detailed Design (UML 2.0):** Phân hệ `04-architecture-and-design/03-database-design.md` (ERD), `04-state-machines.md` và `sequence-diagrams/`.
- [ ] **4. API Specifications (Swagger/Postman):** Phân hệ `05-api-contracts/`.
- [ ] **5. Software Testing Document (Unit, Integration, Concurrency):** Phân hệ `06-testing-and-qa/`.
- [ ] **6. System Implementation, Deployment & User Guide:** Phân hệ `07-devops-and-deployment/` và `08-sprints/`.
