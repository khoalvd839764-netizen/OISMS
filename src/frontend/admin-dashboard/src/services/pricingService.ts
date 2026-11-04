import { httpClient } from './httpClient';
import type {
  PriceList,
  CreatePriceChangeRequest,
  PriceChangeRequestResponse,
  EffectivePrice,
  PriceHistoryItem,
} from '../types/pricing';

export const pricingService = {
  /**
   * Lấy danh sách các bảng giá (retail, wholesale)
   */
  async getPriceLists(): Promise<PriceList[]> {
    const res = await httpClient.get<PriceList[]>('/api/price-lists');
    return res.data;
  },

  /**
   * Khởi tạo phiếu đổi giá mới (Idempotency-Key bắt buộc do client gửi lên)
   */
  async createPriceChangeRequest(
    payload: CreatePriceChangeRequest,
    idempotencyKey: string
  ): Promise<PriceChangeRequestResponse> {
    const res = await httpClient.post<PriceChangeRequestResponse>(
      '/api/price-change-requests',
      payload,
      {
        headers: {
          'Idempotency-Key': idempotencyKey,
          'X-Request-Key': idempotencyKey,
        },
      }
    );
    return res.data;
  },

  /**
   * Tra cứu danh sách phiếu đổi giá có phân trang và lọc theo trạng thái
   */
  async getRequests(
    status?: string,
    page = 1,
    pageSize = 20
  ): Promise<{ items: PriceChangeRequestResponse[]; totalCount: number; page: number; pageSize: number }> {
    const params: Record<string, any> = { page, pageSize };
    if (status && status !== 'all') {
      params.status = status;
    }
    const res = await httpClient.get<{
      items: PriceChangeRequestResponse[];
      totalCount: number;
      page: number;
      pageSize: number;
    }>('/api/price-change-requests', { params });
    return res.data;
  },

  /**
   * Xem chi tiết một phiếu đổi giá theo ID
   */
  async getRequestById(id: string): Promise<PriceChangeRequestResponse> {
    const res = await httpClient.get<PriceChangeRequestResponse>(`/api/price-change-requests/${id}`);
    return res.data;
  },

  /**
   * Duyệt phiếu đổi giá (Chỉ dành cho Owner)
   */
  async approveRequest(id: string): Promise<PriceChangeRequestResponse> {
    const res = await httpClient.post<PriceChangeRequestResponse>(
      `/api/price-change-requests/${id}/approve`
    );
    return res.data;
  },

  /**
   * Từ chối phiếu đổi giá (Chỉ dành cho Owner)
   */
  async rejectRequest(id: string, note?: string): Promise<PriceChangeRequestResponse> {
    const res = await httpClient.post<PriceChangeRequestResponse>(
      `/api/price-change-requests/${id}/reject`,
      { note: note || '' }
    );
    return res.data;
  },

  /**
   * Hủy dòng giá đã lên lịch trong tương lai chưa đến thời điểm hiệu lực (Chỉ Owner)
   */
  async cancelScheduledPrice(id: string, reason?: string): Promise<{ message: string }> {
    const res = await httpClient.post<{ message: string }>(
      `/api/sku-unit-prices/${id}/cancel`,
      { reason: reason || '' }
    );
    return res.data;
  },

  /**
   * Tra cứu giá bán có hiệu lực của SKU tại một thời điểm (mặc định hiện tại)
   */
  async getEffectivePrice(
    skuId: string,
    priceListCode?: string,
    at?: string
  ): Promise<EffectivePrice[]> {
    const params: Record<string, any> = { skuId };
    if (priceListCode) params.priceListCode = priceListCode;
    if (at) params.at = at;
    const res = await httpClient.get<EffectivePrice[]>('/api/sku-unit-prices/effective', { params });
    return res.data;
  },

  /**
   * Xem lịch sử thay đổi giá của SKU và đơn vị tính
   */
  async getPriceHistory(
    skuId: string,
    unitId?: string,
    priceListCode?: string
  ): Promise<PriceHistoryItem[]> {
    const params: Record<string, any> = { skuId };
    if (unitId) params.unitId = unitId;
    if (priceListCode) params.priceListCode = priceListCode;
    const res = await httpClient.get<PriceHistoryItem[]>('/api/sku-unit-prices/history', { params });
    return res.data;
  },
};
