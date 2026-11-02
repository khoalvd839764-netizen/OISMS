/**
 * Automated Test Suite for Standalone QR Module (S3-POS-B03 v1.2-integration)
 *
 * Verifies:
 * - B03-F01: Browser environment compatibility without Node Buffer
 * - B03-F02: Boundary validation for account (max 19), amount (max 13 digits), description (max 50, no special chars)
 *            and independent TLV parser structural verification
 * - B03-F03: Full requestKey UUID v4 identity & snapshot idempotency invariance
 * - T04-AC-03: Security Guard: displaying, scanning, or countdown NEVER marks payment confirmed
 */

import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
import vm from 'node:vm';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const require = createRequire(import.meta.url);
const ts = require(path.resolve(process.cwd(), 'src/frontend/pos-app/node_modules/typescript'));

// Helper transpile TypeScript to CommonJS for VM loading
// B03-F01: Tuyệt đối KHÔNG inject Buffer vào global context để mô phỏng chính xác môi trường Browser
function loadTs(relativeFilePath, injectBuffer = false) {
  const fullPath = path.resolve(__dirname, relativeFilePath);
  const src = fs.readFileSync(fullPath, 'utf8');
  const js = ts.transpileModule(src, {
    compilerOptions: {
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2022,
      esModuleInterop: true,
    },
  }).outputText;

  const mod = { exports: {} };
  const context = {
    exports: mod.exports,
    module: mod,
    require: (modName) => {
      if (modName.endsWith('.js') || modName.endsWith('.ts')) {
        const targetPath = path.resolve(path.dirname(fullPath), modName.replace(/\.js$/, '.ts'));
        return loadTs(targetPath, injectBuffer);
      }
      return require(modName);
    },
    // Browser globals
    TextEncoder,
    Uint8Array,
    console,
    Date,
    Math,
    Number,
    Error,
    encodeURIComponent,
    parseInt,
  };

  if (injectBuffer) {
    context.Buffer = Buffer;
  }

  vm.createContext(context);
  vm.runInContext(js, context);
  return mod.exports;
}

// Tải module mà KHÔNG có Node Buffer
const qrService = loadTs('./qr-service.ts', false);
const qrFixtures = loadTs('./qr-fixtures.ts', false);

const {
  calculateCrc16Ccitt,
  formatTlv,
  parseTlvPayload,
  validateBankConfig,
  validateQrPaymentPayload,
  generateVietQrPayload,
  assertIdempotentPayload,
  isPaymentConfirmedByScanOrTimeout,
  extractShortReference,
  MAX_MONEY_AMOUNT,
} = qrService;

const {
  VALID_TEST_BANK_VIETINBANK,
  VALID_TEST_BANK_MBBANK,
  VALID_TEST_BANK_VCB,
  INVALID_TEST_BANK_MISSING_BIN,
  INVALID_TEST_BANK_SHORT_BIN,
  INVALID_TEST_BANK_NON_NUMERIC_BIN,
  INVALID_TEST_BANK_EMPTY_ACCOUNT,
  INVALID_TEST_BANK_LONG_ACCOUNT,
  INVALID_TEST_BANK_EMPTY_NAME,
  VALID_PAYLOAD_STANDARD,
  VALID_PAYLOAD_MULTI_ITEMS,
  VALID_PAYLOAD_DECIMAL_AMOUNT,
  VALID_PAYLOAD_BOUNDARY_MAX,
  INVALID_PAYLOAD_ZERO_AMOUNT,
  INVALID_PAYLOAD_NEGATIVE_AMOUNT,
  INVALID_PAYLOAD_OVERFLOW_AMOUNT,
  INVALID_PAYLOAD_EXCESSIVE_DECIMALS,
  INVALID_PAYLOAD_LONG_DESCRIPTION,
  INVALID_PAYLOAD_SPECIAL_CHAR_DESCRIPTION,
  INVALID_PAYLOAD_EMPTY_TRIM_DESCRIPTION,
  INVALID_PAYLOAD_MISSING_REQUEST_KEY,
  INVALID_PAYLOAD_MALFORMED_REQUEST_KEY,
} = qrFixtures;

// ============================================================================
// TEST CASES
// ============================================================================

