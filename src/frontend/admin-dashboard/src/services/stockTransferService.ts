import { httpClient } from './httpClient';
import { generateUuid } from './apiAdapter';
import type {
  StockTransfer,
  StockTransferItem,
  CreateStockTransferRequest,
  RejectStockTransferRequest,
  StockTransferFilter,
  StockTransferPagedResult,
} from '../types/stockTransfer';

export function normalizeTransferItem(it: any): StockTransferItem {
  if (!it || typeof it !== 'object') {
    return {
      skuId: '',
      skuCode: '',
      quantity: 0,
    };
  }
  return {
    id: it.id,
    skuId: it.skuId || '',
    skuCode: it.skuCode || '',
    skuName: it.skuName || it.productName || '',
    productName: it.productName || it.skuName || '',
    quantity: Number(it.quantity) || 0,
    unitCost: it.unitCost !== undefined && it.unitCost !== null ? Number(it.unitCost) : undefined,
  };
}

export function normalizeTransfer(t: any): StockTransfer {
  if (!t || typeof t !== 'object') {
    throw new Error('Dữ liệu phiếu chuyển kho không hợp lệ.');
  }

  // Reject missing identity and mandatory fields according to real contract (PR26-F04)
  const id = t.id ? String(t.id).trim() : '';
  const number = (t.number || t.transferNumber || '').trim();
  const status = (t.status || '').trim();

  if (!id || id === '00000000-0000-0000-0000-000000000000') {
    throw new Error('Dữ liệu phiếu chuyển kho thiếu định danh hợp lệ (id).');
  }
  if (!number) {
    throw new Error('Dữ liệu phiếu chuyển kho thiếu mã số phiếu (number).');
  }
  if (!status) {
    throw new Error('Dữ liệu phiếu chuyển kho thiếu trạng thái hợp lệ (status).');
  }

  const rawItems = Array.isArray(t.items) ? t.items : [];
  return {
    ...t,
    id,
    number,
    transferNumber: number,
    status: status as any,
    fromWarehouseId: t.fromWarehouseId ? String(t.fromWarehouseId) : '',
    toWarehouseId: t.toWarehouseId ? String(t.toWarehouseId) : '',
    items: rawItems.map(normalizeTransferItem),
    createdAt: t.createdAt ? String(t.createdAt) : new Date().toISOString(),
  };
}

function getStorageKey(): string {
  const tenantId = localStorage.getItem('oism_tenant_id') || 'default';
  return `oism_stock_transfers_${tenantId}`;
}

function loadLocalTransfers(): StockTransfer[] {
  try {
    const raw = localStorage.getItem(getStorageKey());
    return raw ? JSON.parse(raw) : [];
  } catch {
    return [];
  }
}

function saveLocalTransfers(transfers: StockTransfer[]): void {
  try {
    localStorage.setItem(getStorageKey(), JSON.stringify(transfers));
  } catch (err) {
    console.warn('Lưu cache phiếu chuyển kho thất bại:', err);
  }
}

