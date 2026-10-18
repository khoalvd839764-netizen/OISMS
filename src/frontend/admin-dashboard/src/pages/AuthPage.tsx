import {
  AlertCircle,
  ArrowRight,
  Building2,
  Check,
  Eye,
  EyeOff,
  LockKeyhole,
  Mail,
  Phone,
  ShieldCheck,
  Store,
  User,
} from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { Button, Input } from '../components/common/ui';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../components/feedback/Toast';
import { parseApiError } from '../api/client';

export function AuthPage() {
  const { isAuthenticated, role, login, registerTenant } = useAuth();
  const { showToast } = useToast();
  const navigate = useNavigate();
  const location = useLocation();

  const [isRegisterMode, setIsRegisterMode] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [errorMsg, setErrorMsg] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<string[]>([]);

  // Login fields
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [tenantCode, setTenantCode] = useState('');

  // Register Tenant fields
  const [tenantName, setTenantName] = useState('');
  const [regTenantCode, setRegTenantCode] = useState('');
  const [fullName, setFullName] = useState('');
  const [phoneNumber, setPhoneNumber] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');

  // Redirect if already authenticated
  if (isAuthenticated) {
    const fromPath = (location.state as { from?: { pathname?: string } })?.from?.pathname;
    if (role === 'Cashier') {
      return <Navigate to="/pos" replace />;
    }
    return <Navigate to={fromPath && fromPath !== '/login' ? fromPath : '/'} replace />;
  }

  const handleTenantNameChange = (value: string) => {
    setTenantName(value);
    if (!regTenantCode || regTenantCode === slugifyCode(tenantName)) {
      setRegTenantCode(slugifyCode(value));
    }
  };

  function slugifyCode(input: string): string {
    return input
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')
      .replace(/đ/gi, 'd')
      .toUpperCase()
      .replace(/[^A-Z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .slice(0, 24);
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setErrorMsg(null);
    setFieldErrors([]);

    if (!email.trim() || !password) {
      setErrorMsg('Vui lòng nhập đầy đủ Email và Mật khẩu.');
      return;
    }

    if (isRegisterMode) {
      if (!tenantName.trim() || !regTenantCode.trim() || !fullName.trim()) {
        setErrorMsg('Vui lòng điền đầy đủ Tên doanh nghiệp, Mã doanh nghiệp và Họ tên Owner.');
        return;
      }
      if (password.length < 6) {
        setErrorMsg('Mật khẩu phải có ít nhất 6 ký tự.');
        return;
      }
      if (password !== confirmPassword) {
        setErrorMsg('Mật khẩu xác nhận không khớp.');
        return;
      }
    }

    setLoading(true);
    try {
      if (isRegisterMode) {
        const authRes = await registerTenant({
          tenantName: tenantName.trim(),
          tenantCode: regTenantCode.trim().toUpperCase(),
          fullName: fullName.trim(),
          email: email.trim(),
          phoneNumber: phoneNumber.trim() || undefined,
          password,
        });
        showToast(
          'success',
          'Khởi tạo doanh nghiệp thành công!',
          `Chào mừng ${authRes.user.fullName} đến với ${authRes.user.tenantName}.`
        );
        navigate('/', { replace: true });
      } else {
        const authRes = await login({
          email: email.trim(),
          password,
          tenantCode: tenantCode.trim() ? tenantCode.trim().toUpperCase() : undefined,
        });
        showToast('success', 'Đăng nhập thành công', `Xin chào ${authRes.user.fullName}!`);
        const isCashierOnly =
          authRes.user.roles.includes('Cashier') &&
          !authRes.user.roles.includes('Owner') &&
          !authRes.user.roles.includes('Staff');
        const fromPath = (location.state as { from?: { pathname?: string } })?.from?.pathname;
        navigate(isCashierOnly ? '/pos' : fromPath || '/', { replace: true });
      }
    } catch (err) {
      const parsed = parseApiError(err);
      setErrorMsg(parsed.message);
      if (parsed.errors && parsed.errors.length > 1) {
        setFieldErrors(parsed.errors);
      }
      showToast('error', isRegisterMode ? 'Đăng ký thất bại' : 'Đăng nhập thất bại', parsed.message);
    } finally {
      setLoading(false);
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
          <span className="auth-kicker">VẬN HÀNH BÁN LẺ ĐA KÊNH</span>
          <h1>Một nguồn dữ liệu tin cậy cho bán hàng và tồn kho.</h1>
          <p>
            Quản lý danh mục, biến thể SKU, phiếu nhập kho nhiều dòng, sổ kho bất biến và giá vốn bình quân
            gia quyền (WAC) trong cùng một hệ thống đa chi nhánh được phân quyền rõ ràng.
          </p>
          <ul>
            <li>
              <Check size={16} />
              Tồn kho theo từng SKU và từng kho vật lý (OnHand, Reserved, Available)
            </li>
            <li>
              <Check size={16} />
              Sổ kho bất biến (Append-Only Ledger), truy vết đầy đủ chứng từ gốc
            </li>
            <li>
              <Check size={16} />
              Bảo mật đa thuê bao (Multi-tenant JWT) & phân quyền RBAC (Owner / Staff / Cashier)
            </li>
          </ul>
        </div>
        <small>Hệ thống quản trị OISM · Múi giờ Asia/Ho_Chi_Minh</small>
      </section>

      <section className="auth-form-wrap">
        <form className="auth-form" onSubmit={handleSubmit} noValidate>
          <div className="auth-mobile-logo">
            <div className="brand-mark">O</div>
            <strong>OISM</strong>
          </div>
          <span className="auth-icon">
            <ShieldCheck size={23} />
          </span>
          <h2>{isRegisterMode ? 'Tạo doanh nghiệp mới (Tenant)' : 'Đăng nhập vào OISM'}</h2>
          <p>
            {isRegisterMode
              ? 'Khởi tạo không gian dữ liệu độc lập cùng tài khoản Owner quản trị toàn quyền.'
              : 'Đăng nhập bằng tài khoản doanh nghiệp của bạn để tiếp tục vận hành.'}
          </p>

          {errorMsg && (
            <div
              role="alert"
              style={{
                display: 'flex',
                gap: '10px',
                alignItems: 'flex-start',
                padding: '12px 14px',
                borderRadius: '10px',
                background: '#FEF2F2',
                border: '1px solid #FECACA',
                color: '#991B1B',
                fontSize: '13px',
                marginBottom: '14px',
              }}
            >
              <AlertCircle size={18} style={{ flexShrink: 0, marginTop: '1px' }} />
              <div>
                <strong style={{ display: 'block', marginBottom: fieldErrors.length > 0 ? '4px' : 0 }}>
                  {errorMsg}
                </strong>
                {fieldErrors.length > 0 && (
                  <ul style={{ margin: 0, paddingLeft: '16px' }}>
                    {fieldErrors.map((errText, idx) => (
                      <li key={idx}>{errText}</li>
                    ))}
                  </ul>
                )}
              </div>
            </div>
          )}

          {isRegisterMode && (
            <>
              <label>
                <span>Tên doanh nghiệp / chuỗi cửa hàng *</span>
                <div className="auth-input">
                  <Store size={17} />
                  <Input
                    placeholder="Ví dụ: OISM Fashion Studio"
                    value={tenantName}
                    onChange={(e) => handleTenantNameChange(e.target.value)}
                    required
                  />
                </div>
              </label>

              <label>
                <span>Mã định danh doanh nghiệp (Tenant Code) *</span>
                <div className="auth-input">
                  <Building2 size={17} />
                  <Input
                    placeholder="Ví dụ: OISM-STUDIO"
                    value={regTenantCode}
                    onChange={(e) => setRegTenantCode(e.target.value.toUpperCase())}
                    required
                  />
                </div>
              </label>

              <label>
                <span>Họ và tên Chủ sở hữu (Owner) *</span>
                <div className="auth-input">
                  <User size={17} />
                  <Input
                    placeholder="Nguyễn Văn An"
                    value={fullName}
                    onChange={(e) => setFullName(e.target.value)}
                    required
                  />
                </div>
              </label>
            </>
          )}

          <label>
            <span>Email đăng nhập *</span>
            <div className="auth-input">
              <Mail size={17} />
              <Input
                type="email"
                placeholder="owner@cuahang.vn"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                autoComplete="email"
                required
              />
            </div>
          </label>

          {isRegisterMode ? (
            <label>
              <span>Số điện thoại liên hệ</span>
              <div className="auth-input">
                <Phone size={17} />
                <Input
                  type="tel"
                  placeholder="0901234567"
                  value={phoneNumber}
                  onChange={(e) => setPhoneNumber(e.target.value)}
                />
              </div>
            </label>
          ) : (
            <label>
              <span>Mã doanh nghiệp (Tenant Code - Không bắt buộc)</span>
              <div className="auth-input">
                <Building2 size={17} />
                <Input
                  placeholder="Để trống nếu email chỉ thuộc 1 doanh nghiệp"
                  value={tenantCode}
                  onChange={(e) => setTenantCode(e.target.value.toUpperCase())}
                />
              </div>
            </label>
          )}

          <label>
            <span>Mật khẩu *</span>
            <div className="auth-input">
              <LockKeyhole size={17} />
              <Input
                type={showPassword ? 'text' : 'password'}
                placeholder="Nhập mật khẩu"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete={isRegisterMode ? 'new-password' : 'current-password'}
                required
              />
              <button
                type="button"
                onClick={() => setShowPassword(!showPassword)}
                style={{
                  background: 'none',
                  border: 'none',
                  padding: 0,
                  cursor: 'pointer',
                  color: 'inherit',
                  display: 'flex',
                }}
                aria-label={showPassword ? 'Ẩn mật khẩu' : 'Hiện mật khẩu'}
              >
                {showPassword ? <EyeOff size={17} /> : <Eye size={17} />}
              </button>
            </div>
          </label>

          {isRegisterMode && (
            <label>
              <span>Xác nhận mật khẩu *</span>
              <div className="auth-input">
                <LockKeyhole size={17} />
                <Input
                  type={showPassword ? 'text' : 'password'}
                  placeholder="Nhập lại mật khẩu"
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  autoComplete="new-password"
                  required
                />
              </div>
            </label>
          )}

          <Button type="submit" className="auth-submit" disabled={loading}>
            {loading
              ? 'Đang xác thực với máy chủ...'
              : isRegisterMode
              ? 'Khởi tạo doanh nghiệp'
              : 'Đăng nhập hệ thống'}
            <ArrowRight size={17} />
          </Button>

          <div className="auth-switch">
            {isRegisterMode ? 'Đã có tài khoản doanh nghiệp?' : 'Chưa có không gian doanh nghiệp?'}
            <Button
              type="button"
              variant="ghost"
              onClick={() => {
                setIsRegisterMode(!isRegisterMode);
                setErrorMsg(null);
                setFieldErrors([]);
              }}
            >
              {isRegisterMode ? 'Quay lại Đăng nhập' : 'Đăng ký doanh nghiệp mới'}
            </Button>
          </div>
        </form>
      </section>
    </main>
  );
}
