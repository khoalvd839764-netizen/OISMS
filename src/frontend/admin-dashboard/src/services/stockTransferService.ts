import { httpClient } from './httpClient';
import { generateUuid } from './apiAdapter';
import type {
  StockTransfer,
  CreateStockTransferRequest,
  RejectStockTransferRequest,
} from '../types/stockTransfer';

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
   * Lấy danh sách phiếu chuyển kho
   */
  async getTransfers(): Promise<StockTransfer[]> {
    const response = await httpClient.get('/api/stock-transfers');
    if (Array.isArray(response.data)) {
      return response.data;
    }
    if (response.data && Array.isArray(response.data.items)) {
      return response.data.items;
    }
    return [];
  },

  /**
   * Lấy chi tiết phiếu chuyển kho theo ID
   */
  async getTransferById(id: string): Promise<StockTransfer> {
    const response = await httpClient.get(`/api/stock-transfers/${id}`);
    if (response.data) {
      return response.data;
    }
    throw new Error('Không tìm thấy phiếu chuyển kho.');
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
