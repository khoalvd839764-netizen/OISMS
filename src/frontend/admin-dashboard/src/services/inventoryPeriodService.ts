import { httpClient } from './httpClient';

export interface InventoryPeriodInBreakdown {
  purchaseQuantity: number;
  purchaseValue: number | null;
  returnQuantity: number;
  returnValue: number | null;
  transferInQuantity: number;
  transferInValue: number | null;
  adjustmentInQuantity: number;
  adjustmentInValue: number | null;
}

export interface InventoryPeriodOutBreakdown {
  posQuantity: number;
  posValue: number | null;
  ecommerceQuantity: number;
  ecommerceValue: number | null;
  adminQuantity: number;
  adminValue: number | null;
  transferOutQuantity: number;
  transferOutValue: number | null;
  adjustmentOutQuantity: number;
  adjustmentOutValue: number | null;
}

export interface InventoryPeriodItem {
  skuId: string;
  skuCode: string;
  barcode: string | null;
  productName: string;
  variantName: string | null;
  categoryName: string | null;
  unitName: string;
  openingQuantity: number;
  openingValue: number | null;
  inQuantity: number;
  inValue: number | null;
  outQuantity: number;
  outValue: number | null;
  closingQuantity: number;
  closingValue: number | null;
  openingInTransitQuantity: number;
  closingInTransitQuantity: number;
  openingInTransitValue?: number | null;
  closingInTransitValue?: number | null;
  inBreakdown: InventoryPeriodInBreakdown;
  outBreakdown: InventoryPeriodOutBreakdown;
}

export interface InventoryPeriodSummary {
  totalOpeningQuantity: number;
  totalOpeningValue: number | null;
  totalInQuantity: number;
  totalInValue: number | null;
  totalOutQuantity: number;
  totalOutValue: number | null;
  totalClosingQuantity: number;
  totalClosingValue: number | null;
  totalOpeningInTransitQuantity: number;
  totalClosingInTransitQuantity: number;
  totalOpeningInTransitValue?: number | null;
  totalClosingInTransitValue?: number | null;
}

export interface InventoryPeriodReport {
  fromDate: string;
  toDate: string;
  branchId: string | null;
  branchName: string;
  isOwner: boolean;
  isPeriodUpToNow: boolean;
  items: InventoryPeriodItem[];
  summary: InventoryPeriodSummary;
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface InventoryPeriodFilter {
  fromDate: string;
  toDate: string;
  branchId?: string;
  searchTerm?: string;
  categoryId?: string;
  page?: number;
  pageSize?: number;
}

export interface StockCardFilter {
  skuId: string;
  branchId: string;
  fromDate: string;
  toDate: string;
}

export interface StockCardEntry {
  id: string;
  createdAt: string;
  type: string;
  sourceType: string;
  documentNumber: string;
  goodsIssueId?: string | null;
  goodsIssueNumber?: string | null;
  referenceId: string | null;
  inQuantity: number;
  inUnitPrice: number | null;
  inValue: number | null;
  outQuantity: number;
  outUnitPrice: number | null;
  outValue: number | null;
  balanceAfterQuantity: number;
  balanceAfterValue: number | null;
}

export interface StockCardReport {
  skuId: string;
  skuCode: string;
  barcode: string | null;
  productName: string;
  variantName: string | null;
  unitName: string;
  branchId: string;
  branchName: string;
  fromDate: string;
  toDate: string;
  isOwner: boolean;
  reportTitle: string;
  openingQuantity: number;
  openingValue: number | null;
  entries: StockCardEntry[];
  totalInQuantity: number;
  totalInValue: number | null;
  totalOutQuantity: number;
  totalOutValue: number | null;
  closingQuantity: number;
  closingValue: number | null;
  hasRoundingDifference: boolean;
  roundingDifference: number | null;
  currentOnHand: number | null;
  currentWac: number | null;
  currentTheoreticalValue: number | null;
}

function triggerDownload(blobData: BlobPart, defaultName: string) {
  const blob = new Blob([blobData], {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  });
  const url = window.URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = defaultName;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  window.URL.revokeObjectURL(url);
}

export const inventoryPeriodService = {
  async getInventoryPeriod(filter: InventoryPeriodFilter): Promise<InventoryPeriodReport> {
    const params: Record<string, any> = {
      fromDate: filter.fromDate,
      toDate: filter.toDate,
      page: filter.page ?? 1,
      pageSize: filter.pageSize ?? 50,
    };
    if (filter.branchId) params.branchId = filter.branchId;
    if (filter.searchTerm?.trim()) params.searchTerm = filter.searchTerm.trim();
    if (filter.categoryId) params.categoryId = filter.categoryId;

    const res = await httpClient.get<InventoryPeriodReport>('/api/reports/inventory-period', { params });
    return res.data;
  },

  async exportInventoryPeriodExcel(filter: InventoryPeriodFilter): Promise<void> {
    const params: Record<string, any> = {
      fromDate: filter.fromDate,
      toDate: filter.toDate,
      page: 1,
      pageSize: 10000,
    };
    if (filter.branchId) params.branchId = filter.branchId;
    if (filter.searchTerm?.trim()) params.searchTerm = filter.searchTerm.trim();
    if (filter.categoryId) params.categoryId = filter.categoryId;

    const res = await httpClient.get('/api/reports/inventory-period/export-excel', {
      params,
      responseType: 'blob',
    });
    triggerDownload(res.data, `BaoCaoTonTheoKy_${filter.fromDate}_${filter.toDate}.xlsx`);
  },

  async getStockCard(filter: StockCardFilter): Promise<StockCardReport> {
    const params: Record<string, any> = {
      skuId: filter.skuId,
      branchId: filter.branchId,
      fromDate: filter.fromDate,
      toDate: filter.toDate,
    };
    const res = await httpClient.get<StockCardReport>('/api/reports/stock-card', { params });
    return res.data;
  },

  async exportStockCardExcel(filter: StockCardFilter, skuCode?: string): Promise<void> {
    const params: Record<string, any> = {
      skuId: filter.skuId,
      branchId: filter.branchId,
      fromDate: filter.fromDate,
      toDate: filter.toDate,
    };
    const res = await httpClient.get('/api/reports/stock-card/export-excel', {
      params,
      responseType: 'blob',
    });
    triggerDownload(res.data, `TheKho_${skuCode || 'SKU'}_${filter.fromDate}_${filter.toDate}.xlsx`);
  },
};
