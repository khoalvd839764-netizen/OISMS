import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { randomUUID } from 'node:crypto';
import { ts, APP_URL, appRequire } from './test-support/app-path.mjs';
import { verifyLiveEnvironment, LIVE_URL } from './test-support/live-environment.mjs';
import { createNetworkProxy } from './test-support/network-proxy.mjs';

const require=appRequire;
const proof=await verifyLiveEnvironment(); // Must pass before any POST.
const report={date:'2026-11-08',database:proof.database,checks:[],status:'RUNNING'};
const record=(name,data={})=>{report.checks.push({name,status:'PASS',...data});console.log('PASS '+name);};
const proxy=await createNetworkProxy(LIVE_URL);
const password=process.env.POS_TEST_PASSWORD||'YourStrongDemoPassword123!';
let ownerToken;
async function api(route,body,token=ownerToken) {
  const response=await fetch(LIVE_URL+route,{method:body===undefined?'GET':'POST',headers:{'Content-Type':'application/json','Idempotency-Key':body?.requestKey||randomUUID(),...(token?{Authorization:'Bearer '+token}:{})},...(body===undefined?{}:{body:JSON.stringify(body)})});
  const data=await response.json();assert.ok(response.ok,`${route}: ${response.status} ${JSON.stringify(data)}`);return data;
}
function runtime() {
  const local=new Map(),listeners=new Map(),modules={};
  const storage={getItem:k=>local.get(k)??null,setItem:(k,v)=>local.set(k,String(v)),removeItem:k=>local.delete(k)};
  const context={console,Date,Intl,TextEncoder,Uint8Array,URL,URLSearchParams,Promise,Map,Set,Math,JSON,atob,crypto:globalThis.crypto,setTimeout,clearTimeout,fetch,localStorage:storage,sessionStorage:storage,
    CustomEvent:class {constructor(type){this.type=type;}},window:{dispatchEvent:e=>{for(const cb of listeners.get(e.type)||[])cb(e);},addEventListener:(type,cb)=>{if(!listeners.has(type))listeners.set(type,new Set());listeners.get(type).add(cb);}},__importMeta:{env:{VITE_API_BASE_URL:proxy.url}}};
  function load(name) {
    if(modules[name])return modules[name];
    const source=fs.readFileSync(new URL('./src/services/'+name+'.ts',APP_URL),'utf8').replace(/import\.meta/g,'__importMeta');
    const mod={exports:{}};
    vm.runInNewContext(ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,
      {...context,module:mod,exports:mod.exports,require:n=>n.startsWith('./')?load(n.slice(2)):require(n)});
    return modules[name]=mod.exports;
  }
  return {load,local};
}
const tabB=runtime(),session=tabB.load('posSession'),hub=tabB.load('realtimeService'),posApi=tabB.load('posApi'),stockModule=tabB.load('branchStock'),units=tabB.load('posCartUnits');
let coordinator;const catalog=[];let liveEvents=0,reloads=0,lastStatus='';
async function until(predicate,label,timeout=20000){const end=Date.now()+timeout;while(!predicate()){if(Date.now()>end)throw Error('Timeout: '+label);await new Promise(r=>setTimeout(r,80));}}
try {
  const owner=await api('/api/auth/login',{email:'owner@shopa.local',password},null);ownerToken=owner.accessToken;
  const branches=await api('/api/branches'),branch=branches[0];assert.ok(branch?.id);
  const warehouses=await api('/api/warehouses'),warehouse=warehouses.find(w=>w.branchId===branch.id);
  const products=await api('/api/products'),product=products[0],sku=product.variants[0],suppliers=await api('/api/suppliers');
  const po=await api('/api/purchase-orders',{branchId:branch.id,supplierId:suppliers[0].id,items:[{skuId:sku.id,quantity:30,unitPrice:100000}]});
  await api(`/api/purchase-orders/${po.id}/approve`,{});
  const receipt=await api('/api/goods-receipts',{purchaseOrderId:po.id,warehouseId:warehouse.id,items:[{purchaseOrderItemId:po.items[0].id,quantity:30}]});
  await api(`/api/goods-receipts/${receipt.id}/confirm`,{});
  const user=await session.loginPos('cashier@shopa.local',password);assert.ok(user.id&&user.tenantId);
  const me=await session.getPosMe();assert.equal(me.id,user.id);await session.refreshPosToken();assert.equal((await session.getPosMe()).id,user.id);record('Cashier login/refresh/me trên PostgreSQL thật');
  let cart=[{variantId:sku.id,quantity:1,factorToBase:1,available:0}];
  coordinator=stockModule.createBranchStock(branch.id,posApi.fetchPosCatalog,items=>{catalog.splice(0,catalog.length,...items);cart=stockModule.updateCartStock(cart,items);},error=>{throw error;});
  hub.subscribeStockLevelChanged(e=>{liveEvents++;coordinator.event(e);});hub.subscribeReconnected(()=>{reloads++;void coordinator.refresh();});hub.subscribeRealtimeStatus(s=>{lastStatus=s;});
  await coordinator.refresh();const initial=catalog.find(p=>p.variantId===sku.id)?.available;assert.ok(initial>=30);
  await hub.startRealtimeConnection(user,branch.id);assert.equal(lastStatus,'connected');
  const found=await posApi.searchPosProducts(sku.sku,branch.id),line=found.find(p=>p.variantId===sku.id);assert.ok(line);
  const unit=units.resolveCartUnit(line);assert.ok(unit);
  const payload={branchId:branch.id,channel:'POS',requestKey:randomUUID(),paymentMethod:'CASH',amountGiven:unit.price,changeDue:0,totalAmount:unit.price,items:[{skuId:sku.id,skuCode:sku.sku,productName:product.name,variantName:sku.variantName,quantity:1,unitId:unit.unit?.unitId,unitPrice:unit.price}]};
  const first=await api('/api/pos/checkout',payload);assert.ok(first.orderId&&first.receipt);
  await until(()=>catalog.find(p=>p.variantId===sku.id)?.available===initial-(unit.unit?.factor||1),'tab B inventory update');
  assert.equal(cart[0].available,initial-(unit.unit?.factor||1));record('Tab A checkout → service thật của tab B cập nhật catalog và giới hạn giỏ',{before:initial,after:cart[0].available,events:liveEvents});
  const replay=await api('/api/pos/checkout',payload);assert.equal(replay.orderId,first.orderId);
  const orderCount=Number(proof.query(`SELECT count(*) FROM orders WHERE request_key='${payload.requestKey}'`));assert.equal(orderCount,1);record('Idempotent replay chỉ một order trong PostgreSQL');
  assert.match(first.orderId,/^[0-9a-f-]{36}$/i);
  const paymentCount=Number(proof.query(`SELECT count(*) FROM payments WHERE order_id='${first.orderId}'`));
  const invoiceCount=Number(proof.query(`SELECT count(*) FROM sales_invoices WHERE order_id='${first.orderId}'`));
  const ledgerCount=Number(proof.query(`SELECT count(*) FROM inventory_transactions t JOIN order_items i ON i.id=t.order_item_id WHERE i.order_id='${first.orderId}' AND t.type='OUT'`));
  assert.equal(paymentCount,1);assert.equal(invoiceCount,1);assert.equal(ledgerCount,1);record('Replay giữ đúng một payment, invoice và ledger OUT',{paymentCount,invoiceCount,ledgerCount});
  const branchB=await api('/api/branches',{name:'POS migration '+randomUUID().slice(0,8),address:'Isolated fixture'});
  await hub.switchBranch(branchB.id);const beforeOther=liveEvents;
  await api('/api/pos/checkout',{...payload,requestKey:randomUUID()});await new Promise(r=>setTimeout(r,500));assert.equal(liveEvents,beforeOther);record('Đổi branch không nhận sự kiện branch cũ');
  await hub.switchBranch(branch.id);
  await coordinator.refresh();const beforeOffline=catalog.find(p=>p.variantId===sku.id).available;
  proxy.cut();await until(()=>lastStatus==='reconnecting'||lastStatus==='disconnected','physical disconnect');
  await api('/api/pos/checkout',{...payload,requestKey:randomUUID()});const reconnectBaseline=reloads;
  proxy.restore();await until(()=>reloads>reconnectBaseline&&lastStatus==='connected','real automatic reconnect',30000);
  await until(()=>catalog.find(p=>p.variantId===sku.id)?.available===beforeOffline-(unit.unit?.factor||1),'authoritative reconnect stock');record('Ngắt TCP thật → tự reconnect/rejoin/refetch đúng tồn');
  const beforeLogout=liveEvents;await hub.stopRealtimeConnection();await session.logoutPos();
  const other=await session.loginPos('owner@shopb.local',password);const otherBranches=await api('/api/branches',undefined,session.getPosAccessToken());await hub.startRealtimeConnection(other,otherBranches[0].id);
  await api('/api/pos/checkout',{...payload,requestKey:randomUUID()});await new Promise(r=>setTimeout(r,500));assert.equal(liveEvents,beforeLogout);record('Logout/login tenant B không nhận sự kiện tenant A');
  report.fixture={branchId:branch.id,otherBranchId:branchB.id,skuId:sku.id};report.status='PASS';
} catch(error) { report.status='FAIL';report.error=error.message;throw error; }
finally {
  coordinator?.dispose();await hub.stopRealtimeConnection();await proxy.close();
  const out=path.resolve('tmp/pos-migration-2026-11-08');fs.mkdirSync(out,{recursive:true});fs.writeFileSync(path.join(out,'live-report.json'),JSON.stringify(report,null,2));
}
