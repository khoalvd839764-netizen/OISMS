import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { PosAuthProvider, useAuth } from './context/PosAuthContext';
import { LoginPage } from './pages/LoginPage';
import { PosPage } from './pages/PosPage';
function ProtectedPos() {
  const { user, loading, error, retry, logout } = useAuth();
  if (loading) return <main className="pos-login" role="status">Đang xác thực phiên POS...</main>;
  if (error) return <main className="pos-login"><div role="alert"><p>{error}</p><button onClick={retry}>Thử lại</button><button onClick={() => void logout()}>Đăng nhập lại</button></div></main>;
  return user ? <PosPage key={user.tenantId + ':' + user.id} /> : <Navigate to="/login" replace />;
}
export default function App() { return <PosAuthProvider><BrowserRouter><Routes>
  <Route path="/login" element={<LoginPage />} /><Route path="/" element={<ProtectedPos />} />
  <Route path="/pos" element={<Navigate to="/" replace />} /><Route path="*" element={<Navigate to="/" replace />} />
</Routes></BrowserRouter></PosAuthProvider>; }
