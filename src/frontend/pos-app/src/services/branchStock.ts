import type { CartItem, PosProductSearchResponse } from '../types/pos';
import type { StockChanged } from './realtimeService';

export function updateCartStock(cart: CartItem[], products: PosProductSearchResponse[]): CartItem[] {
  const available = new Map(products.map(product => [product.variantId, product.available ?? 0]));
  return cart.map(item => ({ ...item, available: available.get(item.variantId) ?? 0 }));
}

/** A branch-scoped read coordinator. Later invalidations make every older GET ineligible to commit. */
export function createBranchStock(
  branchId: string,
  read: (branchId: string) => Promise<PosProductSearchResponse[]>,
  commit: (items: PosProductSearchResponse[]) => void,
  failed: (error: Error) => void,
) {
  let revision = 0, disposed = false;
  const eventTimes = new Map<string, number>();
  async function refresh() {
    const expected = ++revision;
    try {
      const items = await read(branchId);
      if (!disposed && expected === revision) commit(items);
    } catch (error) { if (!disposed && expected === revision) failed(error instanceof Error ? error : new Error('Không tải được tồn kho.')); }
  }
  function event(stock: StockChanged) {
    if (disposed || stock.branchId.toLowerCase() !== branchId.toLowerCase()) return;
    const time = Date.parse(stock.timestamp);
    if (!Number.isFinite(time) || time < (eventTimes.get(stock.skuId) ?? -Infinity)) return;
    eventTimes.set(stock.skuId, time);
    // Never treat a late/unverified event quantity as the authoritative balance.
    void refresh();
  }
  return { refresh, event, dispose: () => { disposed = true; revision++; eventTimes.clear(); } };
}
