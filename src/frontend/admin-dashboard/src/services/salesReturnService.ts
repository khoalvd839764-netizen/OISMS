import { httpClient } from './httpClient';
import { generateUuid } from './apiAdapter';
import type {
  ReturnableItem,
  CreateSalesReturnRequest,
  SalesReturn,
  SalesReturnFilter,
  SalesReturnPagedResult,
} from '../types/salesReturns';

// [BF-004] Lưu và khôi phục ý định tạo phiếu trả hàng theo chữ ký payload
let lastReturnIntent: { signature: string; requestKey: string; timestamp: number } | null = null;

function getReturnIntentStorageKey(): string {
  const tenantId = localStorage.getItem('oism_tenant_id') || 'default';
  return `oism_sales_return_intent_${tenantId}`;
}

export function resolveSalesReturnRequestKey(sig: string, explicitKey?: string): string {
  if (lastReturnIntent && lastReturnIntent.signature === sig) {
    return lastReturnIntent.requestKey;
  }
  try {
    const raw = sessionStorage.getItem(getReturnIntentStorageKey());
    if (raw) {
      const parsed = JSON.parse(raw);
      if (parsed && parsed.signature === sig && parsed.requestKey) {
        lastReturnIntent = parsed;
        return parsed.requestKey;
      }
    }
  } catch {}

  const key = explicitKey || generateUuid();
  const intent = { signature: sig, requestKey: key, timestamp: Date.now() };
  lastReturnIntent = intent;
  try {
    sessionStorage.setItem(getReturnIntentStorageKey(), JSON.stringify(intent));
  } catch {}
  return key;
}

export function clearSalesReturnIntent(): void {
  lastReturnIntent = null;
  try {
    sessionStorage.removeItem(getReturnIntentStorageKey());
  } catch {}
}

export function getSalesReturnErrorMessage(err: any): { code?: string; message: string } {
  const errorData = err?.response?.data;
  const code = errorData?.errorCode || errorData?.extensions?.errorCode || err?.code;
  const detail = errorData?.detail || errorData?.message || err?.message;

  switch (code) {
    case 'RETURN_QUANTITY_EXCEEDED':
      return { code, message: 'Số lượng trả vượt số còn được trả.' };
    case 'REFUND_AMOUNT_EXCEEDED':
      return { code, message: 'Tiền hoàn vượt giá bán.' };
    case 'ORDER_NOT_RETURNABLE':
      return { code, message: 'Đơn chưa xuất kho hoặc đã hủy.' };
    case 'RETURN_WAREHOUSE_MISMATCH':
      return { code, message: 'Kho không thuộc chi nhánh của đơn.' };
    case 'INVALID_STATE_TRANSITION':
      return { code, message: 'Phiếu đã được xử lý.' };
    case 'IDEMPOTENCY_PAYLOAD_MISMATCH':
      return { code, message: 'Yêu cầu trùng với một phiếu khác.' };
    default:
      return { code, message: detail || 'Đã xảy ra lỗi khi xử lý phiếu trả hàng.' };
  }
}

export const salesReturnService = {
  /**
   * Lấy danh sách các dòng hàng có thể trả từ đơn hàng gốc
   */
  async getReturnableItems(orderId: string): Promise<ReturnableItem[]> {
    const response = await httpClient.get(`/api/orders/${orderId}/returnable-items`);
    return Array.isArray(response.data) ? response.data : [];
  },

  /**
   * Lập phiếu trả hàng ở trạng thái Nháp (Draft)
   * Giữ nguyên Idempotency-Key theo chữ ký payload khi kết quả chưa rõ (BF-004)
   */
  async create(data: CreateSalesReturnRequest): Promise<SalesReturn> {
    const sig = JSON.stringify({
      orderId: data.orderId,
      warehouseId: data.warehouseId,
      reason: (data.reason || '').trim(),
      note: (data.note || '').trim(),
      items: (data.items || []).map((i) => ({
        orderItemId: i.orderItemId,
        quantity: Number(i.quantity) || 0,
        refundAmount: i.refundAmount !== undefined && i.refundAmount !== null ? Number(i.refundAmount) : null,
      })),
    });

    const requestKey = resolveSalesReturnRequestKey(sig, data.requestKey);

    const payload = {
      ...data,
      requestKey,
    };

    const response = await httpClient.post('/api/sales-returns', payload, {
      headers: {
        'Idempotency-Key': requestKey,
        'X-Request-Key': requestKey,
      },
    });

    clearSalesReturnIntent();
    return response.data;
  },

  /**
   * Danh sách phiếu trả hàng có phân trang và bộ lọc
   */
  async list(filter?: SalesReturnFilter): Promise<SalesReturnPagedResult> {
    const params: Record<string, any> = {};
    if (filter?.branchId) params.branchId = filter.branchId;
    if (filter?.warehouseId) params.warehouseId = filter.warehouseId;
    if (filter?.orderId) params.orderId = filter.orderId;
    if (filter?.status) params.status = filter.status;
    if (filter?.fromDate) params.fromDate = filter.fromDate;
    if (filter?.toDate) params.toDate = filter.toDate;
    if (filter?.searchTerm) params.searchTerm = filter.searchTerm;
    params.pageIndex = filter?.pageIndex || 1;
    params.pageSize = filter?.pageSize || 20;

    const response = await httpClient.get('/api/sales-returns', { params });
    const data = response.data;

    if (data && typeof data === 'object') {
      const items = Array.isArray(data.items) ? data.items : [];
      return {
        items,
        pageIndex: data.pageIndex || params.pageIndex,
        pageSize: data.pageSize || params.pageSize,
        totalCount: typeof data.totalCount === 'number' ? data.totalCount : items.length,
        totalPages: typeof data.totalPages === 'number' ? data.totalPages : Math.ceil(items.length / params.pageSize) || 1,
      };
    }

    if (Array.isArray(data)) {
      return {
        items: data,
        pageIndex: params.pageIndex,
        pageSize: params.pageSize,
        totalCount: data.length,
        totalPages: Math.ceil(data.length / params.pageSize) || 1,
      };
    }

    throw new Error('Định dạng dữ liệu phiếu trả hàng từ máy chủ không hợp lệ.');
  },

  /**
   * Chi tiết một phiếu trả hàng theo ID
   */
  async getById(id: string): Promise<SalesReturn> {
    const response = await httpClient.get(`/api/sales-returns/${id}`);
    return response.data;
  },

  /**
   * Xác nhận phiếu trả hàng (Draft -> Confirmed)
   */
  async confirm(id: string, refundMethod?: string | null): Promise<SalesReturn> {
    const response = await httpClient.post(`/api/sales-returns/${id}/confirm`, {
      refundMethod: refundMethod || null,
    });
    return response.data;
  },

  /**
   * Hủy phiếu trả hàng (Draft -> Cancelled)
   */
  async cancel(id: string): Promise<SalesReturn> {
    const response = await httpClient.post(`/api/sales-returns/${id}/cancel`);
    return response.data;
  },
};
