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
  quantity: number;
}

export interface StockTransfer {
  id: string;
  transferNumber: string;
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