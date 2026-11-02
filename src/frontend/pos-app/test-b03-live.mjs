import fs from 'node:fs';
import path from 'node:path';
import http from 'node:http';
import { randomUUID } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { fileURLToPath, pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';
import { verifyIsolatedBackend, sqlUuid } from './test-support/isolated-backend.mjs';
import { startLiveProxy } from './test-support/live-proxy.mjs';

const app = fileURLToPath(new URL('./', import.meta.url));
const root = path.resolve(app, '../../..');
const evidence = path.join(root, 'docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-03-qr-final/evidence/v1.3-integration');
const pendingRecords = () => { const raw = JSON.parse(sessionStorage.getItem('pos_pending_checkout_v1') || 'null'); return Array.isArray(raw) ? raw : raw?.records || (raw ? [raw] : []); };
const bank = { bankBin: '970415', bankName: 'Fixture only', accountNumber: '123456789', accountName: 'TEST A - DO NOT PAY', isTestFixture: true };
const bankB = { ...bank, accountNumber: '987654321', accountName: 'TEST B - DO NOT PAY' };

export async function runB03Live() {
  fs.mkdirSync(evidence, { recursive: true });
  const proof = await verifyIsolatedBackend();
  const report = { provider: 'Real production React in installed Edge, real HTTP backend + PostgreSQL. Loopback proxy injects response loss, invalid auth and Hub outage; only external QR image is substituted. No bank transfer.', head: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim(), timestamp: new Date().toISOString(), environment: { ...proof, query: undefined }, cases: [], pageErrors: [], status: 'IMPLEMENTING' };
  const record = (name, result) => { report.cases.push({ name, status: 'PASS', ...result }); console.log('PASS ' + name + ' ' + JSON.stringify(result)); };
  let ownerToken;
  async function api(route, body, token = ownerToken) {
    const response = await fetch(proof.backendUrl + route, { method: body === undefined ? 'GET' : 'POST', headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) }, ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
    const data = await response.json(); assert.ok(response.ok, `${route}: ${response.status} ${JSON.stringify(data)}`); return data;
  }
  const password = process.env.POS_TEST_PASSWORD || 'YourStrongDemoPassword123!';
  const owner = await api('/api/auth/login', { email: 'owner@shopa.local', password }); ownerToken = owner.accessToken;
  const branches = await api('/api/branches'), warehouses = await api('/api/warehouses');
  const branch = branches[0], warehouse = warehouses.find(w => w.branchId === branch.id); assert.ok(branch && warehouse);
  const nonce = 'B03-' + randomUUID().slice(0, 8).toUpperCase();
  const created = await api('/api/products', { name: nonce, variants: ['A', 'B'].map(suffix => ({ sku: nonce + '-' + suffix, variantName: suffix, barcode: nonce + '-BAR-' + suffix, retailPrice: 100, wholesalePrice: 80 })) });
  const product = await api(`/api/products/${created.productId}`);
  const [skuA, skuB] = product.variants; assert.ok(skuA?.id && skuB?.id);
  const suppliers = await api('/api/suppliers');
  const supplier = suppliers.find(s => s.isActive) || await api('/api/suppliers', { name: nonce });
  async function stockIn(branchId, warehouseId, skus, quantity) { const receipt = await api('/api/goods-receipts', { branchId, warehouseId, supplierId: supplier.id, note: nonce, items: skus.map(s => ({ skuId: s.id, quantity, unitPrice: 40 })) }); await api(`/api/goods-receipts/${receipt.id}/confirm`, {}); }
  await stockIn(branch.id, warehouse.id, [skuA, skuB], 10);
  let otherBranch = branches[1], otherWarehouse;
  if (!otherBranch) otherBranch = await api('/api/branches', { name: nonce + ' Branch', address: 'Isolated test fixture' });
  otherWarehouse = warehouses.find(w => w.branchId === otherBranch.id);
  if (!otherWarehouse) otherWarehouse = await api('/api/warehouses', { name: nonce + ' Warehouse', branchId: otherBranch.id });
  await stockIn(otherBranch.id, otherWarehouse.id, [skuA], 10);
  report.fixture = { tenantId: owner.user.tenantId, branchId: branch.id, otherBranchId: otherBranch.id, productId: product.id, skuIds: [skuA.id, skuB.id], initialAvailable: 10 };

  const proxy = await startLiveProxy(proof.backendUrl);
  const output = path.join(root, 'tmp/pos-b03-final/dist');
  execFileSync(process.execPath, [path.join(app, 'node_modules/vite/bin/vite.js'), 'build', '--outDir', output], { cwd: app, env: { ...process.env, VITE_API_BASE_URL: proxy.url }, encoding: 'utf8', windowsHide: true });
  const server = http.createServer((req, res) => { let p = path.resolve(output, '.' + new URL(req.url, 'http://fixture.local').pathname); if (!p.startsWith(output + path.sep) && p !== output) { res.writeHead(403).end(); return; } if (!fs.existsSync(p) || fs.statSync(p).isDirectory()) p = path.join(output, 'index.html'); res.setHeader('Content-Type', ({ '.js': 'application/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml' })[path.extname(p)] || 'application/octet-stream'); res.end(fs.readFileSync(p)); });
  await new Promise(r => server.listen(0, '127.0.0.1', r));
  const runtimePath = process.env.PLAYWRIGHT_MODULE_PATH || 'C:/Users/Jacki Khoa/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright/index.js';
  const runtime = await import(pathToFileURL(runtimePath).href), chromium = (runtime.default || runtime).chromium;
  const browser = await chromium.launch({ executablePath: process.env.EDGE_EXECUTABLE_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 1050 } });
  await context.route('https://img.vietqr.io/**', r => r.fulfill({ status: 200, contentType: 'image/svg+xml', body: '<svg xmlns="http://www.w3.org/2000/svg" width="200" height="200"><rect width="200" height="200" fill="white"/><text x="20" y="100">TEST IMAGE ONLY</text></svg>' }));
  const page = await context.newPage(); page.on('pageerror', e => report.pageErrors.push(e.message)); page.on('dialog', d => d.accept());
  const modal = () => page.getByRole('dialog', { name: 'Thanh Toán Đơn Hàng', exact: true });
  const receipt = () => page.getByRole('dialog', { name: 'Hóa đơn', exact: true });
  const pay = () => modal().getByRole('button', { name: /Gửi Checkout|Thử lại thanh toán/ });
  const search = () => page.getByPlaceholder(/Tìm tên/);
  const cartRow = sku => page.locator('tbody tr').filter({ hasText: sku.sku });
  async function add(sku) { await search().fill(sku.barcode); await search().press('Enter'); await cartRow(sku).waitFor(); }
  async function openPay() { await page.keyboard.press('F4'); await modal().waitFor(); }
  async function closeReceipt() { await receipt().getByRole('button', { name: 'Đóng hóa đơn', exact: true }).click(); }
  async function login(email, initial = false) { if (initial && !(await page.locator('input[type=email]').isVisible())) await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click(); await page.locator('input[type=email]').fill(email); await page.locator('input[type=password]').fill(password); await page.locator('form').getByRole('button', { name: /Đăng nhập/i }).click(); await page.locator('input[type=email]').waitFor({ state: 'hidden' }); await page.locator('#branch-select option').first().waitFor({ state: 'attached' }); }
  async function configure(branchId, value) { await page.evaluate(({ tenantId, branchId, value }) => localStorage.setItem(`pos_bank_config_v1:${encodeURIComponent(tenantId)}:${encodeURIComponent(branchId)}`, JSON.stringify({ version: 1, tenantId, branchId, bankConfig: value })), { tenantId: owner.user.tenantId, branchId, value }); }
  function databaseCheckout(request, expectedRows) {
    const key = sqlUuid(request.payload.requestKey), tenant = sqlUuid(owner.user.tenantId);
    const orders = Number(proof.query(`SELECT count(*) FROM orders WHERE tenant_id=${tenant} AND request_key=${key};`));
    const payments = Number(proof.query(`SELECT count(*) FROM payments p JOIN orders o ON p.order_id=o.id WHERE o.tenant_id=${tenant} AND o.request_key=${key};`));
    const outs = Number(proof.query(`SELECT count(*) FROM inventory_transactions t JOIN order_items i ON t.order_item_id=i.id JOIN orders o ON i.order_id=o.id WHERE o.tenant_id=${tenant} AND o.request_key=${key} AND t.type='OUT';`));
    assert.equal(orders, 1); assert.equal(payments, 1); assert.equal(outs, expectedRows); return { requestKey: request.payload.requestKey, orders, payments, outs };
  }
  try {
    await context.tracing.start({ screenshots: true, snapshots: true });
    await page.goto(`http://127.0.0.1:${server.address().port}`, { waitUntil: 'networkidle' });
    await login('cashier@shopa.local', true); await page.locator('#branch-select').selectOption(branch.id);
    await page.locator('[title="SignalR Hub: Đang kết nối thời gian thực"]').waitFor();
    await configure(branch.id, bank); await configure(otherBranch.id, bankB);
    await add(skuA); await cartRow(skuA).locator('input[type=number]').fill('2'); await openPay();
    const countBefore = proxy.control.checkout.length;
    await modal().getByRole('button', { name: 'In phiếu tạm tính', exact: true }).click(); await receipt().waitFor(); assert.equal(proxy.control.checkout.length, countBefore); await closeReceipt();
    if (!(await modal().count())) await openPay();
    proxy.pauseHub(); await pay().click(); await receipt().waitFor();
    const cash = proxy.control.checkout.at(-1); const cashDb = databaseCheckout(cash, 1);
    await page.pdf({ path: path.join(evidence, 'cash-receipt.pdf'), preferCSSPageSize: true });
    await closeReceipt(); await page.getByRole('button', { name: /In lại hóa đơn vừa bán/ }).click(); await receipt().waitFor(); assert.equal(proxy.control.checkout.length, countBefore + 1); await closeReceipt();
    await add(skuA); assert.equal(await cartRow(skuA).locator('input[type=number]').getAttribute('max'), '8');
    const token = await page.evaluate(() => localStorage.getItem('pos_access_token'));
    const replay = await api('/api/pos/checkout', cash.payload, token); assert.equal(replay.orderNumber, cash.response.orderNumber);
    assert.equal(databaseCheckout(cash, 1).outs, 1); assert.equal(await cartRow(skuA).locator('input[type=number]').getAttribute('max'), '8');
    record('cash, draft, reprint, replay, stock10->8 with Hub offline', { ...cashDb, max: 8 });
    proxy.restoreHub(); await page.locator('[title="SignalR Hub: Đang kết nối thời gian thực"]').waitFor();

    await openPay(); await modal().getByRole('button', { name: 'Chuyển khoản QR', exact: true }).click();
    const firstQr = await modal().locator('img[alt="VietQR Code"]').getAttribute('src'); assert.ok(firstQr.includes(bank.accountNumber));
    await modal().getByRole('button', { name: 'Đóng thanh toán', exact: true }).click(); await openPay(); assert.equal(await modal().locator('img').getAttribute('src'), firstQr);
    await modal().getByRole('button', { name: 'Đóng thanh toán', exact: true }).click(); await page.locator('#branch-select').selectOption(otherBranch.id); await add(skuA); await openPay(); await modal().getByRole('button', { name: 'Chuyển khoản QR', exact: true }).click(); assert.ok((await modal().locator('img').getAttribute('src')).includes(bankB.accountNumber));
    await modal().getByRole('button', { name: 'Đóng thanh toán', exact: true }).click(); await page.locator('#branch-select').selectOption(branch.id);
    record('QR close/open identity and branch-scoped receiver', { branchA: bank.accountNumber, branchB: bankB.accountNumber });

    await add(skuA); await add(skuB); await openPay(); await modal().getByRole('button', { name: 'Chuyển khoản QR', exact: true }).click(); const beforeQr = await modal().locator('img').getAttribute('src');
    proxy.control.dropNextCheckout = true; await pay().click();
    await modal().getByRole('button', { name: 'Thử lại thanh toán (F4)', exact: true }).waitFor();
    const lost = proxy.control.checkout.at(-1); assert.equal(lost.dropped, true); const unknown = await page.evaluate(pendingRecords); assert.equal(unknown.length, 1); assert.equal(unknown[0].requestKey, lost.payload.requestKey); assert.equal(await page.locator('tbody tr').count(), 2); databaseCheckout(lost, 2);
    await configure(branch.id, bankB); await page.reload({ waitUntil: 'networkidle' }); await openPay(); assert.equal(await modal().locator('img').getAttribute('src'), beforeQr); await page.screenshot({ path: path.join(evidence, 'qr-unknown-frozen-snapshot.png'), fullPage: true });
    await pay().click(); await receipt().waitFor(); const retried = proxy.control.checkout.at(-1); assert.deepEqual(retried.payload, lost.payload); assert.equal(retried.status, 200); assert.equal((await page.evaluate(pendingRecords)).length, 0); const qrDb = databaseCheckout(retried, 2);
    await page.screenshot({ path: path.join(evidence, 'server-receipt-after-retry.png'), fullPage: true }); await closeReceipt();
    record('QR response lost after commit -> reload -> UI retry single transaction', { ...qrDb, identicalPayload: true, frozenReceiver: bank.accountNumber });

    await add(skuA); await openPay(); await modal().getByRole('button', { name: 'Tiền mặt', exact: true }).click(); proxy.control.rejectNextAuth = true; await pay().click(); await page.locator('input[type=email]').waitFor(); const authKey = (await page.evaluate(pendingRecords))[0].requestKey;
    await login('cashier@shopb.local'); assert.equal(await page.locator('tbody tr').count(), 0); assert.ok((await page.evaluate(pendingRecords)).some(p => p.requestKey === authKey));
    await page.getByTitle('Đăng xuất', { exact: true }).click(); await login('cashier@shopa.local', true); await openPay(); assert.equal((await page.evaluate(pendingRecords))[0].requestKey, authKey); await pay().click(); await receipt().waitFor(); const reauthDb = databaseCheckout(proxy.control.checkout.at(-1), 1); await closeReceipt();
    record('real backend401, different owner isolation, same owner recovery', reauthDb);

    await add(skuA); const initialMax = Number(await cartRow(skuA).locator('input[type=number]').getAttribute('max')); const oldHub = proxy.control.hubConnections; proxy.pauseHub();
    const reserved = await api('/api/orders', { branchId: branch.id, channel: 'Admin', requestKey: randomUUID(), items: [{ skuId: skuA.id, skuCode: skuA.sku, quantity: 1, unitPrice: 100 }] }); proxy.restoreHub();
    await page.locator('[title="SignalR Hub: Đang kết nối thời gian thực"]').waitFor(); await page.waitForFunction(({ sku, expected }) => { const row = [...document.querySelectorAll('tbody tr')].find(r => r.textContent.includes(sku)); return row?.querySelector('input[type=number]')?.getAttribute('max') === String(expected); }, { sku: skuA.sku, expected: initialMax - 1 }); assert.ok(proxy.control.hubConnections > oldHub);
    await api(`/api/orders/${reserved.id}/cancel`, { reason: 'Release isolated test fixture reservation' });
    record('real Hub loss/reconnect/rejoin recovers missed stock', { before: initialMax, after: initialMax - 1, newHubConnection: true });

    await page.reload({ waitUntil: 'networkidle' }); await add(skuB); const availableB = Number(await cartRow(skuB).locator('input[type=number]').getAttribute('max')); await cartRow(skuB).locator('input[type=number]').fill(String(availableB));
    await api('/api/pos/checkout', { branchId: branch.id, channel: 'POS', paymentMethod: 'CASH', requestKey: randomUUID(), amountGiven: 100 * (availableB - 1), changeDue: 0, totalAmount: 100 * (availableB - 1), items: [{ skuId: skuB.id, skuCode: skuB.sku, quantity: availableB - 1, unitPrice: 100, productName: nonce, variantName: skuB.variantName }] }, token);
    await openPay(); await modal().getByRole('button', { name: 'Tiền mặt', exact: true }).click();
    const rejection = page.waitForResponse(r => r.url().endsWith('/api/pos/checkout') && r.request().method() === 'POST');
    await pay().click(); await rejection; const rejected = proxy.control.checkout.at(-1); assert.ok(rejected.status === 400 || rejected.status === 409); assert.equal(Number(proof.query(`SELECT count(*) FROM orders WHERE request_key=${sqlUuid(rejected.payload.requestKey)};`)), 0); assert.equal(await page.locator('tbody tr').count(), 1);
    record('insufficient stock authoritative rejection preserves cart', { status: rejected.status, orders: 0 });

    // Real GET failure must remain an error; a new successful lookup recovers.
    await page.reload({ waitUntil: 'networkidle' });
    proxy.control.failStock = true;
    await search().fill(skuA.barcode); await search().press('Enter');
    await page.getByText(/HTTP 503/).first().waitFor();
    assert.ok(proxy.control.stockFailures > 0); assert.equal(await page.locator('tbody tr').count(), 0);
    proxy.control.failStock = false; await add(skuA);
    const actual = await api(`/api/pos/barcode/${skuA.barcode}?branchId=${branch.id}`);
    assert.equal(Number(await cartRow(skuA).locator('input[type=number]').getAttribute('max')), actual.available);
    record('stock GET503 visible error, successful lookup recovery', { stockFailures: proxy.control.stockFailures, recoveredAvailable: actual.available });

    // Hold the actual branch A response, switch to B, then deliver A late.
    await page.reload({ waitUntil: 'networkidle' });
    proxy.control.holdNextStock = true;
    await search().fill(skuA.barcode); await search().press('Enter');
    await new Promise((resolve, reject) => { const until = Date.now() + 10000; const poll = () => proxy.control.stockHeld ? resolve() : Date.now() > until ? reject(Error('Actual stock response was not held')) : setTimeout(poll, 20); poll(); });
    await page.locator('#branch-select').selectOption(otherBranch.id);
    const released = page.waitForResponse(r => r.url().includes('/api/pos/barcode/') && r.url().includes(branch.id));
    proxy.releaseStock(); await released;
    await page.evaluate(() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))));
    assert.equal(await page.locator('tbody tr').count(), 0);
    await add(skuA); assert.equal(await cartRow(skuA).locator('input[type=number]').getAttribute('max'), '10');
    record('late branch A response ignored after switching B', { branchBAvailable: 10, staleCartRows: 0 });
    assert.equal(report.pageErrors.length, 0); report.status = 'PASS';
  } catch (error) { report.status = 'FAIL'; report.error = error.stack; await page.screenshot({ path: path.join(evidence, 'failure.png'), fullPage: true }).catch(() => {}); throw error; }
  finally {
    report.executed = report.cases.length;
    report.checkoutTrace = proxy.control.checkout;
    fs.writeFileSync(path.join(evidence, 'browser-postgres-results.json'), JSON.stringify(report, null, 2));
    await context.tracing.stop({ path: path.join(root, 'tmp/pos-b03-final/browser-trace.zip') }).catch(() => {});
    await context.close(); await browser.close(); await proxy.close(); await new Promise(r => server.close(r));
  }
  return report;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await runB03Live();
