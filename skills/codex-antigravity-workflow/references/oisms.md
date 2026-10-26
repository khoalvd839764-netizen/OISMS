# Ngữ cảnh OISMS

Các đường dẫn dưới đây tính từ root repo. Xác minh phiên bản hiện tại khi bắt đầu task, không coi thông tin trong skill là đặc tả thay thế.

| Nguồn | Cách dùng |
| --- | --- |
| `docs/04-architecture-and-design/` | Chuẩn kiến trúc, dữ liệu và invariant liên quan |
| `docs/05-api-contracts/` | API/DTO và quyền truy cập đã thống nhất |
| `docs/06-testing-and-qa/` | Phương pháp, môi trường và phạm vi kiểm chứng |
| `docs/08-sprints/sprint-2/` | Baseline và phần chuyển tiếp từ Sprint 2 |
| `docs/08-sprints/sprint-3/README.md` | Phân công Sprint 3; kiểm tra kế hoạch hiện hành khi sang sprint khác |
| `docs/09-personal-reports/dang-khoa/` | Báo cáo của Đăng Khoa; đọc report sprint trước và ghi kết quả vào sprint đang làm |
| `docs/10-ai-skills/README.md` | Chọn skill kỹ thuật phù hợp, không tải toàn bộ danh mục |

Với task Sprint 3 của Đăng Khoa: dùng report Sprint 2 làm bằng chứng đầu vào, đối chiếu code và ghi kết quả mới vào `docs/09-personal-reports/dang-khoa/sprint-3/README.md`. Chỉ sửa report Sprint 2 khi thực sự đính chính lịch sử và ghi rõ nội dung đính chính. Không giả định mọi task đều thuộc Đăng Khoa hoặc Sprint 3.

Khi task liên quan, kiểm tra các invariant: ranh giới Clean Architecture, tenant isolation cả đọc/ghi, phân quyền trường dữ liệu trả ra, ledger append-only, WAC, tính nguyên tử, idempotency và thứ tự lock thống nhất với luồng cũ. Không biến danh sách này thành yêu cầu triển khai tất cả cho một task nhỏ.

Ví dụ ranh giới: task “thiết kế contract Order” có thể cần mô tả retry/idempotency và kiểm tra DTO; không tự thêm state machine runtime, migration hoặc webhook processor nếu brief chưa giao. Báo cáo test InMemory phải ghi InMemory; không gọi đó là minh chứng transaction PostgreSQL.
