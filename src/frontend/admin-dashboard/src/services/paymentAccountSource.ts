/** NOTE: Có bản tương đương tại pos-app/src/services/paymentAccountSource.ts. Khi chỉnh sửa cần cập nhật đồng bộ cả hai ứng dụng. */
/**
 * Nguồn nạp danh sách tài khoản ngân hàng thụ hưởng nhận tiền theo tenant (Giai đoạn b - CS-05).
 * Có bản ở cả hai app (pos-app và admin-dashboard), sửa thì sửa cả hai bản.
 */
export {
  loadPaymentAccounts,
  getPaymentAccountsSync,
  toBankConfig,
} from './qrService';
