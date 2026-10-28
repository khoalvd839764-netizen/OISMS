// Run from repo root. Actual TS/TSX modules; VM hooks and I/O doubles, no browser/network/DB writes.
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const ts = require(path.resolve('src/frontend/pos-app/node_modules/typescript'));
const root = process.cwd();
const rel = 'src/frontend/pos-app/src/';
const read = p => fs.readFileSync(path.resolve(p), 'utf8');
function compile(p, globals, imports = {}) {
  const source = read(p).replace(/import\.meta/g, '__importMeta');
  const js = ts.transpileModule(source, {fileName:p, compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.ReactJSX}}).outputText;
  const mod = {exports:{}};
  vm.runInNewContext(js, {...globals, module:mod,exports:mod.exports,require:n=>{
    if (n in imports) return imports[n];
    throw new Error('Unexpected import '+n);
  }});
  return mod.exports;
}
const storage = new Map();
const globals = {TextEncoder,Uint8Array,console, __importMeta:{env:{PROD:true,DEV:false}},window:{},localStorage:{getItem:k=>storage.get(k)??null}};
const qr = compile(rel+'services/qrService.ts', globals);
const results = {provider:'Actual TypeScript/TSX in Node VM; React hook/JSX and API/I/O doubles; no browser, network, bank or database calls',head:require('node:child_process').execSync('git rev-parse HEAD',{encoding:'utf8'}).trim(),probes:{}};
const fallback = qr.getPosBankConfig('branch-a');
assert.equal(fallback.accountNumber,'101088889999');
results.probes.productionMissingConfig = {expected:'null and checkout blocked',actual:fallback};
const bankA={bankBin:'970415',bankName:'Fixture A',accountNumber:'123456789',accountName:'TEST A',isTestFixture:true};
const bankB={...bankA,accountNumber:'987654321',accountName:'TEST B'};
storage.set('pos_bank_config',JSON.stringify(bankA));
results.probes.bankContextIgnored={branchA:qr.getPosBankConfig('branch-a'),branchB:qr.getPosBankConfig('branch-b')};
assert.deepEqual(results.probes.bankContextIgnored.branchA,results.probes.bankContextIgnored.branchB);
const owner={userId:'user-a',tenantId:'tenant-a',email:'a@example.test',branchId:'branch-a'};
let pending=null;
let counter=0;
const calls=[];
const user={id:owner.userId,tenantId:owner.tenantId,email:owner.email};
const api={getStoredUser:()=>user,getPendingCheckout:()=>pending,isSameOwner:()=>true,
  generateUuidV4:()=>`${(++counter).toString(16).padStart(8,'0')}-0000-4000-8000-000000000001`,
  savePendingCheckout:r=>{pending=r;},updatePendingCheckoutStatus:s=>{pending.status=s;},clearPendingCheckout:()=>{pending=null;},
  checkoutPosOrder:async p=>{calls.push(JSON.parse(JSON.stringify(p)));return {success:false,isUnknown:true,message:'simulated response loss'};},mapServerReceiptToReceiptData:r=>r};
const states=[],deps=[];let cursor=0, effects=[],dirty=false;
const react={useState:init=>{const i=cursor++;if(!(i in states))states[i]=typeof init==='function'?init():init;return [states[i],v=>{const next=typeof v==='function'?v(states[i]):v;if(!Object.is(next,states[i])){states[i]=next;dirty=true;}}];},
  useEffect:(cb,d)=>{const i=cursor++;if(!deps[i]||d.some((v,j)=>!Object.is(v,deps[i][j]))){deps[i]=d;effects.push(cb);}},useRef:v=>{const i=cursor++;return states[i]??(states[i]={current:v});}};
