const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { world, branchA, sku, response, defer, input, flush, find } = require('../../../docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-02-stock-realtime-pwa/evidence/review-r4/harness.cjs');

const results = [];

(async () => {
  console.log('=== RUNNING S3-POS-B02 v1.5 PROBES ===');
  console.log('Timestamp:', new Date().toISOString());

  // TEST 1: Older in-flight barcode response (10) returns after newer refresh fails
  // ACCEPTANCE: Cache MUST retain isStale: true and error message. Older barcode response must NOT clear error or add as authoritative fresh stock.
  {
    console.log('\n[TEST 1] Older barcode returns after newer refresh fails:');
    const gate = defer();
    const w = world(async (url) => {
      if (String(url).includes('/barcode/')) {
        await gate.promise;
        return response({ ...sku, available: 10 });
      }
      throw new Error('network failure on refresh');
    });

    w.api.setAuthoritativeStock(branchA, sku.variantId, 10, { skuId: sku.variantId, skuCode: sku.sku, barcode: sku.barcode });
    w.api.setAuthoritativeStock(branchA, sku.sku, 10, { skuId: sku.variantId, skuCode: sku.sku, barcode: sku.barcode });

    const added = [];
    const m = w.mountSearch({ branchId: branchA, onAddToCart: (x) => added.push(x), searchInputRef: { current: null } });

    // 1. In-flight barcode read starts
    input(m).props.onChange({ target: { value: 'BAR-A' } });
    m.settle();
    const pendingBarcode = input(m).props.onKeyDown({ key: 'Enter', preventDefault() {} });
    await flush();

    // 2. Newer refresh fails
    const refresh = await w.api.invalidateAndRefetchStock(branchA, [{ skuCode: sku.sku }]);
    assert.equal(refresh.success, false, 'Refresh should report failure');

    const beforeOlder = { ...w.api.getCachedStock(branchA, sku.sku) };
    assert.equal(beforeOlder.isStale, true, 'Cache must be stale immediately after refresh failure');
    assert.ok(beforeOlder.error, 'Cache must have error message');

    // 3. Older barcode finishes
    gate.resolve();
    await pendingBarcode;
    m.settle();

    const afterOlder = { ...w.api.getCachedStock(branchA, sku.sku) };
    console.log('  State after older barcode completion:', { isStale: afterOlder.isStale, error: afterOlder.error, addedCount: added.length });

    assert.equal(afterOlder.isStale, true, 'Older barcode must NOT clear isStale flag of newer failed refresh');
    assert.ok(afterOlder.error, 'Older barcode must NOT clear error message of newer failed refresh');
    assert.equal(added.length, 0, 'Older barcode must NOT be added to cart as authoritative fresh stock');

    console.log('  -> TEST 1 PASSED: Failure state preserved against older barcode completion');
    results.push({ test: 'older-barcode-after-refresh-fail', status: 'PASS' });
  }

  // TEST 2: Older in-flight refetch response (10) returns after newer refresh fails
  // ACCEPTANCE: Cache MUST retain isStale: true and error message.
  {
    console.log('\n[TEST 2] Older refetch returns after newer refresh fails:');
    const gate = defer();
    let call = 0;
    const w = world(async () => {
      if (++call === 1) {
        await gate.promise;
        return response([{ ...sku, available: 10 }]);
      }
      throw new Error('network failure on newer refresh');
    });

    w.api.setAuthoritativeStock(branchA, sku.variantId, 10, { skuId: sku.variantId, skuCode: sku.sku, barcode: sku.barcode });
    w.api.setAuthoritativeStock(branchA, sku.sku, 10, { skuId: sku.variantId, skuCode: sku.sku, barcode: sku.barcode });

    // 1. Older refetch starts
    const olderRefetch = w.api.invalidateAndRefetchStock(branchA, [{ skuCode: sku.sku }]);
    await flush();

    // 2. Newer refetch fails
    const newerRefetch = await w.api.invalidateAndRefetchStock(branchA, [{ skuCode: sku.sku }]);
    assert.equal(newerRefetch.success, false, 'Newer refresh should report failure');

    const beforeOlder = { ...w.api.getCachedStock(branchA, sku.sku) };
    assert.equal(beforeOlder.isStale, true, 'Cache must be stale after failure');

    // 3. Older refetch resolves
    gate.resolve();
    await olderRefetch;

    const afterOlder = { ...w.api.getCachedStock(branchA, sku.sku) };
    console.log('  State after older refetch completion:', { isStale: afterOlder.isStale, error: afterOlder.error });

    assert.equal(afterOlder.isStale, true, 'Older refetch must NOT clear isStale flag of newer failed refresh');
    assert.ok(afterOlder.error, 'Older refetch must NOT clear error message of newer failed refresh');

    console.log('  -> TEST 2 PASSED: Failure state preserved against older refetch completion');
    results.push({ test: 'older-refetch-after-refresh-fail', status: 'PASS' });
  }

  // TEST 3: Newer retry (8) succeeds after failed refresh
  // ACCEPTANCE: Fresh stock 8 clears stale and error flags.
  {
    console.log('\n[TEST 3] Newer retry 8 succeeds after failed refresh:');
    let shouldFail = true;
    const w = world(async () => {
      if (shouldFail) throw new Error('temporary network failure');
      return response([{ ...sku, available: 8 }]);
    });

    w.api.setAuthoritativeStock(branchA, sku.variantId, 10, { skuId: sku.variantId, skuCode: sku.sku, barcode: sku.barcode });
    w.api.setAuthoritativeStock(branchA, sku.sku, 10, { skuId: sku.variantId, skuCode: sku.sku, barcode: sku.barcode });

    // Refresh fails
    const failRefresh = await w.api.invalidateAndRefetchStock(branchA, [{ skuCode: sku.sku }]);
    assert.equal(failRefresh.success, false);
    assert.equal(w.api.getCachedStock(branchA, sku.sku).isStale, true);

    // Retry succeeds
    shouldFail = false;
    const retryRefresh = await w.api.invalidateAndRefetchStock(branchA, [{ skuCode: sku.sku }]);
    assert.equal(retryRefresh.success, true);

    const freshStock = w.api.getCachedStock(branchA, sku.sku);
    assert.equal(freshStock.available, 8, 'Cache available should be 8');
    assert.equal(freshStock.isStale, false, 'isStale must be false after successful retry');
    assert.equal(freshStock.error, null, 'error must be null after successful retry');

    console.log('  -> TEST 3 PASSED: Successful retry clears stale state and sets fresh stock 8');
    results.push({ test: 'newer-retry-succeeds-clears-stale', status: 'PASS' });
  }

  // TEST 4: R4 Success Race - Older barcode (10) returns after newer refetch (8) succeeds
  {
    console.log('\n[TEST 4] R4 Success Race: older barcode after newer refetch 8:');
    const gate = defer();
    const w = world(async (url) => {
      if (String(url).includes('/barcode/')) {
        await gate.promise;
        return response({ ...sku, available: 10 });
      }
      return response([{ ...sku, available: 8 }]);
    });

    w.api.setAuthoritativeStock(branchA, sku.variantId, 10, { skuId: sku.variantId, skuCode: sku.sku, barcode: sku.barcode });
    const added = [];
    const m = w.mountSearch({ branchId: branchA, onAddToCart: (x) => added.push(x), searchInputRef: { current: null } });

    input(m).props.onChange({ target: { value: 'BAR-A' } });
    m.settle();
    const pending = input(m).props.onKeyDown({ key: 'Enter', preventDefault() {} });
    await flush();

    await w.api.invalidateAndRefetchStock(branchA, [{ skuId: sku.variantId, skuCode: sku.sku }]);
    assert.equal(w.api.getCachedStock(branchA, sku.variantId).available, 8);

    gate.resolve();
    await pending;
    m.settle();

    assert.equal(w.api.getCachedStock(branchA, sku.variantId).available, 8);
    assert.equal(added[0].available, 8);
    console.log('  -> TEST 4 PASSED: Newer 8 preserved against older barcode 10');
    results.push({ test: 'r4-older-barcode-newer-refetch-success', status: 'PASS' });
  }

  // TEST 5: R4 Success Race - Fresh search 8 with existing cache 10
  {
    console.log('\n[TEST 5] R4 Success Race: fresh search 8 with existing cache 10:');
    const w = world(async () => response([{ ...sku, available: 8 }]));
    w.api.setAuthoritativeStock(branchA, sku.variantId, 10, { skuId: sku.variantId, skuCode: sku.sku, barcode: sku.barcode });
    const added = [];
    const m = w.mountSearch({ branchId: branchA, onAddToCart: (x) => added.push(x), searchInputRef: { current: null } });

    input(m).props.onChange({ target: { value: 'SKU-A' } });
    m.settle();
    w.advance(200);
    await flush();
    m.settle();

    const row = find(m.tree(), (n) => typeof n.props?.onClick === 'function' && String(n.props.onClick).includes('handleSelectProduct'));
    assert.ok(row, 'Search dropdown item must exist');
    row.props.onClick();

    assert.equal(w.api.getCachedStock(branchA, sku.variantId).available, 8);
    assert.equal(added[0].available, 8);
    console.log('  -> TEST 5 PASSED: Fresh search 8 overrides cache 10');
    results.push({ test: 'r4-fresh-search-overrides-cache', status: 'PASS' });
  }

  // TEST 6: Older refetch 10 after newer barcode 8 succeeds
  {
    console.log('\n[TEST 6] Older refetch 10 after newer barcode 8:');
    const gate = defer();
    const w = world(async (url) =>
      String(url).includes('/barcode/') ? response({ ...sku, available: 8 }) : (await gate.promise, response([{ ...sku, available: 10 }]))
    );

    w.api.setAuthoritativeStock(branchA, sku.variantId, 10, { skuId: sku.variantId, skuCode: sku.sku, barcode: sku.barcode });
    const oldRefresh = w.api.invalidateAndRefetchStock(branchA, [{ skuId: sku.variantId, skuCode: sku.sku }]);
    await flush();

    const m = w.mountSearch({ branchId: branchA, onAddToCart() {}, searchInputRef: { current: null } });
    input(m).props.onChange({ target: { value: 'BAR-A' } });
    m.settle();
    await input(m).props.onKeyDown({ key: 'Enter', preventDefault() {} });

    gate.resolve();
    await oldRefresh;

    assert.equal(w.api.getCachedStock(branchA, sku.variantId).available, 8);
    console.log('  -> TEST 6 PASSED: Newer barcode 8 preserved against older refetch 10');
    results.push({ test: 'older-refetch-newer-barcode-success', status: 'PASS' });
  }

  console.log('\n=== ALL v1.5 PROBES PASSED ===');
  console.log(JSON.stringify(results, null, 2));
})().catch((err) => {
  console.error('\n[PROBE FAILURE DETECTED]:', err.message);
  console.error(err.stack);
  process.exit(1);
});
