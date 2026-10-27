# 11. ĐẶC TẢ CHI TIẾT CÁC MÀN HÌNH GIAO DIỆN HỆ THỐNG OISMS (UI SCREENS & USER FLOWS)

| Thông tin kiểm soát thư mục | Chi tiết |
| :--- | :--- |
| **Mã phân hệ (Module ID)** | `MOD-SCR-11` |
| **Phiên bản hiện hành** | `v1.0.0` |
| **Ngày cập nhật gần nhất** | `2026-10-25` |
| **Phạm vi mã nguồn ứng dụng** | Web Admin Dashboard (`src/frontend/admin-dashboard`) & Ứng dụng POS độc lập (`src/frontend/pos-app`) |
| **Tài liệu quy chuẩn đi kèm** | Quy định mã lỗi & màu sắc: [`../12-error-and-status-codes/README.md`](../12-error-and-status-codes/README.md) |

---

## 1. Giới Thiệu Phân Hệ Màn Hình

Hệ thống OISMS (Omnichannel Inventory & Sales Management System) được trang bị kiến trúc giao diện người dùng hiện đại, phân tách tối ưu cho hai bối cảnh sử dụng:
1. **Web Admin Dashboard (Cổng 5173 - `admin-dashboard`):** Bàn làm việc quản trị tập trung dành cho Chủ doanh nghiệp (`Owner`) và Nhân viên vận hành (`Staff`), hỗ trợ quản lý sản phẩm đa biến thể, sổ cái kho bất biến, nhập xuất tồn, điều phối đơn hàng đa kênh và báo cáo tài chính quản trị.
2. **Point of Sale Application (Cổng 5174 - `pos-app`):** Ứng dụng bán lẻ tại quầy chuyên dụng dành cho Thu ngân (`Cashier`) và Nhân viên bán hàng, tối ưu hóa tốc độ với phím tắt nhanh (`F2`, `F4`, `Esc`), quét mã vạch tức thời, kiểm tra tồn khả dụng thời gian thực và thanh toán nguyên tử chống bán vượt.

---

## 2. Ma Trận Phân Quyền Truy Cập Màn Hình (RBAC Matrix)

| STT | Tên màn hình | Đường dẫn Route | Chủ sở hữu (`Owner`) | Nhân viên (`Staff`) | Thu ngân (`Cashier`) | Tài liệu đặc tả chi tiết |
| :---: | :--- | :--- | :---: | :---: | :---: | :--- |
| **1** | Màn hình Đăng nhập & Xác thực | `/login` | ✅ | ✅ | ✅ | [`01-auth-screens.md`](./01-auth-screens.md) |
| **2** | Màn hình Bị từ chối quyền truy cập | `/403` | ✅ | ✅ | ✅ | [`01-auth-screens.md`](./01-auth-screens.md) |
| **3** | Màn hình Hết hạn phiên làm việc | `/session-expired` | ✅ | ✅ | ✅ | [`01-auth-screens.md`](./01-auth-screens.md) |
| **4** | Dashboard Tổng quan kinh doanh | `/admin`, `/` | ✅ | ✅ | ❌ | [`02-dashboard-screen.md`](./02-dashboard-screen.md) |
| **5** | Quản lý Danh mục, Sản phẩm & SKU | `/admin/products` | ✅ | ✅ | ❌ | [`03-product-screens.md`](./03-product-screens.md) |
| **6** | Quản lý Kho, Sổ cái & Kiểm kê | `/admin/inventory` | ✅ | ✅ | ❌ | [`04-inventory-screens.md`](./04-inventory-screens.md) |
| **7** | Trung tâm Đơn hàng đa kênh | `/admin/orders` | ✅ | ✅ | ❌ | [`05-order-screens.md`](./05-order-screens.md) |
| **8** | Quầy Bán hàng POS tích hợp | `/pos` | ✅ | ✅ | ✅ | [`06-pos-terminal-screens.md`](./06-pos-terminal-screens.md) |
| **9** | Quầy Bán hàng POS độc lập (PWA) | `http://localhost:5174` | ✅ | ✅ | ✅ | [`06-pos-terminal-screens.md`](./06-pos-terminal-screens.md) |
| **10** | Điều phối & Bàn giao vận chuyển | `/admin/handoff` | ✅ | ✅ | ❌ | [`07-handoff-screens.md`](./07-handoff-screens.md) |
| **11** | Báo cáo Doanh thu & Quản trị Lãi gộp | `/admin/reports` | ✅ | ❌ | ❌ | [`08-report-screens.md`](./08-report-screens.md) |
| **12** | Quản lý Cơ cấu Tổ chức & Chi nhánh | `/admin/organization` | ✅ | ❌ | ❌ | [`09-organization-screens.md`](./09-organization-screens.md) |

---

## 3. Kiến Trúc Khung Giao Diện Chuẩn (Layout Architecture)

### 3.1. Bố Cục Quản Trị Web Admin (`AdminShell`)
Giao diện Web Admin tuân theo chuẩn Master-Detail hiện đại với thanh điều hướng cánh trái cố định và thanh tiêu đề tiện ích trên đỉnh:

