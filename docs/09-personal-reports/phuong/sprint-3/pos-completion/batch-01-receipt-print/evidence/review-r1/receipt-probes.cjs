const fs=require('node:fs');const path=require('node:path');
const harness=fs.readFileSync(path.join(__dirname,'harness.cjs'),'utf8');
const scenarios=String.raw`
(async()=>{
 const validReceipt={orderId:'55555555-5555-4555-8555-555555555555',orderNumber:'SERVER-1',receiptNumber:'SERVER-1',branchId:branchA,branchName:'A',branchAddress:null,branchPhone:null,cashierName:'A',createdAt:'2026-10-27T07:00:00Z',paymentMethod:'Cash',totalAmount:100,amountGiven:100,changeDue:0,items:[{skuId:props.items[0].variantId,skuCode:'SKU-A',productName:'Server renamed product',quantity:1,unitPrice:100,subtotal:100}]};
 for(const kind of ['valid-snapshot','empty-item','invalid-field-throws']){
  let printed=[];const calls=[];
  const payload=kind==='empty-item'?{orderNumber:'SERVER-1',receipt:{items:[{}]}}:{orderNumber:'SERVER-1',receipt:{...validReceipt,...(kind==='invalid-field-throws'?{branchAddress:42}:{})}};
  const env=environment(async(url,init)=>{calls.push(JSON.parse(init.body));return response(200,payload);});
  const api=apiFor(env),m=modal(env,api,{...props,currentUser:userA,onSuccessAndPrint:r=>printed.push(r)});
  await checkout(m);
  const afterFirst=api.getPendingCheckout(userA,branchA);
  if(kind==='valid-snapshot')assert.equal(printed[0].items[0].name,'Server renamed product');
  if(kind==='empty-item') {assert.equal(printed.length,1);assert.equal(printed[0].totalAmount,0);assert.equal(afterFirst,null);}
  if(kind==='invalid-field-throws'){assert.equal(printed.length,0);assert.equal(afterFirst,null);await checkout(m);assert.notEqual(calls[0].requestKey,calls[1].requestKey);}
  observations.push({kind,printedCount:printed.length,pendingKeptAfterFirst:Boolean(afterFirst),printedReceipt:printed[0]||null,requestCount:calls.length,newKeyAfterMapperError: calls.length>1?calls[0].requestKey!==calls[1].requestKey:undefined});
 }
 const result={provider:'Actual API/mapper/PaymentModal TS/TSX in Node VM with hook/storage/fetch stubs; no real backend/PostgreSQL',observations};
 fs.writeFileSync(path.join(__dirname,'receipt-results.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
})().catch(e=>{console.error(e);process.exitCode=1});`;
new Function('require','__dirname',harness+scenarios)(require,__dirname);
