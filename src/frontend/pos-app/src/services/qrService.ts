/**
 * Dịch vụ Xử lý & Sinh Mã VietQR POS (S3-POS-B03)
 *
 * Chức năng:
 * 1. Chuẩn hóa & mã hóa chuỗi EMVCo / VietQR (NAPAS 247) với kiểm tra CRC-16/CCITT-FALSE.
 * 2. Sinh đường dẫn QuickLink VietQR cho hiển thị hình ảnh động.
 * 3. Kiểm định nghiêm ngặt số tiền, cấu hình ngân hàng và khóa Idempotency (RequestKey UUID v4).
 * 4. Bảo đảm tính bất biến (Idempotent Invariance) của nội dung QR và mã tham chiếu khi retry.
 * 5. Thiết lập Guard chống suy diễn thanh toán thành công từ hiển thị, quét mã hoặc hết thời gian.
 *
 * Tương thích 100% môi trường Browser, không phụ thuộc Node.js Buffer (B03-F01).
 */

import type {
  BankConfig,
  QrPaymentPayload,
  VietQrResult,
  QrValidationResult,
  QrSnapshotData,
  TlvField,
  ScopedBankConfig,
  PosQrIntent,
} from '../types/qr';

/** Giới hạn tiền tệ hệ thống OISMS theo chuẩn QuickLink/NAPAS: tối đa 13 chữ số nguyên (9.999.999.999.999 đ) */
export const MAX_MONEY_AMOUNT = 9_999_999_999_999;

/** Regex kiểm tra UUID v4 RFC 4122 chuẩn */
export const UUID_V4_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

/** Regex kiểm tra 6 chữ số BIN ngân hàng */
export const BANK_BIN_REGEX = /^\d{6}$/;

/** Regex kiểm tra số tài khoản ngân hàng thụ hưởng (6-19 ký tự alphanumeric theo chuẩn QuickLink/NAPAS) */
export const ACCOUNT_NUMBER_REGEX = /^[0-9a-zA-Z]{6,19}$/;

/** Regex kiểm tra nội dung chuyển khoản hợp lệ (tối đa 50 ký tự không có ký tự đặc biệt) */
export const DESCRIPTION_REGEX = /^[0-9a-zA-Z\s\-_]{1,50}$/;

/**
 * Đếm độ dài byte UTF-8 của chuỗi ký tự mà không dùng Buffer của Node.js (tương thích 100% browser).
 */
export function getUtf8ByteLength(str: string): number {
  if (typeof TextEncoder !== 'undefined') {
    return new TextEncoder().encode(str).length;
  }
  let count = 0;
  for (let i = 0; i < str.length; i++) {
    const code = str.charCodeAt(i);
    if (code <= 0x7f) count += 1;
    else if (code <= 0x7ff) count += 2;
    else if (code >= 0xd800 && code <= 0xdbff) {
      count += 4;
      i++; // surrogate pair
    } else count += 3;
  }
  return count;
}

/**
 * Chuyển chuỗi sang mảng byte UTF-8 (Uint8Array) hoàn toàn không dùng Buffer.
 */
export function stringToUtf8Bytes(str: string): Uint8Array {
  if (typeof TextEncoder !== 'undefined') {
    return new TextEncoder().encode(str);
  }
  const bytes: number[] = [];
  for (let i = 0; i < str.length; i++) {
    const charCode = str.charCodeAt(i);
    if (charCode <= 0x7f) {
      bytes.push(charCode);
    } else if (charCode <= 0x7ff) {
      bytes.push(0xc0 | (charCode >> 6), 0x80 | (charCode & 0x3f));
    } else if (charCode >= 0xd800 && charCode <= 0xdbff) {
      i++;
      const nextCharCode = str.charCodeAt(i);
      const codePoint = 0x10000 + (((charCode & 0x3ff) << 10) | (nextCharCode & 0x3ff));
      bytes.push(
        0xf0 | (codePoint >> 18),
        0x80 | ((codePoint >> 12) & 0x3f),
        0x80 | ((codePoint >> 6) & 0x3f),
        0x80 | (codePoint & 0x3f)
      );
    } else {
      bytes.push(0xe0 | (charCode >> 12), 0x80 | ((charCode >> 6) & 0x3f), 0x80 | (charCode & 0x3f));
    }
  }
  return new Uint8Array(bytes);
}

