# CHECKLIST NGHIỆM THU ĐỒ ÁN OISMS (SPRINT 2)
> **Học phần:** Công nghệ Phần mềm (Mã HP: 122005 - 3 Tín chỉ) — Trường Đại học Giao thông Vận tải TP.HCM (UTH)  
> **Dự án:** OISMS — Omnichannel Inventory & Sales Management System (Hệ thống Quản lý Bán hàng & Tồn kho Đa kênh)  
> **Cấu trúc nộp bài:** Tuân thủ 100% sơ đồ cấu trúc Repository chuẩn do Giảng viên yêu cầu trên lớp.

---

## I. KIỂM TRA CẤU TRÚC REPOSITORY (PROJECT ARCHITECTURE CHECKLIST)

| Thành phần | Đường dẫn thư mục | Trạng thái | Mô tả chi tiết |
| :--- | :--- | :---: | :--- |
| **`Docs/`** | `docs/` |  Hoàn thành | Chứa toàn bộ tài liệu đặc tả dạng `.md` do từng thành viên tự commit và push (`Member push/git`). |
| **`BE/`** | `BE/` &rarr; `src/backend/` |  Hoàn thành | Mã nguồn Backend .NET 8 Web API, C# Clean Architecture (Domain, Application, Infrastructure, WebApi). |
| **`FE/`** | `FE/` &rarr; `src/frontend/` |  Hoàn thành | Mã nguồn Frontend: Web Admin Dashboard (React 19 + Tailwind) và Web POS PWA (Touch UI + Barcode). |
| **`README.MD`**| `README.md` |  Hoàn thành | Bản thuyết minh tổng thể dự án, hướng dẫn dựng môi trường Docker Compose và chạy kiểm thử tự động. |
| **`Check_list.md`**| `Check_list.md` |  Hoàn thành | Danh mục kiểm tra tiêu chuẩn nghiệm thu và chỉ số chất lượng đồ án dành cho Giảng viên chấm điểm. |
| **`Skills/`** | `Skills/` |  Hoàn thành | Hồ sơ minh chứng 5 nhóm kỹ năng chuyên môn và kỹ thuật công nghệ lõi của các thành viên. |

---

## II. CHECKLIST YÊU CẦU CHỨC NĂNG (FUNCTIONAL REQUIREMENTS - SPRINT 2)

### 1. Phân hệ Xác thực, Đa người thuê & Phân quyền (`FR-AUTH`) — Phụ trách: Nhựt Phùng Minh
- [x] **`FR-AUTH-01`**: Đăng ký Tenant Cửa hàng mới, tự động khởi tạo không gian dữ liệu cô lập với `TenantId` riêng biệt.
- [x] **`FR-AUTH-02`**: Đăng nhập/Đăng xuất bằng Email/SĐT + Mật khẩu băm BCrypt; cấp phát JWT Access Token (60 phút) + Refresh Token.
- [x] **`FR-AUTH-03`**: Phân quyền RBAC 4 vai trò (`Owner`, `Staff`, `Cashier`, `Guest`). Chốt chặn 2 tầng: React Route Guard và Backend `[Authorize]`.
- [x] **`FR-AUTH-04`**: Quản lý chi nhánh (`Branches`) và kho hàng (`Warehouses`) trực thuộc cửa hàng; hỗ trợ bật/tắt kích hoạt.
- [x] **Ma trận phân quyền 3.1.3 (`OISMS-31`)**: Đặc tả 39 màn hình giao diện đối chiếu quyền hạn C/R/U/D/S trên Confluence và Git.

### 2. Phân hệ Danh mục, Sản phẩm & Mã vạch (`FR-PROD`) — Phụ trách: Nhựt Phùng Minh
- [x] **`FR-PROD-01`**: Quản lý danh mục hàng hóa phân cấp cha-con và thương hiệu; cài đặt thuật toán **DFS Cycle Detection** chống tạo vòng lặp chu trình.
- [x] **`FR-PROD-02`**: Quản lý sản phẩm đơn giản và sản phẩm biến thể (Màu sắc, Kích cỡ); quản lý mã SKU duy nhất theo từng Tenant.
- [x] **`FR-PROD-03`**: Tự động sinh mã vạch quốc tế **EAN-13** chuẩn xác bằng thuật toán **Modulo 10**, hỗ trợ quét bằng máy đọc laser.
- [x] **`FR-PROD-04`**: Quản lý giá bán lẻ, giá bán sỉ theo từng mã SKU, tách biệt hoàn toàn khỏi giá vốn.
- [x] **Chống trùng biến thể SKU**: Cài đặt chữ ký chuẩn hóa **Canonical Hash Signature** trên trường `jsonb` thuộc tính sản phẩm.

