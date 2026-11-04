export type StockCountStatus = 'Draft' | 'Counting' | 'PendingApproval' | 'Confirmed' | 'Cancelled';

export const STOCK_COUNT_STATUS_LABEL: Record<StockCountStatus, string> = {
  Draft: 'Bản nháp',
  Counting: 'Đang kiểm đếm',
  PendingApproval: 'Chờ duyệt chênh lệch',
  Confirmed: 'Đã cân bằng (Hoàn tất)',
  Cancelled: 'Đã hủy',
};

export const STOCK_COUNT_STATUS_COLOR: Record<StockCountStatus, { bg: string; text: string; border: string }> = {
  Draft: { bg: 'bg-gray-100', text: 'text-gray-700', border: 'border-gray-300' },
  Counting: { bg: 'bg-amber-100', text: 'text-amber-800', border: 'border-amber-300' },
  PendingApproval: { bg: 'bg-blue-100', text: 'text-blue-800', border: 'border-blue-300' },
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
  countedBy?: string | null;
}

export interface StockCountApproval {
  id: string;
  stockCountId: string;
  decision: string;
  note?: string;
  actedBy: string;
  actedByName?: string;
  actedAt: string;
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
  countType?: string;
  adHocReason?: string;
  periodId?: string;
  submittedBy?: string;
  submittedAt?: string;
  createdBy: string;
  confirmedBy?: string;
  confirmedAt?: string;
  note?: string;
  createdAt: string;
  generatedAdjustmentId?: string;
  isReplay?: boolean;
  items: StockCountItem[];
  approvals?: StockCountApproval[];
}

export interface CreateStockCountRequest {
  requestKey?: string;
  branchId: string;
  warehouseId: string;
  skuIds?: string[] | null;
  reason?: string;
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

export interface SubmitStockCountRequest {
  note?: string;
}

export interface RecountStockCountRequest {
  skuIds: string[];
}

export interface RequestRecountRequest {
  skuIds: string[];
  reason: string;
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

export interface StockCountSchedule {
  id: string;
  tenantId: string;
  branchId: string;
  branchName?: string;
  warehouseId: string;
  warehouseName?: string;
  periodType: 'MidMonth' | 'EndOfMonth';
  dayOfMonth?: number;
  localTime: string;
  dueDays: number;
  assigneeUserId: string;
  assigneeName?: string;
  isActive: boolean;
  createdAt: string;
}

export interface CreateStockCountScheduleRequest {
  branchId: string;
  warehouseId: string;
  periodType: 'MidMonth' | 'EndOfMonth';
  dayOfMonth?: number;
  localTime?: string;
  dueDays?: number;
  assigneeUserId: string;
  isActive?: boolean;
}

export interface UpdateStockCountScheduleRequest {
  dayOfMonth?: number;
  localTime?: string;
  dueDays?: number;
  assigneeUserId?: string;
  isActive?: boolean;
}

export interface StockCountScheduleFilter {
  branchId?: string;
  warehouseId?: string;
  periodType?: string;
  isActive?: boolean;
  pageNumber?: number;
  pageSize?: number;
}

export interface StockCountPeriod {
  id: string;
  tenantId: string;
  scheduleId: string;
  branchId: string;
  branchName?: string;
  warehouseId: string;
  warehouseName?: string;
  periodType: string;
  periodKey: string;
  scheduledAt: string;
  dueAt: string;
  assigneeUserId: string;
  assigneeName?: string;
  status: 'Planned' | 'InProgress' | 'Completed' | 'Cancelled';
  completedAt?: string;
  createdAt: string;
  isOverdue: boolean;
  stockCountId?: string;
  stockCountNumber?: string;
  stockCountStatus?: string;
  differenceSkuCount: number;
}

export interface StockCountPeriodFilter {
  branchId?: string;
  warehouseId?: string;
  status?: string;
  overdue?: boolean;
  from?: string;
  to?: string;
  pageNumber?: number;
  pageSize?: number;
}
