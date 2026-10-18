export interface InventoryBalanceDto {
  id?: string;
  branchId?: string;
  branchName: string;
  warehouseId: string;
  warehouseName: string;
  skuId: string;
  skuCode: string;
  productName: string;
  variantName: string;
  onHand: number;
  reserved: number;
  available?: number;
  averageCost: number;
  totalValue?: number;
  reorderPoint: number;
}

export type LedgerDirection = 'IN' | 'OUT';

export interface InventoryLedgerEntryDto {
  id: string;
  createdAt: string;
  warehouseId: string;
  warehouseName: string;
  skuId: string;
  skuCode: string;
  productName: string;
  direction: LedgerDirection;
  operationType: string;
  quantityDelta: number;
  balanceAfter: number;
  referenceCode: string;
}

export type InventoryLedgerDto = InventoryLedgerEntryDto;

export interface CreateGoodsReceiptLineRequest {
  skuId: string;
  quantity: number;
  unitCost: number;
}

export type StockReceiptItemRequest = CreateGoodsReceiptLineRequest;

export interface CreateStockReceiptRequest {
  supplierId: string;
  warehouseId: string;
  note?: string | null;
  lines: CreateGoodsReceiptLineRequest[];
  items?: CreateGoodsReceiptLineRequest[];
}

export interface GoodsReceiptLineResponse {
  id: string;
  skuId: string;
  skuCode: string;
  productName: string;
  quantity: number;
  unitCost: number;
  lineTotal: number;
}

export interface GoodsReceiptResponse {
  id: string;
  code: string;
  supplierId: string;
  supplierName: string;
  warehouseId: string;
  warehouseName: string;
  status: 'Draft' | 'Confirmed' | 'Cancelled';
  note?: string | null;
  totalAmount: number;
  createdAt: string;
  confirmedAt?: string | null;
  lines: GoodsReceiptLineResponse[];
}

export type StockReceiptDto = GoodsReceiptResponse;
