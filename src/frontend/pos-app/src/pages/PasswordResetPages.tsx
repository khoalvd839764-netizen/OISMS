import { useEffect, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { KeyRound } from 'lucide-react';
import { getPasswordResetEnabled, requestPosPasswordReset, resetPosPassword, takeResetTokenFromUrl } from '../services/posPasswordReset';
import './LoginPage.css';

// Upgrade 10: trang Quên mật khẩu và Đặt mật khẩu mới riêng của POS.

export function ForgotPasswordPage() {
  const [identifier, setIdentifier] = useState('');
  const [enabled, setEnabled] = useState<boolean | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [sent, setSent] = useState('');
  useEffect(() => { getPasswordResetEnabled().then(setEnabled).catch(() => setEnabled(null)); }, []);
  async function submit(event: FormEvent) {
    event.preventDefault(); if (busy) return; setBusy(true); setError('');
    try { setSent(await requestPosPasswordReset(identifier.trim())); } catch (e) { setError(e instanceof Error ? e.message : 'Không gửi được yêu cầu.'); } finally { setBusy(false); }
  }
  return <main className="pos-login"><form onSubmit={submit}><span className="pos-login-icon"><KeyRound size={28} /></span><h1>Quên mật khẩu</h1>
    <p>Nhập email hoặc số điện thoại của tài khoản. Link đặt lại sẽ được gửi vào email của tài khoản đó.</p>
    {enabled === false && <p className="pos-login-notice" role="status">Tự đặt lại mật khẩu chưa được bật. Vui lòng nhờ chủ cửa hàng đặt lại mật khẩu cho bạn.</p>}
    {sent ? <p className="pos-login-notice" role="status">{sent}</p> : <>
      <label>Email hoặc số điện thoại<input autoComplete="username" required value={identifier} disabled={enabled === false} onChange={e => setIdentifier(e.target.value)} /></label>
      {error && <p role="alert">{error}</p>}
      <button disabled={busy || enabled === false} type="submit">{busy ? 'Đang gửi...' : 'Gửi link đặt lại mật khẩu'}</button>
    </>}
    <Link to="/login">Quay lại đăng nhập</Link></form></main>;
}

export function ResetPasswordPage() {
  const [token] = useState(() => takeResetTokenFromUrl());
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [done, setDone] = useState(false);
  async function submit(event: FormEvent) {
    event.preventDefault(); if (busy) return; setError('');
    if (password.length < 6 || password.length > 100) { setError('Mật khẩu mới phải từ 6 đến 100 ký tự.'); return; }
    if (password !== confirm) { setError('Mật khẩu xác nhận không khớp.'); return; }
    setBusy(true);
    try { await resetPosPassword(token, password); setDone(true); } catch (e) { setError(e instanceof Error ? e.message : 'Đặt lại mật khẩu thất bại.'); } finally { setBusy(false); }
  }
  if (!token) return <main className="pos-login"><form><span className="pos-login-icon"><KeyRound size={28} /></span><h1>Link không hợp lệ</h1>
    <p role="alert">Link đặt lại mật khẩu bị thiếu hoặc đã được mở trước đó. Vui lòng mở lại link trong email.</p>
    <Link to="/forgot-password">Yêu cầu link mới</Link><Link to="/login">Quay lại đăng nhập</Link></form></main>;
  return <main className="pos-login"><form onSubmit={submit}><span className="pos-login-icon"><KeyRound size={28} /></span><h1>Đặt mật khẩu mới</h1>
    <p>Sau khi đổi, mọi phiên đăng nhập cũ sẽ phải đăng nhập lại.</p>
    {done ? <p className="pos-login-notice" role="status">Đặt lại mật khẩu thành công. Vui lòng đăng nhập bằng mật khẩu mới.</p> : <>
      <label>Mật khẩu mới<input autoComplete="new-password" type="password" required value={password} onChange={e => setPassword(e.target.value)} /></label>
      <label>Xác nhận mật khẩu mới<input autoComplete="new-password" type="password" required value={confirm} onChange={e => setConfirm(e.target.value)} /></label>
      {error && <p role="alert">{error}{error.startsWith('Link') && <> <Link to="/forgot-password">Yêu cầu link mới</Link></>}</p>}
      <button disabled={busy} type="submit">{busy ? 'Đang lưu...' : 'Đặt mật khẩu mới'}</button>
    </>}
    <Link to="/login">{done ? 'Đăng nhập' : 'Quay lại đăng nhập'}</Link></form></main>;
}
