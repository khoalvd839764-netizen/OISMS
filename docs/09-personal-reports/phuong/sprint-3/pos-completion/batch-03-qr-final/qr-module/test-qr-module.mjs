/**
 * Automated Test Suite for Standalone QR Module (S3-POS-B03)
 *
 * Verifies:
 * - EMVCo / VietQR NAPAS 247 specification compliance
 * - CRC-16/CCITT-FALSE calculation correctness
 * - Bank configuration and payload boundary validation
 * - RequestKey UUID v4 validation and retry idempotency
 * - T04-AC-03 Security Guard: displaying, scanning, or countdown NEVER marks payment confirmed
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
function loadTs(relativeFilePath) {
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
        return loadTs(targetPath);
      }
      return require(modName);
    },
    Buffer,
    console,
    Date,
    Math,
    Number,
    Error,
    encodeURIComponent,
  };
  vm.createContext(context);
  vm.runInContext(js, context);
  return mod.exports;
}

const qrService = loadTs('./qr-service.ts');
const qrFixtures = loadTs('./qr-fixtures.ts');

const {
  calculateCrc16Ccitt,
  formatTlv,
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
  INVALID_TEST_BANK_EMPTY_NAME,
  VALID_PAYLOAD_STANDARD,
  VALID_PAYLOAD_MULTI_ITEMS,
  VALID_PAYLOAD_DECIMAL_AMOUNT,
  VALID_PAYLOAD_BOUNDARY_MAX,
  INVALID_PAYLOAD_ZERO_AMOUNT,
  INVALID_PAYLOAD_NEGATIVE_AMOUNT,
  INVALID_PAYLOAD_OVERFLOW_AMOUNT,
  INVALID_PAYLOAD_EXCESSIVE_DECIMALS,
  INVALID_PAYLOAD_MISSING_REQUEST_KEY,
  INVALID_PAYLOAD_MALFORMED_REQUEST_KEY,
} = qrFixtures;

// ============================================================================
// TEST CASES
// ============================================================================

test('TC-QR-01: EMVCo / VietQR Payload Format Compliance', () => {
  const result = generateVietQrPayload(VALID_TEST_BANK_VIETINBANK, VALID_PAYLOAD_STANDARD);

  assert.ok(result.rawPayload, 'rawPayload must be present');
  assert.ok(result.rawPayload.startsWith('000201'), 'Must start with Tag 00 Payload Format Indicator 01');
  assert.ok(result.rawPayload.includes('010212'), 'Must include Tag 01 Point of Initiation Method 12 (Dynamic)');
  assert.ok(result.rawPayload.includes('A000000727'), 'Must include NAPAS AID A000000727');
  assert.ok(result.rawPayload.includes('970415'), 'Must include Bank BIN 970415');
  assert.ok(result.rawPayload.includes('101088889999'), 'Must include Account Number 101088889999');
  assert.ok(result.rawPayload.includes('QRIBFTTA'), 'Must include Service Code QRIBFTTA');
  assert.ok(result.rawPayload.includes('5303704'), 'Must include Tag 53 Currency 704 (VND)');
  assert.ok(result.rawPayload.includes('5406450000'), 'Must include Tag 54 Amount 450000');
  assert.ok(result.rawPayload.includes('5802VN'), 'Must include Tag 58 Country Code VN');
  assert.ok(result.rawPayload.includes('6304'), 'Must include Tag 63 CRC indicator');
  assert.equal(result.crcValid, true, 'CRC validation flag must be true');
  assert.equal(result.rawPayload.slice(-4), result.crcCode, 'Last 4 chars of payload must match CRC code');
});

test('TC-QR-02: CRC-16/CCITT-FALSE Calculation Accuracy', () => {
  // Test vector: '123456789' -> CRC 0x29B1
  const standardTestCrc = calculateCrc16Ccitt('123456789');
  assert.equal(standardTestCrc, '29B1', 'CRC of 123456789 must equal 29B1');

  // Verify round-trip calculation on generated VietQR string
  const res = generateVietQrPayload(VALID_TEST_BANK_MBBANK, VALID_PAYLOAD_MULTI_ITEMS);
  const dataBeforeCrc = res.rawPayload.slice(0, -4);
  const computedCrc = calculateCrc16Ccitt(dataBeforeCrc);
  assert.equal(computedCrc, res.crcCode, 'Computed CRC over payload must match recorded crcCode');
});

test('TC-QR-03: QuickLink URL Generation Matches VietQR Standard', () => {
  const result = generateVietQrPayload(VALID_TEST_BANK_VCB, VALID_PAYLOAD_STANDARD);

  assert.ok(result.quickLinkUrl.startsWith('https://img.vietqr.io/image/970436-0071001234567-compact2.png'), 'QuickLink URL format matches');
  assert.ok(result.quickLinkUrl.includes('amount=450000'), 'QuickLink URL contains correct amount');
  assert.ok(result.quickLinkUrl.includes('addInfo='), 'QuickLink URL contains addInfo query parameter');
  assert.ok(result.quickLinkUrl.includes('accountName='), 'QuickLink URL contains accountName query parameter');
});

test('TC-QR-04: BankConfig Boundary & Error Validation', () => {
  // 1. Missing config
  const valNull = validateBankConfig(null);
  assert.equal(valNull.valid, false);
  assert.ok(valNull.errors.length > 0);

  // 2. Missing BIN
  const valMissingBin = validateBankConfig(INVALID_TEST_BANK_MISSING_BIN);
  assert.equal(valMissingBin.valid, false);
  assert.ok(valMissingBin.errors.some((e) => e.includes('Bank BIN')));

  // 3. Short BIN (4 digits instead of 6)
  const valShortBin = validateBankConfig(INVALID_TEST_BANK_SHORT_BIN);
  assert.equal(valShortBin.valid, false);
  assert.ok(valShortBin.errors.some((e) => e.includes('Bank BIN')));

  // 4. Non-numeric BIN
  const valNonNumBin = validateBankConfig(INVALID_TEST_BANK_NON_NUMERIC_BIN);
  assert.equal(valNonNumBin.valid, false);
  assert.ok(valNonNumBin.errors.some((e) => e.includes('Bank BIN')));

  // 5. Empty account number
  const valEmptyAcc = validateBankConfig(INVALID_TEST_BANK_EMPTY_ACCOUNT);
  assert.equal(valEmptyAcc.valid, false);
  assert.ok(valEmptyAcc.errors.some((e) => e.includes('Số tài khoản')));

  // 6. Empty account name
  const valEmptyName = validateBankConfig(INVALID_TEST_BANK_EMPTY_NAME);
  assert.equal(valEmptyName.valid, false);
  assert.ok(valEmptyName.errors.some((e) => e.includes('Tên chủ tài khoản')));
});

test('TC-QR-05: Amount Boundary & Currency Scale Validation', () => {
  // 1. Zero amount
  const valZero = validateQrPaymentPayload(INVALID_PAYLOAD_ZERO_AMOUNT);
  assert.equal(valZero.valid, false);
  assert.ok(valZero.errors.some((e) => e.includes('lớn hơn 0')));

  // 2. Negative amount
  const valNeg = validateQrPaymentPayload(INVALID_PAYLOAD_NEGATIVE_AMOUNT);
  assert.equal(valNeg.valid, false);
  assert.ok(valNeg.errors.some((e) => e.includes('lớn hơn 0')));

  // 3. Overflow amount > MaxMoneyAmount
  const valOverflow = validateQrPaymentPayload(INVALID_PAYLOAD_OVERFLOW_AMOUNT);
  assert.equal(valOverflow.valid, false);
  assert.ok(valOverflow.errors.some((e) => e.includes('vượt quá giới hạn tối đa')));

  // 4. Excessive decimals (> 2 decimal places)
  const valDec = validateQrPaymentPayload(INVALID_PAYLOAD_EXCESSIVE_DECIMALS);
  assert.equal(valDec.valid, false);
  assert.ok(valDec.errors.some((e) => e.includes('2 chữ số thập phân')));

  // 5. Boundary max amount (Valid)
  const valMax = validateQrPaymentPayload(VALID_PAYLOAD_BOUNDARY_MAX);
  assert.equal(valMax.valid, true);
  assert.equal(valMax.sanitizedAmount, MAX_MONEY_AMOUNT);
});

test('TC-QR-06: RequestKey UUID v4 Validation & Reference Extraction', () => {
  // 1. Missing RequestKey
  const valMissing = validateQrPaymentPayload(INVALID_PAYLOAD_MISSING_REQUEST_KEY);
  assert.equal(valMissing.valid, false);
  assert.ok(valMissing.errors.some((e) => e.includes('RequestKey')));

  // 2. Malformed RequestKey
  const valMalformed = validateQrPaymentPayload(INVALID_PAYLOAD_MALFORMED_REQUEST_KEY);
  assert.equal(valMalformed.valid, false);
  assert.ok(valMalformed.errors.some((e) => e.includes('UUID v4 RFC 4122')));

  // 3. Short reference extraction from valid UUID
  const shortRef = extractShortReference('a1b2c3d4-e5f6-4789-abcd-ef1234567890');
  assert.equal(shortRef, 'A1B2C3D4', 'Short reference must be first 8 hex characters uppercase');
});

test('TC-QR-07: Idempotency & Invariance on Retry', () => {
  // First attempt
  const firstAttempt = generateVietQrPayload(VALID_TEST_BANK_VIETINBANK, VALID_PAYLOAD_STANDARD);

  // Simulated retry with same requestKey and same payload
  const retryAttempt = generateVietQrPayload(VALID_TEST_BANK_VIETINBANK, VALID_PAYLOAD_STANDARD);

  assert.equal(firstAttempt.transactionReference, retryAttempt.transactionReference, 'Transaction ref must be invariant');
  assert.equal(firstAttempt.transferContent, retryAttempt.transferContent, 'Transfer content must be invariant');
  assert.equal(firstAttempt.rawPayload, retryAttempt.rawPayload, 'Raw EMVCo payload must be 100% identical');
  assert.equal(assertIdempotentPayload(firstAttempt, retryAttempt), true, 'assertIdempotentPayload returns true');

  // Simulated mismatched retry (different requestKey)
  const differentAttempt = generateVietQrPayload(VALID_TEST_BANK_VIETINBANK, VALID_PAYLOAD_MULTI_ITEMS);
  assert.equal(assertIdempotentPayload(firstAttempt, differentAttempt), false, 'assertIdempotentPayload returns false for different payload');
});

test('TC-QR-08: T04-AC-03 Security Guard: Never Infer Payment Confirmation from Display, Scan or Timeout', () => {
  const result = generateVietQrPayload(VALID_TEST_BANK_MBBANK, VALID_PAYLOAD_STANDARD);

  // Status after generation must be AWAITING_CASHIER_CONFIRMATION
  assert.equal(result.status, 'AWAITING_CASHIER_CONFIRMATION');

  // Guard must return false
  const scanConfirmed = isPaymentConfirmedByScanOrTimeout();
  assert.equal(scanConfirmed, false, 'isPaymentConfirmedByScanOrTimeout must strictly return false');

  // Verify that countdown expiring cannot flip status to CONFIRMED
  let status = result.status;
  const simulatedCountdownSeconds = 0; // Countdown reaches zero
  if (simulatedCountdownSeconds === 0) {
    // Correct behavior: do NOT confirm
    if (isPaymentConfirmedByScanOrTimeout()) {
      status = 'CONFIRMED';
    }
  }
  assert.notEqual(status, 'CONFIRMED', 'Status must not become CONFIRMED due to countdown expiration');
  assert.equal(status, 'AWAITING_CASHIER_CONFIRMATION');
});

test('TC-QR-09: Safe Exception Handling on Invalid Invocation', () => {
  // Should throw informative Error when invoked with invalid bank config
  assert.throws(
    () => generateVietQrPayload(INVALID_TEST_BANK_SHORT_BIN, VALID_PAYLOAD_STANDARD),
    (err) => {
      assert.ok(err instanceof Error);
      assert.ok(err.message.includes('Cấu hình ngân hàng không hợp lệ'));
      return true;
    }
  );

  // Should throw informative Error when invoked with invalid payload
  assert.throws(
    () => generateVietQrPayload(VALID_TEST_BANK_VIETINBANK, INVALID_PAYLOAD_ZERO_AMOUNT),
    (err) => {
      assert.ok(err instanceof Error);
      assert.ok(err.message.includes('Dữ liệu thanh toán QR không hợp lệ'));
      return true;
    }
  );
});
