/**
 * Module Dịch vụ Xử lý & Kiểm tra Mã QR Độc lập (S3-POS-B03)
 *
 * Thực hiện:
 * 1. Chuẩn hóa & mã hóa chuỗi EMVCo / VietQR (NAPAS 247) với kiểm tra CRC-16/CCITT-FALSE.
 * 2. Sinh đường dẫn QuickLink VietQR cho hiển thị hình ảnh động.
 * 3. Kiểm định nghiêm ngặt số tiền, cấu hình ngân hàng và khóa Idempotency (RequestKey UUID v4).
 * 4. Bảo đảm tính bất biến (Idempotent Invariance) của nội dung QR và mã tham chiếu khi retry.
 * 5. Thiết lập Guard chống suy diễn thanh toán thành công từ hiển thị, quét mã hoặc hết thời gian.
 */

import type { BankConfig, QrPaymentPayload, VietQrResult, QrValidationResult } from './qr-types.js';

/** Giới hạn tiền tệ hệ thống OISMS trong môi trường POS frontend (tối đa 15 chữ số nguyên: 999 nghìn tỷ VNĐ) */
export const MAX_MONEY_AMOUNT = 999_999_999_999_999;

/** Regex kiểm tra UUID v4 RFC 4122 chuẩn */
export const UUID_V4_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

/** Regex kiểm tra 6 chữ số BIN ngân hàng */
export const BANK_BIN_REGEX = /^\d{6}$/;

/**
 * Tính toán mã CRC-16/CCITT-FALSE (đa thức 0x1021, giá trị khởi tạo 0xFFFF, không đảo bit).
 * Đây là thuật toán chuẩn quốc tế được quy định trong tài liệu EMVCo Book 4 và VietQR NAPAS.
 */
export function calculateCrc16Ccitt(str: string): string {
  let crc = 0xffff;
  const polynomial = 0x1021;
  const buffer = Buffer.from(str, 'utf8');

  for (let i = 0; i < buffer.length; i++) {
    crc ^= buffer[i] << 8;
    for (let j = 0; j < 8; j++) {
      if ((crc & 0x8000) !== 0) {
        crc = ((crc << 1) ^ polynomial) & 0xffff;
      } else {
        crc = (crc << 1) & 0xffff;
      }
    }
  }

  return crc.toString(16).toUpperCase().padStart(4, '0');
}

/**
 * Đóng gói trường dữ liệu TLV (Tag - Length - Value) theo chuẩn EMVCo.
 * Length luôn là 2 chữ số (ví dụ: '01' -> '02' -> '010212').
 */
export function formatTlv(tag: string, value: string): string {
  const len = Buffer.byteLength(value, 'utf8');
  const lenStr = len.toString().padStart(2, '0');
  return `${tag}${lenStr}${value}`;
}

/**
 * Kiểm tra tính hợp lệ của cấu hình ngân hàng thụ hưởng.
 */
export function validateBankConfig(config: BankConfig | null | undefined): QrValidationResult {
  const errors: string[] = [];

  if (!config) {
    return { valid: false, errors: ['Cấu hình ngân hàng không được để trống.'] };
  }

  if (!config.bankBin || !BANK_BIN_REGEX.test(config.bankBin.trim())) {
    errors.push('Mã định danh ngân hàng (Bank BIN) không hợp lệ (phải gồm đúng 6 chữ số).');
  }

  if (!config.accountNumber || config.accountNumber.trim().length === 0) {
    errors.push('Số tài khoản ngân hàng thụ hưởng không được để trống.');
  } else if (!/^[0-9a-zA-Z]{6,30}$/.test(config.accountNumber.trim())) {
    errors.push('Số tài khoản ngân hàng chứa ký tự không hợp lệ hoặc độ dài ngoài khoảng 6–30 ký tự.');
  }

  if (!config.accountName || config.accountName.trim().length === 0) {
    errors.push('Tên chủ tài khoản ngân hàng thụ hưởng không được để trống.');
  }

  return {
    valid: errors.length === 0,
    errors,
  };
}

