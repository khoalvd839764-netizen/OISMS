const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const net = require('node:net');
const crypto = require('node:crypto');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const { pathToFileURL } = require('node:url');
const ts = require(path.resolve('src/frontend/pos-app/node_modules/typescript'));
const sourceFile = path.resolve('src/frontend/pos-app/test-b02-hub-transport.mjs');

const own = '11111111-0000-0000-0001-000000000001';
const foreign = '22222222-0000-0000-0001-000000000001';
const skuId = '11111111-0000-0000-0002-000000000001';
const user = { id: 'fixture-cashier-A', tenantId: '11111111-1111-1111-1111-111111111111', fullName: 'Fixture A' };
const response = (data, status = 200) => ({ ok: status >= 200 && status < 300, status, json: async () => data });

function createHarness({
  emitStockEvent = true,
  autoReconnect = true,
  unverifiedReady = false,
  rejectRejoin = false,
  wrongRecoveredStock = false,
} = {}) {
  const requests = [];
  const connections = [];
  const loadedModules = [];
  const logs = [];
  let isOffline = false;
  let hasDisrupted = false;
  let checkoutCount = 0;
  let currentStock = 10;

  async function fetchFixture(raw, init = {}) {
    const rawUrl = typeof raw === 'string' ? raw : raw?.url || '';
    const url = new URL(rawUrl);
    requests.push({ url: url.href, method: init.method || 'GET' });

    if (url.pathname === '/health/live') return response({ status: 'Live' });
    if (url.pathname === '/health/ready') {
      if (unverifiedReady) {
        return response({ isHealthy: true, currentUser: 'shared_app', description: 'Ready' });
      }
      return response({ isHealthy: true, status: 'Healthy', currentUser: 'oisms_test_app', description: 'isolated' });
    }

    if (url.pathname === '/api/auth/login') {
      const login = JSON.parse(init.body || '{}');
      const isB = login.email?.includes('shopb');
      return response({
        accessToken: url.port === '5001' ? 'fixture-TEST-token' : 'fixture-DEMO-token',
        user: isB ? { ...user, id: 'fixture-B', tenantId: 'tenant-B' } : user,
      });
    }

    if (url.pathname === '/api/branches') return response([{ id: foreign }]);

    if (url.pathname === '/api/pos/checkout') {
      checkoutCount++;
      const body = JSON.parse(init.body || '{}');
      const qty = body.items?.[0]?.quantity || 1;
      currentStock = Math.max(0, currentStock - qty);

      if (!isOffline) {
        // Emit events when online
        setTimeout(() => {
          for (const conn of connections) {
            conn.handlers['OrderCreated']?.({
              orderNumber: 'TEST-ORDER-00' + checkoutCount,
              totalAmount: body.totalAmount || 200000,
              branchId: own,
              createdAt: new Date().toISOString(),
            });
            if (emitStockEvent) {
              conn.handlers['StockLevelChanged']?.({
                skuId: skuId,
                branchId: own,
                newAvailableStock: currentStock,
                changedAt: new Date().toISOString(),
              });
            }
          }
        }, 50);
      }
      return response({ success: true, orderNumber: 'TEST-ORDER-00' + checkoutCount }, 201);
    }

    // Refetch endpoints
    if (url.pathname.includes('/api/inventory/balances/sku/') || url.pathname === '/api/pos/search' || url.pathname.includes('/api/pos/barcode/')) {
      // Corrupt only recovery reads made by the client, preserving the direct
      // no-store backend oracle used to verify the mutation and its baseline.
      const returnStock = wrongRecoveredStock && hasDisrupted && init.cache !== 'no-store' ? 123 : currentStock;
      return response({
        variantId: skuId,
        skuId: skuId,
        skuCode: 'AO-POLO-TRANG-M',
        barcode: '893000000001',
        available: returnStock,
        onHand: returnStock,
        reserved: 0,
      });
    }

    throw new Error('Unexpected fixture request: ' + rawUrl);
  }

  fetchFixture.__isFixture = true;

  class Connection {
    constructor(url, options) {
      this.url = url;
      this.options = options;
      this.state = 'Disconnected';
      this.handlers = {};
      connections.push(this);
    }
    on(name, cb) { this.handlers[name] = cb; }
    onclose(cb) { this.closeHandler = cb; }
    onreconnecting(cb) { this.reconnecting = cb; }
    onreconnected(cb) { this.reconnected = cb; }
    async start() { this.state = 'Connected'; }
    async stop() { this.state = 'Disconnected'; this.closeHandler?.(); }
    async invoke(method, id) {
      if (method === 'JoinBranchGroup' && id === '00000000-0000-0000-0000-000000000000') throw new Error('Thiếu TenantId hoặc BranchId');
      if (method === 'JoinBranchGroup' && id === foreign) throw new Error('Chi nhánh không tồn tại, đã ngừng hoạt động hoặc không thuộc Tenant của bạn');
      if (method === 'JoinBranchGroup') {
        if (hasDisrupted && rejectRejoin) {
          throw new Error('Fixture rejection: JoinBranchGroup denied');
        }
        assert.equal(id, own);
      }
    }
    disruptTransport(error = new Error('Transport dropped')) {
      this.state = 'Reconnecting';
      this.reconnecting?.(error);
      isOffline = true;
      hasDisrupted = true;
    }
    restoreTransport() {
      if (autoReconnect) {
        setTimeout(async () => {
          this.state = 'Connected';
          isOffline = false;
          await this.reconnected?.('reconnected-conn-id-999');
        }, 50);
      }
    }
    _connectionClosed(error) {
      this.disruptTransport(error);
    }
  }

  class Builder {
    withUrl(url, options) { this.url = url; this.options = options; return this; }
    withAutomaticReconnect() { return this; }
    configureLogging() { return this; }
    build() { return new Connection(this.url, this.options); }
  }

  const signalR = {
    HubConnectionBuilder: Builder,
    HubConnectionState: { Connected: 'Connected', Disconnected: 'Disconnected', Connecting: 'Connecting', Reconnecting: 'Reconnecting' },
    LogLevel: { Warning: 3 },
    HttpTransportType: { WebSockets: 1, ServerSentEvents: 2, LongPolling: 4 },
  };

  const quiet = {
    log(...args) { logs.push(args.join(' ')); },
    warn(...args) { logs.push(args.join(' ')); },
    error(...args) { logs.push(args.join(' ')); },
  };

  const vmSpy = {
    runInNewContext(js, ctx, options) {
      const result = vm.runInNewContext(js, ctx, options);
      loadedModules.push({
        file: options.filename,
        injectedToken: ctx.localStorage?.getItem('pos_access_token'),
        embedsDemoBackend: js.includes('http://localhost:5000'),
        embedsTestBackend: js.includes('http://localhost:5001'),
      });
      return result;
    },
  };

  const netControl = {
    disrupt() {
      for (const conn of connections) {
        conn.disruptTransport();
      }
    },
    restore() {
      for (const conn of connections) {
        conn.restoreTransport();
      }
    },
  };

  return { fetchFixture, signalR, quiet, vmSpy, netControl, requests, connections, loadedModules, logs };
}

