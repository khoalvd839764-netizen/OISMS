/**
 * Môi trường chạy PaymentModal của POS độc lập (pos-app) không cần trình duyệt.
 * Dùng để kiểm tra khóa idempotency (BF-003) và nguồn tài khoản nhận tiền chỉ từ API (CS-05).
 */
const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const ts = require('typescript');
const root = path.resolve(__dirname, '../../../..');
const walk = (n, pred) => Array.isArray(n) ? n.flatMap(x => walk(x, pred)) : !n || typeof n !== 'object' ? [] : [...(pred(n) ? [n] : []), ...walk(n.props?.children, pred)];
const ACCOUNTS_PATH = '/api/payment-accounts/active';
const tick = () => new Promise(resolve => setImmediate(resolve));

function world(options = {}) {
  const local = options.local || new Map(), session = options.session || new Map();
  const storage = m => ({ getItem: k => m.get(k) ?? null, setItem: (k, v) => m.set(k, String(v)), removeItem: k => m.delete(k) });
  const user = options.user || { id: 'user-a', tenantId: 'tenant-a', fullName: 'Owner A', email: 'a@fixture.test', tenantName: 'Tenant A' };
  local.set('oisms_pos_v2_access', 'fixture-token');
  const server = options.server || { accounts: [], checkout: null };
  const requests = [], accountRequests = []; let id = 0;
  const globals = { console: { log() {}, warn() {}, error() {} }, TextEncoder, Uint8Array, Date, URL, URLSearchParams, Promise, Map, Set, Math, Intl, JSON, setTimeout, clearTimeout,
    __importMeta: { env: { VITE_API_BASE_URL: 'https://api.fixture.test' } }, localStorage: storage(local), sessionStorage: storage(session),
    window: { sessionStorage: storage(session), localStorage: storage(local), dispatchEvent() {}, confirm: () => true }, CustomEvent: class {},
    crypto: { randomUUID: () => `${String(++id).padStart(8, '0')}-0000-4000-8000-00000000000a` },
    fetch: async (url, init) => {
      if (String(url).includes(ACCOUNTS_PATH)) {
        accountRequests.push({ url, headers: init?.headers || {} });
        if (server.accountsResponse) return server.accountsResponse(url, init);
        return { ok: true, status: 200, json: async () => JSON.parse(JSON.stringify(server.accounts)) };
      }
      const payload = init?.body ? JSON.parse(init.body) : null;
      requests.push({ url, payload });
      if (server.checkout) return server.checkout(payload, requests.length);
      throw new TypeError('Failed to fetch');
    },
  };
  function load(relative, imports = {}) {
    const file = 'src/frontend/pos-app/src/' + relative;
    const source = fs.readFileSync(path.join(root, file), 'utf8').replace(/import\.meta/g, '__importMeta');
    const js = ts.transpileModule(source, { fileName: file, compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX } }).outputText;
    const mod = { exports: {} };
    vm.runInNewContext(js, { ...globals, module: mod, exports: mod.exports, require: n => { if (!(n in imports)) throw Error('Unexpected import ' + n); return imports[n]; } }, { filename: file });
    return mod.exports;
  }
  const auth = load('services/posSession.ts');
  const receiptMapper = load('services/receiptMapper.ts');
  const posApi = load('services/posApi.ts', { './posSession': auth, './receiptMapper': receiptMapper }), qr = load('services/qrService.ts'), store = load('services/pendingCheckoutStore.ts');
  const accountSource = load('services/paymentAccountSource.ts', { './posApi': posApi, './qrService': qr, './posSession': auth });
  let cursor = 0, dirty = false; const state = [], effects = [], cleanups = [];
  const react = {
    useState(initial) { const i = cursor++; if (!(i in state)) state[i] = typeof initial === 'function' ? initial() : initial; return [state[i], value => { const next = typeof value === 'function' ? value(state[i]) : value; if (!Object.is(next, state[i])) { state[i] = next; dirty = true; } }]; },
    useEffect(cb, deps) { const i = cursor++, old = state[i]; if (!old || deps.some((x, j) => !Object.is(x, old[j]))) { state[i] = deps; effects.push({ i, cb }); } },
    useRef(initial) { const i = cursor++; if (!(i in state)) state[i] = { current: initial }; return state[i]; },
  };
  const jsx = (type, props) => ({ type, props });
  const component = load('components/pos/PaymentModal.tsx', {
    react, 'react/jsx-runtime': { jsx, jsxs: jsx }, 'lucide-react': {},
    '../../services/posApi': posApi, '../../services/qrService': qr, '../../services/paymentAccountSource': accountSource, '../../services/pendingCheckoutStore': store, '../../services/receiptMapper': receiptMapper,
  }).PaymentModal;
  const successes = [];
  const props = { isOpen: true, embedded: false, onClose() { props.isOpen = false; }, branchId: 'branch-a', branchName: 'Branch A', tenantName: 'Tenant A', cashierName: 'Owner A', currentUser: user,
    items: [{ variantId: 'sku-a', sku: 'A', productName: 'A', retailPrice: 100000, quantity: 1 }], onSuccessAndPrint(r) { successes.push(r); }, onPreviewReceipt() {} };
  let tree;
  function render() {
    for (let n = 0; n < 20; n++) {
      dirty = false; cursor = 0; tree = component(props);
      effects.splice(0).forEach(({ i, cb }) => { if (typeof cleanups[i] === 'function') cleanups[i](); cleanups[i] = cb(); });
      if (!dirty) return tree;
    }
    throw Error('Render did not settle');
  }
  async function settle() { render(); for (let n = 0; n < 10; n++) { await tick(); if (!dirty) break; render(); } return tree; }
  const button = text => walk(tree, n => n.type === 'button' && String(n.props.onClick).includes(text))[0];
  return { api: { ...posApi, ...store }, auth, posApi, qr, store, globals, props, user, local, session, server, requests, accountRequests, successes, render, settle,
    async selectQR() { render(); button("selectMethod('BANK_TRANSFER_QR')").props.onClick(); await settle(); },
    async closeOpen() { props.isOpen = false; render(); props.isOpen = true; await settle(); },
    accountSelect() { return walk(tree, n => n.type === 'select')[0] || null; },
    reloadAccountsButton() { return button('setAccountsReloadToken'); },
    seedBank(bank, accountId = '00000001-aaaa-4bbb-8ccc-000000000001', isDefault = server.accounts.length === 0) { server.accounts.push({ id: accountId, ...bank, bankShortName: bank.bankShortName || bank.bankName, isDefault }); return accountId; },
    setServerAccounts(list) { server.accounts.splice(0, server.accounts.length, ...list); },
    image() { return walk(tree, n => n.type === 'img' && n.props.alt === 'VietQR Code')[0]?.props.src || null; },
    submitButton() { return button('handleCheckout(false)'); },
    async submit() { const b = button('handleCheckout(false)'); if (!b.props.disabled) await b.props.onClick(); await settle(); },
  };
}
module.exports = { world };
