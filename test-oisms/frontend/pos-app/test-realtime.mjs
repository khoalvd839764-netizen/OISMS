import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import { ts, APP_URL } from './test-support/app-path.mjs';

function load(file, imports = {}) {
  const source=fs.readFileSync(new URL('./src/services/'+file,APP_URL),'utf8').replace(/import\.meta/g,'__importMeta');
  const mod={exports:{}};
  vm.runInNewContext(ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,
    {module:mod,exports:mod.exports,require:n=>imports[n],__importMeta:{env:{VITE_API_BASE_URL:'https://api.fixture.test'}},atob,Date,Promise,Map,Set,console});
  return mod.exports;
}
const deferred=()=>{let resolve,reject;const promise=new Promise((a,b)=>{resolve=a;reject=b;});return {promise,resolve,reject};};
const flush=()=>new Promise(r=>setImmediate(r));
const event=(branchId='A',timestamp='2026-11-08T05:00:00Z')=>({branchId,skuId:'sku',newAvailableStock:9,timestamp});

test('stock: GET trước sự kiện không ghi đè GET mới hơn',async()=>{
  const reads=[],commits=[]; const c=load('branchStock.ts').createBranchStock('A',()=>{const d=deferred();reads.push(d);return d.promise;},v=>commits.push(v),e=>{throw e;});
  const initial=c.refresh();c.event(event());reads[1].resolve([{available:9}]);await flush();reads[0].resolve([{available:20}]);await initial;
  assert.equal(commits.length,1);assert.equal(commits[0][0].available,9);
});
test('stock: sự kiện chi nhánh khác không kích hoạt đọc hoặc sửa dữ liệu',async()=>{
  let reads=0;const c=load('branchStock.ts').createBranchStock('A',async()=>{reads++;return [];},()=>{},()=>{});c.event(event('B'));await flush();assert.equal(reads,0);
});
test('stock: dispose khi đổi branch/phiên loại phản hồi cũ',async()=>{
  const d=deferred(),commits=[];const c=load('branchStock.ts').createBranchStock('A',()=>d.promise,v=>commits.push(v),()=>{});const p=c.refresh();c.dispose();d.resolve([{available:99}]);await p;assert.equal(commits.length,0);
});
test('stock: sự kiện muộn không thay số mới bằng payload cũ',async()=>{
  let reads=0;const values=[];const c=load('branchStock.ts').createBranchStock('A',async()=>{reads++;return [{available:8}];},v=>values.push(v),()=>{});
  c.event(event('A','2026-11-08T05:01:00Z'));await flush();c.event(event());await flush();assert.equal(reads,1);assert.equal(values[0][0].available,8);
});
test('stock: lỗi GET cũ không đè kết quả mới',async()=>{
  const reads=[],errors=[];const c=load('branchStock.ts').createBranchStock('A',()=>{const d=deferred();reads.push(d);return d.promise;},()=>{},e=>errors.push(e));const p=c.refresh();c.event(event());reads[1].resolve([]);await flush();reads[0].reject(Error('old'));await p;assert.equal(errors.length,0);
});
test('stock: reconnect/refetch giữ dòng giỏ, cập nhật tồn mọi quy cách',()=>{
  const m=load('branchStock.ts'),units=load('posCartUnits.ts');const cart=[{variantId:'sku',quantity:3,factorToBase:10,lineKey:'sku:bag',available:50},{variantId:'sku',quantity:2,factorToBase:1,lineKey:'sku:piece',available:50}];
  const next=m.updateCartStock(cart,[{variantId:'sku',available:12}]);assert.equal(next.length,2);assert.equal(next[0].quantity,3);assert.equal(next[1].available,12);assert.equal(units.maxQuantityForLine(next,next[0]),1);assert.equal(cart[0].available,50);
});

