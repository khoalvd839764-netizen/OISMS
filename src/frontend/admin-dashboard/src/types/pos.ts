// Types for POS Application (OISMS Web POS)

export interface PosProductSearchResponse {
  variantId: string;
  productId: string;
  sku: string;
  productName: string;
  variantName: string;
  barcode: string | null;
  retailPrice: number;
  color: string | null;
  size: string | null;
  attributes?: Record<string, unknown> | null;
  onHand: number | null;
  reserved: number | null;
  available: number | null;
}

export interface CartItem {
  variantId: string;
  productId: string;
  sku: string;
  productName: string;
  variantName: string;
  barcode: string | null;
  retailPrice: number;
  quantity: number;
  available: number;
  color?: string | null;
  size?: string | null;
}

export interface Branch {
  id: string;
  name: string;
  address?: string;
  phone?: string;
  isActive: boolean;
}

export interface PosCheckoutItemRequest {
  skuId: string;
  skuCode: string;
  productName: string;
  variantName: string;
  quantity: number;
  unitPrice: number;
}

export interface PosCheckoutRequest {
  branchId: string;
  channel: string; // "POS"
  customerName?: string;
  customerPhone?: string;
  paymentMethod: "CASH" | "BANK_TRANSFER_QR";
  amountGiven?: number;
  changeDue?: number;
  requestKey: string;
  items: PosCheckoutItemRequest[];
  totalAmount: number;
}

export interface ReceiptData {
  receiptNumber: string;
  createdAt: string;
  tenantName: string;
  branchName: string;
  branchAddress: string;
  branchPhone: string;
  cashierName: string;
  customerName?: string;
  customerPhone?: string;
  items: Array<{
    name: string;
    sku: string;
    quantity: number;
    unitPrice: number;
    subtotal: number;
  }>;
  totalAmount: number;
  paymentMethod: "CASH" | "BANK_TRANSFER_QR";
  amountGiven: number;
  changeDue: number;
  isDraft?: boolean;
  paymentStatusTitle?: string;
}
