# Rà soát Admin và backend hỗ trợ trước bàn giao Sprint 2

Ngày kiểm tra: 19/10/2026. Người nhận: Đăng Khoa.

## Bản được kiểm tra

- Nhánh đang checkout: `feature/OISMS-29-hoan-thien-web-admin-phuc-huu`.
- Commit: `ab67d9e`.
- Container Admin/POS/backend hiện thuộc project `cnpmworkbase`, không còn lấy frontend từ project `D:\OISMS` cũ.
- Frontend build pass. Bundle local và container cùng file `index-CAVhHHwg.js`, SHA256 `8481a6ae1edfe49c7ed09225c7cc516578dbcf2279bb1ff0aaed308f028779ea`. Đây là bằng chứng đang kiểm tra đúng frontend đã build từ source hiện tại.
- Frontend source của commit này không khác `c0b7875`. Backend source không khác `431c7d6`. Nhánh này **chưa có** sửa F1–F6 tại `043e9b5`, backend F2 tại `f7e92ee` và Docker F7 tại `c28e88a` của PR #10.

Vì vậy các kết quả “đã sửa” khi review PR #10 commit mới không áp dụng cho bản `ab67d9e` vừa build. Những lỗi được tái hiện dưới đây phần lớn là các lỗi đã biết chưa được đưa sang nhánh này, không phải các lỗi mới tự phát sinh sau một bản sửa đã được xác nhận.

## Kết luận bàn giao

**Chưa nên đánh dấu Admin S2 hoàn tất trên bản này. Có 7 nhóm vấn đề đã xác minh: 3 P1 và 4 P2.** Các luồng API backend chính qua bài kiểm tra thực tế, nhưng frontend/retry/Docker còn các vấn đề bên dưới.

Đây là danh sách lỗi đã xác định trong phạm vi rà soát; không phải cam kết không thể còn lỗi khác. Công cụ điều khiển browser hiện mất kết nối nên **chưa chạy full thao tác UI E2E/responsive/accessibility**. Không dùng kết quả API/source diagnostic để thay thế tuyên bố đã click kiểm tra mọi màn hình.

## Danh sách cần xử lý

| Mã | Phụ trách | Mức độ | Vấn đề trên bản ab67d9e | Bằng chứng hiện tại | Hướng xử lý trước bàn giao |
| --- | --- | --- | --- | --- | --- |
| F1 | Frontend | P1 | Đóng/mở form làm mất ID phiếu nhập đang chờ. Nếu confirm đã commit nhưng mất phản hồi, thử lại tạo phiếu mới và nhập trùng. | Callback frontend thật + backend/PostgreSQL thật: dự định nhập 10, sau reopen/retry có 2 phiếu Confirmed và OnHand 20. | Đưa cơ chế lưu/khôi phục/đối soát phiếu từ bản sửa vào; giữ cùng ID khi retry. Hoàn thiện nhãn bỏ theo dõi phiếu, không tuyên bố đã hủy backend. |
| F2 | Frontend + Backend | P1 | Retry giữ chỗ không có định danh ổn định; runtime nhánh này chưa có contract requestKey. | Checkout thật sau mất phản hồi: ý định giữ 1, Reserved = 2. Probe dùng cùng UUID hợp lệ qua body requestKey/header Idempotency-Key vẫn trả 2 reservation khác nhau. | Đưa backend chống lặp vào nhánh bàn giao; frontend phải quản lý và gửi lại cùng UUID/payload ở cả tạo đơn và modal kho, giữ được sau đóng/mở/reload. Test lại end-to-end; chỉ có backend hoặc chỉ có header ở adapter chưa đủ. |
| F3 | Frontend | P2 | Bảng đang lọc kho không reload số dư sau mutation; loadData chỉ cập nhật tổng chi nhánh. | Dữ liệu API thật và callback loadData: bảng giữ Reserved 5 trong khi API đã Reserved 6. | Refetch kho đang lọc sau nhập/giữ chỗ; sequence guard cho đổi kho; xử lý cả lỗi request riêng kho, không giữ số dư kho cũ dưới nhãn kho mới. |
| F4 | Frontend | P2 | Lỗi tải sổ kho bị bỏ qua và bị trình bày như chưa có bút toán. | Fault injection ledger HTTP500 vào transport, chạy callback thật: loadError vẫn null. Trạng thái UI nguồn dùng mảng rỗng cho empty state. | Tách loading/error/empty của ledger, báo lỗi và retry riêng. |
| F5 | Frontend | P2 | Lọc theo tên chứa chuỗi, chọn Kho lấy cả Kho 2. | Ledger API thật + biểu thức lọc frontend: mong 2 dòng Kho, trả 3 dòng có Kho 2. | Dùng warehouseId, so sánh chính xác; nhãn kèm chi nhánh. |
| F6 | Frontend | P2 | fetchAllPages dừng ở 10 trang = tối đa 1.000 dòng. | Transport giả lập tổng 1.001 dòng, actual adapter chỉ gọi 10 trang và trả 1.000. | Server pagination hoặc tải đủ trang theo batch; không trình bày dữ liệu bị cắt là toàn bộ. Có thể hoãn có điều kiện nếu chắc chắn dưới ngưỡng và ghi rõ giới hạn; không coi là đạt đầy đủ. |
| F7 | Docker/Nginx | P1 | Thiếu SPA fallback, mở trực tiếp/F5 các route React bị 404. | HTTP root 200, /login /products /inventory /orders /403 /session-expired đều 404. | Đưa nginx.conf và COPY vào Dockerfile. Route ứng dụng trả index, asset không tồn tại vẫn trả 404. Build lại image từ chính source bàn giao rồi kiểm chứng. |

