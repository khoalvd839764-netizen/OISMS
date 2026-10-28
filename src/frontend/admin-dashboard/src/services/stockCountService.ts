import { httpClient } from './httpClient';
import { generateUuid } from './apiAdapter';
import type {
  StockCount,
  CreateStockCountRequest,
  RecordStockCountBatchRequest,
  ConfirmStockCountRequest,
  CancelStockCountRequest,
  StockCountFilter,
} from '../types/stockCount';

function getStorageKey(): string {
  const tenantId = localStorage.getItem('oism_tenant_id') || 'default';
  return `oism_stock_counts_${tenantId}`;
}

function loadLocalCounts(): StockCount[] {
  try {
    const raw = localStorage.getItem(getStorageKey());
    return raw ? JSON.parse(raw) : [];
  } catch {
    return [];
  }
}

function saveLocalCounts(counts: StockCount[]): void {
  try {
    localStorage.setItem(getStorageKey(), JSON.stringify(counts));
  } catch (err) {
    console.warn('Lưu cache kiểm kê kho thất bại:', err);
  }
}

export interface ProblemDetailsError {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errorCode?: string;
}

export function parseProblemDetails(error: any): ProblemDetailsError {
  if (error?.response?.data) {
    const data = error.response.data;
    return {
      type: data.type,
      title: data.title || 'Lỗi xử lý nghiệp vụ',
      status: error.response.status,
      detail: data.detail || error.message,
      errorCode: data.errorCode || data.extensions?.errorCode,
    };
  }
  return {
    title: 'Lỗi kết nối mạng',
    detail: error?.message || 'Không thể kết nối đến máy chủ.',
    status: 0,
  };
}

export const stockCountService = {
  /**
   * Lấy danh sách các đợt kiểm kê
   */
  async getStockCounts(filter?: StockCountFilter): Promise<StockCount[]> {
    const response = await httpClient.get('/api/stock-counts', { params: filter });
    if (Array.isArray(response.data)) {
      return response.data;
    }
    if (response.data && Array.isArray(response.data.items)) {
      return response.data.items;
    }
    return [];
  },

  /**
   * Lấy chi tiết đợt kiểm kê theo ID
   */
  async getStockCountById(id: string): Promise<StockCount> {
    const response = await httpClient.get(`/api/stock-counts/${id}`);
    if (response.data) {
      return response.data;
    }
    throw new Error('Không tìm thấy đợt kiểm kê.');
  },

  /**
   * Khởi tạo đợt kiểm kê mới (POST /api/stock-counts)
   */
  async createStockCount(req: CreateStockCountRequest): Promise<StockCount> {
    const requestKey = req.requestKey || generateUuid();
    const payload = {
      requestKey,
      branchId: req.branchId,
      warehouseId: req.warehouseId,
      skuIds: req.skuIds && req.skuIds.length > 0 ? req.skuIds : null,
      note: req.note,
    };

    let result: StockCount;

    try {
      const response = await httpClient.post<StockCount>('/api/stock-counts', payload, {
        headers: {
          'Idempotency-Key': requestKey,
          'X-Request-Key': requestKey,
        },
      });
      result = response.data;
    } catch (error: any) {
      // Ném lỗi ProblemDetails để UI hiển thị thông báo chi tiết
      const pd = parseProblemDetails(error);
      throw pd;
    }

    // Đồng bộ vào cache
    const list = loadLocalCounts();
    const existingIdx = list.findIndex((c) => c.id === result.id);
    if (existingIdx >= 0) {
      list[existingIdx] = result;
    } else {
      list.unshift(result);
    }
    saveLocalCounts(list);

    return result;
  },

  /**
   * Ghi nhận số lượng đếm thực tế (POST /api/stock-counts/{id}/record)
   */
  async recordCounts(id: string, req: RecordStockCountBatchRequest): Promise<StockCount> {
    let result: StockCount;
    try {
      const response = await httpClient.post<StockCount>(`/api/stock-counts/${id}/record`, req);
      result = response.data;
    } catch (error: any) {
      const pd = parseProblemDetails(error);
      throw pd;
    }

    // Cập nhật cache
    const list = loadLocalCounts();
    const idx = list.findIndex((c) => c.id === id);
    if (idx >= 0) {
      list[idx] = result;
      saveLocalCounts(list);
    }

    return result;
  },

  /**
   * Chốt kiểm kê & Cân bằng tồn (POST /api/stock-counts/{id}/confirm)
   */
  async confirmStockCount(id: string, req?: ConfirmStockCountRequest): Promise<StockCount> {
    let result: StockCount;
    try {
      const response = await httpClient.post<StockCount>(`/api/stock-counts/${id}/confirm`, req || {});
      result = response.data;
    } catch (error: any) {
      const pd = parseProblemDetails(error);
      throw pd;
    }

    // Cập nhật cache
    const list = loadLocalCounts();
    const idx = list.findIndex((c) => c.id === id);
    if (idx >= 0) {
      list[idx] = result;
      saveLocalCounts(list);
    }

    return result;
  },

  /**
   * Hủy đợt kiểm kê (POST /api/stock-counts/{id}/cancel)
   */
  async cancelStockCount(id: string, req?: CancelStockCountRequest): Promise<StockCount> {
    let result: StockCount;
    try {
      const response = await httpClient.post<StockCount>(`/api/stock-counts/${id}/cancel`, req || {});
      result = response.data;
    } catch (error: any) {
      const pd = parseProblemDetails(error);
      throw pd;
    }

    // Cập nhật cache
    const list = loadLocalCounts();
    const idx = list.findIndex((c) => c.id === id);
    if (idx >= 0) {
      list[idx] = result;
      saveLocalCounts(list);
    }

    return result;
  },
};
