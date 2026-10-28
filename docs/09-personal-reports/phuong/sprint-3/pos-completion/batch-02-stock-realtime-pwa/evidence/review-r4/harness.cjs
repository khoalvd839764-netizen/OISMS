const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),assert=require('node:assert/strict');
const root=path.resolve('src/frontend/pos-app'),ts=require(path.join(root,'node_modules/typescript'));
const observations=[];
const branchA='11111111-1111-4111-8111-111111111111',branchB='22222222-2222-4222-8222-222222222222';
const userA={id:'user-a',tenantId:'tenant-a',email:'a@fixture.local',fullName:'A'},userB={id:'user-b',tenantId:'tenant-b',email:'b@fixture.local',fullName:'B'};
const sku={variantId:'33333333-3333-4333-8333-333333333333',sku:'SKU-A',productName:'Product A',barcode:'BAR-A',retailPrice:100,available:10,onHand:10,reserved:0};
const response=x=>({ok:true,status:200,json:async()=>x});
const defer=()=>{let resolve;const promise=new Promise(r=>resolve=r);return{promise,resolve}};
function world(fetcher){
 const local=new Map([['pos_access_token','fixture-A'],['pos_user',JSON.stringify(userA)]]),session=new Map();
 const storage=m=>({getItem:k=>m.get(k)??null,setItem:(k,v)=>m.set(k,String(v)),removeItem:k=>m.delete(k)});
 let now=0,nextId=1;const timers=new Map(),connections=[];let holdStart=false;
 const env={console,Date,Math,Intl,URL,URLSearchParams,AbortController,Promise,Set,Map,performance,fetch:(...args)=>fetcher(...args),localStorage:storage(local),sessionStorage:storage(session),navigator:{onLine:true},alert(){},CustomEvent:class{constructor(type){this.type=type}},setTimeout:(cb,ms=0)=>{const id=nextId++;timers.set(id,{cb,due:now+ms});return id},clearTimeout:id=>timers.delete(id)};
 env.window={localStorage:env.localStorage,sessionStorage:env.sessionStorage,addEventListener(){},removeEventListener(){},dispatchEvent(){},confirm:()=>true};
 class Conn{
  constructor(){this.state='Disconnected';this.handlers={};this.invocations=[];this.startGate=defer();this.stopped=0;connections.push(this)}
  on(n,cb){this.handlers[n]=cb}onreconnecting(cb){this.reconnecting=cb}onreconnected(cb){this.reconnected=cb}onclose(cb){this.closed=cb}
  start(){this.state='Connecting';if(holdStart)return this.startGate.promise.then(()=>{this.state='Connected'});this.state='Connected';return Promise.resolve()}
  stop(){this.stopped++;this.state='Disconnected';this.closed?.();return Promise.resolve()}
  invoke(...args){this.invocations.push(args);return Promise.resolve()}
 }
 class Builder{withUrl(){return this}withAutomaticReconnect(){return this}configureLogging(){return this}build(){return new Conn()}}
 const signal={HubConnectionBuilder:Builder,HubConnectionState:{Connected:'Connected',Connecting:'Connecting',Reconnecting:'Reconnecting',Disconnected:'Disconnected'},HttpTransportType:{WebSockets:1,ServerSentEvents:2,LongPolling:4},LogLevel:{Warning:3}};
 function load(file,imports={}){const source=fs.readFileSync(path.join(root,file),'utf8').replaceAll('import.meta.env.VITE_API_BASE_URL','undefined');const js=ts.transpileModule(source,{fileName:file,compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.ReactJSX,esModuleInterop:true}}).outputText;const mod={exports:{}};vm.runInNewContext(js,{...env,module:mod,exports:mod.exports,require:n=>{if(!(n in imports))throw Error('Unexpected import '+n);return imports[n]}},{filename:file});return mod.exports}
 const api=load('src/services/api.ts');const realtime=load('src/services/realtimeService.ts',{'./api':api,'@microsoft/signalr':signal});realtime.setSoundMuted(true);
 const stock=load('src/services/stockService.ts',{'./api':api,'./realtimeService':realtime});
 function mountSearch(props){let cursor=0,dirty=false;const hooks=[],effects=[];const react={
  useState(initial){const i=cursor++;if(!(i in hooks))hooks[i]=typeof initial==='function'?initial():initial;return[hooks[i],value=>{const next=typeof value==='function'?value(hooks[i]):value;if(!Object.is(next,hooks[i])){hooks[i]=next;dirty=true}}]},
  useRef(initial){const i=cursor++;return hooks[i]??(hooks[i]={current:initial})},
  useTransition(){const i=cursor++;hooks[i]??=(fn=>fn());return[false,hooks[i]]},
  useEffect(cb,deps){const i=cursor++,old=hooks[i];if(!old||deps.some((v,k)=>!Object.is(v,old.deps[k]))){effects.push(()=>{old?.cleanup?.();hooks[i]={deps,cleanup:cb()}})}}};
  const jsx=(type,props)=>({type,props});const module=load('src/components/ProductSearch.tsx',{'react':react,'react/jsx-runtime':{jsx,jsxs:jsx,Fragment:'Fragment'},'lucide-react':{},'../services/api':api,'../services/stockService':stock});
  let tree;const instance={props,render(){cursor=0;dirty=false;tree=module.ProductSearch(props);return tree},settle(){for(let i=0;i<15;i++){instance.render();const run=effects.splice(0);run.forEach(f=>f());if(!dirty&&!effects.length)break}return tree},tree:()=>tree};instance.settle();return instance;
 }
 return{api,realtime,stock,env,connections,load,mountSearch,setHold:v=>holdStart=v,advance(ms){now+=ms;for(const[id,t]of[...timers])if(t.due<=now){timers.delete(id);t.cb()}}};
}
function find(node,pred){if(Array.isArray(node))return node.map(n=>find(n,pred)).find(Boolean)||null;if(!node||typeof node!=='object')return null;if(pred(node))return node;return find(node.props?.children,pred)}
const input=m=>find(m.tree(),n=>n.type==='input');
async function flush(){for(let i=0;i<8;i++)await Promise.resolve()}
module.exports={world,branchA,branchB,userA,userB,sku,response,defer,find,input,flush};
