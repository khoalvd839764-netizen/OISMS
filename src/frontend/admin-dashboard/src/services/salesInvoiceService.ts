import { httpClient } from './httpClient';
import type {
  DocumentTrace,
  PagedResult,
  SalesInvoiceFilter,
  SalesInvoiceReceipt,
  SalesInvoiceSummary,
} from '../types/salesInvoices';

export const salesInvoiceService = {
  async getSalesInvoices(filter?: SalesInvoiceFilter): Promise<PagedResult<SalesInvoiceSummary>> {
    const params: Record<string, any> = {};
    if (filter?.number) params.number = filter.number.trim();
    if (filter?.orderId) params.orderId = filter.orderId;
    if (filter?.branchId) params.branchId = filter.branchId;
    if (filter?.fromDate) params.fromDate = filter.fromDate;
    if (filter?.toDate) params.toDate = filter.toDate;
    if (filter?.pageIndex) params.pageIndex = filter.pageIndex;
    if (filter?.pageSize) params.pageSize = filter.pageSize;

    const res = await httpClient.get<PagedResult<SalesInvoiceSummary>>('/api/sales-invoices', { params });
    return res.data;
  },

  async getSalesInvoiceById(id: string): Promise<SalesInvoiceSummary> {
    const res = await httpClient.get<SalesInvoiceSummary>(`/api/sales-invoices/${id}`);
    return res.data;
  },

  async getReceiptById(id: string): Promise<SalesInvoiceReceipt> {
    const res = await httpClient.get<SalesInvoiceReceipt>(`/api/sales-invoices/${id}/receipt`);
    return res.data;
  },

  async getReceiptByNumber(number: string): Promise<SalesInvoiceReceipt> {
    const res = await httpClient.get<SalesInvoiceReceipt>('/api/sales-invoices/receipt', {
      params: { number: number.trim() },
    });
    return res.data;
  },

  async traceInvoiceById(id: string): Promise<DocumentTrace> {
    const res = await httpClient.get<DocumentTrace>(`/api/sales-invoices/${id}/trace`);
    return res.data;
  },
};
