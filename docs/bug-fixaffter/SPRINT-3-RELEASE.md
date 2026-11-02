# Sprint 3 — tích hợp hotfix vào develop, bàn giao main

Ngày: **29/10/2026 · Asia/Saigon**. Phạm vi: `hotfix/sprint-3-core-release` → `develop`; PR đích `develop` → `main`. Đây là bản bàn giao có lỗi tồn đọng, **chưa nghiệm thu toàn bộ sản phẩm để phát hành production**.

## Baseline và cách tích hợp

- Hotfix bắt đầu từ `e3813daf802a8e1916ecfe3511f497a7c9c6084c`: cập nhật kho, POS Admin, Coming soon và báo cáo lỗi.
- Develop đầu vào: `8c53b7b`, đã nhận PR #24 (POS OISMS-44).
- Main tham chiếu: `c742b1d`, đã nhận PR #26 (Admin OISMS-43 trước hotfix).
- Giải quyết bốn xung đột tài liệu Phúc Hữu bằng bản kế hoạch/core mới nhất từ hotfix. Giữ nguyên toàn bộ source, tests và dependency POS riêng từ develop, gồm PaymentModal; không áp lại khóa QR của bản POS cũ.
- Backend và Admin tích hợp giống hệt `e3813da`; POS riêng giống hệt develop `8c53b7b`. Kiểm tra bằng Git tree diff, không chỉ dựa tên file.
- Các file scratch, credential, môi trường local và thay đổi skill ngoài phạm vi không đưa vào bản tích hợp.

## Tổng hợp toàn bộ phạm vi Sprint 3

Các phần dưới đây mô tả phạm vi đã tích lũy trong lịch sử Sprint 3. Một số đã có trên main trước PR này; không coi tất cả là code mới của PR. Phần mới so với main được tách ở mục tiếp theo.

| Phân hệ | Thay đổi Sprint 3 | Giới hạn nghiệm thu |
| --- | --- | --- |
| Backend 1 — OISMS-40 | Entity/EF/API contract; checkout POS nguyên tử và alias; vòng đời đơn Reserved/Confirmed/Completed/Cancelled; WAC snapshot; chuyển kho dispatch/receive; kiểm kê và điều chỉnh tồn; webhook canonical, SignalR sau commit; Hangfire hết hạn reservation; bảo vệ đổi chi nhánh kho có lịch sử. | Theo [SUMMARY BE1](../09-personal-reports/dang-khoa/sprint-3/SUMMARY.md), T02–T06 có review riêng. Không nhận là toàn bộ frontend đã đạt. |
| Backend 2 — OISMS-42 | API đọc/phân trang/lọc đơn và chứng từ; báo cáo doanh thu/COGS/lãi gộp/giá trị tồn/velocity; stock alerts; simulator ba kênh và automated tests; hợp nhất lõi ghi BE1. | [Hồ sơ BE2](../09-personal-reports/nhut/sprint-3/codex-antigravity/README.md) mới xác nhận T01 v1.3; T02–T06 còn cần bằng chứng. Claim 100% trong báo cáo ban đầu không thay kết luận review. |
| Admin — OISMS-43 | Orders Center; lập/duyệt/hủy theo API; chuyển kho hai bước; kiểm kê; báo cáo; thông báo SignalR; lọc theo phiên/chi nhánh; các vòng review B01. | B01 và finding còn mở/chờ review; không coi READY_FOR_REVIEW là VERIFIED. |
| POS riêng — OISMS-44 / PR #24 | Checkout có pending intent/idempotency/recovery theo phiên; receipt server và in 58/80mm; search/barcode và tồn mới; SignalR/reconnect, service worker/PWA; VietQR theo tenant/chi nhánh và snapshot ý định QR. | [B03 handover](../09-personal-reports/phuong/sprint-3/pos-completion/batch-03-qr-final/handover.md) ghi VERIFIED trong phạm vi đã chốt. QR cần cấu hình đúng; xác nhận thu tiền thủ công, chưa có webhook ngân hàng/đối soát tự động. |
| Tài liệu/CI | Checklist, phân công, brief/handover/review và evidence từng người; tài liệu migration/triển khai đã có trong lịch sử; CI backend/PostgreSQL, hai frontend và bổ sung POS regression. | Không cập nhật Jira/Confluence live hoặc triển khai production trong lượt tích hợp. |

