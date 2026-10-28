export type StockCountStatus = 'Draft' | 'Counting' | 'Confirmed' | 'Cancelled';

export const STOCK_COUNT_STATUS_LABEL: Record<StockCountStatus, string> = {
  Draft: 'Bản nháp',
  Counting: 'Đang kiểm đếm',
  Confirmed: 'Đã cân bằng (Hoàn tất)',
  Cancelled: 'Đã hủy',
};

export const STOCK_COUNT_STATUS_COLOR: Record<StockCountStatus, { bg: string; text: string; border: string }> = {
  Draft: { bg: 'bg-gray-100', text: 'text-gray-700', border: 'border-gray-300' },
  Counting: { bg: 'bg-amber-100', text: 'text-amber-800', border: 'border-amber-300' },
  Confirmed: { bg: 'bg-emerald-100', text: 'text-emerald-800', border: 'border-emerald-300' },
  Cancelled: { bg: 'bg-rose-100', text: 'text-rose-800', border: 'border-rose-300' },
};

export interface StockCountItem {
  id: string;
  skuId: string;
  skuCode: string;
  productName: string;
  expectedQuantity: number;
  countedQuantity?: number | null;
  differenceQuantity?: number | null;
  balanceSequenceAtSnapshot: number;
  adjustmentUnitCost?: number | null;
}

export interface StockCount {
  id: string;
  number: string;
  requestKey?: string;
  branchId: string;
  branchName?: string;
  warehouseId: string;
  warehouseName?: string;
  status: StockCountStatus;
  createdBy: string;
  confirmedBy?: string;
  confirmedAt?: string;
  note?: string;
  createdAt: string;
  generatedAdjustmentId?: string;
  isReplay?: boolean;
  items: StockCountItem[];
}

export interface CreateStockCountRequest {
  requestKey?: string;
  branchId: string;
  warehouseId: string;
  skuIds?: string[] | null;
  note?: string;
}

export interface RecordCountItem {
  skuId: string;
  countedQuantity: number;
  adjustmentUnitCost?: number | null;
}

export interface RecordStockCountBatchRequest {
  items: RecordCountItem[];
}

export interface ConfirmStockCountRequest {
  note?: string;
  skuAdjustmentCosts?: Record<string, number>;
}

export interface CancelStockCountRequest {
  reason?: string;
}

export interface StockCountPagedResult {
  items: StockCount[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface StockCountFilter {
  branchId?: string;
  warehouseId?: string;
  status?: StockCountStatus | '';
  pageNumber?: number;
  pageIndex?: number;
  pageSize?: number;
}
