import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  HttpTransportType,
  LogLevel,
} from '@microsoft/signalr';
import type {
  PosUser,
  OrderSummaryRealtimeDto,
  StockChangedRealtimeDto,
  LowStockAlertRealtimeDto,
  RealtimeConnectionStatus,
} from '../types/pos';
import { getAuthToken, invalidateAndRefetchStock, setAuthoritativeStock, refreshBranchStock } from './api';

const API_BASE_URL = 'http://localhost:5000';
const HUB_URL = `${API_BASE_URL}/hubs/notifications`;

let hubConnection: HubConnection | null = null;
let activeStartingConnection: HubConnection | null = null;
let currentGeneration: number = 0;
let activeBranchId: string = '';
let activeUser: Partial<PosUser> | null = null;
let currentStatus: RealtimeConnectionStatus = 'disconnected';
let isMuted: boolean = false;

// Audio context singleton for sound alerts (graceful fail-safe)
let audioCtx: AudioContext | null = null;

export function isSoundMuted(): boolean {
  return isMuted;
}

export function setSoundMuted(muted: boolean): void {
  isMuted = muted;
}

/**
 * Phát âm thanh thông báo chuông dịu nhẹ (chime) bằng Web Audio API thuần túy.
 * Không cần tải thêm file mp3 ngoại vi, không bao giờ gây crash ứng dụng nếu browser chặn âm thanh.
 */
export function playNotificationSound(): void {
  if (isMuted) return;
  try {
    const AudioContextClass = window.AudioContext || (window as any).webkitAudioContext;
    if (!AudioContextClass) return;

    if (!audioCtx) {
      audioCtx = new AudioContextClass();
    }

    if (audioCtx.state === 'suspended') {
      audioCtx.resume().catch(() => {});
    }

    const osc = audioCtx.createOscillator();
    const gain = audioCtx.createGain();

    osc.type = 'sine';
    const now = audioCtx.currentTime;
    osc.frequency.setValueAtTime(659.25, now); // E5
    osc.frequency.exponentialRampToValueAtTime(880.0, now + 0.12); // A5

    gain.gain.setValueAtTime(0.15, now);
    gain.gain.exponentialRampToValueAtTime(0.001, now + 0.35);

    osc.connect(gain);
    gain.connect(audioCtx.destination);

    osc.start(now);
    osc.stop(now + 0.35);
  } catch (_err) {
    // Tự động bỏ qua lỗi âm thanh do chính sách autoplay của trình duyệt
  }
}

// Listeners registry
type StatusListener = (status: RealtimeConnectionStatus) => void;
type OrderListener = (order: OrderSummaryRealtimeDto) => void;
type StockListener = (stock: StockChangedRealtimeDto) => void;
type WarningListener = (warning: LowStockAlertRealtimeDto) => void;
type ReconnectedListener = () => void;

const statusListeners = new Set<StatusListener>();
const orderListeners = new Set<OrderListener>();
const stockListeners = new Set<StockListener>();
const warningListeners = new Set<WarningListener>();
const reconnectedListeners = new Set<ReconnectedListener>();

function setStatus(status: RealtimeConnectionStatus) {
  currentStatus = status;
  for (const listener of statusListeners) {
    try {
      listener(status);
    } catch (err) {
      console.error('Lỗi listener status realtime:', err);
    }
  }
}

export function getRealtimeStatus(): RealtimeConnectionStatus {
  if (!hubConnection) return currentStatus;
  switch (hubConnection.state) {
    case HubConnectionState.Connected:
      return 'connected';
    case HubConnectionState.Connecting:
      return 'connecting';
    case HubConnectionState.Reconnecting:
      return 'reconnecting';
    case HubConnectionState.Disconnected:
    default:
      return 'disconnected';
  }
}

export function subscribeRealtimeStatus(listener: StatusListener): () => void {
  statusListeners.add(listener);
  listener(getRealtimeStatus());
  return () => {
    statusListeners.delete(listener);
  };
}

export function subscribeOrderCreated(listener: OrderListener): () => void {
  orderListeners.add(listener);
  return () => {
    orderListeners.delete(listener);
  };
}

export function subscribeStockLevelChanged(listener: StockListener): () => void {
  stockListeners.add(listener);
  return () => {
    stockListeners.delete(listener);
  };
}

export function subscribeLowStockWarning(listener: WarningListener): () => void {
  warningListeners.add(listener);
  return () => {
    warningListeners.delete(listener);
  };
}

