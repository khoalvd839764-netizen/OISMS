// Hàm thuần dựng payload webhook theo CanonicalWebhookPayloadDto của backend.
// File này không import gì để test-payload.mjs nạp được bằng TypeScript transpile.

export type Channel = 'Shopee' | 'TikTok' | 'Lazada';

export const CHANNELS: Channel[] = ['Shopee', 'TikTok', 'Lazada'];

export interface ItemInput {
  skuCode: string;
  quantity: string;
  unitPrice: string;
  /** Gõ mã tay thay vì chọn từ danh sách sản phẩm của shop, dùng để thử SKU_NOT_FOUND. Không gửi lên backend. */
  manual?: boolean;
}

/** Một SKU đang bán của shop, lấy từ GET /api/webhooks/simulator/catalog. Số lượng theo đơn vị gốc. */
export interface CatalogItem {
  skuCode: string;
  productName: string;
  variantName?: string | null;
  unitName: string;
  unitPrice?: number | null;
  available: number;
}

export interface OrderInput {
  channel: Channel;
  externalShopId: string;
  customerName: string;
  customerPhone: string;
  shippingAddress: string;
  items: ItemInput[];
}

export interface WebhookPayload {
  channel: Channel;
  externalShopId: string;
  externalOrderId: string;
  customerName?: string;
  customerPhone?: string;
  shippingAddress?: string;
  totalAmount: number;
  orderTimestamp: string;
  items: { skuCode: string; quantity: number; unitPrice: number }[];
}

const QTY_PATTERN = /^\d+(\.\d{1,3})?$/;
const PRICE_PATTERN = /^\d+(\.\d{1,2})?$/;

/** Đổi chuỗi thập phân thành số nguyên theo số chữ số lẻ cố định, tránh sai số dấu phẩy động. */
function toScaledInt(value: string, scale: number): bigint {
  const [whole, frac = ''] = value.split('.');
  return BigInt(whole + frac.padEnd(scale, '0'));
}

/**
 * Tổng tiền khớp backend: cộng quantity × unitPrice rồi làm tròn 2 chữ số, kiểu AwayFromZero.
 * Số lượng tối đa 3 chữ số lẻ, đơn giá tối đa 2 chữ số lẻ nên tích có 5 chữ số lẻ.
 */
export function calculateTotal(items: ItemInput[]): number {
  let sum = 0n;
  for (const item of items) {
    sum += toScaledInt(item.quantity.trim(), 3) * toScaledInt(item.unitPrice.trim(), 2);
  }
  const cents = (sum + 500n) / 1000n;
  return Number(cents) / 100;
}

/** Trả danh sách lỗi nhập liệu; mảng rỗng nghĩa là gửi được. */
export function validateOrder(input: OrderInput): string[] {
  const errors: string[] = [];
  if (!input.externalShopId.trim()) errors.push('Chưa nhập mã gian hàng.');
  if (input.items.length === 0) errors.push('Đơn phải có ít nhất một dòng hàng.');

  const priceBySku = new Map<string, string>();
  input.items.forEach((item, index) => {
    const row = `Dòng ${index + 1}`;
    const sku = item.skuCode.trim();
    if (!sku) errors.push(`${row}: ${item.manual ? 'chưa nhập mã SKU' : 'chưa chọn sản phẩm'}.`);
    if (!QTY_PATTERN.test(item.quantity.trim()) || Number(item.quantity) <= 0) {
      errors.push(`${row}: số lượng phải lớn hơn 0, tối đa 3 chữ số lẻ.`);
    }
    if (!PRICE_PATTERN.test(item.unitPrice.trim())) {
      errors.push(`${row}: đơn giá không âm, tối đa 2 chữ số lẻ.`);
    }
    if (sku && PRICE_PATTERN.test(item.unitPrice.trim())) {
      const key = sku.toUpperCase();
      const price = String(Number(item.unitPrice));
      const seen = priceBySku.get(key);
      if (seen !== undefined && seen !== price) {
        errors.push(`${row}: SKU ${sku} xuất hiện nhiều dòng với đơn giá khác nhau.`);
      }
      priceBySku.set(key, price);
    }
  });
  return errors;
}

/** Mã đơn sàn mới cho mỗi lần "Gửi đơn mới". Giữ nguyên mã này khi gửi lại. */
export function newExternalOrderId(channel: Channel, random: () => string = () => crypto.randomUUID()): string {
  const prefix = channel === 'Shopee' ? 'SPE' : channel === 'TikTok' ? 'TTS' : 'LZD';
  return `${prefix}-${random().replace(/-/g, '').slice(0, 12).toUpperCase()}`;
}

export function buildPayload(input: OrderInput, externalOrderId: string, now: Date): WebhookPayload {
  const optional = (value: string) => (value.trim() ? value.trim() : undefined);
  return {
    channel: input.channel,
    externalShopId: input.externalShopId.trim(),
    externalOrderId,
    customerName: optional(input.customerName),
    customerPhone: optional(input.customerPhone),
    shippingAddress: optional(input.shippingAddress),
    totalAmount: calculateTotal(input.items),
    orderTimestamp: now.toISOString(),
    items: input.items.map((item) => ({
      skuCode: item.skuCode.trim(),
      quantity: Number(item.quantity.trim()),
      unitPrice: Number(item.unitPrice.trim()),
    })),
  };
}