test('TC-QR-01: B03-F01 - CRC-16/CCITT-FALSE vector kiểm chuẩn không dùng Node Buffer', () => {
  // Vector chuẩn quốc tế: chuỗi ASCII '123456789' -> CRC '29B1'
  const testVector = '123456789';
  const expectedCrc = '29B1';
  const actualCrc = calculateCrc16Ccitt(testVector);
  assert.equal(actualCrc, expectedCrc, `CRC của chuỗi '${testVector}' phải là '${expectedCrc}'`);

  // Vector EMVCo Tag 00 01
  const emvSample = '000201010212';
  const emvCrc = calculateCrc16Ccitt(emvSample);
  assert.equal(typeof emvCrc, 'string');
  assert.equal(emvCrc.length, 4);
});

test('TC-QR-02: B03-F01 & T04-AC-02 - Sinh chuỗi EMVCo VietQR và QuickLink URL hợp lệ trong Browser', () => {
  const result = generateVietQrPayload(VALID_TEST_BANK_VIETINBANK, VALID_PAYLOAD_STANDARD);

  assert.ok(result.rawPayload, 'Phải sinh chuỗi rawPayload EMVCo');
  assert.equal(result.crcValid, true, 'CRC kiểm tra phải hợp lệ');
  assert.equal(result.crcCode.length, 4, 'Mã CRC phải có độ dài 4 hex');

  // Kiểm tra các trường cơ bản trong rawPayload
  assert.ok(result.rawPayload.startsWith('000201'), 'Tag 00 phải là 01');
  assert.ok(result.rawPayload.includes('010212'), 'Tag 01 phải là 12 (Dynamic QR)');
  assert.ok(result.rawPayload.includes('5303704'), 'Tag 53 phải là 704 (VND)');
  assert.ok(result.rawPayload.includes('5406450000'), 'Tag 54 phải là 450000');
  assert.ok(result.rawPayload.includes('5802VN'), 'Tag 58 phải là VN');
  assert.ok(result.rawPayload.includes('970415'), 'Tag 38 phải chứa Bank BIN VietinBank 970415');
  assert.ok(result.rawPayload.includes('101088889999'), 'Tag 38 phải chứa Số tài khoản 101088889999');

  // QuickLink URL
  assert.ok(result.quickLinkUrl.startsWith('https://img.vietqr.io/image/970415-101088889999-compact2.png'));
  assert.ok(result.quickLinkUrl.includes('amount=450000'));
  assert.ok(result.quickLinkUrl.includes('addInfo=OISMS%20POS%20A1B2C3D4'));

  // Mã tham chiếu và trạng thái
  assert.equal(result.transactionReference, 'A1B2C3D4');
  assert.equal(result.status, 'AWAITING_CASHIER_CONFIRMATION');
  assert.equal(result.isTestFixture, true);
});

test('TC-QR-03: B03-F02 - Bộ parser TLV độc lập giải mã và kiểm chứng cấu trúc chuỗi EMVCo', () => {
  const result = generateVietQrPayload(VALID_TEST_BANK_MBBANK, VALID_PAYLOAD_MULTI_ITEMS);
  const parsed = parseTlvPayload(result.rawPayload);

  assert.equal(parsed.crcValid, true, 'CRC độc lập tính lại phải khớp tuyệt đối');
  assert.ok(parsed.fields.length >= 6, 'Payload phải chứa đầy đủ các trường Tag EMVCo chuẩn');

  const tag00 = parsed.fields.find((f) => f.tag === '00');
  assert.equal(tag00?.value, '01');

  const tag01 = parsed.fields.find((f) => f.tag === '01');
  assert.equal(tag01?.value, '12');

  const tag54 = parsed.fields.find((f) => f.tag === '54');
  assert.equal(tag54?.value, '1648000');

  const tag58 = parsed.fields.find((f) => f.tag === '58');
  assert.equal(tag58?.value, 'VN');
});