export function subscribeReconnected(listener: ReconnectedListener): () => void {
  reconnectedListeners.add(listener);
  return () => {
    reconnectedListeners.delete(listener);
  };
}

function normalizeOrderPayload(payload: any): OrderSummaryRealtimeDto | null {
  if (!payload || typeof payload !== 'object') return null;
  return {
    orderId: String(payload.orderId || payload.OrderId || ''),
    orderNumber: String(payload.orderNumber || payload.OrderNumber || ''),
    branchId: String(payload.branchId || payload.BranchId || ''),
    branchName: payload.branchName || payload.BranchName || null,
    channel: String(payload.channel || payload.Channel || ''),
    status: String(payload.status || payload.Status || ''),
    totalAmount: Number(payload.totalAmount ?? payload.TotalAmount ?? 0),
    totalItems: Number(payload.totalItems ?? payload.TotalItems ?? 0),
    customerName: payload.customerName || payload.CustomerName || null,
    createdAt: String(payload.createdAt || payload.CreatedAt || new Date().toISOString()),
  };
}

function normalizeStockPayload(payload: any): StockChangedRealtimeDto | null {
  if (!payload || typeof payload !== 'object') return null;
  return {
    branchId: String(payload.branchId || payload.BranchId || ''),
    skuId: String(payload.skuId || payload.SkuId || ''),
    newAvailableStock: Number(payload.newAvailableStock ?? payload.NewAvailableStock ?? 0),
    timestamp: String(payload.timestamp || payload.Timestamp || new Date().toISOString()),
  };
}

function normalizeWarningPayload(payload: any): LowStockAlertRealtimeDto | null {
  if (!payload || typeof payload !== 'object') return null;
  return {
    branchId: String(payload.branchId || payload.BranchId || ''),
    skuId: String(payload.skuId || payload.SkuId || ''),
    skuCode: String(payload.skuCode || payload.SkuCode || ''),
    currentAvailable: Number(payload.currentAvailable ?? payload.CurrentAvailable ?? 0),
    threshold: Number(payload.threshold ?? payload.Threshold ?? 0),
    alertedAt: String(payload.alertedAt || payload.AlertedAt || new Date().toISOString()),
  };
}

/**
 * Khởi tạo hoặc cập nhật kết nối SignalR Hub NotificationHub (/hubs/notifications).
 * Tuân thủ nghiêm ngặt các quy tắc T03-AC-01 -> T03-AC-05:
 * 1. Token JWT hợp lệ của vai trò Owner/Staff/Cashier.
 * 2. Token query string chỉ gửi cho Hub path (SignalR client tự gắn), không lộ ra log/REST.
 * 3. Chống trùng connection / listeners khi mount/unmount hoặc StrictMode.
 * 4. Tự động rejoin branch group sau khi reconnect (onreconnected).
 * 5. Bỏ qua callback từ phiên cũ hoặc khác tenant/branch.
 */
