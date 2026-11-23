# MUST DO — UPDATE BEFORE FINAL

> **Bắt buộc hoàn thành trước khi nộp bản final.** Lập ngày 21/11/2026 sau khi đối chiếu đề bài ([DE_BAI_OISM.md](../../08-sprints/sprint-2/DE_BAI_OISM.md), [03-de-bai-cnpm-reference.md](../../00-overview/03-de-bai-cnpm-reference.md)) với [checklist.md](../../../checklist.md) v1.9.0 và mã nguồn trên nhánh `feature/OISMS-66-ra-soat-tong-du-an-phat-trien-tinh-nang-con-thieu-lan-2`.
>
> Làm xong mục nào thì đánh dấu `[x]`, ghi ngày, commit và cập nhật mục tương ứng trong `checklist.md`.

**Plan fix và prompt giao Antigravity:** [OISMS-66-final-prompts.md](../../../skills/claude-to-antigravity/references/OISMS-66-final-prompts.md) (lập 21/11/2026).

**Kết quả kiểm thử:** mọi lần chạy test/đo trong plan lưu ở `docs/06-testing-and-qa/evidence/<mã FR/NFR>/`, sổ tổng hợp `docs/06-testing-and-qa/evidence/README.md` và mục "Kết quả kiểm thử bản final (11/2026)" trong `02-unit-and-integration-test.md` / `03-concurrency-stress-test.md`. Nhóm hồ sơ lấy link từ sổ tổng hợp để cập nhật RTM.

**Thứ tự đề xuất:** 1.1 lọc SKU → 2 đo NFR → 4 kiểm thử → 5 lỗi. Mục 1.2 không làm; mục 3 (hồ sơ) đang update.

## 1. Chức năng còn thiếu theo đề bài (đã xác nhận trong code)

- [x] **1.1 `FR-REP-01` — Lọc báo cáo doanh thu/COGS/lãi gộp theo SKU.** ✅ Xong 21/11/2026 (FIN-A, FIN-B VERIFIED).
  - Hiện trạng: `ReportFilterDto` ([ReportDtos.cs:3](../../../src/backend/src/OISM.Application/Features/Reports/Dtos/ReportDtos.cs)) chỉ có `BranchId`, `Channel`, `StartDate`, `EndDate`, `TopN`; chưa có `SkuId`.
  - Đặc tả: [goi-5-bao-cao-doanh-thu-theo-sku.md](./goi-5-bao-cao-doanh-thu-theo-sku.md)
- [—] **1.2 `FR-PROD-03` — Quét mã vạch khi kiểm kê. ⛔ KHÔNG LÀM** (người dùng quyết định 21/11/2026): nhóm không có thiết bị quét để làm và kiểm chứng. Phần sinh/nhập mã EAN-13/Code128 và quét tại POS đã có; khi bảo vệ trình bày đây là giới hạn đã biết.
  - Hiện trạng: POS đã quét được; màn kiểm kê (`admin-dashboard/src/components/stockCounts/`) chỉ chọn SKU rồi nhập số đếm, không có xử lý barcode/scan ở frontend lẫn `Features/StockCounts`.
  - Đặc tả: [goi-2-quet-ma-vach-kiem-ke.md](./goi-2-quet-ma-vach-kiem-ke.md)

## 2. NFR chưa có bằng chứng (5/11)

Chưa có thư mục `test-oisms/nfr/` và `docs/06-testing-and-qa/evidence/` theo [plan Gói 5](./goi-5-plan-trien-khai.md). Đặc tả: [goi-5-kiem-chung-nfr.md](./goi-5-kiem-chung-nfr.md).

- [x] **2.1 `NFR-PERF-01`** — Đo p95 < 200 ms tìm SKU/quét barcode POS, < 500 ms tạo đơn/giao dịch ghi, < 2 s báo cáo với < 100.000 bản ghi. 🔄 21/11/2026: đã đo local (FIN-D + fix1). ĐẠT POS search/barcode, tạo đơn Admin, checkout c=1; Sau FIN-J (VERIFIED 21/11/2026): báo cáo 8/8 ca dưới 2 s khi PostgreSQL đủ `/dev/shm`; đã thêm `shm_size: 256mb` cho `postgres` và đo lại 15/16 ca ĐẠT local ([bằng chứng](../../06-testing-and-qa/evidence/goi-5/NFR-PERF-01/2026-11-21-shm256.md)); checkout POS 10 quầy SKU khác nhau p95 53–58 ms (ĐẠT); 10 quầy cùng 1 SKU p95 379–577 ms là giới hạn đã biết của khóa chống bán âm ([đo](../../06-testing-and-qa/evidence/goi-5/NFR-PERF-01/2026-11-21-checkout-multisku.md)); chưa đo trên bản demo.
- [ ] **2.2 `NFR-SEC-01`** — Xác nhận TLS 1.3 trên bản deploy (BCrypt, JWT 60 phút, refresh token đã có). 🔄 21/11/2026 (FIN-E): ĐẠT trên Caddy local; chờ deploy Caddyfile mới mới có số trên bản demo.
- [ ] **2.3 `NFR-USA-01`** — Số đo uptime 99,5% khung 07:00–22:00.
- [ ] **2.4 `NFR-USA-02`** — Nghiệm thu thanh toán POS < 3 lần bấm hoặc quét + Enter, trên desktop, tablet, máy cầm tay.
- [x] **2.5 `NFR-MAINT-02`** — Đo lại coverage ≥ 80% cho Ledger, Reserve, COGS trên bản hiện tại (số cũ là Sprint 2). ✅ 21/11/2026 (FIN-F): Ledger 90,90%, Reserve 92,51%, WAC 100%, COGS 89,35%.

