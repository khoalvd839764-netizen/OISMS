export type PurchaseOrderStatus =
  | 'Draft'
  | 'Approved'
  | 'PartiallyReceived'
  | 'Received'
  | 'Closed'
  | 'Cancelled';

export interface PurchaseOrderItemResponse {
  id: string; // purchaseOrderItemId
  skuId: string;
  skuCode: string;
  productName: string;
  unitId: string | null;
  unitName: string;
  factorSnapshot: number;
  quantityDecimalPlaces: number;
  quantity: number;
  orderedBaseQuantity: number;
  unitPrice: number;
  receivedBaseQuantity: number;
  remainingBaseQuantity: number;
  remainingQuantity: number;
  lineTotal: number;
}

export interface PurchaseOrderResponse {
  id: string;
  number: string;
  requestKey: string;
  supplierId: string;
  supplierName: string;
  branchId: string;
  branchName: string;
  status: PurchaseOrderStatus;
  note?: string | null;
  totalAmount: number;
  createdBy: string;
  createdByName?: string | null;
  approvedBy?: string | null;
  approvedByName?: string | null;
  approvedAt?: string | null;
  closedBy?: string | null;
  closedByName?: string | null;
  closedAt?: string | null;
  closeReason?: string | null;
  cancelledBy?: string | null;
  cancelledByName?: string | null;
  cancelledAt?: string | null;
  createdAt: string;
  updatedAt?: string | null;
  isReplay?: boolean;
  items: PurchaseOrderItemResponse[];
}

export interface CreatePurchaseOrderItemDto {
  skuId: string;
  unitId?: string | null;
  quantity: number;
  unitPrice: number;
}

export interface CreatePurchaseOrderDto {
  supplierId: string;
  branchId: string;
  number?: string | null;
  note?: string | null;
  items: CreatePurchaseOrderItemDto[];
}

export interface UpdatePurchaseOrderDraftDto {
  supplierId: string;
  branchId: string;
  number?: string | null;
  note?: string | null;
  items: CreatePurchaseOrderItemDto[];
}

export interface ApprovePurchaseOrderDto {
  confirmZeroPrice: boolean;
}

export interface ClosePurchaseOrderDto {
  reason: string;
}

export interface PurchaseOrderFilter {
  status?: string;
  supplierId?: string;
  branchId?: string;
  pageIndex?: number;
  pageSize?: number;
}

export interface PagedPurchaseOrderResult {
  items: PurchaseOrderResponse[];
  pageIndex: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}