Không cộng thêm các điểm cải thiện còn lại ở bản `043e9b5` thành lỗi riêng ở bản này: chúng là yêu cầu khi tiếp nhận bản sửa để tránh bàn giao chưa hoàn chỉnh. Không yêu cầu xây chức năng tương lai để đóng lỗi hiện tại.

### Vị trí code chính

- F1: `src/frontend/admin-dashboard/src/pages/InventoryPage.tsx:305` và `:413`.
- F2 frontend: `src/frontend/admin-dashboard/src/services/apiAdapter.ts:556`, `:914`; `InventoryPage.tsx:525`.
- F2 backend: `src/backend/src/OISM.Application/Features/Inventory/Dtos/ReservationDtos.cs`, `ReservationService.cs`, `InventoryController.cs`.
- F3: `src/frontend/admin-dashboard/src/pages/InventoryPage.tsx:95`, `:155`, `:233`.
- F4: `src/frontend/admin-dashboard/src/pages/InventoryPage.tsx:107`.
- F5: `src/frontend/admin-dashboard/src/pages/InventoryPage.tsx:225` và dropdown sử dụng w.name.
- F6: `src/frontend/admin-dashboard/src/services/apiAdapter.ts:52`.
- F7: `src/frontend/admin-dashboard/Dockerfile` và cấu hình Nginx; cùng Dockerfile POS có chung cách dùng nginx mặc định nhưng không cộng thành lỗi Admin thứ hai.

## Phần backend đã kiểm tra đạt trong lượt này

Script API ghi nhận **75 điều kiện kiểm tra đạt, 0 failure**, không phải 75 unit test hay toàn bộ test suite. Các nhóm kiểm tra:

1. Register tenant/Owner, login, sai mật khẩu, me, thiếu token và duplicate registration được xử lý đúng.
2. Refresh token rotate, token refresh cũ bị từ chối, access token mới hoạt động, logout làm refresh token không dùng lại được.
3. Owner tạo/sửa chi nhánh/kho và supplier lookup phục vụ form.
4. Category cha/con, chặn cycle; Brand tạo/sửa; Product nhiều SKU; chống trùng SKU cùng tenant; giá âm bị từ chối, cập nhật giá lưu đúng.
5. Nhập 10 × 100.000 và 20 × 130.000: OnHand 30, WAC 120.000. Nhập thêm 5 × 200.000 ở kho khác cùng chi nhánh: tổng 35, WAC 131.428,571429, khớp phạm vi branch/SKU.
6. Confirm lại cùng receipt ID trả 409 có kiểm soát, không tăng tồn; phiếu sai cặp branch/warehouse, null item và trùng SKU bị chặn, tồn không đổi.
7. Ledger ghi đúng số dòng và tổng lượng từ nhập. Runtime app role bị chặn UPDATE ledger bằng `permission denied`; probe dùng transaction trên tenant QA, không có thay đổi được commit.
8. Tồn 1, gửi 50 request reserve độc lập cùng lúc: **1 HTTP200 + 49 HTTP409**, OnHand 1, Reserved 1, Available 0.
9. A10: SKU inactive bị chặn reserve bằng 409; có test bật lại để tiếp tục các kiểm tra QA.
10. Tra cứu POS theo mã SKU và barcode trả đúng variant.
11. RBAC backend: Staff được đọc ledger nhưng không tạo branch; Cashier đọc tồn được nhưng bị cấm ledger/tạo phiếu nhập.
12. Tenant B không đọc/sửa product A, không thấy balances A, không dùng tham chiếu A để tạo phiếu; tenant B được tạo cùng SKU code theo contract hiện tại.
13. Readiness HTTP200, kết nối bằng oisms_app và schema 29 bảng đạt.

Các vai trò Staff/Cashier được provision **chỉ trong tenant QA mới**, sao chép BCrypt hash của Owner QA đó để kiểm thử login/RBAC. Không sửa user/role của tenant cũ.

Frontend guard cũng được thực thi từ source: Cashier bị hướng /403 khi vào Admin, Staff bị chặn route Owner-only, Owner được phép. Kiểm tra refresh đồng thời từ source cho 2 request 401 chỉ có 1 refresh. Đây là source diagnostic, không phải click browser E2E.

**Không phát hiện thêm lỗi backend độc lập qua bài kiểm tra này ngoài khoảng trống chống lặp thao tác F2.** Backend đã có cơ chế retry theo OrderId/OrderItemId, nhưng không thể dùng nó khi client gửi request không có định danh ổn định. Không nhầm giữa khả năng chống vượt tồn (đã đạt 50 request) và chống lặp cùng một ý định sau timeout (F2 chưa đạt).

