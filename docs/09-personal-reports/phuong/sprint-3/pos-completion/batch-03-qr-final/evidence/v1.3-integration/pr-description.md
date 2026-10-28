## Mô tả công việc

Hoàn thiện Web POS: checkout nguyên tử qua API hiện có, retry an toàn khi mất phản hồi/hết phiên, hóa đơn từ server, cập nhật tồn không F5, PWA/thao tác quầy và QR theo đúng cửa hàng/chi nhánh. B03 sửa các lỗi tài khoản mẫu tự xuất hiện, cấu hình QR dùng chung và identity/người nhận thay đổi khi mở lại hoặc retry. Thu ngân kiểm tra tiền vào trước khi xác nhận; chưa có đối soát ngân hàng tự động.

### Thông tin Task Jira

- **Mã Task:** OISMS-44
- **Đường dẫn Jira:** https://khoalevodang301007.atlassian.net/browse/OISMS-44
- **Hạn chót:** Chưa đối chiếu Jira trong lượt này.
- **Người phụ trách:** Phương Lê — Frontend POS; Codex hỗ trợ sửa/nghiệm thu B03 theo yêu cầu.

### Phân loại thay đổi

- [x] Tính năng mới
- [x] Sửa lỗi chức năng
- [x] Thêm/sửa kiểm thử
- [x] Tài liệu
- [x] CI: thêm POS regression suite sau build, không gọi fixture tests là full-stack

## Chi tiết các hạng mục đã hoàn thành

1. T01: pending theo tenant/user/branch; cùng key/payload khi kết quả chưa rõ; giữ giỏ khi401 và phục hồi đúng owner.
2. B01: validate/map receipt từ server, tạm tính tách checkout, in lại không tạo đơn; giữ hỗ trợ58/80mm.
3. B02: làm mới stock sau checkout, chống response cũ ghi đè, Hub reconnect/rejoin/refetch và PWA/offline an toàn.
4. B03: bắt buộc config envelope tenant+branch, không fallback tài khoản test; QR intent giữ UUID qua close/open/tab và pending lưu snapshot người nhận qua reload/config change/retry. Legacy pending không dựng QR sang người nhận mới.
5. Runner live mới dùng build production trong Edge, API/backend/PostgreSQL thật. Guard xác minh target backend/container/DB/network/marker trước ghi; bỏ SQL UPDATE ledger và mọi cờ bypass. Merge develop ecc0c85 giữ cập nhật phân công PR25, không sửa backend/schema/Admin trong đợt B03.

## Danh sách API bàn giao cho Frontend

Không thêm endpoint/schema. POS dùng `POST /api/pos/checkout` (alias tương thích `POST /api/orders/checkout`), `GET /api/pos/search`, `GET /api/pos/barcode/{barcode}`, auth/branches/balance và Hub notifications hiện có. QR config là deployment/localStorage envelope, chưa có UI/API quản lý ngân hàng.

## Bằng chứng kiểm thử và hình ảnh

- `npm test` tại `src/frontend/pos-app`: **51/51 PASS**, gồm34 baseline +8 QR caller/service VM +9 guard fixtures; zero skipped.
- `npm run build`: **PASS**, TypeScript + Vite.
- Root B02 suite: **24/24 PASS**; module QR: **10/10 PASS**. Đây là source/VM/module tests, không đại diện PostgreSQL.
- Live B03: **8/8 PASS**, Edge production + backend5001 + PostgreSQL5433 cách ly: cash/QR/receipt/reprint, stock10→8 không F5 khi Hub offline, replay, QR close/open/branch config, mất response sau commit/reload/UI retry,401/đổi tài khoản, reconnect Hub, thiếu tồn409, stock GET503/recovery, response branch cũ về trễ.
- DB ca mất phản hồi/retry: **1 order,1 payment,2 OUT cho2 SKU**; key/payload/recipient không đổi. Không dùng SQL ghi dữ liệu hoặc chuyển tiền thật.
- [Bàn giao và mapping AC gốc](docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-03-qr-final/handover.md)
- [JSON/log/ảnh/PDF/source hashes](docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-03-qr-final/evidence/v1.3-integration/)
- [Cấu hình QR và migration config cũ](docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-03-qr-final/qr-configuration.md)

Giới hạn: chỉ thay ảnh VietQR external trong test bằng SVG fixture; business API dùng backend thật. Chưa thử chuyển tiền/quét bằng app ngân hàng, máy in vật lý hoặc webhook đối soát tự động. CI của từng HEAD xem Checks; kết quả cục bộ không thay thế CI.

## Bảng kiểm tra bắt buộc trước khi nộp

### Chất lượng mã nguồn

- [x] Build cục bộ thành công
- [x] Các suite nghiệm thu nêu trên PASS, provider/count rõ
- [x] Không commit node_modules/bin/obj/.env/token/browser trace
- [x] Fetch và merge develop; giữ lịch sử, không force-push hoặc tự merge PR

### Đồng bộ Jira và Git

- [x] Nhánh `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`
- [x] Commit sửa B03 gắn `[OISMS-44]` và mô tả tiếng Việt
- [ ] Trạng thái Jira In Review — chưa xác minh/thao tác Jira
- [ ] Dán PR vào bình luận Jira — chưa thực hiện trong lượt này

### Trách nhiệm và hạn chót

- [x] Mô tả phạm vi, contract, test, evidence và giới hạn
- [ ] Đối chiếu deadline trên Jira — chưa xác minh

### Lý do trễ hạn (nếu có)

Chưa đối chiếu deadline; không tự xác nhận đã đúng/trễ hạn.
