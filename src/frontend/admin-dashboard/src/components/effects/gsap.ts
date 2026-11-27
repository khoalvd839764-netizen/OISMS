import gsap from "gsap";
import { useGSAP } from "@gsap/react";

// Đăng ký một lần cho toàn app trước khi gọi useGSAP.
gsap.registerPlugin(useGSAP);

/** Điều kiện chạy hiệu ứng: người dùng không bật giảm chuyển động. */
export const MOTION_OK = "(prefers-reduced-motion: no-preference)";

export { gsap, useGSAP };
