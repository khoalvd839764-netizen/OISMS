import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';

const response = (status, value = {}) => ({ status, ok: status >= 200 && status < 300, json: async () => value });
function fixture(fetchImpl = async () => response(200)) {
  const local = new Map(), events = [], calls = [];
  const source = fs.readFileSync(new URL('./src/services/posSession.ts', import.meta.url), 'utf8').replace(/import\.meta/g, '__importMeta');
  const mod = { exports: {} };
  const context = { module: mod, exports: mod.exports, __importMeta: { env: { VITE_API_BASE_URL: 'https://api.fixture.test' } },
    localStorage: { getItem: k => local.get(k) ?? null, setItem: (k,v) => local.set(k,String(v)), removeItem: k => local.delete(k) },
    window: { dispatchEvent: e => events.push(e.type) }, CustomEvent: class { constructor(type) { this.type = type; } },
    fetch: async (url, init) => { calls.push({ url, init }); return fetchImpl(url, init); } };
  vm.runInNewContext(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 } }).outputText, context);
  return { api: mod.exports, local, calls, events };
}
const user = { id: 'a', tenantId: 'tenant-a', roles: ['Cashier'] };
const deferred = () => { let resolve; const promise = new Promise(r => { resolve = r; }); return { promise, resolve }; };

test('auth: không đọc token của app tím hay Admin', () => {
  const f = fixture(); f.local.set('pos_access_token','old'); f.local.set('oism_access_token','admin');
  assert.equal(f.api.getPosAccessToken(), null);
});
test('auth: login ẩn danh không đính token cũ; lưu khóa POS v2', async () => {
  const f = fixture(async () => response(200, { accessToken: 'new', refreshToken: 'refresh', user }));
  await f.api.loginPos('cashier@fixture.test','secret');
  assert.equal(f.calls[0].init.headers.Authorization, undefined);
  assert.equal(f.api.getPosAccessToken(), 'new'); assert.equal(f.local.get('oisms_pos_v2_refresh'),'refresh');
});
test('auth: login không có tenant hoặc sai quyền bị từ chối', async () => {
  for (const invalid of [{ id:'a',roles:['Owner'] }, { ...user, roles:['Guest'] }]) {
    const f=fixture(async()=>response(200,{ accessToken:'x',user:invalid }));
    await assert.rejects(f.api.loginPos('a','b')); assert.equal(f.api.getPosAccessToken(),null);
  }
});
test('auth: nhiều request 401 chỉ refresh một lần, giữ nguyên body checkout', async () => {
  let refreshes=0; const gate=deferred();
  const f=fixture(async(url,init)=> {
    if(url.endsWith('/refresh-token')) { refreshes++; await gate.promise; return response(200,{accessToken:'new',refreshToken:'r2'}); }
    return response(init.headers.Authorization==='Bearer new'?200:401);
  });
  f.api.savePosSession({accessToken:'old',refreshToken:'r',user},f.api.getPosGeneration());
  const init={method:'POST',body:JSON.stringify({requestKey:'same-key',items:[1]})};
  const a=f.api.authenticatedFetch('https://api.fixture.test/api/pos/checkout',init);
  const b=f.api.authenticatedFetch('https://api.fixture.test/api/branches');
  await new Promise(r=>setImmediate(r)); gate.resolve(); await Promise.all([a,b]);
  assert.equal(refreshes,1);
  const sends=f.calls.filter(c=>c.url.endsWith('/checkout')); assert.equal(sends.length,2); assert.equal(sends[0].init.body,sends[1].init.body);
});
test('auth: refresh trả muộn sau logout không phục hồi token', async () => {
  const gate=deferred(); const f=fixture(async()=>gate.promise);
  f.api.savePosSession({accessToken:'old',refreshToken:'r',user},0);
  const pending=f.api.refreshPosToken(); f.api.clearPosSession(); gate.resolve(response(200,{accessToken:'late',refreshToken:'late-r'}));
  await assert.rejects(pending); assert.equal(f.api.getPosAccessToken(),null);
});
test('auth: refresh phiên A không xóa hoặc ghi đè phiên B', async () => {
  const gate=deferred(); const f=fixture(async()=>gate.promise);
  f.api.savePosSession({accessToken:'A',refreshToken:'rA',user},0);
  const pending=f.api.refreshPosToken(); const g=f.api.clearPosSession();
  f.api.savePosSession({accessToken:'B',refreshToken:'rB',user:{tenantId:'tenant-b'}},g);
  gate.resolve(response(200,{accessToken:'late-A'})); await assert.rejects(pending);
  assert.equal(f.api.getPosAccessToken(),'B'); assert.equal(f.local.get('oisms_pos_v2_tenant'),'tenant-b');
});
test('auth: 401 thiếu refresh kết thúc phiên, không xóa pending checkout', async () => {
  const f=fixture(async()=>response(401)); f.local.set('oisms_pos_v2_access','expired'); f.local.set('pos_pending_checkout_v1','keep');
  await assert.rejects(f.api.authenticatedFetch('/api/branches'));
  assert.equal(f.api.getPosAccessToken(),null); assert.equal(f.local.get('pos_pending_checkout_v1'),'keep'); assert.ok(f.events.includes('pos-auth-expired'));
});
test('auth: request của phiên cũ không trả dữ liệu cho phiên mới', async () => {
  const gate=deferred(),f=fixture(async()=>gate.promise); f.local.set('oisms_pos_v2_access','A');
  const pending=f.api.authenticatedFetch('/api/branches'); f.api.clearPosSession();
  gate.resolve(response(200,[{secret:'tenant-a'}])); await assert.rejects(pending);
});
test('auth: logout xóa token ngay cả khi API revoke lỗi', async () => {
  const f=fixture(async()=>{ throw Error('offline'); }); f.api.savePosSession({accessToken:'a',refreshToken:'r',user},0);
  await f.api.logoutPos(); assert.equal(f.api.getPosAccessToken(),null); assert.ok(f.events.includes('pos-session-ended'));
});
test('auth: login A trả muộn không ghi đè login B', async () => {
  const gate=deferred(); let count=0;
  const f=fixture(async()=>++count===1?gate.promise:response(200,{accessToken:'B',user}));
  const a=f.api.loginPos('A','pw'); await f.api.loginPos('B','pw');
  gate.resolve(response(200,{accessToken:'A',user})); await assert.rejects(a); assert.equal(f.api.getPosAccessToken(),'B');
});
test('auth: đổi định danh ở tab khác tăng generation và loại response cũ', async () => {
  const gate=deferred(),f=fixture(async()=>gate.promise);f.api.savePosSession({accessToken:'A',user},0);
  const pending=f.api.authenticatedFetch('/api/branches');f.local.set('oisms_pos_v2_identity','tenant-b:user-b');f.local.set('oisms_pos_v2_access','B');
  gate.resolve(response(200,{private:'A'}));await assert.rejects(pending);assert.equal(f.api.getPosAccessToken(),'B');
});
test('auth: tab khác đã refresh cùng user thì không xóa token mới', async () => {
  const gate=deferred(),f=fixture(async()=>gate.promise);f.api.savePosSession({accessToken:'old',refreshToken:'r1',user},0);
  const pending=f.api.refreshPosToken();f.local.set('oisms_pos_v2_access','rotated');f.local.set('oisms_pos_v2_refresh','r2');
  gate.resolve(response(401));assert.equal(await pending,'rotated');assert.equal(f.api.getPosAccessToken(),'rotated');
});
test('auth: khôi phục phiên v2 sẵn có bổ sung định danh từ me đã xác thực',async()=>{
  const f=fixture(async()=>response(200,user));f.local.set('oisms_pos_v2_access','existing');
  assert.equal((await f.api.getPosMe()).id,user.id);assert.equal(f.local.get('oisms_pos_v2_identity'),'tenant-a:a');
});
