import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { registerRealtimeAdapter } from './services/api'
import {
  startRealtimeConnection,
  stopRealtimeConnection,
  switchBranch,
  subscribeRealtimeStatus,
  subscribeOrderCreated,
  subscribeLowStockWarning,
  isSoundMuted,
  setSoundMuted,
  playNotificationSound,
} from './services/realtimeService'

// Đăng ký Realtime SignalR Adapter chính thức cho ứng dụng POS
registerRealtimeAdapter({
  start: startRealtimeConnection,
  stop: stopRealtimeConnection,
  switchBranch,
  subscribeStatus: subscribeRealtimeStatus,
  subscribeOrder: subscribeOrderCreated,
  subscribeWarning: subscribeLowStockWarning,
  isMuted: isSoundMuted,
  setMuted: setSoundMuted,
  playNotificationSound,
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
