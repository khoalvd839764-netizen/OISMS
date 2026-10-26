# S3-BE2-T03 — Quyền báo cáo, tenant isolation và validation

**Trạng thái:** PLANNED. **Ưu tiên:** P1. **Finding:** BE2-F03, phần RBAC/isolation của BE2-F08. **Phụ thuộc:** T02 VERIFIED.

## Phạm vi

Thu hẹp quyền `GET /api/reports/revenue-profit` thành Owner. Giữ inventory-velocity/stock-alerts cho Owner,Staff theo DOC-API-04. Kiểm chứng HTTP RBAC/tenant cho API đọc và báo cáo BE2, đồng thời tái sử dụng chính sách webhook BE1. Không sửa toàn bộ auth/catalog nếu chưa có lỗi tái hiện.

## Acceptance criteria

| AC | Điều kiện đạt / bằng chứng |
| --- | --- |
| AC-01 | Revenue: Owner 200, Staff 403, Cashier 403, anonymous 401; không có COGS/profit trong error body. Kiểm chứng bằng HTTP với authentication pipeline thực. |
| AC-02 | Velocity và stock-alerts: Owner/Staff được phép; Cashier/anonymous theo contract. Order/transfer/count GET có ma trận quyền rõ và HTTP tests tương ứng. |
| AC-03 | Hai tenant có dữ liệu trong **cùng database**; tenant A không đọc hoặc tổng hợp số liệu B khi truyền branch/warehouse/SKU/order/document ID của B. Test cả list, detail, totals và breakdown. |
| AC-04 | Thiếu/invalid tenant claim không được query toàn database; trả 401/403 theo chính sách thống nhất. Detail cross-tenant không tiết lộ sự tồn tại của bản ghi. |
| AC-05 | Input/date/page/channel/Guid.Empty được xử lý nhất quán với API docs; lỗi nghiệp vụ không lộ stack trace/SQL/message hạ tầng. |
| AC-06 | Bổ sung ma trận auth/catalog còn thiếu của phân công Sprint 3: dẫn test/evidence đã có cho phần đạt, mở finding riêng có repro cho phần fail. Không tự nhận toàn bộ auth/catalog được nghiệm thu từ test happy path BE2. |

Phải có test thất bại với attribute Owner,Staff hiện tại rồi pass sau fix. InMemory với mỗi tenant một database riêng không đủ chứng minh AC-03.

## Bàn giao

Lưu `handover.md`, ma trận endpoint×role×tenant, testcase và kết quả PostgreSQL/HTTP; ghi rõ suite nào chạy và suite nào chưa chạy. Không thay semantics tài chính của T04 trong task này.
