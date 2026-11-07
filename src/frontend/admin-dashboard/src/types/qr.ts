/**
 * Định nghĩa hợp đồng và kiểu dữ liệu cho module QR POS (S3-POS-B03)
 * Tuân thủ chuẩn EMVCo / VietQR (NAPAS 247) và quy tắc kiểm soát thanh toán POS.
 * NOTE: Có bản tương đương tại pos-app/src/types/qr.ts. Khi chỉnh sửa cần cập nhật đồng bộ cả hai ứng dụng.
 */

export interface BankConfig {
  /** Mã định danh ngân hàng (BIN/IIN) - 6 chữ số theo chuẩn Ngân hàng Nhà nước / NAPAS */
  bankBin: string;
  /** Tên viết tắt hoặc tên đầy đủ của ngân hàng */
  bankName: string;
  /** Số tài khoản thụ hưởng của cửa hàng/chi nhánh (tối đa 19 ký tự theo QuickLink/NAPAS) */
  accountNumber: string;
  /** Tên chủ tài khoản thụ hưởng */
  accountName: string;
  /** Mẫu template hiển thị nếu dùng QuickLink (ví dụ: 'compact2', 'qr_only', 'print') */
  template?: string;
  /** Cờ đánh dấu cấu hình kiểm thử (Fixture) */
  isTestFixture?: boolean;
}

/** Cấu hình tài khoản nhận tiền theo tenant (Giai đoạn b - CS-05) */
export interface PaymentAccountOption {
  id: string;
  bankBin: string;
  bankName: string;
  bankShortName: string;
  accountNumber: string;
  accountName: string;
  isDefault: boolean;
}

/**
 * Kết quả tải danh sách tài khoản nhận tiền từ `GET /api/payment-accounts/active`.
 * POS chỉ lấy tài khoản từ API (không đọc localStorage hay biến window).
 */
export type PaymentAccountsLoadResult =
  | { status: 'ok'; accounts: PaymentAccountOption[] }
  | { status: 'empty'; accounts: PaymentAccountOption[] }
  | { status: 'error'; accounts: PaymentAccountOption[]; message: string; httpStatus?: number };

/** Browser payment intent; retained before checkout, including close/reload. */
export interface PosQrIntent {
  version: 1;
  context: { tenantId: string; userId: string; branchId: string };
  cartFingerprint: string;
  requestKey: string;
  amount: number;
  bankConfig: BankConfig;
  /** Id tài khoản nhận tiền (payment_accounts.id) đã dùng để sinh QR; gửi kèm khi checkout. */
  paymentAccountId?: string;
}

export interface QrPaymentPayload {
  /** Số tiền cần thanh toán (VNĐ), phải > 0 và <= MaxMoneyAmount */
  amount: number;
  /** Khóa yêu cầu Idempotency (UUID v4 RFC 4122), giữ nguyên khi retry */
  requestKey: string;
  /** ID chi nhánh quầy bán hàng */
  branchId?: string;
  /** Nội dung / thông điệp giao dịch (mặc định sinh theo requestKey) */
  orderDescription?: string;
  /** Đơn vị tiền tệ (mặc định: 'VND') */
  currency?: 'VND';
  /** ID thu ngân phụ trách ca */
  cashierUserId?: string;
}

export type QrPaymentStatus =
  | 'IDLE'
  | 'GENERATED'
  | 'AWAITING_CASHIER_CONFIRMATION'
  | 'SUBMITTING'
  | 'CONFIRMED'
  | 'FAILED';

export interface QrSnapshotData {
  requestKey: string;
  amount: number;
  bankBin: string;
  accountNumber: string;
  orderDescription?: string;
  branchId?: string;
}

export interface VietQrResult {
  /** Chuỗi mã hóa EMVCo / VietQR hoàn chỉnh (gồm TLV và mã kiểm tra CRC-16) */
  rawPayload: string;
  /** Đường dẫn ảnh QuickLink theo chuẩn VietQR API */
  quickLinkUrl: string;
  /** Mã tham chiếu ngắn trích xuất từ requestKey để khách ghi nội dung CK */
  transactionReference: string;
  /** Nội dung chuyển khoản chuẩn POS */
  transferContent: string;
  /** Trạng thái kiểm tra tính hợp lệ của CRC-16 */
  crcValid: boolean;
  /** Mã CRC-16 hex 4 ký tự */
  crcCode: string;
  /** Trạng thái thanh toán (khởi tạo luôn là AWAITING_CASHIER_CONFIRMATION) */
  status: QrPaymentStatus;
  /** Cờ đánh dấu dữ liệu tạo từ Fixture kiểm thử */
  isTestFixture: boolean;
  /** Thời điểm tạo mã */
  createdAt: string;
  /** B03-F03: Khóa định danh yêu cầu đầy đủ (UUID v4 RFC 4122) */
  requestKey: string;
  /** B03-F03: Dữ liệu snapshot giao dịch để đối chiếu Idempotency */
  snapshot: QrSnapshotData;
}

export interface QrValidationResult {
  valid: boolean;
  errors: string[];
  sanitizedAmount?: number;
  sanitizedDescription?: string;
}

export interface TlvField {
  tag: string;
  length: number;
  value: string;
  subFields?: TlvField[];
}
