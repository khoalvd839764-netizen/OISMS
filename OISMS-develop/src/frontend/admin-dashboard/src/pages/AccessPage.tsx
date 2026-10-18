import { Clock3, LockKeyhole } from "lucide-react";
import { Link } from "react-router-dom";

export function AccessPage({ type }: { type: "forbidden" | "expired" }) {
  const forbidden = type === "forbidden";
  return <main className="access-page"><div className="access-card"><div className="brand-mark">O</div><span className="access-icon">{forbidden ? <LockKeyhole size={28} /> : <Clock3 size={28} />}</span><h1>{forbidden ? "Bạn không có quyền truy cập" : "Phiên đăng nhập đã hết hạn"}</h1><p>{forbidden ? "Tài khoản hiện tại không được cấp quyền cho kho hoặc chức năng này. Liên hệ Owner để kiểm tra phạm vi." : "Vì lý do bảo mật, vui lòng đăng nhập lại để tiếp tục làm việc."}</p><Link className="button button--primary" to={forbidden ? "/" : "/login"}>{forbidden ? "Về trang tổng quan" : "Đăng nhập lại"}</Link><small>Ẩn menu không thay thế việc kiểm tra quyền tại backend.</small></div></main>;
}