function runRunnerWithEnv(env, harness) {
  let testBody;
  const imports = {
    'node:test': (_title, callback) => { testBody = callback; },
    'node:assert/strict': assert,
    'node:module': {
      createRequire: () => (name) => {
        if (name.includes('signalr')) return harness.signalR;
        throw new Error('Unexpected require: ' + name);
      },
    },
    'node:http': http,
    'node:net': net,
    'node:crypto': crypto,
    'node:path': path,
    'node:fs': fs,
    'node:vm': harness.vmSpy,
    typescript: ts,
  };

  globalThis.__OISMS_PREFLIGHT_NET_CONTROL__ = harness.netControl;

  const source = fs.readFileSync(sourceFile, 'utf8')
    .replace('const require = createRequire(import.meta.url);', 'const localSdkRequire = createRequire(import.meta.url);')
    .replace("const signalR = require('./node_modules/@microsoft/signalr');", "const signalR = localSdkRequire('./node_modules/@microsoft/signalr');")
    .replaceAll('import.meta.url', JSON.stringify(pathToFileURL(sourceFile).href));

  const js = ts.transpileModule(source, {
    fileName: 'runner.ts',
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, esModuleInterop: true },
  }).outputText;

  vm.runInNewContext(
    js,
    {
      exports: {},
      require: (name) => {
        if (!(name in imports)) throw new Error('Unexpected import ' + name);
        return imports[name];
      },
      console: harness.quiet,
      process: { env: { ...env, POS_PREFLIGHT_HARNESS: '1', POS_PREFLIGHT_FIXTURE: '1' } },
      fetch: harness.fetchFixture,
      __OISMS_PREFLIGHT_NET_CONTROL__: harness.netControl,
      globalThis: { __OISMS_PREFLIGHT_NET_CONTROL__: harness.netControl },
      Date, setTimeout, clearTimeout, URL, URLSearchParams, AbortController, Promise, Set, Map, Math, Intl, performance,
    },
    { filename: sourceFile }
  );

  return testBody;
}

