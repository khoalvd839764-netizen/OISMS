/**
 * Nguồn nạp danh sách tài khoản ngân hàng thụ hưởng nhận tiền theo tenant (Giai đoạn b - CS-05).
 * Có bản ở cả hai app (pos-app và admin-dashboard), sửa thì sửa cả hai bản.
 */
export {
  loadPaymentAccounts,
  getPaymentAccountsSync,
  toBankConfig,
} from './qrService';
