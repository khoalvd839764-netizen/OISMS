# Ma trận Kiểm thử Tích hợp E2E Cuối cùng (S3-POS-B03 / T06)

Ngày lập: **28/10/2026**  
Phụ trách: **Phương Lê — Frontend POS**  
Tiêu chuẩn tham chiếu: `PLAN.md` (Mục 9: S3-POS-T06), `codex-antigravity-workflow/SKILL.md` (Sửa lỗi có bằng chứng).

---

## 1. Nguyên tắc An toàn Môi trường (Test Environment Guard)

1. **Tuyệt đối không chạy trên Database nghiệp vụ / Demo dùng chung:**
   - Database mặc định `localhost:5432` (database `oisms` / `oisms_demo`) đang được B02 sử dụng để kiểm chứng live và chứa dữ liệu demo của người dùng.
   - Toàn bộ các ca kiểm thử E2E tác động ghi CSDL (Checkout, Trừ tồn kho, Ghi Sổ cái `OUT`) **bắt buộc** phải trỏ tới PostgreSQL test riêng biệt (ví dụ port `5433` hoặc database `oisms_test`) có gắn marker an toàn (`POS_TEST_ENV=1` / `TEST_DB_GUARD=1`).
2. **Không nuốt lỗi hoặc giả lập thay thế bằng chứng thật:**
   - Test fixture / mock stub trên frontend chỉ phục vụ kiểm tra độc lập các component logic, **không thay thế bằng chứng PostgreSQL thật và SignalR Hub transport thật**.
   - Mọi kiểm thử chưa chạy được do thiếu môi trường hoặc đang chờ B02 được ghi nhận trung thực là **NOT RUN / BLOCKED**.

---

## 2. Bảng Ma trận Kiểm thử Tích hợp Toàn diện (T06-AC-01..07)

