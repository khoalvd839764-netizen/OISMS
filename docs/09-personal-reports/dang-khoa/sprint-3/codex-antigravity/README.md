# Hướng Dẫn Giao Tiếp Codex – Antigravity & Mapping Tài Liệu Sprint 3

Tài liệu này quy chuẩn hóa toàn bộ quy ước phối hợp giữa **Codex** (vai trò Phân tích, Giao task & Review độc lập) và **Antigravity** (vai trò Triển khai, Viết code, Kiểm chứng & Bàn giao) theo skill [`codex-antigravity-workflow`](../../../../.agents/skills/codex-antigravity-workflow/SKILL.md).

---

## 1. Quy Ước Phối Hợp & Tiến Trình Bàn Giao

### 1.1. Chu trình trạng thái công việc (Lifecycle)

```
[READY] ➔ [IMPLEMENTING] ➔ [READY_FOR_REVIEW] ➔ [VERIFIED] (Hoàn tất)
                                      ▲              │
                                      │              ▼
                                      └── [CHANGES_REQUESTED] (Vòng fix)
```

1. **`READY`:** Codex phân tích yêu cầu từ Jira/SRS, soạn thảo brief chuẩn với các tiêu chí nghiệm thu (`AC-xx`) có mã định danh bất biến, baseline rõ ràng và bộ kiểm chứng cần đạt.
2. **`IMPLEMENTING`:** Antigravity tiếp nhận brief, rà soát diff hiện tại, triển khai giải pháp tối thiểu đáp ứng toàn bộ AC, chạy kiểm thử tự động theo đúng provider thực tế (PostgreSQL/Hangfire/SignalR).
3. **`READY_FOR_REVIEW`:** Antigravity bàn giao kết quả kèm bảng ma trận AC, log kiểm thử thực tế, provider và danh sách file thay đổi. *Antigravity tuyệt đối không tự đánh dấu VERIFIED.*
4. **`CHANGES_REQUESTED`:** Nếu phát hiện lỗi hoặc thiếu sót, Codex phát hành biên bản review với các finding có mã định danh ổn định (`Tx-Fxx`), nêu rõ điều kiện đóng. Antigravity chỉ sửa các finding còn mở, không viết lại toàn bộ task.
5. **`VERIFIED`:** Khi 100% tiêu chí AC đạt và toàn bộ findings được đóng bằng bằng chứng runner thực tế, Codex chính thức kết luận `VERIFIED`.

### 1.2. Cấu trúc thư mục chuẩn cho từng Task

Mỗi task kỹ thuật được bố trí thành một thư mục riêng biệt với cấu trúc:
```text
task-xx-<name>/
  README.md         # Mục lục, thông tin kiểm soát, baseline, bảng findings và link tới các vòng
  briefs/           # Các bản brief giao việc từ Codex (v1.0, v1.1, ...)
  handoffs/         # Báo cáo kỹ thuật và bàn giao READY_FOR_REVIEW của Antigravity (v1.0, v1.1, ...)
  reviews/          # Biên bản đánh giá độc lập của Codex (v1.0-review-r1, v1.1-review-r2, ...)
```

* **Xác định bản mới nhất:** Phiên bản hiện hành luôn được ghi rõ tại `README.md` của từng task và liên kết trực tiếp đến file handoff/review có số hiệu revision cao nhất.
* **Bảo toàn lịch sử:** Các biên bản cũ ở trạng thái `CHANGES_REQUESTED` hoặc `READY_FOR_REVIEW` được giữ nguyên vẹn để bảo đảm khả năng truy vết, không sửa hồi tố.

---

## 2. Bảng Tổng Hợp Trạng Thái Các Task Sprint 3 (Backend 1)