module.exports = { createHarness, runRunnerWithEnv };

if (require.main === module) {
  (async () => {
    console.log('=== RUNNING OFFLINE PREFLIGHT & AUDIT HARNESS FOR F06 (v1.8) ===');
    console.log('Timestamp:', new Date().toISOString());

    // TEST 1: Full Live Workflow (Positive Preflight)
    {
      console.log('\n[PREFLIGHT TEST 1] Full Live Workflow on Isolated Test Host (Positive):');
      const harness = createHarness({ emitStockEvent: true, autoReconnect: true });
      const testBody = runRunnerWithEnv({
        POS_LIVE_TEST_ENV: '1',
        POS_TEST_BACKEND_URL: 'http://localhost:5001',
      }, harness);

      let error;
      try {
        await testBody({});
      } catch (caught) {
        error = `${caught.name}: ${caught.message}`;
      }

      assert.equal(error, undefined, `Preflight Test 1 should have ZERO errors, but got: ${error}`);
      assert.ok(harness.requests.some((r) => r.url === 'http://localhost:5001/api/pos/checkout'), 'Checkout sent to 5001');
      assert.ok(harness.logs.some((l) => l.includes('Live Event Mutation & Recovery Workflow: PASS')), 'Must log Recovery Workflow: PASS');
      console.log('  -> TEST 1 PASSED: OrderCreated, StockLevelChanged, Drop, Offline Mutation, Reconnect, Rejoin, GET & Exact Stock 7 PASS!');
    }

    // TEST 2: Demo Target Guard (Fail Closed on localhost:5000)
    {
      console.log('\n[PREFLIGHT TEST 2] Demo Target Guard (Fail Closed on localhost:5000):');
      const harness = createHarness();
      const testBody = runRunnerWithEnv({
        POS_LIVE_TEST_ENV: '1',
        POS_TEST_BACKEND_URL: 'http://localhost:5000',
      }, harness);

      let error;
      try {
        await testBody({});
      } catch (caught) {
        error = `${caught.name}: ${caught.message}`;
      }

      assert.ok(error, 'Runner must FAIL CLOSED when target is demo backend');
      assert.match(error, /DEMO TARGET GUARD REJECTED/, 'Error must indicate demo target guard rejection');
      const demoCheckouts = harness.requests.filter((r) => r.url === 'http://localhost:5000/api/pos/checkout');
      assert.equal(demoCheckouts.length, 0, 'Zero checkout requests allowed against demo backend');
      console.log('  -> TEST 2 PASSED: Demo target guard successfully rejected and prevented mutation on localhost:5000');
    }

    // TEST 3: Database Isolation Guard (Fail Closed on unverified currentUser = shared_app)
    {
      console.log('\n[PREFLIGHT TEST 3] Database Isolation Guard (Fail Closed on unverified shared DB):');
      const harness = createHarness({ unverifiedReady: true });
      const testBody = runRunnerWithEnv({
        POS_LIVE_TEST_ENV: '1',
        POS_TEST_BACKEND_URL: 'http://localhost:5001',
      }, harness);

      let error;
      try {
        await testBody({});
      } catch (caught) {
        error = `${caught.name}: ${caught.message}`;
      }

      assert.ok(error, 'Runner must FAIL CLOSED when database isolation is unverified');
      assert.match(error, /ISOLATION GUARD REJECTED/, 'Error must indicate isolation guard rejection');
      console.log('  -> TEST 3 PASSED: Isolation guard successfully rejected unverified database role!');
    }

    // TEST 4: Defect Catch - Missing StockLevelChanged
    {
      console.log('\n[PREFLIGHT TEST 4] Defect Catch: Missing StockLevelChanged event:');
      const harness = createHarness({ emitStockEvent: false });
      const testBody = runRunnerWithEnv({
        POS_LIVE_TEST_ENV: '1',
        POS_TEST_BACKEND_URL: 'http://localhost:5001',
      }, harness);

      let error;
      try {
        await testBody({});
      } catch (caught) {
        error = `${caught.name}: ${caught.message}`;
      }

      assert.ok(error, 'Runner must FAIL when StockLevelChanged is missing');
      assert.match(error, /StockLevelChanged/, 'Error must report missing StockLevelChanged');
      console.log('  -> TEST 4 PASSED: Runner caught missing StockLevelChanged defect!');
    }

    // TEST 5: Defect Catch - Missing Automatic Reconnect
    {
      console.log('\n[PREFLIGHT TEST 5] Defect Catch: Missing Automatic Reconnect:');
      const harness = createHarness({ autoReconnect: false });
      const testBody = runRunnerWithEnv({
        POS_LIVE_TEST_ENV: '1',
        POS_TEST_BACKEND_URL: 'http://localhost:5001',
      }, harness);

      let error;
      try {
        await testBody({});
      } catch (caught) {
        error = `${caught.name}: ${caught.message}`;
      }

      assert.ok(error, 'Runner must FAIL when auto-reconnect is missing');
      assert.match(error, /reconnect/, 'Error must report missing reconnect');
      console.log('  -> TEST 5 PASSED: Runner caught missing automatic reconnect defect!');
    }

    // TEST 6: Defect Catch - Incorrect Recovered Stock (R7 Probe)
    {
      console.log('\n[PREFLIGHT TEST 6] Defect Catch: Incorrect Recovered Stock (wrong stock 123 !== 7):');
      const harness = createHarness({ wrongRecoveredStock: true });
      const testBody = runRunnerWithEnv({
        POS_LIVE_TEST_ENV: '1',
        POS_TEST_BACKEND_URL: 'http://localhost:5001',
      }, harness);

      let error;
      try {
        await testBody({});
      } catch (caught) {
        error = `${caught.name}: ${caught.message}`;
      }

      assert.ok(error, 'Runner must FAIL when recovered stock does not match expected stock');
      assert.match(error, /Số tồn khôi phục phải khớp chính xác/, 'Error must report mismatched stock value');
      console.log('  -> TEST 6 PASSED: Runner caught incorrect recovered stock defect!');
    }

    // TEST 7: Defect Catch - Rejoin Rejected (R7 Probe)
    {
      console.log('\n[PREFLIGHT TEST 7] Defect Catch: Rejoin Rejected after drop:');
      const harness = createHarness({ rejectRejoin: true });
      const testBody = runRunnerWithEnv({
        POS_LIVE_TEST_ENV: '1',
        POS_TEST_BACKEND_URL: 'http://localhost:5001',
      }, harness);

      let error;
      try {
        await testBody({});
      } catch (caught) {
        error = `${caught.name}: ${caught.message}`;
      }

      assert.ok(error, 'Runner must FAIL when rejoin is rejected');
      assert.match(error, /Rejoin branch group sau reconnect phải thành công/, 'Error must report failed rejoin');
      console.log('  -> TEST 7 PASSED: Runner caught rejoin rejection defect!');
    }

    // TEST 8: Defect Catch - Omitted Reconnect Stock GET (R7 Probe)
    {
      console.log('\n[PREFLIGHT TEST 8] Defect Catch: Omitted Reconnect Stock GET:');
      const harness = createHarness();
      const origSpy = harness.vmSpy.runInNewContext;
      harness.vmSpy.runInNewContext = function(js, ctx, opt) {
        const res = origSpy.call(this, js, ctx, opt);
        if (opt.filename.includes('api.ts')) {
          ctx.module.exports.refreshBranchStock = async () => ({ success: false, updated: {}, errors: {} });
        }
        return res;
      };

      const testBody = runRunnerWithEnv({
        POS_LIVE_TEST_ENV: '1',
        POS_TEST_BACKEND_URL: 'http://localhost:5001',
      }, harness);

      let error;
      try {
        await testBody({});
      } catch (caught) {
        error = `${caught.name}: ${caught.message}`;
      }

      assert.ok(error, 'Runner must FAIL when post-reconnect stock GET is omitted');
      assert.match(error, /Phải có ít nhất 1 request GET stock được gửi sau khi reconnect/, 'Error must report omitted stock GET');
      console.log('  -> TEST 8 PASSED: Runner caught omitted reconnect stock GET defect!');
    }

    console.log('\n=== ALL 8 PREFLIGHT AUDIT TESTS PASSED 100% ===');

    const output = {
      provider: 'Offline preflight & audit harness v1.8 verifying full workflow, demo guard, isolation guard, and 5 negative defect probes',
      status: 'PASS',
      testCases: [
        { name: 'Full Live Workflow (5001)', status: 'PASS', asserted: ['OrderCreated', 'StockLevelChanged', 'networkDrop', 'offlineMutation', 'autoReconnect', 'rejoinSuccess', 'stockRefetch', 'exactStock7'] },
        { name: 'Demo Target Guard (5000)', status: 'PASS', result: 'Fail closed with DEMO TARGET GUARD REJECTED, 0 checkout requests' },
        { name: 'Database Isolation Guard', status: 'PASS', result: 'Fail closed with ISOLATION GUARD REJECTED on unverified DB role' },
        { name: 'Defect Catch (Missing Stock Event)', status: 'PASS', result: 'Caught with AssertionError, 0 false PASS' },
        { name: 'Defect Catch (Missing Auto-Reconnect)', status: 'PASS', result: 'Caught with AssertionError, 0 false PASS' },
        { name: 'Defect Catch (Incorrect Recovered Stock 123 !== 7)', status: 'PASS', result: 'Caught with AssertionError, 0 false PASS' },
        { name: 'Defect Catch (Rejoin Rejected)', status: 'PASS', result: 'Caught with AssertionError, 0 false PASS' },
        { name: 'Defect Catch (Omitted Reconnect Stock GET)', status: 'PASS', result: 'Caught with AssertionError, 0 false PASS' },
      ],
    };

    const resultsPath = path.resolve('docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-02-stock-realtime-pwa/evidence/v1.8/preflight-runner-results.json');
    fs.mkdirSync(path.dirname(resultsPath), { recursive: true });
    fs.writeFileSync(resultsPath, JSON.stringify(output, null, 2) + '\n');
  })().catch((err) => {
    console.error('\n[PREFLIGHT AUDIT FAILED]:', err);
    process.exit(1);
  });
}
