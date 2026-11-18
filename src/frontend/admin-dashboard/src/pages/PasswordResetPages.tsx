import { AlertCircle, ArrowLeft, ArrowRight, CheckCircle2, Eye, EyeOff, KeyRound, LockKeyhole, Mail } from "lucide-react";
import { useEffect, useState, type FormEvent, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { Button, Input } from "../components/ui";
import { requestPasswordReset, resetPassword, takeResetTokenFromUrl, usePasswordResetEnabled } from "../services/passwordResetService";

// Upgrade 10: trang Quên mật khẩu và Đặt mật khẩu mới (không cần đăng nhập).

function AuthCard({ title, subtitle, children }: { title: string; subtitle: string; children: ReactNode }) {
  return (
    <main className="auth-page">
      <section className="auth-story">
        <div className="auth-logo">
          <div className="brand-mark">O</div>
          <strong>OISM</strong>
        </div>
        <div>
          <span className="auth-kicker">BẢO MẬT TÀI KHOẢN</span>
          <h1>Lấy lại quyền truy cập an toàn.</h1>
          <p>Link đặt lại mật khẩu chỉ gửi tới email của tài khoản, có hiệu lực 30 phút và chỉ dùng được một lần.</p>
        </div>
        <small>Phiên bản OISM Web Admin · React 19 & ASP.NET Core 8</small>
      </section>
      <section className="auth-form-wrap">
        <div className="auth-form">
          <div className="auth-mobile-logo">
            <div className="brand-mark">O</div>
            <strong>OISM</strong>
          </div>
          <span className="auth-icon"><KeyRound size={23} /></span>
          <h2>{title}</h2>
          <p>{subtitle}</p>
          {children}
          <Link className="auth-back" to="/login"><ArrowLeft size={14} />Quay lại đăng nhập</Link>
        </div>
      </section>
    </main>
  );
}

function Notice({ kind, children }: { kind: "error" | "success"; children: ReactNode }) {
  return (
    <div className={`auth-notice auth-notice--${kind}`} role={kind === "error" ? "alert" : "status"}>
      {kind === "error" ? <AlertCircle size={16} /> : <CheckCircle2 size={16} />}
      <span>{children}</span>
    </div>
  );
}

export function ForgotPasswordPage() {
  const enabled = usePasswordResetEnabled();
  const [identifier, setIdentifier] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState<string | null>(null);

  useEffect(() => { document.title = "Quên mật khẩu | OISMS"; }, []);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!identifier.trim()) { setError("Vui lòng nhập email hoặc số điện thoại."); return; }
    setLoading(true);
    const result = await requestPasswordReset(identifier.trim());
    setLoading(false);
    if (result.ok) setSent(result.message || "Đã gửi yêu cầu.");
    else setError(result.error || "Không gửi được yêu cầu.");
  };

  return (
    <AuthCard title="Quên mật khẩu" subtitle="Nhập email hoặc số điện thoại của tài khoản. Link đặt lại sẽ được gửi vào email của tài khoản đó.">
      {enabled === false && (
        <Notice kind="error">Tự đặt lại mật khẩu chưa được bật trên hệ thống này. Vui lòng liên hệ chủ cửa hàng để được đặt lại mật khẩu.</Notice>
      )}
      {error && <Notice kind="error">{error}</Notice>}
      {sent ? (
        <Notice kind="success">{sent}</Notice>
      ) : (
        <form onSubmit={submit}>
          <label>
            <span>Email hoặc số điện thoại</span>
            <div className="auth-input">
              <Mail size={17} />
              <Input
                autoFocus
                autoComplete="username"
                placeholder="owner@cuahang.vn hoặc 0901234567"
                value={identifier}
                onChange={(e) => setIdentifier(e.target.value)}
                disabled={enabled === false}
              />
            </div>
          </label>
          <Button type="submit" className="auth-submit" disabled={loading || enabled === false}>
            {loading ? "Đang gửi..." : "Gửi link đặt lại mật khẩu"}
            <ArrowRight size={17} />
          </Button>
        </form>
      )}
    </AuthCard>
  );
}

export function ResetPasswordPage() {
  const [token] = useState(() => takeResetTokenFromUrl());
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [show, setShow] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  useEffect(() => { document.title = "Đặt mật khẩu mới | OISMS"; }, []);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    if (password.length < 6 || password.length > 100) { setError("Mật khẩu mới phải từ 6 đến 100 ký tự."); return; }
    if (password !== confirm) { setError("Mật khẩu xác nhận không khớp."); return; }
    setLoading(true);
    const result = await resetPassword(token, password);
    setLoading(false);
    if (result.ok) setDone(true);
    else setError(result.error || "Đặt lại mật khẩu thất bại.");
  };

  if (!token) {
    return (
      <AuthCard title="Link không hợp lệ" subtitle="Link đặt lại mật khẩu bị thiếu hoặc đã được mở trước đó.">
        <Notice kind="error">Vui lòng mở lại link trong email, hoặc <Link to="/forgot-password">yêu cầu link mới</Link>.</Notice>
      </AuthCard>
    );
  }

  return (
    <AuthCard title="Đặt mật khẩu mới" subtitle="Sau khi đổi, mọi phiên đăng nhập cũ sẽ phải đăng nhập lại.">
      {error && <Notice kind="error">{error} {error.includes("Link") && <Link to="/forgot-password">Yêu cầu link mới</Link>}</Notice>}
      {done ? (
        <Notice kind="success">Đặt lại mật khẩu thành công. <Link to="/login">Đăng nhập bằng mật khẩu mới</Link>.</Notice>
      ) : (
        <form onSubmit={submit}>
          <label>
            <span>Mật khẩu mới *</span>
            <div className="auth-input">
              <LockKeyhole size={17} />
              <Input type={show ? "text" : "password"} autoComplete="new-password" placeholder="Tối thiểu 6 ký tự" value={password} onChange={(e) => setPassword(e.target.value)} autoFocus />
              <button type="button" aria-label={show ? "Ẩn mật khẩu" : "Hiện mật khẩu"} onClick={() => setShow(!show)} style={{ background: "none", border: "none", cursor: "pointer", color: "inherit", padding: 0 }}>
                {show ? <EyeOff size={17} /> : <Eye size={17} />}
              </button>
            </div>
          </label>
          <label>
            <span>Xác nhận mật khẩu mới *</span>
            <div className="auth-input">
              <LockKeyhole size={17} />
              <Input type={show ? "text" : "password"} autoComplete="new-password" placeholder="Nhập lại mật khẩu mới" value={confirm} onChange={(e) => setConfirm(e.target.value)} />
            </div>
          </label>
          <Button type="submit" className="auth-submit" disabled={loading}>
            {loading ? "Đang lưu..." : "Đặt mật khẩu mới"}
            <ArrowRight size={17} />
          </Button>
        </form>
      )}
    </AuthCard>
  );
}
