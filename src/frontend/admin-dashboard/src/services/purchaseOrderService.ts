import { httpClient } from './httpClient';
import type {
  ApprovePurchaseOrderDto,
  ClosePurchaseOrderDto,
  CreatePurchaseOrderDto,
  PagedPurchaseOrderResult,
  PurchaseOrderFilter,
  PurchaseOrderPriceSuggestion,
  PurchaseOrderPriceSuggestionQuery,
  PurchaseOrderResponse,
  ReturnPurchaseOrderDto,
  UpdatePurchaseOrderDraftDto,
} from '../types/purchaseOrder';

export const purchaseOrderService = {
  async getPurchaseOrders(filter?: PurchaseOrderFilter): Promise<PagedPurchaseOrderResult> {
    const params: Record<string, string | number> = {};
    if (filter?.status && filter.status !== 'all') params.status = filter.status;
    if (filter?.supplierId && filter.supplierId !== 'all') params.supplierId = filter.supplierId;
    if (filter?.branchId && filter.branchId !== 'all') params.branchId = filter.branchId;
    if (filter?.pageIndex) params.pageIndex = filter.pageIndex;
    if (filter?.pageSize) params.pageSize = filter.pageSize;

    const res = await httpClient.get<PagedPurchaseOrderResult>('/api/purchase-orders', { params });
    return res.data;
  },

  async getPurchaseOrderById(id: string): Promise<PurchaseOrderResponse> {
    const res = await httpClient.get<PurchaseOrderResponse>(`/api/purchase-orders/${id}`);
    return res.data;
  },

  async getPriceSuggestion(
    query: PurchaseOrderPriceSuggestionQuery
  ): Promise<PurchaseOrderPriceSuggestion> {
    const params: Record<string, string> = {
      branchId: query.branchId,
      skuId: query.skuId,
    };
    if (query.supplierId) params.supplierId = query.supplierId;
    if (query.unitId) params.unitId = query.unitId;

    const res = await httpClient.get<PurchaseOrderPriceSuggestion>(
      '/api/purchase-orders/price-suggestion',
      { params }
    );
    return res.data;
  },

  async createPurchaseOrder(
    dto: CreatePurchaseOrderDto,
    idempotencyKey: string
  ): Promise<{ data: PurchaseOrderResponse; status: number }> {
    const res = await httpClient.post<PurchaseOrderResponse>('/api/purchase-orders', dto, {
      headers: {
        'Idempotency-Key': idempotencyKey,
      },
    });
    return { data: res.data, status: res.status };
  },

  async updateDraftPurchaseOrder(
    id: string,
    dto: UpdatePurchaseOrderDraftDto
  ): Promise<PurchaseOrderResponse> {
    const res = await httpClient.put<PurchaseOrderResponse>(`/api/purchase-orders/${id}`, dto);
    return res.data;
  },

  async submitPurchaseOrder(id: string): Promise<PurchaseOrderResponse> {
    const res = await httpClient.post<PurchaseOrderResponse>(`/api/purchase-orders/${id}/submit`);
    return res.data;
  },

  async returnPurchaseOrder(id: string, reason: string): Promise<PurchaseOrderResponse> {
    const payload: ReturnPurchaseOrderDto = { reason };
    const res = await httpClient.post<PurchaseOrderResponse>(
      `/api/purchase-orders/${id}/return`,
      payload
    );
    return res.data;
  },

  async approvePurchaseOrder(
    id: string,
    confirmZeroPrice = false,
    version?: number
  ): Promise<PurchaseOrderResponse> {
    const payload: ApprovePurchaseOrderDto = { confirmZeroPrice, version };
    const res = await httpClient.post<PurchaseOrderResponse>(
      `/api/purchase-orders/${id}/approve`,
      payload
    );
    return res.data;
  },

  async closePurchaseOrder(
    id: string,
    reason: string
  ): Promise<PurchaseOrderResponse> {
    const payload: ClosePurchaseOrderDto = { reason };
    const res = await httpClient.post<PurchaseOrderResponse>(
      `/api/purchase-orders/${id}/close`,
      payload
    );
    return res.data;
  },

  async cancelPurchaseOrder(id: string): Promise<PurchaseOrderResponse> {
    const res = await httpClient.post<PurchaseOrderResponse>(
      `/api/purchase-orders/${id}/cancel`
    );
    return res.data;
  },
};