/** Cùng mã đơn nhưng tăng số lượng dòng đầu: backend phải trả 409 WEBHOOK_PAYLOAD_MISMATCH. */
export function mutatePayload(payload: WebhookPayload): WebhookPayload {
  const items = payload.items.map((item, index) =>
    index === 0 ? { ...item, quantity: Number(toScaledInt(String(item.quantity), 3) + 1000n) / 1000 } : item,
  );
  const totalAmount = calculateTotal(
    items.map((item) => ({ skuCode: item.skuCode, quantity: String(item.quantity), unitPrice: String(item.unitPrice) })),
  );
  return { ...payload, items, totalAmount };
}

export interface ParsedResult {
  ok: boolean;
  summary: string;
  errorCode?: string;
}

/** Đọc cả kết quả thành công lẫn các dạng body lỗi backend đang trả ({errorCode,message}, ProblemDetails, {message}). */
export function parseResponse(status: number, body: unknown): ParsedResult {
  const data = (body && typeof body === 'object' ? body : {}) as Record<string, unknown>;
  const text = (key: string) => (typeof data[key] === 'string' ? (data[key] as string) : undefined);

  if (status >= 200 && status < 300) {
    const order = text('orderNumber') ?? text('orderId') ?? '';
    const orderStatus = text('status') ?? '';
    const replay = status === 200 ? ' (gửi lặp, trả kết quả cũ)' : '';
    return { ok: true, summary: [order, orderStatus].filter(Boolean).join(' · ') + replay };
  }

  const errorCode = text('errorCode') ?? text('code');
  const message = text('message') ?? text('detail') ?? text('title') ?? (typeof body === 'string' ? body : '');
  if (!message && status >= 502 && status <= 504) {
    return { ok: false, errorCode, summary: `Không kết nối được backend (HTTP ${status}).` };
  }
  return { ok: false, errorCode, summary: message || `HTTP ${status}` };
}

export const MANUAL_SKU = '__manual__';

export function catalogOptionLabel(item: CatalogItem): string {
  const name = item.variantName ? `${item.productName} · ${item.variantName}` : item.productName;
  return `${name} (${item.skuCode})`;
}

export function findCatalogItem(catalog: readonly CatalogItem[], skuCode: string): CatalogItem | undefined {
  const key = skuCode.trim().toUpperCase();
  return key ? catalog.find((c) => c.skuCode.toUpperCase() === key) : undefined;
}

/**
 * Giá trị ô chọn sản phẩm của một dòng: mã SKU nếu có trong shop, MANUAL_SKU nếu đang gõ tay
 * hoặc mã cũ không thuộc shop đang chọn (ví dụ vừa đổi kết nối), chuỗi rỗng nếu chưa chọn.
 */
export function selectValueFor(item: ItemInput, catalog: readonly CatalogItem[]): string {
  const found = findCatalogItem(catalog, item.skuCode);
  if (item.manual || (item.skuCode.trim() && !found)) return MANUAL_SKU;
  return found ? found.skuCode : '';
}

/** Chọn sản phẩm: điền mã SKU và giá bán lẻ hiện hành (shop chưa đặt giá thì giữ giá đang nhập). */
export function applyCatalogChoice(item: ItemInput, choice: string, catalog: readonly CatalogItem[]): ItemInput {
  if (choice === MANUAL_SKU) {
    return { ...item, manual: true };
  }
  const found = findCatalogItem(catalog, choice);
  if (!found) {
    return { ...item, skuCode: '', manual: false };
  }
  const price = found.unitPrice;
  return {
    ...item,
    skuCode: found.skuCode,
    unitPrice: price !== null && price !== undefined && price >= 0 ? String(price) : item.unitPrice,
    manual: false,
  };
}

/**
 * Cảnh báo theo từng dòng khi tổng số lượng của một SKU trong đơn vượt tồn có thể bán.
 * Chỉ là cảnh báo: bắn vượt tồn là kịch bản kiểm thử hợp lệ, backend sẽ từ chối.
 */
export function stockWarnings(items: readonly ItemInput[], catalog: readonly CatalogItem[]): (string | null)[] {
  const totals = new Map<string, number>();
  for (const item of items) {
    const found = findCatalogItem(catalog, item.skuCode);
    const qty = Number(item.quantity.trim());
    if (found && Number.isFinite(qty) && qty > 0) {
      totals.set(found.skuCode, (totals.get(found.skuCode) ?? 0) + qty);
    }
  }
  return items.map((item) => {
    const found = findCatalogItem(catalog, item.skuCode);
    if (!found) return null;
    const total = totals.get(found.skuCode) ?? 0;
    return total > found.available ? `Đặt ${total} nhưng chỉ còn ${found.available} ${found.unitName} có thể bán.` : null;
  });
}
