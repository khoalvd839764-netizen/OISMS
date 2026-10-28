# Prompt thực hiện sản phẩm chạy luồng cơ bản — v2.0

Bạn là Antigravity, hoàn thiện sản phẩm Admin theo chỉ đạo mới: ưu tiên các luồng cơ bản chạy thật; đưa lỗi phụ và phần test/evidence chuyên sâu vào backlog sau. Áp dụng workflow skill và AGENTS; chỉ đạo v2.0 của người dùng ưu tiên cách tổ chức task cũ.

Đọc:
- `docs/09-personal-reports/phuc-huu/sprint-3/admin-completion/PLAN.md`: phần mốc 1, C0–C5 và CORE-01..08.
- `docs/09-personal-reports/phuc-huu/sprint-3/admin-completion/BACKLOG.md`.
- Handoff/review B01 v1.4 chỉ để tái sử dụng phần đã đạt và nhận diện giới hạn; không đọc lại toàn bộ lịch sử nếu không cần.

Baseline `feature/OISMS-43-orders-transfers`, `f276eee` + working tree hiện có. Pin lại và bảo toàn mọi thay đổi skill/tài liệu/test trước sửa. CodeGraph trước tìm/đọc code; không index lại repo hoặc audit toàn bộ. Được triển khai phần Admin luồng đơn hàng, chuyển kho, kiểm kê, báo cáo/realtime cần thiết trong một đợt; **không đợi B01 VERIFIED mới làm B02/B03**.

Làm rõ ưu tiên: trước hết hoàn thiện màn hình/form/service/API wiring của các module cơ bản, rồi nối/thử luồng, sau đó gom fix lỗi quan trọng trong một đợt ổn định. Khi đang xây nền tảng, ghi bug theo ID và tiếp tục phần độc lập; chỉ sửa tối thiểu lỗi chặn nối/thử luồng. Không biến mỗi finding thành bước chờ review. Những yêu cầu “sửa trước PASS/core-ready” bên dưới áp dụng lúc ổn định/kết luận, không khóa việc hoàn thiện các module còn thiếu. Giữ trạng thái IN_PROGRESS/PARTIAL_CORE tới khi đã xử lý lỗi quan trọng; không nhận đã sẵn sàng chỉ vì có đủ màn hình.
Thứ tự:
1. Chạy Admin + backend đúng source + PostgreSQL test. Kiểm tra login/Orders/GET transfers/counts, xử lý runtime cũ trả 405. Ghi URL/port/head/provider. Dùng runtime dev thích hợp, không production; bảo toàn dữ liệu/dịch vụ người dùng.
2. Login/role/session và dữ liệu nền sẵn có. Không làm lại catalog; dùng seed/API hợp lệ tạo fixture.
3. Orders: tạo/reserve/confirm/process/complete; đơn khác cancel; lỗi thiếu tồn, retry/double click không trùng. Reload thấy state server đúng và đối chứng tồn.
4. Transfers: Draft→dispatch→receive, đọc lại list/detail bằng phiên mới, source/target tồn đúng từng bước. Không fake success khi API lỗi, không thêm Reject trái contract.
5. Counts: nhập→Lưu→Chốt, có thể chọn cách chặn dirty bắt Lưu rõ ràng. Ca đã lưu 10→sửa7 phải chốt7 hoặc chặn; zero/drift/record fail xử lý đúng, không có adjustment sai.
6. Dashboard/report thật theo role; một giao dịch POS cùng backend/test DB phản ánh sang Admin qua tải lại. Realtime chưa an toàn thì tắt sạch theo phiên và dùng refetch thủ công, không giữ connection rò tenant chỉ để có chuông.

Không dành lượt này để đóng hết E01/E02, viết lại test framework hoặc chỉnh layout in. Giữ print ở trạng thái chưa nghiệm thu, backlog sau. Phần E02 browser đọc chứng từ kho vẫn làm ngay vì chính là core flow C3/C4. Nếu smoke tái hiện lỗi sai dữ liệu/quyền, tạo/trừ tồn trùng hoặc giả thành công thì phải sửa trong scope trước PASS luồng đó. Lỗi presentation, test coverage sâu, benchmark hoặc reconnect nâng cao không chặn phần độc lập.

Tái sử dụng 13 component tests + 2 sensitivity và 3 HTTP PostgreSQL đã PASS nếu source/dependency/provider/fixture còn hợp lệ. Manual smoke lặp lại được là đủ cho thao tác UI cơ bản; đường transaction/tenant/idempotency bị sửa cần test provider thực phù hợp. Không chạy lại toàn suite khi không có ảnh hưởng. Không bỏ gate dữ liệu hoặc nới assertion để xanh.

Mỗi 45–60 phút cần một core flow chạy thêm hoặc blocker rõ; gặp lỗi hai lần không tiến triển thì đổi cách kiểm chứng, tiếp tục phần độc lập. Làm toàn bộ checkpoint trong một đợt, tự sửa lỗi cơ bản trước bàn giao; không chờ Codex duyệt từng bước.

Bàn giao một `core-product/handover.md` tại thư mục admin-completion khi có kết quả:
- head/diff, URL/cách chạy, fixture/database test và giới hạn;
- CORE-01..08 PASS/FAIL/NOT RUN, mã chứng từ + lệnh/quan sát đủ tái chạy;
- blocker thật và backlog trì hoãn giữ ID/điều kiện đóng.

Trạng thái `CORE_READY_FOR_REVIEW` nếu đủ checklist, hoặc `PARTIAL_CORE` nếu còn luồng thiếu. Codex xác nhận `CORE_READY_WITH_KNOWN_ISSUES` sau review; chưa tự gọi VERIFIED đầy đủ hoặc CLOSED các finding cũ. Không commit/push/merge/đổi PR base, sửa POS/engine tồn/ledger/WAC/schema hoặc thêm nghiệp vụ ngoài phạm vi. Bug backend chặn core phải ghi repro/contract và phương án sửa giới hạn trước phần vượt phạm vi.
