export interface InventoryAdjustmentItem {
  id: string;
  adjustmentId: string;
  skuId: string;
  skuCode: string;
  skuName: string;
  baseUnitName: string;
  quantityDelta: number;
  direction: 'IN' | 'OUT' | string;
  unitCost: number | null;
  totalCost: number | null;
  originalTransactionId?: string | null;
}

export interface InventoryAdjustment {
  id: string;
  number: string;
  branchId: string;
  branchName: string;
  warehouseId: string;
  warehouseName: string;
  reason: string;
  status: string;
  direction: 'IN' | 'OUT' | 'MIXED' | string;
  stockCountId?: string | null;
  stockCountNumber?: string | null;
  createdBy: string;
  createdByName?: string | null;
  confirmedBy?: string | null;
  confirmedByName?: string | null;
  confirmedAt?: string | null;
  createdAt: string;
  updatedAt?: string | null;
  totalQuantityDelta: number;
  totalCost: number | null;
  items: InventoryAdjustmentItem[];
}
