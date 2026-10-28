const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const ts = require('../../src/frontend/pos-app/node_modules/typescript');
const baseline = process.argv.includes('--baseline');
const root = path.join(__dirname, baseline ? 'base/src/frontend/pos-app' : 'head/src/frontend/pos-app');
const apiSource = fs.readFileSync(path.join(root, 'src/services/api.ts'), 'utf8').replace('import.meta.env.VITE_API_BASE_URL', 'undefined');
const modalSource = fs.readFileSync(path.join(root, 'src/components/PaymentModal.tsx'), 'utf8');
const observations = [];
function environment(fetchImpl) {
  const data = new Map([['pos_access_token', 'test-only-token']]);
  const events = [];
  const listeners = new Map();
  return {
    fetch: fetchImpl, console, crypto: crypto.webcrypto, Intl, Date, Math,
    localStorage: { getItem: k => data.get(k) ?? null, setItem: (k, v) => data.set(k, v), removeItem: k => data.delete(k) },
    window: {
      dispatchEvent: e => { events.push(e.type); for (const callback of listeners.get(e.type) ?? []) callback(e); },
      addEventListener: (type, callback) => { const callbacks = listeners.get(type) ?? new Set(); callbacks.add(callback); listeners.set(type, callbacks); },
      removeEventListener: (type, callback) => listeners.get(type)?.delete(callback),
    },
    navigator: { onLine: true }, alert() {},
    CustomEvent: class { constructor(type) { this.type = type; } },
    data, events,
  };
}
function load(source, fileName, env, imports = {}) {
  const js = ts.transpileModule(source, { fileName, compilerOptions: {
    module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022,
    jsx: ts.JsxEmit.ReactJSX, esModuleInterop: true,
  }}).outputText;
  const module = { exports: {} };
  vm.runInNewContext(js, { ...env, module, exports: module.exports, require: name => {
    if (name in imports) return imports[name];
    throw new Error(`Unexpected import: ${name}`);
  }}, { filename: fileName });
  return module.exports;
}
function response(status, data) { return { status, ok: status >= 200 && status < 300, json: async () => data }; }
function mount(env, props, api) {
  const states = []; let cursor = 0;
  const jsx = (type, props) => ({ type, props });
  const react = { useState: initial => {
    const index = cursor++;
    if (!(index in states)) states[index] = typeof initial === 'function' ? initial() : initial;
    return [states[index], next => states[index] = typeof next === 'function' ? next(states[index]) : next];
  }};
  const { PaymentModal } = load(modalSource, 'PaymentModal.tsx', env, {
    react, 'react/jsx-runtime': { jsx, jsxs: jsx, Fragment: 'Fragment' },
    'lucide-react': {}, '../services/api': api,
  });
  function find(node, predicate) {
    if (Array.isArray(node)) return node.map(x => find(x, predicate)).find(Boolean);
    if (!node || typeof node !== 'object') return null;
    if (predicate(node)) return node;
    return find(node.props?.children, predicate);
  }
  return { states, checkout: async () => {
    cursor = 0;
    const tree = PaymentModal(props);
    const button = find(tree, n => n.type === 'button' && String(n.props.onClick).includes('handleCheckout(false)'));
    assert.ok(button, 'Actual checkout button must exist');
    await button.props.onClick();
  }};
}
const baseProps = {
  isOpen: true, onClose() {}, branchId: crypto.randomUUID(), branchName: 'Branch UI',
  tenantName: 'Fixture', cashierName: 'Fixture cashier', onPreviewReceipt() {},
  items: [{ variantId: crypto.randomUUID(), sku: 'SKU-A', productName: 'Ten ao cu', variantName: '', quantity: 1, retailPrice: 100, available: 10 }],
};
(async () => {
  const stored = new Map(); const sent = []; const printed = [];
  const env = environment(async (url, init) => {
    const request = JSON.parse(init.body); sent.push({ url, key: request.requestKey, headers: init.headers });
    if (!baseline) {
      assert.equal(init.headers['Idempotency-Key'], request.requestKey);
      assert.equal(init.headers['X-Request-Key'], request.requestKey);
    }
    let receipt = stored.get(request.requestKey);
    if (!receipt) {
      receipt = { receiptNumber: `ORD-${stored.size + 1}`, orderNumber: `ORD-${stored.size + 1}`,
        createdAt: '2026-10-27T07:00:00Z', branchName: 'Branch server', cashierName: 'Cashier server',
        totalAmount: 100, amountGiven: 100, changeDue: 0, paymentMethod: 'Cash',
        items: [{ skuId: request.items[0].skuId, skuCode: 'SKU-A', productName: 'Ten ao moi trong snapshot server', variantName: '', quantity: 1, unitPrice: 100, subtotal: 100 }],
      };
      stored.set(request.requestKey, receipt);
    }
    if (sent.length === 1) throw new TypeError('Response lost after backend commit (injected)');
    return response(201, { success: true, orderNumber: receipt.orderNumber, receipt });
  });
  const api = load(apiSource, 'api.ts', env);
  const component = mount(env, { ...baseProps, onSuccessAndPrint: r => printed.push(r) }, api);
  await component.checkout();
  assert.equal(printed.length, 0);
  await component.checkout();
  assert.equal(printed.length, 1);
  observations.push({ probe: 'Canonical headers match body', result: sent.every(x => x.url.endsWith('/api/pos/checkout') && x.headers['Idempotency-Key'] === x.key) });
  observations.push({ probe: 'Lost response followed by retry using actual PaymentModal handler', keys: sent.map(x => x.key), modeledPersistedOrders: stored.size, bugReproduced: stored.size === 2 });
  const savedReceipt = [...stored.values()][1];
  observations.push({ probe: 'Server receipt metadata versus item snapshot', printedBranch: printed[0].branchName, serverBranch: savedReceipt.branchName,
    printedItemName: printed[0].items[0].name, serverItemName: savedReceipt.items[0].productName,
    bugReproduced: printed[0].items[0].name !== savedReceipt.items[0].productName });
  const problem = { type: 'https://oisms.local/errors/INSUFFICIENT_STOCK', title: 'INSUFFICIENT_STOCK', status: 409,
    detail: 'SKU-A: yeu cau 2, kha dung 1', errorCode: 'INSUFFICIENT_STOCK' };
  const errorEnv = environment(async () => response(409, problem));
  const errorApi = load(apiSource, 'api.ts', errorEnv);
  const failure = await errorApi.checkoutPosOrder({ ...baseProps, requestKey: crypto.randomUUID(), items: [] });
  observations.push({ probe: '409 ProblemDetails detail', expectedDetail: problem.detail, actualMessage: failure.message, bugReproduced: !failure.message.includes(problem.detail) });
  const authEnv = environment(async () => response(401, { title: 'Unauthorized', status: 401 }));
  const authApi = load(apiSource, 'api.ts', authEnv);
  const appStates = []; const effects = []; let appCursor = 0;
  const appReact = {
    useState: initial => {
      const index = appCursor++;
      if (!(index in appStates)) appStates[index] = typeof initial === 'function' ? initial() : initial;
      return [appStates[index], next => appStates[index] = typeof next === 'function' ? next(appStates[index]) : next];
    },
    useEffect: callback => effects.push(callback), useRef: value => ({ current: value }),
  };
  const element = (type, props) => ({ type, props });
  const appImports = { react: appReact, 'react/jsx-runtime': { jsx: element, jsxs: element, Fragment: 'Fragment' },
    'lucide-react': {}, './services/api': { ...authApi, fetchBranches: async () => [], getStoredUser: () => ({ fullName: 'Fixture' }) } };
  for (const name of ['PosHeader', 'ProductSearch', 'CartTable', 'PaymentModal', 'ReceiptPrintModal', 'LoginModal']) appImports[`./components/${name}`] = {};
  const app = load(fs.readFileSync(path.join(root, 'src/App.tsx'), 'utf8'), 'App.tsx', authEnv, appImports);
  app.App();
  appStates[0] = [...baseProps.items]; appStates[6] = true;
  for (const effect of effects) effect();
  const beforeAuthCartSize = appStates[0].length;
  await authApi.checkoutPosOrder({ ...baseProps, requestKey: crypto.randomUUID(), items: [] });
  observations.push({ probe: 'Checkout 401 through actual App auth-expired listener', cartBefore: beforeAuthCartSize, cartAfter: appStates[0].length,
    authEvents: authEnv.events, paymentOpenAfter: appStates[6], loginOpenAfter: appStates[7], bugReproduced: appStates[0].length === 0 });
  fs.writeFileSync(path.join(__dirname, baseline ? 'baseline-probe-results.json' : 'probe-results.json'), JSON.stringify({
    head: baseline ? '9e41eb11f751dfeac64d2eef3b360a02810113be' : '44317f3b5188c276fa3ef25c8faf30323e503079', provider: 'Actual TS/TSX functions transpiled in Node VM; mocked React hooks, fetch and server persistence model; no real HTTP/PostgreSQL/browser E2E', observations,
  }, null, 2));
  console.log(JSON.stringify(observations, null, 2));
})().catch(error => { console.error(error); process.exitCode = 1; });
