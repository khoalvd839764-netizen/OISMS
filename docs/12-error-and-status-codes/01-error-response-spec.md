# 01. ĐẶC TẢ CẤU TRÚC PHẢN HỒI LỖI CHUẨN RFC 7807 (PROBLEM DETAILS SPECIFICATION)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-ERR-01` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực** | `2026-10-25` |
| **Tiêu chuẩn tuân thủ** | `IETF RFC 7807: Problem Details for HTTP APIs` |
| **Mã tham chiếu backend** | [`OismException.cs`](file:///d:/OISMS/src/backend/src/OISM.Application/Common/Exceptions/OismException.cs), [`PosCheckoutActionHelper.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/PosCheckoutActionHelper.cs), [`Program.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Program.cs#L86-L122) |

---

## 1. Nguyên Tắc Thiết Kế Cấu Trúc Lỗi

Toàn bộ các yêu cầu HTTP gặp lỗi (mã trạng thái $4xx$ và $5xx$) trong hệ thống OISMS phải trả về nội dung theo định dạng MIME `application/problem+json` tuân thủ tiêu chuẩn quốc tế **RFC 7807**.

### 1.1. Mục Tiêu Cốt Lõi
1. **Tính máy móc đọc được (Machine-readable):** Cung cấp trường định danh lỗi duy nhất `errorCode` dạng chuỗi ký tự viết hoa để Frontend có thể switch-case xử lý logic (ví dụ: kích hoạt modal nạp tiền khi gặp `INSUFFICIENT_CASH_RECEIVED`, hoặc focus ô số lượng khi gặp `INSUFFICIENT_STOCK`).
2. **Tính con người đọc được (Human-readable):** Cung cấp trường tiêu đề `title` và thông điệp chi tiết `detail` bằng Tiếng Việt rõ ràng, giúp nhân viên thu ngân hoặc quản trị viên hiểu ngay nguyên nhân và cách khắc phục mà không cần đọc mã kỹ thuật.
3. **Tính truy vết (Traceability):** Cung cấp `instance` (đường dẫn API bị lỗi) và `timestamp` (thời điểm xảy ra lỗi dạng ISO 8601 UTC) để tra cứu nhật ký hệ thống (Application Logs).
4. **Bảo mật thông tin:** Không để lộ cấu trúc bảng CSDL, câu lệnh SQL gốc, hoặc Stack Trace ra ngoài môi trường Production.

---

## 2. Đặc Tả Lược Đồ JSON (JSON Schema)

### 2.1. Cấu Trúc Phản Hồi Chuẩn

```json
{
  "type": "https://oisms.local/errors/{ERROR_CODE}",
  "title": "Tiêu đề lỗi ngắn gọn bằng tiếng Việt",
  "status": 409,
  "detail": "Mô tả chi tiết nguyên nhân phát sinh lỗi và gợi ý hành động cụ thể.",
  "instance": "/api/pos/checkout",
  "errorCode": "INSUFFICIENT_STOCK",
  "errors": {
    "fieldName": [
      "Thông báo lỗi chi tiết theo từng trường (nếu là lỗi xác thực)"
    ]
  },
  "timestamp": "2026-10-25T08:15:30.1234567Z"
}
```

### 2.2. Bảng Mô Tả Các Trường Dữ Liệu

| Tên trường | Kiểu dữ liệu | Bắt buộc | Mô tả & Quy chuẩn |
| :--- | :---: | :---: | :--- |
| `type` | `string (URI)` | **Có** | Định danh loại lỗi dưới dạng URI chuẩn. Mặc định là `https://oisms.local/errors/{errorCode}`. |
| `title` | `string` | **Có** | Tóm tắt lỗi ngắn gọn bằng tiếng Việt, viết hoa chữ cái đầu. Ví dụ: `"Tồn kho không đủ"`, `"Lỗi xác thực dữ liệu"`. |
| `status` | `integer` | **Có** | Mã trạng thái HTTP phản hồi (trùng khớp hoàn toàn với Status Code của giao thức HTTP). |
| `detail` | `string` | **Có** | Nội dung thông báo lỗi chi tiết, có thể chứa các biến động (vd: mã SKU, số lượng khả dụng, giá tiền sai lệch). Được thiết kế để hiển thị trực tiếp cho người dùng. |
| `instance` | `string` | **Có** | Đường dẫn API Endpoint nơi phát sinh lỗi (lấy từ `HttpContext.Request.Path`). |
| `errorCode` | `string` | **Có** | Chuỗi định danh mã lỗi nghiệp vụ chuẩn `SNAKE_CASE_UPPER` (Chi tiết xem tại [`02-error-codes-catalog.md`](./02-error-codes-catalog.md)). |
| `errors` | `object` | *Tùy chọn* | Từ điển các lỗi theo từng trường dữ liệu đầu vào. Chỉ xuất hiện khi `status = 400` và `errorCode = "VALIDATION_ERROR"`. |
| `timestamp` | `string (ISO 8601)` | **Có** | Thời điểm xảy ra lỗi theo chuẩn ISO 8601 UTC dạng `YYYY-MM-DDTHH:mm:ss.fffffffZ`. |

---

## 3. Bảng Ánh Xạ Mã Trạng Thái HTTP (HTTP Status Codes)

| HTTP Status | Tên trạng thái | Phân loại ngữ cảnh trong OISMS | Ví dụ ErrorCode điển hình |
| :---: | :--- | :--- | :--- |
| **`400`** | **Bad Request** | Dữ liệu đầu vào sai định dạng, thiếu trường bắt buộc, giá trị số âm, hoặc vi phạm cú pháp. | `VALIDATION_ERROR`, `INSUFFICIENT_CASH_RECEIVED`, `PRICE_MISMATCH`, `INVALID_PAYMENT_METHOD` |
| **`401`** | **Unauthorized** | Chưa cung cấp JWT Token, Token không hợp lệ hoặc đã hết hạn hiệu lực. | `AUTH_UNAUTHORIZED`, `AUTH_TOKEN_EXPIRED`, `AUTH_INVALID_TOKEN` |
| **`403`** | **Forbidden** | Người dùng đã đăng nhập thành công nhưng không có quyền thực hiện thao tác (RBAC). | `FORBIDDEN_ACCESS`, `AUTH_TENANT_LOCKED` |
| **`404`** | **Not Found** | Không tìm thấy bản ghi dữ liệu theo khóa chính hoặc mã định danh. | `SKU_NOT_FOUND`, `ORDER_NOT_FOUND`, `BRANCH_NOT_FOUND`, `STOCK_COUNT_NOT_FOUND` |
| **`409`** | **Conflict** | Xung đột nghiệp vụ, không thỏa mãn trạng thái hệ thống, bán vượt tồn kho, xung đột Idempotency. | `INSUFFICIENT_STOCK`, `IDEMPOTENCY_PAYLOAD_MISMATCH`, `INVALID_STATE_TRANSITION`, `STOCK_COUNT_DRIFT_DETECTED` |
| **`422`** | **Unprocessable Entity** | Dữ liệu đúng cú pháp nhưng thực thể liên quan đang bị khóa/ngưng hoạt động. | `SKU_INACTIVE`, `BRANCH_INACTIVE`, `WAREHOUSE_INACTIVE` |
| **`500`** | **Internal Server Error** | Sự cố máy chủ không mong muốn, lỗi kết nối cơ sở dữ liệu hoặc ngoại lệ chưa bắt được. | `INTERNAL_SERVER_ERROR`, `DATABASE_CONNECTION_ERROR` |

---

## 4. Các Ví Dụ Phản Hồi Mẫu

### 4.1. Lỗi Xác Thực Dữ Liệu Form (`400 Bad Request`)
```json
{
  "type": "https://oisms.local/errors/VALIDATION_ERROR",
  "title": "Lỗi xác thực dữ liệu",
  "status": 400,
  "detail": "Số lượng mặt hàng mua phải lớn hơn 0.",
  "instance": "/api/pos/checkout",
  "errorCode": "VALIDATION_ERROR",
  "errors": {
    "Items[0].Quantity": ["Số lượng mặt hàng mua phải lớn hơn 0."],
    "PaymentMethod": ["Phương thức thanh toán không được để trống."]
  },
  "timestamp": "2026-10-25T15:10:00.0000000Z"
}
```

### 4.2. Lỗi Không Đủ Tồn Kho Khả Dụng (`409 Conflict`)
```json
{
  "type": "https://oisms.local/errors/INSUFFICIENT_STOCK",
  "title": "Tồn kho không đủ",
  "status": 409,
  "detail": "SKU 'AO-THUN-DEN-L' chỉ còn 2 sản phẩm khả dụng, không đủ đáp ứng số lượng yêu cầu 5.",
  "instance": "/api/pos/checkout",
  "errorCode": "INSUFFICIENT_STOCK",
  "timestamp": "2026-10-25T15:10:02.1245600Z"
}
```

### 4.3. Lỗi Xung Đột Khóa Idempotency (`409 Conflict`)
```json
{
  "type": "https://oisms.local/errors/IDEMPOTENCY_PAYLOAD_MISMATCH",
  "title": "Xung đột khóa Idempotency",
  "status": 409,
  "detail": "RequestKey đã được sử dụng cho một yêu cầu thanh toán khác với nội dung giỏ hàng khác.",
  "instance": "/api/pos/checkout",
  "errorCode": "IDEMPOTENCY_PAYLOAD_MISMATCH",
  "timestamp": "2026-10-25T15:10:05.9876543Z"
}
```

---

## 5. Quy Chuẩn Xử Lý Tại Frontend (Client-side Handling)

### 5.1. Hàm Phân Giải Lỗi Tiêu Chuẩn (Error Parser)
```typescript
export interface OismProblemDetails {
  type: string;
  title: string;
  status: number;
  detail: string;
  instance: string;
  errorCode: string;
  errors?: Record<string, string[]>;
  timestamp: string;
}

export function parseApiError(error: any): { title: string; detail: string; errorCode: string; fieldErrors?: Record<string, string[]> } {
  if (error.response?.data) {
    const data: OismProblemDetails = error.response.data;
    return {
      title: data.title || "Lỗi thao tác",
      detail: data.detail || "Đã xảy ra lỗi không xác định từ máy chủ.",
      errorCode: data.errorCode || "UNKNOWN_ERROR",
      fieldErrors: data.errors
    };
  }
  if (error.request) {
    return {
      title: "Mất kết nối mạng",
      detail: "Không thể kết nối đến máy chủ OISMS. Vui lòng kiểm tra đường truyền mạng.",
      errorCode: "NETWORK_DISCONNECTED"
    };
  }
  return {
    title: "Lỗi ứng dụng",
    detail: error.message || "Đã xảy ra lỗi ngoài dự kiến.",
    errorCode: "CLIENT_ERROR"
  };
}
```

### 5.2. Điều Hướng Hành Vi Dựa Trên `errorCode`
* Khi `status === 401`: Xóa token local, phát sự kiện `pos-auth-expired` hoặc chuyển hướng về màn hình `/login` kèm tham số `redirectUrl`.
* Khi `status === 403`: Chuyển hướng người dùng sang màn hình `/403` (Xem chi tiết tại [`DOC-SCR-01 - Auth Screens`](../11-ui-screens/01-auth-screens.md)).
* Khi `errorCode === "INSUFFICIENT_STOCK"`: Tự động cập nhật lại số lượng khả dụng trên giỏ hàng và mở thông báo Toast cảnh báo viền đỏ (Xem tại [`DOC-ERR-03 - Color System Tokens`](./03-color-system-tokens.md)).
* Khi `errorCode === "VALIDATION_ERROR"`: Tự động map danh sách `errors` vào các trường Form tương ứng và đổi màu viền sang đỏ `#c93c48`.
