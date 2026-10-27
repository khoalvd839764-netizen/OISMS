# 03. HỆ THỐNG MÃ MÀU VÀ DESIGN TOKENS TRẠNG THÁI (COLOR SYSTEM & STATUS TOKENS)

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-ERR-03` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực** | `2026-10-25` |
| **Tiêu chuẩn tuân thủ** | `WCAG 2.1 AA (Contrast Ratio >= 4.5:1)`, Tailwind CSS v4 |
| **Source code áp dụng** | [`src/frontend/admin-dashboard/src/index.css`](file:///d:/OISMS/src/frontend/admin-dashboard/src/index.css), [`src/frontend/pos-app/src/index.css`](file:///d:/OISMS/src/frontend/pos-app/src/index.css) |

---

## 1. Giới Thiệu Hệ Màu OISMS

Hệ thống mã màu trong OISMS được xây dựng trên triết lý **rõ ràng, an toàn thị giác và phản hồi trực quan ngay lập tức**. Toàn bộ mã màu thông báo trạng thái, phản hồi lỗi và phân loại dữ liệu được kiểm soát nghiêm ngặt qua 5 cấp độ ngữ nghĩa (Semantic Tokens).

Mỗi cấp độ bao gồm 4 biến thể phối hợp:
1. **Solid Base:** Màu chính dùng cho nút bấm, viền nổi bật hoặc văn bản tiêu đề quan trọng.
2. **Soft Background:** Màu nền nhạt (pha tỉ lệ 8-12% opacity) giúp làm nổi bật khối thông báo mà không gây mỏi mắt.
3. **Border Line:** Màu đường viền bao quanh Toast/Alert giúp tạo độ tương phản hình khối sắc nét.
4. **Deep Text:** Màu chữ đậm tương phản cao, bảo đảm đạt tỷ lệ tương phản tối thiểu $4.5:1$ theo chuẩn WCAG 2.1 AA.

---

## 2. Bảng Tra Cứu Design Tokens Cốt Lõi

| Nhóm ngữ nghĩa | Tên biến CSS | Mã HEX | Giá trị RGB | Mô tả công năng | Ví dụ áp dụng điển hình |
| :--- | :--- | :---: | :---: | :--- | :--- |
| 🔴 **Danger (Lỗi / Nguy hiểm)** | `--danger`<br/>`--danger-soft`<br/>`--danger-border`<br/>`--danger-text` | `#c93c48`<br/>`#fff0f1`<br/>`#f8cdd2`<br/>`#8d3540` | `201, 60, 72`<br/>`255, 240, 241`<br/>`248, 205, 210`<br/>`141, 53, 64` | Báo lỗi nghiêm trọng, thất bại giao dịch, hết hàng khả dụng, hành động xóa dữ liệu vĩnh viễn. | Toast lỗi thanh toán, Input lỗi xác thực, Nút "Hủy đơn hàng", Huy hiệu `Cancelled`, Trạng thái `OFFLINE`. |
| 🟡 **Warning (Cảnh báo)** | `--warning`<br/>`--warning-soft`<br/>`--warning-border`<br/>`--warning-text` | `#b76e00`<br/>`#fff5dc`<br/>`#fae1aa`<br/>`#7a4900` | `183, 110, 0`<br/>`255, 245, 220`<br/>`250, 225, 170`<br/>`122, 73, 0` | Cảnh báo trạng thái cần chú ý, sắp chạm ngưỡng giới hạn hoặc chờ phê duyệt. | Cảnh báo tồn kho dưới định mức (`Low Stock`), Huy hiệu `Reserved`, Đợt kiểm kê đang đếm (`Counting`). |
| 🟢 **Success (Thành công)** | `--success`<br/>`--success-soft`<br/>`--success-border`<br/>`--success-text` | `#16845b`<br/>`#e8f7f0`<br/>`#bbf7d0`<br/>`#0d543a` | `22, 132, 91`<br/>`232, 247, 240`<br/>`187, 247, 208`<br/>`13, 84, 58` | Xác nhận hành động thành công, hệ thống ổn định, giao dịch hoàn tất hợp lệ. | Toast thanh toán POS thành công, Duyệt nhập kho, Huy hiệu `Confirmed`, `Delivered`, `Finished`, Trạng thái `ONLINE`. |
| 🔵 **Info / Brand (Thông tin)** | `--brand`<br/>`--brand-dark`<br/>`--info`<br/>`--info-soft`<br/>`--info-border`<br/>`--info-text` | `#1769e0`<br/>`#0f55bd`<br/>`#2768d8`<br/>`#edf3ff`<br/>`#bfdbfe`<br/>`#1e40af` | `23, 105, 224`<br/>`15, 85, 189`<br/>`39, 104, 216`<br/>`237, 243, 255`<br/>`191, 219, 254`<br/>`30, 64, 175` | Nhận diện thương hiệu chính, trạng thái đang vận hành, luân chuyển trung gian. | Nút hành động chính (`Primary Button`), Huy hiệu đơn hàng sàn TMĐT, Phiếu chuyển kho `InTransit`. |
| ⚪ **Neutral (Trung tính)** | `--ink`<br/>`--muted`<br/>`--subtle`<br/>`--line`<br/>`--canvas`<br/>`--surface` | `#172033`<br/>`#667085`<br/>`#98a2b3`<br/>`#e6eaf0`<br/>`#f6f7f9`<br/>`#ffffff` | `23, 32, 51`<br/>`102, 112, 133`<br/>`152, 162, 179`<br/>`230, 234, 240`<br/>`246, 247, 249`<br/>`255, 255, 255` | Màu nền giao diện, đường kẻ phân cách, văn bản phụ, trạng thái sơ khởi. | Nền trang (`Canvas`), Viền bảng dữ liệu, Huy hiệu bản nháp (`Draft`), Mặt hàng ngừng kinh doanh (`Inactive`). |

