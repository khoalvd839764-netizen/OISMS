# S3-BE2-T02 — Hoàn thiện API đọc đơn và chứng từ kho

**Trạng thái:** PLANNED. **Ưu tiên:** P1. **Finding:** BE2-F09. **Phụ thuộc:** T01 VERIFIED.

## Phạm vi

Hoàn thiện các điểm mở rộng `GetOrdersAsync` / `GetOrderByIdAsync` trên contract BE1. Bổ sung list/detail cho stock transfer và stock count theo model đã được BE1 nghiệm thu. Goods receipts, inventory balances và ledger đã có GET: đối chiếu để tái sử dụng, không viết lại.

Chốt bảng route, query parameters, DTO, role và error response trước khi code; ghi vào handover/API docs. Giữ tên filter chuẩn BE1 (`PageNumber`, `SearchKeyword`...) hoặc có adapter tương thích được kiểm thử nếu consumer đang dùng `PageIndex` / `Search`. Không đổi mutation contracts, không thay engine và không tạo schema mới chỉ để query.

## Acceptance criteria

| AC | Điều kiện đạt / bằng chứng |
| --- | --- |
| AC-01 | GET order list/detail không còn NotImplementedException; trả items, snapshot lịch sử và tổng số đúng contract trên PostgreSQL. SKU đổi tên sau bán không làm đổi tên snapshot trên chứng từ. |
| AC-02 | Filter status/channel/branch/time/search kết hợp được; pagination ổn định khi CreatedAt trùng nhau, không trùng/mất item do thiếu tie-breaker. Có giới hạn page size và kiểm tra overflow/page bất hợp lệ. |
| AC-03 | Có list/detail transfer và count kèm items, status và dữ liệu đọc phù hợp. Chốt filter riêng theo model, gồm branch/warehouse/status/time khi áp dụng; không thêm filter không có semantics. |
| AC-04 | Mọi query bắt buộc tenant scope cả root và child; id không tồn tại hoặc thuộc tenant khác trả 404 theo contract. Không dùng IgnoreQueryFilters để lấy dữ liệu nhiều tenant. |
| AC-05 | GET không sửa status, tồn, reservation hay ledger. Integration test chụp trước/sau xác nhận không có side effect. |
| AC-06 | Input rỗng/Guid.Empty, range ngày ngược, pagination cực lớn có response được định nghĩa; không trả lỗi 500 hoặc SQL/provider exception. |

Mỗi API mới cần integration test dữ liệu thật và HTTP test route/shape. T03 sẽ bổ sung ma trận quyền/isolation xuyên toàn bộ API BE2; không trì hoãn các test cần cho thay đổi T02.

## Bàn giao

`handover.md` gồm bảng endpoint/consumer, AC→testcase→runner result, diff và finding còn mở. Không tự đánh dấu các task khác VERIFIED; không sửa COGS/report aggregation trong task này.