## Thay đổi mới so với main tham chiếu

1. **Kho và lịch sử:** DTO/luồng đọc ledger bổ sung liên kết loại chứng từ và định danh; phân biệt kiểm kê, điều chỉnh, chuyển kho và chứng từ gốc. Bổ sung kiểm thử API/provider cho đọc chứng từ, phân quyền, phân trang và ledger liên quan.
2. **Phiếu nhập:** tạo Draft và xác nhận riêng; danh sách đọc từ server; đọc lại tồn/sổ kho sau thao tác. Không tự nhận tạo Draft là đã nhập tồn.
3. **Chuyển kho/kiểm kê Admin:** chỉnh luồng gọi API và action theo trạng thái; bỏ fake success/Reject không có backend; bổ sung xác nhận số đếm và xử lý chi tiết. Những nhánh còn lỗi vẫn ghi trong BUG-LIST.
4. **POS Admin `/pos`:** bố cục catalog dạng lưới và giỏ/thanh toán bên phải, tìm kiếm/danh mục, ảnh trống khi chưa có dữ liệu, ghép catalog với tồn chi nhánh; khóa ngữ cảnh khi checkout đang chạy; thanh toán tiền mặt. Đây là implementation riêng, không mặc định có các bảo vệ của POS riêng.
5. **Coming soon:** khóa các nút chưa nối nghiệp vụ; bỏ số liệu/biểu đồ mẫu dễ bị hiểu là dữ liệu thật trên Tổng quan. Chức năng chưa triển khai vẫn chưa hoàn thành FR.
6. **POS riêng từ develop:** tích hợp đầy đủ các commit PR #24 cùng test/CI, giữ nguyên implementation mới; không thay bằng PaymentModal cũ của nhánh hotfix.
7. **Bàn giao:** checklist v1.2 và báo cáo 20 finding, 8 nhóm chức năng chưa hoàn thiện; bổ sung ghi chú phạm vi sau tích hợp này.

## Lỗi tồn đọng và thứ tự xử lý

Chi tiết từng lỗi/cách tái hiện/điều kiện đóng: [BUG-LIST.md](BUG-LIST.md). Audit gốc chạy trên `e3813da` trước khi đưa POS mới vào; chưa tự đóng finding chỉ vì merge.

| Ưu tiên | ID | Việc còn lại |
| --- | --- | --- |
| P1 | BF-001, BF-002 | HTTP/refresh và SignalR Admin phải thuộc đúng phiên; response/kết nối cũ không cập nhật phiên mới. |
| P1 | BF-003, BF-004, BF-005, BF-013 | Bảo toàn request key và snapshot khi retry kết quả unknown: checkout Admin, lập chuyển kho, tạo kiểm kê, giữ chỗ. Phần BF-003 của POS riêng cần kiểm chứng lại theo source mới. |
| P1 | BF-006 | Xóa ô đếm và xác nhận gán tồn sổ sách không được chốt số đếm cũ. |
| P1 | BF-009, BF-011 | Không ghi đè báo cáo bằng response cũ; thống nhất ngày Việt Nam, timezone và biên ngày. |
| P2 | BF-007, BF-008, BF-010, BF-012 | Không tự lưu ô trống; tránh khóa nút sau đổi chứng từ; xử lý số liệu báo cáo cũ khi API lỗi; tồn theo kho nguồn. |
| P2 | BF-014..018 | Hiện đúng lỗi API chi nhánh/kho, hết phiên checkout Admin, realtime tồn, deep link cảnh báo và bộ lọc kênh Admin. Phần POS riêng của BF-015 cần kiểm chứng lại. |
| P2 | BF-019 | Validate receipt/identity trước nhận checkout thành công ở POS Admin; phần POS riêng cần kiểm chứng lại. |
| P1 / cần kiểm chứng lại | BF-020 | Finding giỏ/chi nhánh của POS riêng được tái hiện trên source cũ. POS mới thay App/PaymentModal và có bảo vệ pending; không trình bày bằng chứng cũ là lỗi đã tái hiện trên source tích hợp. Chưa tự ghi CLOSED/VERIFIED. |
| Theo hồ sơ BE2 | BE2-F03 / BE2-F04 | Quyền xem tài chính; chỉ tính trạng thái doanh thu đúng yêu cầu; filter SKU theo dòng; thời điểm CreatedAt/ConfirmedAt và timezone cần chốt trước khi sửa. |

