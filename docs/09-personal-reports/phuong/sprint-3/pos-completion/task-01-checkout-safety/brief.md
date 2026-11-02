# Task brief — S3-POS-T01 / v1.0

Trạng thái: **VERIFIED v1.3 — review R4** trong phạm vi T01. [Review R4](./reviews/v1.3-review-r4.md): POS-T01-F01/F02/F03/F04 CLOSED; build/25 tests và hai ca recovery đạt. [Evidence R4](./evidence/review-r4/README.md). Giữ lịch sử R1–R3 và AC gốc bên dưới. Browser/HTTP/PostgreSQL E2E vẫn ở gate T06; T02 là task tiếp theo, chưa triển khai.

## Mục tiêu và baseline

- Làm trên `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`, baseline đầu vào `44317f3b5188c276fa3ef25c8faf30323e503079`, PR #24.
- Đọc [plan](../PLAN.md), [review R1](../../PR24_REVIEW_2026-10-27.md), [evidence](../../evidence/pr24-r1/README.md), contract checkout và báo cáo Sprint 2 được dẫn trong plan.
- F01 có trên base/head: mỗi click sinh key mới. F02 là regression mới: checkout 401 phát event khiến App xóa giỏ. Các probes R1 là frontend thực thi với hooks/fetch/server model stub, chưa phải HTTP/PostgreSQL E2E.
- Giữ review/evidence và mọi thay đổi chưa commit đã có khi bắt đầu. Ghi branch/SHA/status thực tế; không ghi đè file đang được người khác sửa.

## Phạm vi

Được sửa `App.tsx`, `PaymentModal.tsx`, `services/api.ts`, types và helper/store/test phục vụ checkout/session. Tập trung quản lý pending attempt ở tầng state/service dùng chung; lifetime không gắn với mỗi lần click/mount modal. Không sửa receipt mapper, SignalR, QR, PWA hoặc backend trong task này.

Pending record cần version và owner identity tenant/user/branch, RequestKey UUID hợp lệ, payload snapshot và trạng thái xử lý. Chốt cách lưu (ưu tiên sessionStorage theo phạm vi tab) trong handover, không lưu token/password vào record. Dùng identity từ phiên đã xác thực; không tự coi decode JWT phía client là cơ chế xác thực thay server.

Phân biệt thất bại xác định và kết quả chưa xác định. Network timeout/lỗi mất response sau commit phải giữ key/body và hướng người dùng đối soát/retry. Không tự đổi SKU/giá/tiền khách đưa của pending attempt đang unknown rồi gửi cùng key; cũng không tạo key mới âm thầm để vượt qua mismatch. Backend hiện có canonical/alias và nhận đúng DTO; không đổi contract để tránh sửa frontend.

## Tiêu chí nghiệm thu

| AC | Hành vi cần đạt | Kiểm chứng |
| --- | --- | --- |
| T01-AC-01 | Một checkout attempt giữ cùng UUID/body qua click lại, lỗi mạng, modal đóng/mở và reload theo contract; header hai loại luôn khớp body. Chỉ giao dịch mới sau khi xử lý kết quả cũ mới nhận key mới. | Test actual state/helper/component, giữ payload snapshot; không chỉ kiểm tra chuỗi source. |
| T01-AC-02 | Response mất sau server commit: frontend giữ pending; retry đúng identity trả kết quả cũ. Không thanh toán/trừ tồn lần hai. | Module/component test đầu tiên; test HTTP/PostgreSQL thực nếu môi trường có. Nếu chưa chạy được, ghi NOT RUN và giữ gate E2E ở T06, không gọi probe stub là bằng chứng DB. |
| T01-AC-03 | 401: bảo toàn giỏ/pending trước đổi UI đăng nhập; cùng tenant/user được khôi phục đúng branch sau xác thực. Đổi tài khoản/tenant không hiển thị/gửi record trước. | Test App event/auth flow thực với session fixtures, gồm same-user và cross-user/cross-tenant. |
| T01-AC-04 | Trong submitting/unknown, thao tác phím tắt/đổi giỏ/đổi branch/logout không làm mất dấu giao dịch chưa rõ kết quả; có quy tắc UI rõ và không tạo hai submit song song. | Component/browser flows, mở lại modal, thử đổi branch và quantity. |
| T01-AC-05 | Phân loại 409 insufficient stock/mismatch, 400 validation/price mismatch, 401/403, 404/405 route fallback, 5xx và network. Đọc ProblemDetails detail/errorCode, không báo tất cả thành thiếu tồn/lỗi mạng. Giữ hoặc reset pending theo kết quả đã xác định. | Mock transport trả đúng body backend; giữ cùng key khi fallback canonical → alias. |
| T01-AC-06 | Thành công đã xác định chỉ clear giỏ/pending một lần; lỗi giữ dữ liệu phù hợp, không hiển thị success hoặc in hóa đơn giả. UUID fallback hợp lệ; không dùng `req-${Date.now()}` làm GUID. | New success/replay/error tests và build/test regression. |

## Bàn giao

Tạo `handover.md` cùng thư mục: thiết kế state transitions/storage owner scope, AC → tên test → runner result, tested SHA, lệnh chạy, giới hạn và finding còn mở. Tests với stub và tests backend/PostgreSQL phải tách rõ. Chạy build POS, test cũ và tests thực thi code mới; không chạy lặp các suite backend không liên quan.

Bản giao việc ban đầu yêu cầu Anti bàn giao `READY_FOR_REVIEW v1.0`; các vòng fix sau tăng phiên bản, kết quả thẩm định mới nhất ở review R4 v1.3 liên kết đầu tài liệu. Implementer không tự đánh dấu VERIFIED, làm T02–T06, commit/push, merge develop hoặc đổi phạm vi QR từ brief này. Việc xuất bản code cần yêu cầu giao việc tương ứng.