### 3. Phân hệ Sổ cái kho & Giá vốn bình quân (`FR-INV`, `FR-COST`) — Phụ trách: Lê Võ Đăng Khoa
- [x] **`FR-INV-01`**: Sổ cái kho bất biến (**Append-Only Inventory Ledger**). Mọi biến động kho đều ghi nhận bản ghi mới; cấm hoàn toàn `UPDATE / DELETE`.
- [x] **`FR-INV-02`**: Phiếu nhập kho từ nhà cung cấp: Tăng số lượng tồn thực tế (`on_hand`) và chốt giao dịch trong Database Transaction nguyên tử.
- [x] **`FR-COST-01`**: Tự động tính lại Giá vốn bình quân gia quyền (**Weighted Average Cost - WAC**) ngay khi duyệt phiếu nhập kho.
- [x] **`FR-COST-02`**: Chụp nhanh (Snapshot) giá vốn tại thời điểm chốt đơn và ghi nhận cố định vào `OrderItem.CostPrice`.

### 4. Phân hệ Đơn hàng & Chống bán vượt tồn kho (`FR-ORD`, `FR-RSE`) — Phụ trách: Khoa & Phương
- [x] **`FR-ORD-01`**: Chuẩn hóa mô hình đơn hàng thống nhất từ quầy POS, Admin và kênh sàn thương mại điện tử.
- [x] **`FR-ORD-02`**: Quản lý vòng đời đơn hàng qua Máy trạng thái (State Machine): `Draft` &rarr; `Reserved` &rarr; `Confirmed` &rarr; `Completed` / `Cancelled`.
- [x] **`FR-RSE-01`**: Công thức tính tồn kho khả dụng thời gian thực: `Available = OnHand - Reserved`.
- [x] **`FR-RSE-02`**: Giữ chỗ tự động khi có đơn đặt online; từ chối đơn hàng nếu số lượng mua vượt quá số lượng khả dụng.
- [x] **`NFR-PERF-02`**: Giải quyết bài toán tranh chấp 50 request tranh 1 sản phẩm cuối cùng bằng **PostgreSQL Pessimistic Row Lock (`FOR UPDATE`)**.

### 5. Phân hệ Quầy bán hàng nhanh Web POS (`FR-POS`) — Phụ trách: Phuongw Le & Nhựt
- [x] **`FR-POS-01`**: Giao diện bán hàng tối ưu cảm ứng, hỗ trợ phím tắt F2 tra cứu nhanh, quét mã vạch < 200ms.
- [x] **`FR-POS-02`**: Kiểm tra tồn kho khả dụng trực tiếp từ CSDL, không bán vào phần tồn kho đã được giữ chỗ cho đơn online.
- [x] **`FR-POS-03`**: Thanh toán đa phương thức (Tiền mặt, QR Chuyển khoản) và hỗ trợ in hóa đơn bán lẻ trực tiếp từ trình duyệt.
- [x] **`FR-POS-04`**: Chuỗi thanh toán POS nguyên tử: Reserve &rarr; Confirm &rarr; Deduct &rarr; Write Ledger trong cùng một giao dịch.

### 6. Phân hệ Giao diện Quản trị Web Admin — Phụ trách: Phúc Hữu
- [x] Giao diện Admin Dashboard hoàn chỉnh (React 19, Tailwind CSS, Vite) tích hợp đầy đủ 10 màn hình quản trị.
- [x] Bảng Sổ cái kho trực quan phân loại bút toán `IN` (Xanh) và `OUT` (Đỏ), thể hiện số dư lũy kế `BalanceAfter`.
- [x] Tích hợp bộ lọc phân quyền menu sidebar tự động ẩn mục Báo cáo tài chính đối với tài khoản không phải Owner.

---

## III. CHECKLIST YÊU CẦU PHI CHỨC NĂNG (NON-FUNCTIONAL REQUIREMENTS)