function world() {
  const conns=[];let authGeneration=0,token='fixture';
  class Conn {
    state='Disconnected';handlers={};calls=[];gate=null;
    on(n,cb){this.handlers[n]=cb;}onclose(cb){this.closed=cb;}onreconnecting(cb){this.reconnecting=cb;}onreconnected(cb){this.reconnected=cb;}
    async start(){this.state='Connecting';if(this.gate)await this.gate.promise;this.state='Connected';}
    async stop(){this.state='Disconnected';this.stopped=true;}
    async invoke(...args){this.calls.push(args);}
  }
  class Builder {withUrl(url,options){this.url=url;this.options=options;return this;}withAutomaticReconnect(){return this;}configureLogging(){return this;}build(){const c=new Conn();c.url=this.url;c.options=this.options;conns.push(c);return c;}}
  const api=load('realtimeService.ts',{'@microsoft/signalr':{HubConnectionBuilder:Builder,HubConnectionState:{Connected:'Connected'},HttpTransportType:{WebSockets:1,ServerSentEvents:2,LongPolling:4},LogLevel:{Warning:3}},'./posSession':{getPosAccessToken:()=>token,getPosGeneration:()=>authGeneration,refreshPosToken:async()=>{token='renewed';return token;}}});
  return {api,conns,changeAuth:()=>authGeneration++,setToken:v=>token=v};
}
const user={id:'user-a',tenantId:'tenant-a'};
test('hub: join đúng branch, chỉ phát StockLevelChanged đúng branch',async()=>{
  const w=world(),received=[];w.api.subscribeStockLevelChanged(v=>received.push(v));await w.api.startRealtimeConnection(user,'A');const c=w.conns[0];
  assert.ok(c.calls.some(v=>v[0]==='JoinBranchGroup'&&v[1]==='A'));c.handlers.StockLevelChanged(event('B'));c.handlers.StockLevelChanged(event());assert.equal(received.length,1);
});
test('hub: đổi branch leave/join và bỏ sự kiện branch cũ',async()=>{
  const w=world(),received=[];w.api.subscribeStockLevelChanged(v=>received.push(v));await w.api.startRealtimeConnection(user,'A');await w.api.startRealtimeConnection(user,'B');const c=w.conns[0];
  assert.equal(w.conns.length,1);assert.ok(c.calls.some(v=>v[0]==='LeaveBranchGroup'&&v[1]==='A'));c.handlers.StockLevelChanged(event());c.handlers.StockLevelChanged(event('B'));assert.equal(received.length,1);assert.equal(received[0].branchId,'B');
});
test('hub: logout stop và callback cũ không được dùng',async()=>{
  const w=world(),received=[];w.api.subscribeStockLevelChanged(v=>received.push(v));await w.api.startRealtimeConnection(user,'A');await w.api.stopRealtimeConnection();w.conns[0].handlers.StockLevelChanged(event());assert.equal(received.length,0);assert.ok(w.conns[0].stopped);
});
test('hub: đổi generation auth loại sự kiện tenant trước dù callback chưa unmount',async()=>{
  const w=world(),received=[];w.api.subscribeStockLevelChanged(v=>received.push(v));await w.api.startRealtimeConnection(user,'A');w.changeAuth();w.conns[0].handlers.StockLevelChanged(event());assert.equal(received.length,0);
});
test('hub: reconnect rejoin rồi thông báo tải lại tồn',async()=>{
  const w=world();let reloads=0;w.api.subscribeReconnected(()=>reloads++);await w.api.startRealtimeConnection(user,'A');const baseline=reloads;await w.conns[0].reconnected();assert.equal(reloads,baseline+1);assert.equal(w.conns[0].calls.filter(v=>v[0]==='JoinBranchGroup').length,2);
});
test('hub: phiên B không nhận callback từ connection A',async()=>{
  const w=world(),received=[];w.api.subscribeStockLevelChanged(v=>received.push(v));await w.api.startRealtimeConnection(user,'A');const old=w.conns[0];await w.api.startRealtimeConnection({id:'b',tenantId:'tenant-b'},'B');old.handlers.StockLevelChanged(event('B'));assert.equal(received.length,0);assert.ok(old.stopped);
});
test('hub: payload thiếu timestamp/sai số tồn bị bỏ qua',async()=>{
  const w=world(),received=[];w.api.subscribeStockLevelChanged(v=>received.push(v));await w.api.startRealtimeConnection(user,'A');const c=w.conns[0];c.handlers.StockLevelChanged({...event(),timestamp:''});c.handlers.StockLevelChanged({...event(),newAvailableStock:-1});assert.equal(received.length,0);
});
test('hub: token hết hạn dùng refresh của phiên POS mới',async()=>{
  const w=world();w.setToken('x.'+btoa(JSON.stringify({exp:1}))+'.x');await w.api.startRealtimeConnection(user,'A');assert.equal(await w.conns[0].options.accessTokenFactory(),'renewed');assert.equal(w.conns[0].options.transport,7);
});
