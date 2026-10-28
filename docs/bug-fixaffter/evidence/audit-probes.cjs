// Read-only probes of the current source. No requests reach the live API or database.
// A reproduced defect is recorded as REPRODUCED; process success means the probe ran, not product PASS.
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),assert=require('node:assert/strict');
const repo=path.resolve(__dirname,'../../..'),admin=path.join(repo,'src/frontend/admin-dashboard');
module.paths.unshift(path.join(repo,'scratch/node_modules'),path.join(admin,'node_modules'));
const ts=require('typescript'),React=require('react'),{JSDOM}=require('jsdom');
const dom=new JSDOM('<html><body></body></html>',{url:'http://localhost:5173/inventory'});
for(const key of ['window','document','navigator','localStorage','sessionStorage','HTMLElement','HTMLInputElement','HTMLSelectElement','CustomEvent','Event']) {
  Object.defineProperty(global,key,{configurable:true,value:key==='window'?dom.window:key==='document'?dom.window.document:dom.window[key]});
}
global.React=React;global.IS_REACT_ACT_ENVIRONMENT=true;global.alert=window.alert=()=>{};global.confirm=window.confirm=()=>true;
const results=[];
function record(id,scenario,observed){results.push({id,status:'REPRODUCED',scenario,observed});}
function loadVm(relative,imports={},environment={}){
  const file=path.join(admin,relative),src=fs.readFileSync(file,'utf8').replaceAll('import.meta.env.VITE_API_BASE_URL','undefined');
  const code=ts.transpileModule(src,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText;
  const m={exports:{}};vm.runInNewContext(code,{module:m,exports:m.exports,console,localStorage,sessionStorage,window,CustomEvent,setTimeout,clearTimeout,...environment,require:n=>{if(!(n in imports))throw Error('Missing dependency '+n);return imports[n];}},{filename:file});return m.exports;
}
function mock(relative,exports){const file=path.join(admin,'src',relative);require.cache[file]={id:file,filename:file,loaded:true,exports};}
for(const ext of ['.ts','.tsx'])require.extensions[ext]=(m,file)=>{
  m.paths.unshift(path.join(admin,'node_modules'),path.join(repo,'scratch/node_modules'));
  const src=fs.readFileSync(file,'utf8').replaceAll('import.meta.env.VITE_API_BASE_URL','undefined');
  m._compile(ts.transpileModule(src,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.ReactJSX,esModuleInterop:true}}).outputText,file);
};
require.extensions['.css']=()=>{};
const api={generateUuid:()=>require('node:crypto').randomUUID(),getBranches:async()=>({data:['A','B','C'].map(id=>({id,name:'Branch '+id}))}),getWarehouses:async()=>({data:[]}),getSkus:async()=>({data:[]})};
let actor={user:{id:'owner',tenantId:'tenant-A',role:'Owner',name:'Owner'},account:{id:'owner',tenantId:'tenant-A',role:'Owner'},logout:async()=>{}};
mock('context/AuthContext.tsx',{useAuth:()=>actor});mock('services/apiAdapter.ts',{apiAdapter:api,generateUuid:api.generateUuid});
const {act}=React,{createRoot}=require('react-dom/client');let root,container;
async function mount(Component,props={}){document.body.innerHTML='<div id="root"></div>';container=document.getElementById('root');root=createRoot(container);await act(async()=>root.render(React.createElement(Component,props)));}
async function render(Component,props){await act(async()=>root.render(React.createElement(Component,props)));}
async function cleanup(){await act(async()=>root.unmount());}
function button(text){const b=[...container.querySelectorAll('button')].find(b=>b.textContent.includes(text));assert.ok(b,'Missing '+text);return b;}
async function click(text){const b=button(text);assert.equal(b.disabled,false,text);await act(async()=>b.click());}
async function change(el,value){assert.ok(el);await act(async()=>{const proto=el.tagName==='SELECT'?dom.window.HTMLSelectElement.prototype:dom.window.HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(el,value);el.dispatchEvent(new dom.window.Event(el.tagName==='SELECT'?'change':'input',{bubbles:true}));});}
function pnl(n){return{totalNetRevenue:n,totalCostOfGoodsSold:0,totalGrossProfit:n,grossMarginPercentage:100,totalOrders:1,totalItemsSold:1,byChannel:[],byBranch:[],dailyTrend:[]};}
const count=(id='count-A',quantity=10)=>({id,number:id,status:'Counting',branchId:'branch-A',warehouseId:'wh-A',createdAt:'2026-10-28T12:00:00Z',items:[{id:'line-'+id,skuId:'sku-A',skuCode:'SKU-A',productName:'Fixture',expectedQuantity:8,countedQuantity:quantity}]});

