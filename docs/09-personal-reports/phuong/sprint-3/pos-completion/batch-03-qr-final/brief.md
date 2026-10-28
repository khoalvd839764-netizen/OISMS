# Brief Công việc — S3-POS-B03: QR và Nghiệm thu Cuối cùng POS

| Thông tin kiểm soát | Chi tiết |
| :--- | :--- |
| **Mã đợt (Batch ID)** | `S3-POS-B03` |
| **Phiên bản (Version)** | `v1.0-parallel` |
| **Ngày giao việc** | `28/10/2026` |
| **Phụ trách triển khai** | **Phương Lê — Frontend POS** (Antigravity) |
| **Workspace & Branch** | `D:/OISMS` — Branch `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don` |
| **Baseline commit** | HEAD `44317f3b5188c276fa3ef25c8faf30323e503079` + Working tree hiện hữu |
| **Phạm vi đợt gộp** | Toàn bộ **S3-POS-T04 (T04-AC-01..05)** + **S3-POS-T06 (T06-AC-01..07)** |
| **Trạng thái đợt** | **IMPLEMENTING** (Hoàn thành phần độc lập song song; giữ gate chờ B02 VERIFIED) |

---

## 1. Bối cảnh & Mục tiêu

- Đợt T01 và B01 đã đạt **VERIFIED**.
- Đợt B02 đang hoàn thiện sửa F04 (thứ tự cập nhật tồn kho khi barcode/search trả về) và live verification F06 (Realtime transport/reconnect).
- Người dùng cho phép triển khai **phần độc lập của S3-POS-B03 trước** (song song với B02); nghiệm thu tích hợp cuối cùng vẫn bắt buộc phải chờ B02 đạt VERIFIED.
- **Mục tiêu của Đợt B03:**
  1. Làm rõ dứt điểm hiện trạng QR, hợp đồng backend và nguyên nhân finding PR24-F04.
  2. Chuẩn bị module xử lý & kiểm tra dữ liệu QR độc lập, bảo đảm chuẩn EMVCo / VietQR NAPAS 247, kiểm tra số tiền, identity và tính bất biến (Idempotent Invariance) khi retry.
  3. Chuẩn bị ma trận tích hợp toàn diện T06-AC-01..07 và runner E2E có Database Guard để bảo vệ môi trường thử nghiệm song song.
  4. Xác lập rõ ràng điều kiện tích hợp và bàn giao trung thực khi B02 sẵn sàng.

---

## 2. Khóa Phạm vi Giai đoạn Song song

### Trong phạm vi cho phép (In-Scope — Giai đoạn song song):
- Chỉ thêm module QR độc lập, fixtures kiểm thử, test tự động riêng và tài liệu kỹ thuật tại đường dẫn:
  `docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-03-qr-final/`
- Chuẩn bị runner E2E có guard kiểm tra CSDL và dữ liệu fixtures cho toàn bộ 7 tiêu chí T06.

### Tuyệt đối ngoài phạm vi (Out-of-Scope — Chặn sửa đổi):
- **Không sửa các file dùng chung đang được B02 chỉnh sửa:**
  - `src/frontend/pos-app/src/App.tsx`
  - `src/frontend/pos-app/src/components/PaymentModal.tsx`
  - `src/frontend/pos-app/src/components/ProductSearch.tsx`
  - `src/frontend/pos-app/src/services/api.ts`
  - `src/frontend/pos-app/src/services/stockService.ts`
  - `src/frontend/pos-app/src/services/realtimeService.ts`
  - `src/frontend/pos-app/src/types/pos.ts`
  - `src/frontend/pos-app/package.json` / `package-lock.json`
  - `src/frontend/pos-app/public/sw.js`
- **Không sửa test hoặc báo cáo của B02** (`batch-02-stock-realtime-pwa/*`).
- **Không sửa `PLAN.md` và các báo cáo tổng kết chung** trong giai đoạn song song.
- **Không reset, clean, stash, chuyển nhánh, commit, push hoặc merge.**
- **Không coi hiển thị QR, quét QR hoặc countdown hết giờ là thanh toán thành công.**
- **Không chuyển tiền thật, không thêm schema CSDL hay xây dựng hệ thống đối soát ngân hàng.**

---

## 3. Danh mục Tiêu chí Nghiệm thu Gốc (AC Mapping)

### 3.1. Phân hệ QR — S3-POS-T04

