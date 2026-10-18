import { Bell, BellRing, CircleAlert, Volume2, VolumeX, Wifi, WifiOff } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Badge, Button } from '../common/ui';

export interface NotificationItem {
  id: string;
  type: 'inventory.low' | 'order.created' | 'receipt.confirmed';
  title: string;
  message: string;
  sku?: string;
  warehouse?: string;
  href: string;
  createdAt: string;
}

function playNotificationSound() {
  const AudioContextClass =
    window.AudioContext ||
    (window as typeof window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
  if (!AudioContextClass) return;
  try {
    const context = new AudioContextClass();
    const oscillator = context.createOscillator();
    const gain = context.createGain();
    oscillator.frequency.value = 720;
    gain.gain.setValueAtTime(0.08, context.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.001, context.currentTime + 0.18);
    oscillator.connect(gain);
    gain.connect(context.destination);
    oscillator.start();
    oscillator.stop(context.currentTime + 0.18);
  } catch {
    // Ignore audio autoplay restrictions
  }
}

export function NotificationCenter({ compact = false }: { compact?: boolean }) {
  const navigate = useNavigate();
  const [events, setEvents] = useState<NotificationItem[]>([]);
  const [status, setStatus] = useState<'connected' | 'reconnecting' | 'disconnected'>(() =>
    typeof navigator !== 'undefined' && !navigator.onLine ? 'disconnected' : 'connected'
  );
  const [open, setOpen] = useState(false);
  const [sound, setSound] = useState(() => localStorage.getItem('oism_notification_sound') !== 'off');
  const [popup, setPopup] = useState<NotificationItem | null>(null);

  useEffect(() => {
    const handleOnline = () => setStatus('connected');
    const handleOffline = () => setStatus('disconnected');
    const handleNetErr = () => setStatus('reconnecting');

    window.addEventListener('online', handleOnline);
    window.addEventListener('offline', handleOffline);
    window.addEventListener('oism:network-error', handleNetErr);
    return () => {
      window.removeEventListener('online', handleOnline);
      window.removeEventListener('offline', handleOffline);
      window.removeEventListener('oism:network-error', handleNetErr);
    };
  }, []);

  useEffect(() => {
    const handleNotification = (e: Event) => {
      const customEvent = e as CustomEvent<NotificationItem>;
      const item = customEvent.detail;
      if (!item) return;

      setEvents((current) => [item, ...current].slice(0, 10));
      setPopup(item);
      if (sound) playNotificationSound();
      setTimeout(() => setPopup(null), 5000);
    };

    window.addEventListener('oism:notification', handleNotification);
    return () => {
      window.removeEventListener('oism:notification', handleNotification);
    };
  }, [sound]);

  const toggleSound = () => {
    const next = !sound;
    setSound(next);
    localStorage.setItem('oism_notification_sound', next ? 'on' : 'off');
  };

  const openEvent = (event: NotificationItem) => {
    setOpen(false);
    setPopup(null);
    navigate(event.href);
  };

  return (
    <div className={`notification-center ${compact ? 'notification-center--compact' : ''}`}>
      <span
        className={`live-status live-status--${status}`}
        title={
          status === 'connected'
            ? 'Realtime đang kết nối'
            : status === 'reconnecting'
            ? 'Realtime đang kết nối lại'
            : 'Realtime mất kết nối'
        }
      >
        {status === 'connected' ? 'Trực tuyến' : status === 'reconnecting' ? 'Đang nối lại' : 'Mất kết nối'}
      </span>
      <button
        type="button"
        className="notification"
        aria-label="Mở thông báo"
        onClick={() => setOpen(!open)}
      >
        <Bell size={20} />
        {events.length > 0 && <i />}
      </button>

      {open && (
        <div className="notification-menu">
          <div className="notification-menu-head">
            <div>
              <strong>Thông báo hệ thống</strong>
              <span>Biến động kho & đơn hàng</span>
            </div>
            <Button variant="ghost" onClick={toggleSound} title="Bật/Tắt chuông báo">
              {sound ? <Volume2 size={17} /> : <VolumeX size={17} />}
            </Button>
          </div>
          <div className={`connection-row connection-row--${status}`}>
            {status === 'connected' ? <Wifi size={15} /> : <WifiOff size={15} />}
            <span>
              {status === 'connected'
                ? 'Đang kết nối'
                : status === 'reconnecting'
                ? 'Đang kết nối lại...'
                : 'Mất kết nối'}
            </span>
          </div>
          <div className="notification-items">
            {events.length === 0 ? (
              <div className="notification-empty">
                <BellRing size={24} />
                <span>Chưa có thông báo mới</span>
              </div>
            ) : (
              events.map((event) => (
                <button key={event.id} onClick={() => openEvent(event)} type="button">
                  <span className="notification-event-icon">
                    {event.type === 'inventory.low' ? <CircleAlert size={17} /> : <BellRing size={17} />}
                  </span>
                  <div>
                    <strong>{event.title}</strong>
                    <p>{event.message}</p>
                    <small>
                      {new Date(event.createdAt).toLocaleTimeString('vi-VN', {
                        hour: '2-digit',
                        minute: '2-digit',
                      })}
                    </small>
                  </div>
                </button>
              ))
            )}
          </div>
        </div>
      )}

      {popup && (
        <button className="realtime-popup" onClick={() => openEvent(popup)} type="button">
          <span>
            <BellRing size={18} />
          </span>
          <div>
            <Badge tone="info">Sự kiện mới</Badge>
            <strong>{popup.title}</strong>
            <p>{popup.message}</p>
          </div>
        </button>
      )}
    </div>
  );
}
