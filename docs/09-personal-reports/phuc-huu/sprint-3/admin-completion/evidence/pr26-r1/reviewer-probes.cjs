const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),assert=require('node:assert/strict');
const root=path.resolve('tmp/pr26-review/snapshot/OISMS-403431446d213bbbf7f27d01b9622672f2fa4653/src/frontend/admin-dashboard');
const ts=require(path.join(root,'node_modules/typescript'));
const observations=[];let uuid=0;
const memory=new Map([['oism_tenant_id','tenant-A'],['oism_access_token','token-A']]);
const localStorage={getItem:k=>memory.get(k)??null,setItem:(k,v)=>memory.set(k,String(v)),removeItem:k=>memory.delete(k)};
const customEvents=[];
const env={console:{...console,warn(){}},localStorage,window:{dispatchEvent:e=>customEvents.push(e),setTimeout},CustomEvent:class{constructor(type,options){this.type=type;this.detail=options?.detail}},setTimeout,clearTimeout,alert(){},confirm:()=>true};
function load(relative,imports={}){
 const source=fs.readFileSync(path.join(root,relative),'utf8').replaceAll('import.meta.env.VITE_API_BASE_URL','undefined');
 const code=ts.transpileModule(source,{fileName:relative,compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.ReactJSX,esModuleInterop:true}}).outputText;
 const module={exports:{}};vm.runInNewContext(code,{...env,module,exports:module.exports,require:n=>{if(!(n in imports))throw Error('Missing import '+n);return imports[n]}},{filename:relative});return module.exports;
}
const api={generateUuid:()=>`fixture-key-${++uuid}`};
const error=status=>({message:'Fixture rejected',response:{status,data:{detail:'Fixture rejection',errorCode:'INVALID_STATE_TRANSITION'}}});
function hooks(){let cursor=0,dirty=false;const cells=[],effects=[];return{api:{useState(initial){const i=cursor++;if(!(i in cells))cells[i]=typeof initial==='function'?initial():initial;return[cells[i],v=>{const next=typeof v==='function'?v(cells[i]):v;if(!Object.is(cells[i],next)){cells[i]=next;dirty=true}}]},useEffect(cb,deps){const i=cursor++,old=cells[i];if(!old||deps.some((d,k)=>!Object.is(d,old.deps[k]))){effects.push(()=>{old?.cleanup?.();cells[i]={deps,cleanup:cb()}})}}},render(fn){cursor=0;dirty=false;const tree=fn();effects.splice(0).forEach(f=>f());return tree},settle(fn){let tree;for(let i=0;i<15;i++){tree=this.render(fn);if(!dirty)break}return tree}}}
function walk(n,p){if(Array.isArray(n))return n.flatMap(x=>walk(x,p));if(!n||typeof n!=='object')return[];return[...(p(n)?[n]:[]),...walk(n.props?.children,p)]}
const jsx=(type,props)=>({type,props});
async function flush(){for(let i=0;i<12;i++)await Promise.resolve()}
(async()=>{
 const posts=[];const rejected={get:async()=>{throw error(405)},post:async(...args)=>{posts.push(args);throw error(409)}};
 const transfers=load('src/services/stockTransferService.ts',{'./httpClient':{httpClient:rejected},'./apiAdapter':api}).stockTransferService;
 const created=await transfers.createTransfer({fromBranchId:'branch-A',toBranchId:'branch-B',fromWarehouseId:'wh-A',toWarehouseId:'wh-B',items:[{skuId:'sku-A',quantity:2}]});
 assert.equal(created.status,'Draft');await transfers.dispatchTransfer(created.id);assert.equal((await transfers.getTransferById(created.id)).status,'InTransit');await transfers.rejectTransfer(created.id,{reason:'damaged'});assert.equal((await transfers.getTransferById(created.id)).status,'Rejected');
 observations.push({scenario:'Transfer POSTs all reject 409; reads reject 405',createResolved:created.status,dispatchResolved:'InTransit',cancelResolved:'Rejected',fabricatedNumber:created.transferNumber,posts:posts.map(p=>p[0])});
 const realRequests=[];const counts=load('src/services/stockCountService.ts',{'./httpClient':{httpClient:{...rejected,post:async(url,body,config)=>{realRequests.push({url,body,config});return{data:{id:'count-1',status:'Draft',items:[]}}}}},'./apiAdapter':api}).stockCountService;
 await counts.createStockCount({branchId:'branch-A',warehouseId:'wh-A'});await counts.createStockCount({branchId:'branch-A',warehouseId:'wh-A'});
 assert.notEqual(realRequests[0].body.requestKey,realRequests[1].body.requestKey);
 observations.push({scenario:'Create stock count retry without caller requestKey',firstKey:realRequests[0].body.requestKey,retryKey:realRequests[1].body.requestKey,note:'Unknown-result retries with unchanged form get different identity'});
 const connections=[];class Conn{constructor(options){this.options=options;this.state='Disconnected';this.handlers={};this.calls=[];connections.push(this)}on(n,cb){this.handlers[n]=cb}onreconnecting(cb){this.reconnecting=cb}onreconnected(cb){this.reconnected=cb}onclose(cb){this.closed=cb}async start(){this.authenticatedWith=this.options.accessTokenFactory();this.state='Connected'}async stop(){this.state='Disconnected'}async invoke(...a){this.calls.push(a)}}class Builder{withUrl(u,o){this.o=o;return this}withAutomaticReconnect(){return this}configureLogging(){return this}build(){return new Conn(this.o)}}
 const realtime=load('src/services/realtimeAdapter.ts',{'@microsoft/signalr':{HubConnectionBuilder:Builder,HubConnection:Conn,HubConnectionState:{Connected:'Connected',Disconnected:'Disconnected'},LogLevel:{Warning:3}}}).realtimeAdapter;
 await realtime.start();localStorage.removeItem('oism_access_token');localStorage.setItem('oism_tenant_id','tenant-B');localStorage.setItem('oism_access_token','token-B');await realtime.start();const received=[];realtime.subscribe(x=>received.push(x.orderCode));connections[0].handlers.OrderCreated({orderId:'private-A',orderNumber:'PRIVATE-A'});
 assert.equal(connections.length,1);assert.equal(connections[0].authenticatedWith,'token-A');assert.deepEqual(received,['PRIVATE-A']);
 observations.push({scenario:'User A logout/token B login; NotificationCenter start called again without stop',connections:connections.length,authenticatedWith:connections[0].authenticatedWith,currentToken:localStorage.getItem('oism_access_token'),oldOwnerEventDelivered:received,branchInvocations:connections[0].calls});
 const saved={id:'count-1',number:'COUNT-1',status:'Counting',warehouseId:'wh-A',branchId:'branch-A',items:[{id:'line-A',skuId:'sku-A',skuCode:'SKU-A',productName:'A',expectedQuantity:10,countedQuantity:10,balanceSequenceAtSnapshot:1}]};
 const countCalls=[];const h=hooks();const countService={getStockCountById:async()=>saved,recordCounts:async(id,body)=>{countCalls.push({op:'record',body});return saved},confirmStockCount:async(id,body)=>{countCalls.push({op:'confirm',body});return{...saved,status:'Confirmed'}},cancelStockCount:async()=>saved};
 const comp=load('src/components/stockCounts/StockCountDetailDrawer.tsx',{'react':h.api,'react/jsx-runtime':{jsx,jsxs:jsx,Fragment:'Fragment'},'../../services/stockCountService':{stockCountService:countService},'../../types/stockCount':{STOCK_COUNT_STATUS_LABEL:{Counting:'Counting',Confirmed:'Confirmed'},STOCK_COUNT_STATUS_COLOR:{Counting:{},Confirmed:{}}},'lucide-react':{}}).StockCountDetailDrawer;
 const render=()=>comp({countId:'count-1',onClose(){},onRefresh(){}});let tree=h.settle(render);await flush();tree=h.settle(render);
 const quantity=walk(tree,n=>n.type==='input'&&n.props.type==='number')[0];assert.ok(quantity);quantity.props.onChange({target:{value:'7'}});tree=h.settle(render);
 const confirmButton=walk(tree,n=>n.type==='button'&&String(n.props.onClick).includes('handleConfirmCount'))[0];
 const byHandler=walk(tree,n=>n.type==='button'&&n.props.onClick?.name==='handleConfirmCount')[0];assert.ok(byHandler||confirmButton);await(byHandler||confirmButton).props.onClick();
 assert.equal(countCalls.length,1);assert.equal(countCalls[0].op,'confirm');assert.equal(saved.items[0].countedQuantity,10);
 observations.push({scenario:'Persisted count=10; user edits UI to 7 then clicks Confirm',requests:countCalls,serverCountConfirmed:10,uiEditedCount:7,note:'No record request persists current edited quantity before confirm'});
 const reportSource=fs.readFileSync(path.join(root,'src/pages/ReportsPage.tsx'),'utf8');assert.ok(reportSource.includes('`${startDate}T00:00:00Z`'));const selected='2026-10-28';const transmitted=selected+'T00:00:00Z';const localStart=selected+'T00:00:00+07:00';
 observations.push({scenario:'Report calendar date in Vietnam',selectedDate:selected,transmittedStart:transmitted,localMidnightInstant:new Date(localStart).toISOString(),hoursExcludedAtStart:(Date.parse(transmitted)-Date.parse(localStart))/3600000,note:'Boundary comparison, source constructs UTC Z for local calendar date'});
 fs.writeFileSync(path.join(__dirname,'probe-results.json'),JSON.stringify({provider:'Actual snapshot TS/TSX transpiled under Node VM; controlled HTTP/storage/hooks/fake SignalR. No live backend/PostgreSQL.',observations},null,2));console.log(JSON.stringify(observations,null,2));
})().catch(e=>{console.error(e);process.exitCode=1});