export async function startRealtimeConnection(user: PosUser | null, branchId: string): Promise<void> {
  if (!user) {
    await stopRealtimeConnection();
    return;
  }

  const token = getAuthToken();
  if (!token) {
    await stopRealtimeConnection();
    return;
  }

  // Nếu đã kết nối cùng user, cùng tenant:
  if (
    hubConnection &&
    hubConnection.state === HubConnectionState.Connected &&
    activeUser &&
    activeUser.id === user.id &&
    activeUser.tenantId === user.tenantId
  ) {
    // Nếu đổi chi nhánh: hủy đăng ký chi nhánh cũ và tham gia chi nhánh mới
    if (branchId && branchId !== activeBranchId) {
      await switchBranch(branchId);
    }
    return;
  }

  // B02-F01: Tăng thế hệ phiên (generation) để lập tức vô hiệu hóa kết nối/callback của phiên trước
  currentGeneration++;
  const sessionGen = currentGeneration;

  // Lập tức dừng và thu hồi mọi kết nối cũ hoặc kết nối đang dở dang
  const prevConn = hubConnection;
  const prevStarting = activeStartingConnection;
  hubConnection = null;
  activeStartingConnection = null;

  if (prevConn) {
    try {
      if (activeBranchId && prevConn.state === HubConnectionState.Connected) {
        prevConn.invoke('LeaveBranchGroup', activeBranchId).catch(() => {});
      }
      prevConn.stop().catch(() => {});
    } catch {}
  }
  if (prevStarting) {
    try {
      prevStarting.stop().catch(() => {});
    } catch {}
  }

  activeUser = { ...user };
  activeBranchId = branchId;

  setStatus('connecting');

  const builder = new HubConnectionBuilder()
    .withUrl(HUB_URL, {
      accessTokenFactory: () => getAuthToken() || '',
      transport:
        HttpTransportType.WebSockets |
        HttpTransportType.ServerSentEvents |
        HttpTransportType.LongPolling,
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Warning);

  const conn = builder.build();
  activeStartingConnection = conn;

  // Vòng đời kết nối được bảo vệ bởi sessionGen
  conn.onreconnecting(() => {
    if (sessionGen !== currentGeneration) return;
    setStatus('reconnecting');
  });

  conn.onreconnected(async (_connectionId) => {
    if (sessionGen !== currentGeneration) return;
    setStatus('connected');
    // T03-AC-01 & T03-AC-05: Khi reconnect có connectionId mới, bắt buộc re-join branch group hiện hành
    if (activeBranchId) {
      try {
        await conn.invoke('JoinBranchGroup', activeBranchId);
      } catch (err) {
        console.error('Lỗi khi rejoin chi nhánh sau reconnect:', err);
      }
      // B02-F04: Khôi phục refetch sau reconnect cho SKU liên quan view/cart của đúng branch
      try {
        await refreshBranchStock(activeBranchId);
      } catch (err) {
        console.error('Lỗi refreshBranchStock sau reconnect:', err);
      }
    }
    // Kích hoạt callback refetch stock cho các subscriber
    for (const cb of reconnectedListeners) {
      try {
        cb();
      } catch (e) {
        console.error('Lỗi reconnectedListener:', e);
      }
    }
  });

  conn.onclose(() => {
    if (sessionGen !== currentGeneration) return;
    setStatus('disconnected');
  });

  // Lắng nghe các sự kiện nghiệp vụ từ NotificationHub
  // 1. OrderCreated: Đơn mới từ Admin/Sàn (nhóm tenant_{tenantId})
  conn.on('OrderCreated', (rawPayload) => {
    if (sessionGen !== currentGeneration) return;
    const order = normalizeOrderPayload(rawPayload);
    if (!order) return;
    playNotificationSound();
    for (const listener of orderListeners) {
      try {
        listener(order);
      } catch (e) {
        console.error('Lỗi listener OrderCreated:', e);
      }
    }
  });

  // 2. StockLevelChanged: Biến động tồn kho (nhóm tenant_{tenantId}_branch_{branchId})
  conn.on('StockLevelChanged', (rawPayload) => {
    if (sessionGen !== currentGeneration) return;
    const stock = normalizeStockPayload(rawPayload);
    if (!stock) return;

    // T03-AC-03: Sự kiện chi nhánh khác KHÔNG được làm đổi tồn/giỏ của chi nhánh hiện tại
    if (activeBranchId && stock.branchId && stock.branchId.toLowerCase() !== activeBranchId.toLowerCase()) {
      return;
    }

    // B02-F04: Tuyệt đối không ghi đè số dư từ event muộn/unverified;
    // Kích hoạt GET API refetch số tồn chuẩn xác thực từ server cho chi nhánh hiện tại
    if (stock.branchId && stock.skuId) {
      invalidateAndRefetchStock(stock.branchId, [{ skuId: stock.skuId }]).catch(() => {});
    }

    for (const listener of stockListeners) {
      try {
        listener(stock);
      } catch (e) {
        console.error('Lỗi listener StockLevelChanged:', e);
      }
    }
  });

  // 3. LowStockWarning: Cảnh báo tồn dưới ngưỡng an toàn (nhóm tenant_{tenantId})
  conn.on('LowStockWarning', (rawPayload) => {
    if (sessionGen !== currentGeneration) return;
    const warning = normalizeWarningPayload(rawPayload);
    if (!warning) return;

    // Chỉ cảnh báo nếu người dùng đang quan tâm đến chi nhánh này
    if (activeBranchId && warning.branchId && warning.branchId.toLowerCase() !== activeBranchId.toLowerCase()) {
      return;
    }

    // B02-F04: Refetch tồn kho chính thức từ API
    if (warning.branchId && (warning.skuId || warning.skuCode)) {
      invalidateAndRefetchStock(warning.branchId, [{ skuId: warning.skuId, skuCode: warning.skuCode }]).catch(() => {});
    }

    playNotificationSound();
    for (const listener of warningListeners) {
      try {
        listener(warning);
      } catch (e) {
        console.error('Lỗi listener LowStockWarning:', e);
      }
    }
  });

  try {
    await conn.start();

    // B02-F01: Nếu trong lúc chờ conn.start(), phiên đã bị stop hoặc đổi sang user khác
    if (sessionGen !== currentGeneration) {
      try {
        await conn.stop();
      } catch {}
      return;
    }

    hubConnection = conn;
    activeStartingConnection = null;
    setStatus('connected');

    // Sau khi kết nối thành công: gia nhập nhóm chi nhánh nếu đã chọn branch
    if (branchId) {
      try {
        await conn.invoke('JoinBranchGroup', branchId);
      } catch (joinErr) {
        console.warn('Không thể tham gia nhóm chi nhánh:', joinErr);
      }
    }
  } catch (err) {
    if (sessionGen === currentGeneration) {
      activeStartingConnection = null;
      console.warn('Không thể kết nối SignalR Hub (máy chủ offline hoặc token không hợp lệ):', err);
      setStatus('disconnected');
    }
  }
}

/**
 * Đổi nhóm chi nhánh: rời nhóm cũ và tham gia nhóm mới qua đúng server method
 */
export async function switchBranch(newBranchId: string): Promise<void> {
  const old = activeBranchId;
  activeBranchId = newBranchId;

  if (!hubConnection || hubConnection.state !== HubConnectionState.Connected) {
    return;
  }

  try {
    if (old && old !== newBranchId) {
      await hubConnection.invoke('LeaveBranchGroup', old).catch(() => {});
    }
    if (newBranchId) {
      await hubConnection.invoke('JoinBranchGroup', newBranchId);
    }
  } catch (err) {
    console.error('Lỗi khi chuyển nhóm chi nhánh trên SignalR Hub:', err);
  }
}

/**
 * Ngắt kết nối SignalR an toàn và dọn dẹp trạng thái (B02-F01: Thu hồi cả kết nối đang khởi tạo)
 */
export async function stopRealtimeConnection(): Promise<void> {
  currentGeneration++;
  const connToStop = hubConnection;
  const startingToStop = activeStartingConnection;
  hubConnection = null;
  activeStartingConnection = null;
  activeUser = null;
  activeBranchId = '';
  setStatus('disconnected');

  if (connToStop) {
    try {
      if (activeBranchId && connToStop.state === HubConnectionState.Connected) {
        await connToStop.invoke('LeaveBranchGroup', activeBranchId).catch(() => {});
      }
      await connToStop.stop();
    } catch {}
  }

  if (startingToStop) {
    try {
      await startingToStop.stop();
    } catch {}
  }
}

/**
 * Mock helper dùng riêng cho kiểm thử tự động (Unit / Component test stubs)
 */
export function _simulateRealtimeEvent(
  eventName: 'OrderCreated' | 'StockLevelChanged' | 'LowStockWarning',
  payload: any
): void {
  if (eventName === 'OrderCreated') {
    const order = normalizeOrderPayload(payload);
    if (order) {
      for (const l of orderListeners) l(order);
    }
  } else if (eventName === 'StockLevelChanged') {
    const stock = normalizeStockPayload(payload);
    if (stock) {
      if (!activeBranchId || !stock.branchId || stock.branchId.toLowerCase() === activeBranchId.toLowerCase()) {
        if (stock.newAvailableStock !== undefined) {
          setAuthoritativeStock(stock.branchId, stock.skuId, stock.newAvailableStock);
        }
        // B02-F04: Refetch API
        invalidateAndRefetchStock(stock.branchId, [{ skuId: stock.skuId }]).catch(() => {});
        for (const l of stockListeners) l(stock);
      }
    }
  } else if (eventName === 'LowStockWarning') {
    const warn = normalizeWarningPayload(payload);
    if (warn) {
      if (!activeBranchId || !warn.branchId || warn.branchId.toLowerCase() === activeBranchId.toLowerCase()) {
        invalidateAndRefetchStock(warn.branchId, [{ skuId: warn.skuId, skuCode: warn.skuCode }]).catch(() => {});
        for (const l of warningListeners) l(warn);
      }
    }
  }
}

export function _setActiveBranchForTesting(branchId: string): void {
  activeBranchId = branchId;
}
