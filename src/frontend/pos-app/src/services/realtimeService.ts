import { HubConnectionBuilder, HubConnectionState, HttpTransportType, LogLevel, type HubConnection } from '@microsoft/signalr';
import { getPosAccessToken, getPosGeneration, refreshPosToken } from './posSession';

export type RealtimeStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';
export interface StockChanged { branchId: string; skuId: string; newAvailableStock: number; timestamp: string; }
export interface BranchOrderItem { skuId: string; skuCode: string; productName: string; quantity: number; unitName?: string; }
export interface BranchOrderCreated { orderId: string; orderNumber: string; branchId: string; channel: string; createdAt: string; items: BranchOrderItem[]; }
interface User { id: string; tenantId: string; }
const HUB_URL = `${(import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000').replace(/\/$/, '')}/hubs/notifications`;
let connection: HubConnection | null = null;
let starting: HubConnection | null = null;
let generation = 0;
let branchRevision = 0;
let activeBranch = '';
let activeUser: User | null = null;
let status: RealtimeStatus = 'disconnected';
const stocks = new Set<(event: StockChanged) => void>();
const branchOrders = new Set<(event: BranchOrderCreated) => void>();
const statuses = new Set<(status: RealtimeStatus) => void>();
const reconnects = new Set<() => void>();
function setStatus(next: RealtimeStatus) { status = next; for (const cb of statuses) cb(next); }
export function subscribeRealtimeStatus(cb: (value: RealtimeStatus) => void) { statuses.add(cb); cb(status); return () => { statuses.delete(cb); }; }
export function subscribeStockLevelChanged(cb: (event: StockChanged) => void) { stocks.add(cb); return () => { stocks.delete(cb); }; }
export function subscribeBranchOrderCreated(cb: (event: BranchOrderCreated) => void) { branchOrders.add(cb); return () => { branchOrders.delete(cb); }; }
export function subscribeReconnected(cb: () => void) { reconnects.add(cb); return () => { reconnects.delete(cb); }; }

