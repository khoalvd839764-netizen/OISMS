// src/types/ordersCenter.ts
// Kiểu dữ liệu cho Trung tâm đơn hàng đa kênh (OISMS-43, SCR-ORD-01..03).
// Khớp OrderDtos.cs: OrderSummaryDto, OrderDetailDto, OrderFilterDto, PagedResultDto.
// Lưu ý tsconfig: không dùng enum (erasableSyntaxOnly); import kiểu phải là `import type`.

export type OrderStatus =
  | 'Draft'
  | 'Reserved'
  | 'Confirmed'
  | 'Processing' // TODO: xác nhận với backend, DTO chỉ nhắc Draft/Reserved/Confirmed/Completed/Cancelled
  | 'Completed'
  | 'Cancelled';

// Backend dùng 'Admin' (không phải 'Web' như spec Jira).
export type OrderChannel = 'POS' | 'Admin' | 'Shopee' | 'TikTok' | 'Lazada';

export const ORDER_STATUSES: readonly OrderStatus[] = [
  'Draft', 'Reserved', 'Confirmed', 'Processing', 'Completed', 'Cancelled',
];

export const ORDER_CHANNELS: readonly OrderChannel[] = [
  'POS', 'Admin', 'Shopee', 'TikTok', 'Lazada',
];

export const ORDER_STATUS_LABEL: Record<OrderStatus, string> = {
  Draft: 'Nháp',
  Reserved: 'Đã giữ chỗ',
  Confirmed: 'Đã duyệt',
  Processing: 'Đang xử lý',
  Completed: 'Hoàn tất',
  Cancelled: 'Đã hủy',
};

export type OrderAction = 'confirm' | 'cancel' | 'process' | 'complete';

// Nút nào hiện ở trạng thái nào. TODO: đối chiếu với state machine của backend.
export const ORDER_ACTIONS: Record<OrderStatus, readonly OrderAction[]> = {
  Draft: [],
  Reserved: ['confirm', 'cancel'],
  Confirmed: ['process', 'complete'],
  Processing: ['complete'],
  Completed: [],
  Cancelled: [],
};

// Body gửi lên khi thao tác. Hủy đơn BẮT BUỘC có lý do (CancelOrderRequestDto.Reason).
export interface CancelOrderBody { reason: string }
export interface NoteBody { note?: string }

export interface CreateOrderItemRequest {
  skuId: string;
  skuCode: string;
  productName: string;
  variantName?: string;
  quantity: number;
  unitPrice: number;
}

export interface CreateOrderRequest {
  branchId: string;
  channel: string;
  customerName?: string;
  customerPhone?: string;
  shippingAddress?: string;
  requestKey: string;
  items: CreateOrderItemRequest[];
}

// Tham số GET /api/orders (OrderFilterDto): chú ý tên là pageIndex và search.
export interface OrderFilter {
  status?: OrderStatus;
  channel?: OrderChannel;
  branchId?: string;
  fromDate?: string; // ISO 8601
  toDate?: string;
  search?: string;
  pageIndex: number;
  pageSize: number;
}

export interface OrderSummary {
  id: string;
  number: string;
  branchId: string;
  branchName?: string;
  channel: string;
  externalOrderId?: string;
  status: OrderStatus;
  customerName?: string;
  customerPhone?: string;
  totalAmount: number;
  itemCount: number;
  createdAt: string;
  confirmedAt?: string;
  completedAt?: string;
  cancelledAt?: string;
}

export interface OrderLine {
  id: string;
  skuId: string;
  skuCode: string;
  skuName: string;
  quantity: number;
  unitPrice: number;
  costPrice: number | null; // null khi chưa Confirmed: hiển thị "Chưa chốt", không hiển thị 0
}

export interface OrderDetail extends OrderSummary {
  shippingAddress?: string;
  items: OrderLine[];
  totalCogs: number | null;
}

export interface Paged<T> {
  items: T[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}

// ---------- Bộ chuẩn hóa: chịu được cả OrderDetailDto (skuCode) lẫn OrderResponseDto (skuCodeSnapshot) ----------

type Raw = Record<string, unknown>;

const isRaw = (v: unknown): v is Raw => typeof v === 'object' && v !== null;
const str = (v: unknown): string | undefined => (typeof v === 'string' ? v : undefined);
const num = (v: unknown): number | undefined => (typeof v === 'number' ? v : undefined);

function normalizeLine(raw: Raw): OrderLine {
  return {
    id: str(raw.id) ?? '',
    skuId: str(raw.skuId) ?? '',
    skuCode: str(raw.skuCode) ?? str(raw.skuCodeSnapshot) ?? '',
    skuName: str(raw.skuName) ?? str(raw.skuNameSnapshot) ?? '',
    quantity: num(raw.quantity) ?? 0,
    unitPrice: num(raw.unitPrice) ?? 0,
    costPrice: num(raw.costPrice) ?? null,
  };
}

export function normalizeSummary(raw: unknown): OrderSummary {
  if (!isRaw(raw)) throw new Error('Dữ liệu đơn hàng không hợp lệ.');
  return {
    id: str(raw.id) ?? '',
    number: str(raw.number) ?? '',
    branchId: str(raw.branchId) ?? '',
    branchName: str(raw.branchName),
    channel: str(raw.channel) ?? '',
    externalOrderId: str(raw.externalOrderId),
    status: (str(raw.status) ?? 'Draft') as OrderStatus,
    customerName: str(raw.customerName),
    customerPhone: str(raw.customerPhone),
    totalAmount: num(raw.totalAmount) ?? 0,
    itemCount: num(raw.itemCount) ?? (Array.isArray(raw.items) ? raw.items.length : 0),
    createdAt: str(raw.createdAt) ?? '',
    confirmedAt: str(raw.confirmedAt),
    completedAt: str(raw.completedAt),
    cancelledAt: str(raw.cancelledAt),
  };
}

export function normalizeDetail(raw: unknown): OrderDetail {
  const summary = normalizeSummary(raw);
  const r = raw as Raw;
  const items = Array.isArray(r.items) ? r.items.filter(isRaw).map(normalizeLine) : [];
  const allCosted = items.length > 0 && items.every((i) => i.costPrice !== null);
  return {
    ...summary,
    itemCount: summary.itemCount || items.length,
    shippingAddress: str(r.shippingAddress),
    items,
    totalCogs:
      num(r.totalCogs) ??
      (allCosted ? items.reduce((sum, i) => sum + i.quantity * (i.costPrice ?? 0), 0) : null),
  };
}

export function normalizePaged<T>(raw: unknown, mapItem: (item: unknown) => T): Paged<T> {
  if (!isRaw(raw)) throw new Error('Dữ liệu danh sách không hợp lệ.');
  const items = Array.isArray(raw.items) ? raw.items.map(mapItem) : [];
  const pageSize = num(raw.pageSize) ?? items.length;
  const totalCount = num(raw.totalCount) ?? items.length;
  return {
    items,
    totalCount,
    pageNumber: num(raw.pageNumber) ?? num(raw.pageIndex) ?? 1,
    pageSize,
    totalPages: num(raw.totalPages) ?? (pageSize > 0 ? Math.ceil(totalCount / pageSize) : 1),
  };
}