Các mục chưa hoàn thiện: **CS-01** nhận trả hàng; **CS-02** nhân viên/quyền; **CS-03** lưu ngưỡng cảnh báo; **CS-04** tìm kiếm toàn hệ thống; **CS-05** QR ở POS Admin; **CS-06** in giao hàng/báo cáo Admin; **CS-07** KPI/đơn/biểu đồ Tổng quan thật; **CS-08** AI. Xem [COMING-SOON.md](COMING-SOON.md).

Riêng POS mới: VietQR đã có cấu hình tenant/chi nhánh; không có UI/API quản lý tài khoản ngân hàng, webhook ngân hàng hay tự đối soát. Service worker/PWA không đồng nghĩa cho phép chốt giao dịch tài chính khi offline.

## Kiểm thử và bằng chứng

| Kiểm tra | Kết quả / nguồn |
| --- | --- |
| Backend trước tích hợp | **158 unit + 285 integration = 443 PASS**, 0 FAIL. Tái sử dụng `evidence/backend-test.log` và TRX; source backend/tests tích hợp không đổi so với `e3813da`. Không nhận là vừa chạy lại 443 ca. |
| Admin regression trước tích hợp | **13 PASS**, tái sử dụng `evidence/frontend-regression.log`; toàn bộ source Admin không đổi. |
| Build Admin/POS trên cây tích hợp | **Cả hai PASS**, log tại `evidence/release-integration/admin-build.log` và `pos-build.log`; Admin có cảnh báo bundle >500 kB. |
| POS mới: regression, realtime/PWA, QR module | **51 + 24 + 10 = 85 PASS**, 0 FAIL, 0 SKIP; chạy mới `npm test` từ thư mục POS, B02 và QR module từ root. Log tương ứng trong `evidence/release-integration/`. Lần B02 đầu chạy sai cwd được giữ riêng trong `pos-b02-first-wrong-cwd.log`; chạy lại đúng cwd PASS. Log có lỗi mạng fixture/Hub offline; không dùng kết quả này thay cho Hub transport/E2E thật. |
| Browser/backend/PostgreSQL POS | Tái sử dụng 8 ca theo hồ sơ B03 source `becc76d`; không chạy lại giao dịch trên DB đang sử dụng trong lượt tích hợp. |
| Kiểm tra whitespace | Markdown mới kiểm tra riêng. Diff toàn nhánh còn trailing whitespace từ source đã có và raw log; không nhận là lint toàn repo PASS, không sửa source ngoài phạm vi để che kết quả. |
| CI của develop/PR mới | Phải đọc kết quả theo commit vừa push; chưa nhận là xanh từ kết quả local hoặc CI cũ. |

Không phát hành production, không merge PR develop → main trong lượt này. Review PR cần tập trung các lỗi P1 còn mở, điều kiện vận hành QR, quyền báo cáo và hồi quy luồng kho trước quyết định phát hành.
