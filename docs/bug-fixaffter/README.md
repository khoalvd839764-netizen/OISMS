# Bug fix after — danh sách lỗi cần sửa

Đợt rà soát: **28–29/10/2026 · Asia/Saigon**. Báo cáo chính được viết bằng **Markdown**; không cần đọc file JavaScript/JSON để hiểu danh sách lỗi.

## Đọc các file này

| File | Nội dung |
| --- | --- |
| [BUG-LIST.md](BUG-LIST.md) | Lỗi đang mở: mức ưu tiên, cách tái hiện, ảnh hưởng, vị trí code và điều kiện sửa xong |
| [COMING-SOON.md](COMING-SOON.md) | Chức năng chưa hoàn thiện, đã/đang để Coming soon; tách khỏi lỗi của chức năng đang dùng |
| [TEST-RESULTS.md](TEST-RESULTS.md) | Kết quả kiểm tra, phạm vi đã quét, giới hạn và việc còn cần kiểm chứng |
| `evidence/` | Bằng chứng kỹ thuật, mã kiểm tra, log và ảnh; chỉ cần khi triển khai hoặc review bản sửa |

## Kết quả hiện có

**20 nhóm lỗi đã được ghi nhận** trong [BUG-LIST.md](BUG-LIST.md). Checklist tổng đã đồng bộ tại [checklist.md](../../checklist.md), mục 0A. Backend đã có kết quả cuối: **158 unit + 285 integration = 443 PASS, 0 FAIL**. Chi tiết tại [TEST-RESULTS.md](TEST-RESULTS.md).

Ưu tiên sửa theo thứ tự:

1. **BF-001, BF-002:** xử lý HTTP/refresh và realtime chưa tách an toàn giữa các phiên đăng nhập.
2. **BF-003..006, BF-013:** giữ nguyên ý định giao dịch khi retry; kiểm kê không chốt số khác với số người dùng xác nhận.
3. **BF-009..011:** báo cáo không bị phản hồi cũ ghi đè, không hiển thị số cũ theo bộ lọc mới, không lệch ngày.
4. Các lỗi khóa thao tác, chọn kho, báo lỗi, đồng bộ và giao diện còn lại.

Không tự đánh dấu các lỗi này CLOSED/VERIFIED. Các lỗi mới tái hiện bằng phản hồi điều khiển được ghi rõ là **kiểm tra có kiểm soát**, không nhận là đã gây lỗi trên database đang sử dụng.

## Baseline và nguyên tắc

- Branch: `feature/OISMS-43-orders-transfers`.
- HEAD: `f276eeed72782f7272f32a62f4bb61ba761f3e89` **cộng working tree chưa commit**. HEAD một mình không đại diện source đang rà soát.
- Phạm vi: Admin Dashboard, POS `/pos` và POS riêng, các API/backend và các yêu cầu liên quan; đối chiếu finding cũ với code hiện tại.
- Lượt này chỉ ghi báo cáo và bằng chứng; không sửa source sản phẩm, không commit/push, không tạo giao dịch thử trên database đang sử dụng.
- PostgreSQL tests dùng database riêng có tên `oisms_test_…` và guard của `PostgresTestEnvironment`.
- Đây là kết quả rà soát các module và kịch bản được liệt kê, không phải cam kết đã phát hiện mọi lỗi có thể tồn tại trong mọi điều kiện vận hành.