Mỗi phép đo ghi: commit, cấu hình máy, dữ liệu, lệnh, kết quả thô, trạng thái ĐẠT / CHƯA ĐẠT.

## 3. Hồ sơ bàn giao (4/6 nhóm chưa đạt) — 🔄 ĐANG UPDATE

Nhóm đang tự cập nhật, không đưa vào plan giao Antigravity. Đặc tả: [goi-5-ho-so-ban-giao.md](./goi-5-ho-so-ban-giao.md).

- [ ] **3.1 URS/RTM** — 🔄 Đang update. Tách RTM thành mỗi mã FR/NFR một dòng, có bằng chứng riêng (liên kết bằng chứng mục 2 khi có).
- [ ] **3.2 Kiến trúc** — 🔄 Đang update. Rà lại Package Diagram (mục 1 SDD).
- [ ] **3.3 SDD UML 2.0** — 🔄 Đang update. Mục 3.4–3.6 (mua hàng, chuyển kho, kiểm kê) và 3.10–3.11 (RBAC, báo cáo).
- [ ] **3.4 Hướng dẫn sử dụng** — 🔄 Đang update. Đối chiếu đủ các luồng Admin/POS.

## 4. Kiểm thử và đánh giá còn mở

- [x] **4.1** Biên bản E2E chuyển kho và kiểm kê trên giao diện. ✅ 21/11/2026 (FIN-H): [biên bản](../../06-testing-and-qa/evidence/e2e-kho/README.md).
- [x] **4.2 CS-08 / `FR-AI-01`** — Đối chiếu dự báo với dữ liệu thực tế + tiêu chí chất lượng. [goi-5-danh-gia-du-bao-nhap-hang.md](./goi-5-danh-gia-du-bao-nhap-hang.md) ✅ 21/11/2026 (FIN-C + fix1): WAPE 7,70%, recall 100% (7/7) trên dữ liệu tổng hợp; chưa đối chiếu dữ liệu bán thật.
- [x] **4.3** Hồi quy POS [BF-019](../../bug-fixaffter/errors/BF-019-pos-nhan-http-200-sai-contract-la.md) và [BF-020](../../bug-fixaffter/errors/BF-020-pos-rieng-chua-khoa-gio-va-ngu.md). ✅ 21/11/2026 (FIN-G): có test hồi quy, BF-020 đã sửa trong `PaymentModal.tsx`.

## 5. Ngoài đề bài — nên xử lý hoặc chốt trước final

- [ ] **5.1 Lỗi chưa sửa:**
  - BF-024 — tìm kiếm phiếu nhập mở sai phiếu (P2). ✅ Đã sửa 21/11/2026 (FIN-I).
  - BF-025 — test AC01 fail trên Windows do CRLF (P2).
  - BF-026 — `/health/ready` trả tên user DB (P3). ✅ Đã sửa 21/11/2026 (FIN-E), cần deploy Caddyfile mới.
  - BF-027 — báo cáo cộng số lượng khác đơn vị tính (P3). Đã chốt 21/11/2026: trả `null` khi khác đơn vị gốc. ✅ Đã sửa (FIN-I).
- [ ] **5.2 BF-021** — Checklist lỗi ghi chưa sửa, ma trận RBAC ghi đã sửa. Xác nhận rồi đóng.
- [ ] **5.3 Nhóm chốt:** bỏ đặc tả màn Handoff hay làm lại; có cấm số lẻ với đơn vị đếm ("Cái") không.
- Tạm gác (không bắt buộc): webhook ngân hàng VietQR (Upgrade 07 giai đoạn C), OTP SMS (Upgrade 10 giai đoạn 3), Tổng quan lọc theo thời gian (CS-07).
