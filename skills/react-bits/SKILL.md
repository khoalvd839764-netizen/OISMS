---
name: react-bits
description: Chọn, chép và chỉnh component hiệu ứng React Bits (DavidHDev/react-bits) cho frontend OISMS. Dùng khi cần thêm hiệu ứng nền, chữ động, thẻ spotlight, đếm số hoặc micro-interaction vào admin-dashboard; nhắc đến React Bits, reactbits.dev hoặc component hiệu ứng copy-paste.
license: MIT
---

# React Bits trong OISMS

React Bits là thư viện component hiệu ứng dạng **chép mã nguồn vào dự án**, không phải gói npm. Nguồn: https://github.com/DavidHDev/react-bits, trang demo https://reactbits.dev.

Giấy phép là **MIT + Commons Clause**: được dùng trong sản phẩm, không được bán lại chính bộ component. Giữ dòng ghi nguồn ở đầu mỗi file chép về.

## Lấy component

Mỗi component có 4 biến thể trong repo: `src/ts-tailwind`, `src/ts-default`, `src/tailwind`, `src/content` (JS + CSS). Admin OISMS dùng TypeScript + Tailwind 4 nên lấy **`src/ts-tailwind/<Nhóm>/<Tên>/<Tên>.tsx`**.

Nhóm: `Animations`, `Backgrounds`, `Components`, `TextAnimations`, `Micro`.

Cách lấy:

1. Clone repo vào thư mục tạm (không clone vào repo OISMS).
2. Đọc toàn bộ file component trước khi chép.
3. Chép vào `src/frontend/admin-dashboard/src/components/effects/`, thêm dòng đầu file:
   `// Nguồn: React Bits (DavidHDev/react-bits), <Nhóm>/<Tên>, MIT + Commons Clause. Đã chỉnh cho OISMS: <ghi chú>.`
4. Kiểm tra `import`: nếu cần `motion`, `ogl`, `three`, `matter-js`... thì cân nhắc trước khi thêm phụ thuộc (xem bên dưới).

Có thể dùng shadcn CLI (`npx shadcn@latest add @react-bits/<Tên>-TS-TW`) nhưng admin-dashboard không có `components.json`, nên chép tay là cách được chọn.

## Phụ thuộc đã có trong admin-dashboard

`gsap`, `@gsap/react`, `lenis`. **Không** thêm `motion`, `three`, `ogl` nếu chưa được đồng ý: ưu tiên component chỉ dùng React, WebGL thuần hoặc GSAP. Component dùng `motion` (ví dụ `CountUp`, `BlurText`) thì viết lại phần hoạt ảnh bằng GSAP.

| Component | Phụ thuộc | Ghi chú |
| --- | --- | --- |
| `Backgrounds/SoftAurora`, `Aurora` | WebGL thuần | Hợp nền trang đăng nhập |
| `Components/SpotlightCard`, `BorderGlow` | React | Hợp thẻ KPI |
| `Animations/AnimatedContent`, `FadeContent` | GSAP + ScrollTrigger | Vào trang/khối |
| `TextAnimations/SplitText` | GSAP SplitText | GSAP đã miễn phí toàn bộ plugin |
| `TextAnimations/CountUp`, `BlurText` | motion | Viết lại bằng GSAP |
| `Backgrounds/Particles`, `Galaxy`... | WebGL nặng | Tránh trong trang nghiệp vụ |

## Quy tắc dùng trong OISMS

- **Chỉ admin-dashboard.** Không đưa hiệu ứng vào `pos-app`: thu ngân cần thao tác nhanh. Không dùng ở `marketplace-simulator`.
- **Nền động chỉ ở trang công khai** (đăng nhập, quên mật khẩu). Trang nghiệp vụ (bảng, phiếu, modal) giữ tĩnh, chỉ cho phép hiệu ứng vào trang ngắn (≤ 0.4s), đếm số KPI và spotlight khi rê chuột.
- **Tôn trọng `prefers-reduced-motion`.** Dùng `gsap.matchMedia()` hoặc kiểm tra `window.matchMedia('(prefers-reduced-motion: reduce)')`; khi bật, hiển thị trạng thái cuối ngay.
- **Không đổi dữ liệu hiển thị.** Đếm số chỉ là hoạt ảnh; giá trị cuối phải đúng chuỗi định dạng `vi-VN` như trước. Giữ nguyên kiểm tra quyền (ví dụ lãi gộp chỉ Owner) — hiệu ứng không được làm lộ trường bị che.
- **Màu theo token của Admin** (`--brand`, `--brand-soft`, `--nav`...) trong `src/index.css`, không dùng màu mặc định của demo.
- **Dọn dẹp khi unmount.** Component WebGL phải xóa canvas, hủy `requestAnimationFrame`, `ResizeObserver`; GSAP dùng `useGSAP` với `scope` (xem skill `gsap-react`).
- **CSS lớp:** style của Admin nằm ngoài `@layer`, nên thắng class Tailwind. Khi bọc component bằng class có sẵn (ví dụ `stat-card`), kiểm tra lại padding, bo góc, nền.

## Trước khi xong

1. `npm run build --prefix src/frontend/admin-dashboard` PASS.
2. Mở trang trên trình duyệt, không có lỗi console, bật thử reduced motion.
3. Kiểm tra ở màn hẹp (≤ 1024px) không vỡ bố cục.
4. Cập nhật `docs/report-after-upgrade/` nếu thay đổi giao diện người dùng thấy được.
