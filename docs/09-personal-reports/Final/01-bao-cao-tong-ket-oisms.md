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

Tài liệu này tóm tắt mức độ đáp ứng đề bài và kết quả kiểm thử để thầy tiện theo dõi. Chi tiết nghiệp vụ, sơ đồ và đóng góp từng thành viên nằm trong năm báo cáo cá nhân đi kèm.

---

## 3. Tổng Quan Mức Độ Đáp Ứng Đề Bài

| Nhóm yêu cầu | Kết quả | Còn lại |
| :--- | :---: | :--- |
| Yêu cầu chức năng (`30 FR` + `FR-AI-01`) | **30 / 31** | `FR-PROD-03`: chưa quét mã vạch ở màn kiểm kê |
| Yêu cầu phi chức năng (`11 NFR`) | **9 / 11** | `NFR-USA-01` chưa đo uptime dài giờ; `NFR-USA-02` chưa thử máy POS cầm tay |
| Gói công việc | **4 / 5** | Gói 5: đo uptime, thiết bị cầm tay, hoàn thiện hồ sơ |
| Nhóm hồ sơ nghiệm thu | **2 / 6** | URS/RTM, Kiến trúc, UML 2.0, HDSD đang rà soát đồng bộ với code |

Quy ước: `[x]` đã đạt, có bằng chứng; `[ ]` chưa đạt hoặc đạt một phần (ghi rõ phần còn thiếu).

---

## 4. Checklist Yêu Cầu Chức Năng (FR)

**Xác thực, đa khách thuê, phân quyền**

- [x] `FR-AUTH-01` Đăng ký cửa hàng, `TenantId` riêng, cô lập dữ liệu.
- [x] `FR-AUTH-02` Đăng nhập email/SĐT, JWT 60 phút + refresh token, đăng xuất.
- [x] `FR-AUTH-03` Phân quyền `Owner` / `Staff` / `Cashier` trên API và giao diện.
- [x] `FR-AUTH-04` Owner quản lý chi nhánh, kho.

**Danh mục, SKU, mã vạch, giá**

- [x] `FR-PROD-01` Danh mục cha–con, thương hiệu.
- [x] `FR-PROD-02` Sản phẩm, biến thể, SKU duy nhất trong tenant.
- [ ] `FR-PROD-03` Mã vạch EAN-13/Code128: sinh, nhập và quét tại POS **đã có**; quét ở màn kiểm kê **chưa làm** (không có thiết bị quét để kiểm chứng).
- [x] `FR-PROD-04` Giá bán lẻ/sỉ theo SKU, tách riêng giá vốn.

**Sổ cái và nghiệp vụ kho**

- [x] `FR-INV-01` Sổ cái chỉ ghi thêm; trigger PostgreSQL chặn `UPDATE`/`DELETE`/`TRUNCATE`.
- [x] `FR-INV-02` Phiếu nhập từ nhà cung cấp tăng tồn, cập nhật giá vốn.
- [x] `FR-INV-03` Chuyển kho hai bước (xuất gửi → xác nhận nhận).
- [x] `FR-INV-04` Kiểm kê, đối chiếu, Owner duyệt chênh lệch, ghi bút toán điều chỉnh.

**Giá vốn, giữ chỗ, chống bán vượt**

- [x] `FR-COST-01` Tính giá vốn bình quân gia quyền (WAC) khi xác nhận nhập.
- [x] `FR-COST-02` Chụp `OrderItem.CostPrice` khi xuất đơn / thanh toán POS.
- [x] `FR-RSE-01` `available = on_hand − reserved` theo SKU, chi nhánh.
- [x] `FR-RSE-02` Giữ chỗ khi nhận đơn online, từ chối khi thiếu hàng.
- [x] `FR-RSE-03` Xác nhận thì trừ tồn + ghi sổ; hủy/hết hạn thì giải phóng giữ chỗ.

**Đơn hàng đa kênh và POS**

- [x] `FR-ORD-01` Đơn POS, Admin, sàn TMĐT về một mô hình chung.
- [x] `FR-ORD-02` Vòng đời `Draft → Reserved → Confirmed → Completed`, `Cancelled` khi hủy hợp lệ.
- [x] `FR-ORD-03` Xem đơn chờ, duyệt, in phiếu giao, hủy đơn.
- [x] `FR-POS-01` Tìm nhanh tên/SKU/mã vạch, phím tắt.
- [x] `FR-POS-02` Chặn bán vào phần đã giữ cho đơn online.
- [x] `FR-POS-03` Tiền mặt / VietQR, tự mở in biên nhận.
- [x] `FR-POS-04` Checkout nguyên tử trong một giao dịch.

**Giả lập sàn, realtime, tác vụ nền, báo cáo, dự báo**

