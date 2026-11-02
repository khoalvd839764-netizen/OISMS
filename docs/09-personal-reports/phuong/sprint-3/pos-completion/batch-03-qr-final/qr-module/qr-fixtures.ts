/**
 * DỮ LIỆU KIỂM THỬ (TEST FIXTURES ONLY) — TUYỆT ĐỐI KHÔNG CHUYỂN TIỀN THẬT
 *
 * File này chứa các tập dữ liệu giả lập phục vụ kiểm thử tự động module QR (S3-POS-B03).
 * Toàn bộ thông tin tài khoản, ngân hàng và giao dịch dưới đây là dữ liệu giả định (Mock/Fixture),
 * không đại diện cho tài khoản thật của bất kỳ cá nhân hay tổ chức nào.
 */

import type { BankConfig, QrPaymentPayload } from './qr-types.js';

// ============================================================================
// 1. CẤU HÌNH NGÂN HÀNG HỢP LỆ (CHO KIỂM THỬ)
// ============================================================================

export const VALID_TEST_BANK_VIETINBANK: BankConfig = {
  bankBin: '970415', // VietinBank
  bankName: 'VietinBank Test',
  accountNumber: '101088889999', // 12 ký tự (< 19)
  accountName: 'OISMS TEST STORE VIETIN',
  template: 'compact2',
  isTestFixture: true,
};

export const VALID_TEST_BANK_MBBANK: BankConfig = {
  bankBin: '970422', // MBBank
  bankName: 'MBBank Test',
  accountNumber: '098765432199',
  accountName: 'OISMS TEST BRANCH Q1',
  template: 'compact2',
  isTestFixture: true,
};

export const VALID_TEST_BANK_VCB: BankConfig = {
  bankBin: '970436', // Vietcombank
  bankName: 'Vietcombank Test',
  accountNumber: '0071001234567',
  accountName: 'OISMS TEST HEADQUARTERS',
  template: 'compact2',
  isTestFixture: true,
};

// ============================================================================
// 2. CẤU HÌNH NGÂN HÀNG KHÔNG HỢP LỆ (DÙNG ĐỂ TEST BẮT LỖI)
// ============================================================================

export const INVALID_TEST_BANK_MISSING_BIN: BankConfig = {
  bankBin: '',
  bankName: 'Invalid Bank',
  accountNumber: '123456789',
  accountName: 'TEST INVALID',
  isTestFixture: true,
};

export const INVALID_TEST_BANK_SHORT_BIN: BankConfig = {
  bankBin: '9704', // Chỉ 4 số (thiếu 2 số)
  bankName: 'Short BIN Bank',
  accountNumber: '123456789',
  accountName: 'TEST INVALID',
  isTestFixture: true,
};

export const INVALID_TEST_BANK_NON_NUMERIC_BIN: BankConfig = {
  bankBin: '9704AB', // Chứa chữ cái
  bankName: 'Non numeric BIN Bank',
  accountNumber: '123456789',
  accountName: 'TEST INVALID',
  isTestFixture: true,
};

export const INVALID_TEST_BANK_EMPTY_ACCOUNT: BankConfig = {
  bankBin: '970415',
  bankName: 'VietinBank Test',
  accountNumber: '   ',
  accountName: 'TEST EMPTY ACCOUNT',
  isTestFixture: true,
};

export const INVALID_TEST_BANK_LONG_ACCOUNT: BankConfig = {
  bankBin: '970415',
  bankName: 'VietinBank Test',
  accountNumber: '12345678901234567890', // 20 ký tự (vượt trần 19 ký tự theo QuickLink/NAPAS)
  accountName: 'TEST LONG ACCOUNT',
  isTestFixture: true,
};

export const INVALID_TEST_BANK_EMPTY_NAME: BankConfig = {
  bankBin: '970415',
  bankName: 'VietinBank Test',
  accountNumber: '101088889999',
  accountName: '',
  isTestFixture: true,
};

// ============================================================================
// 3. PAYLOAD THANH TOÁN HỢP LỆ (DỮ LIỆU KIỂM THỬ)
// ============================================================================

export const VALID_PAYLOAD_STANDARD: QrPaymentPayload = {
  amount: 450000, // Khớp với DOC-CTR-S3-01 PosCheckoutRequestDto
  requestKey: 'a1b2c3d4-e5f6-4789-abcd-ef1234567890',
  branchId: '11111111-1111-1111-1111-111111111111',
  orderDescription: 'OISMS POS A1B2C3D4',
  currency: 'VND',
};

