# Hiệu ứng giao diện Admin: GSAP, Lenis, React Bits

Ngày: 28/11/2026. Phạm vi: chỉ `src/frontend/admin-dashboard`. Không đổi `pos-app`, `marketplace-simulator`, backend hay API.

## Quyết định phạm vi

- Chỉ dùng hiệu ứng nhẹ ở Admin. POS giữ nguyên vì thu ngân cần thao tác nhanh.
- Lenis (cuộn mượt) **chỉ bật ở trang đăng nhập/đăng ký**. Không bật trong trang nghiệp vụ vì dễ làm hỏng cuộn trong bảng, modal và header dính.
- Mọi hiệu ứng tắt khi người dùng bật `prefers-reduced-motion`.

## Thay đổi

| Vị trí | Thay đổi |
| --- | --- |
| `package.json` | Thêm `gsap`, `@gsap/react`, `lenis` |
| `src/components/effects/gsap.ts` | Đăng ký `useGSAP`, hằng `MOTION_OK` cho `gsap.matchMedia()` |
| `src/components/effects/SoftAurora.tsx` | Chép từ React Bits (Backgrounds/SoftAurora), nền WebGL2 cho khối giới thiệu ở trang đăng nhập. Máy không có WebGL2 thì giữ nền gradient cũ |
| `src/components/effects/SpotlightCard.tsx` | Chép từ React Bits (Components/SpotlightCard), dùng cho thẻ KPI |
| `src/components/effects/CountUp.tsx` | Đếm số bằng GSAP (ý tưởng từ React Bits CountUp, bản gốc dùng `motion`). Giá trị cuối luôn đúng chuỗi định dạng như trước |
| `src/components/ui.tsx` | `StatCard` bọc `SpotlightCard`, nhận thêm `countTo` (không bắt buộc) |
| `src/pages/DashboardPage.tsx` | 4 thẻ KPI đếm số khi có dữ liệu. Lãi gộp chỉ đếm khi là Owner, giữ nguyên kiểm tra quyền |
| `src/components/AdminShell.tsx` | Khối nội dung trượt nhẹ vào khi đổi route (0,35 giây, xóa style inline khi xong để không ảnh hưởng modal `position: fixed`) |
| `src/pages/AuthPage.tsx` | Lenis, nền SoftAurora, hiệu ứng mở đầu bằng timeline GSAP, hiệu ứng khi chuyển Đăng nhập ⇄ Đăng ký |

Lưu ý kỹ thuật: CSS của Admin nằm trong `@layer base/components`, nên class Tailwind của component React Bits (`p-8`, `rounded-3xl`, `relative`) thắng. Vì vậy padding, bo góc và vị trí được đặt bằng `style` inline.

## Skill

Skill GSAP (chính thức của GreenSock) và Lenis (chính thức của Darkroom Engineering) được chép vào `skills/`. Skill `react-bits` do nhóm tự viết. Nguồn và phiên bản ghi ở `skills/THIRD_PARTY_SKILLS.md`.

## Kiểm tra

- `npm run build --prefix src/frontend/admin-dashboard`: PASS.
- Trình duyệt (dev server, backend Docker): trang đăng nhập không lỗi console; bố cục khối giới thiệu giữ nguyên; form đăng ký hiện đủ 7 trường sau hiệu ứng; Lenis cuộn mượt khi form dài hơn màn hình.
- Dashboard (Owner, dữ liệu demo): 4 thẻ KPI đếm số trong khoảng 1,1 giây, giá trị cuối giống hệt bản cũ (kể cả `2.285.761,194 ₫`); spotlight sáng theo con trỏ; padding/bo góc giữ `17px 18px`/`12px`.
- Đổi trang Tổng quan → Sản phẩm: nội dung trượt vào khoảng 0,4 giây, xong thì `transform: none`, không còn style inline. Modal "Thêm sản phẩm" phủ đúng toàn màn hình.
- Lỗi đã sửa khi kiểm tra: ở chế độ dev, React StrictMode chạy effect hai lần làm số nhảy thẳng tới giá trị cuối. Đã sửa phần dọn dẹp của `CountUp`.
- Chưa kiểm tra được hình ảnh nền SoftAurora vì trình duyệt kiểm thử không có WebGL2 (đã xác nhận fallback gradient hiển thị đúng).
