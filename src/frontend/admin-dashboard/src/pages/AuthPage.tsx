import { ArrowRight, Check, Eye, EyeOff, LockKeyhole, Mail, Phone, ShieldCheck, Store, User, AlertCircle } from "lucide-react";
import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Button, Input } from "../components/ui";
import { useAuth } from "../context/AuthContext";

export function AuthPage() {
  const [register, setRegister] = useState(false);
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [tenantName, setTenantName] = useState("");
  const [tenantCode, setTenantCode] = useState("");
  const [fullName, setFullName] = useState("");
  const [phone, setPhone] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [loading, setLoading] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const { login, registerTenant } = useAuth();
  const navigate = useNavigate();

  const handleLogin = async () => {
    setErrorMessage(null);
    if (!email.trim() || !password.trim()) {
      setErrorMessage("Vui lòng nhập đầy đủ Email và Mật khẩu.");
      return;
    }

    setLoading(true);
    const result = await login({ email: email.trim(), password: password.trim() });
    setLoading(false);

    if (result.success) {
      navigate("/");
    } else {
      setErrorMessage(result.error || "Đăng nhập thất bại.");
    }
  };

  const handleRegister = async () => {
    setErrorMessage(null);
    if (!tenantName.trim() || !fullName.trim() || !email.trim() || !password.trim()) {
      setErrorMessage("Vui lòng điền đầy đủ các thông tin bắt buộc.");
      return;
    }

    if (password !== confirmPassword) {
      setErrorMessage("Mật khẩu xác nhận không khớp.");
      return;
    }

    setLoading(true);
    const result = await registerTenant({
      tenantName: tenantName.trim(),
      tenantCode: tenantCode.trim() || undefined,
      ownerEmail: email.trim(),
      ownerPassword: password.trim(),
      ownerFullName: fullName.trim(),
      ownerPhone: phone.trim() || undefined,
    });
    setLoading(false);

    if (result.success) {
      navigate("/");
    } else {
      setErrorMessage(result.error || "Đăng ký doanh nghiệp thất bại.");
    }
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (register) {
      handleRegister();
    } else {
      handleLogin();
    }
  };

  return (
    <main className="auth-page">
      <section className="auth-story">
        <div className="auth-logo">
          <div className="brand-mark">O</div>
          <strong>OISM</strong>
        </div>
        <div>
          <span className="auth-kicker">VẬN HÀNH ĐA KÊNH</span>
          <h1>Một nguồn dữ liệu tin cậy cho bán hàng và tồn kho.</h1>
          <p>Quản lý đơn hàng, kho, giá vốn WAC và điểm bán POS trong cùng một hệ thống phân quyền đa khách thuê (SaaS Multi-tenant).</p>
          <ul>
            <li><Check size={16} />Tồn kho theo SKU: OnHand, Reserved và Available</li>
            <li><Check size={16} />Sổ cái kho bất biến (Append-Only), chống bán vượt tồn</li>
            <li><Check size={16} />Tự động hóa tính giá vốn bình quân gia quyền</li>
          </ul>
        </div>
        <small>Phiên bản OISM Web Admin · React 19 & ASP.NET Core 8</small>
      </section>

      <section className="auth-form-wrap">
        <form className="auth-form" onSubmit={handleSubmit}>
          <div className="auth-mobile-logo">
            <div className="brand-mark">O</div>
            <strong>OISM</strong>
          </div>
          <span className="auth-icon"><ShieldCheck size={23} /></span>
          <h2>{register ? "Tạo doanh nghiệp mới" : "Đăng nhập vào OISM"}</h2>
          <p>{register ? "Mỗi doanh nghiệp có không gian dữ liệu riêng biệt." : "Tiếp tục công việc với tài khoản được phân quyền."}</p>

          {errorMessage && (
            <div style={{ background: "#fee2e2", border: "1px solid #ef4444", color: "#b91c1c", padding: "10px 14px", borderRadius: "8px", fontSize: "12px", display: "flex", alignItems: "center", gap: "8px", margin: "10px 0" }}>
              <AlertCircle size={16} style={{ flexShrink: 0 }} />
              <span>{errorMessage}</span>
            </div>
          )}

          {register && (
            <>
              <label>
                <span>Tên cửa hàng / Doanh nghiệp *</span>
                <div className="auth-input">
                  <Store size={17} />
                  <Input placeholder="Ví dụ: OISM Retail Studio" value={tenantName} onChange={(e) => setTenantName(e.target.value)} required />
                </div>
              </label>
              <label>
                <span>Mã định danh Cửa hàng (Tenant Code)</span>
                <div className="auth-input">
                  <Store size={17} />
                  <Input placeholder="Tự động sinh (hoặc nhập: SHOP-01)" value={tenantCode} onChange={(e) => setTenantCode(e.target.value.toUpperCase())} />
                </div>
              </label>
              <label>
                <span>Họ tên Chủ sở hữu (Owner) *</span>
                <div className="auth-input">
                  <User size={17} />
                  <Input placeholder="Nguyễn Văn An" value={fullName} onChange={(e) => setFullName(e.target.value)} required />
                </div>
              </label>
              <label>
                <span>Số điện thoại</span>
                <div className="auth-input">
                  <Phone size={17} />
                  <Input placeholder="0901234567" value={phone} onChange={(e) => setPhone(e.target.value)} />
                </div>
              </label>
            </>
          )}

          <label>
            <span>{register ? "Email quản trị *" : "Email đăng nhập"}</span>
            <div className="auth-input">
              <Mail size={17} />
              <Input
                type="email"
                placeholder={register ? "owner@cuahang.vn" : "owner@shopa.local"}
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
              />
            </div>
          </label>

          <label>
            <span>Mật khẩu *</span>
            <div className="auth-input">
              <LockKeyhole size={17} />
              <Input
                type={showPassword ? "text" : "password"}
                placeholder="Nhập mật khẩu"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
              />
              <button
                type="button"
                onClick={() => setShowPassword(!showPassword)}
                style={{ background: "none", border: "none", cursor: "pointer", color: "inherit", padding: 0 }}
              >
                {showPassword ? <EyeOff size={17} /> : <Eye size={17} />}
              </button>
            </div>
          </label>

          {register && (
            <label>
              <span>Xác nhận mật khẩu *</span>
              <div className="auth-input">
                <LockKeyhole size={17} />
                <Input
                  type={showPassword ? "text" : "password"}
                  placeholder="Nhập lại mật khẩu"
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  required
                />
              </div>
            </label>
          )}

          <Button
            type="submit"
            className="auth-submit"
            disabled={loading}
          >
            {loading ? "Đang xử lý..." : register ? "Tạo doanh nghiệp & Đăng nhập" : "Đăng nhập"}
            <ArrowRight size={17} />
          </Button>

          <div className="auth-switch">
            {register ? "Đã có tài khoản doanh nghiệp?" : "Chưa có tài khoản?"}
            <Button
              type="button"
              variant="ghost"
              onClick={() => {
                setRegister(!register);
                setErrorMessage(null);
              }}
            >
              {register ? "Đăng nhập" : "Đăng ký doanh nghiệp"}
            </Button>
          </div>

          {!register && (
            <div className="demo-login" style={{ fontSize: "11px", color: "#64748b", marginTop: "12px", borderTop: "1px dashed #cbd5e1", paddingTop: "8px" }}>
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "6px" }}>
                <strong>Tài khoản kiểm thử mẫu:</strong>
                <span style={{ fontSize: "10px", color: "#94a3b8" }}>Bấm để điền nhanh</span>
              </div>
              <div style={{ display: "flex", gap: "6px", flexWrap: "wrap" }}>
                <button
                  type="button"
                  onClick={() => {
                    setEmail("owner@shopa.local");
                    setPassword("YourStrongDemoPassword123!");
                  }}
                  style={{ background: "#f1f5f9", border: "1px solid #cbd5e1", borderRadius: "4px", padding: "4px 8px", cursor: "pointer", fontSize: "11px" }}
                >
                  👑 Chủ cửa hàng (Owner)
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setEmail("staff@shopa.local");
                    setPassword("YourStrongDemoPassword123!");
                  }}
                  style={{ background: "#f0fdf4", border: "1px solid #bbf7d0", borderRadius: "4px", padding: "4px 8px", cursor: "pointer", fontSize: "11px", color: "#166534" }}
                >
                  📦 Nhân viên kho (Staff)
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setEmail("cashier@shopa.local");
                    setPassword("YourStrongDemoPassword123!");
                  }}
                  style={{ background: "#fef2f2", border: "1px solid #fecaca", borderRadius: "4px", padding: "4px 8px", cursor: "pointer", fontSize: "11px", color: "#991b1b" }}
                >
                  💳 Thu ngân (Thử lỗi 403)
                </button>
              </div>
            </div>
          )}
        </form>
      </section>
    </main>
  );
}
