export interface ReportFilter {
  branchId?: string;
  channel?: string;
  startDate?: string;
  endDate?: string;
  topN?: number;
}

export interface ChannelRevenueBreakdown {
  channel: string;
  revenue: number;
  costOfGoodsSold: number;
  grossProfit: number;
  grossMarginPercentage: number;
  orderCount: number;
}

export interface BranchRevenueBreakdown {
  branchId: string;
  branchName: string;
  revenue: number;
  costOfGoodsSold: number;
  grossProfit: number;
  orderCount: number;
}

export interface DailyRevenueBreakdown {
  date: string;
  revenue: number;
  costOfGoodsSold: number;
  grossProfit: number;
  orderCount: number;
}

export interface RevenueProfitReport {
  totalNetRevenue: number;
  totalCostOfGoodsSold: number;
  totalGrossProfit: number;
  grossMarginPercentage: number;
  totalOrders: number;
  totalItemsSold: number;
  byChannel: ChannelRevenueBreakdown[];
  byBranch: BranchRevenueBreakdown[];
  dailyTrend: DailyRevenueBreakdown[];
}

export interface SkuVelocityItem {
  skuId: string;
  skuCode: string;
  skuName: string;
  quantitySold: number;
  totalRevenue: number;
  currentOnHand: number;
  daysWithoutSale: number;
}

export interface InventoryVelocityReport {
  totalInventoryValue: number;
  totalOnHandQuantity: number;
  totalReservedQuantity: number;
  totalAvailableQuantity: number;
  bestSellers: SkuVelocityItem[];
  slowMovingItems: SkuVelocityItem[];
}

export type StockAlertLevel = 'Warning' | 'Critical' | 'OutOfStock';

export interface StockAlert {
  skuId: string;
  skuCode: string;
  skuName: string;
  branchId: string;
  branchName: string;
  onHand: number;
  reserved: number;
  available: number;
  reorderPoint: number;
  alertLevel: StockAlertLevel | string;
}
