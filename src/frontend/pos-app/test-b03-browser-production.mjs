/**
 * Browser E2E Verification for Final POS Acceptance (S3-POS-B03 / T06)
 *
 * Chạy trên Edge Chromium thực tế thông qua Playwright runtime.
 * Kiểm chứng:
 * 1. Mở Modal thanh toán (F4)
 * 2. Chuyển sang tab VietQR: hiển thị ảnh QuickLink VietQR, thông tin ngân hàng, nội dung CK, UUID, cảnh báo thu ngân
 * 3. Chụp ảnh màn hình làm bằng chứng nghiệm thu v1.2-integration
 * 4. Nhấn checkout hoàn tất và kiểm chứng modal hóa đơn thanh toán
 */

import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { spawn } from 'node:child_process';
import assert from 'node:assert';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const PLAYWRIGHT_PATH = 'C:/Users/Jacki Khoa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright/index.js';
const playwrightModule = await import(pathToFileURL(PLAYWRIGHT_PATH).href);
const playwright = playwrightModule.default || playwrightModule;
const chromium = playwright.chromium;

const EVIDENCE_DIR = path.resolve(__dirname, '../../../docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-03-qr-final/evidence/v1.2-integration');
if (!fs.existsSync(EVIDENCE_DIR)) {
  fs.mkdirSync(EVIDENCE_DIR, { recursive: true });
}

console.log('================================================================');
console.log('  OISMS SPRINT 3 POS — BROWSER E2E PRODUCTION VERIFICATION');
console.log('================================================================');

// 1. Khởi chạy vite preview server phục vụ dist/ trên port 5175
console.log('Khởi chạy vite preview server (port 5175)...');
const previewProcess = spawn('npx', ['vite', 'preview', '--host', '127.0.0.1', '--port', '5175'], {
  cwd: __dirname,
  shell: true,
  stdio: 'pipe',
});

// Chờ preview server khởi động
await new Promise((resolve) => setTimeout(resolve, 2500));

try {
  const EDGE_EXECUTABLE_PATH = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
  console.log('Khởi động Playwright Edge (Chromium)...');
  const browser = await chromium.launch({
    executablePath: EDGE_EXECUTABLE_PATH,
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox'],
  });

  const context = await browser.newContext({
    viewport: { width: 1280, height: 800 },
    hasTouch: false,
    isMobile: false,
  });

  const page = await context.newPage();

  // Mock API routes cho kịch bản browser test
  await page.route('**/api/auth/login', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        accessToken: 'mock-jwt-token-sprint3-b03',
        user: {
          id: 'user-pos-01',
          tenantId: '11111111-1111-1111-1111-111111111111',
          fullName: 'Thu Ngân Shop A',
          email: 'cashier@shopa.local',
        },
      }),
    });
  });

  await page.route('**/api/branches', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        { id: '11111111-0000-0000-0001-000000000001', name: 'Chi nhánh Quận 1', address: '123 Lê Lợi', isActive: true },
      ]),
    });
  });

  await page.route('**/api/pos/search**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          variantId: '11111111-0000-0000-0006-000000000001',
          productId: '11111111-0000-0000-0005-000000000001',
          sku: 'POLO-NAM-DEN-M',
          productName: 'Áo Polo Nam Thể Thao',
          variantName: 'Đen / Size M',
          retailPrice: 299000,
          available: 10,
          onHand: 10,
          reserved: 0,
        },
      ]),
    });
  });

  await page.route('**/api/pos/checkout', async (route) => {
    const postData = JSON.parse(route.request().postData() || '{}');
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        success: true,
        message: 'Thanh toán đơn hàng thành công!',
        receipt: {
          orderNumber: 'POS-TEST-B03-001',
          receiptNumber: 'POS-TEST-B03-001',
          totalAmount: postData.totalAmount || 598000,
          paymentMethod: postData.paymentMethod || 'BANK_TRANSFER_QR',
          createdAt: new Date().toISOString(),
          branchName: 'Chi nhánh Quận 1',
          cashierName: 'Thu Ngân Shop A',
          items: [
            {
              skuCode: 'POLO-NAM-DEN-M',
              productName: 'Áo Polo Nam Thể Thao',
              variantName: 'Đen / Size M',
              quantity: 2,
              unitPrice: 299000,
              subtotal: 598000,
            },
          ],
        },
      }),
    });
  });

  // Navigate to app
  console.log('Mở ứng dụng POS tại http://127.0.0.1:5175/ ...');
  await page.goto('http://127.0.0.1:5175/');
  await page.waitForLoadState('networkidle');

  // Search for POLO and add to cart
  console.log('Tìm kiếm sản phẩm POLO và thêm vào giỏ...');
  await page.keyboard.press('F2');
  await page.keyboard.type('POLO');
  await page.waitForSelector('text=Áo Polo Nam Thể Thao', { timeout: 4000 });
  await page.click('text=Áo Polo Nam Thể Thao');

  // Add 1 more -> total 2 units
  await page.keyboard.press('F2');
  await page.keyboard.type('POLO');
  await page.waitForSelector('text=Áo Polo Nam Thể Thao');
  await page.click('text=Áo Polo Nam Thể Thao');

  // Open PaymentModal with F4
  console.log('Mở Modal thanh toán (F4)...');
  await page.keyboard.press('F4');
  await page.waitForSelector('text=Thanh Toán Đơn Hàng', { timeout: 4000 });

  // Switch to VietQR payment method
  console.log('Chọn phương thức Chuyển khoản QR...');
  await page.click('button:has-text("Chuyển khoản QR")');

  // Verify VietQR UI elements
  console.log('Xác minh các thành phần VietQR trên giao diện...');
  await page.waitForSelector('img[alt="VietQR Code"]', { timeout: 3000 });
  await page.waitForSelector('text=Số tài khoản:', { timeout: 3000 });
  await page.waitForSelector('text=Nội dung CK:', { timeout: 3000 });
  await page.waitForSelector('text=Xác nhận thủ công của thu ngân', { timeout: 3000 });

  // Capture screenshot: PaymentModal with live VietQR
  const qrScreenshotPath = path.join(EVIDENCE_DIR, 'pos-b03-vietqr-modal.png');
  await page.screenshot({ path: qrScreenshotPath, fullPage: true });
  console.log(`-> Bằng chứng 1 đã chụp: ${qrScreenshotPath}`);

  // Complete checkout
  console.log('Thu ngân bấm Xác nhận thanh toán (F4)...');
  await page.click('button:has-text("Gửi Checkout đơn hàng (F4)")');

  // Wait for ReceiptPrintModal
  await page.waitForSelector('text=HÓA ĐƠN BÁN HÀNG', { timeout: 5000 });
  console.log('Modal Hóa đơn bán hàng chính thức đã hiển thị: PASS');

  // Capture screenshot: ReceiptPrintModal
  const receiptScreenshotPath = path.join(EVIDENCE_DIR, 'pos-b03-receipt-modal.png');
  await page.screenshot({ path: receiptScreenshotPath, fullPage: true });
  console.log(`-> Bằng chứng 2 đã chụp: ${receiptScreenshotPath}`);

  await browser.close();
  console.log('\n================================================================');
  console.log('  KẾT QUẢ BROWSER E2E: 100% PASS');
  console.log('================================================================');
  process.exit(0);

} finally {
  previewProcess.kill();
}
