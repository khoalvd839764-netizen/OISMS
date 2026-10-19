import { Bell, BellRing, CircleAlert, Volume2, VolumeX, Wifi, WifiOff } from "lucide-react";
import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { realtimeAdapter, type RealtimeEvent, type RealtimeStatus } from "../services/realtimeAdapter";
import { Badge, Button } from "./ui";

function playNotificationSound() {
  const AudioContextClass = window.AudioContext || (window as typeof window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
  if (!AudioContextClass) return;
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
}

export function NotificationCenter({ compact = false }: { compact?: boolean }) {
  const navigate = useNavigate();
  const [events, setEvents] = useState<RealtimeEvent[]>([{
    type: "inventory.low",
    id: "initial-low-stock",
    title: "SKU dưới ngưỡng tại Kho Quận 1",
    message: "BTL-750-GRN còn khả dụng 1, ngưỡng cảnh báo 3.",
    sku: "BTL-750-GRN",
    warehouse: "Kho Quận 1",
    href: "/inventory?sku=BTL-750-GRN&warehouse=kho-q1",
    createdAt: new Date().toISOString(),
  }]);
  const [status, setStatus] = useState<RealtimeStatus>("connected");
  const [open, setOpen] = useState(false);
  const [sound, setSound] = useState(() => window.localStorage.getItem("oism-notification-sound") !== "off");
  const [popup, setPopup] = useState<RealtimeEvent | null>(null);

  useEffect(() => {
    const unsubscribeEvent = realtimeAdapter.subscribe((event) => {
      setEvents((current) => [event, ...current].slice(0, 8));
      setPopup(event);
      if (sound) playNotificationSound();
      window.setTimeout(() => setPopup(null), 5000);
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
      <span className={`live-status live-status--${status}`} title={status === "connected" ? "Realtime đang kết nối" : status === "reconnecting" ? "Realtime đang kết nối lại" : "Realtime mất kết nối"}>{status === "connected" ? "Trực tuyến" : status === "reconnecting" ? "Đang nối lại" : "Mất kết nối"}</span>
      <button className="notification" aria-label="Mở thông báo" onClick={() => setOpen(!open)}><Bell size={20} />{events.length > 0 && <i />}</button>
      {open && <div className="notification-menu">
        <div className="notification-menu-head"><div><strong>Thông báo realtime</strong><span>Adapter mock · SignalR dự kiến</span></div><Button variant="ghost" onClick={toggleSound}>{sound ? <Volume2 size={17} /> : <VolumeX size={17} />}</Button></div>
        <div className={`connection-row connection-row--${status}`}>{status === "connected" ? <Wifi size={15} /> : <WifiOff size={15} />}<span>{status === "connected" ? "Đang kết nối" : status === "reconnecting" ? "Đang kết nối lại..." : "Mất kết nối"}</span></div>
        <div className="notification-items">{events.length === 0 ? <div className="notification-empty"><BellRing size={24} /><span>Chưa có sự kiện mới</span></div> : events.map((event) => <button key={event.id} onClick={() => openEvent(event)}><span className="notification-event-icon">{event.type === "inventory.low" ? <CircleAlert size={17} /> : <BellRing size={17} />}</span><div><strong>{event.title}</strong><p>{event.message}</p><small>{new Date(event.createdAt).toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit", timeZone: "Asia/Ho_Chi_Minh" })}</small></div></button>)}</div>
      </div>}
      {popup && <button className="realtime-popup" onClick={() => openEvent(popup)}><span><BellRing size={18} /></span><div><Badge tone="info">Sự kiện mới</Badge><strong>{popup.title}</strong><p>{popup.message}</p></div></button>}
    </div>
  );
}
