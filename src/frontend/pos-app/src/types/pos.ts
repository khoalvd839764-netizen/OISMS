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

export interface PosReceiptItemDto {
  skuId: string;
  skuCode: string;
  productName: string;
  variantName?: string | null;
  quantity: number;
  unitPrice: number;
  subtotal?: number;
}

export interface PosReceiptDto {
  orderId: string;
  receiptNumber: string;
  orderNumber: string;
  branchId: string;
  branchName?: string | null;
  branchAddress?: string | null;
  branchPhone?: string | null;
  cashierName?: string | null;
  customerName?: string | null;
  customerPhone?: string | null;
  totalAmount: number;
  paymentMethod: string;
  amountGiven?: number | null;
  changeDue?: number | null;
  createdAt: string;
  items: PosReceiptItemDto[];
}

export interface PosCheckoutResponseDto {
  success: boolean;
  message: string;
  orderId: string;
  orderNumber: string;
  totalAmount: number;
  amountGiven?: number | null;
  changeDue?: number | null;
  paymentMethod: string;
  checkoutAt: string;
  receipt: PosReceiptDto;
}

export interface ReceiptItemData {
  name: string;
  sku: string;
  skuId?: string;
  barcode?: string;
  quantity: number;
  unitPrice: number;
  subtotal: number;
}

export interface ReceiptData {
  receiptNumber: string;
  orderNumber?: string;
  orderId?: string;
  createdAt: string;
  tenantName: string;
  branchName: string;
  branchAddress?: string;
  branchPhone?: string;
  cashierName: string;
  customerName?: string;
  customerPhone?: string;
  items: ReceiptItemData[];
  totalAmount: number;
  paymentMethod: string;
  amountGiven: number;
  changeDue: number;
  isDraft?: boolean;
  paymentStatusTitle?: string;
  paperWidth?: '80mm' | '58mm';
}

export interface PosUser {
  id?: string;
  tenantId?: string;
  tenantCode?: string;
  fullName: string;
  email: string;
  tenantName?: string;
}

export interface CheckoutOwner {
  tenantId?: string;
  tenantCode?: string;
  tenantName?: string;
  userId?: string;
  email?: string;
  cashierName?: string;
  branchId: string;
}

export type PendingCheckoutStatus = 'submitting' | 'unknown';

export interface PendingCheckoutRecord {
  version: 1;
  requestKey: string;
  owner: CheckoutOwner;
  createdAt: number;
  updatedAt: number;
  status: PendingCheckoutStatus;
  requestPayload: PosCheckoutRequest;
  lastErrorMessage?: string;
  lastErrorCode?: string;
}

export interface CheckoutResult {
  success: boolean;
  message: string;
  orderNumber?: string;
  receipt?: PosReceiptDto | null;
  isPendingBackend?: boolean;
  statusCode?: number;
  errorCode?: string;
  detail?: string;
  isUnknown?: boolean;
  isAuthExpired?: boolean;
  isDefinitiveError?: boolean;
}

export interface OrderSummaryRealtimeDto {
  orderId: string;
  orderNumber: string;
  branchId: string;
  branchName?: string | null;
  channel: string;
  status: string;
  totalAmount: number;
  totalItems: number;
  customerName?: string | null;
  createdAt: string;
}

export interface StockChangedRealtimeDto {
  branchId: string;
  skuId: string;
  newAvailableStock: number;
  timestamp: string;
}

export interface LowStockAlertRealtimeDto {
  branchId: string;
  skuId: string;
  skuCode: string;
  currentAvailable: number;
  threshold: number;
  alertedAt: string;
}

export type RealtimeConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';