/**
 * Kiểm tra tính hợp lệ của Payload thanh toán QR.
 */
export function validateQrPaymentPayload(payload: QrPaymentPayload | null | undefined): QrValidationResult {
  const errors: string[] = [];

  if (!payload) {
    return { valid: false, errors: ['Dữ liệu thanh toán không được để trống.'] };
  }

  // 1. Kiểm tra số tiền
  if (typeof payload.amount !== 'number' || Number.isNaN(payload.amount)) {
    errors.push('Số tiền thanh toán phải là một số hợp lệ.');
  } else if (payload.amount <= 0) {
    errors.push('Số tiền thanh toán phải lớn hơn 0.');
  } else if (payload.amount > MAX_MONEY_AMOUNT) {
    errors.push(`Số tiền thanh toán vượt quá giới hạn tối đa cho phép của hệ thống (${MAX_MONEY_AMOUNT.toLocaleString('vi-VN')} đ).`);
  } else {
    // Kiểm tra số chữ số thập phân (tối đa 2 chữ số theo hợp đồng tiền tệ)
    const rounded = Math.round(payload.amount * 100) / 100;
    if (Math.abs(payload.amount - rounded) > 1e-6) {
      errors.push('Số tiền thanh toán không được vượt quá 2 chữ số thập phân.');
    }
  }

  // 2. Kiểm tra RequestKey (Idempotency Key)
  if (!payload.requestKey || payload.requestKey.trim().length === 0) {
    errors.push('Mã yêu cầu (RequestKey) không được để trống.');
  } else if (!UUID_V4_REGEX.test(payload.requestKey.trim())) {
    errors.push('Mã yêu cầu (RequestKey) phải là UUID v4 RFC 4122 hợp lệ.');
  }

  return {
    valid: errors.length === 0,
    errors,
    sanitizedAmount: typeof payload.amount === 'number' && payload.amount > 0 ? Math.round(payload.amount * 100) / 100 : undefined,
  };
}

/**
 * Trích xuất mã tham chiếu ngắn từ RequestKey để làm mã nội dung chuyển khoản tại quầy.
 * Ví dụ: RequestKey 'a1b2c3d4-e5f6-4789-abcd-ef1234567890' -> 'A1B2C3D4'
 */
export function extractShortReference(requestKey: string): string {
  const clean = requestKey.replace(/-/g, '').toUpperCase();
  return clean.slice(0, 8);
}

/**
 * Sinh mã VietQR (chuỗi EMVCo Payload và QuickLink URL).
 *
 * @param bankConfig Thông tin tài khoản ngân hàng thụ hưởng.
 * @param payload Dữ liệu thanh toán (số tiền, requestKey,...).
 */
