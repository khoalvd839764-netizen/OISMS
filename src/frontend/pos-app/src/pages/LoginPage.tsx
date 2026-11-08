import { useState, type FormEvent } from 'react';
import { Navigate } from 'react-router-dom';
import { Store } from 'lucide-react';
import { useAuth } from '../context/PosAuthContext';
import { ADMIN_URL } from '../services/appUrls';
import './LoginPage.css';

export function LoginPage() {
  const { user, login, loading } = useAuth();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  if (user) return <Navigate to="/" replace />;
  async function submit(event: FormEvent) { event.preventDefault(); if (busy) return; setBusy(true); setError(''); try { await login(email.trim(), password); } catch (e) { setError(e instanceof Error ? e.message : 'Không thể đăng nhập.'); } finally { setBusy(false); } }
  return <main className="pos-login"><form onSubmit={submit}><span className="pos-login-icon"><Store size={28} /></span><h1>Đăng nhập OISMS POS</h1><p>Quầy bán hàng dành cho Owner, Staff và Cashier.</p>
    <label>Email<input autoComplete="username" type="email" required value={email} onChange={e => setEmail(e.target.value)} /></label>
    <label>Mật khẩu<input autoComplete="current-password" type="password" required value={password} onChange={e => setPassword(e.target.value)} /></label>
    {error && <p role="alert">{error}</p>}<button disabled={busy || loading} type="submit">{busy ? 'Đang đăng nhập...' : 'Đăng nhập'}</button>
    <a href={`${ADMIN_URL}/login`}>Mở trang quản trị</a></form></main>;
}
