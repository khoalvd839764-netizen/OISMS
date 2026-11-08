const fs = require('node:fs'), path = require('node:path'), vm = require('node:vm');
const { execFileSync } = require('node:child_process');
const ts = require('typescript');
const root = path.resolve(__dirname, '../../../..');
const walk = (n, pred) => Array.isArray(n) ? n.flatMap(x => walk(x, pred)) : !n || typeof n !== 'object' ? [] : [...(pred(n) ? [n] : []), ...walk(n.props?.children, pred)];
const ACCOUNTS_PATH = '/api/payment-accounts/active';
const tick = () => new Promise(resolve => setImmediate(resolve));

/** Chuyển cấu hình ngân hàng fixture thành một dòng trả về từ GET /api/payment-accounts/active. */
function toServerAccount(bank, id, isDefault = false) {
  return { id, bankBin: bank.bankBin, bankName: bank.bankName, bankShortName: bank.bankShortName || bank.bankName, accountNumber: bank.accountNumber, accountName: bank.accountName, isDefault };
}

/**
 * Môi trường chạy PaymentModal của POS PWA không cần trình duyệt.
 * Tài khoản nhận tiền chỉ đến từ API giả (`serverAccounts`), không từ localStorage hay window.
 */
function world(options = {}) {
  const local = options.local || new Map(), session = options.session || new Map();
  const storage = m => ({ getItem: k => m.get(k) ?? null, setItem: (k, v) => m.set(k, v), removeItem: k => m.delete(k) });
  const user = options.user || { id: 'user-a', tenantId: 'tenant-a', fullName: 'Fixture A', email: 'a@fixture.test' };
  local.set('pos_access_token', 'fixture-token');
  const server = options.server || { accounts: [], accountsResponse: null };
  const requests = [], accountRequests = []; let id = 0;
  const globals = { console: { log() {}, warn() {}, error() {} }, TextEncoder, Uint8Array, Date, URL, URLSearchParams, AbortController, Promise, Map, Set, Math, Intl, performance, setTimeout, clearTimeout,
    __importMeta: { env: { PROD: true, DEV: false, VITE_API_BASE_URL: 'https://api.fixture.test/' } }, localStorage: storage(local), sessionStorage: storage(session),
    window: { sessionStorage: storage(session), localStorage: storage(local), dispatchEvent() {}, confirm: () => true }, CustomEvent: class {},
    crypto: { randomUUID: () => `${String(++id).padStart(8, '0')}-0000-4000-8000-000000000001` },
    fetch: async (url, init) => {
      if (String(url).includes(ACCOUNTS_PATH)) {
        accountRequests.push({ url, headers: init?.headers || {} });
        if (server.accountsResponse) return server.accountsResponse(url, init);
        return { ok: true, status: 200, json: async () => JSON.parse(JSON.stringify(server.accounts)) };
      }
      requests.push({ url, payload: init?.body ? JSON.parse(init.body) : null });
      return options.fetch ? options.fetch(url, init) : { ok: false, status: 500, json: async () => ({ detail: 'Fixture response lost after possible commit' }) };
    },
  };
  function load(relative, imports = {}) {
    const file = 'src/frontend/pos-app/' + relative;
    const source = (process.env.POS_QR_BASELINE ? execFileSync('git', ['show', `${process.env.POS_QR_BASELINE}:${file}`], { cwd: root, encoding: 'utf8' }) : fs.readFileSync(path.join(root, file), 'utf8')).replace(/import\.meta/g, '__importMeta');
    const js = ts.transpileModule(source, { fileName: file, compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX } }).outputText;
    const mod = { exports: {} }; vm.runInNewContext(js, { ...globals, module: mod, exports: mod.exports, require: n => { if (!(n in imports)) throw Error('Unexpected import ' + n); return imports[n]; } }, { filename: file }); return mod.exports;
  }
  const api = load('src/services/api.ts'), qr = load('src/services/qrService.ts');
  const accountSource = load('src/services/paymentAccountSource.ts', { './api': api, './qrService': qr });
  let cursor = 0, dirty = false; const state = [], effects = [], cleanups = [];
  const react = {
    useState(initial) { const i = cursor++; if (!(i in state)) state[i] = typeof initial === 'function' ? initial() : initial; return [state[i], value => { const next = typeof value === 'function' ? value(state[i]) : value; if (!Object.is(next, state[i])) { state[i] = next; dirty = true; } }]; },
    useEffect(cb, deps) { const i = cursor++, old = state[i]; if (!old || deps.some((x, j) => !Object.is(x, old[j]))) { state[i] = deps; effects.push({ i, cb }); } },
  };
  const jsx = (type, props) => ({ type, props });
  const component = load('src/components/PaymentModal.tsx', { react, 'react/jsx-runtime': { jsx, jsxs: jsx }, 'lucide-react': {}, '../services/api': api, '../services/qrService': qr, '../services/paymentAccountSource': accountSource }).PaymentModal;
  const props = { isOpen: true, onClose() { props.isOpen = false; }, branchId: 'branch-a', branchName: 'Branch A', tenantName: 'Tenant A', cashierName: 'Cashier A', currentUser: user,
    items: [{ variantId: 'sku-a', sku: 'A', productName: 'A', retailPrice: 100000, quantity: 1 }], onSuccessAndPrint() {}, onPreviewReceipt() {} };
  let tree;
  function render() {
    for (let n = 0; n < 20; n++) {
      dirty = false; cursor = 0; tree = component(props);
      effects.splice(0).forEach(({ i, cb }) => { if (typeof cleanups[i] === 'function') cleanups[i](); cleanups[i] = cb(); });
      if (!dirty) return tree;
    }
    throw Error('Render did not settle');
  }
  /** Render rồi chờ các lời gọi API bất đồng bộ (tải tài khoản) xong. */
  async function settle() { render(); for (let n = 0; n < 10; n++) { await tick(); if (!dirty) break; render(); } return tree; }
  const button = text => walk(tree, n => n.type === 'button' && String(n.props.onClick).includes(text))[0];
  return { api, qr, props, user, local, session, globals, requests, accountRequests, server, render, settle,
    async selectQR() { render(); button("setPaymentMethod('BANK_TRANSFER_QR')").props.onClick(); await settle(); },
    async closeOpen() { props.isOpen = false; render(); props.isOpen = true; await settle(); },
    image() { return walk(tree, n => n.type === 'img' && n.props.alt === 'VietQR Code')[0]?.props.src || null; },
    accountSelect() { return walk(tree, n => n.type === 'select')[0] || null; },
    reloadAccountsButton() { return button('setAccountsReloadToken'); },
    submitButton() { return button('handleCheckout(false)'); },
    async submit() { const b = button('handleCheckout(false)'); if (!b.props.disabled) await b.props.onClick(); await settle(); },
    /** Thêm tài khoản vào danh sách server trả về. */
    seedBank(bank, accountId = `0000000${server.accounts.length + 1}-aaaa-4bbb-8ccc-000000000001`, isDefault = server.accounts.length === 0) { server.accounts.push(toServerAccount(bank, accountId, isDefault)); return accountId; },
    setServerAccounts(list) { server.accounts.splice(0, server.accounts.length, ...list); },
  };
}
module.exports = { world, toServerAccount, ACCOUNTS_PATH };