test('TC-QR-04: B03-F02 - Kiểm tra biên và từ chối cấu hình ngân hàng lỗi (account > 19 ký tự)', () => {
  // Thiếu BIN
  assert.equal(validateBankConfig(INVALID_TEST_BANK_MISSING_BIN).valid, false);
  // BIN ngắn hơn 6 số
  assert.equal(validateBankConfig(INVALID_TEST_BANK_SHORT_BIN).valid, false);
  // BIN chứa chữ cái
  assert.equal(validateBankConfig(INVALID_TEST_BANK_NON_NUMERIC_BIN).valid, false);
  // Số tài khoản rỗng
  assert.equal(validateBankConfig(INVALID_TEST_BANK_EMPTY_ACCOUNT).valid, false);
  // Tên tài khoản rỗng
  assert.equal(validateBankConfig(INVALID_TEST_BANK_EMPTY_NAME).valid, false);

  // B03-F02: Số tài khoản 20 ký tự (vượt trần 19 ký tự của QuickLink/NAPAS) phải bị từ chối
  const longAccRes = validateBankConfig(INVALID_TEST_BANK_LONG_ACCOUNT);
  assert.equal(longAccRes.valid, false, 'Số tài khoản 20 ký tự phải bị từ chối');
  assert.match(longAccRes.errors[0], /19 ký tự/);

  // Thử sinh payload với config lỗi phải ném ngoại lệ
  assert.throws(() => {
    generateVietQrPayload(INVALID_TEST_BANK_LONG_ACCOUNT, VALID_PAYLOAD_STANDARD);
  }, /Cấu hình ngân hàng không hợp lệ/);
});

test('TC-QR-05: B03-F02 - Kiểm tra biên số tiền (tiền âm, 0, tiền vượt 13 chữ số, số lẻ quá 2 thập phân)', () => {
  // Tiền = 0
  assert.equal(validateQrPaymentPayload(INVALID_PAYLOAD_ZERO_AMOUNT).valid, false);
  // Tiền âm
  assert.equal(validateQrPaymentPayload(INVALID_PAYLOAD_NEGATIVE_AMOUNT).valid, false);
  // Tiền quá 2 số thập phân
  assert.equal(validateQrPaymentPayload(INVALID_PAYLOAD_EXCESSIVE_DECIMALS).valid, false);

  // B03-F02: Tiền vượt 13 chữ số (10,000,000,000,000) phải bị từ chối
  const overflowRes = validateQrPaymentPayload(INVALID_PAYLOAD_OVERFLOW_AMOUNT);
  assert.equal(overflowRes.valid, false, 'Tiền 14 chữ số phải bị từ chối theo chuẩn QuickLink');
  assert.match(overflowRes.errors[0], /13 chữ số/);

  // Giá trị trần hợp lệ 13 chữ số (9,999,999,999,999) phải được chấp nhận
  const boundaryMaxRes = validateQrPaymentPayload(VALID_PAYLOAD_BOUNDARY_MAX);
  assert.equal(boundaryMaxRes.valid, true, 'Giá trị trần 13 chữ số phải được chấp nhận');

  // Decimal hợp lệ (1 số thập phân)
  const decimalRes = validateQrPaymentPayload(VALID_PAYLOAD_DECIMAL_AMOUNT);
  assert.equal(decimalRes.valid, true);
  assert.equal(decimalRes.sanitizedAmount, 99999.5);
});

test('TC-QR-06: B03-F02 - Kiểm tra biên nội dung chuyển khoản (vượt 50 ký tự, ký tự đặc biệt, trim rỗng)', () => {
  // Vượt 50 ký tự
  const longDescRes = validateQrPaymentPayload(INVALID_PAYLOAD_LONG_DESCRIPTION);
  assert.equal(longDescRes.valid, false, 'Nội dung > 50 ký tự phải bị từ chối');
  assert.match(longDescRes.errors[0], /50 ký tự/);

  // Ký tự đặc biệt
  const specialRes = validateQrPaymentPayload(INVALID_PAYLOAD_SPECIAL_CHAR_DESCRIPTION);
  assert.equal(specialRes.valid, false, 'Nội dung có ký tự đặc biệt phải bị từ chối');

  // Khoảng trắng rỗng sau trim
  const emptyTrimRes = validateQrPaymentPayload(INVALID_PAYLOAD_EMPTY_TRIM_DESCRIPTION);
  assert.equal(emptyTrimRes.valid, false, 'Nội dung rỗng sau trim phải bị từ chối');

  // Format TLV kiểm tra overflow
  assert.throws(() => {
    formatTlv('08', 'A'.repeat(100)); // 100 bytes vượt quá 99 bytes (2 chữ số length)
  }, /vượt quá giới hạn 2 chữ số/);
});

