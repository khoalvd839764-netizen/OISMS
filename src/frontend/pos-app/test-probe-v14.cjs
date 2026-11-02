const fs = require('fs');
const path = require('path');
const vm = require('vm');
const assert = require('assert/strict');

const root = path.resolve(__dirname);
const ts = require(path.join(root, 'node_modules/typescript'));

function loadTs(file, imports = {}) {
  const src = fs.readFileSync(path.join(root, file), 'utf8')
    .replace(/import\.meta\.env\.VITE_API_BASE_URL/g, "'http://localhost:5000'");
  const js = ts.transpileModule(src, {
    fileName: file,
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const mod = { exports: {} };
  const ctx = {
    console, Date, Math, Intl, URL, URLSearchParams, AbortController, Promise, Set, Map, performance, setTimeout, clearTimeout,
    fetch: () => Promise.reject(new Error('no fetch')),
    localStorage: { getItem: () => null, setItem() {}, removeItem() {} },
    sessionStorage: { getItem: () => null, setItem() {}, removeItem() {} },
    window: { localStorage: { getItem: () => null }, sessionStorage: {} },
    module: mod, exports: mod.exports,
    require: (n) => imports[n] || {},
  };
  vm.runInNewContext(js, ctx, { filename: file });
  return mod.exports;
}

const api = loadTs('src/services/api.ts');
const stockService = loadTs('src/services/stockService.ts', { './api': api });

async function runProbes() {
  console.log('=== VERIFYING R4 PROBES AFTER FIX (v1.4) ===');
  console.log('Timestamp:', new Date().toISOString());

  // PROBE 1: Older Barcode vs Newer Refetch
  api._resetStockState();
  const branchId = '11111111-0000-0000-0001-000000000001';
  const skuId = '99999999-9999-9999-9999-999999999999';
  const sku = 'SKU-PROBE-1';
  const barcode = '893000000001';

  // Barcode read begins at t=1 (simulating in-flight scanPosBarcode)
  const barcodeTicket = stockService.issueStockReadTicket();

  // In-flight, refetch happens at t=2 and writes available = 8
  const refetchTicket = stockService.issueStockReadTicket();
  stockService.setAuthoritativeStock(branchId, skuId, 8, { skuId, skuCode: sku, barcode }, refetchTicket);
  assert.equal(stockService.getCachedStock(branchId, skuId).available, 8, 'Cache after refetch should be 8');

  // Older barcode returns 10 at t=3 with barcodeTicket (from t=1)
  const barcodeCommitted = stockService.setAuthoritativeStock(branchId, skuId, 10, { skuId, skuCode: sku, barcode }, barcodeTicket);
  assert.equal(barcodeCommitted, false, 'Barcode with older ticket should be rejected');

  // Verify caller logic in ProductSearch:
  let effectiveAvailable = 10;
  if (!barcodeCommitted) {
    const latestCached = stockService.getCachedStock(branchId, skuId);
    effectiveAvailable = latestCached ? latestCached.available : 10;
  }
  assert.equal(stockService.getCachedStock(branchId, skuId).available, 8, 'Cache must remain 8');
  assert.equal(effectiveAvailable, 8, 'ProductSearch onAddToCart available must be 8, not 10');
  console.log('[PROBE 1 - Older Barcode vs Newer Refetch]: PASS! Cache=8, Added=8');

  // PROBE 2: Fresh Search 8 with Existing Cache 10
  api._resetStockState();
  // Existing cache has stock 10 from previous transaction / search (ticket 1)
  const initialTicket = stockService.issueStockReadTicket();
  stockService.setAuthoritativeStock(branchId, skuId, 10, { skuId, skuCode: sku, barcode }, initialTicket);
  assert.equal(stockService.getCachedStock(branchId, skuId).available, 10, 'Initial cache is 10');

  // User types query -> search starts at t=2
  const searchTicket = stockService.issueStockReadTicket();
  // Fresh search returns 8 from API at t=3
  const searchCommitted = stockService.setAuthoritativeStock(branchId, skuId, 8, { skuId, skuCode: sku, barcode }, searchTicket);
  assert.equal(searchCommitted, true, 'Fresh search should commit successfully');
  assert.equal(stockService.getCachedStock(branchId, skuId).available, 8, 'Cache must update to 8');

  // When user clicks row, handleSelectProduct reads stockMap
  const selectedCached = stockService.getCachedStock(branchId, skuId);
  const selectedEffective = selectedCached ? selectedCached.available : 8;
  assert.equal(selectedEffective, 8, 'Selected product from dropdown must add with available = 8');
  console.log('[PROBE 2 - Fresh Search 8 with Existing Cache 10]: PASS! Cache=8, Added=8');

  // PROBE 3: Search old 10 completes after direct write 8
  api._resetStockState();
  const oldSearchTicket = stockService.issueStockReadTicket();
  // Direct write (e.g. checkout deduction or event) at t=2 writes 8
  stockService.setAuthoritativeStock(branchId, skuId, 8, { skuId, skuCode: sku, barcode }); // direct write
  // Old search returns 10 at t=3
  const oldSearchCommitted = stockService.setAuthoritativeStock(branchId, skuId, 10, { skuId, skuCode: sku, barcode }, oldSearchTicket);
  assert.equal(oldSearchCommitted, false, 'Old search should not overwrite direct write');
  assert.equal(stockService.getCachedStock(branchId, skuId).available, 8, 'Cache stays 8');
  console.log('[PROBE 3 - Search old 10 completes after direct write 8]: PASS! Cache=8');

  // PROBE 4: Refetch old 10 completes after direct write 8
  api._resetStockState();
  const oldRefetchTicket = stockService.issueStockReadTicket();
  stockService.setAuthoritativeStock(branchId, skuId, 8, { skuId, skuCode: sku, barcode });
  const oldRefetchCommitted = stockService.setAuthoritativeStock(branchId, skuId, 10, { skuId, skuCode: sku, barcode }, oldRefetchTicket);
  assert.equal(oldRefetchCommitted, false, 'Old refetch should not overwrite direct write');
  assert.equal(stockService.getCachedStock(branchId, skuId).available, 8, 'Cache stays 8');
  console.log('[PROBE 4 - Refetch old 10 completes after direct write 8]: PASS! Cache=8');
}

runProbes().then(() => {
  console.log('\n=== ALL v1.4 PROBES PASSED 100% ===');
}).catch(e => {
  console.error('FAILED:', e);
  process.exit(1);
});