/**
 * Tính toán mã CRC-16/CCITT-FALSE (đa thức 0x1021, giá trị khởi tạo 0xFFFF, không đảo bit).
 */
export function calculateCrc16Ccitt(str: string): string {
  let crc = 0xffff;
  const polynomial = 0x1021;
  const bytes = stringToUtf8Bytes(str);

  for (let i = 0; i < bytes.length; i++) {
    crc ^= bytes[i] << 8;
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
 */
export function formatTlv(tag: string, value: string): string {
  const len = getUtf8ByteLength(value);
  if (len > 99) {
    throw new Error(`Độ dài trường dữ liệu Tag ${tag} vượt quá giới hạn 2 chữ số (tối đa 99 bytes, nhận được ${len} bytes).`);
  }
  const lenStr = len.toString().padStart(2, '0');
  return `${tag}${lenStr}${value}`;
}

/**
 * Bộ giải mã TLV độc lập (Independent TLV Parser):
 * Đọc tuần tự các trường 2 chữ số Tag, 2 chữ số Length, và Value để xác minh tính toàn vẹn cấu trúc EMVCo.
 */
export function parseTlvPayload(rawPayload: string): { fields: TlvField[]; crcValid: boolean; crcCalculated: string; crcInPayload: string } {
  const fields: TlvField[] = [];
  let offset = 0;

  if (rawPayload.length < 4) {
    throw new Error('Chuỗi payload quá ngắn để chứa trường TLV hợp lệ.');
  }

  const payloadWithoutCrc = rawPayload.slice(0, -4);
  const crcInPayload = rawPayload.slice(-4);
  const crcCalculated = calculateCrc16Ccitt(payloadWithoutCrc);
  const crcValid = crcInPayload.toUpperCase() === crcCalculated.toUpperCase();

  while (offset < rawPayload.length) {
    if (offset + 4 > rawPayload.length) {
      throw new Error(`Lỗi cấu trúc TLV tại offset ${offset}: không đủ 4 ký tự cho Tag và Length.`);
    }

    const tag = rawPayload.slice(offset, offset + 2);
    const lengthStr = rawPayload.slice(offset + 2, offset + 4);

    if (!/^\d{2}$/.test(tag) || !/^\d{2}$/.test(lengthStr)) {
      throw new Error(`Lỗi định dạng TLV tại offset ${offset}: Tag '${tag}' hoặc Length '${lengthStr}' không phải là 2 chữ số.`);
    }

    const length = parseInt(lengthStr, 10);
    const valueStart = offset + 4;
    const valueEnd = valueStart + length;

    if (valueEnd > rawPayload.length) {
      throw new Error(`Lỗi tràn dữ liệu TLV tại offset ${offset}: Tag '${tag}' khai báo length ${length} nhưng chuỗi chỉ còn ${rawPayload.length - valueStart} ký tự.`);
    }

    const value = rawPayload.slice(valueStart, valueEnd);
    fields.push({ tag, length, value });

    offset = valueEnd;
  }

  return { fields, crcValid, crcCalculated, crcInPayload };
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
  } else if (!ACCOUNT_NUMBER_REGEX.test(config.accountNumber.trim())) {
    errors.push('Số tài khoản ngân hàng không hợp lệ (phải từ 6 đến 19 ký tự chữ hoặc số theo chuẩn QuickLink/NAPAS).');
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
    errors.push(`Số tiền thanh toán vượt quá giới hạn tối đa cho phép của QuickLink/NAPAS (${MAX_MONEY_AMOUNT.toLocaleString('vi-VN')} đ, tối đa 13 chữ số).`);
  } else {
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

  // 3. Kiểm tra nội dung mô tả
  let sanitizedDesc: string | undefined;
  if (payload.orderDescription !== undefined) {
    const trimmed = payload.orderDescription.trim();
    if (trimmed.length === 0) {
      errors.push('Nội dung chuyển khoản không được để trống hoặc chỉ chứa khoảng trắng.');
    } else if (trimmed.length > 50) {
      errors.push(`Nội dung chuyển khoản vượt quá giới hạn 50 ký tự của QuickLink (nhận được ${trimmed.length} ký tự).`);
    } else if (!DESCRIPTION_REGEX.test(trimmed)) {
      errors.push('Nội dung chuyển khoản chứa ký tự đặc biệt không được hỗ trợ bởi VietQR/QuickLink.');
    } else {
      sanitizedDesc = trimmed;
    }
  }

  return {
    valid: errors.length === 0,
    errors,
    sanitizedAmount: typeof payload.amount === 'number' && payload.amount > 0 ? Math.round(payload.amount * 100) / 100 : undefined,
    sanitizedDescription: sanitizedDesc,
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
 */
export function generateVietQrPayload(bankConfig: BankConfig, payload: QrPaymentPayload): VietQrResult {
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
  const transferContent = payloadVal.sanitizedDescription || `OISMS POS ${shortRef}`;

  if (transferContent.length > 50) {
    throw new Error(`Nội dung chuyển khoản quá dài (${transferContent.length} ký tự), vượt trần 50 ký tự.`);
  }

  // Tag 00: Payload Format Indicator = '01'
  const tag00 = formatTlv('00', '01');

  // Tag 01: Point of Initiation Method = '12' (Dynamic QR với số tiền xác định)
  const tag01 = formatTlv('01', '12');

  // Tag 38: Merchant Account Information - NAPAS
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

  // Tag 62: Additional Data Field Template (Sub 08: Purpose of Transaction / Reference)
  const sub62_08 = formatTlv('08', transferContent);
  const tag62 = formatTlv('62', sub62_08);

  // Ghép các trường trước trường CRC (Tag 63)
  const preCrc = `${tag00}${tag01}${tag38}${tag53}${tag54}${tag58}${tag62}6304`;

  // Tính toán CRC16-CCITT cho toàn bộ chuỗi
  const crcCode = calculateCrc16Ccitt(preCrc);
  const rawPayload = `${preCrc}${crcCode}`;

  // Kiểm chứng lại tính toàn vẹn cấu trúc qua bộ parser TLV độc lập
  const parsed = parseTlvPayload(rawPayload);
  if (!parsed.crcValid) {
    throw new Error(`Lỗi toàn vẹn dữ liệu: CRC tính toán không khớp với mã kiểm tra sinh ra (${parsed.crcCalculated} !== ${parsed.crcInPayload}).`);
  }

  // Đường dẫn QuickLink
  const template = bankConfig.template || 'compact2';
  const encodedContent = encodeURIComponent(transferContent);
  const encodedAccountName = encodeURIComponent(bankConfig.accountName.trim());
  const quickLinkUrl = `https://img.vietqr.io/image/${cleanBin}-${cleanAccount}-${template}.png?amount=${amountStr}&addInfo=${encodedContent}&accountName=${encodedAccountName}`;

  const snapshot: QrSnapshotData = {
    requestKey: payload.requestKey,
    amount: cleanAmount,
    bankBin: cleanBin,
    accountNumber: cleanAccount,
    orderDescription: transferContent,
    branchId: payload.branchId,
  };

  return {
    rawPayload,
    quickLinkUrl,
    transactionReference: shortRef,
    transferContent,
    crcValid: parsed.crcValid,
    crcCode,
    status: 'AWAITING_CASHIER_CONFIRMATION',
    isTestFixture: Boolean(bankConfig.isTestFixture),
    createdAt: new Date().toISOString(),
    requestKey: payload.requestKey,
    snapshot,
  };
}

/**
 * Kiểm tra tính toàn vẹn và bất biến (Idempotent Invariance) khi Retry giao dịch:
 * Bắt buộc đối chiếu full RequestKey UUID và dữ liệu snapshot;
 * hai UUID khác nhau dù trùng 8 ký tự đầu KHÔNG ĐƯỢC coi là cùng giao dịch.
 */
export function assertIdempotentPayload(first: VietQrResult, retry: VietQrResult): boolean {
  if (!first || !retry) return false;

  if (first.requestKey !== retry.requestKey) {
    return false;
  }
  if (first.snapshot?.amount !== retry.snapshot?.amount) {
    return false;
  }
  if (first.snapshot?.bankBin !== retry.snapshot?.bankBin) {
    return false;
  }
  if (first.snapshot?.accountNumber !== retry.snapshot?.accountNumber) {
    return false;
  }
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

/**
 * Lấy cấu hình tài khoản ngân hàng thụ hưởng cho POS.
 * 
 * Nguồn ưu tiên:
 * 1. window.__OISMS_POS_BANK_CONFIGS__: danh sách envelope gắn tenant + chi nhánh.
 * 2. Nếu không inject danh sách, đọc localStorage theo getPosBankConfigKey.
 * Chỉ nhận envelope version 1 khớp cả hai ID. Thiếu/sai cấu hình trả về null;
 * không dùng cấu hình global cũ, biến môi trường hay tài khoản mẫu làm fallback.
 */
export function getPosBankConfig(branchId?: string, tenantId?: string): BankConfig | null {
  if (!branchId || !tenantId) return null;
  try {
    // A deployment-provided array is authoritative; no cross-context fallback.
    const configured = typeof window !== 'undefined' ? (window as any).__OISMS_POS_BANK_CONFIGS__ : undefined;
    const candidate: ScopedBankConfig | undefined = configured !== undefined
      ? (Array.isArray(configured) ? configured.find((item: ScopedBankConfig) => item?.tenantId === tenantId && item?.branchId === branchId) : undefined)
      : JSON.parse(localStorage.getItem(getPosBankConfigKey(tenantId, branchId)) || 'null');
    if (candidate?.version !== 1 || candidate.tenantId !== tenantId || candidate.branchId !== branchId || !validateBankConfig(candidate.bankConfig).valid) return null;
    return { ...candidate.bankConfig };
  } catch {
    return null;
  }
}

export function getPosBankConfigKey(tenantId: string, branchId: string): string {
  return `pos_bank_config_v1:${encodeURIComponent(tenantId)}:${encodeURIComponent(branchId)}`;
}

type QrUser = { id?: string; tenantId?: string } | null | undefined;
const intentMemory = new Map<string, string>();
function intentKey(user: QrUser, branchId: string): string | null {
  return user?.id && user.tenantId && branchId
    ? `pos_qr_intent_v1:${encodeURIComponent(user.tenantId)}:${encodeURIComponent(user.id)}:${encodeURIComponent(branchId)}` : null;
}

export function isPosQrIntentFor(intent: PosQrIntent | undefined | null, user: QrUser, branchId: string): intent is PosQrIntent {
  return Boolean(intent?.version === 1 && user?.id && user.tenantId &&
    intent.context?.userId === user.id && intent.context.tenantId === user.tenantId && intent.context.branchId === branchId &&
    UUID_V4_REGEX.test(intent.requestKey) && Number.isFinite(intent.amount) && intent.amount > 0 &&
    typeof intent.cartFingerprint === 'string' && validateBankConfig(intent.bankConfig).valid);
}

export function getOrCreatePosQrIntent(user: QrUser, branchId: string, cartFingerprint: string, amount: number, bank: BankConfig | null, createKey: () => string): PosQrIntent | null {
  const key = intentKey(user, branchId);
  if (!key) return null;
  let saved: PosQrIntent | null = null;
  try { saved = JSON.parse(sessionStorage.getItem(key) || intentMemory.get(key) || 'null'); } catch { try { saved = JSON.parse(intentMemory.get(key) || 'null'); } catch {} }
  if (isPosQrIntentFor(saved, user, branchId) && saved.cartFingerprint === cartFingerprint && saved.amount === amount) return saved;
  if (!bank || !validateBankConfig(bank).valid) return null;
  const intent: PosQrIntent = {
    version: 1, context: { tenantId: user!.tenantId!, userId: user!.id!, branchId },
    cartFingerprint, amount, requestKey: createKey(), bankConfig: { ...bank },
  };
  if (!isPosQrIntentFor(intent, user, branchId)) return null;
  const raw = JSON.stringify(intent);
  intentMemory.set(key, raw);
  try { sessionStorage.setItem(key, raw); } catch {}
  return intent;
}

export function clearPosQrIntent(user: QrUser, branchId: string): void {
  const key = intentKey(user, branchId);
  if (!key) return;
  intentMemory.delete(key);
  try { sessionStorage.removeItem(key); } catch {}
}