| Mã AC | Tiêu chí nghiệm thu gốc | Điều kiện đạt |
| :---: | :--- | :--- |
| **T04-AC-01** | Ma trận hiện trạng QR | Phân định rõ 3 mức: (1) SVG mô phỏng tĩnh; (2) QR mã hóa payload thanh toán thật (EMVCo/VietQR); (3) Đối soát giao dịch ngân hàng tự động. Xác định mức phải giao Sprint 3 và phần chuyển tiếp. |
| **T04-AC-02** | Cấu hình & Sinh Payload QR thực | Có nguồn cấu hình tài khoản thụ hưởng rõ ràng, chuẩn sinh mã EMVCo / QuickLink chuẩn hóa; số tiền và nội dung gắn chặt với định danh giao dịch (`RequestKey`); không hardcode thông tin giả vào production; không tự tạo giao dịch mới khi retry/chuyển tab. |
| **T04-AC-03** | Xác nhận chuyển khoản an toàn | Luồng xác nhận chuyển khoản đúng hợp đồng backend. Xác định rõ người xác nhận (Thu ngân xác nhận tại quầy). Tuyệt đối **KHÔNG** suy ra thanh toán thành công chỉ vì QR được hiển thị, được quét hoặc hết thời gian đếm ngược. |
| **T04-AC-04** | Minh bạch hồ sơ nghiệm thu | Ghi nhận đúng hiện trạng: UI và báo cáo ghi rõ phạm vi thực tế, cảnh báo mô phỏng/chuyển tiếp; xóa bỏ toàn bộ claim vơ 100% POS/QR khi chưa đủ điều kiện. |
| **T04-AC-05** | Bộ test kiểm thử dữ liệu QR | Test payload bằng cấu hình fixture được ghi rõ là "Dữ liệu kiểm thử", validate số tiền, identity, lỗi cấu hình và luồng frontend; không chuyển tiền thật. Bằng chứng nào chưa có ghi rõ `NOT RUN / BLOCKED`. |

### 3.2. Phân hệ Nghiệm thu Tích hợp Cuối cùng — S3-POS-T06

| Mã AC | Tiêu chí nghiệm thu gốc | Điều kiện đạt |
| :---: | :--- | :--- |
| **T06-AC-01** | Full-stack Browser + Backend + PostgreSQL Test | Chạy browser với backend và PostgreSQL test có guard/marker. Đăng nhập, chọn branch, search/barcode, checkout tiền mặt, receipt/in lại, thiếu tồn, lỗi mạng sau commit, 401/relogin, đổi tài khoản/branch, SignalR/reconnect đều có evidence. |
| **T06-AC-02** | Network Loss Retry (Idempotent Recovery) | Case response mất sau commit: retry bằng frontend thật, DB cuối đúng 1 order/payment/lần OUT; cart chỉ xóa khi đã xác định success, key/payload không đổi khi còn unknown. Không dùng server model stub làm bằng chứng PostgreSQL. |
| **T06-AC-03** | Bộ test Suite tổng hợp | Có test module/component thật cho F01/F02/F03 và các negative paths; giữ test cũ; `npm run build`, `npm test` và bộ tests mới đều PASS, ghi đúng runner/count/provider. |
| **T06-AC-04** | Đóng hồ sơ Finding PR24-F01..F04 | Bảng PR24-F01..F04 có evidence đóng, quyết định QR còn lại rõ. Báo cáo bỏ claim 100% khi có AC chưa đạt; cập nhật checklist/phần việc đúng người. |
| **T06-AC-05** | Git sync Develop & Head CI Success | Fetch develop/head, kiểm tra diff/conflict trên nhánh POS và giữ cập nhật docs PR #25. CI cho head cuối success; backend suite cục bộ chỉ chạy lại khi có rủi ro liên quan. |
| **T06-AC-06** | Cập nhật Mô tả PR #24 | Cập nhật PR #24 hiện có theo template/quy tắc repo: phạm vi, hành vi trước/sau, contract, test và giới hạn; không tạo PR trùng, force-push hay tự merge develop. |
| **T06-AC-07** | Nghiệm thu Dứt điểm POS-STOCK-F01 | Nghiệm thu lại POS-STOCK-F01 qua frontend và backend test: Available 10, checkout mua 2 → UI 8 không F5; replay/event đến sau vẫn 8. Có case Hub offline, API refresh lỗi và đổi branch trước response cũ. |

---

## 4. Ranh giới Nghiệm thu & Chốt chặn Tích hợp (Gates)

1. **Gate 1 — Làm rõ QR (QR Decision Gate):**
   - Đã khảo sát và làm rõ toàn bộ 4 câu hỏi cốt lõi tại `evidence/qr-clarification-analysis.md`.
   - Chuẩn bị sẵn module QR độc lập và bộ 9 test cases đạt 100% PASS.
2. **Gate 2 — Chờ B02 VERIFIED (B02 Verification Gate):**
   - B02 hiện đang sửa F04 và F06. Khi B02 được reviewer xác nhận **VERIFIED** và ngừng chỉnh sửa các file dùng chung, B03 mới đọc lại git diff, pin baseline mới và tiến hành tích hợp.
3. **Gate 3 — Chốt an toàn CSDL (Database Guard Gate):**
   - Runner E2E chỉ được kích hoạt live khi có PostgreSQL test riêng (port khác 5432, ví dụ 5433). Tuyệt đối không reset CSDL demo trong lúc B02 đang kiểm thử.