| Mã AC | Tiêu đề kịch bản | Luồng thao tác / Đầu vào | Endpoint & Component | Điều kiện đạt & Assertion | Trạng thái đợt song song |
| :---: | :--- | :--- | :--- | :--- | :---: |
| **T06-AC-01** | Luồng nghiệp vụ tổng thể POS trên Browser + Backend + DB | 1. Đăng nhập `cashier@shopa.local`.<br>2. Chọn Chi nhánh A.<br>3. Tìm kiếm / quét barcode SKU.<br>4. Thanh toán Tiền mặt & QR.<br>5. In hóa đơn & In lại.<br>6. Cảnh báo thiếu tồn (409).<br>7. Mất mạng sau commit & retry.<br>8. 401 hết phiên & đăng nhập lại.<br>9. Đổi user / branch.<br>10. SignalR realtime & reconnect. | - `PosHeader.tsx`<br>- `ProductSearch.tsx`<br>- `CartTable.tsx`<br>- `PaymentModal.tsx`<br>- `ReceiptPrintModal.tsx`<br>- `POST /api/pos/checkout`<br>- `/hubs/notifications` | - Toàn bộ các bước đều có log/screenshot/trace chứng minh.<br>- PostgreSQL ghi nhận đúng 1 Order, 1 Payment, 1 dòng Ledger OUT tương ứng cho mỗi đơn hoàn tất.<br>- UI hiển thị đúng trạng thái. | **CHỜ TÍCH HỢP B02** (Runner đã sẵn sàng, chờ B02 VERIFIED) |
| **T06-AC-02** | Xử lý Mất phản hồi mạng sau khi Server đã Commit (Idempotent Retry) | 1. Khách mua giỏ hàng 2 mặt hàng.<br>2. Bấm Checkout (sinh `RequestKey` UUID v4).<br>3. Server hoàn tất transaction CSDL nhưng mạng bị rớt trước khi client nhận HTTP 200.<br>4. Frontend rơi vào `unknown pending`, giữ nguyên giỏ và giữ nguyên `RequestKey`.<br>5. Bấm "Thử lại thanh toán (Retry)". | - `PaymentModal.tsx`<br>- `pendingCheckoutStore.ts`<br>- `POST /api/pos/checkout`<br>- Header `Idempotency-Key` | - Request thứ hai gửi đi giữ 100% cùng `RequestKey` và cùng `items`.<br>- Backend nhận diện idempotent replay, trả `200 OK` với dữ liệu đơn hàng cũ.<br>- CSDL PostgreSQL **chỉ có duy nhất 1 Order, 1 Payment, 1 lần trừ kho**.<br>- Giỏ hàng và pending chỉ được xóa sau khi nhận được `200 OK` hợp lệ. | **CHỜ TÍCH HỢP B02** (Mô hình kiểm chứng đã chuẩn bị) |
| **T06-AC-03** | Bộ kiểm thử tự động Frontend & Component | 1. Chạy regression suite 34 test baseline.<br>2. Chạy bộ test B01 (Hóa đơn/in nhiệt).<br>3. Chạy bộ test B02 (Freshness/Realtime).<br>4. Chạy bộ test B03 (QR độc lập 9/9 test). | - `npm test`<br>- `test-pos-e2e.mjs`<br>- `test-b02-realtime-pwa.mjs`<br>- `test-qr-module.mjs`<br>- `npm run build` | - `npm run build` **PASS** 0 lỗi TypeScript.<br>- `npm test` **PASS** 100%.<br>- Bộ test độc lập QR **9/9 PASS** 0 fail.<br>- Báo cáo ghi rõ runner, provider và số lượng. | **PHẦN B03 ĐỘC LẬP: PASS**<br>Phần toàn diện chờ B02 tích hợp |
| **T06-AC-04** | Đóng hồ sơ Finding PR24-F01..F04 & Bỏ Claim 100% | 1. Đối chiếu PR24-F01 (Idempotent Key).<br>2. Đối chiếu PR24-F02 (401 mất giỏ).<br>3. Đối chiếu PR24-F03 (In từ snapshot server).<br>4. Đối chiếu PR24-F04 (VietQR động). | - Báo cáo Sprint 3<br>- `brief.md`<br>- `handover.md` | - F01/F02/F03 đã có bằng chứng đóng từ T01 & B01.<br>- F04: Sửa claim trong báo cáo: Bỏ nhận vơ VietQR động 100%, ghi rõ hiện trạng module QR chuẩn hóa và chờ quyết định cấu hình ngân hàng.<br>- Phân định đúng người phụ trách. | **ĐÃ HOÀN THIỆN ĐỘC LẬP** |
| **T06-AC-05** | Đồng bộ Git, Fetch Develop & Kiểm tra CI | 1. Kiểm tra diff so với develop `ecc0c85`.<br>2. Bảo tồn các file tài liệu PR #25.<br>3. Kiểm tra CI workflow run trên GitHub Actions. | - GitHub Actions<br>- `git status`<br>- `git log` | - Không phát sinh xung đột với develop.<br>- CI workflow run của PR #24 trên GitHub Actions báo trạng thái **Success**. | **CHỜ TÍCH HỢP B02** (Chỉ thực hiện sau khi B02 merge/commit) |
| **T06-AC-06** | Cập nhật Mô tả PR #24 | 1. Cập nhật PR description theo đúng template OISMS.<br>2. Nêu rõ phạm vi, hành vi trước/sau, danh sách AC và giới hạn. | - PR #24 trên GitHub | - Không tạo PR trùng lặp.<br>- Không force-push làm mất commit history.<br>- Giữ đầy đủ checklist nghiệm thu. | **CHỜ TÍCH HỢP B02** |
| **T06-AC-07** | Nghiệm thu Dứt điểm POS-STOCK-F01 (Freshness Tồn kho) | 1. Khởi tạo SKU có tồn khả dụng Available = 10.<br>2. Tìm kiếm / barcode hiển thị 10.<br>3. Mua số lượng 2 và Checkout thành công.<br>4. Kiểm tra UI kết quả SKU cùng chi nhánh: phải hiển thị **8** ngay lập tức mà **KHÔNG cần bấm F5**.<br>5. Giả lập Replay cùng đơn hoặc nhận SignalR event đến sau: UI **vẫn giữ 8**, tuyệt đối không bị trừ tiếp thành 6 hoặc nhảy lại 10. | - `ProductSearch.tsx`<br>- `stockService.ts`<br>- `realtimeService.ts`<br>- `GET /api/pos/search`<br>- PostgreSQL DB | - Available chuyển từ 10 xuống 8 mượt mà không reload trang.<br>- Event đến sau hoặc replay không trừ đúp (giữ 8).<br>- Response cũ không đè số mới.<br>- Đối chứng số dư DB thực tế khớp với UI (8). | **CHỜ B02 VERIFIED** (B02 v1.4 đang fix F04/F06) |
