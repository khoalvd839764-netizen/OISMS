# Contract Mapping — S3-ADMIN-B01

Bảng đối chiếu Route, DTO, Filter, Pagination, Status, Role và ErrorCode của phân hệ Đơn hàng, Chuyển kho, Kiểm kê, Báo cáo và Realtime.

## 1. Phân hệ Chứng từ Đọc (Stock Transfers & Stock Counts)

| Tuyến đường (Route) | Phương thức | DTO Đầu vào / Query | DTO Phản hồi | Phân quyền (Role) | Mã lỗi chính (ErrorCode) |
| --- | --- | --- | --- | --- | --- |
| `/api/stock-transfers` | `GET` | `StockTransferFilterRequestDto` (`branchId`, `warehouseId`, `status`, `pageNumber`, `pageSize`) | `PagedResultDto<StockTransferResponseDto>` | `Owner, Staff` (Cashier: 403 Forbidden) | `UNAUTHORIZED`, `VALIDATION_ERROR` |
| `/api/stock-transfers/{id}` | `GET` | `id` (GUID) | `StockTransferResponseDto` | `Owner, Staff` (Cashier: 403 Forbidden) | `NOT_FOUND` (404), `VALIDATION_ERROR` (400) |
| `/api/stock-counts` | `GET` | `StockCountFilterRequestDto` (`branchId`, `warehouseId`, `status`, `pageNumber`, `pageSize`) | `PagedResultDto<StockCountResponseDto>` | `Owner, Staff` (Cashier: 403 Forbidden) | `UNAUTHORIZED`, `VALIDATION_ERROR` |
| `/api/stock-counts/{id}` | `GET` | `id` (GUID) | `StockCountResponseDto` | `Owner, Staff` (Cashier: 403 Forbidden) | `NOT_FOUND` (404), `VALIDATION_ERROR` (400) |

## 2. Phân hệ Trung tâm Đơn hàng (Orders Center)

| Tuyến đường (Route) | Phương thức | DTO Đầu vào / Header | DTO Phản hồi | Phân quyền (Role) | Mã lỗi chính (ErrorCode) |
| --- | --- | --- | --- | --- | --- |
| `/api/orders` | `GET` | `OrderFilterDto` (`status`, `channel`, `branchId`, `fromDate`, `toDate`, `search`, `pageIndex`, `pageSize`) | `PagedResult<OrderSummaryDto>` | `Owner, Staff` | `VALIDATION_ERROR` |
| `/api/orders/{id}` | `GET` | `id` (GUID) | `OrderResponseDto` | `Owner, Staff` | `ORDER_NOT_FOUND` (404) |
| `/api/orders` | `POST` | `CreateOrderRequestDto` + Header: `Idempotency-Key` / `X-Request-Key` | `OrderResponseDto` (HTTP 201 hoặc 200 Replay) | `Owner, Staff` | `INSUFFICIENT_STOCK`, `VALIDATION_ERROR`, `IDEMPOTENCY_CONFLICT` |
| `/api/orders/{id}/confirm` | `POST` | `ConfirmOrderRequestDto` (`confirmedBy`, `note`) | `OrderResponseDto` (HTTP 200) | `Owner, Staff` | `INVALID_STATE_TRANSITION`, `INSUFFICIENT_STOCK` |
| `/api/orders/{id}/process` | `POST` | `ProcessOrderRequestDto` (`note`) | `OrderResponseDto` (HTTP 200) | `Owner, Staff` | `INVALID_STATE_TRANSITION` |
| `/api/orders/{id}/complete` | `POST` | `CompleteOrderRequestDto` (`completedBy`, `note`) | `OrderResponseDto` (HTTP 200) | `Owner, Staff` | `INVALID_STATE_TRANSITION` |
| `/api/orders/{id}/cancel` | `POST` | `CancelOrderRequestDto` (`reason`, `cancelledBy`) | `OrderResponseDto` (HTTP 200) | `Owner, Staff` | `INVALID_STATE_TRANSITION`, `ORDER_ALREADY_CANCELLED` |

## 3. Ma trận chuyển đổi trạng thái (State Machine)

| Trạng thái hiện tại | Thao tác khả dụng | API Endpoint | Trạng thái tiếp theo | Ghi chú nghiệp vụ |
| --- | --- | --- | --- | --- |
| `Draft` | Tạo / Giữ chỗ | `POST /api/orders` | `Reserved` | Khởi tạo đơn và giữ chỗ tồn kho 15 phút |
| `Reserved` | Duyệt đơn | `POST /api/orders/{id}/confirm` | `Confirmed` | Trừ `on_hand`, giảm `reserved`, ghi Sổ cái OUT, chốt WAC |
| `Reserved` | Hủy đơn | `POST /api/orders/{id}/cancel` | `Cancelled` | Hoàn trả giữ chỗ về khả dụng, không ghi sổ cái |
| `Confirmed` | Xử lý / Đóng gói | `POST /api/orders/{id}/process` | `Processing` | Đóng gói đơn hàng, chuẩn bị xuất kho giao vận |
| `Confirmed` | Hoàn tất | `POST /api/orders/{id}/complete` | `Completed` | Khách nhận hàng thành công (không trừ tồn lần 2) |
| `Processing` | Hoàn tất | `POST /api/orders/{id}/complete` | `Completed` | Hoàn tất chu trình đơn hàng |
| `Completed` | Không | N/A | N/A | Trạng thái cuối |
| `Cancelled` | Không | N/A | N/A | Trạng thái cuối |
