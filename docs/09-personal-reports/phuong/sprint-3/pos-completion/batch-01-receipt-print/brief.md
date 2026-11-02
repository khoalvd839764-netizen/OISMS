# Brief — S3-POS-B01 / v1.0 — Hóa đơn và in nhiệt

Trạng thái: **VERIFIED v1.2 — review R3** trong phạm vi **T02 + phần in của T05**. [Review R3](./reviews/v1.2-review-r3.md): B01-F01/F02/F03 CLOSED; [evidence R3](./evidence/review-r3/README.md). Giữ AC gốc và lịch sử R1/R2. B02 là đợt tiếp theo, chưa triển khai; không đánh dấu toàn T05 hoặc toàn POS hoàn tất.

## Mục tiêu và baseline

- Repo `D:\OISMS`, branch `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`, HEAD `44317f3b5188c276fa3ef25c8faf30323e503079` **cộng working tree T01 v1.3 đã VERIFIED**. Không checkout lại HEAD để lấy baseline vì sẽ mất các sửa chưa commit.
- [Baseline SHA256](./evidence/baseline.json) chụp các đầu vào frontend và DTO/service backend tham chiếu. Bảy đầu vào T01 đã đối chiếu khớp [review R4](../task-01-checkout-safety/reviews/v1.3-review-r4.md). Hash là mốc trước sửa, không yêu cầu giữ hash cũ sau khi thực hiện B01; nếu code khác baseline trước khi bắt đầu thì đọc diff và ghi nhận thay đổi, không reset.
- Kết quả cần đạt: hóa đơn chính thức lấy dữ liệu từ receipt server, tạm tính tách riêng, in lại không checkout lại và khổ in 58/80mm có bằng chứng.
- Finding cần đóng: **PR24-F03** (items còn lấy từ giỏ thay snapshot). T01-F01..F04 đã đóng, giữ regression; chỉ mở lại nếu B01 gây regression có repro.
- Đọc `.agents/skills/codex-antigravity-workflow/SKILL.md`, reference Antigravity, [plan](../PLAN.md), [review PR24](../../PR24_REVIEW_2026-10-27.md), [API POS](../../../../../05-api-contracts/05-pos-endpoints.md), [contract Sprint 3](../../../../../08-sprints/sprint-3/01-api-contracts-sprint-3.md).
- Với `.codegraph/`, dùng CodeGraph trước khi đọc/tìm code. Lượt chuẩn bị brief CodeGraph trả source modal nhưng chưa trả DTO backend; đã đối chiếu trực tiếp các file dưới, không tự index.

## Phạm vi

Được sửa trong `src/frontend/pos-app`: types, API/receipt mapper, PaymentModal, ReceiptPrintModal, state in lại trong App, CSS và test/helper/tài liệu cần trực tiếp cho AC. Giữ contract canonical/alias checkout, owner/session/retry của T01, không dùng lifetime modal làm lifetime key.

Nguồn contract hiện tại để đọc, **không sửa backend**:

- `src/backend/src/OISM.Application/Features/Orders/Dtos/OrderDtos.cs`: `PosCheckoutResponseDto`, `PosReceiptDto`, `PosReceiptItemDto`.
- `src/backend/src/OISM.Application/Features/Orders/Services/OrderService.cs`: luồng receipt mới và replay.
- Response là envelope có `success`, `orderId/orderNumber`, `totalAmount`, `amountGiven/changeDue`, `paymentMethod`, `checkoutAt`, `receipt`. Receipt có identity, branch/customer/cashier, items, totals và createdAt. Đọc tên field/casing/payment values thực tế trước khi map, không chép enum frontend để suy đoán response.
- Receipt không chứa tenantName: tên cửa hàng nếu hiển thị có thể lấy từ phiên của đúng owner như thông tin bổ sung, không dùng dữ liệu local thay items/totals/server snapshot. Nullable địa chỉ, hotline, khách hàng không được thay bằng thông tin bịa.
- Hiện CSS bình luận 58/80mm nhưng rules và `@page` cố định 80mm; cần xử lý hành vi in thật, không chỉ đổi label/comment. Cho phép chọn khổ in và giữ lựa chọn trong phạm vi phù hợp, không thêm hệ thống cài đặt mới.

Ngoài phạm vi: sửa backend/schema, SignalR, refresh tồn/POS-STOCK-F01 (B02), Service Worker/offline/hotkeys/search race ngoài tác động trực tiếp của dialog in (B02), QR/ngân hàng (B03), refactor rộng hoặc thêm dependency không cần thiết. Không triển khai B02/B03 trong prompt này.

## AC — giữ mã gốc