```text
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│  SIDEBAR (240px)          │ TOPBAR (Chi nhánh đang chọn | Chuông thông báo | Avatar User)      │
├───────────────────────────┼────────────────────────────────────────────────────────────────────┤
│ [Logo OISMS Multi-tenant] │ BREADCRUMB / TIÊU ĐỀ TRANG                                         │
│                           ├────────────────────────────────────────────────────────────────────┤
│ 📊 Tổng quan (Dashboard)  │                                                                    │
│ 📦 Sản phẩm & Giá         │                                                                    │
│ 🏬 Tồn kho & Sổ cái       │                      NỘI DUNG MÀN HÌNH CHÍNH                       │
│ 🛒 Đơn hàng đa kênh       │                 (Tables, Filter Bars, Data Cards)                  │
│ 🚚 Bàn giao vận chuyển    │                                                                    │
│ ───────────────           │                                                                    │
│ 📈 Báo cáo lãi gộp (Owner)│                                                                    │
│ 🏢 Chi nhánh & Kho (Owner)│                                                                    │
│ ───────────────           │                                                                    │
│ 💻 Quầy POS Bán lẻ        │                                                                    │
└───────────────────────────┴────────────────────────────────────────────────────────────────────┘
```

### 3.2. Bố Cục Quầy Thu Ngân Bán Lẻ POS (`PosApp`)
Giao diện POS được tối ưu hóa cho màn hình cảm ứng, máy POS hai màn hình và phím bấm vật lý siêu tốc:

```text
┌────────────────────────────────────────────────────────────────────────────────────────────────┐
│ TOP HEADER: [Tên Cửa Hàng] | [Chi nhánh: Q.1] | [Thu ngân: An] | 🟢 ONLINE | [F11 Toàn màn hình]│
├───────────────────────────────────────────────────────────────────┬────────────────────────────┤
│ KHU VỰC TÌM KIẾM & QUÉT MÃ VẠCH (F2)                              │ BẢNG GIỎ HÀNG (CART TABLE) │
│ [ 🔍 Quét mã vạch hoặc gõ tên SKU (F2)... ]                      │ 1. Áo thun trắng - M x2    │
│ ───────────────────────────────────────────────────────────────── │    300,000đ  [ - ] [ + ]   │
│ DANH SÁCH SẢN PHẨM / BIẾN THỂ GỢI Ý NHANH                         │ 2. Quần Jean slimfit x1    │
│ ┌─────────────────────────┐  ┌─────────────────────────┐         │    450,000đ  [ - ] [ + ]   │
│ │ Áo sơ mi Oxford         │  │ Giày Sneaker Canvas     │         ├────────────────────────────┤
│ │ SKU: SM-OXF-01          │  │ SKU: SNK-CNV-42         │         │ Tạm tính:         750,000đ │
│ │ 350,000đ | Tồn: 18 cái  │  │ 520,000đ | Tồn: 5 đôi   │         │ Giảm giá:               0đ │
│ └─────────────────────────┘  └─────────────────────────┘         │ TỔNG CỘNG:        750,000đ │
│                                                                   ├────────────────────────────┤
│                                                                   │ [ F4: THANH TOÁN (PAY) ]   │
└───────────────────────────────────────────────────────────────────┴────────────────────────────┘
```

---

## 4. Mục Lục Tài Liệu Chi Tiết Trong Thư Mục

1. [`01-auth-screens.md`](./01-auth-screens.md) — Đặc tả Màn hình Đăng nhập, Trang lỗi 403 Forbidden và Trang hết hạn phiên 401 Session Timeout.
2. [`02-dashboard-screen.md`](./02-dashboard-screen.md) — Đặc tả Màn hình Tổng quan, Thẻ chỉ số KPI, Biểu đồ doanh thu/lợi nhuận và Cảnh báo kho hàng.
3. [`03-product-screens.md`](./03-product-screens.md) — Đặc tả Màn hình Danh mục, Sản phẩm, Biến thể SKU, Sinh Barcode tự động và Thiết lập giá bán.
4. [`04-inventory-screens.md`](./04-inventory-screens.md) — Đặc tả Màn hình Sổ cái bất biến, Phiếu nhập kho (tính WAC), Chuyển kho 2 bước, Kiểm kê kho (Drift check) và Giữ chỗ.
5. [`05-order-screens.md`](./05-order-screens.md) — Đặc tả Trung tâm Đơn hàng đa kênh, Vòng đời trạng thái đơn hàng, Drawer chi tiết đơn và Tạo đơn thủ công.
6. [`06-pos-terminal-screens.md`](./06-pos-terminal-screens.md) — Đặc tả Màn hình Bán hàng POS tại quầy, Giỏ hàng, Modal thanh toán tiền mặt/QR và In hóa đơn nhiệt.
7. [`07-handoff-screens.md`](./07-handoff-screens.md) — Đặc tả Màn hình Quét mã vận đơn bàn giao shipper và In phiếu đóng gói (Packing Slip).
8. [`08-report-screens.md`](./08-report-screens.md) — Đặc tả Màn hình Báo cáo Doanh thu - Giá vốn - Lãi gộp, Định giá kho theo WAC và Gợi ý tái đặt hàng AI.
9. [`09-organization-screens.md`](./09-organization-screens.md) — Đặc tả Màn hình Quản lý Chi nhánh, Kho trực thuộc, Danh bạ Nhà cung cấp và Phân quyền nhân sự.
