export type PurchaseOrderStatus =
  | 'Draft'
  | 'PendingApproval'
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
  unitId?: string | null;
  unitName: string;
  factorSnapshot: number;
  quantityDecimalPlaces: number;
  quantity: number;
  orderedBaseQuantity: number;
  unitPrice: number | null;
  allocatedUnitCost?: number | null;
  receivedBaseQuantity: number;
  remainingBaseQuantity: number;
  receivedQuantity?: number;
  remainingQuantity: number;
  lineTotal: number | null;
  allocatedLineTotal?: number | null;
}

export interface PurchaseOrderResponse {
  id: string;
  number: string;
  requestKey: string;
  supplierId: string | null;
  supplierName: string | null;
  branchId: string;
  branchName: string;
  orderDate?: string;
  status: PurchaseOrderStatus;
  note?: string | null;
  subtotal?: number | null;
  shippingFee?: number | null;
  discountAmount?: number | null;
  vatAmount?: number | null;
  totalAllocatedCost?: number | null;
  totalAmount: number | null;
  version: number;
  createdBy: string;
  createdByName?: string | null;
  approvedBy?: string | null;
  approvedByName?: string | null;
  approvedAt?: string | null;
  returnedBy?: string | null;
  returnedByName?: string | null;
  returnedAt?: string | null;
  returnReason?: string | null;
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
  unitPrice?: number | null;
}

export interface CreatePurchaseOrderDto {
  supplierId?: string | null;
  branchId: string;
  orderDate?: string;
  shippingFee?: number;
  discountAmount?: number;
  vatAmount?: number;
  number?: string | null;
  note?: string | null;
  items: CreatePurchaseOrderItemDto[];
}

export interface UpdatePurchaseOrderDraftDto {
  supplierId?: string | null;
  branchId: string;
  orderDate?: string;
  shippingFee?: number;
  discountAmount?: number;
  vatAmount?: number;
  version?: number;
  number?: string | null;
  note?: string | null;
  items: CreatePurchaseOrderItemDto[];
}

export interface ApprovePurchaseOrderDto {
  confirmZeroPrice: boolean;
  version?: number;
}

export interface ReturnPurchaseOrderDto {
  reason: string;
}

export interface ClosePurchaseOrderDto {
  reason: string;
}

export interface PurchaseOrderPriceSuggestionQuery {
  supplierId?: string | null;
  branchId: string;
  skuId: string;
  unitId?: string | null;
}

export interface PurchaseOrderPriceSuggestion {
  supplierId?: string | null;
  branchId: string;
  skuId: string;
  unitId?: string | null;
  unitName: string;
  factorToBase: number;
  suggestedUnitPrice?: number | null;
  lastAllocatedUnitCost?: number | null;
  lastPurchaseOrderId?: string | null;
  lastPurchaseOrderNumber?: string | null;
  lastOrderDate?: string | null;
  lastApprovedAt?: string | null;
  currentBaseWac: number;
  currentUnitWac: number;
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
