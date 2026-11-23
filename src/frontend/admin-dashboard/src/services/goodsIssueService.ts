import { httpClient } from './httpClient';
import type { GoodsIssue, PosDailyGoodsIssueSummary } from '../types/goodsIssues';

export const goodsIssueService = {
  async getByOrderId(orderId: string): Promise<GoodsIssue[]> {
    const res = await httpClient.get<GoodsIssue[]>(`/api/goods-issues/by-order/${orderId}`);
    return res.data || [];
  },

  async getByOrderIds(orderIds: string[]): Promise<GoodsIssue[]> {
    const res = await httpClient.post<GoodsIssue[]>('/api/goods-issues/by-orders', { orderIds });
    return res.data || [];
  },

  async getById(id: string): Promise<GoodsIssue> {
    const res = await httpClient.get<GoodsIssue>(`/api/goods-issues/${id}`);
    return res.data;
  },

  async getPosDailySummary(params: { date: string; branchId?: string }): Promise<PosDailyGoodsIssueSummary> {
    const query: Record<string, string> = { date: params.date };
    if (params.branchId) {
      query.branchId = params.branchId;
    }
    const res = await httpClient.get<PosDailyGoodsIssueSummary>('/api/goods-issues/pos-daily-summary', {
      params: query,
    });
    return res.data;
  },
};
