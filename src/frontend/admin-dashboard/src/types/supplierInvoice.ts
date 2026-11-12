export type SupplierInvoiceStatus =
  | "Draft" | "Matched" | "PendingApproval" | "Approved" | "Rejected" | "Cancelled";

export interface SupplierInvoiceLine {
  goodsReceiptItemId: string;
  quantity: number;
  unitPrice: number;
}

export interface SupplierInvoiceDraft {
  invoiceSeries: string;
  invoiceNumber: string;
  invoiceDate: string;
  vatAmount: number;
  shippingFee: number;
  items: SupplierInvoiceLine[];
}

export interface CreateSupplierInvoice extends SupplierInvoiceDraft {
  goodsReceiptId: string;
  supplierId: string;
}

export interface PurchaseVariance {
  id: string;
  varianceType: "AtReceipt" | "AfterReceipt";
  status: "Pending" | "Approved" | "Rejected";
  totalAmountDiff: number;
  decidedBy: string | null;
  decidedAt: string | null;
  decisionNote: string | null;
}

export interface ComparisonValue {
  quantity: number;
  unitPrice: number | null;
}

export interface SupplierInvoiceComparison {
  goodsReceiptItemId: string;
  purchaseOrderItemId: string | null;
  skuId: string;
  unitName: string;
  factorSnapshot: number;
  purchaseOrder: ComparisonValue;
  goodsReceipt: ComparisonValue;
  invoice: ComparisonValue | null;
  expectedQuantity: number | null;
  expectedUnitPrice: number | null;
  quantityDiff: number | null;
  priceDiff: number | null;
  amountDiff: number | null;
}

export interface SupplierInvoice {
  id: string;
  goodsReceiptId: string;
  supplierId: string;
  invoiceSeries: string;
  invoiceNumber: string;
  invoiceDate: string;
  subtotal: number;
  vatAmount: number;
  shippingFee: number;
  totalAmount: number;
  status: SupplierInvoiceStatus;
  requestKey: string;
  submittedAt: string | null;
  cancelledAt: string | null;
  cancelReason: string | null;
  variance: PurchaseVariance | null;
  comparison: SupplierInvoiceComparison[] | null;
  isReplay: boolean;
  supplierName?: string | null;
  goodsReceiptNumber?: string | null;
  goodsReceiptStatus?: string | null;
  purchaseOrderId?: string | null;
  purchaseOrderNumber?: string | null;
}

export interface SupplierInvoiceFilter {
  goodsReceiptId?: string;
  purchaseOrderId?: string;
  supplierId?: string;
  fromDate?: string;
  toDate?: string;
  status?: SupplierInvoiceStatus;
  pendingOnly?: boolean;
  pageIndex?: number;
  pageSize?: number;
}

export interface SupplierInvoicePage {
  items: SupplierInvoice[];
  pageIndex: number;
  pageSize: number;
  totalCount: number;
}
