/**
 * Nguồn tài khoản nhận tiền của POS tích hợp trong Admin (CS-05): chỉ lấy từ `GET /api/payment-accounts/active`.
 * Bản tương ứng ở pos-app/src/services/paymentAccountSource.ts chỉ khác chỗ lấy gốc API và token.
 */
import type { PaymentAccountsLoadResult } from '../types/qr';
import { getApiBaseUrl, getAuthToken } from './posApi';
import { loadPaymentAccounts } from './qrService';

export function fetchActivePaymentAccounts(): Promise<PaymentAccountsLoadResult> {
  return loadPaymentAccounts({ baseUrl: getApiBaseUrl(), token: getAuthToken() });
}