| ID gốc | Điều kiện đạt | Kiểm chứng |
| --- | --- | --- |
| T02-AC-01 | Type response/receipt theo backend, bỏ `receipt?: any` trong luồng này; map items, tên/SKU, quantity, unit price/subtotal, tổng, payment, tiền đưa/thối, số chứng từ và timestamp. | Mapper và handler production chạy với fixtures khớp DTO/service hiện tại; xác minh cả 201 mới và 200 replay. |
| T02-AC-02 | Giỏ giữ tên cũ, server trả tên mới cùng SKU/giá: hóa đơn chính thức lấy tên từ server. Không dùng items/tổng local để vá response thiếu. | Component thực tạo ReceiptData/hiển thị tên mới; fixture cố ý khác tên ở giỏ và response để test phân biệt được nguồn dữ liệu. |
| T02-AC-03 | 201/200 in dữ liệu chuẩn; 2xx receipt hỏng/thiếu không clear pending rồi in phiếu giả. Giữ key/payload và hướng xử lý cần đối soát. | Parse/type/required-data negative cases qua API + modal handler. Khi xác nhận đủ, success mới clear đúng attempt/giỏ; retry không tạo giao dịch mới. |
| T02-AC-04 | Tạm tính có nhãn chưa thanh toán, không POST/clear giỏ/key. Hủy dialog in và in lại receipt đã xác nhận không checkout lại. | Spy request qua actual callbacks, đóng/mở lại và in lại cùng receipt. Bảo toàn pending khi preview. Chỉ cần in lại receipt đã có của phiên hiện tại, không mở module tra cứu lịch sử mới. |
| T02-AC-05 | Nullable branch/customer có quy tắc hiển thị rõ, không bịa hotline/địa chỉ. Không có COGS/giá vốn trong type/render receipt bán lẻ; amount/date/payment mapping theo contract. | Fixtures nullable và payment methods thực tế; inspect rendered receipt, không chỉ regex source. Trình bày/làm tròn không tự thay số tiền chuẩn của server. |
| T05-AC-02 | Khổ 58mm và 80mm hoạt động theo lựa chọn/quy tắc rõ, cả tạm tính và chính thức không bị che/trắng/cắt cột số tiền khi in. | Print preview hoặc PDF tạo bằng browser với kích thước tương ứng, kiểm tra tên dài/nhiều dòng/số tiền lớn. Đối chiếu kích thước trang và đọc được nội dung; không dùng CSS assertion làm bằng chứng duy nhất. |
| T05-AC-05 — phần in | Ghi điều kiện browser/viewport/khổ giấy khi kiểm tra in, đối chiếu kết quả trực quan. Máy in vật lý chưa thử phải ghi rõ. | Lưu ảnh/PDF/bằng chứng cùng môi trường và giới hạn. Các phần desktop/touch/barcode/keyboard/reopen/reconnect khác của AC này vẫn ở B02. |

## Kiểm chứng và bàn giao

- Tại `D:\OISMS\src\frontend\pos-app`: `npm run build`, `npm test` và runner mới nếu bổ sung. Giữ 25 tests baseline; nếu contract fixture cũ không có receipt đủ thì cập nhật fixture đúng contract, không nới assertion để chấp nhận response hỏng.
- Test phải thực thi mapper/component/handler production; không chỉ kiểm tra chuỗi source hoặc tự dựng hàm mô phỏng để chứng minh code sản phẩm. B01 cần bằng chứng browser print preview/PDF để nghiệm thu phần in. Nếu môi trường thiếu, ghi NOT RUN/phần bị chặn và tiếp tục phần độc lập, không tự VERIFIED.
- Kiểm chứng contract bằng DTO/service đã đọc; nếu có backend test đang sẵn sàng thì đối chiếu response thật mà không reset DB nghiệp vụ. Full browser→HTTP→PostgreSQL và CI cuối vẫn ở B03/T06; phân biệt fixture, browser thật và DB thật trong báo cáo.
- Không cài dependency/nâng package hàng loạt. Nếu thực sự cần dependency mới phục vụ test/in, nêu lý do và lockfile; dùng nguồn chính thức. Không chạy lặp backend suite khi không có thay đổi/rủi ro backend.
- Tạo `handover.md` tại thư mục B01, trạng thái **READY_FOR_REVIEW v1.0**, bảng AC gốc → testcase/runner → PASS/FAIL/NOT RUN → evidence; danh sách thay đổi, HEAD + hash working tree đã test, giới hạn và finding PR24-F03. Lưu log/PDF/ảnh phù hợp trong `evidence/v1.0/`.
- Cập nhật trạng thái đợt trong plan/index. Không đánh dấu toàn T05 hoàn tất vì mới làm phần in; không tự VERIFIED. Một handover bao trùm B01, giữ lịch sử task/review T01.
- Không commit/push/merge, force push, reset/clean hoặc đổi nhánh. Không gửi lệnh sang agent/app khác. Khi xong dừng để review đợt 1.
