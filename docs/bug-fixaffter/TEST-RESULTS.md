# Phạm vi và kết quả kiểm tra

Đợt thực hiện: 28–29/10/2026. Cập nhật chốt tài liệu: 29/10/2026. Checklist tổng: [checklist.md](../../checklist.md), mục 0A.

## Kiểm tra sau tích hợp develop / POS mới

- Backend và Admin được so sánh Git tree với `e3813da`: không đổi source/tests. Kết quả 443 backend và 13 frontend bên dưới được tái sử dụng, không nhận là vừa chạy lại.
- POS riêng giữ nguyên source/tests/dependency của develop `8c53b7b` sau PR #24. Các probe POS riêng trong audit cũ không đại diện cho source mới.
- Chạy mới build Admin và POS: **PASS**; POS `npm test`: **51 PASS**; B02 realtime/PWA: **24 PASS**; QR module: **10 PASS**. Log trong `evidence/release-integration/`. Lần B02 đầu sai cwd được giữ riêng, lần chạy đúng cwd đạt.
- CI theo commit mới và browser/backend/PostgreSQL toàn luồng chưa được thay thế bằng kết quả test component này. Tổng hợp: [SPRINT-3-RELEASE.md](SPRINT-3-RELEASE.md).

## Đã thực hiện

| Hạng mục | Kết quả hiện tại | Bằng chứng / giới hạn |
| --- | --- | --- |
| Unit tests backend | **158 PASS, 0 FAIL** | `evidence/backend-test.log`, file TRX trong `evidence/backend/` |
| Integration tests backend toàn bộ | **285 PASS, 0 FAIL, 0 SKIP** | Kết quả cuối đã xuất sau lần kiểm tra trước: `evidence/backend/backend-audit_net8.0_20261029001914.trx`, thời lượng 20 phút 45 giây; đối chiếu `backend-test.log`. Database test đã quan sát: `oisms_test_f24a92cbcd5c`; không phải DB ứng dụng `oisms`. Tổng backend: **443 PASS**. |
| Frontend regression thực thi React/DOM hiện có | **13 PASS, 0 FAIL** | `evidence/frontend-regression.log`; chạy source hiện tại bằng harness trong `scratch/test_regression_v12.cjs` |
| Build trước khi đẩy nhánh 29/10 | **Admin PASS, POS riêng PASS** | Chạy lại `npm run build` ở cả hai frontend; Admin có cảnh báo bundle trên 500 kB, không có lỗi build |
| Phép thử lỗi bổ sung | **Đã tái hiện các kịch bản ghi trong BUG-LIST** | `evidence/audit-probes.cjs`, `probe-results.json`, `probes.log`; actual React/JSDOM và service transpile, HTTP/SignalR điều khiển; không phải E2E PostgreSQL |
| Browser direct link cảnh báo tồn | **FAIL**: vẫn mở P&L | `evidence/reports-alert-link.jpg` và `.json` |
| Giữ source sản phẩm | **261/261 file trong manifest giữ nguyên SHA-256** | So sánh `evidence/source-manifest-start.json` và `source-manifest-end.json`; phạm vi là các file đã có trong manifest đầu, không phải chứng nhận toàn bộ repository |

**Lưu ý:** script `audit-probes.cjs` thoát 0 khi tái hiện được lỗi theo mô tả. Điều đó không có nghĩa sản phẩm PASS. Khi sửa lỗi phải chuyển kịch bản thành kiểm thử hành vi đúng, không giữ assertion đòi lỗi tiếp tục tồn tại.

## Lệnh chính

Chạy từ `D:\OISMS`:

```powershell
node docs/bug-fixaffter/evidence/audit-probes.cjs
node scratch/test_regression_v12.cjs
dotnet test src/backend/OISM.sln --no-restore -m:1 -p:UseSharedCompilation=false --logger 'trx;LogFilePrefix=backend-audit' --results-directory D:/OISMS/docs/bug-fixaffter/evidence/backend --verbosity minimal
```

Phép thử Node dùng `typescript`, `react`, `react-dom` của admin-dashboard và `jsdom` đã có trong `scratch/node_modules`. Không tự cài dependency mới trong đợt audit. PostgreSQL tests sử dụng cấu hình test và guard sẵn có; mật khẩu không được đưa vào báo cáo.

## Những kết quả từ lượt trước được tái sử dụng

- Build Admin và POS riêng đã PASS sau lần gắn Coming soon; hai frontend Docker đã được cập nhật ở lượt trước. Trước khi đẩy nhánh ngày 29/10, cả hai build được chạy lại và tiếp tục PASS (bảng trên); không rebuild Docker thêm trong lượt đẩy nhánh.
- 14 kiểm thử POS/phiếu nhập/lịch sử kho đã PASS ở lượt trước; đây không phải 14 ca vừa chạy lại trong audit này. Các file nguồn liên quan được đưa vào manifest để phát hiện thay đổi trong đợt audit.
- Các nhánh đã sửa: lỗi lịch sử phân loại kiểm kê/chuyển kho, fake success chuyển kho, action Reject không có backend, và dirty10→7 cơ bản. Không tự ghi lại các lỗi đã sửa thành OPEN. Các BF mới ghi rõ nhánh còn hở.

## Giới hạn và phần phải kiểm chứng khi sửa

1. Browser đang dùng dữ liệu localhost có thể được người dùng hoặc tác vụ khác thay đổi. Không dùng số lượng chứng từ tại một thời điểm làm fixture ổn định.
2. Không tạo đơn/thanh toán, xác nhận nhập, dispatch/receive hay confirm count thử trên database đang dùng. Các kịch bản lỗi ghi dữ liệu được điều khiển trong harness hoặc phải dùng database test riêng.
3. Các lỗi refresh/Hub đã tái hiện đường client bằng transport/HTTP điều khiển; chưa cố phát dữ liệu thật chéo tenant qua browser.
4. Chưa benchmark tải lớn/latency/uptime, kiểm tra hạ tầng production, gửi giao dịch ngân hàng thật, hay xác minh kết nối sàn thật.
5. Cần chốt ngày ghi nhận doanh thu theo `CreatedAt` hay `ConfirmedAt`, timezone tenant và phạm vi quyền chi nhánh trước khi đổi nghiệp vụ. Hiện report backend lọc theo `CreatedAt`.
6. Không xem trạng thái handover `CORE_READY_FOR_REVIEW` hoặc các test API happy path là bằng chứng toàn bộ trải nghiệm browser đã hoàn thiện.
