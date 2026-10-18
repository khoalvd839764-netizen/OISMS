import { useEffect, useState } from 'react';
import { WifiOff, AlertTriangle } from 'lucide-react';

export function NetworkBanner() {
  const [isOnline, setIsOnline] = useState(navigator.onLine);
  const [networkErrorMsg, setNetworkErrorMsg] = useState<string | null>(null);

  useEffect(() => {
    const handleOnline = () => {
      setIsOnline(true);
      setNetworkErrorMsg(null);
    };

    const handleOffline = () => {
      setIsOnline(false);
    };

    const handleCustomNetworkError = (e: Event) => {
      const customEvent = e as CustomEvent<{ message?: string }>;
      setNetworkErrorMsg(customEvent.detail?.message || 'Không thể kết nối đến máy chủ backend.');
      window.setTimeout(() => setNetworkErrorMsg(null), 8000);
    };

    window.addEventListener('online', handleOnline);
    window.addEventListener('offline', handleOffline);
    window.addEventListener('oism:network-error', handleCustomNetworkError);

    return () => {
      window.removeEventListener('online', handleOnline);
      window.removeEventListener('offline', handleOffline);
      window.removeEventListener('oism:network-error', handleCustomNetworkError);
    };
  }, []);

  if (!isOnline) {
    return (
      <div
        style={{
          backgroundColor: '#c93c48',
          color: '#ffffff',
          padding: '8px 16px',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          gap: 10,
          fontWeight: 600,
          fontSize: 13,
          position: 'sticky',
          top: 0,
          zIndex: 9999,
        }}
      >
        <WifiOff size={18} />
        <span>Bạn đang ngoại tuyến. Vui lòng kiểm tra lại kết nối mạng Internet.</span>
      </div>
    );
  }

  if (networkErrorMsg) {
    return (
      <div
        style={{
          backgroundColor: '#b76e00',
          color: '#ffffff',
          padding: '8px 16px',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          gap: 10,
          fontWeight: 600,
          fontSize: 13,
          position: 'sticky',
          top: 0,
          zIndex: 9998,
        }}
      >
        <AlertTriangle size={18} />
        <span>{networkErrorMsg}</span>
      </div>
    );
  }

  return null;
}