- [x] **`NFR-PERF-01` (Hiệu năng):** Quét mã vạch và tìm kiếm sản phẩm tại quầy POS phản hồi với độ trễ **$p95 < 200\text{ ms}$** dưới tải 50 kết nối đồng thời.
- [x] **`NFR-TENANT-01` (Cô lập dữ liệu):** 100% câu truy vấn dữ liệu được bảo vệ bằng EF Core Global Query Filter, triệt tiêu rủi ro rò rỉ dữ liệu giữa các tenant.
- [x] **`NFR-SEC-01` (Bảo mật tài khoản):** Băm mật khẩu chuẩn BCrypt, hỗ trợ xoay vòng Refresh Token (Token Rotation) nguyên tử chống tấn công Replay Attack.
- [x] **`NFR-SEC-02` (Tính toàn vẹn giao dịch):** Đảm bảo tính nguyên tử (Atomicity) theo chuẩn ACID; rollback 100% nếu xảy ra lỗi trong chuỗi xử lý.
- [x] **Xử lý triệt để Unhappy Paths:** Không để phát sinh lỗi `500 Crash Server`; 100% ngoại lệ được chuyển hóa thành mã lỗi chuẩn (`400`, `401`, `403`, `409`).

---

## IV. CHECKLIST KIỂM THỬ & CHẤT LƯỢNG MÃ NGUỒN (TESTING & VERIFICATION)

- [x] **Tổng số bài kiểm thử tự động:** **194 / 194 Tests PASS 100%**.
  * Unit Tests nghiệp vụ: **103 bài**.
  * Integration Tests trên PostgreSQL thật (`oisms_test`): **91 bài**.
- [x] **Code Coverage tầng Application & Domain:** Đạt **94.83%** (vượt xa tiêu chuẩn 80% của đồ án môn học).
- [x] **Continuous Integration (CI):** Pipeline GitHub Actions tự động build, kiểm tra format code và chạy toàn bộ 194 bài test mỗi khi mở Pull Request.
- [x] **Môi trường Container hóa:** 6 container Docker (`backend`, `admin`, `pos`, `postgres`, `redis`, `pgadmin`) hoạt động ổn định và có healthcheck tự động.

---

## V. CHECKLIST ĐÓNG GÓP CỦA TỪNG THÀNH VIÊN (MEMBER CONTRIBUTIONS & GIT TRACEABILITY)

| Thành viên | Vai trò | Phân hệ phụ trách chính | Minh chứng Git / PR | File Báo cáo trong `Docs/` |
| :--- | :--- | :--- | :---: | :--- |
| **Lê Võ Đăng Khoa** | Nhóm trưởng / Backend 1 | Kiến trúc CSDL, Sổ kho, WAC, Docker, CI/CD | PR #1, PR #3, PR #12 | `docs/Dang Khoa report/S2_FINAL_HANDOVER_ACCEPTANCE_2026-10-19.md` |
| **Nhựt Phùng Minh** | Backend 2 Lead | Auth JWT, RBAC, Danh mục, SKU, Barcode, POS API | PR #4, PR #14 | `docs/Nhut Report/README.md`<br>`docs/CONFLUENCE_3_1_3_SCREEN_AUTHORIZATION_MATRIX.md` |
| **Phúc Hữu** | Frontend Admin Lead | Giao diện Quản trị Web Admin, Route Guard | PR #10 | `docs/Phúc hữu report/README.md` |
| **Phuongw Le** | Frontend POS Lead | Giao diện Quầy Web POS, Quét mã vạch, Background Jobs | PR #11 | `docs/Phương report/README.md` |
| **Nam Nguyễn** | QA & Tester Lead | Kịch bản kiểm thử E2E, Ma trận truy vết RTM | Test Suite | `docs/TEST_PLAN_SPRINT_2_AND_3.md` |

---

## VI. KẾT LUẬN NGHIỆM THU
* Toàn bộ 5 hạng mục theo sơ đồ cấu trúc của Giảng viên (`Docs/`, `BE/`, `FE/`, `README.md`, `Check_list.md`, `Skills/`) đã được thiết lập đầy đủ, chính xác và đồng bộ.
* Dự án sẵn sàng 100% để bảo vệ trước Hội đồng chấm đồ án học phần Công nghệ Phần mềm (122005) - UTH.
