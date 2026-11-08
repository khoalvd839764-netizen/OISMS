import { useEffect, useRef } from 'react';
import { useAuth } from '../context/AuthContext';
import { openStandalonePos, POS_URL } from '../services/posNavigation';

export function PosRedirectPage() {
  const { user, isLoading, logout } = useAuth();
  const started = useRef(false);
  useEffect(() => {
    if (isLoading || started.current) return;
    started.current = true;
    void openStandalonePos(user?.role, logout);
  }, [isLoading, user?.role, logout]);
  return <main className="access-page"><div className="access-card"><h1>Đang mở quầy POS</h1><p>Quầy bán hàng chạy trong ứng dụng riêng.</p><a href={POS_URL}>Mở POS</a></div></main>;
}
