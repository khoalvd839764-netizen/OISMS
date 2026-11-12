import type { SupplierInvoiceStatus } from "./supplierInvoice";

export interface CreateGoodsReceiptItemDto {
  purchaseOrderItemId: string;
  quantity: number;
}

export interface CreateGoodsReceiptDto {
  purchaseOrderId: string;
  warehouseId: string;
  number?: string | null;
  note?: string | null;
  items: CreateGoodsReceiptItemDto[];
}

export interface GoodsReceiptItemResponse {
  id: string;
  purchaseOrderItemId?: string | null;
  skuId: string;
  skuCode: string;
  productName: string;
  unitId?: string | null;
  unitName: string;
  factorSnapshot: number;
  baseQuantity: number;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

export interface GoodsReceiptResponse {
  id: string;
  number: string;
  purchaseOrderId?: string | null;
  purchaseOrderNumber?: string | null;
  invoiceId?: string | null;
  invoiceStatus?: SupplierInvoiceStatus | null;
  branchId: string;
  branchName: string;
  warehouseId: string;
  warehouseName: string;
  supplierId: string;
  supplierName: string;
  status: 'Draft' | 'Confirmed' | 'Cancelled';
  note?: string | null;
  totalAmount: number;
  createdBy: string;
  createdByName?: string | null;
  confirmedAt?: string | null;
  confirmedBy?: string | null;
  confirmedByName?: string | null;
  createdAt: string;
  updatedAt?: string | null;
  items: GoodsReceiptItemResponse[];
}

export interface GoodsReceiptFilter {
  branchId?: string;
  warehouseId?: string;
  supplierId?: string;
  purchaseOrderId?: string;
  status?: string;
  fromDate?: string;
  toDate?: string;
  pageIndex?: number;
  pageSize?: number;
}

export interface PagedGoodsReceiptResult {
  items: GoodsReceiptResponse[];
  pageIndex: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}
