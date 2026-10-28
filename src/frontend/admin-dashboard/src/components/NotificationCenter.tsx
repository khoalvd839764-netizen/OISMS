import { Bell, BellRing, CircleAlert, Volume2, VolumeX, Wifi, WifiOff } from "lucide-react";
import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { realtimeAdapter, type RealtimeEvent, type RealtimeStatus } from "../services/realtimeAdapter";
import { Badge, Button } from "./ui";

function playNotificationSound() {
  const AudioContextClass = window.AudioContext || (window as typeof window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
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
  } catch (err) {
    console.debug("Audio autoplay prevented or error:", err);
  }
}

export function NotificationCenter({ compact = false }: { compact?: boolean }) {
  const navigate = useNavigate();
  const [events, setEvents] = useState<RealtimeEvent[]>([]);
  const [status, setStatus] = useState<RealtimeStatus>(() => realtimeAdapter.getStatus());
  const [open, setOpen] = useState(false);
  const [sound, setSound] = useState(() => window.localStorage.getItem("oism-notification-sound") !== "off");
  const [popup, setPopup] = useState<RealtimeEvent | null>(null);

  useEffect(() => {
    // Khởi động kết nối SignalR với token hiện thời
    realtimeAdapter.start();

    const unsubscribeEvent = realtimeAdapter.subscribe((event) => {
      setEvents((current) => [event, ...current].slice(0, 15));
      setPopup(event);
      if (sound) playNotificationSound();
      window.setTimeout(() => setPopup(null), 6000);
    });

    const unsubscribeStatus = realtimeAdapter.subscribeStatus(setStatus);

    return () => {
      unsubscribeEvent();
      unsubscribeStatus();
    };
  }, [sound]);

  const toggleSound = () => {
    const next = !sound;
    setSound(next);
    window.localStorage.setItem("oism-notification-sound", next ? "on" : "off");
  };

  const openEvent = (event: RealtimeEvent) => {
    setOpen(false);
    setPopup(null);
    navigate(event.href);
  };

  return (
    <div className={`notification-center ${compact ? "notification-center--compact" : ""}`}>
      <span
        className={`live-status live-status--${status}`}
        title={
          status === "connected"
            ? "Realtime SignalR đang kết nối"
            : status === "reconnecting"
            ? "Realtime SignalR đang kết nối lại"
            : "Realtime SignalR mất kết nối"
        }
      >
        {status === "connected" ? "Trực tuyến" : status === "reconnecting" ? "Đang nối lại" : "Mất kết nối"}
      </span>
      <button className="notification" aria-label="Mở thông báo" onClick={() => setOpen(!open)}>
        <Bell size={20} />
        {events.length > 0 && <i />}
      </button>
      {open && (
        <div className="notification-menu">
          <div className="notification-menu-head">
            <div>
              <strong>Thông báo thời gian thực</strong>
              <span>SignalR Hub (/hubs/notifications)</span>
            </div>
            <Button variant="ghost" onClick={toggleSound}>
              {sound ? <Volume2 size={17} /> : <VolumeX size={17} />}
            </Button>
          </div>
          <div className={`connection-row connection-row--${status}`}>
            {status === "connected" ? <Wifi size={15} /> : <WifiOff size={15} />}
            <span>
              {status === "connected"
                ? "Đang kết nối trực tiếp"
                : status === "reconnecting"
                ? "Đang kết nối lại..."
                : "Mất kết nối"}
            </span>
          </div>
          <div className="notification-items">
            {events.length === 0 ? (
              <div className="notification-empty">
                <BellRing size={24} />
                <span>Chưa có sự kiện mới</span>
              </div>
            ) : (
              events.map((event) => (
                <button key={event.id} onClick={() => openEvent(event)}>
                  <span className="notification-event-icon">
                    {event.type === "inventory.low" ? <CircleAlert size={17} /> : <BellRing size={17} />}
                  </span>
                  <div>
                    <strong>{event.title}</strong>
                    <p>{event.message}</p>
                    <small>
                      {new Date(event.createdAt).toLocaleTimeString("vi-VN", {
                        hour: "2-digit",
                        minute: "2-digit",
                        second: "2-digit",
                        timeZone: "Asia/Ho_Chi_Minh",
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
        <button className="realtime-popup" onClick={() => openEvent(popup)}>
          <span>
            <BellRing size={18} />
          </span>
          <div>
            <Badge tone={popup.type === "inventory.low" ? "warning" : "info"}>Sự kiện mới</Badge>
            <strong>{popup.title}</strong>
            <p>{popup.message}</p>
          </div>
        </button>
      )}
    </div>
  );
}
