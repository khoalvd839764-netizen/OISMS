import type { CartItem, PosProductSearchResponse } from '../types/pos';

export interface CurrentPriceItem {
  skuId: string;
  skuCode?: string;
  unitId?: string | null;
  currentPrice: number;
}

/** Chọn đơn vị và giá khi thêm vào giỏ. Trả về null nếu đơn vị chưa có giá, không tự tính giá. */
export function resolveCartUnit(product: PosProductSearchResponse) {
  const units = product.units ?? [];
  if (units.length === 0) {
    return { unit: undefined, price: product.retailPrice };
  }
  const matched = units.find((u) => u.unitId === product.matchedUnitId);
  const candidates = matched
    ? [matched]
    : [units.find((u) => u.isDefaultSale), units.find((u) => u.isBase), ...units];
  const unit = candidates.find((u) => u && u.salePrice != null);
  if (!unit) return null;
  return { unit, price: unit.salePrice as number };
}

const lineKeyOf = (item: CartItem) => item.lineKey || `${item.variantId}:${item.unitId || ''}`;

/** Số chữ số thập phân được phép của đơn vị trên dòng giỏ. */
export function lineDecimalPlaces(item: CartItem): number {
  return item.units?.find((u) => u.unitId === item.unitId)?.decimalPlaces ?? 0;
}

export function normalizeQuantity(quantity: number, decimalPlaces: number): number {
  const factor = 10 ** decimalPlaces;
  return Math.round(quantity * factor) / factor;
}

/** Lượng tồn gốc đang dùng trong giỏ của một SKU, có thể bỏ qua một dòng. */
export function baseQuantityInCart(cart: CartItem[], variantId: string, excludeLineKey?: string): number {
  return cart
    .filter((item) => item.variantId === variantId && lineKeyOf(item) !== excludeLineKey)
    .reduce((sum, item) => sum + item.quantity * (item.factorToBase || 1), 0);
}

/** Số lượng tối đa của dòng theo đơn vị đang chọn, tính cả các dòng khác cùng SKU. */
export function maxQuantityForLine(cart: CartItem[], item: CartItem): number {
  const remaining = item.available - baseQuantityInCart(cart, item.variantId, lineKeyOf(item));
  const factor = 10 ** lineDecimalPlaces(item);
  return Math.max(0, Math.floor((remaining / (item.factorToBase || 1)) * factor + 1e-9) / factor);
}

/** Áp giá mới server trả về khi PRICE_MISMATCH vào đúng dòng SKU và đơn vị. */
export function applyCurrentPrices(cart: CartItem[], prices: CurrentPriceItem[]): CartItem[] {
  return cart.map((item) => {
    const match = prices.find(
      (p) => p.skuId === item.variantId && (!p.unitId || !item.unitId || p.unitId === item.unitId)
    );
    if (!match) return item;
    return {
      ...item,
      retailPrice: match.currentPrice,
      units: item.units?.map((u) => (u.unitId === item.unitId ? { ...u, salePrice: match.currentPrice } : u)),
    };
  });
}
