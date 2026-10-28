# 07. ĐẶC TẢ MÀN HÌNH BÀN GIAO & ĐIỀU PHỐI VẬN CHUYỂN (HANDOFF & DISPATCH SCREENS)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SCR-07` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực** | `2026-10-25` |
| **Đường dẫn URL Routes** | `/admin/handoff`, `/handoff` |
| **Quyền truy cập (RBAC)** | `Owner`, `Staff` |
| **Source code Frontend** | [`HandoffPage.tsx`](file:///d:/OISMS/src/frontend/admin-dashboard/src/pages/HandoffPage.tsx) |
| **Controllers Backend** | [`OrdersController.cs`](file:///d:/OISMS/src/backend/src/OISM.WebApi/Controllers/OrdersController.cs) |

---

## 1. Mục Tiêu & Luồng Nghiệp Vụ

Màn hình **Bàn Giao Kỹ Thuật & Điều Phối Vận Chuyển (Handoff & Dispatch)** giải quyết hai mục tiêu trọng yếu:
1. **Nghiệp vụ Vận hành kho:** Kiểm soát quá trình đóng gói và bàn giao các kiện hàng đã duyệt (`Confirmed`) cho các đơn vị vận chuyển (Giao Hàng Nhanh, Viettel Post, Shopee Xpress...), in phiếu đóng gói (Packing Slip) và cập nhật trạng thái đơn sang Đang giao (`Shipped`).
2. **Kỹ thuật & Kiến trúc (Technical Handoff Matrix):** Cung cấp bảng tổng mục kiểm tra ranh giới tích hợp giữa Frontend React và Backend ASP.NET Core, xác thực tính tương thích của DTO camelCase, cơ chế JWT Bearer, và các ràng buộc phi chức năng (NFR).

---

## 2. Bố Cục Giao Diện Wireframe (ASCII Wireframe)

```text
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ BREADCRUMB: TÀI LIỆU BÀN GIAO                                                                  │
│ Handoff Kỹ Thuật & Nghiệp Vụ Điều Phối                       [ In Danh Sách ] [ Đồng Bộ Sàn ]  │
├────────────────────────────────────────────────────────────────────────────────────────────────┤
│ MA TRẬN MÀN HÌNH VÀ HÀNH ĐỘNG TÍCH HỢP:                                                        │
│ Route          | Hành động nghiệp vụ              | Quyền hạn   | Mã FR   | API Dự kiến        │
│ ───────────────┼──────────────────────────────────┼─────────────┼─────────┼─────────────────── │
│ /login         | Đăng nhập, xác thực JWT, refresh | Công khai   | FR-AUTH | POST /api/auth/*   │
│ /products      | Quản lý SKU, barcode EAN-13, giá | Owner, Staff| FR-PROD | /api/products      │
│ /inventory     | Xem số dư, sổ cái, nhập kho WAC  | Owner, Staff| FR-INV  | /api/inventory/*   │
│ /orders        | Giữ chỗ, xác nhận đơn, xuất kho  | Owner, Staff| FR-ORD  | /api/orders/*      │
│ /pos           | Quét barcode, checkout nguyên tử | Cashier     | FR-POS  | POST /api/pos/*    │
│ /reports       | Doanh thu, giá vốn, lãi gộp      | Owner       | FR-REP  | GET /api/reports/* │
├──────────────────────────────────────────────────┬─────────────────────────────────────────────┤
│ 💻 RANH GIỚI TÍCH HỢP HỆ THỐNG                   │ ⚠️ CÁC GIẢ ĐỊNH NGHIỆP VỤ CẦN DUYỆT        │
│ • TenantId, Role lấy an toàn từ JWT Claims.      │ • Khóa hủy sau trạng thái Confirmed: Bắt    │
│ • Sổ kho Append-Only; Giá vốn là snapshot.       │   buộc phải tạo phiếu trả hoàn tồn.         │
│ • Checkout POS dùng Idempotency-Key và atomic DB.│ • Một chi nhánh có thể có nhiều kho hàng.   │
│ • Phản hồi quầy POS tuyệt đối không lộ COGS.     │ • Ngưỡng gợi ý nhập hàng AI dựa trên WAC.   │
└──────────────────────────────────────────────────┴─────────────────────────────────────────────┘
```

---

## 3. Bảng Hàm API Backend & Tài Liệu Tham Chiếu

| Method | Endpoint Backend | Hàm xử lý Controller / Service | Tài liệu Hợp đồng API tham chiếu | Mã lỗi liên quan |
| :---: | :--- | :--- | :--- | :--- |
| `GET` | `/api/orders` | `OrdersController.GetOrders` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#1-danh-mục-endpoints-đơn-hàng-đa-kênh--webhook-fr-ord-0103-fr-sim-01) | `AUTH_UNAUTHORIZED` (Xem [`DOC-ERR-02`](../12-error-and-status-codes/02-error-codes-catalog.md)) |
| `POST` | `/api/orders/{id}/complete` | `OrdersController.CompleteOrder` | [`DOC-API-04 - 04-order-endpoints.md`](../05-api-contracts/04-order-endpoints.md#1-danh-mục-endpoints-đơn-hàng-đa-kênh--webhook-fr-ord-0103-fr-sim-01) | `ORDER_NOT_FOUND`, `INVALID_STATE_TRANSITION` |
| `GET` | `/api/branches` | `BranchController.GetBranches` | [`DOC-API-01 - 01-auth-endpoints.md`](../05-api-contracts/01-auth-endpoints.md#3-endpoints-quản-lý-chi-nhánh-kho-hàng--nhà-cung-cấp-fr-auth-04) | `AUTH_UNAUTHORIZED` |