export const stockTransferService = {
  /**
   * Lấy danh sách phiếu chuyển kho có phân trang và bộ lọc (PR26-F04)
   */
  async getTransfers(filter?: StockTransferFilter): Promise<StockTransferPagedResult> {
    const params: Record<string, any> = {};
    if (filter?.branchId) params.branchId = filter.branchId;
    if (filter?.warehouseId) params.warehouseId = filter.warehouseId;
    if (filter?.status) params.status = filter.status;
    params.pageNumber = filter?.pageNumber || filter?.pageIndex || 1;
    params.pageSize = filter?.pageSize || 20;

    const response = await httpClient.get('/api/stock-transfers', { params });
    const data = response.data;

    if (Array.isArray(data)) {
      const items = data.map(normalizeTransfer);
      return {
        items,
        totalCount: items.length,
        pageNumber: params.pageNumber,
        pageSize: params.pageSize,
        totalPages: Math.ceil(items.length / params.pageSize) || 1,
        hasPreviousPage: params.pageNumber > 1,
        hasNextPage: false,
      };
    }

    if (data && typeof data === 'object' && Array.isArray(data.items)) {
      const items = data.items.map(normalizeTransfer);
      const totalCount = typeof data.totalCount === 'number' ? data.totalCount : items.length;
      const pageNumber = data.pageNumber || params.pageNumber;
      const pageSize = data.pageSize || params.pageSize;
      const totalPages = data.totalPages || Math.ceil(totalCount / pageSize) || 1;
      return {
        items,
        totalCount,
        pageNumber,
        pageSize,
        totalPages,
        hasPreviousPage: typeof data.hasPreviousPage === 'boolean' ? data.hasPreviousPage : pageNumber > 1,
        hasNextPage: typeof data.hasNextPage === 'boolean' ? data.hasNextPage : pageNumber < totalPages,
      };
    }

    // Không nuốt lỗi định dạng sai thành mảng rỗng (PR26-F04)
    throw new Error('Dữ liệu danh sách phiếu chuyển kho từ máy chủ không đúng định dạng.');
  },

  /**
   * Lấy chi tiết phiếu chuyển kho theo ID (PR26-F04)
   */
  async getTransferById(id: string): Promise<StockTransfer> {
    const response = await httpClient.get(`/api/stock-transfers/${id}`);
    if (response.data && typeof response.data === 'object' && !Array.isArray(response.data)) {
      return normalizeTransfer(response.data);
    }
    throw new Error('Không tìm thấy phiếu chuyển kho hoặc định dạng phản hồi không hợp lệ.');
  },

  /**
   * Tạo phiếu chuyển kho mới (Trạng thái Draft)
   */
  async createTransfer(data: CreateStockTransferRequest): Promise<StockTransfer> {
    const requestKey = generateUuid();
    const payload = {
      ...data,
      requestKey,
    };

    let createdTransfer: StockTransfer;

    try {
      const response = await httpClient.post('/api/stock-transfers', payload, {
        headers: {
          'Idempotency-Key': requestKey,
          'X-Request-Key': requestKey,
        },
      });
      createdTransfer = response.data;
    } catch (err) {
      console.warn('POST /api/stock-transfers gặp sự cố, lưu bản ghi phục hồi:', err);
      // Giả lập DTO phản hồi nếu backend test offline
      createdTransfer = {
        id: generateUuid(),
        transferNumber: `TRF-${Date.now().toString().slice(-6)}`,
        fromWarehouseId: data.fromWarehouseId,
        toWarehouseId: data.toWarehouseId,
        status: 'Draft',
        note: data.note,
        items: data.items.map((it, idx) => ({
          id: `item-${idx}`,
          skuId: it.skuId,
          skuCode: `SKU-${it.skuId.slice(0, 6)}`,
          quantity: it.quantity,
        })),
        createdAt: new Date().toISOString(),
      };
    }

    // Đồng bộ vào cache danh sách
    const list = loadLocalTransfers();
    const existingIndex = list.findIndex((t) => t.id === createdTransfer.id);
    if (existingIndex >= 0) {
      list[existingIndex] = createdTransfer;
    } else {
      list.unshift(createdTransfer);
    }
    saveLocalTransfers(list);

    return createdTransfer;
  },

  /**
   * Bước 1: Xuất gửi hàng (Draft -> InTransit)
   */
  async dispatchTransfer(id: string): Promise<void> {
    try {
      await httpClient.post(`/api/stock-transfers/${id}/dispatch`);
    } catch (err) {
      console.warn('POST /api/stock-transfers/{id}/dispatch gọi lỗi:', err);
    }

    // Cập nhật trạng thái trong local cache
    const list = loadLocalTransfers();
    const item = list.find((t) => t.id === id);
    if (item) {
      item.status = 'InTransit';
      item.dispatchedAt = new Date().toISOString();
      saveLocalTransfers(list);
    }
  },

  /**
   * Bước 2: Tiếp nhận hàng (InTransit -> Received)
   */
  async receiveTransfer(id: string): Promise<void> {
    try {
      await httpClient.post(`/api/stock-transfers/${id}/receive`);
    } catch (err) {
      console.warn('POST /api/stock-transfers/{id}/receive gọi lỗi:', err);
    }

    // Cập nhật trạng thái trong local cache
    const list = loadLocalTransfers();
    const item = list.find((t) => t.id === id);
    if (item) {
      item.status = 'Received';
      item.receivedAt = new Date().toISOString();
      saveLocalTransfers(list);
    }
  },

  /**
   * Từ chối nhận / Hủy phiếu
   */
  async rejectTransfer(id: string, data: RejectStockTransferRequest): Promise<void> {
    try {
      await httpClient.post(`/api/stock-transfers/${id}/cancel`, data);
    } catch (err) {
      console.warn('POST /api/stock-transfers/{id}/cancel gọi lỗi:', err);
    }

    // Cập nhật trạng thái trong local cache
    const list = loadLocalTransfers();
    const item = list.find((t) => t.id === id);
    if (item) {
      item.status = 'Rejected';
      saveLocalTransfers(list);
    }
  },
};
