export interface ReorderRecommendationQuery {
  branchId?: string;
  leadTimeDays?: number;
  safetyStockDays?: number;
  onlyNeedsReorder?: boolean;
}

export interface ReorderRecommendation {
  skuId: string;
  skuCode: string;
  skuName: string;
  baseUnit: string;
  branchId: string;
  branchName: string;
  onHandQuantity: number;
  reservedQuantity: number;
  availableQuantity: number;
  reorderPoint: number;
  dailySalesVelocity: number;
  daysUntilStockout: number;
  leadTimeDays: number;
  safetyStockDays: number;
  needsReorder: boolean;
  suggestedQuantity: number;
  confidenceLevel: 'High' | 'Medium' | 'Low' | string;
  reason: string;
}

export interface ReorderRecommendationSummary {
  tenantId: string;
  branchId?: string | null;
  evaluatedAt: string;
  totalSkusAnalyzed: number;
  skusNeedingReorder: number;
  totalSuggestedQuantity: number;
  recommendations: ReorderRecommendation[];
}
