import type { ReceiptData } from '../types/pos';
const text = (value: unknown) => typeof value === 'string' ? value.trim() : '';
const money = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value) && value >= 0;

/** Confirmed receipts come from the server snapshot; whitelist customer-facing fields only. */
export function mapServerReceipt(value: unknown, tenantName: string): ReceiptData {
  if (!value || typeof value !== 'object') throw new Error('Thiếu snapshot hóa đơn từ máy chủ.');
  const raw = value as Record<string, unknown>;
  if (!text(raw.receiptNumber) || !text(raw.orderNumber) || !text(raw.createdAt) || !Number.isFinite(Date.parse(text(raw.createdAt))) || !money(raw.totalAmount) || !Array.isArray(raw.items) || !raw.items.length) throw new Error('Snapshot hóa đơn không hợp lệ.');
  const method = text(raw.paymentMethod).replace(/_/g, '').toLowerCase();
  if (!['cash', 'banktransfer', 'banktransferqr'].includes(method)) throw new Error('Phương thức trên hóa đơn không hợp lệ.');
  const items = raw.items.map((value: unknown) => {
    if (!value || typeof value !== 'object') throw new Error('Dòng hóa đơn không hợp lệ.');
    const item = value as Record<string, unknown>;
    if (!text(item.skuCode) || !money(item.quantity) || item.quantity <= 0 || !money(item.unitPrice) || !money(item.subtotal) || Math.abs(item.subtotal - item.quantity * item.unitPrice) > .011) throw new Error('Số lượng/giá trên hóa đơn không hợp lệ.');
    return { name: (text(item.productName) || text(item.skuCode)) + (text(item.variantName) ? ` - ${text(item.variantName)}` : ''), sku: text(item.skuCode), unitName: text(item.unitName), quantity: item.quantity, unitPrice: item.unitPrice, subtotal: item.subtotal };
  });
  if (Math.abs(items.reduce((sum, item) => sum + item.subtotal, 0) - raw.totalAmount) > .011) throw new Error('Tổng hóa đơn không khớp các dòng.');
  const amountGiven = money(raw.amountGiven) ? raw.amountGiven : raw.totalAmount;
  const changeDue = money(raw.changeDue) ? raw.changeDue : Math.max(0, amountGiven - raw.totalAmount);
  return { receiptNumber: text(raw.receiptNumber), orderNumber: text(raw.orderNumber), createdAt: new Date(text(raw.createdAt)).toLocaleString('vi-VN'),
    tenantName, branchName: text(raw.branchName), branchAddress: text(raw.branchAddress), branchPhone: text(raw.branchPhone), cashierName: text(raw.cashierName),
    customerName: text(raw.customerName) || undefined, customerPhone: text(raw.customerPhone) || undefined, items, totalAmount: raw.totalAmount,
    paymentMethod: method === 'cash' ? 'CASH' : 'BANK_TRANSFER_QR', amountGiven, changeDue, isDraft: false,
    receiverBankBin: text(raw.receiverBankBin) || undefined, receiverBankName: text(raw.receiverBankName) || undefined,
    receiverAccountNumber: text(raw.receiverAccountNumber) || undefined, receiverAccountName: text(raw.receiverAccountName) || undefined, transferReference: text(raw.transferReference) || undefined };
}
