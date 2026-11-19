export interface GoodsIssueItem {
  id: string;
  goodsIssueId: string;
  inventoryTransactionId: string;
  orderItemId: string;
  skuId: string;
  skuCode: string;
  skuName: string;
  unitId?: string | null;
  unitName: string;
  baseUnitName: string;
  factorSnapshot: number;
  quantity: number;
  baseQuantity: number;
  unitCost: number | null;
  totalCost: number | null;
}

export interface GoodsIssue {
  id: string;
  number: string;
  orderId: string;
  orderNumber: string;
  salesInvoiceId: string;
  salesInvoiceNumber: string;
  branchId: string;
  branchName: string;
  warehouseId: string;
  warehouseName: string;
  channel: string;
  reason: string;
  customerName?: string | null;
  issuedBy?: string | null;
  issuedByName?: string | null;
  issuedAt: string;
  totalQuantity: number;
  totalBaseQuantity: number;
  totalCost: number | null;
  items: GoodsIssueItem[];
}

export interface PosDailyGoodsIssueSku {
  skuId: string;
  skuCode: string;
  skuName: string;
  baseUnitName: string;
  totalQuantity: number;
  totalBaseQuantity: number;
  unitCost: number | null;
  totalCost: number | null;
}

export interface PosDailyGoodsIssueInvoice {
  goodsIssueId: string;
  goodsIssueNumber: string;
  salesInvoiceId: string;
  salesInvoiceNumber: string;
  orderId: string;
  orderNumber: string;
  branchId: string;
  branchName: string;
  warehouseId: string;
  warehouseName: string;
  customerName?: string | null;
  issuedByName?: string | null;
  issuedAt: string;
  totalQuantity: number;
  totalBaseQuantity: number;
  totalSalesAmount: number;
  totalCost: number | null;
}

export interface PosDailyGoodsIssueSummary {
  businessDate: string;
  fromUtc: string;
  toUtc: string;
  branchId?: string | null;
  branchName: string;
  totalOrders: number;
  totalInvoices: number;
  totalGoodsIssues: number;
  totalQuantity: number;
  totalBaseQuantity: number;
  totalCost: number | null;
  items: PosDailyGoodsIssueSku[];
  invoices: PosDailyGoodsIssueInvoice[];
}
