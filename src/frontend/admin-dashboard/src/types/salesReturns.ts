// src/types/salesReturns.ts
// Kiểu dữ liệu cho Phân hệ Quản lý Trả hàng & Hoàn tiền (OISMS-58, CS-01)
// Khớp với backend DTOs tại SalesReturnDtos.cs

export type SalesReturnStatus = 'Draft' | 'Confirmed' | 'Cancelled';

export const SALES_RETURN_STATUS_LABEL: Record<string, string> = {
  Draft: 'Nháp',
  Confirmed: 'Đã xác nhận',
  Cancelled: 'Đã hủy',
};

export interface ReturnableItem {
  orderItemId: string;
  skuId: string;
  skuCode: string;
  skuName: string;
  unitId?: string | null;
  unitName: string;
  factorSnapshot: number;
  soldQuantity: number;
  returnedQuantity: number;
  returnableQuantity: number;
  unitPrice: number;
  originalUnitCost: number;
}

export interface CreateSalesReturnItem {
  orderItemId: string;
  quantity: number;
  refundAmount?: number | null;
}

export interface CreateSalesReturnRequest {
  requestKey?: string;
  orderId: string;
  warehouseId: string;
  reason: string;
  note?: string | null;
  items: CreateSalesReturnItem[];
}

export interface ConfirmSalesReturnRequest {
  refundMethod?: string | null;
}

export interface SalesReturnItem {
  id: string;
  salesReturnId: string;
  orderId: string;
  orderItemId: string;
  skuId: string;
  skuCode: string;
  skuName: string;
  unitId?: string | null;
  unitName: string;
  factorSnapshot: number;
  quantity: number;
  baseQuantity: number;
  refundAmount: number;
  originalUnitCost: number;
}

export interface SalesReturn {
  id: string;
  requestKey: string;
  number: string;
  orderId: string;
  orderNumber: string;
  orderChannel: string;
  branchId: string;
  branchName: string;
  warehouseId: string;
  warehouseName: string;
  status: SalesReturnStatus | string;
  reason: string;
  note?: string | null;
  totalRefundAmount: number;
  refundMethod?: string | null;
  refundedAt?: string | null;
  createdBy: string;
  createdByName?: string | null;
  confirmedBy?: string | null;
  confirmedByName?: string | null;
  confirmedAt?: string | null;
  createdAt: string;
  updatedAt?: string | null;
  items: SalesReturnItem[];
}

export interface SalesReturnFilter {
  branchId?: string;
  warehouseId?: string;
  orderId?: string;
  status?: string;
  fromDate?: string;
  toDate?: string;
  searchTerm?: string;
  pageIndex?: number;
  pageSize?: number;
}

export interface SalesReturnPagedResult {
  items: SalesReturn[];
  pageIndex: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}
