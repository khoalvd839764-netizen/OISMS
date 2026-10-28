export type TransferStatus = 'Draft' | 'InTransit' | 'Received' | 'Rejected' | 'Cancelled';

export const TRANSFER_STATUS_LABEL: Record<TransferStatus, string> = {
  Draft: 'Phiếu nháp',
  InTransit: 'Đang vận chuyển',
  Received: 'Đã nhận hàng',
  Rejected: 'Từ chối nhận',
  Cancelled: 'Đã hủy',
};

export interface StockTransferItem {
  id?: string;
  skuId: string;
  skuCode: string;
  skuName?: string;
  productName?: string;
  quantity: number;
  unitCost?: number;
}

export interface StockTransfer {
  id: string;
  number?: string;
  transferNumber?: string;
  requestKey?: string;
  fromBranchId?: string;
  fromBranchName?: string;
  toBranchId?: string;
  toBranchName?: string;
  fromWarehouseId: string;
  fromWarehouseName?: string;
  toWarehouseId: string;
  toWarehouseName?: string;
  status: TransferStatus;
  note?: string;
  items: StockTransferItem[];
  createdAt: string;
  dispatchedAt?: string;
  receivedAt?: string;
}

export interface CreateStockTransferRequest {
  requestKey?: string;
  fromBranchId?: string;
  toBranchId?: string;
  fromWarehouseId: string;
  toWarehouseId: string;
  note?: string;
  items: Array<{
    skuId: string;
    quantity: number;
  }>;
}

export interface RejectStockTransferRequest {
  reason: string;
}

export interface StockTransferFilter {
  branchId?: string;
  warehouseId?: string;
  status?: string;
  pageNumber?: number;
  pageIndex?: number;
  pageSize?: number;
}

export interface StockTransferPagedResult {
  items: StockTransfer[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}
