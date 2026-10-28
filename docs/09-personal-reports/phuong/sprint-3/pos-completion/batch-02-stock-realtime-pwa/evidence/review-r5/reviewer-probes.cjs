const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const { world, branchA, userA, sku, response, defer, input, flush, find } = require('../review-r4/harness.cjs');
const observations = [];

(async () => {
  // Existing R4 counterexample: actual barcode component vs actual newer refresh.
  {
    const gate = defer();
    const w = world(async url => {
      if (String(url).includes('/barcode/')) { await gate.promise; return response({ ...sku, available: 10 }); }
      return response([{ ...sku, available: 8 }]);
    });
    w.api.setAuthoritativeStock(branchA, sku.variantId, 10, { skuId: sku.variantId, skuCode: sku.sku, barcode: sku.barcode });
    const added = [];
    const m = w.mountSearch({ branchId: branchA, onAddToCart: x => added.push(x), searchInputRef: { current: null } });
    input(m).props.onChange({ target: { value: 'BAR-A' } }); m.settle();
    const pending = input(m).props.onKeyDown({ key: 'Enter', preventDefault() {} }); await flush();
    await w.api.invalidateAndRefetchStock(branchA, [{ skuId: sku.variantId, skuCode: sku.sku }]);
    assert.equal(w.api.getCachedStock(branchA, sku.variantId).available, 8);
    gate.resolve(); await pending; m.settle();
    assert.equal(w.api.getCachedStock(branchA, sku.variantId).available, 8);
    assert.equal(added[0].available, 8);
    observations.push({ case: 'F04-R4-old-barcode-after-new-refresh', status: 'PASS', final: 8, added: 8 });
  }
  // Existing R4 counterexample: fresh search must update an existing cache.
  {
    const w = world(async () => response([{ ...sku, available: 8 }]));
    w.api.setAuthoritativeStock(branchA, sku.variantId, 10, { skuId: sku.variantId, skuCode: sku.sku, barcode: sku.barcode });
    const added = [];
    const m = w.mountSearch({ branchId: branchA, onAddToCart: x => added.push(x), searchInputRef: { current: null } });
    input(m).props.onChange({ target: { value: 'SKU-A' } }); m.settle(); w.advance(200); await flush(); m.settle();
    const row = find(m.tree(), n => typeof n.props?.onClick === 'function' && String(n.props.onClick).includes('handleSelectProduct'));
    assert.ok(row); row.props.onClick();
    assert.equal(w.api.getCachedStock(branchA, sku.variantId).available, 8);
    assert.equal(added[0].available, 8);
    observations.push({ case: 'F04-R4-fresh-search-cached10', status: 'PASS', final: 8, added: 8 });
  }
  // Preserve the opposite direction: newer barcode response protects against old refresh.
  {
    const gate = defer();
    const w = world(async url => String(url).includes('/barcode/') ? response({ ...sku, available: 8 }) : (await gate.promise, response([{ ...sku, available: 10 }])));
    w.api.setAuthoritativeStock(branchA, sku.variantId, 10, { skuId: sku.variantId, skuCode: sku.sku, barcode: sku.barcode });
    const oldRefresh = w.api.invalidateAndRefetchStock(branchA, [{ skuId: sku.variantId, skuCode: sku.sku }]); await flush();
    const m = w.mountSearch({ branchId: branchA, onAddToCart() {}, searchInputRef: { current: null } });
    input(m).props.onChange({ target: { value: 'BAR-A' } }); m.settle(); await input(m).props.onKeyDown({ key: 'Enter', preventDefault() {} });
    gate.resolve(); await oldRefresh;
    assert.equal(w.api.getCachedStock(branchA, sku.variantId).available, 8);
    observations.push({ case: 'F04-old-refresh-after-new-barcode', status: 'PASS', final: 8 });
  }
  // Newer refresh failure must not be erased by an older barcode completion.
  {
    const gate = defer();
    const w = world(async url => {
      if (String(url).includes('/barcode/')) { await gate.promise; return response({ ...sku, available: 10 }); }
      throw new Error('reviewer-newer-refresh-network-failure');
    });
    w.api.setAuthoritativeStock(branchA, sku.variantId, 10, { skuId: sku.variantId, skuCode: sku.sku, barcode: sku.barcode });
    w.api.setAuthoritativeStock(branchA, sku.sku, 10, { skuId: sku.variantId, skuCode: sku.sku, barcode: sku.barcode });
    const added = [];
    const m = w.mountSearch({ branchId: branchA, onAddToCart: x => added.push(x), searchInputRef: { current: null } });
    input(m).props.onChange({ target: { value: 'BAR-A' } }); m.settle();
    const pending = input(m).props.onKeyDown({ key: 'Enter', preventDefault() {} }); await flush();
    const refresh = await w.api.invalidateAndRefetchStock(branchA, [{ skuCode: sku.sku }]);
    assert.equal(refresh.success, false);
    const before = { ...w.api.getCachedStock(branchA, sku.sku) };
    assert.equal(before.available, 10);
    assert.equal(before.isStale, true);
    gate.resolve(); await pending; m.settle();
    const after = { ...w.api.getCachedStock(branchA, sku.sku) };
    assert.equal(after.isStale, false); // Assert the reproduced bug, not acceptance.
    assert.equal(after.error, null);
    observations.push({ case: 'F04-old-barcode-clears-newer-refresh-error', status: 'REPRODUCED', beforeOlderCompletion: before, afterOlderCompletion: after, cartAddedAvailable: added[0]?.available });
  }
  // Newer refresh failure must not be erased by an older refresh completion either.
  {
    const gate = defer(); let call = 0;
    const w = world(async () => { if (++call === 1) { await gate.promise; return response([{ ...sku, available: 10 }]); } throw new Error('reviewer-newer-refresh-network-failure'); });
    w.api.setAuthoritativeStock(branchA, sku.variantId, 10, { skuId: sku.variantId, skuCode: sku.sku, barcode: sku.barcode });
    w.api.setAuthoritativeStock(branchA, sku.sku, 10, { skuId: sku.variantId, skuCode: sku.sku, barcode: sku.barcode });
    const older = w.api.invalidateAndRefetchStock(branchA, [{ skuCode: sku.sku }]); await flush();
    await w.api.invalidateAndRefetchStock(branchA, [{ skuCode: sku.sku }]);
    const before = { ...w.api.getCachedStock(branchA, sku.sku) }; assert.equal(before.isStale, true);
    assert.equal(before.available, 10);
    gate.resolve(); await older;
    const after = { ...w.api.getCachedStock(branchA, sku.sku) }; assert.equal(after.isStale, false);
    observations.push({ case: 'F04-old-refresh-clears-newer-refresh-error', status: 'REPRODUCED', beforeOlderCompletion: before, afterOlderCompletion: after });
  }
  const reported = JSON.parse(fs.readFileSync(path.resolve(__dirname, '../v1.4/working-tree-hashes.json'), 'utf8'));
  const hashes = Object.entries(reported).map(([file, expected]) => {
    const actual = crypto.createHash('sha256').update(fs.readFileSync(path.resolve(file))).digest('hex');
    return { path: file, expected, actual, matches: actual === expected };
  });
  assert.ok(hashes.every(h => h.matches));
  const output = { provider: 'Actual TS/TSX in R4 hook harness with controlled HTTP; no backend or DB writes', observations, hashes };
  fs.writeFileSync(path.join(__dirname, 'probe-results.json'), JSON.stringify(output, null, 2) + '\n');
  console.log(JSON.stringify(output, null, 2));
})().catch(error => { console.error(error); process.exitCode = 1; });
