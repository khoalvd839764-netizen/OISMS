const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const { execFileSync } = require('node:child_process');
const ts = require('typescript');
const root = path.resolve(__dirname, '../../../..');
const walk = (n, pred) => Array.isArray(n) ? n.flatMap(x => walk(x, pred)) : !n || typeof n !== 'object' ? [] : [...(pred(n) ? [n] : []), ...walk(n.props?.children, pred)];
function world(options = {}) {
  const local = options.local || new Map(), session = options.session || new Map();
  const storage = m => ({ getItem: k => m.get(k) ?? null, setItem: (k, v) => m.set(k, v), removeItem: k => m.delete(k) });
  const user = options.user || { id: 'user-a', tenantId: 'tenant-a', fullName: 'Fixture A', email: 'a@fixture.test' };
  local.set('pos_access_token', 'fixture-token');
  const requests = []; let id = 0;
  const globals = { console: { log() {}, warn() {}, error() {} }, TextEncoder, Uint8Array, Date, URL, URLSearchParams, AbortController, Promise, Map, Set, Math, Intl, performance, setTimeout, clearTimeout,
    __importMeta: { env: { PROD: true, DEV: false } }, localStorage: storage(local), sessionStorage: storage(session),
    window: { sessionStorage: storage(session), localStorage: storage(local), dispatchEvent() {} }, CustomEvent: class {},
    crypto: { randomUUID: () => `${String(++id).padStart(8, '0')}-0000-4000-8000-000000000001` },
    fetch: async (url, init) => { requests.push({ url, payload: init?.body ? JSON.parse(init.body) : null }); return options.fetch ? options.fetch(url, init) : { ok: false, status: 500, json: async () => ({ detail: 'Fixture response lost after possible commit' }) }; },
  };
  function load(relative, imports = {}) {
    const file = 'src/frontend/pos-app/' + relative;
    const source = (process.env.POS_QR_BASELINE ? execFileSync('git', ['show', `${process.env.POS_QR_BASELINE}:${file}`], { cwd: root, encoding: 'utf8' }) : fs.readFileSync(path.join(root, file), 'utf8')).replace(/import\.meta/g, '__importMeta');
    const js = ts.transpileModule(source, { fileName: file, compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX } }).outputText;
    const mod = { exports: {} }; vm.runInNewContext(js, { ...globals, module: mod, exports: mod.exports, require: n => { if (!(n in imports)) throw Error('Unexpected import ' + n); return imports[n]; } }, { filename: file }); return mod.exports;
  }
  const api = load('src/services/api.ts'), qr = load('src/services/qrService.ts');
  let cursor = 0, dirty = false; const state = [], effects = [];
  const react = {
    useState(initial) { const i = cursor++; if (!(i in state)) state[i] = typeof initial === 'function' ? initial() : initial; return [state[i], value => { const next = typeof value === 'function' ? value(state[i]) : value; if (!Object.is(next, state[i])) { state[i] = next; dirty = true; } }]; },
    useEffect(cb, deps) { const i = cursor++, old = state[i]; if (!old || deps.some((x, j) => !Object.is(x, old[j]))) { state[i] = deps; effects.push(cb); } },
  };
  const jsx = (type, props) => ({ type, props });
  const component = load('src/components/PaymentModal.tsx', { react, 'react/jsx-runtime': { jsx, jsxs: jsx }, 'lucide-react': {}, '../services/api': api, '../services/qrService': qr }).PaymentModal;
  const props = { isOpen: true, onClose() { props.isOpen = false; }, branchId: 'branch-a', branchName: 'Branch A', tenantName: 'Tenant A', cashierName: 'Cashier A', currentUser: user,
    items: [{ variantId: 'sku-a', sku: 'A', productName: 'A', retailPrice: 100000, quantity: 1 }], onSuccessAndPrint() {}, onPreviewReceipt() {} };
  let tree;
  function render() { for (let n = 0; n < 20; n++) { dirty = false; cursor = 0; tree = component(props); effects.splice(0).forEach(f => f()); if (!dirty) return tree; } throw Error('Render did not settle'); }
  const button = text => walk(tree, n => n.type === 'button' && String(n.props.onClick).includes(text))[0];
  return { api, qr, props, user, local, session, globals, requests, render,
    selectQR() { render(); button("setPaymentMethod('BANK_TRANSFER_QR')").props.onClick(); render(); },
    closeOpen() { props.isOpen = false; render(); props.isOpen = true; render(); },
    image() { return walk(tree, n => n.type === 'img' && n.props.alt === 'VietQR Code')[0]?.props.src || null; },
    submitButton() { return button('handleCheckout(false)'); },
    async submit() { const b = button('handleCheckout(false)'); if (!b.props.disabled) await b.props.onClick(); render(); },
    seedBank(bank, tenant = user.tenantId, branch = props.branchId) { local.set(`pos_bank_config_v1:${encodeURIComponent(tenant)}:${encodeURIComponent(branch)}`, JSON.stringify({ version: 1, tenantId: tenant, branchId: branch, bankConfig: bank })); if (process.env.POS_QR_BASELINE) local.set('pos_bank_config', JSON.stringify(bank)); },
  };
}
module.exports = { world };
