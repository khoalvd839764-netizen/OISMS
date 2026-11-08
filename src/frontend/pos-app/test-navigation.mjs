import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import ts from 'typescript';
function navigation(url) {
  const source=fs.readFileSync(new URL('../admin-dashboard/src/services/posNavigation.ts',import.meta.url),'utf8').replace(/import\.meta/g,'__importMeta');
  const mod={exports:{}},calls=[];
  vm.runInNewContext(ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,
    {module:mod,exports:mod.exports,URL,__importMeta:{env:{VITE_POS_URL:url}},window:{location:{replace:u=>calls.push(u)}}});
  return {...mod.exports,calls};
}
test('navigation: URL local mặc định mở POS riêng ở 5174',async()=>{const n=navigation();await n.openStandalonePos();assert.deepEqual(n.calls,['http://localhost:5174']);});
test('navigation: Cashier phải kết thúc phiên Admin trước khi chuyển',async()=>{const n=navigation('https://pos.fixture.test/');let loggedOut=false;await n.openStandalonePos('Cashier',async()=>{assert.equal(n.calls.length,0);loggedOut=true;});assert.ok(loggedOut);assert.deepEqual(n.calls,['https://pos.fixture.test']);});
test('navigation: Owner giữ phiên quản trị khi mở POS',async()=>{const n=navigation();await n.openStandalonePos('Owner',()=>{throw Error('Must not log out Owner');});assert.equal(n.calls.length,1);});
test('navigation: không chấp nhận URL script',()=>assert.throws(()=>navigation('javascript:alert(1)')));
