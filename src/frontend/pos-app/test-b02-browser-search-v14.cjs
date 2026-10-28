const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const assert = require('node:assert/strict');
const { chromium } = require('C:/Users/Jacki Khoa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');

const dist = path.resolve(__dirname, 'dist');
const evidenceDir = path.resolve(__dirname, '../../../docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-02-stock-realtime-pwa/evidence/v1.4');
if (!fs.existsSync(evidenceDir)) {
  fs.mkdirSync(evidenceDir, { recursive: true });
}

const branch = '11111111-1111-4111-8111-111111111111';
const variant = '33333333-3333-4333-8333-333333333333';
const user = { id: 'user-review', tenantId: 'tenant-review', email: 'review@fixture.local', fullName: 'Reviewer fixture', tenantName: 'Fixture shop' };

const server = http.createServer((req, res) => {
  let file = path.resolve(dist, '.' + new URL(req.url, 'http://localhost').pathname);
  if (!file.startsWith(dist + path.sep) && file !== dist) {
    res.writeHead(403).end();
    return;
  }
  if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) file = path.join(dist, 'index.html');
  const mime = {
    '.html': 'text/html',
    '.js': 'text/javascript',
    '.css': 'text/css',
    '.svg': 'image/svg+xml',
    '.webmanifest': 'application/manifest+json',
  };
  res.setHeader('Content-Type', mime[path.extname(file)] || 'application/octet-stream');
  res.end(fs.readFileSync(file));
});

(async () => {
  await new Promise((r) => server.listen(0, '127.0.0.1', r));
  const base = 'http://127.0.0.1:' + server.address().port;
  const context = await chromium.launchPersistentContext(path.join(__dirname, 'edge-profile-v14'), {
    executablePath: 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    headless: true,
    viewport: { width: 1440, height: 1000 },
  });

  try {
    await context.addInitScript((u) => {
      localStorage.setItem('pos_user', JSON.stringify(u));
      localStorage.setItem('pos_access_token', 'fixture-only-token');
    }, user);
    await context.route('**/hubs/**', (r) => r.abort());

    let available = 10;
    const requests = [];
    const item = () => ({
      variantId: variant,
      productId: 'product-review',
      sku: 'SKU-A',
      productName: 'Product A',
      variantName: '',
      barcode: 'BAR-A',
      retailPrice: 100,
      available,
      onHand: available,
      reserved: 0,
    });

    await context.route('**/api/**', async (route) => {
      const url = new URL(route.request().url());
      requests.push({ method: route.request().method(), path: url.pathname, availableAtRequest: available });
      let data;
      let status = 200;
      if (url.pathname === '/api/branches') data = [{ id: branch, name: 'Branch A', isActive: true }];
      else if (url.pathname === '/api/pos/search') data = [item()];
      else if (url.pathname.startsWith('/api/pos/barcode/')) data = item();
      else if (url.pathname === '/api/pos/checkout') {
        available = 8;
        status = 201;
        data = {
          success: true,
          orderNumber: 'SERVER-CHECKOUT-1',
          receipt: {
            orderId: '55555555-5555-4555-8555-555555555555',
            receiptNumber: 'SERVER-CHECKOUT-1',
            orderNumber: 'SERVER-CHECKOUT-1',
            branchId: branch,
            branchName: 'Branch A',
            cashierName: user.fullName,
            totalAmount: 200,
            paymentMethod: 'Cash',
            amountGiven: 200,
            changeDue: 0,
            createdAt: '2026-10-28T00:00:00Z',
            items: [{ skuId: variant, skuCode: 'SKU-A', productName: 'Product A', quantity: 2, unitPrice: 100, subtotal: 200 }],
          },
        };
      } else {
        status = 404;
        data = { detail: 'Fixture route not defined' };
      }
      await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(data) });
    });

    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', (e) => errors.push(e.message));
    await page.goto(base, { waitUntil: 'networkidle' });

    // Step 1: Search SKU-A (server available = 10), add to cart
    const search = page.getByPlaceholder(/Tìm tên/);
    await search.fill('SKU-A');
    await page.getByText('SKU: SKU-A', { exact: true }).click();

    // Step 2: Server stock updates to 8 (e.g. from background checkout or other channel)
    available = 8;

    // Step 3: Fresh search for SKU-A returns available = 8
    const refreshed = page.waitForResponse((r) => r.url().includes('/api/pos/search'));
    await search.fill('SKU-A');
    await refreshed;

    // Check that dropdown shows "Khả dụng: 8"
    await page.waitForSelector('text=Khả dụng: 8');

    // Click item from dropdown
    await page.getByText('SKU: SKU-A', { exact: true }).click();

    // Step 4: Verify cart input max is updated to 8!
    await page.locator('input[type=number]').waitFor();
    const max = await page.locator('input[type=number]').first().getAttribute('max');

    console.log(`[BROWSER SEARCH VERIFICATION]:`);
    console.log(`  Server stock changed to: ${available}`);
    console.log(`  Cart input max after fresh search & selection: ${max}`);

    assert.equal(available, 8, 'Server stock is 8');
    assert.equal(max, '8', 'Cart input max must be updated to 8 (previously was stuck at 10 on baseline)!');
    assert.ok(requests.some((r) => r.path === '/api/pos/search' && r.availableAtRequest === 8), 'Search request with stock 8 was made');

    const screenshotPath = path.join(evidenceDir, 'browser-search-fixed-8.png');
    await page.screenshot({ path: screenshotPath, fullPage: true });
    console.log(`  Screenshot saved to: ${screenshotPath}`);

    const result = {
      timestamp: new Date().toISOString(),
      provider: 'Real production dist in installed Edge; fixture REST APIs, Hub offline. Server changes after initial search.',
      serverAvailable: available,
      cartInputMaxAfterFreshSearch: max,
      requests,
      pageErrors: errors,
      status: 'PASS',
      defectFixed: 'B02-F04 probe 2: Search with stock 8 now successfully updates cache and cart from 10 to 8',
    };
    const jsonPath = path.join(evidenceDir, 'browser-search-results.json');
    fs.writeFileSync(jsonPath, JSON.stringify(result, null, 2));
    console.log(`  Results written to: ${jsonPath}`);
    console.log('=== BROWSER SEARCH v1.4 TEST PASSED 100% ===');
  } finally {
    await context.close();
    await new Promise((r) => server.close(r));
  }
})().catch((e) => {
  console.error(e);
  server.close();
  process.exitCode = 1;
});