export const VALID_PAYLOAD_MULTI_ITEMS: QrPaymentPayload = {
  amount: 1648000, // Khớp với PosCheckoutIntegrationTests (2 SKU)
  requestKey: 'b2c3d4e5-f6a7-4890-8cde-fa2345678901',
  branchId: '11111111-1111-1111-1111-111111111111',
  orderDescription: 'OISMS POS B2C3D4E5',
  currency: 'VND',
};

export const VALID_PAYLOAD_DECIMAL_AMOUNT: QrPaymentPayload = {
  amount: 99999.5, // Có số lẻ 1 chữ số thập phân hợp lệ
  requestKey: 'c3d4e5f6-a7b8-4901-8def-ab3456789012',
  branchId: '11111111-1111-1111-1111-111111111111',
  orderDescription: 'OISMS POS C3D4E5F6',
  currency: 'VND',
};

export const VALID_PAYLOAD_BOUNDARY_MAX: QrPaymentPayload = {
  amount: 9_999_999_999_999, // Giá trị trần chuẩn 13 chữ số QuickLink/NAPAS
  requestKey: 'd4e5f6a7-b8c9-4012-aefa-bc4567890123',
  branchId: '11111111-1111-1111-1111-111111111111',
  orderDescription: 'OISMS POS D4E5F6A7',
  currency: 'VND',
};

// ============================================================================
// 4. PAYLOAD THANH TOÁN KHÔNG HỢP LỆ (DÙNG ĐỂ TEST BẮT LỖI)
// ============================================================================

export const INVALID_PAYLOAD_ZERO_AMOUNT: QrPaymentPayload = {
  amount: 0,
  requestKey: 'e5f6a7b8-c9d0-4123-8fab-cd5678901234',
  currency: 'VND',
};

export const INVALID_PAYLOAD_NEGATIVE_AMOUNT: QrPaymentPayload = {
  amount: -50000,
  requestKey: 'f6a7b8c9-d0e1-4234-8abc-de6789012345',
  currency: 'VND',
};

export const INVALID_PAYLOAD_OVERFLOW_AMOUNT: QrPaymentPayload = {
  amount: 10_000_000_000_000, // 14 chữ số, vượt trần 9,999,999,999,999
  requestKey: 'a7b8c9d0-e1f2-4345-8bcd-ef7890123456',
  currency: 'VND',
};

export const INVALID_PAYLOAD_EXCESSIVE_DECIMALS: QrPaymentPayload = {
  amount: 12345.678, // 3 số thập phân (vi phạm giới hạn tiền tệ scale <= 2)
  requestKey: 'b8c9d0e1-f2a3-4456-8cde-fa8901234567',
  currency: 'VND',
};

export const INVALID_PAYLOAD_LONG_DESCRIPTION: QrPaymentPayload = {
  amount: 100000,
  requestKey: 'c8d9e0f1-a2b3-4567-8def-123456789012',
  orderDescription: 'A'.repeat(51), // 51 ký tự (vượt trần 50 ký tự QuickLink)
  currency: 'VND',
};

export const INVALID_PAYLOAD_SPECIAL_CHAR_DESCRIPTION: QrPaymentPayload = {
  amount: 100000,
  requestKey: 'd9e0f1a2-b3c4-4678-8efa-234567890123',
  orderDescription: 'OISMS @#$ 123', // Ký tự đặc biệt không hợp lệ
  currency: 'VND',
};

export const INVALID_PAYLOAD_EMPTY_TRIM_DESCRIPTION: QrPaymentPayload = {
  amount: 100000,
  requestKey: 'e0f1a2b3-c4d5-4789-8fab-345678901234',
  orderDescription: '     ', // Khoảng trắng rỗng sau trim
  currency: 'VND',
};

export const INVALID_PAYLOAD_MISSING_REQUEST_KEY: QrPaymentPayload = {
  amount: 100000,
  requestKey: '',
  currency: 'VND',
};

export const INVALID_PAYLOAD_MALFORMED_REQUEST_KEY: QrPaymentPayload = {
  amount: 100000,
  requestKey: 'not-a-valid-uuid-key',
  currency: 'VND',
};
