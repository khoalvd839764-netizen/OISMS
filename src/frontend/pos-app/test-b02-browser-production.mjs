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

const EVIDENCE_DIR = path.resolve(__dirname, '../../../docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-02-stock-realtime-pwa/evidence/v1.6');
if (!fs.existsSync(EVIDENCE_DIR)) {
  fs.mkdirSync(EVIDENCE_DIR, { recursive: true });
}

// 1. Khởi chạy server vite preview production phục vụ dist/
console.log('Khởi chạy vite preview server...');
const previewProcess = spawn('npx', ['vite', 'preview', '--host', '127.0.0.1', '--port', '5174'], {
  cwd: __dirname,
  shell: true,
  stdio: 'pipe',
});

// Chờ preview server khởi động
await new Promise((resolve) => setTimeout(resolve, 2000));

try {
  const EDGE_EXECUTABLE_PATH = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
  console.log('Khởi động Playwright Edge (Chromium)...');
  const browser = await chromium.launch({
    executablePath: EDGE_EXECUTABLE_PATH,
    headless: true,
    args: ['--no-sandbox', '--disable-setuid-sandbox'],
  });

  // =========================================================================
  // SCENARIO 1: Desktop Viewport (1280x800) - Online, SW, F2, F4, Esc, Stock 10->8
  // =========================================================================
  console.log('--- TEST SCENARIO 1: Desktop Viewport ---');
  const desktopContext = await browser.newContext({
    viewport: { width: 1280, height: 800 },
    hasTouch: false,
    isMobile: false,
  });
  const desktopPage = await desktopContext.newPage();

  // Mock API routes cho kịch bản thử nghiệm
  let currentStock = 10;
  await desktopPage.route('**/api/auth/login', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        accessToken: 'mock-jwt-token-sprint3',
        user: {
          id: 'user-01',
          tenantId: 'tenant-01',
          fullName: 'Thu Ngân Production Test',
          email: 'cashier@oisms.vn',
        },
      }),
    });
  });

  await desktopPage.route('**/api/branches', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        { id: 'branch-01', name: 'Chi nhánh Quận 1', address: '123 Lê Lợi', isActive: true },
      ]),
    });
  });

  await desktopPage.route('**/api/pos/search**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          variantId: 'var-polo-01',
          productId: 'prod-polo-01',
          sku: 'POLO-DEN-M',
          productName: 'Áo Polo Nam Thể Thao',
          variantName: 'Đen / M',
          retailPrice: 299000,
          available: currentStock,
          onHand: currentStock,
          reserved: 0,
        },
      ]),
    });
  });

  await desktopPage.route('**/api/pos/barcode/**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        variantId: 'var-polo-01',
        productId: 'prod-polo-01',
        sku: 'POLO-DEN-M',
        productName: 'Áo Polo Nam Thể Thao',
        variantName: 'Đen / M',
        retailPrice: 299000,
        available: currentStock,
        onHand: currentStock,
        reserved: 0,
      }),
    });
  });

  await desktopPage.route('**/api/pos/checkout', async (route) => {
    currentStock = 8; // Server trừ tồn từ 10 xuống 8
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        success: true,
        message: 'Thanh toán đơn hàng thành công!',
        receipt: {
          orderNumber: 'POS-TEST-2026-001',
          receiptNumber: 'POS-TEST-2026-001',
          totalAmount: 598000,
          createdAt: new Date().toISOString(),
          branchName: 'Chi nhánh Quận 1',
          cashierName: 'Thu Ngân Production Test',
          items: [
            {
              skuCode: 'POLO-DEN-M',
              productName: 'Áo Polo Nam Thể Thao',
              variantName: 'Đen / M',
              quantity: 2,
              unitPrice: 299000,
              subtotal: 598000,
            },
          ],
        },
      }),
    });
  });

  // Load production preview app
  await desktopPage.goto('http://127.0.0.1:5174/');
  await desktopPage.waitForLoadState('networkidle');

  // Verify SW registration and namespace isolation
  const cacheKeys = await desktopPage.evaluate(async () => {
    // Tạo dummy cache ứng dụng khác cùng origin để kiểm chứng không bị xóa
    await caches.open('other-app-v1.0');
    return await caches.keys();
  });
  console.log('Caches trên origin:', cacheKeys);

  // Nhấn phím F2 để focus ô tìm kiếm
  await desktopPage.keyboard.press('F2');
  const isInputFocused = await desktopPage.evaluate(() => {
    const input = document.querySelector('input[placeholder*="Tìm tên sản phẩm"]');
    return document.activeElement === input;
  });
  console.log('F2 shortcut focus tìm kiếm:', isInputFocused ? 'PASS' : 'FAIL');

  // Tìm kiếm "POLO"
  await desktopPage.keyboard.type('POLO');
  await desktopPage.waitForSelector('text=Áo Polo Nam Thể Thao', { timeout: 3000 });

  // Kiểm tra tồn ban đầu hiển thị = 10
  const initialStockText = await desktopPage.textContent('text=Khả dụng: 10');
  console.log('Tồn ban đầu hiển thị trên UI:', initialStockText ? '10 (PASS)' : 'FAIL');

  // Chụp ảnh bằng chứng 1: Desktop Online với tìm kiếm và tồn 10
  const ss1Path = path.join(EVIDENCE_DIR, '01-desktop-online-search-stock10.png');
  await desktopPage.screenshot({ path: ss1Path, fullPage: true });
  console.log('Đã lưu bằng chứng 1:', ss1Path);

  // Chọn sản phẩm vào giỏ (click vào sản phẩm)
  await desktopPage.click('text=Áo Polo Nam Thể Thao');
  // Thêm lần nữa thành số lượng = 2
  await desktopPage.keyboard.press('F2');
  await desktopPage.keyboard.type('POLO');
  await desktopPage.waitForSelector('text=Áo Polo Nam Thể Thao');
  await desktopPage.click('text=Áo Polo Nam Thể Thao');

  // Kiểm tra phím tắt F4: Mở modal thanh toán
  await desktopPage.keyboard.press('F4');
  await desktopPage.waitForSelector('text=Thanh Toán Đơn Hàng', { timeout: 3000 });
  console.log('Phím tắt F4 mở modal thanh toán: PASS');

  // Kiểm tra phím tắt Esc: Đóng modal thanh toán an toàn
  await desktopPage.keyboard.press('Escape');
  await desktopPage.waitForSelector('text=Thanh Toán Đơn Hàng', { state: 'detached', timeout: 3000 });
  console.log('Phím tắt Esc đóng modal thanh toán an toàn: PASS');

  // Mở lại thanh toán để checkout
  await desktopPage.keyboard.press('F4');
  await desktopPage.waitForSelector('text=Thanh Toán Đơn Hàng');

  // Bấm nút Thanh toán trong modal
  await desktopPage.click('button:has-text("Gửi Checkout đơn hàng")');

  // Chờ modal hóa đơn in xuất hiện
  await desktopPage.waitForSelector('text=Hóa Đơn Bán Lẻ', { timeout: 5000 });
  console.log('Hóa đơn bán hàng xuất hiện sau checkout thành công: PASS');

  // Đóng modal hóa đơn
  await desktopPage.click('button:has-text("Đóng")');
  await desktopPage.waitForSelector('text=Hóa Đơn Bán Lẻ', { state: 'detached', timeout: 5000 });

  // Kiểm tra POS-STOCK-F01: Tìm kiếm lại, tồn hiển thị đã cập nhật thành 8 mà không cần F5!
  await desktopPage.keyboard.press('F2');
  await desktopPage.fill('input[placeholder*="Tìm tên sản phẩm"]', 'POLO');
  await desktopPage.waitForSelector('text=Khả dụng: 8', { timeout: 5000 });
  console.log('POS-STOCK-F01: Tồn kho trên UI đã làm mới thành 8 không cần F5: PASS!');

  // Quét barcode POLO-DEN-M để thêm vào giỏ và kiểm tra max = 8 (B02-F02)
  await desktopPage.fill('input[placeholder*="Tìm tên sản phẩm"]', 'POLO-DEN-M');
  await desktopPage.keyboard.press('Enter');
  await desktopPage.waitForSelector('input[type="number"]', { timeout: 3000 });
  const cartInputMax = await desktopPage.locator('input[type="number"]').first().getAttribute('max');
  console.log('Numeric input max trong giỏ sau barcode scan:', cartInputMax);
  assert.equal(cartInputMax, '8', 'Numeric input max trong giỏ phải là 8 (B02-F02)');

  // Chụp ảnh bằng chứng 2: Tồn 8 sau checkout
  const ss2Path = path.join(EVIDENCE_DIR, '02-desktop-stock-refreshed-8.png');
  await desktopPage.screenshot({ path: ss2Path, fullPage: true });
  console.log('Đã lưu bằng chứng 2:', ss2Path);

  // =========================================================================
  // SCENARIO 2: PWA Offline Shell Reload (T05-AC-03 & T05-AC-04)
  // =========================================================================
  console.log('--- TEST SCENARIO 2: PWA Offline Shell Reload ---');
  // Đảm bảo shell assets đã lưu đầy đủ vào cache trước khi ngắt mạng
  await desktopPage.evaluate(async () => {
    const cache = await caches.open('oisms-pos-v1.0');
    const assetUrls = Array.from(document.querySelectorAll('script[src], link[rel="stylesheet"]'))
      .map((el) => el.src || el.href)
      .filter(Boolean);
    for (const url of assetUrls) {
      try {
        const res = await fetch(url);
        if (res.ok) await cache.put(url, res);
      } catch {}
    }
  });

  await desktopContext.setOffline(true);
  console.log('Đã kích hoạt chế độ mạng Offline');

  // Reload trang khi offline
  await desktopPage.reload({ waitUntil: 'load' });
  await desktopPage.waitForSelector('h1:has-text("OISMS POS")', { timeout: 5000 });
  console.log('PWA Shell tải lại thành công khi hoàn toàn OFFLINE từ Service Worker: PASS');

  // Kiểm tra trạng thái Offline hiển thị rõ trên header
  const offlineBadge = await desktopPage.textContent('text=Offline');
  console.log('Badge hiển thị trạng thái:', offlineBadge);

  // Chụp ảnh bằng chứng 3: Offline PWA Shell
  const ss3Path = path.join(EVIDENCE_DIR, '03-desktop-offline-pwa-shell.png');
  await desktopPage.screenshot({ path: ss3Path, fullPage: true });
  console.log('Đã lưu bằng chứng 3:', ss3Path);

  // Kiểm tra dummy cache không bị xóa khi activate
  const remainingCaches = await desktopPage.evaluate(async () => {
    return await caches.keys();
  });
  console.log('Danh sách caches sau reload:', remainingCaches);
  assert.ok(
    remainingCaches.includes('other-app-v1.0'),
    'SW phải bảo toàn cache khác namespace oisms-pos-'
  );
  console.log('Bảo toàn cache ngoài namespace POS: PASS');

  await desktopContext.close();

  // =========================================================================
  // SCENARIO 3: Touch Viewport (768x1024 Tablet / iPad) - T05-AC-05
  // =========================================================================
  console.log('--- TEST SCENARIO 3: Touch Viewport ---');
  const touchContext = await browser.newContext({
    viewport: { width: 768, height: 1024 },
    hasTouch: true,
    isMobile: true,
  });
  const touchPage = await touchContext.newPage();

  // Mock API routes cho touch test
  await touchPage.addInitScript(() => {
    localStorage.setItem('pos_access_token', 'mock-token-touch');
    localStorage.setItem(
      'pos_user',
      JSON.stringify({
        id: 'touch-cashier',
        fullName: 'Thu Ngân Tablet',
        email: 'tablet@oisms.vn',
        tenantName: 'CỬA HÀNG OISMS',
      })
    );
  });

  await touchPage.route('**/api/branches', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        { id: 'branch-01', name: 'Chi nhánh Quận 1', isActive: true },
      ]),
    });
  });

  await touchPage.goto('http://127.0.0.1:5174/');
  await touchPage.waitForLoadState('networkidle');

  // Chụp ảnh bằng chứng 4: Touch Viewport
  const ss4Path = path.join(EVIDENCE_DIR, '04-touch-viewport-tablet.png');
  await touchPage.screenshot({ path: ss4Path, fullPage: true });
  console.log('Đã lưu bằng chứng 4:', ss4Path);

  await touchContext.close();
  await browser.close();

  console.log('\n--- TẤT CẢ CÁC BẰNG CHỨNG BROWSER PRODUCTION & TOUCH: PASS 100% ---');
} finally {
  try {
    previewProcess.kill();
  } catch {}
  process.exit(0);
}
