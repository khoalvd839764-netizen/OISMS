# Bàn giao S3-POS-B03 v1.3-integration

Ngày 28/10/2026, Asia/Saigon. Trạng thái **VERIFIED v1.3-integration — source becc76d đã pass toàn bộ nghiệm thu và CI frontend/backend**. Người dùng yêu cầu Codex trực tiếp hoàn tất B03. Nhánh feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don. Baseline fcf8fb2a4d35954ff5c7909930981b21cdd0fc9e; đã merge develop ecc0c85 bằng4197e28, không conflict. Runtime/test đã kiểm tra được định danh bằng evidence/v1.3-integration/source-hashes.json; hash HEAD trong report live là HEAD trước khi commit các sửa đổi.

Quyết định người dùng: tài khoản thụ hưởng riêng từng chi nhánh, gắn đúng tenant. Service chỉ nhận envelope version1 khớp hai ID; thiếu/sai cấu hình khóa QR. Không có tài khoản mẫu tự động trong production. Hướng dẫn cấu hình và nâng cấp: [qr-configuration.md](./qr-configuration.md).

| Finding | Cách đóng và bằng chứng | Trạng thái cục bộ |
| --- | --- | --- |
| B03-F01/F02/F03 | Giữ module browser-native/TLV boundary/full UUID; module10/10 PASS | CLOSED, preserved |
| B03-F04 P1 | Guard bind backend5001 đến đúng container DB marker5433/role/db/network;9 test bao gồm sai port/DB/container/marker dù cùng role. Runner mới bỏ probe SQL UPDATE ledger; mọi SQL là SELECT | CLOSED |
| B03-F06 P1 | Bỏ fallback account test/environment/global cũ; thiếu/sai config service trả null, UI không sinh QR/khóa nút. Fixture có nhãn không chuyển tiền | CLOSED |
| B03-F08 P1 | Config lookup exact tenant+branch, không nhận global context khác; browser2 chi nhánh và regression tenant/owner | CLOSED |
| B03-F07 P2 | Ý định QR lưu session theo tenant/user/branch+cart fingerprint; UUID ổn định qua close/open/tab, đổi giỏ sinh ý định mới. Browser và regression caller | CLOSED |
| B03-F09 P2 | Pending lưu QR snapshot người nhận/key/amount; config đổi không đổi QR cũ. Browser mất response, reload rồi retry đúng payload. Legacy pending không snapshot chỉ replay, không dựng QR mới | CLOSED |
| B03-F05 P2 | Khôi phục toàn bộ mapping T04/T06 gốc; thay mock runner bằng8 luồng real browser/backend/PG; phân biệt rõ provider. PR24 đã cập nhật theo template; CI source becc76d frontend/backend SUCCESS | CLOSED |

Bằng chứng trước/sau: before-qr-regression.log chạy cùng8 assertion trên baseline fcf8fb2:7 FAIL,1 PASS; pos-unit.log bản sửa:51/51 PASS, gồm34 baseline +8 QR +9 guard fixtures. before-r2-probes.log là counterexample tái hiện, không phải test feature đạt. Không ghi đè evidence v1.2 hoặc review R2.

Lệnh và kết quả tại evidence/v1.3-integration:

- cwd src/frontend/pos-app: npm test **51/51 PASS**, zero skipped; npm run build **PASS** (tsc + Vite).
- cwd root: node --test src/frontend/pos-app/test-b02-realtime-pwa.mjs **24/24 PASS**, provider source + actual TS component/service trong VM, SignalR fixture.
- cwd root: node docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-03-qr-final/qr-module/test-qr-module.mjs **10/10 PASS**, browser-compatible module/TLV parser trong VM.
- cwd root: node src/frontend/pos-app/test-b03-live.mjs **8/8 PASS**, Edge production + backend5001/PostgreSQL5433 thật; detailed browser-postgres-results.json, screenshots và cash-receipt.pdf.

Case mất response: trước khi retry DB đã có1 order/1 payment/2 OUT cho2 SKU; frontend vẫn giữ2 dòng/pending. Sau reload và retry cùng key/payload, vẫn đúng1/1/2, cart/pending mới xóa khi nhận receipt hợp lệ. Cash mua2 từ10 → UI8 không F5 khi Hub offline; replay không trừ lần hai. 401 thật và đổi ShopB không được replay của ShopA; quay lại ShopA phục hồi key cũ. Hub mất kết nối/rejoin thực sự khôi phục biến động bỏ lỡ. Thiếu tồn409 không tạo order và giữ giỏ. GET503 hiển thị lỗi; lookup mới khôi phục số dư thật. ResponseA về sau đổiB bị bỏ qua.

Mapping [AC gốc](./e2e/e2e-matrix.md). PR24-F01/F02 giữ kết quả T01 + live loss/401/owner; PR24-F03 giữ B01 + receipt/reprint từ server; PR24-F04 đóng bằng QR có config scoped/payload/identity và mô tả đúng phạm vi, không gọi QR là tự động đối soát.

Giới hạn/phần tiếp theo: chưa có màn hình/API quản lý bank config; cấu hình qua deployment/localStorage có envelope. Chưa có webhook ngân hàng/auto reconciliation, chưa chuyển tiền thật, chưa kiểm chứng scan bằng ứng dụng ngân hàng hay máy in vật lý. Đây là phạm vi chuyển tiếp đã ghi, không phải kiểm thử PASS. Không thay backend/schema/ledger/WAC, không sửa Admin, không reset/truncate demo. Dữ liệu fixture tích lũy ở DB test qua API; không xóa dữ liệu người dùng. PR24 đã cập nhật theo template; không tạo PR khác hoặc tự merge develop.


Nghiệm thu xuất bản: source commit becc76d9c002384d451c243ade3e9c66c6e23244 đã được push lên PR24, develop đã là ancestor, CI [frontend/backend SUCCESS](https://github.com/khoalvd839764-netizen/OISMS/actions/runs/40682864038). Chi tiết ci-source-head.json. Commit chốt hồ sơ sau đó chỉ sửa tài liệu; CI của HEAD xuất bản cuối được kiểm tra riêng trên PR24 Checks trước khi kết thúc lượt làm việc. Không có finding P0/P1 còn mở trong phạm vi B03. Jira In Review/bình luận Jira/deadline chưa xác minh và không được đánh dấu đã làm.