(async()=>{
  // Retry identity at the actual service boundary.
  localStorage.setItem('oism_tenant_id','fixture-tenant');
  for(const kind of ['transfer','count']){
    const keys=[];let calls=0;
    const http={post:async(url,payload)=>{keys.push(payload.requestKey);if(++calls===1)throw Error('Lost response after commit');return{data:{id:'fixture-id',number:'FIXTURE-01',status:'Draft',items:[]}};}};
    const module=loadVm('src/services/'+(kind==='transfer'?'stockTransferService':'stockCountService')+'.ts',{'./httpClient':{httpClient:http},'./apiAdapter':{generateUuid:api.generateUuid}});
    const invoke=()=>kind==='transfer'?module.stockTransferService.createTransfer({fromBranchId:'A',toBranchId:'B',fromWarehouseId:'WA',toWarehouseId:'WB',items:[{skuId:'sku-A',quantity:1}]}):module.stockCountService.createStockCount({branchId:'A',warehouseId:'WA'});
    await invoke().catch(()=>{});await invoke();assert.notEqual(keys[0],keys[1]);record('retry-'+kind,'Same payload retried after an unknown result',{sameKey:keys[0]===keys[1],requestCount:keys.length});
  }
  // An in-flight refresh belonging to A must never overwrite a newly signed-in B.
  let releaseRefresh;
  const client={defaults:{headers:{common:{}}},interceptors:{request:{use(){}},response:{use(){}}}};
  const axios={create:()=>client,post:()=>new Promise(resolve=>releaseRefresh=resolve)};
  const authClient=loadVm('src/services/httpClient.ts',{axios:{__esModule:true,default:axios}});
  localStorage.setItem('oism_access_token','fixture-token-A');localStorage.setItem('oism_refresh_token','fixture-refresh-A');
  const pending=authClient.refreshAccessToken();localStorage.setItem('oism_access_token','fixture-token-B');localStorage.setItem('oism_refresh_token','fixture-refresh-B');
  releaseRefresh({data:{accessToken:'fixture-refreshed-A',refreshToken:'fixture-next-refresh-A'}});await pending;
  assert.equal(localStorage.getItem('oism_access_token'),'fixture-refreshed-A');record('auth-refresh-owner','Login B while token refresh A is pending',{expectedOwner:'B',storedTokenOwner:'A'});
  let rejectResponse;const replays=[];const retryClient=cfg=>{replays.push({...cfg,headers:{...cfg.headers}});return Promise.resolve({data:{}});};
  retryClient.defaults={headers:{common:{}}};retryClient.interceptors={request:{use(){}},response:{use(ok,reject){rejectResponse=reject;}}};
  loadVm('src/services/httpClient.ts',{axios:{__esModule:true,default:{create:()=>retryClient}}});
  localStorage.setItem('oism_access_token','fixture-token-B');
  await rejectResponse({config:{url:'/api/brands',method:'post',data:{name:'Created by A'},headers:{Authorization:'Bearer fixture-token-A'}},response:{status:401}});
  assert.equal(replays[0].headers.Authorization,'Bearer fixture-token-B');record('auth-old-401-replay','An old mutation sent by A returns 401 after B signs in',{originalIntentOwner:'A',replayedWithOwner:'B',mutationReplayed:true});
  // A live transport cannot be reused merely because its connection state is Connected.
  const connections=[];
  class Conn{constructor(options){this.options=options;this.state='Disconnected';this.handlers={};connections.push(this);}on(n,cb){this.handlers[n]=cb;}onreconnecting(){}onreconnected(){}onclose(){}async start(){this.owner=this.options.accessTokenFactory();this.state='Connected';}async stop(){this.state='Disconnected';}async invoke(){}}
  class Builder{withUrl(url,options){this.options=options;return this;}withAutomaticReconnect(){return this;}configureLogging(){return this;}build(){return new Conn(this.options);}}
  const realtime=loadVm('src/services/realtimeAdapter.ts',{'@microsoft/signalr':{HubConnectionBuilder:Builder,HubConnectionState:{Connected:'Connected',Disconnected:'Disconnected'},LogLevel:{Warning:3}}}).realtimeAdapter;
  localStorage.setItem('oism_access_token','fixture-token-A');await realtime.start();localStorage.setItem('oism_access_token','fixture-token-B');await realtime.start();const events=[];realtime.subscribe(e=>events.push(e.orderCode));connections[0].handlers.OrderCreated({orderId:'fixture-A',orderNumber:'PRIVATE-A'});
  assert.deepEqual(events,['PRIVATE-A']);record('realtime-owner','A connected transport remains active when B subscribes',{connections:connections.length,transportOwner:'A',activeOwner:'B',oldOwnerEventDelivered:true});

  let getReport=async()=>pnl(0);const filters=[];
  mock('services/reportService.ts',{reportService:{getRevenueProfit:filter=>{filters.push({...filter});return getReport(filter);},getInventoryVelocity:async()=>({bestSellers:[],slowMovingItems:[]}),getStockAlerts:async()=>[]}});
  const {ReportsPage}=require(path.join(admin,'src/pages/ReportsPage.tsx'));
  const NativeDate=global.Date;global.Date=class extends NativeDate{constructor(...args){super(...(args.length?args:['2026-10-28T18:00:00Z']));}static now(){return NativeDate.parse('2026-10-28T18:00:00Z');}};
  await mount(ReportsPage);
  try{await change([...container.querySelectorAll('select')].find(s=>[...s.options].some(o=>o.value==='today')),'today');assert.equal(filters.at(-1).startDate,'2026-10-28T00:00:00Z');record('report-today-vietnam','At 01:00 on 29/10 in Vietnam, choose Today',{clientDateVietnam:'2026-10-29',requestedCalendarDate:'2026-10-28'});}finally{await cleanup();global.Date=NativeDate;}
  await mount(ReportsPage);
  try{
    const preset=[...container.querySelectorAll('select')].find(s=>[...s.options].some(o=>o.value==='custom'));
    await change(preset,'custom');const dates=container.querySelectorAll('input[type=date]');await change(dates[0],'2026-10-28');await change(dates[1],'2026-10-28');
    const transmitted=filters.at(-1).startDate;assert.equal(transmitted,'2026-10-28T00:00:00Z');record('report-timezone','Choose 28/10/2026 as a Vietnamese business date',{transmittedStart:transmitted,expectedVietnamMidnight:'2026-10-27T17:00:00.000Z',shiftHours:7});
    let resolveA,resolveB;getReport=f=>new Promise(resolve=>{if(f.branchId==='A')resolveA=resolve;if(f.branchId==='B')resolveB=resolve;});
    const branch=[...container.querySelectorAll('select')].find(s=>[...s.options].some(o=>o.value==='A'));
    const channel=[...container.querySelectorAll('select')].find(s=>[...s.options].some(o=>o.value==='Web'));
    assert.ok(channel);assert.equal([...channel.options].some(o=>o.value==='Admin'),false);
    record('report-admin-channel','Report channel picker omits the Admin channel used by manual orders',{adminOptionPresent:false,unsupportedWebOptionPresent:true});
    await change(branch,'A');await change(branch,'B');await act(async()=>resolveB(pnl(9900)));assert.ok(container.textContent.includes('9.900'));
    await act(async()=>resolveA(pnl(1100)));assert.equal(branch.value,'B');assert.ok(container.textContent.includes('1.100'));record('report-response-order','Request A arrives after request B under filter B',{selectedBranch:branch.value,expectedRevenue:9900,displayedRevenue:1100});
    getReport=async()=>{throw Error('Fixture unavailable for branch C');};await change(branch,'C');assert.ok(container.textContent.includes('Fixture unavailable'));assert.ok(container.textContent.includes('1.100'));record('report-stale-on-error','Branch C fails after prior report data was loaded',{selectedBranch:branch.value,oldRevenueStillRendered:1100,errorVisible:true});
  }finally{await cleanup();}

  let recordCalls=[],confirmCalls=[],recordImpl=async(id,body)=>({...count(id),items:[{...count(id).items[0],countedQuantity:body.items[0].countedQuantity}]});
  let getCount=async id=>count(id);
  const countSvc={getStockCountById:id=>getCount(id),recordCounts:async(id,body)=>{recordCalls.push(body);return recordImpl(id,body);},confirmStockCount:async(id,body)=>{confirmCalls.push(body);return{...count(id),status:'Confirmed'};},cancelStockCount:async id=>({...count(id),status:'Cancelled'})};
  mock('services/stockCountService.ts',{stockCountService:countSvc});const {StockCountDetailDrawer}=require(path.join(admin,'src/components/stockCounts/StockCountDetailDrawer.tsx'));
  const props=id=>({countId:id,onClose(){},onRefresh(){}});
  await mount(StockCountDetailDrawer,props('count-A'));
  try{await change(container.querySelector('input[type=number]'),'');await click('Chốt kiểm kê');assert.equal(recordCalls.length,0);assert.equal(confirmCalls.length,1);record('count-cleared-value','Saved count=10, expected=8; clear input then accept assigning book stock at confirm',{recordRequests:0,confirmRequests:1,savedCountSentForConfirmation:10,expectedAssignedCount:8});}finally{await cleanup();}
  recordCalls=[];confirmCalls=[];getCount=async id=>count(id,null);
  await mount(StockCountDetailDrawer,props('count-empty'));
  try{assert.equal(container.querySelector('input[type=number]').value,'');await click('Lưu kết quả đếm');assert.equal(recordCalls[0].items[0].countedQuantity,8);record('count-save-blank','Save untouched blank count inputs',{userEnteredCount:false,recordedCount:8,assignmentPromptShown:false});}finally{await cleanup();}
  getCount=async id=>count(id);let releaseSave;recordImpl=()=>new Promise(resolve=>releaseSave=resolve);
  await mount(StockCountDetailDrawer,props('count-A'));
  try{await click('Lưu kết quả đếm');await render(StockCountDetailDrawer,props('count-B'));await act(async()=>releaseSave(count('count-A')));assert.equal(button('Lưu kết quả đếm').disabled,true);record('count-action-lock','Switch document while an old save is in flight',{newDocument:'count-B',saveStillDisabledAfterOldRequestFinished:true});}finally{await cleanup();}

  let releaseDispatch;const transfer=id=>({id,number:id,status:'Draft',fromWarehouseId:'WA',toWarehouseId:'WB',items:[],createdAt:'2026-10-28T12:00:00Z'});
  mock('services/stockTransferService.ts',{stockTransferService:{getTransferById:async id=>transfer(id),dispatchTransfer:()=>new Promise(resolve=>releaseDispatch=resolve)}});
  const {TransferDetailDrawer}=require(path.join(admin,'src/components/transfers/TransferDetailDrawer.tsx'));const trProps=id=>({transferId:id,onClose(){},onRefresh(){}});
  await mount(TransferDetailDrawer,trProps('transfer-A'));
  try{await click('Xuất gửi');await render(TransferDetailDrawer,trProps('transfer-B'));await act(async()=>releaseDispatch());assert.equal(button('Xuất gửi').disabled,true);record('transfer-action-lock','Switch transfer while old dispatch is pending',{newDocument:'transfer-B',dispatchStillDisabledAfterOldRequestFinished:true});}finally{await cleanup();}

  mock('services/posApi.ts',{checkoutPosOrder:async payload=>{posRequests.push(payload);return{success:false,message:'Lost response after commit'};}});const posRequests=[];
  const {PaymentModal}=require(path.join(admin,'src/components/pos/PaymentModal.tsx'));
  await mount(PaymentModal,{embedded:true,isOpen:true,onClose(){},branchId:'A',branchName:'A',tenantName:'Fixture',cashierName:'Fixture',items:[{variantId:'sku-A',sku:'SKU-A',productName:'Fixture',quantity:1,retailPrice:100000}],onSuccessAndPrint(){},onPreviewReceipt(){}});
  try{await click('Thanh toán');await click('Thanh toán');assert.notEqual(posRequests[0].requestKey,posRequests[1].requestKey);record('pos-retry-key','Retry the same cash checkout after an unknown result',{sameKey:false,checkoutCalls:2});}finally{await cleanup();}

  // Typed errors returned by the adapter are not exceptions; the page must render them.
  api.getBranches=async()=>({error:{message:'Fixture branches API failed'}});api.getWarehouses=async()=>({error:{message:'Fixture warehouses API failed'}});
  const {OrganizationPage}=require(path.join(admin,'src/pages/OrganizationPage.tsx'));await mount(OrganizationPage);
  try{assert.ok(container.textContent.includes('Chưa có chi nhánh nào khớp bộ lọc'));assert.ok(!container.textContent.includes('Fixture branches API failed'));record('organization-hidden-error','Initial organization read APIs return errors',{emptyStateShown:true,errorShown:false});}finally{await cleanup();}
  api.getWarehouses=async()=>({data:[{id:'WA',name:'Warehouse A',branchId:'A'},{id:'WB',name:'Warehouse B',branchId:'B'}]});
  api.getSkus=async()=>({data:[{id:'sku-A',code:'SKU-A',name:'Fixture',onHand:50,reserved:0}]});let balanceReads=0;api.getBalances=async()=>{balanceReads++;return{data:[{id:'sku-A',onHand:3,reserved:1}]};};
  const {CreateTransferModal}=require(path.join(admin,'src/components/transfers/CreateTransferModal.tsx'));
  await mount(CreateTransferModal,{isOpen:true,onClose(){},onSuccess(){}});
  try{const selects=container.querySelectorAll('select');await change(selects[0],'WB');const label=selects[2].selectedOptions[0].textContent;assert.ok(label.includes('50'));assert.equal(balanceReads,0);record('transfer-source-stock','Choose source warehouse B; SKU picker still shows tenant total stock',{displayedOnHand:50,warehouseBalanceReads:0,sourceWarehouseStockFixture:3});}finally{await cleanup();}
  const apiEvents=[];const fakeWindow={dispatchEvent:event=>apiEvents.push(event.type)};
  const posApi=loadVm('src/services/posApi.ts',{}, {window:fakeWindow,fetch:async()=>({ok:false,status:401,json:async()=>({detail:'Fixture token expired',errorCode:'UNAUTHORIZED'})})});
  localStorage.setItem('oism_access_token','fixture-expired-token');const denied=await posApi.checkoutPosOrder({});
  assert.equal(apiEvents.length,0);assert.equal(localStorage.getItem('oism_access_token'),'fixture-expired-token');assert.ok(!denied.message.includes('Fixture token expired'));
  record('pos-checkout-401','Checkout returns 401 ProblemDetails',{sessionExpiryEventDispatched:false,expiredTokenRetained:true,backendDetailShown:false});
  const malformedPosApi=loadVm('src/services/posApi.ts',{}, {fetch:async()=>({ok:true,status:200,json:async()=>({unexpected:true})})});
  const malformed=await malformedPosApi.checkoutPosOrder({});assert.equal(malformed.success,true);assert.equal(malformed.orderNumber,undefined);record('pos-malformed-success','HTTP 200 with no server order identity',{reportedSuccess:true,serverOrderNumberPresent:false});
  // Exercise the separate POS App + real PaymentModal. Only catalog entry and API are fixture boundaries.
  const separatePos=path.join(repo,'src/frontend/pos-app/src');
  const posMock=(relative,exports)=>{const file=path.join(separatePos,relative);require.cache[file]={id:file,filename:file,loaded:true,exports};};
  let releaseOldCheckout;
  posMock('services/api.ts',{getStoredUser:()=>({fullName:'Fixture cashier',email:'fixture@example.test',tenantName:'Fixture'}),removeAuthToken(){},fetchBranches:async()=>[{id:'A',name:'Branch A'},{id:'B',name:'Branch B'}],checkoutPosOrder:()=>new Promise(resolve=>releaseOldCheckout=resolve)});
  const product={variantId:'sku-fixture',productId:'p-fixture',sku:'FIXTURE-SKU',productName:'Fixture item',variantName:'M',retailPrice:100000,available:10};
  posMock('components/ProductSearch.tsx',{ProductSearch:({onAddToCart})=>React.createElement('button',{onClick:()=>onAddToCart(product)},'Add fixture item')});
  posMock('components/ReceiptPrintModal.tsx',{ReceiptPrintModal:()=>null});posMock('components/LoginModal.tsx',{LoginModal:()=>null});
  const {App:SeparatePosApp}=require(path.join(separatePos,'App.tsx'));await mount(SeparatePosApp);
  try{await click('Add fixture item');await click('THANH TOÁN');await click('Gửi Checkout đơn hàng');await act(async()=>window.dispatchEvent(new dom.window.KeyboardEvent('keydown',{key:'Escape',bubbles:true})));
    await change(container.querySelector('select'),'B');await click('Add fixture item');assert.ok(container.querySelector('input[type=number]'));
    await act(async()=>releaseOldCheckout({success:true,orderNumber:'OLD-ORDER-A'}));assert.equal(container.querySelector('select').value,'B');assert.equal(container.querySelector('input[type=number]'),null);
    record('separate-pos-old-checkout','Close pending checkout A, switch to B, add a new cart, then receive A success',{activeBranch:'B',oldCheckoutBranch:'A',newCartErased:true});
  }finally{await cleanup();}
  fs.writeFileSync(path.join(__dirname,'probe-results.json'),JSON.stringify({provider:'Current source; actual React/JSDOM components and VM services; controlled HTTP/SignalR/storage. No live writes.',observations:results},null,2));
  console.log(JSON.stringify(results,null,2));
})().catch(error=>{console.error(error);fs.writeFileSync(path.join(__dirname,'probe-results.partial.json'),JSON.stringify({observations:results,error:error.message},null,2));process.exitCode=1;});
