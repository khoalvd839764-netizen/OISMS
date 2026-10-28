# Backlog hoàn thiện sau core — v2.0

Theo chỉ đạo người dùng ngày 28/10/2026: ưu tiên [luồng cơ bản](./PLAN.md); backlog này giữ lỗi/AC đầy đủ để xử lý tiếp, không tự đóng finding. Chưa có lịch hoàn tất cố định. Phúc Hữu/Antigravity phụ trách Admin, Codex review; phụ thuộc backend nêu rõ theo từng item.

## Quy tắc ưu tiên

- **P0-CORE:** lỗi trực tiếp chặn flow cơ bản, sai dữ liệu/tồn/tenant/quyền hoặc fake success. Không trì hoãn để gắn core-ready.
- **P1-NEXT:** chức năng cần hoàn thiện ngay sau core, ví dụ phiếu in và realtime an toàn.
- **P2-LATER:** mở rộng độ phủ, edge case không có bug sản phẩm tái hiện, polish/performance. Nếu có repro gây sai dữ liệu/quyền thì nâng P0-CORE.
- Trạng thái DEFERRED nghĩa là còn mở và được hoãn theo phạm vi mốc 1; không phải CLOSED hoặc VERIFIED. Không hạ severity lịch sử P1/P2 chỉ vì đổi thời điểm làm.
- Dùng ID gốc; tách phần còn lại trong cùng hàng để không sửa lại phần đã đạt. Bug mới ghi scenario/ảnh hưởng/điều kiện đóng trong bảng này, không tạo bộ report riêng.

| ID / nguồn | Việc làm ngay cho core | Phần hoãn / ưu tiên sau core | Điều kiện đóng đầy đủ |
| --- | --- | --- | --- |
| B01-E01; B01-F02/F04 phần evidence | Giữ guard hiện có; smoke create/retry/session không gửi trùng/chéo owner; xử lý bug thật nếu có | **DEFERRED — P2-LATER:** first-submit deferred POST→reload mutation-sensitive test và manual-retry race regression còn thiếu | Ca chạy callback thật PASS, mutant phù hợp FAIL trong bản cô lập; đóng bằng evidence, giữ 13+2 ca đã đạt |
| B01-E02 — print; A02-AC-04 | Xem chi tiết đơn/server snapshot để tra cứu; công khai print chưa nghiệm thu | **DEFERRED — P1-NEXT:** bỏ Orders filter/list khỏi print, sửa 80mm/A4, PDF Owner/Staff và masking | PDF chỉ đúng phiếu, number/items/tổng đúng, bố cục rõ ở khổ đã chốt, không hở COGS; tự xem mọi trang |
| B01-E02 — browser chứng từ kho; PR26-F04 | **P0-CORE:** backend đúng source và browser mới đọc list/detail transfers/counts trong C0/C3/C4 | Không hoãn dependency 405 dưới nhãn evidence; filter/page edge case phụ có thể để sau nếu không làm mất dữ liệu cần thao tác | Browser/API thật đọc lại phiếu và mapping/filter/paging đúng AC; giữ HTTP PostgreSQL 3/3 còn hợp lệ |
| PR26-F01/F03/F05/F06 | **P0-CORE nếu còn tái hiện:** fake success, count dirty10→7, action sai, retry mất key/trùng chứng từ; xác minh source hiện tại trong core | Evidence lỗi rộng, chaos/network/reload matrix đầy đủ: **P2-LATER** sau khi core đúng và guard dữ liệu đã có | Đủ điều kiện đóng trong review R1/AC A03/A04; không gọi CLOSED cả finding từ một happy path |
| PR26-F02 — lifecycle Hub | **P0-CORE nếu còn nguy cơ rò phiên:** sửa lifecycle tối thiểu hoặc tắt Hub/event cũ; core dùng refresh | **DEFERRED — P1-NEXT:** hoàn thiện owner/token/deferred start/reconnect an toàn để bật lại | Auth A→logout→B không nhận event/status A; đúng tenant/role, không listener trùng |
| PR26-F07; A06 realtime | Dùng refresh sau mutation và nút tải lại, thông báo trạng thái realtime rõ | **DEFERRED — P1-NEXT:** branch groups, events, reconnect/refetch và chuông | Transport thật + browser thấy dữ liệu cập nhật đúng, bỏ event sai context; giữ permission |
| A05 reports nâng cao | **P0-CORE nếu số/role sai:** đọc số thật, filter cơ bản/fixture đối chứng; không giả thành công | **DEFERRED — P2-LATER:** toàn bộ timezone boundary/filter matrix, print report/polish | Đạt đầy đủ A05 bằng dữ liệu biết trước/provider thích hợp, không mở rộng quyền từ suy đoán |
| A06-AC-04; POS integration mở rộng | **P0-CORE:** CORE-08 cùng backend/test DB, refetch Admin thấy giao dịch POS | **P1-NEXT:** đầy đủ race/lost-response/reconnect E2E head tích hợp | Đúng idempotency/ledger/tenant; tất cả AC liên quan có evidence trên head cuối |
| PR26-F08; A06-AC-05 | Chỉ ghi nhận base/head/diff hiện tại khi cần, không thay PR từ plan | **P1-DELIVERY:** cập nhật PR develop/title/body/CI khi được giao delivery | Diff đúng phạm vi, CI head cuối và reviewer duyệt theo quy tắc repo; core-ready không tự cho phép merge |
| UI responsive, âm thanh, chunk warning, benchmark | Chỉ sửa nếu thực sự không thao tác được hoặc khiến người dùng hiểu sai dữ liệu | **DEFERRED — P2-LATER** | Tiêu chí cụ thể theo repro; không refactor/nâng dependency hàng loạt |

## Nguồn và lịch sử

- [Review v1.4](./batch-01-contract-orders/reviews/v1.4-review.md): source/evidence đã kiểm tra, E01/E02 còn mở trước thay đổi ưu tiên.
- [Review R1](./reviews/pr26-review-r1.md): PR26-F01..F08; trạng thái lịch sử, cần đối chiếu source hiện hành.
- [AC đầy đủ](./PLAN.md#ac-đầy-đủ-giữ-để-hoàn-thiện-sau-mốc-1): nghiệm thu đầy đủ sau core.
- Prompt fix v1.5 trong lịch sử không còn là thứ tự ưu tiên mặc định; dùng [prompt v2.0](./PROMPT-START-B01.md) cho đợt core mới.

Chỉ đưa việc từ backlog vào triển khai sau khi có kết quả core hoặc có repro nâng P0-CORE. Gom các sửa liên quan, review phần diff chịu tác động; không mở lại toàn repository.
