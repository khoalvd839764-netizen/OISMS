const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const { pathToFileURL } = require('node:url');
const ts = require(path.resolve('src/frontend/pos-app/node_modules/typescript'));
const sourceFile = path.resolve('src/frontend/pos-app/test-b02-hub-transport.mjs');
const requests = [], connections = [], loadedModules = [];
let testBody;
const own = '11111111-0000-0000-0001-000000000001';
const foreign = '22222222-0000-0000-0001-000000000001';
const user = { id: 'fixture-cashier-A', tenantId: '11111111-1111-1111-1111-111111111111', fullName: 'Fixture A' };
const response = data => ({ ok: true, status: 200, json: async () => data });
async function fetchFixture(raw, init = {}) {
  const url = new URL(raw); requests.push({ url: url.href, method: init.method || 'GET' });
  if (url.pathname === '/health/live') return response({ status: 'healthy' });
  if (url.pathname === '/api/auth/login') {
    const login = JSON.parse(init.body);
    const isB = login.email.includes('shopb');
    return response({ accessToken: url.port === '5001' ? 'fixture-TEST-token' : 'fixture-DEMO-token', user: isB ? { ...user, id: 'fixture-B', tenantId: 'tenant-B' } : user });
  }
  if (url.pathname === '/api/branches') return response([{ id: foreign }]);
  throw new Error('Unexpected fixture request: ' + raw);
}
class Connection {
  constructor(url, options) { this.url = url; this.options = options; this.state = 'Disconnected'; this.handlers = {}; connections.push(this); }
  on(name, cb) { this.handlers[name] = cb; }
  onclose(cb) { this.closeHandler = cb; }
  onreconnecting(cb) { this.reconnecting = cb; }
  onreconnected(cb) { this.reconnected = cb; }
  async start() { this.state = 'Connected'; }
  async stop() { this.state = 'Disconnected'; this.closeHandler?.(); }
  async invoke(method, id) {
    if (method === 'JoinBranchGroup' && id === '00000000-0000-0000-0000-000000000000') throw new Error('Thiếu TenantId hoặc BranchId');
    if (method === 'JoinBranchGroup' && id === foreign) throw new Error('Chi nhánh không tồn tại, đã ngừng hoạt động hoặc không thuộc Tenant của bạn');
    if (method === 'JoinBranchGroup') assert.equal(id, own);
  }
}
class Builder {
  withUrl(url, options) { this.url = url; this.options = options; return this; }
  withAutomaticReconnect() { return this; }
  configureLogging() { return this; }
  build() { return new Connection(this.url, this.options); }
}
const signalR = { HubConnectionBuilder: Builder, HubConnectionState: { Connected: 'Connected', Disconnected: 'Disconnected', Connecting: 'Connecting', Reconnecting: 'Reconnecting' }, LogLevel: { Warning: 3 }, HttpTransportType: { WebSockets: 1 } };
const quiet = { log() {}, warn() {}, error() {} };
const vmSpy = { runInNewContext(js, ctx, options) {
  const result = vm.runInNewContext(js, ctx, options);
  loadedModules.push({ file: options.filename, injectedToken: ctx.localStorage?.getItem('pos_access_token'), embedsDemoBackend: js.includes('http://localhost:5000') });
  return result;
} };
const imports = {
  'node:test': (_title, callback) => { testBody = callback; },
  'node:assert/strict': assert,
  'node:module': { createRequire: () => name => { if (name.includes('signalr')) return signalR; throw new Error('Unexpected require: ' + name); } },
  'node:path': path, 'node:fs': fs, 'node:vm': vmSpy, typescript: ts,
};
// Rename only the runner's local ESM require binding to avoid shadowing the CJS import loader.
const source = fs.readFileSync(sourceFile, 'utf8')
  .replace('const require = createRequire(import.meta.url);', 'const localSdkRequire = createRequire(import.meta.url);')
  .replace("const signalR = require('./node_modules/@microsoft/signalr');", "const signalR = localSdkRequire('./node_modules/@microsoft/signalr');")
  .replaceAll('import.meta.url', JSON.stringify(pathToFileURL(sourceFile).href));
const js = ts.transpileModule(source, { fileName: 'runner.ts', compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, esModuleInterop: true } }).outputText;
vm.runInNewContext(js, { exports: {}, require: name => { if (!(name in imports)) throw new Error('Unexpected import ' + name); return imports[name]; }, console: quiet, process: { env: { POS_LIVE_TEST_ENV: '1', POS_TEST_BACKEND_URL: 'http://localhost:5001' } }, fetch: fetchFixture, Date, setTimeout, clearTimeout, URL, URLSearchParams, AbortController, Promise, Set, Map, Math, Intl, performance }, { filename: sourceFile });
(async () => {
  let error;
  try { await testBody({}); } catch (caught) { error = `${caught.name}: ${caught.message}`; }
  assert.match(error || '', /subscribeOrder is not a function/);
  assert.ok(requests.some(request => request.url === 'http://localhost:5001/api/auth/login'));
  assert.ok(loadedModules.slice(-2).every(module => module.injectedToken === 'fixture-DEMO-token' && module.embedsDemoBackend));
  const output = { provider: 'Unchanged live runner and production TS loaders, fixture HTTP + fake SDK; no external requests or DB writes', configuredTestHost: 'http://localhost:5001', error, requests, loadedModules, smokeConnections: connections.map(connection => ({ url: connection.url, token: connection.options.accessTokenFactory() })) };
  fs.writeFileSync(path.join(__dirname, 'live-runner-results.json'), JSON.stringify(output, null, 2) + '\n');
  console.log(JSON.stringify(output, null, 2));
})().catch(error => { console.error(error); process.exitCode = 1; });
