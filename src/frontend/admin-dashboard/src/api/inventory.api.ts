import { apiClient } from './client';
import type {
  CreateStockReceiptRequest,
  GoodsReceiptResponse,
  InventoryBalanceDto,
  InventoryLedgerEntryDto,
  LedgerDirection,
} from '../types/inventory.types';

export const inventoryApi = {
  // Inventory Balances (/api/inventory/balances)
  async getBalances(params?: {
    branchId?: string;
    warehouseId?: string;
    search?: string;
    lowStockOnly?: boolean;
  }): Promise<InventoryBalanceDto[]> {
    const cleanParams: Record<string, string | boolean> = {};
    if (params?.branchId && params.branchId !== 'all') cleanParams.branchId = params.branchId;
    if (params?.warehouseId && params.warehouseId !== 'all') cleanParams.warehouseId = params.warehouseId;
    if (params?.search?.trim()) cleanParams.search = params.search.trim();
    if (params?.lowStockOnly) cleanParams.lowStockOnly = true;

    const response = await apiClient.get<InventoryBalanceDto[]>('/inventory/balances', {
      params: cleanParams,
    });
    return response.data;
  },

  // Read-only Append-Only Inventory Ledger (/api/inventory/ledger)
  async getLedger(params?: {
    warehouseId?: string;
    skuId?: string;
    direction?: LedgerDirection | 'all';
    operationType?: string;
    transactionType?: string;
    search?: string;
  }): Promise<InventoryLedgerEntryDto[]> {
    const cleanParams: Record<string, string> = {};
    if (params?.warehouseId && params.warehouseId !== 'all') cleanParams.warehouseId = params.warehouseId;
    if (params?.skuId) cleanParams.skuId = params.skuId;
    if (params?.direction && params.direction !== 'all') cleanParams.direction = params.direction;
    const op = params?.operationType || params?.transactionType;
    if (op && op !== 'all') {
      cleanParams.operationType = op;
    }
    if (params?.search?.trim()) cleanParams.search = params.search.trim();

    const response = await apiClient.get<InventoryLedgerEntryDto[]>('/inventory/ledger', {
      params: cleanParams,
    });
    return response.data;
  },

  // Goods Receipts (/api/inventory/receipts)
  async getReceipts(params?: {
    warehouseId?: string;
    supplierId?: string;
    status?: string;
  }): Promise<GoodsReceiptResponse[]> {
    const cleanParams: Record<string, string> = {};
    if (params?.warehouseId) cleanParams.warehouseId = params.warehouseId;
    if (params?.supplierId) cleanParams.supplierId = params.supplierId;
    if (params?.status) cleanParams.status = params.status;

    const response = await apiClient.get<GoodsReceiptResponse[]>('/inventory/receipts', {
      params: cleanParams,
    });
    return response.data;
  },

  async createReceipt(payload: CreateStockReceiptRequest): Promise<GoodsReceiptResponse> {
    const response = await apiClient.post<GoodsReceiptResponse>('/inventory/receipts', {
      ...payload,
      items: payload.lines,
    });
    return response.data;
  },

  async confirmReceipt(id: string): Promise<{ message: string; receipt?: GoodsReceiptResponse }> {
    const response = await apiClient.post<{ message: string; receipt?: GoodsReceiptResponse }>(
      `/inventory/receipts/${id}/confirm`
    );
    return response.data;
  },

  async updateReorderPoint(payload: {
    warehouseId?: string;
    branchId?: string;
    skuId: string;
    reorderPoint: number;
  }): Promise<{ message: string }> {
    const response = await apiClient.put<{ message: string }>(
      '/inventory/balances/reorder-point',
      payload
    );
    return response.data;
  },
};