| Mã Task | Tên Nhiệm vụ / Phân hệ | Triển khai | Reviewer | Phiên bản | Trạng thái Nghiệm thu | Đường dẫn chi tiết |
| :--- | :--- | :---: | :---: | :---: | :---: | :--- |
| **`S3-BE1-T01`** | API Contract & Persistence Foundation | Antigravity | Báo cáo gốc | `v1.0` | **DONE (theo báo cáo gốc)** | [`task-01-contract-foundation/`](./task-01-contract-foundation/README.md) |
| **`S3-BE1-T02`** | Atomic POS Checkout (Toàn giỏ nguyên tử) | Antigravity | Codex | `v1.2` | **VERIFIED v1.2** | [`task-02-pos-checkout/`](./task-02-pos-checkout/README.md) |
| **`S3-BE1-T03`** | Online Order Lifecycle (State Machine) | Antigravity | Codex | `v1.1` | **VERIFIED v1.1** | [`task-03-order-lifecycle/`](./task-03-order-lifecycle/README.md) |
| **`S3-BE1-T04A`**| Two-Step Stock Transfer (Chuyển kho 2 bước) | Antigravity | Codex | `v1.2` | **VERIFIED v1.2** | [`task-04a-stock-transfer/`](./task-04a-stock-transfer/README.md) |
| **`S3-BE1-T04B`**| Stocktaking & Reconciliation (Kiểm kê kho) | Antigravity | Codex | `v1.3` | **VERIFIED v1.3** | [`task-04b-stock-count/`](./task-04b-stock-count/README.md) |
| **`S3-BE1-T05`** | Omnichannel Webhook & SignalR Realtime | Antigravity | Codex | `v1.4` | **VERIFIED v1.4** | [`task-05-webhook-realtime/`](./task-05-webhook-realtime/README.md) |
| **`S3-BE1-T06`** | Hangfire Reservation Expiry Worker | Antigravity | Codex | `v1.2` | **VERIFIED v1.2** | [`task-06-reservation-expiry/`](./task-06-reservation-expiry/README.md) |
| **`S3-BE1-DOCS`**| Chuẩn hóa Docs, Tổng kết BE1 & PR develop | Antigravity | Codex | `v1.0` | **IMPLEMENTING** | [`docs-pr-develop/`](./docs-pr-develop/README.md) |

---

## 3. Bảng Mapping Truy Vết Toàn Diện (Old Monolithic Sections ➔ New Files)

Toàn bộ 44 mục từ tài liệu monolithic cũ (`docs/09-personal-reports/dang-khoa/sprint-3/README.md`) đã được phân rã và ánh xạ chính xác sang các tài liệu con chuyên biệt:

| Mục cũ (Anchor cũ) | Tiêu đề nội dung trích xuất | Đường dẫn file mới tương ứng |
| :--- | :--- | :--- |
| **Mục 1** (`#1-mục-tiêu--phạm-vi-objectives--scope`) | Mục tiêu & Phạm vi Task 1 | [`task-01-contract-foundation/handoffs/v1.0-handoff.md`](./task-01-contract-foundation/handoffs/v1.0-handoff.md) |
| **Mục 2** (`#2-kế-thừa-nền-tảng-sprint-2-inheritance-analysis`) | Kế thừa nền tảng Sprint 2 | [`task-01-contract-foundation/handoffs/v1.0-handoff.md`](./task-01-contract-foundation/handoffs/v1.0-handoff.md) |
| **Mục 3** (`#3-các-quyết-định-kỹ-thuật--lý-do-technical-decisions`) | Các quyết định kỹ thuật Task 1 | [`task-01-contract-foundation/handoffs/v1.0-handoff.md`](./task-01-contract-foundation/handoffs/v1.0-handoff.md) |
| **Mục 4** (`#4-danh-sách-file-thay-đổi--bổ-sung-changelog`) | Danh sách file thay đổi Task 1 | [`task-01-contract-foundation/handoffs/v1.0-handoff.md`](./task-01-contract-foundation/handoffs/v1.0-handoff.md) |
| **Mục 5** (`#5-bảng-bàn-giao-cho-các-thành-viên-handover-matrix`) | Bảng bàn giao thành viên Task 1 | [`task-01-contract-foundation/handoffs/v1.0-handoff.md`](./task-01-contract-foundation/handoffs/v1.0-handoff.md) |
| **Mục 6** (`#6-lệnh-kiểm-chứng--kết-quả-thực-tế-verification-evidence`) | Lệnh kiểm chứng & Kết quả Task 1 | [`task-01-contract-foundation/handoffs/v1.0-handoff.md`](./task-01-contract-foundation/handoffs/v1.0-handoff.md) |
| **Mục 7** (`#7-checklist-nghiệm-thu-task-1--kế-hoạch-triển-khai-tiếp-theo`) | Checklist nghiệm thu & Kế hoạch Task 1 | [`task-01-contract-foundation/handoffs/v1.0-handoff.md`](./task-01-contract-foundation/handoffs/v1.0-handoff.md) |
| **Mục 8** (`#8-báo-cáo-kỹ-thuật-task-2-thanh-toán-nhanh-quầy-pos-toàn-giỏ-nguyên-tử-s3-be1-t02-pos-checkout`) | Báo cáo kỹ thuật POS Checkout v1.0 | [`task-02-pos-checkout/handoffs/v1.0-handoff.md`](./task-02-pos-checkout/handoffs/v1.0-handoff.md) |
| **Mục 9** (`#9-báo-cáo-fix-và-review-vòng-2-revision-v12--s3-be1-t02-pos-checkout`) | Báo cáo fix Task 2 v1.2 | [`task-02-pos-checkout/handoffs/v1.2-handoff.md`](./task-02-pos-checkout/handoffs/v1.2-handoff.md) |
| **Mục 10** (`#10-kết-luận-kiểm-chứng-độc-lập-của-codex--vòng-3--v12`) | Codex review Task 2 R3 (VERIFIED v1.2) | [`task-02-pos-checkout/reviews/v1.2-review-r3.md`](./task-02-pos-checkout/reviews/v1.2-review-r3.md) |
| **Mục 11** (`#11-báo-cáo-triển-khai-task-3-vòng-đời-đơn-hàng-trực-tuyến-ngoài-pos-online-order-lifecycle---v10`) | Báo cáo triển khai Task 3 v1.0 | [`task-03-order-lifecycle/handoffs/v1.0-handoff.md`](./task-03-order-lifecycle/handoffs/v1.0-handoff.md) |
| **Mục 12** (`#12-codex-review-task-3--vòng-1--v10`) | Codex review Task 3 R1 | [`task-03-order-lifecycle/reviews/v1.0-review-r1.md`](./task-03-order-lifecycle/reviews/v1.0-review-r1.md) |
| **Mục 13** (`#13-báo-cáo-xử-lý-review-vòng-1--revision-v11-24102026`) | Báo cáo xử lý review Task 3 v1.1 | [`task-03-order-lifecycle/handoffs/v1.1-handoff.md`](./task-03-order-lifecycle/handoffs/v1.1-handoff.md) |
| **Mục 14** (`#14-codex-review-task-3--vòng-2--v11-verified`) | Codex review Task 3 R2 (VERIFIED v1.1) | [`task-03-order-lifecycle/reviews/v1.1-review-r2.md`](./task-03-order-lifecycle/reviews/v1.1-review-r2.md) |
| **Mục 15** (`#15-báo-cáo-triển-khai-task-4a--chuyển-kho-nội-bộ-2-bước-s3-be1-t04a-stock-transfer--v10`) | Báo cáo triển khai Task 4A v1.0 | [`task-04a-stock-transfer/handoffs/v1.0-handoff.md`](./task-04a-stock-transfer/handoffs/v1.0-handoff.md) |
| **Mục 16** (`#16-codex-review-task-4a--vòng-1--v10-changes_requested`) | Codex review Task 4A R1 | [`task-04a-stock-transfer/reviews/v1.0-review-r1.md`](./task-04a-stock-transfer/reviews/v1.0-review-r1.md) |
| **Mục 17** (`#17-báo-cáo-triển-khai-task-4a--sửa-review-vòng-1-s3-be1-t04a-stock-transfer--v11`) | Báo cáo sửa review Task 4A v1.1 | [`task-04a-stock-transfer/handoffs/v1.1-handoff.md`](./task-04a-stock-transfer/handoffs/v1.1-handoff.md) |
| **Mục 18** (`#18-codex-review-task-4a--vòng-2--v11-changes_requested`) | Codex review Task 4A R2 | [`task-04a-stock-transfer/reviews/v1.1-review-r2.md`](./task-04a-stock-transfer/reviews/v1.1-review-r2.md) |
| **Mục 19** (`#19-báo-cáo-triển-khai-task-4a--hoàn-thiện-kiểm-chứng--report-s3-be1-t04a-stock-transfer--v12`) | Báo cáo hoàn thiện Task 4A v1.2 | [`task-04a-stock-transfer/handoffs/v1.2-handoff.md`](./task-04a-stock-transfer/handoffs/v1.2-handoff.md) |
| **Mục 20** (`#20-codex-review-task-4a--vòng-3--v12-verified`) | Codex review Task 4A R3 (VERIFIED v1.2) | [`task-04a-stock-transfer/reviews/v1.2-review-r3.md`](./task-04a-stock-transfer/reviews/v1.2-review-r3.md) |
| **Mục 21** (`#21-báo-cáo-triển-khai-task-4b-stock-count--v10`) | Báo cáo triển khai Task 4B v1.0 | [`task-04b-stock-count/handoffs/v1.0-handoff.md`](./task-04b-stock-count/handoffs/v1.0-handoff.md) |
| **Mục 22** (`#22-codex-review-task-4b--vòng-1--v10-changes_requested`) | Codex review Task 4B R1 | [`task-04b-stock-count/reviews/v1.0-review-r1.md`](./task-04b-stock-count/reviews/v1.0-review-r1.md) |
| **Mục 23** (`#23-bàn-giao-task-4b--fix-review-vòng-1--v11-ready_for_review-v11`) | Bàn giao fix review Task 4B v1.1 | [`task-04b-stock-count/handoffs/v1.1-handoff.md`](./task-04b-stock-count/handoffs/v1.1-handoff.md) |
| **Mục 24** (`#24-codex-review-task-4b--vòng-2--v11-changes_requested`) | Codex review Task 4B R2 | [`task-04b-stock-count/reviews/v1.1-review-r2.md`](./task-04b-stock-count/reviews/v1.1-review-r2.md) |
| **Mục 25** (`#25-bàn-giao-task-4b--vòng-3--v12-ready_for_review-v12`) | Bàn giao Task 4B v1.2 | [`task-04b-stock-count/handoffs/v1.2-handoff.md`](./task-04b-stock-count/handoffs/v1.2-handoff.md) |
| **Mục 26** (`#26-codex-review-task-4b--vòng-3--v12-changes_requested-chỉ-còn-f05`) | Codex review Task 4B R3 | [`task-04b-stock-count/reviews/v1.2-review-r3.md`](./task-04b-stock-count/reviews/v1.2-review-r3.md) |
| **Mục 27** (`#27-bàn-giao-task-4b--vòng-4--v13-ready_for_review-v13-hoàn-thiện-duy-nhất-t4b-f05`) | Bàn giao Task 4B v1.3 | [`task-04b-stock-count/handoffs/v1.3-handoff.md`](./task-04b-stock-count/handoffs/v1.3-handoff.md) |
| **Mục 28** (`#28-codex-review-task-4b--vòng-4--v13-verified`) | Codex review Task 4B R4 (VERIFIED v1.3) | [`task-04b-stock-count/reviews/v1.3-review-r4.md`](./task-04b-stock-count/reviews/v1.3-review-r4.md) |
| **Mục 29** (`#29-bàn-giao-task-5--s3-be1-t05-webhook-realtime--v10-ready_for_review`) | Bàn giao Task 5 v1.0 | [`task-05-webhook-realtime/handoffs/v1.0-handoff.md`](./task-05-webhook-realtime/handoffs/v1.0-handoff.md) |
| **Mục 30** (`#30-codex-review-task-5--vòng-1--v10-changes_requested`) | Codex review Task 5 R1 | [`task-05-webhook-realtime/reviews/v1.0-review-r1.md`](./task-05-webhook-realtime/reviews/v1.0-review-r1.md) |
| **Mục 31** (`#31-bàn-giao-task-5--s3-be1-t05-webhook-realtime--v11-ready_for_review-v11`) | Bàn giao Task 5 v1.1 | [`task-05-webhook-realtime/handoffs/v1.1-handoff.md`](./task-05-webhook-realtime/handoffs/v1.1-handoff.md) |
| **Mục 32** (`#32-codex-review-task-5--vòng-2--bàn-giao-v11--changes_requested`) | Codex review Task 5 R2 | [`task-05-webhook-realtime/reviews/v1.1-review-r2.md`](./task-05-webhook-realtime/reviews/v1.1-review-r2.md) |
| **Mục 33** (`#33-bàn-giao-task-5--s3-be1-t05-webhook-realtime--v12-ready_for_review-v12`) | Bàn giao Task 5 v1.2 | [`task-05-webhook-realtime/handoffs/v1.2-handoff.md`](./task-05-webhook-realtime/handoffs/v1.2-handoff.md) |
| **Mục 34** (`#34-codex-review-task-5--vòng3--bàn-giao-v12--changes_requested`) | Codex review Task 5 R3 | [`task-05-webhook-realtime/reviews/v1.2-review-r3.md`](./task-05-webhook-realtime/reviews/v1.2-review-r3.md) |
| **Mục 35** (`#35-bàn-giao-task-5--s3-be1-t05-webhook-realtime--v13-ready_for_review`) | Bàn giao Task 5 v1.3 | [`task-05-webhook-realtime/handoffs/v1.3-handoff.md`](./task-05-webhook-realtime/handoffs/v1.3-handoff.md) |
| **Mục 36** (`#36-codex-review-task-5--vòng4--bàn-giao-v13--changes_requested`) | Codex review Task 5 R4 | [`task-05-webhook-realtime/reviews/v1.3-review-r4.md`](./task-05-webhook-realtime/reviews/v1.3-review-r4.md) |
| **Mục 37** (`#37-bàn-giao-task-5--s3-be1-t05-webhook-realtime--v14-ready_for_review`) | Bàn giao Task 5 v1.4 | [`task-05-webhook-realtime/handoffs/v1.4-handoff.md`](./task-05-webhook-realtime/handoffs/v1.4-handoff.md) |
| **Mục 38** (`#38-codex-review-task-5--vòng5--v14--verified`) | Codex review Task 5 R5 (VERIFIED v1.4) | [`task-05-webhook-realtime/reviews/v1.4-review-r5.md`](./task-05-webhook-realtime/reviews/v1.4-review-r5.md) |
| **Mục 39** (`#39-bàn-giao-task-6--s3-be1-t06-reservation-expiry--v10-ready_for_review`) | Bàn giao Task 6 v1.0 | [`task-06-reservation-expiry/handoffs/v1.0-handoff.md`](./task-06-reservation-expiry/handoffs/v1.0-handoff.md) |
| **Mục 40** (`#40-codex-review-task-6--r1--bàn-giao-v10--changes_requested`) | Codex review Task 6 R1 | [`task-06-reservation-expiry/reviews/v1.0-review-r1.md`](./task-06-reservation-expiry/reviews/v1.0-review-r1.md) |
| **Mục 41** (`#41-bàn-giao-task-6--s3-be1-t06-reservation-expiry--v11-ready_for_review`) | Bàn giao Task 6 v1.1 | [`task-06-reservation-expiry/handoffs/v1.1-handoff.md`](./task-06-reservation-expiry/handoffs/v1.1-handoff.md) |
| **Mục 42** (`#42-codex-review-task-6--r2--bàn-giao-v11--changes_requested`) | Codex review Task 6 R2 | [`task-06-reservation-expiry/reviews/v1.1-review-r2.md`](./task-06-reservation-expiry/reviews/v1.1-review-r2.md) |
| **Mục 43** (`#43-bàn-giao-task-6-v12--s3-be1-t06-reservation-expiry-ready_for_review-v12`) | Bàn giao Task 6 v1.2 | [`task-06-reservation-expiry/handoffs/v1.2-handoff.md`](./task-06-reservation-expiry/handoffs/v1.2-handoff.md) |
| **Mục 44** (`#44-codex-review-task-6--r3--v12--verified`) | Codex review Task 6 R3 (VERIFIED v1.2) | [`task-06-reservation-expiry/reviews/v1.2-review-r3.md`](./task-06-reservation-expiry/reviews/v1.2-review-r3.md) |

Ngoài ra, các file brief rời cũ đã được chuyển vào đúng cấu trúc:
- `S3-BE1-T05-WEBHOOK-REALTIME-BRIEF.md` ➔ [`task-05-webhook-realtime/briefs/v1.0-brief.md`](./task-05-webhook-realtime/briefs/v1.0-brief.md)
- `S3-BE1-T06-RESERVATION-EXPIRY-BRIEF.md` ➔ [`task-06-reservation-expiry/briefs/v1.0-brief.md`](./task-06-reservation-expiry/briefs/v1.0-brief.md)