- [x] `FR-SIM-01` Giả lập webhook Shopee / TikTok Shop / Lazada.
- [x] `FR-SIM-02` Thông báo đơn mới realtime (SignalR) cho Admin và POS.
- [x] `FR-SIM-03` Hangfire tự hủy giữ chỗ quá hạn, tổng hợp báo cáo ngày.
- [x] `FR-REP-01` Doanh thu thuần, COGS, lãi gộp; lọc thời gian, chi nhánh, kênh, tối đa 20 SKU.
- [x] `FR-REP-02` Giá trị tồn, hàng bán chạy, hàng chậm luân chuyển.
- [x] `FR-REP-03` Cảnh báo tồn thấp theo ngưỡng.
- [x] `FR-AI-01` Dự báo nhu cầu và gợi ý nhập hàng.

---

## 5. Checklist Yêu Cầu Phi Chức Năng (NFR)

- [x] `NFR-PERF-01` Tìm SKU < 200 ms, ghi < 500 ms, báo cáo < 2 s — đạt trên môi trường local (chi tiết mục 6.3).
- [x] `NFR-PERF-02` 50 đơn đồng thời tranh 1 SKU cuối: đúng 1 thành công, 49 nhận `409`, tồn không âm.
- [x] `NFR-TENANT-01` Cô lập dữ liệu giữa các tenant.
- [x] `NFR-TENANT-02` Chỉ mục `(TenantId, CreatedAt)`, `(TenantId, SKUId)`; `EXPLAIN ANALYZE` trên 100.000 dòng.
- [x] `NFR-SEC-01` BCrypt, JWT 60 phút + refresh token, chỉ cho TLS 1.3 (kiểm chứng trên Caddy local).
- [x] `NFR-SEC-02` Giao dịch nguyên tử, rollback toàn phần khi lỗi giữa chừng.
- [x] `NFR-SEC-03` Sổ cái cấm sửa/xóa ở mức cơ sở dữ liệu.
- [ ] `NFR-USA-01` Uptime 99,5% khung 07:00–22:00 — đã có script đo, chưa thu thập dài giờ.
- [ ] `NFR-USA-02` POS < 3 thao tác — **thao tác đạt** (quét + `Enter` → `F4` → xác nhận); chưa thử máy POS cầm tay.
- [x] `NFR-MAINT-01` Clean Architecture 4 tầng, quy tắc phụ thuộc khóa bằng test.
- [x] `NFR-MAINT-02` Coverage ≥ 80% cho Ledger, Reserve, COGS.

---

## 6. Kết Quả Kiểm Thử (chốt 21/11/2026)

### 6.1. Kiểm thử tự động

| Bộ kiểm thử | Môi trường | Kết quả |
| :--- | :--- | :---: |
| Backend Unit (`OISM.UnitTests`) | EF Core InMemory | **434 / 434 PASS** |
| Backend Integration (`OISM.IntegrationTests`) | PostgreSQL 16 thật, DB cô lập mỗi lần chạy | **481 / 481 PASS** |
| POS (`npm test`) | Node, chạy component thật | **101 / 101 PASS** |
| POS E2E (`npm run test:live`) | Docker | **8 / 8 PASS** |
| Giả lập sàn (`npm test`) | Node | **9 / 9 PASS** |
| Build 3 frontend + lint POS | Vite / oxlint | **PASS** |

### 6.2. Độ phủ kiểm thử phần lõi (`NFR-MAINT-02`, ngưỡng ≥ 80%)

| Phần lõi | Coverage |
| :--- | :---: |
| Sổ cái (Ledger) | **90,90%** |
| Giữ chỗ (Reserve) | **92,51%** |
| Giá vốn (WAC) | **100,00%** |
| Doanh thu / COGS / Lãi gộp | **89,35%** |

### 6.3. Hiệu năng (`NFR-PERF-01`, đo local trên 100.000 dòng hàng)

| Thao tác | Ngưỡng | p95 (10 luồng) | Kết quả |
| :--- | :---: | :---: | :---: |
| Tìm SKU (`/api/pos/search`) | < 200 ms | 24,92 ms | Đạt |
| Quét mã vạch (`/api/pos/barcode`) | < 200 ms | 10,05 ms | Đạt |
| Tạo đơn Admin (`POST /api/orders`) | < 500 ms | 230,94 ms | Đạt |
| POS checkout, 10 quầy khác SKU | < 500 ms | 53,08 – 58,35 ms | Đạt |
| Báo cáo doanh thu (8 kịch bản lọc) | < 2.000 ms | ≤ 1.160,97 ms | Đạt |
| POS checkout, 10 quầy **cùng 1 SKU** | < 500 ms | 379 – 577 ms | Giới hạn đã biết* |

\* Mười giao dịch cùng một SKU phải xếp hàng qua khóa chống bán âm; đây là đánh đổi có chủ đích để đảm bảo `NFR-PERF-02`.

### 6.4. Đồng thời và dự báo

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

---

## 9. Tài Liệu Đi Kèm

| Tài liệu | Nội dung |
| :--- | :--- |
| Báo cáo cá nhân 02–06 | Đóng góp từng thành viên, sơ đồ nghiệp vụ, đối chiếu đề bài chi tiết |
| `checklist.md` | Checklist đầy đủ kèm liên kết code, test và bằng chứng cho từng mã yêu cầu |
| `docs/06-testing-and-qa/05-ket-qua-kiem-thu-tong-hop.md` | Kết quả kiểm thử tổng hợp và lệnh chạy lại |
