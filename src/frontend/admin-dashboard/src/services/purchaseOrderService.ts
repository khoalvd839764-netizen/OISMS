import { httpClient } from './httpClient';
import type {
  CreatePurchaseOrderDto,
  PagedPurchaseOrderResult,
  PurchaseOrderFilter,
  PurchaseOrderResponse,
  UpdatePurchaseOrderDraftDto,
} from '../types/purchaseOrder';

export const purchaseOrderService = {
  async getPurchaseOrders(filter?: PurchaseOrderFilter): Promise<PagedPurchaseOrderResult> {
    const params: Record<string, any> = {};
    if (filter?.status) params.status = filter.status;
    if (filter?.supplierId) params.supplierId = filter.supplierId;
    if (filter?.branchId) params.branchId = filter.branchId;
    if (filter?.pageIndex) params.pageIndex = filter.pageIndex;
    if (filter?.pageSize) params.pageSize = filter.pageSize;

    const res = await httpClient.get<PagedPurchaseOrderResult>('/api/purchase-orders', { params });
    return res.data;
  },

  async getPurchaseOrderById(id: string): Promise<PurchaseOrderResponse> {
    const res = await httpClient.get<PurchaseOrderResponse>(`/api/purchase-orders/${id}`);
    return res.data;
  },

  async createPurchaseOrder(
    dto: CreatePurchaseOrderDto,
    idempotencyKey: string
  ): Promise<PurchaseOrderResponse> {
    const res = await httpClient.post<PurchaseOrderResponse>('/api/purchase-orders', dto, {
      headers: {
        'Idempotency-Key': idempotencyKey,
      },
    });
    return res.data;
  },

  async updatePurchaseOrder(
    id: string,
    dto: UpdatePurchaseOrderDraftDto
  ): Promise<PurchaseOrderResponse> {
    const res = await httpClient.put<PurchaseOrderResponse>(`/api/purchase-orders/${id}`, dto);
    return res.data;
  },

  async approvePurchaseOrder(
    id: string,
    confirmZeroPrice = false
  ): Promise<PurchaseOrderResponse> {
    const res = await httpClient.post<PurchaseOrderResponse>(
      `/api/purchase-orders/${id}/approve`,
      { confirmZeroPrice }
    );
    return res.data;
  },

  async closePurchaseOrder(
    id: string,
    reason: string
  ): Promise<PurchaseOrderResponse> {
    const res = await httpClient.post<PurchaseOrderResponse>(
      `/api/purchase-orders/${id}/close`,
      { reason }
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
