export interface SalesInvoiceSummary {
  id: string;
  number: string;
  orderId: string;
  orderNumber: string;
  branchId: string;
  branchName: string;
  channel: string;
  customerName?: string;
  totalAmount: number;
  issuedByName?: string;
  issuedAt: string;
}

export interface SalesInvoiceReceiptItem {
  skuId: string;
  skuCode: string;
  productName: string;
  variantName?: string;
  unitId?: string;
  unitName: string;
  quantity: number;
  unitPrice: number;
  subtotal: number;
}

export interface SalesInvoiceReceipt {
  invoiceId: string;
  orderId: string;
  receiptNumber: string;
  orderNumber: string;
  branchId: string;
  branchName?: string;
  branchAddress?: string;
  branchPhone?: string;
  cashierName?: string;
  customerName?: string;
  customerPhone?: string;
  totalAmount: number;
  paymentMethod?: string;
  amountGiven?: number;
  changeDue?: number;
  createdAt: string;
  items: SalesInvoiceReceiptItem[];
}

export interface DocumentTraceInvoice {
  id: string;
  number: string;
  issuedAt: string;
  issuedByName?: string;
  totalAmount: number;
}

export interface DocumentTraceSource {
  type: string;
  id?: string;
  number?: string;
  status?: string;
  actorUserId?: string;
  actorName?: string;
  actedAt?: string;
}

export interface DocumentTraceLine {
  ledgerId: string;
  type: string;
  skuId: string;
  skuCode: string;
  warehouseId: string;
  warehouseName: string;
  quantity: number;
  unitCost: number;
  balanceAfter: number;
  sequenceNo: number;
  createdAt: string;
  orderItemId?: string;
  unitName?: string;
  orderItemQuantity?: number;
}

export interface DocumentTrace {
  salesInvoice?: DocumentTraceInvoice;
  source: DocumentTraceSource;
  lines: DocumentTraceLine[];
}

export interface SalesInvoiceFilter {
  number?: string;
  orderId?: string;
  branchId?: string;
  fromDate?: string;
  toDate?: string;
  pageIndex?: number;
  pageSize?: number;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  pageIndex: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}
