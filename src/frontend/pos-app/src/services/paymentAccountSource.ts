/**
 * Nguồn tài khoản nhận tiền của POS PWA (CS-05): chỉ lấy từ `GET /api/payment-accounts/active`.
 * Bản tương ứng ở admin-dashboard/src/services/paymentAccountSource.ts chỉ khác chỗ lấy gốc API và token.
 */
import type { PaymentAccountsLoadResult } from '../types/qr';
import { getApiBaseUrl, getAuthToken } from './api';
import { loadPaymentAccounts } from './qrService';

export function fetchActivePaymentAccounts(): Promise<PaymentAccountsLoadResult> {
  return loadPaymentAccounts({ baseUrl: getApiBaseUrl(), token: getAuthToken() });
}
