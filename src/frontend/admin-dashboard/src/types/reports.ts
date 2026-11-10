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
  /** Tiền trả hàng đã xác nhận (đơn sàn: SL trả × đơn giá). */
  returnAmount: number;
  /** Doanh thu sau khi trừ hàng trả. */
  netRevenue: number;
  /** Giá vốn: Staff nhận null. */
  returnCogs: number | null;
  netCogs: number | null;
  costOfGoodsSold: number | null;
  grossProfit: number | null;
  grossMarginPercentage: number | null;
  orderCount: number;
}

export interface BranchRevenueBreakdown {
  branchId: string;
  branchName: string;
  revenue: number;
  /** Tiền trả hàng đã xác nhận (đơn sàn: SL trả × đơn giá). */
  returnAmount: number;
  /** Doanh thu sau khi trừ hàng trả. */
  netRevenue: number;
  /** Giá vốn: Staff nhận null. */
  returnCogs: number | null;
  netCogs: number | null;
  costOfGoodsSold: number | null;
  grossProfit: number | null;
  orderCount: number;
}

export interface DailyRevenueBreakdown {
  date: string;
  revenue: number;
  /** Tiền trả hàng đã xác nhận (đơn sàn: SL trả × đơn giá). */
  returnAmount: number;
  /** Doanh thu sau khi trừ hàng trả. */
  netRevenue: number;
  /** Giá vốn: Staff nhận null. */
  returnCogs: number | null;
  netCogs: number | null;
  costOfGoodsSold: number | null;
  grossProfit: number | null;
  orderCount: number;
}

export interface RevenueProfitReport {
  /** Doanh thu gộp trước khi trừ hàng trả. */
  revenue: number;
  totalRevenue: number;
  /** Tiền trả hàng đã xác nhận (đơn sàn: SL trả × đơn giá). */
  returnAmount: number;
  /** Doanh thu sau khi trừ hàng trả. */
  netRevenue: number;
  /** Giá vốn: Staff nhận null. */
  returnCogs: number | null;
  netCogs: number | null;
  costOfGoodsSold: number | null;
  grossProfit: number | null;
  /** Bộ Total* là số thuần: totalNetRevenue - totalCostOfGoodsSold = totalGrossProfit. */
  totalNetRevenue: number;
  totalCostOfGoodsSold: number | null;
  totalGrossProfit: number | null;
  grossMarginPercentage: number | null;
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
  baseUnitName?: string | null;
}

export interface UnitQuantity {
  unitName: string;
  onHand: number;
  reserved: number;
  available: number;
}

export interface InventoryVelocityReport {
  totalInventoryValue: number;
  totalOnHandQuantity: number;
  totalReservedQuantity: number;
  totalAvailableQuantity: number;
  quantityByUnit: UnitQuantity[];
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

export interface RecentOrder {
  id: string;
  number: string;
  channel: string;
  customerName?: string | null;
  totalAmount: number;
  status: string;
  createdAt: string;
}

export interface DashboardSummary {
  totalRevenue: number;
  returnAmount: number;
  netRevenue: number;
  returnCogs: number | null;
  netCogs: number | null;
  totalOrders: number;
  totalGrossProfit: number | null;
  grossMarginPercentage: number;
  skuCount: number;
  totalInventoryValue: number | null;
  lowStockCount: number;
  recentOrders: RecentOrder[];
  recentRevenueTrend: DailyRevenueBreakdown[];
}