export function generateVietQrPayload(bankConfig: BankConfig, payload: QrPaymentPayload): VietQrResult {
  // Validate cấu hình và payload
  const configVal = validateBankConfig(bankConfig);
  if (!configVal.valid) {
    throw new Error(`Cấu hình ngân hàng không hợp lệ: ${configVal.errors.join('; ')}`);
  }

  const payloadVal = validateQrPaymentPayload(payload);
  if (!payloadVal.valid) {
    throw new Error(`Dữ liệu thanh toán QR không hợp lệ: ${payloadVal.errors.join('; ')}`);
  }

  const cleanBin = bankConfig.bankBin.trim();
  const cleanAccount = bankConfig.accountNumber.trim();
  const cleanAmount = payloadVal.sanitizedAmount!;
  const amountStr = Number.isInteger(cleanAmount) ? cleanAmount.toString() : cleanAmount.toFixed(2);
  const shortRef = extractShortReference(payload.requestKey);
  const transferContent = payload.orderDescription ? payload.orderDescription.trim() : `OISMS POS ${shortRef}`;

  // Xây dựng chuỗi EMVCo theo chuẩn VietQR NAPAS:
  // Tag 00: Payload Format Indicator = '01'
  const tag00 = formatTlv('00', '01');

  // Tag 01: Point of Initiation Method = '12' (Dynamic QR với số tiền xác định)
  const tag01 = formatTlv('01', '12');

  // Tag 38: Merchant Account Information - NAPAS
  //   Sub 00: AID = 'A000000727' (NAPAS)
  //   Sub 01: Beneficiary Organization:
  //     Sub-sub 00: Bank BIN
  //     Sub-sub 01: Account Number
  //   Sub 02: Service Code = 'QRIBFTTA' (Chuyển nhanh 247 qua tài khoản)
  const sub38_00 = formatTlv('00', 'A000000727');
  const sub38_01_00 = formatTlv('00', cleanBin);
  const sub38_01_01 = formatTlv('01', cleanAccount);
  const sub38_01 = formatTlv('01', `${sub38_01_00}${sub38_01_01}`);
  const sub38_02 = formatTlv('02', 'QRIBFTTA');
  const tag38 = formatTlv('38', `${sub38_00}${sub38_01}${sub38_02}`);

  // Tag 53: Transaction Currency = '704' (VND)
  const tag53 = formatTlv('53', '704');

  // Tag 54: Transaction Amount
  const tag54 = formatTlv('54', amountStr);

  // Tag 58: Country Code = 'VN'
  const tag58 = formatTlv('58', 'VN');

  // Tag 62: Additional Data Field Template
  //   Sub 08: Purpose of Transaction / Reference
  const sub62_08 = formatTlv('08', transferContent);
  const tag62 = formatTlv('62', sub62_08);

  // Ghép các trường dữ liệu trước trường CRC (Tag 63)
  const preCrc = `${tag00}${tag01}${tag38}${tag53}${tag54}${tag58}${tag62}6304`;

  // Tính toán CRC16-CCITT cho toàn bộ chuỗi
  const crcCode = calculateCrc16Ccitt(preCrc);
  const rawPayload = `${preCrc}${crcCode}`;

  // Kiểm tra lại tính hợp lệ của chuỗi đã ghép
  const verifyPayloadWithoutCrc = rawPayload.slice(0, -4);
  const calculatedVerificationCrc = calculateCrc16Ccitt(verifyPayloadWithoutCrc);
  const crcValid = calculatedVerificationCrc === crcCode;

  // Xây dựng đường dẫn QuickLink
  const template = bankConfig.template || 'compact2';
  const encodedContent = encodeURIComponent(transferContent);
  const encodedAccountName = encodeURIComponent(bankConfig.accountName.trim());
  const quickLinkUrl = `https://img.vietqr.io/image/${cleanBin}-${cleanAccount}-${template}.png?amount=${amountStr}&addInfo=${encodedContent}&accountName=${encodedAccountName}`;

  return {
    rawPayload,
    quickLinkUrl,
    transactionReference: shortRef,
    transferContent,
    crcValid,
    crcCode,
    status: 'AWAITING_CASHIER_CONFIRMATION',
    isTestFixture: Boolean(bankConfig.isTestFixture),
    createdAt: new Date().toISOString(),
  };
}

/**
 * Kiểm tra tính toàn vẹn và bất biến (Idempotent Invariance) khi Retry giao dịch:
 * Khi gửi lại cùng một requestKey, nội dung chuyển khoản, mã tham chiếu và số tiền phải giữ nguyên.
 */
export function assertIdempotentPayload(first: VietQrResult, retry: VietQrResult): boolean {
  if (first.transactionReference !== retry.transactionReference) {
    return false;
  }
  if (first.transferContent !== retry.transferContent) {
    return false;
  }
  if (first.rawPayload !== retry.rawPayload) {
    return false;
  }
  return true;
}

/**
 * GUARD BẢO MẬT: Kiểm tra xem giao dịch có thể tự động thành công do quét mã hoặc timeout hay không.
 *
 * THEO T04-AC-03: Tuyệt đối KHÔNG suy ra thanh toán thành công chỉ vì QR được hiển thị, được quét
 * hoặc hết thời gian đếm ngược.
 * Hàm này luôn trả về FALSE để bảo đảm quy tắc nghiệp vụ không bị vi phạm.
 */
export function isPaymentConfirmedByScanOrTimeout(): boolean {
  return false;
}
