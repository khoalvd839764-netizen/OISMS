import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
import { ts, APP_URL } from './test-support/app-path.mjs';
import runtime from './test-support/pos-runtime.cjs';
const mod={exports:{}};
const source=fs.readFileSync(new URL('./src/services/receiptMapper.ts',APP_URL),'utf8');
vm.runInNewContext(ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,{module:mod,exports:mod.exports,Date,Math,Number});
const map=mod.exports.mapServerReceipt;
const receipt=()=>({receiptNumber:'INV-1',orderNumber:'POS-1',createdAt:'2026-11-08T06:00:00Z',paymentMethod:'Cash',totalAmount:200,amountGiven:250,changeDue:50,items:[{skuCode:'A',productName:'Server product',unitName:'Cái',quantity:2,unitPrice:100,subtotal:200}]});
test('receipt: chỉ dùng snapshot server, không dựng lại từ giỏ hiện tại',()=>{const out=map(receipt(),'Tenant');assert.equal(out.items[0].name,'Server product');assert.equal(out.items[0].quantity,2);assert.equal(out.totalAmount,200);assert.equal(out.changeDue,50);});
test('receipt: metadata nullable không bịa địa chỉ/điện thoại',()=>{const out=map(receipt(),'Tenant');assert.equal(out.branchPhone,'');assert.equal(out.branchAddress,'');assert.equal(out.customerPhone,undefined);});
test('receipt: không đưa giá vốn/COGS từ response vào dữ liệu in',()=>{const raw=receipt();raw.totalCogs=123;raw.items[0].costPrice=80;const out=map(raw,'Tenant');assert.doesNotMatch(JSON.stringify(out),/costPrice|totalCogs|totalCost/i);});
for(const [name,change] of [
  ['thiếu items',r=>{delete r.items;}],['dòng rỗng',r=>{r.items=[{}];}],['thiếu thời điểm server',r=>{delete r.createdAt;}],
  ['sai số lượng',r=>{r.items[0].quantity=-1;}],['giá không hữu hạn',r=>{r.items[0].unitPrice=Infinity;}],['sai tổng',r=>{r.totalAmount=999;}],
])test('receipt: từ chối '+name,()=>{const raw=receipt();change(raw);assert.throws(()=>map(raw,'Tenant'));});
test('receipt: HTTP 200 nhưng snapshot lỗi giữ pending để đối soát',async()=>{
  const w=runtime.world();w.server.checkout=()=>({ok:true,status:200,json:async()=>({orderNumber:'POS-1',receipt:{items:[{}]}})});
  await w.settle();await w.submit();assert.equal(w.successes.length,0);assert.equal(w.store.getPendingCheckout(w.user,'branch-a')?.status,'unknown');
  const key=w.requests[0].payload.requestKey;await w.submit();assert.equal(w.requests[1].payload.requestKey,key);
});
test('print: modal ancestor không nằm trong selector display:none; page size hợp lệ',()=>{
  const css=fs.readFileSync(new URL('./src/index.css',APP_URL),'utf8');const print=css.slice(css.indexOf('@media print'));
  assert.doesNotMatch(print,/[^{}]*\.no-print-bg[^{}]*\{[^}]*display:\s*none/);
  assert.match(print,/\.no-print-bg\s*\{[^}]*display:\s*block/);assert.doesNotMatch(print,/size:\s*80mm auto/);
  const component=fs.readFileSync(new URL('./src/components/pos/ReceiptPrintModal.tsx',APP_URL),'utf8');assert.match(component,/<option value="58">/);assert.match(component,/<option value="80">/);
});