const jsx=(type,props)=>({type,props});
const {PaymentModal}=compile(rel+'components/PaymentModal.tsx',globals,{react,'react/jsx-runtime':{jsx,jsxs:jsx},'lucide-react':{},'../services/api':api,'../services/qrService':qr});
const props={isOpen:true,onClose:()=>{},branchId:owner.branchId,branchName:'Fixture branch',tenantName:'Fixture tenant',cashierName:'Fixture cashier',currentUser:user,items:[{variantId:'sku-a',sku:'A',productName:'A',retailPrice:100000,quantity:1}],onSuccessAndPrint:()=>{},onPreviewReceipt:()=>{}};
function render(){let tree;for(let n=0;n<10;n++){dirty=false;cursor=0;effects=[];tree=PaymentModal(props);effects.forEach(e=>e());if(!dirty)return tree;}throw Error('render loop');}
function find(node,p){if(Array.isArray(node)){for(const n of node){const v=find(n,p);if(v)return v;}return null;}if(!node||typeof node!=='object')return null;return p(node)?node:find(node.props?.children,p);}
const qrImage=tree=>find(tree,n=>n.type==='img'&&n.props.alt==='VietQR Code').props.src;
async function main(){
  let tree=render();find(tree,n=>n.type==='button'&&String(n.props.onClick).includes("setPaymentMethod('BANK_TRANSFER_QR')")).props.onClick();
  tree=render();const first=qrImage(tree);
  props.isOpen=false;render();props.isOpen=true;tree=render();const reopened=qrImage(tree);
  assert.notEqual(first,reopened);
  results.probes.reopenBeforeSubmit={expected:'same intent keeps QR reference',first,reopened,checkoutCalls:calls.length};
  const before=qrImage(tree);
  await find(tree,n=>n.type==='button'&&String(n.props.onClick).includes('handleCheckout(false)')).props.onClick();
  tree=render();assert.equal(pending.status,'unknown');const key=pending.requestKey;
  storage.set('pos_bank_config',JSON.stringify(bankB));tree=render();const after=qrImage(tree);
  assert.notEqual(before,after);assert.equal(pending.requestKey,key);
  results.probes.pendingBankChanged={expected:'freeze bank snapshot for same unknown attempt',requestKey:key,before,after,pendingHasBankSnapshot:'bankConfig' in pending||'qrSnapshot' in pending};
  // Run the real runner until first login with controlled read-only fake readiness/marker.
  const runnerPath='docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-03-qr-final/e2e/test-runner-e2e-guard.mjs';
  const code=read(runnerPath).replace(/^import .*;\r?\n/gm,'').replace(/import\.meta\.url/g,'__moduleUrl');
  results.probes.guard=[];
  for(const port of ['5434','not-a-port','5433']){
    const logs=[];let reachedLogin=false;let markerCalls=0;
    const env={POSTGRES_TEST_PORT:port,POS_TEST_BACKEND_URL:'http://different-backend.test:5000',POSTGRES_TEST_CONTAINER:'separate-marker-container'};
    const context={fs,path,fileURLToPath:require('node:url').fileURLToPath,__moduleUrl:require('node:url').pathToFileURL(path.resolve(runnerPath)).href,assert,
      console:{log:(...x)=>logs.push(x.join(' ')),error:()=>{}},process:{env,exit:n=>{throw Error('exit '+n);}},
      execSync:()=>{markerCalls++;return '_oisms_test_fixture_marker';},
      fetch:async url=>{if(url.endsWith('/health/ready'))return {json:async()=>({isHealthy:true,currentUser:'oisms_test_app',database:'other_db'})};reachedLogin=true;throw Error('STOP before authentication/mutation');}};
    await vm.runInNewContext('(async()=>{'+code+'})()',context).catch(()=>{});
    assert.equal(reachedLogin,true);
    results.probes.guard.push({port,backend:env.POS_TEST_BACKEND_URL,markerContainer:env.POSTGRES_TEST_CONTAINER,readinessDatabase:'other_db',guardPassed:logs.some(x=>x.includes('Guard PASSED')),reachedLogin,markerCalls});
  }
  results.sourceHashes=Object.fromEntries([rel+'services/qrService.ts',rel+'components/PaymentModal.tsx',runnerPath].map(p=>[p,crypto.createHash('sha256').update(read(p)).digest('hex')]));
  fs.writeFileSync(path.join(__dirname,'probe-results.json'),JSON.stringify(results,null,2)+'\n');
  console.log(JSON.stringify(results,null,2));
}
main().catch(e=>{console.error(e);process.exitCode=1;});
