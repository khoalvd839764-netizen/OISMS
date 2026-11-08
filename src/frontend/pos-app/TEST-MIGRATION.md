# Test migration — 08/11/2026

POS hiện tại được chuyển từ Admin. Test chỉ nạp implementation quầy tại `pos-app/src`; `test-navigation.mjs` kiểm tra helper mở ứng dụng của Admin, không nạp lại UI POS từ Admin.

| Test cũ | Xử lý |
| --- | --- |
| `test-admin-pos-checkout.mjs` | Chuyển nguyên 6 ca sang `test-checkout.mjs`; harness `pos-runtime.cjs` nạp source POS mới. |
| `test-cart-units.mjs` | Giữ 6 ca của implementation hiện tại, bỏ 6 lần chạy lặp lại trên bản code Admin đã xóa. |
| `test-qr-parity.mjs` | Giữ 9 ca hành vi trong `test-qr.mjs`; bỏ 9 lần chạy trùng và phép so sánh hai bản source vì chỉ còn một POS. |
| `test-b03-qr.mjs` | Giữ đủ 12 ca, nạp PaymentModal/service mới qua `pos-runtime.cjs`. |
| `test-b03-environment.mjs` | Giữ guard môi trường cô lập. |
| `test-pos-e2e.mjs` ca 1–13 | Bỏ assertion cấu trúc/source JSX/state của UI tím; thay bằng build, test hành vi giỏ/checkout và nghiệm thu trình duyệt giao diện hiện tại. |
| `test-pos-e2e.mjs` ca 14–25 | Các quy tắc retry/định danh/đổi phiên giữ qua checkout, QR, auth và pending tests; phép dò state index của App tím bị gỡ cùng App. |
| `test-pos-e2e.mjs` ca 26–34 | Harness của receipt/print UI tím không dùng được với UI hiện tại; không chép lại component cũ chỉ để giữ test. Nghiệm thu invoice/receipt hiện tại được ghi trong báo cáo di chuyển; các khác biệt so với tính năng của app tím phải ghi riêng, không coi như đã pass. |
| B02 preflight/transport/live-stock và helper browser/probe v14–v16 | Các runner cũ nạp App/ProductSearch/cache tồn tím và đường dẫn evidence snapshot, được thay bằng test realtime mới nạp service/màn hiện tại. Test live chỉ chạy riêng trên backend/PostgreSQL cô lập; test reconnect phải ngắt transport thật. |
| B03 browser/live, generate-print-evidence, measure-pos-latency | Runner phụ thuộc modal/table/login tím và đường dẫn evidence cũ được ngừng dùng; không giữ lệnh script dẫn tới source đã xóa. Không sửa evidence lịch sử. |

Mốc nền trước di chuyển: 37/37 (gồm hai lượt chạy cùng helper ở hai app). Sau chuyển: các ca còn áp dụng được giữ một lần, bổ sung test auth/điều hướng riêng. Số lượng khác nhau không được diễn giải thành đã chạy lại nguyên UI tím.
