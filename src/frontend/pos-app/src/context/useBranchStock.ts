import { useEffect, useRef, useState } from 'react';
import type { PosProductSearchResponse } from '../types/pos';
import { fetchPosCatalog } from '../services/posApi';
import { createBranchStock } from '../services/branchStock';
import { startRealtimeConnection, stopRealtimeConnection, subscribeReconnected, subscribeRealtimeStatus, subscribeStockLevelChanged, type RealtimeStatus } from '../services/realtimeService';

export function useBranchStock(user: { id: string; tenantId: string } | null, branchId: string, refresh: number, onCatalog: (items: PosProductSearchResponse[]) => void) {
  const scope = `${user?.tenantId}:${user?.id}:${branchId}`;
  const userId = user?.id, tenantId = user?.tenantId;
  const [state, setState] = useState<{ scope: string; items: PosProductSearchResponse[]; loading: boolean; error: string | null }>({ scope: '', items: [], loading: true, error: null });
  const [status, setStatus] = useState<RealtimeStatus>('disconnected');
  const callback = useRef(onCatalog);
  useEffect(() => { callback.current = onCatalog; }, [onCatalog]);
  const reload = useRef<() => void>(() => {});
  useEffect(() => {
    const end = () => { void stopRealtimeConnection(); };
    window.addEventListener('pos-auth-expired', end); window.addEventListener('pos-session-ended', end);
    return () => { window.removeEventListener('pos-auth-expired', end); window.removeEventListener('pos-session-ended', end); void stopRealtimeConnection(); };
  }, [user?.id, user?.tenantId]);
  useEffect(() => {
    if (!userId || !tenantId || !branchId) return;
    let active = true;
    const stock = createBranchStock(branchId, fetchPosCatalog, items => {
      if (!active) return;
      setState({ scope, items, loading: false, error: null }); callback.current(items);
    }, error => { if (active) setState(previous => ({ scope, items: previous.scope === scope ? previous.items : [], loading: false, error: error.message })); });
    const unsubscribeStock = subscribeStockLevelChanged(stock.event);
    const unsubscribeReconnect = subscribeReconnected(() => { void stock.refresh(); });
    const unsubscribeStatus = subscribeRealtimeStatus(setStatus);
    reload.current = () => { void stock.refresh(); };
    void stock.refresh();
    void startRealtimeConnection({ id: userId, tenantId }, branchId);
    return () => { active = false; stock.dispose(); unsubscribeStock(); unsubscribeReconnect(); unsubscribeStatus(); reload.current = () => {}; };
  }, [userId, tenantId, branchId, scope, refresh]);
  return { ...(state.scope === scope ? state : { items: [], loading: true, error: null }), status, reload: () => reload.current() };
}
