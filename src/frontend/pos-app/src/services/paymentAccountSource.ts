/**
 * Nguồn tài khoản nhận tiền của POS độc lập (CS-05): chỉ lấy từ `GET /api/payment-accounts/active`.
 */
import { authenticatedFetch } from './posSession';
import type { PaymentAccountsLoadResult } from '../types/qr';
import { getApiBaseUrl, getAuthToken } from './posApi';
import { loadPaymentAccounts } from './qrService';

export function fetchActivePaymentAccounts(): Promise<PaymentAccountsLoadResult> {
  return loadPaymentAccounts({ baseUrl: getApiBaseUrl(), token: getAuthToken(), fetchFn: authenticatedFetch });
}
