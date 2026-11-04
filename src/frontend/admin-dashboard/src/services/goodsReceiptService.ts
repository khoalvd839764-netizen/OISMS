import { httpClient } from './httpClient';
import type {
  CreateGoodsReceiptDto,
  GoodsReceiptFilter,
  GoodsReceiptResponse,
  PagedGoodsReceiptResult,
} from '../types/goodsReceipt';

export const goodsReceiptService = {
  async getGoodsReceipts(filter?: GoodsReceiptFilter): Promise<PagedGoodsReceiptResult> {
    const params: Record<string, any> = {};
    if (filter?.status) params.status = filter.status;
    if (filter?.supplierId) params.supplierId = filter.supplierId;
    if (filter?.branchId) params.branchId = filter.branchId;
    if (filter?.warehouseId) params.warehouseId = filter.warehouseId;
    if (filter?.purchaseOrderId) params.purchaseOrderId = filter.purchaseOrderId;
    if (filter?.fromDate) params.fromDate = filter.fromDate;
    if (filter?.toDate) params.toDate = filter.toDate;
    if (filter?.pageIndex) params.pageIndex = filter.pageIndex;
    if (filter?.pageSize) params.pageSize = filter.pageSize;

    const res = await httpClient.get<PagedGoodsReceiptResult>('/api/goods-receipts', { params });
    return res.data;
  },

  async getGoodsReceiptById(id: string): Promise<GoodsReceiptResponse> {
    const res = await httpClient.get<GoodsReceiptResponse>(`/api/goods-receipts/${id}`);
    return res.data;
  },

  async createGoodsReceipt(dto: CreateGoodsReceiptDto): Promise<GoodsReceiptResponse> {
    const res = await httpClient.post<GoodsReceiptResponse>('/api/goods-receipts', dto);
    return res.data;
  },

  async confirmGoodsReceipt(id: string): Promise<GoodsReceiptResponse> {
    const res = await httpClient.post<GoodsReceiptResponse>(`/api/goods-receipts/${id}/confirm`);
    return res.data;
  },

  async cancelGoodsReceipt(id: string): Promise<GoodsReceiptResponse> {
    const res = await httpClient.post<GoodsReceiptResponse>(`/api/goods-receipts/${id}/cancel`);
    return res.data;
  },
};