---

## 3. Khai Báo Biến CSS Chuẩn Dự Án

Được khai báo tại `:root` trong [`src/frontend/admin-dashboard/src/index.css`](file:///d:/OISMS/src/frontend/admin-dashboard/src/index.css):

```css
:root {
  /* Brand Tokens */
  --brand: #1769e0;
  --brand-dark: #0f55bd;
  --brand-soft: #eaf2ff;
  
  /* Text & Surface Tokens */
  --ink: #172033;
  --muted: #667085;
  --subtle: #98a2b3;
  --line: #e6eaf0;
  --surface: #ffffff;
  --canvas: #f6f7f9;
  --nav: #111b2e;

  /* Semantic Feedback Tokens */
  --success: #16845b;
  --success-soft: #e8f7f0;
  --success-border: #bbf7d0;
  --success-text: #0d543a;

  --warning: #b76e00;
  --warning-soft: #fff5dc;
  --warning-border: #fae1aa;
  --warning-text: #7a4900;

  --danger: #c93c48;
  --danger-soft: #fff0f1;
  --danger-border: #f8cdd2;
  --danger-text: #8d3540;

  --info: #2768d8;
  --info-soft: #edf3ff;
  --info-border: #bfdbfe;
  --info-text: #1e40af;

  /* Shape & Elevation */
  --radius: 12px;
  --shadow: 0 1px 2px rgb(16 24 40 / 4%), 0 3px 12px rgb(16 24 40 / 4%);
}
```

---

## 4. Ma Trận Màu Sắc Cho Các Trạng Thái Thực Thể Nghiệp Vụ

### 4.1. Vòng Đời Đơn Hàng (`Order Status`)
*Chi tiết máy trạng thái:* [`../04-architecture-and-design/04-state-machines.md`](../04-architecture-and-design/04-state-machines.md)

| Trạng thái | Tone màu | Màu nền (`Background`) | Màu chữ (`Text`) | Màu viền (`Border`) | Ý nghĩa nghiệp vụ |
| :--- | :---: | :---: | :---: | :---: | :--- |
| `Draft` | `neutral` | `#f0f2f5` | `#526075` | `#e4e7ec` | Đơn hàng nháp chưa giữ tồn kho. |
| `Reserved` | `warning` | `#fff5dc` | `#7a4900` | `#fae1aa` | Đang giữ chỗ tồn kho trong 15 phút. |
| `Confirmed` | `info` | `#edf3ff` | `#1e40af` | `#bfdbfe` | Đã duyệt xuất kho, đã trừ tồn và chụp WAC. |
| `Shipped` | `info` | `#e0f2fe` | `#0369a1` | `#bae6fd` | Đang giao hàng trên đường. |
| `Delivered` / `Completed` | `success` | `#e8f7f0` | `#0d543a` | `#bbf7d0` | Khách đã nhận hàng thành công. |
| `Cancelled` | `danger` | `#fff0f1` | `#8d3540` | `#f8cdd2` | Đơn đã hủy, đã hoàn tồn giữ chỗ về kho. |

### 4.2. Vòng Đời Phiếu Chuyển Kho (`Stock Transfer Status`)
| Trạng thái | Tone màu | Màu nền | Màu chữ | Ý nghĩa nghiệp vụ |
| :--- | :---: | :---: | :---: | :--- |
| `Draft` | `neutral` | `#f0f2f5` | `#526075` | Mới tạo danh sách hàng cần chuyển. |
| `InTransit` | `info` | `#edf3ff` | `#1e40af` | Đã xuất khỏi kho đi, đang trên đường vận chuyển. |
| `Received` | `success` | `#e8f7f0` | `#0d543a` | Kho đến đã kiểm đếm và xác nhận nhập kho thành công. |
| `Cancelled` | `danger` | `#fff0f1` | `#8d3540` | Hủy phiếu chuyển (chỉ cho phép khi ở `Draft`). |

### 4.3. Vòng Đời Phiếu Kiểm Kê (`Stock Count Status`)
| Trạng thái | Tone màu | Màu nền | Màu chữ | Ý nghĩa nghiệp vụ |
| :--- | :---: | :---: | :---: | :--- |
| `Draft` | `neutral` | `#f0f2f5` | `#526075` | Đang lập danh mục mặt hàng kiểm kê. |
| `Counting` | `warning` | `#fff5dc` | `#7a4900` | Nhân viên đang đi kiểm đếm thực tế tại quầy/kệ. |
| `Finished` | `success` | `#e8f7f0` | `#0d543a` | Đã chốt số liệu và cân bằng chênh lệch sổ sách. |
| `Cancelled` | `danger` | `#fff0f1` | `#8d3540` | Hủy đợt kiểm kê. |

### 4.4. Trạng Thái Kết Nối Quầy POS (`Network Status`)
| Trạng thái | Tone màu | Mã HEX | Biểu tượng Lucide | Ý nghĩa và hành vi |
| :--- | :---: | :---: | :---: | :--- |
| `ONLINE` | `success` | `#16845b` | `<Wifi size={14} />` | Kết nối trực tuyến với máy chủ qua SignalR/HTTP. Mọi thao tác đồng bộ tức thời. |
| `OFFLINE` | `danger` | `#c93c48` | `<WifiOff size={14} />` | Mất kết nối Internet. Chuyển sang lưu tạm giỏ hàng LocalStorage, phát cảnh báo viền đỏ. |