// Reuses the former client's generation/branch guard and transport lifecycle; no legacy stock cache or UI.
export async function startRealtimeConnection(user: User, branchId: string) {
  if (!getPosAccessToken()) { await stopRealtimeConnection(); return; }
  if (connection?.state === HubConnectionState.Connected && activeUser?.id === user.id && activeUser?.tenantId === user.tenantId) {
    await switchBranch(branchId); return;
  }
  const previous = connection, previousStarting = starting;
  const expected = ++generation;
  const authGeneration = getPosGeneration();
  connection = null; starting = null;
  activeUser = { ...user }; activeBranch = branchId; branchRevision++;
  const current = () => expected === generation && authGeneration === getPosGeneration();
  await Promise.allSettled([previous?.stop(), previousStarting?.stop()]);
  if (!current()) return;
  setStatus('connecting');
  const conn = new HubConnectionBuilder().withUrl(HUB_URL, {
    accessTokenFactory: async () => {
      if (!current()) return '';
      let token = getPosAccessToken() || '';
      try {
        const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
        if (payload.exp * 1000 < Date.now() + 30000) token = await refreshPosToken();
      } catch { /* Malformed tokens are rejected by the hub; a failed refresh already ends the session. */ }
      return current() ? token : '';
    },
    transport: HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents | HttpTransportType.LongPolling,
  }).withAutomaticReconnect([0, 2000, 5000, 10000, 30000]).configureLogging(LogLevel.Warning).build();
  starting = conn;
  const rejoin = async () => {
    const revision = branchRevision, branch = activeBranch;
    if (branch) await conn.invoke('JoinBranchGroup', branch);
    return current() && revision === branchRevision;
  };
  conn.onreconnecting(() => { if (current()) setStatus('reconnecting'); });
  conn.onreconnected(async () => {
    if (!current()) return;
    try { if (!await rejoin()) return; setStatus('connected'); for (const cb of reconnects) cb(); }
    catch { if (current()) setStatus('disconnected'); }
  });
  conn.onclose(() => { if (current()) setStatus('disconnected'); });
  conn.on('StockLevelChanged', raw => {
    if (!current() || !raw || typeof raw !== 'object') return;
    const event: StockChanged = {
      branchId: String(raw.branchId || raw.BranchId || ''), skuId: String(raw.skuId || raw.SkuId || ''),
      newAvailableStock: Number(raw.newAvailableStock ?? raw.NewAvailableStock), timestamp: String(raw.timestamp || raw.Timestamp || ''),
    };
    if (!activeBranch || event.branchId.toLowerCase() !== activeBranch.toLowerCase() || !event.skuId || !Number.isFinite(event.newAvailableStock) || event.newAvailableStock < 0 || !Number.isFinite(Date.parse(event.timestamp))) return;
    for (const cb of stocks) cb(event);
  });
  conn.on('BranchOrderCreated', raw => {
    if (!current() || !raw || typeof raw !== 'object') return;
    const rawItems = Array.isArray(raw.items) ? raw.items : Array.isArray(raw.Items) ? raw.Items : null;
    if (!rawItems) return;
    const event: BranchOrderCreated = {
      orderId: String(raw.orderId || raw.OrderId || '').trim(),
      orderNumber: String(raw.orderNumber || raw.OrderNumber || '').trim(),
      branchId: String(raw.branchId || raw.BranchId || '').trim(),
      channel: String(raw.channel || raw.Channel || '').trim(),
      createdAt: String(raw.createdAt || raw.CreatedAt || '').trim(),
      items: rawItems
        .filter((it: unknown): it is Record<string, unknown> => Boolean(it && typeof it === 'object'))
        .map((it: Record<string, unknown>) => ({
          skuId: String(it.skuId || it.SkuId || '').trim(),
          skuCode: String(it.skuCode || it.SkuCode || '').trim(),
          productName: String(it.productName || it.ProductName || '').trim(),
          quantity: Number(it.quantity ?? it.Quantity),
          unitName: typeof (it.unitName ?? it.UnitName) === 'string' ? String(it.unitName ?? it.UnitName).trim() : undefined,
        })),
    };
    if (
      !activeBranch ||
      !event.orderId ||
      !event.orderNumber ||
      !event.channel ||
      event.branchId.toLowerCase() !== activeBranch.toLowerCase() ||
      !Number.isFinite(Date.parse(event.createdAt)) ||
      event.items.length === 0 ||
      event.items.some(i => !i.skuId || !i.skuCode || !Number.isFinite(i.quantity) || i.quantity <= 0)
    ) {
      return;
    }
    for (const cb of branchOrders) cb(event);
  });
  try {
    await conn.start();
    if (!current()) { await conn.stop(); return; }
    connection = conn; starting = null;
    if (!await rejoin()) return;
    setStatus('connected');
    // Close the initial GET-before-join window: a commit during connection setup must be re-read.
    for (const cb of reconnects) cb();
  } catch {
    if (current()) { connection = null; starting = null; setStatus('disconnected'); }
    await conn.stop().catch(() => {});
  }
}

export async function switchBranch(branchId: string) {
  const old = activeBranch; if (old === branchId) return;
  activeBranch = branchId;
  const revision = ++branchRevision, expected = generation, conn = connection;
  if (!conn || conn.state !== HubConnectionState.Connected) return;
  try {
    if (old) await conn.invoke('LeaveBranchGroup', old);
    if (expected !== generation || revision !== branchRevision) return;
    if (branchId) await conn.invoke('JoinBranchGroup', branchId);
    if (expected === generation && revision === branchRevision) for (const cb of reconnects) cb();
  } catch { if (expected === generation) setStatus('disconnected'); }
}

export async function stopRealtimeConnection() {
  generation++; branchRevision++;
  const conn = connection, pending = starting, oldBranch = activeBranch;
  connection = null; starting = null; activeUser = null; activeBranch = '';
  setStatus('disconnected');
  if (conn) {
    if (oldBranch && conn.state === HubConnectionState.Connected) await conn.invoke('LeaveBranchGroup', oldBranch).catch(() => {});
    await conn.stop().catch(() => {});
  }
  if (pending && pending !== conn) await pending.stop().catch(() => {});
}
