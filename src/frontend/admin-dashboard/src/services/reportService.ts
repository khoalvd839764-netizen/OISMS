import { httpClient } from './httpClient';
import type {
  ReportFilter,
  RevenueProfitReport,
  InventoryVelocityReport,
  StockAlert,
  DailySalesSummaryComparison,
  RebuildDailySummaryRequest,
  RebuildDailySummaryResponse,
} from '../types/reports';

export const reportService = {
  /**
   * Báo cáo Doanh thu & Lợi nhuận (P&L - Gross Profit)
   * GET /api/reports/revenue-profit
   */
  async getRevenueProfit(filter: ReportFilter): Promise<RevenueProfitReport> {
    const search = new URLSearchParams();
    if (filter.branchId) search.append('branchId', filter.branchId);
    if (filter.channel) search.append('channel', filter.channel);
    if (filter.startDate) search.append('startDate', filter.startDate);
    if (filter.endDate) search.append('endDate', filter.endDate);
    if (filter.skuIds && filter.skuIds.length > 0) {
      for (const id of filter.skuIds) {
        if (id) search.append('skuIds', id);
      }
    }

    const query = search.toString();
    const url = query ? `/api/reports/revenue-profit?${query}` : '/api/reports/revenue-profit';
    const response = await httpClient.get<RevenueProfitReport>(url);
    return response.data;
  },

  /**
   * Báo cáo Vận tốc tồn kho & Luân chuyển hàng hóa
   * GET /api/reports/inventory-velocity
   */
  async getInventoryVelocity(filter: ReportFilter): Promise<InventoryVelocityReport> {
    const params: Record<string, any> = {};
    if (filter.branchId) params.branchId = filter.branchId;
    if (filter.channel) params.channel = filter.channel;
    if (filter.startDate) params.startDate = filter.startDate;
    if (filter.endDate) params.endDate = filter.endDate;
    if (filter.topN) params.topN = filter.topN;

    const response = await httpClient.get<InventoryVelocityReport>('/api/reports/inventory-velocity', {
      params,
    });
    return response.data;
  },

  /**
   * Báo cáo Cảnh báo tồn kho chạm ngưỡng (Stock Alerts)
   * GET /api/reports/stock-alerts
   */
  async getStockAlerts(branchId?: string): Promise<StockAlert[]> {
    const params: Record<string, any> = {};
    if (branchId) params.branchId = branchId;

    const response = await httpClient.get<StockAlert[]>('/api/reports/stock-alerts', {
      params,
    });
    return response.data;
  },

  /**
   * Đối soát bảng tổng hợp ngày daily_sales_summaries với báo cáo trực tiếp (chỉ Owner)
   * GET /api/reports/daily-summaries
   */
  async getDailySummaries(filter: {
    fromDate: string;
    toDate: string;
    branchId?: string;
  }): Promise<DailySalesSummaryComparison> {
    const params: Record<string, string> = {
      fromDate: filter.fromDate,
      toDate: filter.toDate,
    };
    if (filter.branchId) params.branchId = filter.branchId;

    const response = await httpClient.get<DailySalesSummaryComparison>('/api/reports/daily-summaries', {
      params,
    });
    return response.data;
  },

  /**
   * Tính lại thủ công bảng tổng hợp theo khoảng ngày (tối đa 31 ngày, chỉ Owner)
   * POST /api/reports/daily-summaries/rebuild
   */
  async rebuildDailySummaries(payload: RebuildDailySummaryRequest): Promise<RebuildDailySummaryResponse> {
    const response = await httpClient.post<RebuildDailySummaryResponse>(
      '/api/reports/daily-summaries/rebuild',
      payload
    );
    return response.data;
  },
};
