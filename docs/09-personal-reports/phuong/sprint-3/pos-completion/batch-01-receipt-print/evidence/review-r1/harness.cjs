const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const root = path.resolve(process.cwd(), "src/frontend/pos-app");
const ts = require(path.join(root, "node_modules/typescript"));
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const observations = [];
const userA = { id: 'user-a', email: 'a@fixture.local', tenantId: 'tenant-a', tenantName: 'Fixture Shop', fullName: 'Cashier A' };
const userB = { id: 'user-b', email: 'b@fixture.local', tenantId: 'tenant-a', tenantName: 'Fixture Shop', fullName: 'Cashier B' };
const branchA = '11111111-1111-4111-8111-111111111111';
const branchB = '22222222-2222-4222-8222-222222222222';
const props = {
  isOpen: true, onClose() {}, branchId: branchA, branchName: 'Branch A', tenantName: userA.tenantName, cashierName: userA.fullName,
  items: [{ variantId: '33333333-3333-4333-8333-333333333333', sku: 'SKU-A', productName: 'Fixture product', variantName: '', quantity: 1, retailPrice: 100, available: 10 }],
  onSuccessAndPrint() {}, onPreviewReceipt() {},
};
function environment(fetchImpl) {
  const local = new Map([['pos_access_token', 'fixture-token'], ['pos_user', JSON.stringify(userA)]]);
  const session = new Map(); const listeners = new Map();
  const storage = map => ({ getItem: k => map.get(k) ?? null, setItem: (k, v) => map.set(k, String(v)), removeItem: k => map.delete(k) });
  const env = { fetch: fetchImpl, console, crypto: crypto.webcrypto, Intl, Date, Math,
    localStorage: storage(local), sessionStorage: storage(session), navigator: { onLine: true }, alert() {},
    CustomEvent: class { constructor(type) { this.type = type; } }, local, session,
  };
  env.window = { sessionStorage: env.sessionStorage, localStorage: env.localStorage, confirm: () => true,
    addEventListener(type, callback) { const set = listeners.get(type) ?? new Set(); set.add(callback); listeners.set(type, set); },
    removeEventListener: (type, callback) => listeners.get(type)?.delete(callback),
    dispatchEvent(event) { for (const callback of listeners.get(event.type) ?? []) callback(event); },
  };
  return env;
}
function load(source, filename, env, imports = {}) {
  const js = ts.transpileModule(source, { fileName: filename, compilerOptions: {
    module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX, esModuleInterop: true,
  }}).outputText;
  const mod = { exports: {} };
  vm.runInNewContext(js, { ...env, module: mod, exports: mod.exports,
    require: name => { if (!(name in imports)) throw new Error(`Unexpected import ${name}`); return imports[name]; },
  }, { filename });
  return mod.exports;
}
function apiFor(env) { return load(read('src/services/api.ts').replace('import.meta.env.VITE_API_BASE_URL', 'undefined'), 'api.ts', env); }
function mount(file, exportName, env, imports, input) {
  const state = []; const effects = []; let cursor = 0; let collectEffects = true;
  const react = {
    useState(initial) { const i = cursor++; if (!(i in state)) state[i] = typeof initial === 'function' ? initial() : initial;
      return [state[i], value => state[i] = typeof value === 'function' ? value(state[i]) : value]; },
    useEffect(callback) { if (collectEffects) effects.push(callback); }, useRef: value => ({ current: value }),
  };
  const jsx = (type, props) => ({ type, props });
  const exports = load(read(file), file, env, { ...imports, react, 'react/jsx-runtime': { jsx, jsxs: jsx, Fragment: 'Fragment' }, 'lucide-react': {} });
  const instance = { state, effects, render() { cursor = 0; const tree = exports[exportName](input); collectEffects = false; return tree; } };
  instance.render();
  return instance;
}
function find(node, predicate) {
  if (Array.isArray(node)) return node.map(n => find(n, predicate)).find(Boolean) || null;
  if (!node || typeof node !== 'object') return null;
  if (predicate(node)) return node;
  return find(node.props?.children, predicate);
}
function modal(env, api, input = props) { return mount('src/components/PaymentModal.tsx', 'PaymentModal', env, { '../services/api': api }, input); }
function app(env, api, branches = [{ id: branchA, name: 'Branch A' }]) {
  const components = {};
  const imports = { './services/api': { ...api, fetchBranches: async () => branches } };
  for (const name of ['PosHeader', 'ProductSearch', 'CartTable', 'PaymentModal', 'ReceiptPrintModal', 'LoginModal']) {
    components[name] = function FixtureComponent() {};
    imports[`./components/${name}`] = { [name]: components[name] };
  }
  const instance = mount('src/App.tsx', 'App', env, imports);
  instance.components = components;
  for (const effect of instance.effects) effect();
  return instance;
}
async function checkout(instance) {
  const button = find(instance.render(), n => n.type === 'button' && String(n.props.onClick).includes('handleCheckout(false)'));
  assert.ok(button && !button.props.disabled, 'Actual checkout button should be usable');
  await button.props.onClick();
}
const response = (status, value) => ({ status, ok: status >= 200 && status < 300, json: async () => value });
function transport(firstOutcome = 'lost') {
  const orders = new Map(); const calls = [];
  return { orders, calls, fetch: async (url, init) => {
    const req = JSON.parse(init.body); calls.push(req);
    assert.equal(init.headers['Idempotency-Key'], req.requestKey);
    let order = orders.get(req.requestKey);
    if (!order) { order = { number: `ORDER-${orders.size + 1}` }; orders.set(req.requestKey, order); }
    if (calls.length === 1 && firstOutcome === 'lost') throw new TypeError('Injected response loss after modeled commit');
    if (calls.length === 1 && firstOutcome === '500') return response(500, { title: 'Internal Server Error', status: 500 });
    return response(200, { success: true, orderNumber: order.number, receipt: { receiptNumber: order.number, createdAt: '2026-10-27T07:00:00Z' } });
  }};
}
