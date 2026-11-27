// Ý tưởng từ React Bits TextAnimations/CountUp (DavidHDev/react-bits); bản gốc dùng motion, viết lại bằng GSAP để không thêm phụ thuộc.
import { useRef } from "react";
import { gsap, MOTION_OK, useGSAP } from "./gsap";

type CountUpProps = {
  value: number;
  /** Định dạng hiển thị; giá trị cuối luôn là format(value) chính xác. */
  format: (n: number) => string;
  duration?: number;
};

export function CountUp({ value, format, duration = 1.1 }: CountUpProps) {
  const ref = useRef<HTMLSpanElement>(null);
  const shown = useRef(0);
  const formatRef = useRef(format);
  formatRef.current = format;

  useGSAP(
    () => {
      const el = ref.current;
      if (!el) return;
      const finish = () => {
        shown.current = value;
        el.textContent = formatRef.current(value);
      };

      const mm = gsap.matchMedia();
      mm.add(MOTION_OK, () => {
        const counter = { n: shown.current };
        el.textContent = formatRef.current(Math.round(counter.n));
        gsap.to(counter, {
          n: value,
          duration,
          ease: "power2.out",
          onUpdate: () => {
            shown.current = counter.n;
            el.textContent = formatRef.current(Math.round(counter.n));
          },
          onComplete: finish,
        });
        // Bị hủy giữa chừng (đổi giá trị, StrictMode chạy effect 2 lần): giữ `shown` ở mức đang đếm
        // để lần chạy sau đếm tiếp từ đó; lần chạy sau luôn kết thúc ở format(value).
      });
      mm.add("(prefers-reduced-motion: reduce)", finish);
      return () => mm.revert();
    },
    { dependencies: [value, duration] },
  );

  return <span ref={ref}>{format(value)}</span>;
}
