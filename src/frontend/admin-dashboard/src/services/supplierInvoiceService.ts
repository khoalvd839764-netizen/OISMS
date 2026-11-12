import { isAxiosError } from "axios";
import { httpClient } from "./httpClient";
import type {
  CreateSupplierInvoice,
  SupplierInvoice,
  SupplierInvoiceDraft,
  SupplierInvoiceFilter,
  SupplierInvoicePage,
} from "../types/supplierInvoice";

const path = "/api/supplier-invoices";

export const supplierInvoiceService = {
  async list(filter: SupplierInvoiceFilter): Promise<SupplierInvoicePage> {
    const { data } = await httpClient.get<SupplierInvoicePage>(path, { params: filter });
    return data;
  },
  async get(id: string): Promise<SupplierInvoice> {
    const { data } = await httpClient.get<SupplierInvoice>(`${path}/${id}`);
    return data;
  },
  async create(body: CreateSupplierInvoice, requestKey: string): Promise<SupplierInvoice> {
    const { data } = await httpClient.post<SupplierInvoice>(path, body, {
      headers: { "Idempotency-Key": requestKey },
    });
    return data;
  },
  async update(id: string, body: SupplierInvoiceDraft): Promise<SupplierInvoice> {
    const { data } = await httpClient.put<SupplierInvoice>(`${path}/${id}`, body);
    return data;
  },
  async submit(id: string): Promise<SupplierInvoice> {
    const { data } = await httpClient.post<SupplierInvoice>(`${path}/${id}/submit`);
    return data;
  },
  async approve(id: string): Promise<SupplierInvoice> {
    const { data } = await httpClient.post<SupplierInvoice>(`${path}/${id}/approve`);
    return data;
  },
  async reject(id: string, decisionNote: string): Promise<SupplierInvoice> {
    const { data } = await httpClient.post<SupplierInvoice>(`${path}/${id}/reject`, { decisionNote });
    return data;
  },
  async cancel(id: string, reason: string): Promise<SupplierInvoice> {
    const { data } = await httpClient.post<SupplierInvoice>(`${path}/${id}/cancel`, { reason });
    return data;
  },
};

export const receiptInvoicePendingMessage =
  "Hóa đơn NCC cần được gửi đối chiếu hoặc duyệt trước khi xác nhận nhập kho.";

const errorMessages: Readonly<Record<string, string>> = {
  SUPPLIER_INVOICE_DUPLICATE: "Nhà cung cấp đã có hóa đơn chưa hủy trùng ký hiệu và số.",
  SUPPLIER_INVOICE_INVALID_STATE: "Trạng thái chứng từ đã thay đổi hoặc không cho phép thao tác này.",
  SUPPLIER_INVOICE_RECEIPT_MISMATCH: "Hóa đơn không khớp với nhà cung cấp, phiếu nhập hoặc các dòng phiếu mua.",
  SUPPLIER_INVOICE_ALREADY_EXISTS: "Phiếu nhập đã có hóa đơn còn hiệu lực. Hãy tải lại phiếu để xem hóa đơn.",
  GOODS_RECEIPT_INVOICE_PENDING: receiptInvoicePendingMessage,
  IDEMPOTENCY_PAYLOAD_MISMATCH: "Khóa gửi lại đã được dùng cho nội dung khác. Hãy tải lại phiếu trước khi tiếp tục.",
};

interface InvoiceProblem {
  errorCode?: string;
  detail?: string;
  message?: string;
}

export function readSupplierInvoiceError(error: unknown, fallback: string) {
  const response = isAxiosError<InvoiceProblem>(error) ? error.response : undefined;
  const code = response?.data?.errorCode;
  return {
    code,
    message: (code && errorMessages[code]) || response?.data?.detail || response?.data?.message || fallback,
    uncertain: response == null || response.status === 408 || response.status >= 500,
  };
}