## Cách hiểu fault injection và giới hạn

- F1/F2: backend xử lý và commit thật, script chủ động bỏ phản hồi ở client để tạo trường hợp kết quả không rõ; không khẳng định mạng/server tự nhiên bị timeout trong lượt test.
- F3: callback nguồn chạy bằng diagnostic VM với API thật; warehouse state được so sánh với số dư thật, chưa quan sát lại trực tiếp bảng trên browser lần này.
- F4: lỗi ledger 500 được tiêm ở transport, không chủ động phá dịch vụ hoặc sửa DB để gây 500 thật.
- F6: dùng mock 1.001 dòng để kiểm tra biên, không tạo hàng nghìn dòng rác trong database đang chạy.
- Chưa thử fresh database/volume bootstrap, migration sạch, mid-confirm rollback fault injection, hoặc chạy lại toàn bộ backend unit/integration suite trong lượt này. Source backend không khác bản `431c7d6` đã từng chạy suite trước đó; kết quả cũ không thay cho xác minh môi trường sạch mới.
- Chưa kiểm tra đầy đủ giao diện mobile, thao tác bàn phím, layout và từng nút bằng browser do kết nối công cụ browser bị đóng. Cần vòng E2E cuối sau khi hợp nhất bản sửa.

## Phạm vi loại trừ

Không tính checkout/thanh toán hoàn chỉnh, vòng đời xác nhận/hoàn tất/hủy đơn, chuyển kho, kiểm kê, báo cáo nâng cao hoặc trả hàng là thiếu backend S2. Các preview/demo trên UI không được trình bày là nghiệp vụ đã lưu và xử lý thật. Không tự đưa chức năng trả hàng vào sprint tiếp theo khi đề chưa giao.

Trang POS độc lập của nhánh này chưa nằm trong phạm vi review giao diện Admin; API POS được kiểm thử như backend hỗ trợ S2. Không dùng việc API POS đạt để kết luận toàn bộ giao diện POS đạt.

## Trình tự đóng việc trước bàn giao

1. Đối chiếu/đưa các sửa đã có trong PR #10 (`043e9b5`, `f7e92ee`, `c28e88a`) sang đúng nhánh bàn giao. Nhánh hiện tại và PR có lịch sử khác nhau; không tự merge/cherry-pick trong lượt review này.
2. Hoàn thiện F2 ở tất cả nơi gọi frontend, các nhánh lỗi kho F3, và nhãn bỏ theo dõi phiếu F1 theo báo cáo recheck trước. Không chỉ dựa vào README “đã sửa”.
3. Build đúng image từ source hợp nhất và lặp lại kiểm thử; chốt regression mất phản hồi, đóng/mở/reload, key UUID, payload khác, tenant isolation, số dư và route Nginx.
4. Chạy E2E thật trên browser cùng tester và dữ liệu QA có nhãn. Xác minh Owner/Staff/Cashier ở cả UI và API.
5. Cập nhật README/report bằng đúng commit, bài test đã chạy/chưa chạy và giới hạn; không ghi “done 100%” khi còn P1/chưa xác minh.

## Dữ liệu QA và bằng chứng

Chỉ tạo và thay đổi các tenant có nhãn QA:

- `QA-S2-20261019070840-a` và `QA-S2-20261019070840-b`: dùng cho 75 API checks và diagnostic state/contract. A có 4 phiếu nhập Confirmed, tổng tồn 36 (35 main + 1 race); cuối diagnostic main Reserved 6, race Reserved 1. B có product trùng mã hợp lệ, không có stock.
- `QA-PR10-20261019070930`: F1/F2/F5 bằng source callback và API thật, có 3 phiếu Confirmed, tổng tồn 25, Reserved 2.

Một lần diagnostic frontend ban đầu chưa biên dịch JSX đúng; đã sửa bộ đọc JSX của script và chạy lại thành công. Các thao tác reserve của lần chạy script trước vẫn nằm trong tenant QA, nên main Reserved 6 ở cuối là tổng của hai lượt probe có chủ đích; không phải lỗi overselling của API. Không đụng dữ liệu tenant nghiệp vụ cũ, không reset volume, không đổi mật khẩu, không sửa code ứng dụng.

Các file bằng chứng:

- `tmp/pr10-review/handover-ab67d9e-results.json`: 75 điều kiện API.
- `tmp/pr10-review/handover-ab67d9e-f1-f2-results.json`: F1/F2/F5 thật và A10/WAC/reconfirm.
- `tmp/pr10-review/handover-frontend-ab67d9e-results.json`: UUID F2, stale warehouse, lỗi ledger tiêm ở client, guard, HTTP routes.
- `tmp/pr10-review/results.json`: mock boundary/refresh diagnostics thực thi lại trên source hiện tại. Metadata commit trong script gốc là c0b7875; frontend bytes đã được Git diff xác nhận trùng ab67d9e, nên không xem đây là kết quả test source khác.

Những kết quả này là bằng chứng rà soát tại commit nêu trên; cần cập nhật lại sau mỗi thay đổi source/image.