test('TC-QR-07: Kiểm tra RequestKey UUID v4 hợp lệ', () => {
  // Thiếu requestKey
  assert.equal(validateQrPaymentPayload(INVALID_PAYLOAD_MISSING_REQUEST_KEY).valid, false);
  // RequestKey không đúng UUID v4
  assert.equal(validateQrPaymentPayload(INVALID_PAYLOAD_MALFORMED_REQUEST_KEY).valid, false);

  // Trích xuất mã tham chiếu ngắn 8 ký tự
  const ref = extractShortReference('a1b2c3d4-e5f6-4789-abcd-ef1234567890');
  assert.equal(ref, 'A1B2C3D4');
});

test('TC-QR-08: T04-AC-03 - Guard bảo mật: Tuyệt đối KHÔNG tự động xác nhận thanh toán do hiển thị/quét mã', () => {
  assert.equal(
    isPaymentConfirmedByScanOrTimeout(),
    false,
    'Hàm Guard phải luôn trả về false; thanh toán QR tại quầy bắt buộc do thu ngân kiểm tra và xác nhận thủ công'
  );
});

test('TC-QR-09: B03-F03 - Idempotency & Full Request Identity (phân biệt hai UUID trùng 8 ký tự đầu)', () => {
  const first = generateVietQrPayload(VALID_TEST_BANK_VIETINBANK, VALID_PAYLOAD_STANDARD);

  // Retry cùng RequestKey và cùng dữ liệu -> Phải coi là cùng giao dịch Idempotent
  const retrySame = generateVietQrPayload(VALID_TEST_BANK_VIETINBANK, VALID_PAYLOAD_STANDARD);
  assert.equal(assertIdempotentPayload(first, retrySame), true, 'Cùng requestKey và snapshot phải idempotent');

  // B03-F03: Hai UUID KHÁC NHAU nhưng CÙNG có 8 ký tự đầu giống nhau ('a1b2c3d4-...')
  const differentKey = 'a1b2c3d4-0000-4000-8000-000000000001';
  assert.equal(differentKey.slice(0, 8), VALID_PAYLOAD_STANDARD.requestKey.slice(0, 8)); // Khẳng định trùng 8 ký tự đầu
  const differentResult = generateVietQrPayload(VALID_TEST_BANK_VIETINBANK, {
    ...VALID_PAYLOAD_STANDARD,
    requestKey: differentKey,
  });

  // B03-F03 FIX: assertIdempotentPayload PHẢI TRẢ VỀ FALSE vì requestKey khác nhau!
  const isConsideredSame = assertIdempotentPayload(first, differentResult);
  assert.equal(
    isConsideredSame,
    false,
    'Hai UUID khác nhau dù trùng 8 ký tự đầu TUYỆT ĐỐI KHÔNG ĐƯỢC coi là cùng giao dịch Idempotent!'
  );

  // Thay đổi số tiền với cùng requestKey -> Snapshot khác nhau -> Không hợp lệ
  const differentAmount = generateVietQrPayload(VALID_TEST_BANK_VIETINBANK, {
    ...VALID_PAYLOAD_STANDARD,
    amount: 500000,
  });
  assert.equal(assertIdempotentPayload(first, differentAmount), false, 'Khác số tiền phải trả về false');
});

test('TC-QR-10: B03-F01 - Khẳng định module chạy độc lập trong môi trường VM không có Node Buffer', () => {
  // Transpile và chạy hoàn toàn không có Buffer trong global context
  const cleanBrowserService = loadTs('./qr-service.ts', false);
  const result = cleanBrowserService.generateVietQrPayload(VALID_TEST_BANK_VCB, VALID_PAYLOAD_DECIMAL_AMOUNT);

  assert.ok(result.rawPayload);
  assert.equal(result.crcValid, true);
  assert.equal(result.transactionReference, 'C3D4E5F6');
  assert.equal(result.snapshot.amount, 99999.5);
  assert.equal(result.requestKey, 'c3d4e5f6-a7b8-4901-8def-ab3456789012');
});
